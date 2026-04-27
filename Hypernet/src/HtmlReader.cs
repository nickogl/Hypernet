using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hypernet;

/// <summary>
/// Provides a flat, forward-only HTML reader over buffered storage.
/// </summary>
public ref partial struct HtmlReader : IDisposable
{
	private readonly HtmlReaderOptions _defaultOptions = new();

	private readonly Span<char> _data;
	private readonly HtmlReaderOptions _options;
	private OpenTagStack _stack;
	private HtmlToken _token;
	private int _position;
	private int _attributeStart;
	private int _attributeEnd;
	private int _depth;
	private Span<char> _currentData;

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
	/// <exception cref="InvalidOperationException">Thrown when the current token is neither <see cref="HtmlToken.StartTag" /> nor <see cref="HtmlToken.EndTag" />.</exception>
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
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.StartTag" />.</exception>
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
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.Text" />.</exception>
	public readonly ReadOnlySpan<char> TextNode
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
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.Comment" />.</exception>
	public readonly ReadOnlySpan<char> Comment
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlToken.Comment);

			return _currentData;
		}
	}

	public HtmlReader(Span<char> data, HtmlReaderOptions? options = default)
	{
		options ??= _defaultOptions;
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDepth, nameof(HtmlReaderOptions.MaxDepth));

		_data = data;
		_options = options;
		_stack = new OpenTagStack(32);
	}

	/// <summary>
	/// Releases parser-owned resources and invalidates all previously returned spans.
	/// </summary>
	public readonly void Dispose()
	{
		_stack.Dispose();
	}

	/// <summary>
	/// Tries to get the value of a named attribute from the current start tag.
	/// </summary>
	/// <param name="name">The attribute name to search for.</param>
	/// <param name="value">Receives the attribute value when found.</param>
	/// <returns><see langword="true" /> when a matching attribute is found; otherwise, <see langword="false" />.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.StartTag" />.</exception>
	public readonly bool TryGetAttribute(ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
	{
		ThrowIfUnexpectedEntity(HtmlToken.StartTag);

		foreach (var attribute in Attributes)
		{
			if (attribute.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				value = attribute.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	/// <summary>
	/// Exposes mutable access to the current text node when the current token is text.
	/// </summary>
	/// <returns>A mutable span over the current text node.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.Text" />.</exception>
	public readonly Span<char> GetDangerousMutableTextNode()
	{
		ThrowIfUnexpectedEntity(HtmlToken.Text);

		return _currentData;
	}

	/// <summary>
	/// Exposes mutable access to the current comment when the current token is a comment.
	/// </summary>
	/// <returns>A mutable span over the current comment.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current token is not <see cref="HtmlToken.Comment" />.</exception>
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

	private static int GetRentLength(int minimumLength)
	{
		return minimumLength > 0
			? (int)BitOperations.RoundUpToPowerOf2((uint)minimumLength)
			: 1;
	}

	/// <summary>
	/// Represents a single attribute exposed by <see cref="Attributes" />.
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

	/// <summary>
	/// Represents a zero-allocation enumerable view over a start tag's attributes.
	/// </summary>
	public readonly ref struct AttributeEnumerable
	{
		private readonly ReadOnlySpan<char> _data;

		internal AttributeEnumerable(ReadOnlySpan<char> data)
		{
			_data = data;
		}

		/// <summary>
		/// Gets an enumerator over the current attribute sequence.
		/// </summary>
		public AttributeEnumerator GetEnumerator()
		{
			return new AttributeEnumerator(_data);
		}
	}

	/// <summary>
	/// Enumerates attributes for the current start tag.
	/// </summary>
	public ref struct AttributeEnumerator
	{
		private readonly ReadOnlySpan<char> _data;
		private int _cursor;
		private HtmlAttribute _current;

		/// <summary>
		/// Gets the current attribute.
		/// </summary>
		public readonly HtmlAttribute Current => _current;

		internal AttributeEnumerator(ReadOnlySpan<char> data)
		{
			_data = data;
			_cursor = 0;
			_current = default;
		}

		/// <summary>
		/// Advances to the next attribute.
		/// </summary>
		/// <returns><see langword="true" /> when an attribute is available; otherwise, <see langword="false" />.</returns>
		public bool MoveNext()
		{
			if (!TryReadAttribute(_data, _cursor, out _cursor, out _current))
			{
				return false;
			}

			return true;
		}
	}
}
