using System.Buffers;
using System.Runtime.CompilerServices;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private const string CommentStart = "<!--";
	private static readonly SearchValues<char> _htmlWhitespace = SearchValues.Create(" \t\r\n\f");
	private static readonly SearchValues<char> _unquotedAttributeTerminators = SearchValues.Create(" \t\r\n\f>/");

	/// <summary>
	/// Advances the reader to the next logical HTML entity.
	/// </summary>
	/// <returns>The outcome of the read operation.</returns>
	public HtmlReadResult Read()
	{
		ClearCurrentEntity();

		while (true)
		{
			if (_position >= _data.Length)
			{
				if (_stackCount == 0)
				{
					return HtmlReadResult.EndOfDocument;
				}

				EmitEndTag(PopOpenTag());
				return HtmlReadResult.Node;
			}

			if (_data[_position] != '<')
			{
				ReadTextNode();
				return HtmlReadResult.Node;
			}

			if (TryReadComment())
			{
				return HtmlReadResult.Node;
			}

			var position = _position;
			if (TryReadEndTag())
			{
				return HtmlReadResult.Node;
			}

			if (_position != position)
			{
				continue;
			}

			if (TryReadStartTag())
			{
				return HtmlReadResult.Node;
			}

			if (TrySkipBogusMarkup())
			{
				continue;
			}

			ReadSingleCharacterTextNode();
			return HtmlReadResult.Node;
		}
	}

	private void ClearCurrentEntity()
	{
		_kind = default;
		_depth = 0;
		_currentData = default;
	}

	private void ReadTextNode()
	{
		var start = _position;
		var index = _data.Slice(start).IndexOf('<');
		var end = index >= 0 ? start + index : _data.Length;
		_position = end;
		SetCurrentEntity(HtmlEntityKind.Text, _stackCount, _data.Slice(start, end - start));
	}

	private void ReadSingleCharacterTextNode()
	{
		var start = _position;
		_position++;
		SetCurrentEntity(HtmlEntityKind.Text, _stackCount, _data.Slice(start, 1));
	}

	private bool TryReadComment()
	{
		if (_position + 3 >= _data.Length)
		{
			return false;
		}

		var remaining = _data.Slice(_position);
		if (!remaining.StartsWith(CommentStart, StringComparison.Ordinal))
		{
			return false;
		}

		var contentStart = _position + 4;
		var cursor = contentStart;
		while (cursor < _data.Length)
		{
			var hyphenIndex = _data.Slice(cursor).IndexOf('-');
			if (hyphenIndex < 0)
			{
				_position = _data.Length;
				SetCurrentEntity(HtmlEntityKind.Comment, _stackCount, _data.Slice(contentStart));
				return true;
			}

			cursor += hyphenIndex;
			if (cursor + 2 < _data.Length && _data[cursor + 1] == '-' && _data[cursor + 2] == '>')
			{
				var value = _data.Slice(contentStart, cursor - contentStart);
				_position = cursor + 3;
				SetCurrentEntity(HtmlEntityKind.Comment, _stackCount, value);
				return true;
			}

			cursor++;
		}

		_position = _data.Length;
		SetCurrentEntity(HtmlEntityKind.Comment, _stackCount, _data.Slice(contentStart));
		return true;
	}

	private bool TryReadStartTag()
	{
		var cursor = _position + 1;
		if (cursor >= _data.Length || !IsTagNameStart(_data[cursor]))
		{
			return false;
		}

		var nameStart = cursor;
		cursor++;
		while (cursor < _data.Length && IsTagNameChar(_data[cursor]))
		{
			cursor++;
		}

		var nameEnd = cursor;
		var name = _data.Slice(nameStart, nameEnd - nameStart);
		if (_stackCount > 0 && ShouldImplicitlyClose(GetOpenTagName(_stackCount - 1), name))
		{
			EmitEndTag(PopOpenTag());
			return true;
		}

		var tagEnd = FindMarkupEnd(nameEnd);
		var contentEnd = tagEnd;
		while (contentEnd > nameEnd && IsHtmlWhitespace(_data[contentEnd - 1]))
		{
			contentEnd--;
		}

		var selfClosing = false;
		if (contentEnd > nameEnd && _data[contentEnd - 1] == '/')
		{
			selfClosing = true;
			contentEnd--;
			while (contentEnd > nameEnd && IsHtmlWhitespace(_data[contentEnd - 1]))
			{
				contentEnd--;
			}
		}

		_position = tagEnd < _data.Length && _data[tagEnd] == '>' ? tagEnd + 1 : _data.Length;
		_attributeStart = nameEnd;
		_attributeEnd = contentEnd;
		var depth = _stackCount + 1;
		if (!selfClosing && !IsVoidElement(name))
		{
			PushOpenTag(nameStart, nameEnd - nameStart);
			depth = _stackCount;
		}

		SetCurrentEntity(HtmlEntityKind.StartTag, depth, name);
		return true;
	}

	private bool TryReadEndTag()
	{
		var cursor = _position + 2;
		if (_position + 1 >= _data.Length || _data[_position + 1] != '/' || cursor >= _data.Length || !IsTagNameStart(_data[cursor]))
		{
			return false;
		}

		var nameStart = cursor;
		cursor++;
		while (cursor < _data.Length && IsTagNameChar(_data[cursor]))
		{
			cursor++;
		}

		var name = _data.Slice(nameStart, cursor - nameStart);
		var matchIndex = FindOpenTag(name);
		if (matchIndex < 0)
		{
			_position = FindMarkupNextPosition(cursor);
			return false;
		}

		if (matchIndex < _stackCount - 1)
		{
			EmitEndTag(PopOpenTag());
			return true;
		}

		_position = FindMarkupNextPosition(cursor);
		EmitEndTag(PopOpenTag());
		return true;
	}

	private bool TrySkipBogusMarkup()
	{
		if (_position + 1 >= _data.Length)
		{
			return false;
		}

		var marker = _data[_position + 1];
		if (marker is not ('!' or '?'))
		{
			return false;
		}

		_position = FindMarkupNextPosition(_position + 2);
		return true;
	}

	private void SetCurrentEntity(HtmlEntityKind kind, int depth, Span<char> data)
	{
		_kind = kind;
		_depth = depth;
		_currentData = data;
	}

	private void EmitEndTag(StackItem item)
	{
		SetCurrentEntity(HtmlEntityKind.EndTag, _stackCount, _data.Slice(item.NameStart, item.NameLength));
	}

	private void PushOpenTag(int nameStart, int nameLength)
	{
		if (_stackCount >= _options.MaxDepth)
		{
			ThrowMaxDepthExceeded();
		}

		if (_stackCount == _stackBuffer.Length)
		{
			var newBuffer = ArrayPool<StackItem>.Shared.Rent(GetRentLength(Math.Min(_options.MaxDepth, _stackBuffer.Length * 2)));
			Array.Copy(_stackBuffer, newBuffer, _stackCount);
			ArrayPool<StackItem>.Shared.Return(_stackBuffer);
			_stackBuffer = newBuffer;
		}

		_stackBuffer[_stackCount] = new StackItem(nameStart, nameLength);
		_stackCount++;
	}

	private StackItem PopOpenTag()
	{
		_stackCount--;
		return _stackBuffer[_stackCount];
	}

	private readonly int FindOpenTag(ReadOnlySpan<char> name)
	{
		for (var i = _stackCount - 1; i >= 0; i--)
		{
			if (NamesEqual(GetOpenTagName(i), name))
			{
				return i;
			}
		}

		return -1;
	}

	private readonly ReadOnlySpan<char> GetOpenTagName(int index)
	{
		var item = _stackBuffer[index];
		return _data.Slice(item.NameStart, item.NameLength);
	}

	private readonly int FindMarkupEnd(int cursor)
	{
		var quote = '\0';
		while (cursor < _data.Length)
		{
			var value = _data[cursor];
			if (quote != '\0')
			{
				if (value == quote)
				{
					quote = '\0';
				}

				cursor++;
				continue;
			}

			if (value is '"' or '\'')
			{
				quote = value;
				cursor++;
				continue;
			}

			if (value == '>')
			{
				return cursor;
			}

			cursor++;
		}

		return _data.Length;
	}

	private readonly int FindMarkupNextPosition(int cursor)
	{
		var tagEnd = FindMarkupEnd(cursor);
		return tagEnd < _data.Length && _data[tagEnd] == '>' ? tagEnd + 1 : _data.Length;
	}

	private static bool TryReadAttribute(ReadOnlySpan<char> data, int cursor, out int nextCursor, out HtmlAttribute attribute)
	{
		cursor = SkipWhitespace(data, cursor);
		while (cursor < data.Length && !IsAttributeNameStart(data[cursor]))
		{
			cursor++;
			cursor = SkipWhitespace(data, cursor);
		}

		if (cursor >= data.Length)
		{
			nextCursor = data.Length;
			attribute = default;
			return false;
		}

		var nameStart = cursor;
		cursor++;
		while (cursor < data.Length && IsAttributeNameChar(data[cursor]))
		{
			cursor++;
		}

		var nameLength = cursor - nameStart;
		cursor = SkipWhitespace(data, cursor);
		if (cursor < data.Length && data[cursor] == '=')
		{
			cursor++;
			cursor = SkipWhitespace(data, cursor);
			ReadAttributeValue(data, cursor, out nextCursor, out var valueStart, out var valueLength);
			attribute = new HtmlAttribute(data.Slice(nameStart, nameLength), data.Slice(valueStart, valueLength));
			return true;
		}

		nextCursor = cursor;
		attribute = new HtmlAttribute(data.Slice(nameStart, nameLength), default);
		return true;
	}

	private static void ReadAttributeValue(ReadOnlySpan<char> data, int cursor, out int nextCursor, out int valueStart, out int valueLength)
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
			cursor = valueStart;
			while (cursor < data.Length && data[cursor] != quote)
			{
				cursor++;
			}

			valueLength = cursor - valueStart;
			if (cursor < data.Length)
			{
				cursor++;
			}
			nextCursor = cursor;
			return;
		}

		var terminatorIndex = data.Slice(cursor).IndexOfAny(_unquotedAttributeTerminators);
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

	private static int SkipWhitespace(ReadOnlySpan<char> data, int cursor)
	{
		var index = data[cursor..].IndexOfAnyExcept(_htmlWhitespace);
		return index >= 0 ? cursor + index : data.Length;
	}

	private static bool ShouldImplicitlyClose(ReadOnlySpan<char> openTag, ReadOnlySpan<char> nextTag)
	{
		if (IsHeading(openTag))
		{
			return IsHeading(nextTag);
		}

		return NamesEqual(openTag, nextTag) && IsImplicitCloseTag(nextTag);
	}

	private static bool IsImplicitCloseTag(ReadOnlySpan<char> name)
	{
		return name.Length switch
		{
			1 => name[0] is 'p' or 'P',
			2 => EqualsAsciiIgnoreCase(name, "li") || EqualsAsciiIgnoreCase(name, "dt") || EqualsAsciiIgnoreCase(name, "dd"),
			_ => false,
		};
	}

	private static bool IsHeading(ReadOnlySpan<char> name)
	{
		return name.Length == 2
			&& (name[0] is 'h' or 'H')
			&& name[1] is >= '1' and <= '6';
	}

	private static bool IsVoidElement(ReadOnlySpan<char> name)
	{
		return name.Length switch
		{
			2 => EqualsAsciiIgnoreCase(name, "br") || EqualsAsciiIgnoreCase(name, "hr"),
			3 => EqualsAsciiIgnoreCase(name, "img") || EqualsAsciiIgnoreCase(name, "wbr"),
			4 => EqualsAsciiIgnoreCase(name, "area") || EqualsAsciiIgnoreCase(name, "base") || EqualsAsciiIgnoreCase(name, "col") || EqualsAsciiIgnoreCase(name, "link") || EqualsAsciiIgnoreCase(name, "meta"),
			5 => EqualsAsciiIgnoreCase(name, "embed") || EqualsAsciiIgnoreCase(name, "input") || EqualsAsciiIgnoreCase(name, "param") || EqualsAsciiIgnoreCase(name, "track"),
			6 => EqualsAsciiIgnoreCase(name, "source"),
			_ => false,
		};
	}

	private static bool NamesEqual(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
	{
		return left.Equals(right, StringComparison.OrdinalIgnoreCase);
	}

	private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
	{
		return left.Equals(right, StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsTagNameStart(char value)
	{
		return value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsTagNameChar(char value)
	{
		return IsTagNameStart(value) || value is >= '0' and <= '9' or ':' or '-' or '_';
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsAttributeNameStart(char value)
	{
		return value is not ('"' or '\'' or '=' or '<' or '>' or '/' or '`') && !IsHtmlWhitespace(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsAttributeNameChar(char value)
	{
		return value is not ('"' or '\'' or '=' or '<' or '>' or '/' or '`') && !IsHtmlWhitespace(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsHtmlWhitespace(char value)
	{
		return value is ' ' or '\t' or '\r' or '\n' or '\f';
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void ThrowMaxDepthExceeded()
	{
		throw new InvalidOperationException($"Maximum depth '{_options.MaxDepth}' exceeded.");
	}

	private readonly struct StackItem
	{
		public StackItem(int nameStart, int nameLength)
		{
			NameStart = nameStart;
			NameLength = nameLength;
		}

		public int NameStart { get; }
		public int NameLength { get; }
	}
}
