using System.Buffers;
using System.Text;

namespace Hypernet;

/// <summary>
/// Configures buffering and encoding for <see cref="HtmlContent" />.
/// </summary>
public sealed class HtmlContentOptions
{
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
}
