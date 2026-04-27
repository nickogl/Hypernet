namespace Hypernet;

/// <summary>
/// Identifies the kind of token currently exposed by an <see cref="HtmlReader" />.
/// </summary>
public enum HtmlToken
{
	/// <summary>
	/// A start tag such as <c>&lt;div&gt;</c>.
	/// </summary>
	StartTag,

	/// <summary>
	/// A text node.
	/// </summary>
	Text,

	/// <summary>
	/// An HTML comment.
	/// </summary>
	Comment,

	/// <summary>
	/// An end tag, whether explicit in the source or produced logically by recovery.
	/// </summary>
	EndTag,
}
