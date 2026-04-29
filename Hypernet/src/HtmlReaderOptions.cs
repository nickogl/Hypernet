namespace Hypernet;

/// <summary>
/// Configures buffering, encoding, and recovery limits for <see cref="HtmlReader" />.
/// </summary>
public sealed class HtmlReaderOptions
{
	// About 8 KiB of stack memory, which is acceptable
	internal const int MaxInitialTextContentSegmentSize = 1024;

	/// <summary>
	/// Maximum logical depth supported by recovery. The default is <c>256</c>.
	/// </summary>
	public int MaxDepth { get; init; } = 256;

	/// <summary>
	/// Initial stack-allocated segment size when using <see cref="HtmlReader.GetDangerousTextContent"/>.
	/// If this size is exceeded, it will switch to memory pooling, so choose whichever
	/// satisfies the majority of your inputs (within reason). The default is <c>64</c>.
	/// </summary>
	public int InitialTextContentSegmentSize { get; init; } = 64;

	/// <summary>
	/// Maximum segment size supported when using <see cref="HtmlReader.GetDangerousTextContent"/>.
	/// The default is <c>1024</c>.
	/// </summary>
	public int MaxTextContentSegmentSize { get; init; } = 1024;
}
