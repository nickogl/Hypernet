using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hypernet;

/// <summary>
/// Provides a flat, forward-only HTML reader over buffered storage.
/// </summary>
public ref partial struct HtmlReader : IDisposable
{
	private readonly char[] _buffer;
	private readonly Span<char> _data;
	private readonly HtmlReaderOptions _options;
	private StackItem[] _stackBuffer;
	private HtmlEntityKind _kind;
	private int _position;
	private int _attributeStart;
	private int _attributeEnd;
	private int _stackCount;
	private int _depth;
	private Span<char> _currentData;

	/// <summary>
	/// Gets a view over the full HTML text.
	/// </summary>
	public readonly ReadOnlySpan<char> Data => _data;

	/// <summary>
	/// Gets the kind of the current entity. This is the discriminator for the current reader state.
	/// </summary>
	public readonly HtmlEntityKind Kind => _kind;

	/// <summary>
	/// Gets the logical depth after the current entity has been produced and recovery has been applied.
	/// </summary>
	public readonly int Depth => _depth;

	/// <summary>
	/// Gets the tag name for the current <see cref="HtmlEntityKind.StartTag" /> or <see cref="HtmlEntityKind.EndTag" /> entity.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is neither <see cref="HtmlEntityKind.StartTag" /> nor <see cref="HtmlEntityKind.EndTag" />.</exception>
	public readonly ReadOnlySpan<char> TagName
	{
		get
		{
			if (Kind != HtmlEntityKind.StartTag && Kind != HtmlEntityKind.EndTag)
			{
				ThrowUnexpectedTagNameAccess();
			}

			return _currentData;
		}
	}

	/// <summary>
	/// Gets a zero-allocation enumerable view over the current start tag's attributes.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.StartTag" />.</exception>
	public readonly AttributeEnumerable Attributes
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlEntityKind.StartTag);

			return new AttributeEnumerable(_data[_attributeStart.._attributeEnd]);
		}
	}

	/// <summary>
	/// Gets the payload for the current <see cref="HtmlEntityKind.Text" /> entity.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.Text" />.</exception>
	public readonly ReadOnlySpan<char> TextNode
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlEntityKind.Text);

			return _currentData;
		}
	}

	/// <summary>
	/// Gets the payload for the current <see cref="HtmlEntityKind.Comment" /> entity.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.Comment" />.</exception>
	public readonly ReadOnlySpan<char> Comment
	{
		get
		{
			ThrowIfUnexpectedEntity(HtmlEntityKind.Comment);

			return _currentData;
		}
	}

	internal HtmlReader(Input input)
	{
		_buffer = input.Buffer;
		_data = _buffer.AsSpan(0, input.Length);
		_options = input.Options;

		_stackBuffer = ArrayPool<StackItem>.Shared.Rent(GetRentLength(Math.Max(input.Options.InitialDepthStackSize, 1)));
		_kind = default;
		_position = 0;
		_attributeStart = 0;
		_attributeEnd = 0;
		_stackCount = 0;
		_depth = 0;
		_currentData = default;
	}

	/// <summary>
	/// Releases parser-owned resources and invalidates all previously returned spans.
	/// </summary>
	public readonly void Dispose()
	{
		_options.TextBufferPool.Return(_buffer);

		ArrayPool<StackItem>.Shared.Return(_stackBuffer);
	}

	/// <summary>
	/// Tries to get the value of a named attribute from the current start tag.
	/// </summary>
	/// <param name="name">The attribute name to search for.</param>
	/// <param name="value">Receives the attribute value when found.</param>
	/// <returns><see langword="true" /> when a matching attribute is found; otherwise, <see langword="false" />.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.StartTag" />.</exception>
	public readonly bool TryGetAttribute(ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
	{
		ThrowIfUnexpectedEntity(HtmlEntityKind.StartTag);

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
	/// Exposes mutable access to the current text node when the current entity is text.
	/// </summary>
	/// <returns>A mutable span over the current text node.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.Text" />.</exception>
	public readonly Span<char> GetDangerousMutableTextNode()
	{
		ThrowIfUnexpectedEntity(HtmlEntityKind.Text);

		return _currentData;
	}

	/// <summary>
	/// Exposes mutable access to the current comment when the current entity is a comment.
	/// </summary>
	/// <returns>A mutable span over the current comment.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.Comment" />.</exception>
	public readonly Span<char> GetDangerousMutableComment()
	{
		ThrowIfUnexpectedEntity(HtmlEntityKind.Comment);

		return _currentData;
	}

	private readonly void ThrowIfUnexpectedEntity(HtmlEntityKind expected)
	{
		if (Kind != expected)
		{
			ThrowUnexpectedEntity(expected);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private readonly void ThrowUnexpectedEntity(HtmlEntityKind expected)
	{
		throw new InvalidOperationException($"Expected entity '{expected}' but current entity is '{Kind}'");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private readonly void ThrowUnexpectedTagNameAccess()
	{
		throw new InvalidOperationException($"Expected entity '{HtmlEntityKind.StartTag}' or '{HtmlEntityKind.EndTag}' but current entity is '{Kind}'");
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

		internal AttributeEnumerator(ReadOnlySpan<char> data)
		{
			_data = data;
			_cursor = 0;
			_current = default;
		}

		/// <summary>
		/// Gets the current attribute.
		/// </summary>
		public readonly HtmlAttribute Current => _current;

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
