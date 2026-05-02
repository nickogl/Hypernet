using System.Buffers;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private static readonly SearchValues<char> _htmlWhitespace = SearchValues.Create(" \t\r\n\f");
	private static readonly SearchValues<char> _attributeNameStartDisallowed = SearchValues.Create(" \t\r\n\f\"'=<>/`");
	private static readonly SearchValues<char> _attributeNameTerminators = SearchValues.Create(" \t\r\n\f\"'=<>/`");
	private static readonly SearchValues<char> _unquotedAttributeTerminators = SearchValues.Create(" \t\r\n\f>");

	/// <summary>
	/// Tries to get the value of a named attribute from the current start tag.
	/// </summary>
	/// <remarks>The returned span remains valid as long as the span initially passed to the reader is valid.</remarks>
	/// <param name="name">The attribute name to search for.</param>
	/// <param name="value">Receives the attribute value when found.</param>
	/// <returns><see langword="true" /> when a matching attribute is found; otherwise, <see langword="false" />.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public readonly bool TryGetAttribute(ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
	{
		ThrowIfUnexpectedEntity(HtmlToken.StartTag);
		if (name.IsEmpty)
		{
			value = default;
			return false;
		}

		var data = _data[_attributeStart.._attributeEnd];
		var searchCursor = 0;
		var quoteCursor = 0;
		var activeQuote = '\0';
		var firstChar = name[0];
		while (searchCursor < data.Length)
		{
			var startIndex = FindAttributeNameStart(data, searchCursor, firstChar);
			if (startIndex < 0)
			{
				break;
			}

			AdvanceQuoteState(data, ref quoteCursor, startIndex, ref activeQuote);
			if (activeQuote != '\0' || !IsAttributeNameBoundary(data, startIndex))
			{
				searchCursor = startIndex + 1;
				quoteCursor = searchCursor;
				continue;
			}

			var cursor = startIndex + 1;
			var nameStart = startIndex;
			var nameTerminatorIndex = data[cursor..].IndexOfAny(_attributeNameTerminators);
			var nameEnd = nameTerminatorIndex >= 0 ? cursor + nameTerminatorIndex : data.Length;
			cursor = SkipWhitespace(data, nameEnd);
			if (data[nameStart..nameEnd].Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				if (cursor < data.Length && data[cursor] == '=')
				{
					cursor++;
					cursor = SkipWhitespace(data, cursor);
					ReadAttributeValue(data, cursor, out _, out var valueStart, out var valueLength);
					value = data.Slice(valueStart, valueLength);
					return true;
				}

				value = default;
				return true;
			}

			if (cursor < data.Length && data[cursor] == '=')
			{
				cursor++;
				cursor = SkipWhitespace(data, cursor);
				SkipAttributeValue(data, cursor, out cursor);
			}

			searchCursor = cursor;
			quoteCursor = cursor;
			activeQuote = '\0';
		}

		value = default;
		return false;
	}

	private static bool IsAttributeNameBoundary(scoped ReadOnlySpan<char> data, int startIndex)
	{
		if (startIndex == 0)
		{
			return true;
		}

		var previous = data[startIndex - 1];
		return IsHtmlWhitespace(previous) || previous == '/';
	}

	private static void AdvanceQuoteState(scoped ReadOnlySpan<char> data, ref int cursor, int endExclusive, ref char activeQuote)
	{
		for (var i = cursor; i < endExclusive; i++)
		{
			var current = data[i];
			if (activeQuote == '\0')
			{
				if (current is '"' or '\'')
				{
					activeQuote = current;
				}
			}
			else if (current == activeQuote)
			{
				activeQuote = '\0';
			}
		}

		cursor = endExclusive;
	}

	private static bool TryReadAttribute(ReadOnlySpan<char> data, int cursor, out int nextCursor, out HtmlAttribute attribute)
	{
		var startIndex = data[cursor..].IndexOfAnyExcept(_attributeNameStartDisallowed);
		if (startIndex < 0)
		{
			nextCursor = data.Length;
			attribute = default;
			return false;
		}
		cursor += startIndex;

		var nameStart = cursor++;
		var nameTerminatorIndex = data[cursor..].IndexOfAny(_attributeNameTerminators);
		var nameEnd = nameTerminatorIndex >= 0 ? cursor + nameTerminatorIndex : data.Length;
		cursor = SkipWhitespace(data, nameEnd);
		if (cursor < data.Length && data[cursor] == '=')
		{
			cursor++;
			cursor = SkipWhitespace(data, cursor);
			ReadAttributeValue(data, cursor, out nextCursor, out var valueStart, out var valueLength);
			attribute = new HtmlAttribute(data[nameStart..nameEnd], data.Slice(valueStart, valueLength));
			return true;
		}

		nextCursor = cursor;
		attribute = new HtmlAttribute(data[nameStart..nameEnd], default);
		return true;
	}

	private static int FindAttributeNameStart(scoped ReadOnlySpan<char> data, int cursor, char firstChar)
	{
		var search = data[cursor..];
		if (char.IsAsciiLetter(firstChar))
		{
			var lower = char.ToLowerInvariant(firstChar);
			var upper = char.ToUpperInvariant(firstChar);
			var index = lower == upper
				? search.IndexOf(lower)
				: search.IndexOfAny(lower, upper);
			return index >= 0 ? cursor + index : -1;
		}

		var exactIndex = search.IndexOf(firstChar);
		return exactIndex >= 0 ? cursor + exactIndex : -1;
	}

	private static void ReadAttributeValue(scoped ReadOnlySpan<char> data, int cursor, out int nextCursor, out int valueStart, out int valueLength)
	{
		if (cursor >= data.Length)
		{
			nextCursor = data.Length;
			valueStart = data.Length;
			valueLength = 0;
			return;
		}

		var quote = data[cursor];
		if (quote is '"' or '\'')
		{
			valueStart = cursor + 1;
			var quoteEnd = data[valueStart..].IndexOf(quote);
			if (quoteEnd < 0)
			{
				valueLength = data.Length - valueStart;
				nextCursor = data.Length;
				return;
			}

			valueLength = quoteEnd;
			nextCursor = valueStart + quoteEnd + 1;
			return;
		}

		var terminatorIndex = data[cursor..].IndexOfAny(_unquotedAttributeTerminators);
		if (terminatorIndex < 0)
		{
			valueStart = cursor;
			valueLength = data.Length - cursor;
			nextCursor = data.Length;
			return;
		}

		var end = cursor + terminatorIndex;
		valueStart = cursor;
		valueLength = end - cursor;
		nextCursor = end;
	}

	private static void SkipAttributeValue(scoped ReadOnlySpan<char> data, int cursor, out int nextCursor)
	{
		if (cursor >= data.Length)
		{
			nextCursor = data.Length;
			return;
		}

		var quote = data[cursor];
		if (quote is '"' or '\'')
		{
			var quoteEnd = data[(cursor + 1)..].IndexOf(quote);
			nextCursor = quoteEnd >= 0 ? cursor + quoteEnd + 2 : data.Length;
			return;
		}

		var terminatorIndex = data[cursor..].IndexOfAny(_unquotedAttributeTerminators);
		nextCursor = terminatorIndex >= 0 ? cursor + terminatorIndex : data.Length;
	}

	private static int SkipWhitespace(scoped ReadOnlySpan<char> data, int cursor)
	{
		if (cursor >= data.Length)
		{
			return data.Length;
		}

		var index = data[cursor..].IndexOfAnyExcept(_htmlWhitespace);
		return index >= 0 ? cursor + index : data.Length;
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
			return TryReadAttribute(_data, _cursor, out _cursor, out _current);
		}
	}
}
