using System.Buffers;
using System.Text;

namespace Hypernet.Tests;

public sealed class HtmlEncodingSnifferTests
{
	[Fact]
	public void Append_DetectsUtf8BomAcrossSegments()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(CreateSequence(
			[0xEF],
			[0xBB],
			[0xBF, (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l', (byte)'>']),
			isFinalBlock: false,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.Detected, result);
		Assert.Same(Encoding.UTF8, encoding);
	}

	[Fact]
	public void Append_DetectsUtf8BomAcrossMultipleCalls()
	{
		var sniffer = new HtmlEncodingSniffer();

		var first = sniffer.Append(CreateSequence([0xEF]), isFinalBlock: false, out var firstEncoding);
		var second = sniffer.Append(CreateSequence([0xBB]), isFinalBlock: false, out var secondEncoding);
		var third = sniffer.Append(CreateSequence([0xBF, (byte)'<', (byte)'p', (byte)'>']), isFinalBlock: false, out var thirdEncoding);

		Assert.Equal(HtmlEncodingSniffResult.NeedMoreData, first);
		Assert.Null(firstEncoding);
		Assert.Equal(HtmlEncodingSniffResult.NeedMoreData, second);
		Assert.Null(secondEncoding);
		Assert.Equal(HtmlEncodingSniffResult.Detected, third);
		Assert.Same(Encoding.UTF8, thirdEncoding);
	}

	[Fact]
	public void Append_DetectsUtf16BigEndianBom()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(CreateSequence([0xFE, 0xFF, 0x00, (byte)'<']), isFinalBlock: false, out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.Detected, result);
		Assert.Equal(Encoding.BigEndianUnicode.WebName, encoding?.WebName);
	}

	[Fact]
	public void Append_DetectsUtf16LittleEndianBom()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(CreateSequence([0xFF, 0xFE, (byte)'<', 0x00]), isFinalBlock: false, out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.Detected, result);
		Assert.Equal(Encoding.Unicode.WebName, encoding?.WebName);
	}

	[Fact]
	public void Append_ReturnsNeedMoreData_ForPotentialBomPrefix()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(CreateSequence([0xEF]), isFinalBlock: false, out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.NeedMoreData, result);
		Assert.Null(encoding);
	}

	[Fact]
	public void Append_ReturnsNeedMoreData_ForPotentialUtf8BomPrefixAfterTwoBytes()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(CreateSequence([0xEF, 0xBB]), isFinalBlock: false, out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.NeedMoreData, result);
		Assert.Null(encoding);
	}

	[Fact]
	public void Append_DetectsQuotedCharsetCaseInsensitively()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(
			CreateSequence(
				Encoding.ASCII.GetBytes("<meta http-equiv=\"Content-Type\" content=\"text/html; CHARSET=\""),
				Encoding.ASCII.GetBytes("IsO-8859-1\">")),
			isFinalBlock: true,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.Detected, result);
		Assert.Equal("windows-1252", encoding?.WebName);
	}

	[Fact]
	public void Append_DetectsUnquotedCharsetUntilSemicolon()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(
			CreateSequence(Encoding.ASCII.GetBytes("<meta charset=utf-8; data-x=\"1\">")),
			isFinalBlock: true,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.Detected, result);
		Assert.Equal("utf-8", encoding?.WebName);
	}

	[Fact]
	public void Append_ReturnsUseFallback_WhenCharsetLabelIsUnknown()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(
			CreateSequence(Encoding.ASCII.GetBytes("<meta charset=\"totally-made-up\">")),
			isFinalBlock: true,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.UseFallback, result);
		Assert.Null(encoding);
	}

	[Fact]
	public void Append_ReturnsUseFallback_WhenCharsetValueIsEmpty()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(
			CreateSequence(Encoding.ASCII.GetBytes("<meta charset=>")),
			isFinalBlock: true,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.UseFallback, result);
		Assert.Null(encoding);
	}

	[Fact]
	public void Append_ReturnsUseFallback_WhenFinalBlockHasNoEncodingHint()
	{
		var sniffer = new HtmlEncodingSniffer();

		var result = sniffer.Append(
			CreateSequence(Encoding.ASCII.GetBytes("<html><body>Hello</body></html>")),
			isFinalBlock: true,
			out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.UseFallback, result);
		Assert.Null(encoding);
	}

	[Fact]
	public void Append_ReturnsUseFallback_WhenPrescanLimitReachedWithoutHint()
	{
		var sniffer = new HtmlEncodingSniffer();
		var data = Encoding.ASCII.GetBytes(new string('a', 1100));

		var result = sniffer.Append(CreateSequence(data), isFinalBlock: false, out var encoding);

		Assert.Equal(HtmlEncodingSniffResult.UseFallback, result);
		Assert.Null(encoding);
	}

	private static ReadOnlySequence<byte> CreateSequence(params byte[][] segments)
	{
		ArgumentOutOfRangeException.ThrowIfZero(segments.Length);

		if (segments.Length == 1)
		{
			return new ReadOnlySequence<byte>(segments[0]);
		}

		BufferSegment? first = null;
		BufferSegment? last = null;
		foreach (var segment in segments)
		{
			var current = new BufferSegment(segment);
			if (first is null)
			{
				first = current;
			}
			else
			{
				last!.SetNext(current);
			}

			last = current;
		}

		return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
	}

	private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
	{
		public BufferSegment(byte[] data)
		{
			Memory = data;
		}

		public void SetNext(BufferSegment next)
		{
			next.RunningIndex = RunningIndex + Memory.Length;
			Next = next;
		}
	}
}
