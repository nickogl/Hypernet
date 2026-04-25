namespace Hypernet;

/// <summary>
/// Describes the outcome of <see cref="HtmlReader.Read" />.
/// </summary>
public enum HtmlReadResult
{
    /// <summary>
    /// A node was produced and the current reader properties describe that entity.
    /// </summary>
    Node,

    /// <summary>
    /// The document ended naturally.
    /// </summary>
    EndOfDocument,
}
