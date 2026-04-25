using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Create_FromSpan_CopiesInputAndReturnsTextBufferOnDispose()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		var options = new HtmlReaderOptions() { TextBufferPool = textPool };
		using var reader = HtmlReader.Create("<div>Hello</div>", options);

		Assert.Equal("<div>Hello</div>", reader.Data.ToString());
	}

	[Fact]
	public void Create_FromSpan_RespectsMaxBufferSize()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		var reader = HtmlReader.Create("<div>Hello</div>", options);

		Assert.Equal("<div>", reader.Data.ToString());
	}

	[Fact]
	public void Create_FromSequence_SingleSegment_SniffsEncodingAndStripsBom()
	{
		var reader = HtmlReader.Create(new ReadOnlySequence<byte>([0xEF, 0xBB, 0xBF, (byte)'<', (byte)'p', (byte)'>']), _options);

		Assert.Equal("<p>", reader.Data.ToString());
	}

	[Fact]
	public void Create_FromSequence_MultiSegment_DecodesAcrossSegmentBoundaries()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
		};

		var reader = HtmlReader.Create(
			CreateSequence(
				[(byte)'<', (byte)'p', (byte)'>'],
				[0xC3],
				[0xA9, (byte)'<', (byte)'/', (byte)'p', (byte)'>']),
			options);

		Assert.Equal("<p>é</p>", reader.Data.ToString());
	}

	[Fact]
	public void Create_ThrowsWhenInitialBufferSizeExceedsMaxBufferSize()
	{
		var options = new HtmlReaderOptions() { InitialBufferSize = 8, MaxBufferSize = 4 };

		Assert.Throws<ArgumentException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenInitialDepthStackSizeExceedsMaxDepth()
	{
		var options = new HtmlReaderOptions() { InitialDepthStackSize = 3, MaxDepth = 2 };

		Assert.Throws<ArgumentException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenInitialBufferSizeIsNegative()
	{
		var options = new HtmlReaderOptions() { InitialBufferSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenMaxBufferSizeIsNegative()
	{
		var options = new HtmlReaderOptions() { MaxBufferSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenInitialDepthStackSizeIsNegative()
	{
		var options = new HtmlReaderOptions() { InitialDepthStackSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenMaxDepthIsNegative()
	{
		var options = new HtmlReaderOptions() { MaxDepth = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlReader.Create("<div/>", options));
	}

	[Fact]
	public void Create_FromSequence_RespectsMaxBufferSize()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		var reader = HtmlReader.Create(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("<div>Hello</div>")), options);

		Assert.Equal("<div>", reader.Data.ToString());
	}

	[Fact]
	public async Task CreateAsync_StreamConvenienceOverload_CreatesReaderAndReturnsBuffers()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<main>ok</main>"));
		var reader = await HtmlReader.CreateAsync(stream, _options, CancellationToken.None);

		Assert.Equal("<main>ok</main>", reader.Data.ToString());
	}

	[Fact]
	public async Task CreateAsync_Stream_RespectsMaxBufferSize()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<div>Hello</div>"));
		var reader = await HtmlReader.CreateAsync(stream, options, CancellationToken.None);

		Assert.Equal("<div>", reader.Data.ToString());
	}

	[Fact]
	public async Task CreateAsync_StreamCancellationTokenOverload_CreatesReader()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<a/>"));
		var reader = await HtmlReader.CreateAsync(stream, CancellationToken.None);

		Assert.Equal("<a/>", reader.Data.ToString());
	}

	[Fact]
	public async Task CreateAsync_Stream_ThrowsForCanceledToken_AndDoesNotLeakBuffers()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		using var bytePool = new LeakDetectingArrayPool<byte>();
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<cancel/>"));
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		var options = new HtmlReaderOptions()
		{
			TextBufferPool = textPool,
			ByteBufferPool = bytePool,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await HtmlReader.CreateAsync(stream, options, cancellationTokenSource.Token));
	}

	[Fact]
	public async Task CreateAsync_Stream_ReturnsTextAndByteBuffersOnReadFailure()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		using var bytePool = new LeakDetectingArrayPool<byte>();
		using var stream = new ThrowingStream(Encoding.UTF8.GetBytes("<boom>"), throwOnReadNumber: 2);
		var options = new HtmlReaderOptions()
		{
			Encoding = Encoding.UTF8,
			TextBufferPool = textPool,
			ByteBufferPool = bytePool,
		};

		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await HtmlReader.CreateAsync(stream, options, CancellationToken.None));
	}

	[Fact]
	public async Task CreateAsync_PipeReaderConvenienceOverload_CreatesReaderAndReturnsTextBuffer()
	{
		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<pipe/>"));
		await pipe.Writer.CompleteAsync();

		try
		{
			var reader = await HtmlReader.CreateAsync(pipe.Reader, _options, CancellationToken.None);

			Assert.Equal("<pipe/>", reader.Data.ToString());
		}
		finally
		{
			pipe.Reader.Complete();
		}
	}

	[Fact]
	public async Task CreateAsync_PipeReader_RespectsMaxBufferSize()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<div>Hello</div>"));
		await pipe.Writer.CompleteAsync();

		try
		{
			var reader = await HtmlReader.CreateAsync(pipe.Reader, options, CancellationToken.None);

			Assert.Equal("<div>", reader.Data.ToString());
		}
		finally
		{
			pipe.Reader.Complete();
		}
	}

	[Fact]
	public async Task CreateAsync_PipeReaderCancellationTokenOverload_CreatesReader()
	{
		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<x/>"));
		await pipe.Writer.CompleteAsync();

		try
		{
			var reader = await HtmlReader.CreateAsync(pipe.Reader, CancellationToken.None);

			Assert.Equal("<x/>", reader.Data.ToString());
		}
		finally
		{
			pipe.Reader.Complete();
		}
	}

	[Fact]
	public async Task CreateAsync_PipeReader_ThrowsForCanceledToken_AndDoesNotLeakTextBuffer()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		var options = new HtmlReaderOptions() { TextBufferPool = textPool };
		var reader = new CancelingPipeReader();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await HtmlReader.CreateAsync(reader, options, cancellationTokenSource.Token));
	}

	[Fact]
	public async Task CreateAsync_PipeReader_ReturnsTextBufferOnReadFailure()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<boom/>"));
		await pipe.Writer.FlushAsync();
		var reader = new ThrowingPipeReader(pipe.Reader, throwOnReadNumber: 2);
		var options = new HtmlReaderOptions()
		{
			Encoding = Encoding.UTF8,
			TextBufferPool = textPool,
		};

		try
		{
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await HtmlReader.CreateAsync(reader, options, CancellationToken.None));
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
			reader.Complete();
		}
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

	private sealed class ThrowingStream : Stream
	{
		private readonly byte[] _data;
		private readonly int _throwOnReadNumber;
		private int _position;
		private int _readCount;

		public ThrowingStream(byte[] data, int throwOnReadNumber)
		{
			_data = data;
			_throwOnReadNumber = throwOnReadNumber;
		}

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => _data.Length;
		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_readCount++;
			if (_readCount == _throwOnReadNumber)
			{
				throw new InvalidOperationException("Synthetic read failure.");
			}

			var bytesToCopy = Math.Min(buffer.Length, _data.Length - _position);
			if (bytesToCopy <= 0)
			{
				return ValueTask.FromResult(0);
			}

			_data.AsMemory(_position, bytesToCopy).CopyTo(buffer);
			_position += bytesToCopy;
			return ValueTask.FromResult(bytesToCopy);
		}

		public override void Flush()
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class CancelingPipeReader : PipeReader
	{
		public override void AdvanceTo(SequencePosition consumed)
		{
		}

		public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
		{
		}

		public override void CancelPendingRead()
		{
		}

		public override void Complete(Exception? exception = null)
		{
		}

		public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
		{
			return ValueTask.FromCanceled<ReadResult>(cancellationToken);
		}

		public override bool TryRead(out ReadResult result)
		{
			result = default;
			return false;
		}
	}

	private sealed class ThrowingPipeReader : PipeReader
	{
		private readonly PipeReader _inner;
		private readonly int _throwOnReadNumber;
		private int _readCount;

		public ThrowingPipeReader(PipeReader inner, int throwOnReadNumber)
		{
			_inner = inner;
			_throwOnReadNumber = throwOnReadNumber;
		}

		public override void AdvanceTo(SequencePosition consumed)
		{
			_inner.AdvanceTo(consumed);
		}

		public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
		{
			_inner.AdvanceTo(consumed, examined);
		}

		public override void CancelPendingRead()
		{
			_inner.CancelPendingRead();
		}

		public override void Complete(Exception? exception = null)
		{
			_inner.Complete(exception);
		}

		public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
		{
			_readCount++;
			if (_readCount == _throwOnReadNumber)
			{
				throw new InvalidOperationException("Synthetic pipe failure.");
			}

			return _inner.ReadAsync(cancellationToken);
		}

		public override bool TryRead(out ReadResult result)
		{
			return _inner.TryRead(out result);
		}
	}
}
