namespace Hypernet;

/// <summary>
/// Provides a flat, forward-only HTML reader over buffered storage.
/// </summary>
public ref partial struct HtmlReader : IDisposable
{
	private readonly char[] _buffer;
	private readonly Span<char> _data;
	private readonly HtmlReaderOptions _options;

	internal HtmlReader(Input input)
	{
		_buffer = input.Buffer;
		_data = _buffer.AsSpan(0, input.Length);
		_options = input.Options;
	}

	/// <summary>
	/// Gets a view over the full HTML text.
	/// </summary>
	public readonly ReadOnlySpan<char> Data => _data;

	/// <summary>
	/// Releases parser-owned resources and invalidates all previously returned spans.
	/// </summary>
	public readonly void Dispose()
	{
		_options.TextBufferPool.Return(_buffer);
	}
}
