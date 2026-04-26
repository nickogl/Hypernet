using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace Hypernet.Tests;

public sealed class HtmlContentTests : IDisposable
{
	private readonly AutoReleasingArrayPool<char> _textPool;
	private readonly AutoReleasingArrayPool<byte> _bytePool;
	private readonly HtmlContentOptions _contentOptions;

	public HtmlContentTests()
	{
		_textPool = new AutoReleasingArrayPool<char>();
		_bytePool = new AutoReleasingArrayPool<byte>();
		_contentOptions = new HtmlContentOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
		};
	}

	public void Dispose()
	{
		_bytePool.Dispose();
		_textPool.Dispose();
	}

	[Fact]
	public void Create_FromSpan_CopiesInputAndReturnsTextBufferOnDispose()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		var options = new HtmlContentOptions() { TextBufferPool = textPool };
		using var content = HtmlContent.Create("<div>Hello</div>", options);

		Assert.Equal("<div>Hello</div>", content.Span.ToString());
	}

	[Fact]
	public void Create_FromSpan_RespectsMaxBufferSize()
	{
		var options = new HtmlContentOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		var content = HtmlContent.Create("<div>Hello</div>", options);

		Assert.Equal("<div>", content.Span.ToString());
	}

	[Fact]
	public void Create_FromSequence_SingleSegment_SniffsEncodingAndStripsBom()
	{
		var content = HtmlContent.Create(new ReadOnlySequence<byte>([0xEF, 0xBB, 0xBF, (byte)'<', (byte)'p', (byte)'>']));

		Assert.Equal("<p>", content.Span.ToString());
	}

	[Fact]
	public void Create_FromSequence_MultiSegment_DecodesAcrossSegmentBoundaries()
	{
		var options = new HtmlContentOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
		};

		var content = HtmlContent.Create(
			CreateSequence(
				[(byte)'<', (byte)'p', (byte)'>'],
				[0xC3],
				[0xA9, (byte)'<', (byte)'/', (byte)'p', (byte)'>']),
			options);

		Assert.Equal("<p>é</p>", content.Span.ToString());
	}

	[Fact]
	public void Create_ThrowsWhenInitialBufferSizeExceedsMaxBufferSize()
	{
		var options = new HtmlContentOptions() { InitialBufferSize = 8, MaxBufferSize = 4 };

		Assert.Throws<ArgumentException>(() => HtmlContent.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenInitialBufferSizeIsNegative()
	{
		var options = new HtmlContentOptions() { InitialBufferSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlContent.Create("<div/>", options));
	}

	[Fact]
	public void Create_ThrowsWhenMaxBufferSizeIsNegative()
	{
		var options = new HtmlContentOptions() { MaxBufferSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => HtmlContent.Create("<div/>", options));
	}

	[Fact]
	public void Create_FromSequence_RespectsMaxBufferSize()
	{
		var options = new HtmlContentOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		var content = HtmlContent.Create(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("<div>Hello</div>")), options);

		Assert.Equal("<div>", content.Span.ToString());
	}

	[Fact]
	public async Task CreateAsync_StreamConvenienceOverload_CreatesReaderAndReturnsBuffers()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<main>ok</main>"));
		var content = await HtmlContent.CreateAsync(stream, CancellationToken.None);

		Assert.Equal("<main>ok</main>", content.Span.ToString());
	}

	[Fact]
	public async Task CreateAsync_Stream_RespectsMaxBufferSize()
	{
		var options = new HtmlContentOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			Encoding = Encoding.UTF8,
			InitialBufferSize = 4,
			MaxBufferSize = 5,
		};

		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<div>Hello</div>"));
		var content = await HtmlContent.CreateAsync(stream, options, CancellationToken.None);

		Assert.Equal("<div>", content.Span.ToString());
	}

	[Fact]
	public async Task CreateAsync_StreamCancellationTokenOverload_CreatesReader()
	{
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<a/>"));
		var content = await HtmlContent.CreateAsync(stream, CancellationToken.None);

		Assert.Equal("<a/>", content.Span.ToString());
	}

	[Fact]
	public async Task CreateAsync_Stream_ThrowsForCanceledToken_AndDoesNotLeakBuffers()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		using var bytePool = new LeakDetectingArrayPool<byte>();
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<cancel/>"));
		using var cancellationTokenSource = new CancellationTokenSource();
		cancellationTokenSource.Cancel();

		var options = new HtmlContentOptions()
		{
			TextBufferPool = textPool,
			ByteBufferPool = bytePool,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await HtmlContent.CreateAsync(stream, options, cancellationTokenSource.Token));
	}

	[Fact]
	public async Task CreateAsync_Stream_ReturnsTextAndByteBuffersOnReadFailure()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		using var bytePool = new LeakDetectingArrayPool<byte>();
		using var stream = new ThrowingStream(Encoding.UTF8.GetBytes("<boom>"), throwOnReadNumber: 2);
		var options = new HtmlContentOptions()
		{
			Encoding = Encoding.UTF8,
			TextBufferPool = textPool,
			ByteBufferPool = bytePool,
		};

		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			await HtmlContent.CreateAsync(stream, options, CancellationToken.None));
	}

	[Fact]
	public async Task CreateAsync_PipeReaderConvenienceOverload_CreatesReaderAndReturnsTextBuffer()
	{
		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<pipe/>"));
		await pipe.Writer.CompleteAsync();

		try
		{
			var content = await HtmlContent.CreateAsync(pipe.Reader, CancellationToken.None);

			Assert.Equal("<pipe/>", content.Span.ToString());
		}
		finally
		{
			pipe.Reader.Complete();
		}
	}

	[Fact]
	public async Task CreateAsync_PipeReader_RespectsMaxBufferSize()
	{
		var options = new HtmlContentOptions()
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
			var content = await HtmlContent.CreateAsync(pipe.Reader, options, CancellationToken.None);

			Assert.Equal("<div>", content.Span.ToString());
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
			var content = await HtmlContent.CreateAsync(pipe.Reader, CancellationToken.None);

			Assert.Equal("<x/>", content.Span.ToString());
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
		var options = new HtmlContentOptions() { TextBufferPool = textPool };
		var content = new CancelingPipeReader();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
			await HtmlContent.CreateAsync(content, options, cancellationTokenSource.Token));
	}

	[Fact]
	public async Task CreateAsync_PipeReader_ReturnsTextBufferOnReadFailure()
	{
		using var textPool = new LeakDetectingArrayPool<char>();
		var pipe = new Pipe();
		pipe.Writer.Write(Encoding.UTF8.GetBytes("<boom/>"));
		await pipe.Writer.FlushAsync();
		var content = new ThrowingPipeReader(pipe.Reader, throwOnReadNumber: 2);
		var options = new HtmlContentOptions() { Encoding = Encoding.UTF8, TextBufferPool = textPool };

		try
		{
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await HtmlContent.CreateAsync(content, options, CancellationToken.None));
		}
		finally
		{
			await pipe.Writer.CompleteAsync();
			content.Complete();
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
