namespace Hypernet;

/// <summary>
/// Controls how <c>GetDangerousTextContent</c>-style APIs extract text from HTML subtrees.
/// </summary>
[Flags]
public enum HtmlTextContentOptions
{
	/// <summary>
	/// Uses the default extraction behavior.
	/// Text is collected from ordinary descendant text nodes, HTML entities are decoded,
	/// comments are excluded, non-content text is excluded, and whitespace is preserved.
	/// </summary>
	None = 0,

	/// <summary>
	/// Collapses runs of whitespace into a single space and trims leading and trailing whitespace
	/// in the final extracted text.
	/// </summary>
	NormalizeWhitespace = 1 << 0,

	/// <summary>
	/// Includes comment node payloads in the extracted text.
	/// </summary>
	IncludeComments = 1 << 1,

	/// <summary>
	/// Includes text from elements that commonly do not represent primary page content,
	/// such as <c>script</c>, <c>style</c>, <c>textarea</c>, and <c>template</c>.
	/// </summary>
	IncludeNonContentText = 1 << 2,

	/// <summary>
	/// Preserves unknown or malformed HTML character references as their original source text
	/// instead of omitting them from the extracted content.
	/// </summary>
	KeepUnknownEntities = 1 << 3,
}
