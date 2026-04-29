using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private const string CommentStart = "<!--";
	private const string CommentEnd = "-->";
	private static readonly SearchValues<char> _markupEndCandidates = SearchValues.Create(">'\"");

	/// <summary>
	/// Advances the reader to the next logical HTML token.
	/// </summary>
	/// <returns>
	/// <see langword="true" /> if the reader has more tokens after this, or
	/// <see langword="false" /> if the end of the document was reached.
	/// </returns>
	public bool Read()
	{
		EnsureOpenTagStack();
		EnsureOpenTagStackIntegrity();

		ClearCurrentEntity();
		while (true)
		{
			if (_position >= _data.Length)
			{
				if (_openTagStack.Length == 0)
				{
					return false;
				}

				EmitEndTag(_openTagStack.Pop());
				return true;
			}

			if (_data[_position] != '<')
			{
				ReadTextNode();
				return true;
			}

			var next = _data[_position + 1];
			if (next == '/')
			{
				var position = _position;
				if (TryReadEndTag())
				{
					return true;
				}

				if (_position != position)
				{
					continue;
				}

				ReadSingleCharacterTextNode();
				return true;
			}

			if (next == '!')
			{
				if (TryReadComment())
				{
					return true;
				}
				if (TrySkipBogusMarkup())
				{
					continue;
				}
			}
			else if (next == '?' && TrySkipBogusMarkup())
			{
				continue;
			}
			else if (IsTagNameStart(next) && TryReadStartTag())
			{
				return true;
			}

			ReadSingleCharacterTextNode();
			return true;
		}
	}

	private void ClearCurrentEntity()
	{
		_token = default;
		_depth = 0;
		_currentData = default;
	}

	private void ReadTextNode()
	{
		var start = _position;
		var index = _data[start..].IndexOf('<');
		var end = index >= 0 ? start + index : _data.Length;
		_position = end;
		SetCurrentEntity(HtmlToken.Text, _openTagStack.Length, _data[start..end]);
	}

	private void ReadSingleCharacterTextNode()
	{
		var start = _position;
		_position++;
		SetCurrentEntity(HtmlToken.Text, _openTagStack.Length, _data.Slice(start, 1));
	}

	private bool TryReadComment()
	{
		if (_position + 3 >= _data.Length)
		{
			return false;
		}
		var remaining = _data[_position..];
		if (!remaining.StartsWith(CommentStart, StringComparison.Ordinal))
		{
			return false;
		}

		var contentStart = _position + 4;
		var tagEndIndex = _data[contentStart..].IndexOf(CommentEnd);
		if (tagEndIndex < 0)
		{
			_position = _data.Length;
			SetCurrentEntity(HtmlToken.Comment, _openTagStack.Length, _data[contentStart..]);
			return true;
		}

		var cursor = contentStart + tagEndIndex;
		var value = _data[contentStart..cursor];
		_position = cursor + 3;
		SetCurrentEntity(HtmlToken.Comment, _openTagStack.Length, value);
		return true;
	}

	private bool TryReadStartTag()
	{
		var cursor = _position + 1;
		if (cursor >= _data.Length || !IsTagNameStart(_data[cursor]))
		{
			return false;
		}
		var nameStart = cursor++;

		while (cursor < _data.Length && IsTagNameChar(_data[cursor]))
		{
			cursor++;
		}
		if (cursor < _data.Length && !IsStartTagNameTerminator(_data[cursor]))
		{
			return false;
		}

		var nameEnd = cursor;
		var name = _data[nameStart..nameEnd];
		if (_openTagStack.Length > 0 && ShouldImplicitlyClose(GetOpenTagName(_openTagStack.Length - 1), name))
		{
			EmitEndTag(_openTagStack.Pop());
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
		var depth = _openTagStack.Length + 1;
		if (!selfClosing && !IsVoidElement(name))
		{
			_openTagStack.Push(new OpenTagStackItem(nameStart, nameEnd - nameStart), _options.MaxDepth);
			depth = _openTagStack.Length;
		}

		SetCurrentEntity(HtmlToken.StartTag, depth, name);
		return true;
	}

	private bool TryReadEndTag()
	{
		var cursor = _position + 2;
		if (_position + 1 >= _data.Length
			|| _data[_position + 1] != '/'
			|| cursor >= _data.Length
			|| !IsTagNameStart(_data[cursor]))
		{
			return false;
		}
		var nameStart = cursor++;

		while (cursor < _data.Length && IsTagNameChar(_data[cursor]))
		{
			cursor++;
		}
		if (cursor < _data.Length && !IsEndTagNameTerminator(_data[cursor]))
		{
			return false;
		}

		var name = _data[nameStart..cursor];
		var matchIndex = FindOpenTag(name);
		if (matchIndex < 0)
		{
			_position = FindMarkupNextPosition(cursor);
			return false;
		}
		if (matchIndex < _openTagStack.Length - 1)
		{
			EmitEndTag(_openTagStack.Pop());
			return true;
		}

		_position = FindMarkupNextPosition(cursor);
		EmitEndTag(_openTagStack.Pop());
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

	private void SetCurrentEntity(HtmlToken kind, int depth, Span<char> data)
	{
		_token = kind;
		_depth = depth;
		_currentData = data;
	}

	private void EmitEndTag(OpenTagStackItem item)
	{
		SetCurrentEntity(HtmlToken.EndTag, _openTagStack.Length, _data.Slice(item.NameOffset, item.NameLength));
	}

	private readonly int FindOpenTag(ReadOnlySpan<char> name)
	{
		for (var i = _openTagStack.Length - 1; i >= 0; i--)
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
		ref var item = ref _openTagStack[index];
		return _data.Slice(item.NameOffset, item.NameLength);
	}

	private readonly int FindMarkupEnd(int cursor)
	{
		var data = _data;
		while (cursor < _data.Length)
		{
			var hit = data[cursor..].IndexOfAny(_markupEndCandidates);
			if (hit < 0)
			{
				return data.Length;
			}
			cursor += hit;
			if (data[cursor] == '>')
			{
				return cursor;
			}

			var quote = data[cursor++];
			var quoteEnd = data[cursor..].IndexOf(quote);
			if (quoteEnd < 0)
			{
				return data.Length;
			}

			cursor += quoteEnd + 1;
		}

		return data.Length;
	}

	private readonly int FindMarkupNextPosition(int cursor)
	{
		var tagEnd = FindMarkupEnd(cursor);
		return tagEnd < _data.Length && _data[tagEnd] == '>' ? tagEnd + 1 : _data.Length;
	}

	private static bool ShouldImplicitlyClose(scoped ReadOnlySpan<char> openTag, scoped ReadOnlySpan<char> nextTag)
	{
		if (IsHeading(openTag))
		{
			return IsHeading(nextTag);
		}

		return NamesEqual(openTag, nextTag) && IsImplicitCloseTag(nextTag);
	}

	private static bool IsImplicitCloseTag(scoped ReadOnlySpan<char> name)
	{
		return name.Length switch
		{
			1 => name[0] is 'p' or 'P',
			2 => EqualsAsciiIgnoreCase(name, "li") || EqualsAsciiIgnoreCase(name, "dt") || EqualsAsciiIgnoreCase(name, "dd"),
			_ => false,
		};
	}

	private static bool IsHeading(scoped ReadOnlySpan<char> name)
	{
		return name.Length == 2
			&& (name[0] is 'h' or 'H')
			&& name[1] is >= '1' and <= '6';
	}

	private static bool IsVoidElement(scoped ReadOnlySpan<char> name)
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool NamesEqual(scoped ReadOnlySpan<char> left, scoped ReadOnlySpan<char> right)
	{
		return left.Equals(right, StringComparison.OrdinalIgnoreCase);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool EqualsAsciiIgnoreCase(scoped ReadOnlySpan<char> left, scoped ReadOnlySpan<char> right)
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
	private static bool IsHtmlWhitespace(char value)
	{
		return value is ' ' or '\t' or '\r' or '\n' or '\f';
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsStartTagNameTerminator(char value)
	{
		return value == '>' || value == '/' || IsHtmlWhitespace(value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsEndTagNameTerminator(char value)
	{
		return value == '>' || IsHtmlWhitespace(value);
	}

	private unsafe void EnsureOpenTagStack()
	{
		if (_openTagStack._inline.IsEmpty)
		{
			Span<OpenTagStackItem> openTagSpan = _openTagStackInlineStorage;
			fixed (OpenTagStackItem* openTagPtr = &_openTagStackInlineStorage[0])
			{
#if DEBUG
				_openTagStackInlineStorageAddress = openTagPtr;
#endif
				_openTagStack = new(openTagPtr, openTagSpan.Length);
			}
		}
	}

	[Conditional("DEBUG")]
	private readonly unsafe void EnsureOpenTagStackIntegrity()
	{
#if DEBUG
		fixed (OpenTagStackItem* ptr = &_openTagStackInlineStorage[0])
		{
			Debug.Assert(
				ptr == _openTagStackInlineStorageAddress,
				"HtmlReader was copied after construction. It contains internal references to its own inline storage and must be passed by reference.");
		}
#endif
	}
}
