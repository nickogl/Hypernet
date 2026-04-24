using System.Buffers;
using System.Text;

namespace Hypernet;

/// <summary>
/// Configures buffering, encoding, and recovery limits for <see cref="HtmlReader" />.
/// </summary>
public sealed class HtmlReaderOptions
{
	internal bool _validated;

	/// <summary>
	/// Caller-specified encoding. When provided, it overrides document-declared charset information.
	/// </summary>
	public Encoding? Encoding { get; init; }

	/// <summary>
	/// Array pool to use for text data.
	/// </summary>
	public ArrayPool<char> TextBufferPool { get; init; } = ArrayPool<char>.Shared;

	/// <summary>
	/// Array pool to use for byte data.
	/// </summary>
	public ArrayPool<byte> ByteBufferPool { get; init; } = ArrayPool<byte>.Shared;

	/// <summary>
	/// Initial buffer size. The default is <c>16_384</c>.
	/// </summary>
	public int InitialBufferSize { get; init; } = 16_384;

	/// <summary>
	/// Maximum buffer size. The default is <c>1_048_576</c>.
	/// </summary>
	public int MaxBufferSize { get; init; } = 1_048_576;

	/// <summary>
	/// Initial open-element stack capacity. The default is <c>32</c>.
	/// </summary>
	public int InitialDepthStackSize { get; init; } = 32;

	/// <summary>
	/// Maximum logical depth supported by recovery. The default is <c>256</c>.
	/// </summary>
	public int MaxDepth { get; init; } = 256;
}
