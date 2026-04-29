namespace Hypernet;

/// <summary>
/// Represents a single HTML attribute.
/// </summary>
public readonly ref struct HtmlAttribute(ReadOnlySpan<char> name, ReadOnlySpan<char> value)
{
	/// <summary>
	/// Gets the attribute name.
	/// </summary>
	public ReadOnlySpan<char> Name { get; } = name;

	/// <summary>
	/// Gets the attribute value.
	/// </summary>
	public ReadOnlySpan<char> Value { get; } = value;
}
