using System.Buffers;
using System.ComponentModel;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hypernet;

public readonly ref struct HtmlContent : IDisposable
{
	private readonly static HtmlContentOptions _defaultOptions = new();
	private readonly Input _input;
	public readonly Span<char> Span => _input.Buffer.AsSpan(0, _input.Length);
	public readonly bool IsTruncated => _input.IsTruncated;

	internal HtmlContent(Input input)
	{
		_input = input;
	}

	public void Dispose()
	{
		_input.Options.TextBufferPool.Return(_input.Buffer);
	}

	/// <summary>
	/// Creates buffered HTML content over immutable text data.
	/// </summary>
	/// <param name="data">The HTML source to read.</param>
	/// <param name="options">Optional content configuration.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static HtmlContent Create(scoped ReadOnlySpan<char> data, HtmlContentOptions? options = default)
	{
		options ??= _defaultOptions;
		ThrowIfOptionsInvalid(options);

		var length = Math.Min(data.Length, options.MaxBufferSize);
		var buffer = GetBuffer(options, null, usedLength: 0, requiredLength: length);
		data[..length].CopyTo(buffer);

		return new HtmlContent(new Input()
		{
			Buffer = buffer,
			Length = length,
			IsTruncated = data.Length > length,
			Options = options,
		});
	}

	/// <summary>
	/// Creates buffered HTML content over immutable binary input.
	/// </summary>
	/// <param name="data">The HTML source to read.</param>
	/// <param name="options">Optional content configuration.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static HtmlContent Create(ReadOnlySequence<byte> data, HtmlContentOptions? options = default)
	{
		options ??= _defaultOptions;
		ThrowIfOptionsInvalid(options);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, int.MaxValue, nameof(data));

		var encoding = options.Encoding;
		if (encoding is null)
		{
			var sniffer = new EncodingSniffer();
			sniffer.Append(data, isFinalBlock: true, out encoding);
			encoding ??= Encoding.UTF8;
		}

		data = SkipPreamble(data, encoding);

		char[]? buffer = GetBuffer(options, null, usedLength: 0, requiredLength: encoding.GetMaxCharCount((int)data.Length));
		var length = 0;
		var isTruncated = false;
		var decoding = new DecodingState(encoding, encoding.GetDecoder());
		foreach (var segment in data)
		{
			if (!DecodeSpanToBuffer(segment.Span, options, decoding, ref buffer, ref length))
			{
				isTruncated = true;
				break;
			}
		}

		if (!isTruncated)
		{
			FlushDecoder(options, decoding, ref buffer, ref length, ref isTruncated);
		}

		buffer ??= GetBuffer(options, null, usedLength: 0, requiredLength: 0);

		return new HtmlContent(new Input()
		{
			Buffer = buffer,
			Length = length,
			IsTruncated = isTruncated,
			Options = options,
		});
	}

	/// <summary>
	/// Creates buffered HTML content by consuming the provided <see cref="Stream" />.
	/// </summary>
	/// <param name="stream">The stream containing HTML source.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static Awaitable CreateAsync(Stream stream, CancellationToken cancellationToken)
	{
		return CreateAsync(stream, _defaultOptions, cancellationToken);
	}

	/// <summary>
	/// Creates buffered HTML content by consuming the provided <see cref="Stream" />.
	/// </summary>
	/// <param name="stream">The stream containing HTML source.</param>
	/// <param name="options">Optional content configuration.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static Awaitable CreateAsync(Stream stream, HtmlContentOptions options, CancellationToken cancellationToken = default)
	{
		ThrowIfOptionsInvalid(options);

		return new Awaitable(CreateAsyncCore(stream, options, cancellationToken));

		static async ValueTask<Input> CreateAsyncCore(Stream stream, HtmlContentOptions options, CancellationToken cancellationToken)
		{
			var sniffer = new EncodingSniffer();
			var decodingState = DecodingState.FromEncoding(options.Encoding);
			char[]? buffer = null;
			byte[] byteBuffer = options.ByteBufferPool.Rent(Math.Max(options.InitialBufferSize, 1024));
			var bufferedByteCount = 0;
			var length = 0;
			var isTruncated = false;
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
						if (sniffResult == EncodingSniffResult.NeedMoreData)
						{
							continue;
						}

						encoding ??= Encoding.UTF8;
						decodingState = new DecodingState(encoding, encoding.GetDecoder());
						DecodeBufferedBytes(decodingState.Value);
					}
					else if (bytesRead > 0)
					{
						DecodeSpan(source, decodingState.Value);
					}

					if (isCompleted)
					{
						FlushDecoder(options, decodingState.Value, ref buffer, ref length, ref isTruncated);
						buffer ??= GetBuffer(options, null, usedLength: 0, requiredLength: 0);
						return new Input()
						{
							Buffer = buffer,
							Length = length,
							IsTruncated = isTruncated,
							Options = options,
						};
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

				DecodeToBuffer(data, decoding);
				bufferedByteCount = 0;
			}

			void DecodeToBuffer(ReadOnlySequence<byte> data, DecodingState decoding)
			{
				foreach (var segment in data)
				{
					DecodeSpan(segment.Span, decoding);
				}
			}

			void DecodeSpan(ReadOnlySpan<byte> data, DecodingState decoding)
			{
				if (!DecodeSpanToBuffer(data, options, decoding, ref buffer, ref length))
				{
					isTruncated = true;
				}
			}
		}
	}

	/// <summary>
	/// Creates buffered HTML content by consuming the provided <see cref="PipeReader" />.
	/// </summary>
	/// <param name="reader">The pipe reader containing HTML source.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static Awaitable CreateAsync(PipeReader reader, CancellationToken cancellationToken = default)
	{
		return CreateAsync(reader, _defaultOptions, cancellationToken);
	}

	/// <summary>
	/// Creates buffered HTML content by consuming the provided <see cref="PipeReader" />.
	/// </summary>
	/// <param name="reader">The pipe reader containing HTML source.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <param name="cancellationToken">A cancellation token for the read operation.</param>
	/// <returns>HTML content for use in <see cref="HtmlReader"/>.</returns>
	public static Awaitable CreateAsync(PipeReader reader, HtmlContentOptions options, CancellationToken cancellationToken = default)
	{
		ThrowIfOptionsInvalid(options);

		return new Awaitable(CreateAsyncCore(reader, options, cancellationToken));

		static async ValueTask<Input> CreateAsyncCore(PipeReader reader, HtmlContentOptions options, CancellationToken cancellationToken)
		{
			var sniffer = new EncodingSniffer();
			var decodingState = DecodingState.FromEncoding(options.Encoding);
			char[]? buffer = null;
			var length = 0;
			var isTruncated = false;
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
						if (sniffResult == EncodingSniffResult.NeedMoreData)
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

						DecodeToBuffer(source, decodingState.Value);
					}

					if (result.IsCompleted)
					{
						FlushDecoder(options, decodingState.Value, ref buffer, ref length, ref isTruncated);
						reader.AdvanceTo(source.End);
						buffer ??= GetBuffer(options, null, usedLength: 0, requiredLength: 0);

						return new Input()
						{
							Buffer = buffer,
							Length = length,
							IsTruncated = isTruncated,
							Options = options,
						};
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

			void DecodeToBuffer(ReadOnlySequence<byte> data, DecodingState decoding)
			{
				foreach (var segment in data)
				{
					if (!DecodeSpanToBuffer(segment.Span, options, decoding, ref buffer, ref length))
					{
						isTruncated = true;
						return;
					}
				}
			}
		}
	}

	private static bool DecodeSpanToBuffer(
		ReadOnlySpan<byte> data,
		HtmlContentOptions options,
		DecodingState decoding,
		ref char[]? buffer,
		ref int length)
	{
		var logicalRemaining = options.MaxBufferSize - length;
		if (logicalRemaining <= 0)
		{
			return data.IsEmpty;
		}

		buffer = GetBuffer(options, buffer, length, length + decoding.Encoding.GetMaxCharCount(data.Length));
		var writableLength = Math.Min(logicalRemaining, buffer.Length - length);
		decoding.Decoder.Convert(
			data,
			buffer.AsSpan(length, writableLength),
			flush: false,
			out var bytesUsed,
			out var charsUsed,
			out _);
		length += charsUsed;
		return bytesUsed == data.Length;
	}

	private static void FlushDecoder(
		HtmlContentOptions options,
		DecodingState decoding,
		ref char[]? buffer,
		ref int length,
		ref bool isTruncated)
	{
		var logicalRemaining = options.MaxBufferSize - length;
		if (logicalRemaining <= 0)
		{
			Span<char> scratch = stackalloc char[1];
			decoding.Decoder.Convert([], scratch, flush: true, out _, out var charsUsed, out var completed);
			isTruncated |= charsUsed > 0 || !completed;
		}
		else
		{
			buffer = GetBuffer(options, buffer, length, length + 4);
			var writableLength = Math.Min(logicalRemaining, buffer.Length - length);
			decoding.Decoder.Convert([], buffer.AsSpan(length, writableLength), flush: true, out _, out var charsUsed, out var completed);
			length += charsUsed;
			isTruncated |= !completed;
		}
	}

	private static void ThrowIfOptionsInvalid(HtmlContentOptions options)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(options.InitialBufferSize, nameof(HtmlContentOptions.InitialBufferSize));
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxBufferSize, nameof(HtmlContentOptions.MaxBufferSize));
		if (options.InitialBufferSize > options.MaxBufferSize)
		{
			throw new ArgumentException("Initial buffer size must not exceed the maximum buffer size.", nameof(options));
		}
	}

	private static ReadOnlySequence<byte> SkipPreamble(ReadOnlySequence<byte> data, Encoding encoding)
	{
		return encoding.Preamble.IsEmpty || !StartsWith(data, encoding.Preamble)
			? data
			: data.Slice(encoding.Preamble.Length);
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

	private static char[] GetBuffer(HtmlContentOptions options, char[]? buffer, int usedLength, int requiredLength)
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

	private static int GetRentLength(int minimumLength)
	{
		return minimumLength > 0
			? (int)BitOperations.RoundUpToPowerOf2((uint)minimumLength)
			: 1;
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

	internal readonly struct Input
	{
		public char[] Buffer { get; init; }
		public int Length { get; init; }
		public bool IsTruncated { get; init; }
		public HtmlContentOptions Options { get; init; }
	}

	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly struct Awaitable
	{
		private readonly ValueTask<Input> _source;

		internal Awaitable(ValueTask<Input> source)
		{
			_source = source;
		}

		public Awaiter GetAwaiter()
		{
			return new Awaiter(_source.GetAwaiter());
		}

		public readonly struct Awaiter : ICriticalNotifyCompletion
		{
			private readonly ValueTaskAwaiter<Input> _inner;

			internal Awaiter(ValueTaskAwaiter<Input> inner)
			{
				_inner = inner;
			}

			public bool IsCompleted => _inner.IsCompleted;

			public HtmlContent GetResult()
			{
				return new HtmlContent(_inner.GetResult());
			}

			public void OnCompleted(Action continuation)
			{
				_inner.OnCompleted(continuation);
			}

			public void UnsafeOnCompleted(Action continuation)
			{
				_inner.UnsafeOnCompleted(continuation);
			}
		}
	}
}
