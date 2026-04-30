using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hypernet;

/// <summary>
/// Provides a flat, forward-only HTML reader over buffered storage.
/// </summary>
/// <remarks>
/// <para>
/// Do not copy this struct under any circumstances. Always pass it by ref.
/// This is because it contains internal references to its own inline storage.
/// There are diagnostics in debug mode to flag invalid usage, so make sure to
/// meticulously test your reader code.
/// </para>
/// <para>
/// This reader is optimized for high-throughput processing of common HTML fragments.
/// It recognizes ASCII tag names only. Markup with non-ASCII tag names is treated as
/// text rather than as start or end tags.
/// </para>
/// </remarks>
public ref partial struct HtmlReader : IDisposable
{
	private static readonly HtmlReaderOptions _defaultOptions = new();

	private readonly Span<char> _data;
	private readonly HtmlReaderOptions _options;

	private HtmlToken _token;
	private int _position;
	private int _attributeStart;
	private int _attributeEnd;
	private int _depth;
	private Span<char> _currentData;

	// NOTE: 64 items should cover most pages; increase if it turns out not to
	private OpenTagStackInlineStorage _openTagStackInlineStorage;
	private ScratchBuffer<OpenTagStackItem> _openTagStack;
#if DEBUG
	private unsafe OpenTagStackItem* _openTagStackInlineStorageAddress;
#endif

	/// <summary>
	/// Gets a view over the full HTML text.
	/// </summary>
	public readonly ReadOnlySpan<char> Data => _data;

	/// <summary>
	/// Gets the kind of the current token. This is the discriminator for the current reader state.
	/// </summary>
	public readonly HtmlToken Token => _token;

	/// <summary>
	/// Gets the logical depth after the current token has been produced and recovery has been applied.
	/// </summary>
	public readonly int Depth => _depth;

	/// <summary>
	/// Gets the tag name for the current <see cref="HtmlToken.StartTag" /> or <see cref="HtmlToken.EndTag" /> token.
	/// </summary>
	/// <remarks>The returned span remains valid as long as the span initially passed to the reader is valid.</remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is neither <see cref="HtmlToken.StartTag" /> nor <see cref="HtmlToken.EndTag" />.
	/// </exception>
	public readonly ReadOnlySpan<char> TagName
	{
		get
		{
			if (Token != HtmlToken.StartTag && Token != HtmlToken.EndTag)
			{
				ThrowUnexpectedTagNameAccess();
			}

			return _currentData;
		}
	}

	/// <summary>
	/// Gets a zero-allocation enumerable view over the current start tag's attributes.
	/// </summary>
	/// <remarks>
	/// The returned attribute's name and value spans remain valid as long as the span initially
	/// passed to the reader is valid.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public readonly AttributeEnumerable Attributes
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlToken.StartTag);

			return new AttributeEnumerable(_data[_attributeStart.._attributeEnd]);
		}
	}

	/// <summary>
	/// Gets the payload for the current <see cref="HtmlToken.Text" /> token.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This only returns text up to the last tag's closing tag or the next opening tag. To get text for
	/// the whole subtree, use <see cref="TryGetTextContent"/> or <see cref="GetDangerousTextContent"/>.
	/// </para>
	/// <para>
	/// The returned span remains valid as long as the span initially passed to the reader is valid.
	/// </para>
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.Text" />.
	/// </exception>
	public readonly ReadOnlySpan<char> Text
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlToken.Text);

			return _currentData;
		}
	}

	/// <summary>
	/// Gets the payload for the current <see cref="HtmlToken.Comment" /> token.
	/// </summary>
	/// <remarks>The returned span remains valid as long as the span initially passed to the reader is valid.</remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.Comment" />.
	/// </exception>
	public readonly ReadOnlySpan<char> Comment
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlToken.Comment);

			return _currentData;
		}
	}

	/// <summary>
	/// Creates an HTML reader over mutable buffered content.
	/// </summary>
	/// <param name="data">The HTML buffer to read from.</param>
	/// <param name="options">Optional reader configuration.</param>
	/// <remarks>
	/// The reader may rewrite the underlying buffer during some operations, but
	/// this is explicit through appropriately named methods. Returned spans remain
	/// valid only while the underlying buffer remains valid.
	/// </remarks>
	public HtmlReader(Span<char> data, HtmlReaderOptions? options = default)
	{
		options ??= _defaultOptions;
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth, nameof(HtmlReaderOptions.MaxDepth));
		ArgumentOutOfRangeException.ThrowIfNegative(options.InitialTextContentSegmentSize, nameof(HtmlReaderOptions.InitialTextContentSegmentSize));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(options.InitialTextContentSegmentSize, HtmlReaderOptions.MaxInitialTextContentSegmentSize);
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxTextContentSegmentSize, nameof(HtmlReaderOptions.MaxTextContentSegmentSize));

		_data = data;
		_options = options;
	}

	/// <summary>
	/// Releases parser-owned resources and invalidates all previously returned spans.
	/// </summary>
	public void Dispose()
	{
		_openTagStack.Dispose();
	}

	/// <summary>
	/// Exposes mutable access to the current text node when the current token is text.
	/// </summary>
	/// <returns>A mutable span over the current text node.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.Text" />.
	/// </exception>
	public readonly Span<char> GetDangerousMutableTextNode()
	{
		ThrowIfUnexpectedEntity(HtmlToken.Text);

		return _currentData;
	}

	/// <summary>
	/// Exposes mutable access to the current comment when the current token is a comment.
	/// </summary>
	/// <returns>A mutable span over the current comment.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.Comment" />.
	/// </exception>
	public readonly Span<char> GetDangerousMutableComment()
	{
		ThrowIfUnexpectedEntity(HtmlToken.Comment);

		return _currentData;
	}

	private readonly void ThrowIfUnexpectedEntity(HtmlToken expected)
	{
		if (Token != expected)
		{
			ThrowUnexpectedEntity(expected);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private readonly void ThrowUnexpectedEntity(HtmlToken expected)
	{
		throw new InvalidOperationException($"Expected token '{expected}' but current token is '{Token}'");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private readonly void ThrowUnexpectedTagNameAccess()
	{
		throw new InvalidOperationException($"Expected token '{HtmlToken.StartTag}' or '{HtmlToken.EndTag}' but current token is '{Token}'");
	}

	private readonly struct OpenTagStackItem
	{
		public OpenTagStackItem(int nameOffset, int nameLength)
		{
			NameOffset = nameOffset;
			NameLength = nameLength;
		}

		public int NameOffset { get; }
		public int NameLength { get; }
	}

	[InlineArray(64)]
	private struct OpenTagStackInlineStorage
	{
		private OpenTagStackItem _element0;
	}
}
