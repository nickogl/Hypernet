using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hypernet;

internal struct EncodingSniffer
{
	private const int PrescanByteLimit = 1024;
	private const string CharsetPrefix = "charset=";

	private PrescanBuffer _prescanBuffer;
	private int _prescanLength;
	private byte _firstByte;
	private byte _secondByte;
	private byte _thirdByte;
	private int _bomLength;

	public EncodingSniffResult Append(ReadOnlySequence<byte> data, bool isFinalBlock, out Encoding? encoding)
	{
		foreach (var segment in data)
		{
			Append(segment.Span);
			if (_prescanLength >= PrescanByteLimit && !NeedsMoreBomBytes())
			{
				break;
			}
		}

		if (TryDetectBomEncoding(out encoding) || TryDetectCharsetEncoding(out encoding))
		{
			return EncodingSniffResult.Detected;
		}

		if (!isFinalBlock && _prescanLength < PrescanByteLimit)
		{
			return EncodingSniffResult.NeedMoreData;
		}

		encoding = null;
		return EncodingSniffResult.UseFallback;
	}

	private void Append(ReadOnlySpan<byte> data)
	{
		// Keep a lowercase ASCII prescan for charset detection, but preserve the
		// first three raw bytes separately so BOM detection can still see them.
		for (var i = 0; i < data.Length; i++)
		{
			var value = data[i];
			if (_bomLength < 3)
			{
				switch (_bomLength)
				{
					case 0:
						_firstByte = value;
						break;
					case 1:
						_secondByte = value;
						break;
					case 2:
						_thirdByte = value;
						break;
				}

				_bomLength++;
			}

			if (_prescanLength >= PrescanByteLimit)
			{
				break;
			}
			_prescanBuffer[_prescanLength++] = ToLowerAscii(value);
		}
	}

	private readonly bool TryDetectBomEncoding(out Encoding? encoding)
	{
		if (_bomLength >= 2)
		{
			if (_firstByte == 0xFE && _secondByte == 0xFF)
			{
				encoding = Encoding.BigEndianUnicode;
				return true;
			}

			if (_firstByte == 0xFF && _secondByte == 0xFE)
			{
				encoding = Encoding.Unicode;
				return true;
			}
		}

		if (_bomLength >= 3 &&
			_firstByte == 0xEF &&
			_secondByte == 0xBB &&
			_thirdByte == 0xBF)
		{
			encoding = Encoding.UTF8;
			return true;
		}

		encoding = null;
		return false;
	}

	private readonly bool TryDetectCharsetEncoding(out Encoding? encoding)
	{
		ReadOnlySpan<char> ascii = _prescanBuffer;
		ascii = ascii[.._prescanLength];

		var charsetIndex = ascii.IndexOf(CharsetPrefix, StringComparison.Ordinal);
		if (charsetIndex < 0
			|| !TryGetCharsetValue(ascii, charsetIndex + CharsetPrefix.Length, out var charset))
		{
			encoding = null;
			return false;
		}

		return Charset.TryGetEncoding(charset, out encoding);
	}

	private readonly bool NeedsMoreBomBytes()
	{
		return _bomLength switch
		{
			0 => false,
			1 => _firstByte is 0xEF or 0xFE or 0xFF,
			2 => _firstByte == 0xEF && _secondByte == 0xBB,
			_ => false,
		};
	}

	private static char ToLowerAscii(byte value)
	{
		return value is >= (byte)'A' and <= (byte)'Z'
			? (char)(value + 32)
			: (char)value;
	}

	private static bool TryGetCharsetValue(ReadOnlySpan<char> ascii, int startIndex, out ReadOnlySpan<char> charset)
	{
		charset = default;

		if ((uint)startIndex >= (uint)ascii.Length)
		{
			return false;
		}

		var quote = ascii[startIndex];
		if (quote is '"' or '\'')
		{
			startIndex++;
		}
		else
		{
			quote = '\0';
		}

		var endIndex = startIndex;
		while ((uint)endIndex < (uint)ascii.Length)
		{
			var current = ascii[endIndex];
			if ((quote != '\0' && current == quote) ||
				(quote == '\0' && (char.IsWhiteSpace(current) || current == ';' || current == '>')))
			{
				break;
			}

			endIndex++;
		}

		if (endIndex <= startIndex)
		{
			return false;
		}

		charset = ascii[startIndex..endIndex];
		return true;
	}

	[InlineArray(PrescanByteLimit)]
	private struct PrescanBuffer
	{
		private char _element0;
	}
}
