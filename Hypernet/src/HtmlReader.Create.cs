using System.Buffers;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private readonly static HtmlReaderOptions _defaultOptions = new() { _validated = true };

	/// <summary>
	/// Creates a buffered reader over immutable text data.
	/// </summary>
	/// <param name="data">The HTML source to read.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static HtmlReader Create(ReadOnlySpan<char> data, HtmlReaderOptions? options = default)
	{
		options ??= _defaultOptions;
		ValidateOptions(options);

		var length = Math.Min(data.Length, options.MaxBufferSize);
		var buffer = GetBuffer(options, null, usedLength: 0, requiredLength: length);
		data[..length].CopyTo(buffer);

		var input = new Input() { Buffer = buffer, Length = length, Options = options };
		return new HtmlReader(input);
	}

	/// <summary>
	/// Creates a buffered reader over immutable binary input.
	/// </summary>
	/// <param name="data">The HTML source to read.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static HtmlReader Create(ReadOnlySequence<byte> data, HtmlReaderOptions? options = default)
	{
		options ??= _defaultOptions;
		ValidateOptions(options);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, int.MaxValue, nameof(data));

		var encoding = options.Encoding;
		if (encoding is null)
		{
			var sniffer = new HtmlEncodingSniffer();
			sniffer.Append(data, isFinalBlock: true, out encoding);
			encoding ??= Encoding.UTF8;
		}

		data = SkipPreamble(data, encoding);
		var buffer = GetBuffer(options, null, usedLength: 0, requiredLength: encoding.GetMaxCharCount((int)data.Length));
		var length = 0;
		var decoder = encoding.GetDecoder();
		foreach (var segment in data)
		{
			var writableLength = Math.Min(options.MaxBufferSize - length, buffer.Length - length);
			if (writableLength <= 0)
			{
				break;
			}

			decoder.Convert(segment.Span, buffer.AsSpan(length, writableLength), flush: false, out _, out var charsUsed, out _);
			length += charsUsed;
		}

		var flushWritableLength = Math.Min(options.MaxBufferSize - length, buffer.Length - length);
		if (flushWritableLength > 0)
		{
			decoder.Convert([], buffer.AsSpan(length, flushWritableLength), flush: true, out _, out var charsUsed, out _);
			length += charsUsed;
		}

		return new HtmlReader(new Input() { Buffer = buffer, Length = length, Options = options });
	}

	/// <summary>
	/// Creates a buffered reader by consuming the provided <see cref="Stream" />.
	/// </summary>
	/// <param name="stream">The stream containing HTML source.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static Awaitable CreateAsync(Stream stream, CancellationToken cancellationToken)
	{
		return CreateAsync(stream, options: null, cancellationToken);
	}

	/// <summary>
	/// Creates a buffered reader by consuming the provided <see cref="Stream" />.
	/// </summary>
	/// <param name="stream">The stream containing HTML source.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static Awaitable CreateAsync(Stream stream, HtmlReaderOptions? options = default, CancellationToken cancellationToken = default)
	{
		options ??= _defaultOptions;
		ValidateOptions(options);

		return new Awaitable(CreateAsyncCore(stream, options, cancellationToken));

		static async ValueTask<Input> CreateAsyncCore(Stream stream, HtmlReaderOptions options, CancellationToken cancellationToken)
		{
			var sniffer = new HtmlEncodingSniffer();
			var decodingState = DecodingState.FromEncoding(options.Encoding);
			char[]? buffer = null;
			byte[] byteBuffer = options.ByteBufferPool.Rent(Math.Max(options.InitialBufferSize, 1024));
			var bufferedByteCount = 0;
			var length = 0;
			var skipPreamble = true;
			try
			{
				while (true)
				{
					if (decodingState is null && bufferedByteCount == byteBuffer.Length)
					{
						var newBuffer = options.ByteBufferPool.Rent(GetRentLength(byteBuffer.Length * 2));
						byteBuffer.AsSpan(0, bufferedByteCount).CopyTo(newBuffer);
						options.ByteBufferPool.Return(byteBuffer);
						byteBuffer = newBuffer;
					}

					var destination = decodingState is null
						? byteBuffer.AsMemory(bufferedByteCount)
						: byteBuffer.AsMemory();
					var bytesRead = await stream.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
					var isCompleted = bytesRead == 0;
					var source = decodingState is null
						? byteBuffer.AsSpan(bufferedByteCount, bytesRead)
						: byteBuffer.AsSpan(0, bytesRead);

					if (decodingState is null)
					{
						bufferedByteCount += bytesRead;
						var sniffResult = sniffer.Append(new ReadOnlySequence<byte>(byteBuffer, 0, bufferedByteCount), isCompleted, out var encoding);
						if (sniffResult == HtmlEncodingSniffResult.NeedMoreData)
						{
							continue;
						}

						encoding ??= Encoding.UTF8;
						decodingState = new DecodingState(encoding, encoding.GetDecoder());
						DecodeBufferedBytes(decodingState.Value);
					}
					else if (bytesRead > 0)
					{
						DecodeSpanToBuffer(source, decodingState.Value, flush: false);
					}

					if (isCompleted)
					{
						DecodeSpanToBuffer([], decodingState.Value, flush: true);
						buffer ??= GetBuffer(options, null, usedLength: 0, requiredLength: 0);
						return new Input() { Buffer = buffer, Length = length, Options = options };
					}
				}
			}
			catch (Exception)
			{
				if (buffer is not null)
				{
					options.TextBufferPool.Return(buffer);
				}

				throw;
			}
			finally
			{
				options.ByteBufferPool.Return(byteBuffer);
			}

			void DecodeBufferedBytes(DecodingState decoding)
			{
				if (bufferedByteCount == 0)
				{
					return;
				}

				var data = new ReadOnlySequence<byte>(byteBuffer, 0, bufferedByteCount);
				if (skipPreamble)
				{
					data = SkipPreamble(data, decoding.Encoding);
					skipPreamble = false;
				}

				DecodeToBuffer(data, decoding, flush: false);
				bufferedByteCount = 0;
			}

			void DecodeToBuffer(ReadOnlySequence<byte> data, DecodingState decoding, bool flush)
			{
				foreach (var segment in data)
				{
					DecodeSpanToBuffer(segment.Span, decoding, flush: false);
				}

				if (flush)
				{
					DecodeSpanToBuffer([], decoding, flush: true);
				}
			}

			void DecodeSpanToBuffer(ReadOnlySpan<byte> data, DecodingState decoding, bool flush)
			{
				var logicalRemaining = options.MaxBufferSize - length;
				if (logicalRemaining <= 0)
				{
					return;
				}

				buffer = GetBuffer(options, buffer, length, length + decoding.Encoding.GetMaxCharCount(data.Length));
				var writableLength = Math.Min(logicalRemaining, buffer.Length - length);
				decoding.Decoder.Convert(data, buffer.AsSpan(length, writableLength), flush, out _, out var charsUsed, out _);
				length += charsUsed;
			}
		}
	}

	/// <summary>
	/// Creates a buffered reader by consuming the provided <see cref="PipeReader" />.
	/// </summary>
	/// <param name="reader">The pipe reader containing HTML source.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static Awaitable CreateAsync(PipeReader reader, CancellationToken cancellationToken)
	{
		return CreateAsync(reader, options: null, cancellationToken);
	}

	/// <summary>
	/// Creates a buffered reader by consuming the provided <see cref="PipeReader" />.
	/// </summary>
	/// <param name="reader">The pipe reader containing HTML source.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>A reader positioned before the first entity.</returns>
	public static Awaitable CreateAsync(PipeReader reader, HtmlReaderOptions? options = default, CancellationToken cancellationToken = default)
	{
		options ??= _defaultOptions;
		ValidateOptions(options);

		return new Awaitable(CreateAsyncCore(reader, options, cancellationToken));

		static async ValueTask<Input> CreateAsyncCore(PipeReader reader, HtmlReaderOptions options, CancellationToken cancellationToken)
		{
			var sniffer = new HtmlEncodingSniffer();
			var decodingState = DecodingState.FromEncoding(options.Encoding);
			char[]? buffer = null;
			var length = 0;
			long sniffedLength = 0;
			var skipPreamble = true;
			try
			{
				while (true)
				{
					var result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
					var source = result.Buffer;

					if (decodingState is null)
					{
						var sniffResult = sniffer.Append(source.Slice(sniffedLength), result.IsCompleted, out var encoding);
						sniffedLength = source.Length;
						if (sniffResult == HtmlEncodingSniffResult.NeedMoreData)
						{
							reader.AdvanceTo(source.Start, source.End);
							continue;
						}

						encoding ??= Encoding.UTF8;
						decodingState = new DecodingState(encoding, encoding.GetDecoder());
					}

					if (!source.IsEmpty)
					{
						if (skipPreamble)
						{
							source = SkipPreamble(source, decodingState.Value.Encoding);
							skipPreamble = false;
						}

						DecodeToBuffer(source, decodingState.Value, flush: false);
					}

					if (result.IsCompleted)
					{
						DecodeToBuffer(ReadOnlySequence<byte>.Empty, decodingState.Value, flush: true);
						reader.AdvanceTo(source.End);
						buffer ??= GetBuffer(options, null, usedLength: 0, requiredLength: 0);
						return new Input() { Buffer = buffer, Length = length, Options = options };
					}

					reader.AdvanceTo(source.End);
				}
			}
			catch (Exception)
			{
				if (buffer is not null)
				{
					options.TextBufferPool.Return(buffer);
				}
				throw;
			}

			void DecodeToBuffer(ReadOnlySequence<byte> data, DecodingState decoding, bool flush)
			{
				foreach (var segment in data)
				{
					var logicalRemaining = options.MaxBufferSize - length;
					if (logicalRemaining <= 0)
					{
						return;
					}

					buffer = GetBuffer(options, buffer, length, length + decoding.Encoding.GetMaxCharCount(segment.Length));
					var writableLength = Math.Min(logicalRemaining, buffer.Length - length);
					decoding.Decoder.Convert(segment.Span, buffer.AsSpan(length, writableLength), flush: false, out _, out var charsUsed, out _);
					length += charsUsed;
				}

				if (flush)
				{
					var logicalRemaining = options.MaxBufferSize - length;
					if (logicalRemaining <= 0)
					{
						return;
					}

					buffer = GetBuffer(options, buffer, length, length + 4);
					var writableLength = Math.Min(logicalRemaining, buffer.Length - length);
					decoding.Decoder.Convert([], buffer.AsSpan(length, writableLength), flush: true, out _, out var charsUsed, out _);
					length += charsUsed;
				}
			}
		}
	}

	private static void ValidateOptions(HtmlReaderOptions options)
	{
		if (!options._validated)
		{
			ThrowIfOptionsInvalid(options);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowIfOptionsInvalid(HtmlReaderOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(options.InitialBufferSize, nameof(HtmlReaderOptions.InitialBufferSize));
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxBufferSize, nameof(HtmlReaderOptions.MaxBufferSize));
		ArgumentOutOfRangeException.ThrowIfNegative(options.InitialDepthStackSize, nameof(HtmlReaderOptions.InitialDepthStackSize));
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth, nameof(HtmlReaderOptions.MaxDepth));
		if (options.InitialBufferSize > options.MaxBufferSize)
		{
			throw new ArgumentException("Initial buffer size must not exceed the maximum buffer size.", nameof(options));
		}

		if (options.InitialDepthStackSize > options.MaxDepth)
		{
			throw new ArgumentException("Initial depth stack size must not exceed the maximum depth.", nameof(options));
		}

		options._validated = true;
	}

	private static ReadOnlySequence<byte> SkipPreamble(ReadOnlySequence<byte> data, Encoding encoding)
	{
		ReadOnlySpan<byte> preamble = encoding.Preamble;
		if (preamble.IsEmpty || !StartsWith(data, preamble))
		{
			return data;
		}

		return data.Slice(preamble.Length);
	}

	private static bool StartsWith(ReadOnlySequence<byte> data, ReadOnlySpan<byte> prefix)
	{
		if (data.Length < prefix.Length)
		{
			return false;
		}

		var reader = new SequenceReader<byte>(data);
		for (var i = 0; i < prefix.Length; i++)
		{
			if (!reader.TryRead(out var value) || value != prefix[i])
			{
				return false;
			}
		}

		return true;
	}

	private static char[] GetBuffer(HtmlReaderOptions options, char[]? buffer, int usedLength, int requiredLength)
	{
		if (buffer is not null && requiredLength <= buffer.Length)
		{
			return buffer;
		}

		var cappedRequiredLength = Math.Min(requiredLength, options.MaxBufferSize);
		var maxRequestedLength = Math.Max(buffer is null ? options.InitialBufferSize : buffer.Length * 2, cappedRequiredLength);
		var minimumLength = Math.Min(maxRequestedLength, options.MaxBufferSize);
		var newBuffer = options.TextBufferPool.Rent(GetRentLength(minimumLength));
		if (buffer is not null)
		{
			buffer.AsSpan(0, usedLength).CopyTo(newBuffer);
			options.TextBufferPool.Return(buffer);
		}
		return newBuffer;
	}

	private readonly record struct DecodingState(Encoding Encoding, Decoder Decoder)
	{
		public static DecodingState? FromEncoding(Encoding? encoding)
		{
			return encoding is not null
				? new DecodingState(encoding, encoding.GetDecoder())
				: null;
		}
	}
}
