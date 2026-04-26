using System.Buffers;
using System.Text;

namespace Hypernet;

/// <summary>
/// Configures buffering, encoding, and recovery limits for <see cref="HtmlReader" />.
/// </summary>
public sealed class HtmlReaderOptions
{
	/// <summary>
	/// Maximum logical depth supported by recovery. The default is <c>256</c>.
	/// </summary>
	public int MaxDepth { get; init; } = 256;
}
