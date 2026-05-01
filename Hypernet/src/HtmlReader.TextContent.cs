using System.Buffers;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private static readonly SearchValues<char> _characterReferenceStart = SearchValues.Create("&");
	private static readonly SearchValues<char> _normalizedContentTokenStart = SearchValues.Create(" &\t\r\n\f\u00A0");
	private static readonly SearchValues<char> _normalizedWhitespace = SearchValues.Create(" \t\r\n\f\u00A0");

	/// <summary>
	/// Consumes the current element subtree and returns its extracted text content by mutating the underlying HTML buffer.
	/// </summary>
	/// <remarks>
	/// Use this when you want the full extracted text and do not want to allocate or pool additional memory.
	/// This method rewrites the underlying HTML content starting after the current HTML tag, so do not call
	/// it if you plan on re-using that content later.
	/// </remarks>
	/// <param name="options">Controls which textual sources are included and whether whitespace is normalized.</param>
	/// <returns>
	/// The extracted text content for the current element subtree. The returned span is valid for the
	/// provided HTML content's lifetime. Materialize it as a string if you need to persist it after
	/// disposing of the HTML content.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public ReadOnlySpan<char> GetDangerousTextContent(HtmlTextContentOptions options = HtmlTextContentOptions.None)
	{
		ThrowIfUnexpectedEntity(HtmlToken.StartTag);

		if (!IsPersistentStartTag())
		{
			SetCurrentEntity(HtmlToken.EndTag, _depth - 1, _currentData);
			return default;
		}

		var includeComments = (options & HtmlTextContentOptions.IncludeComments) != 0;
		var includeNonContentText = (options & HtmlTextContentOptions.IncludeNonContentText) != 0;
		var normalizeWhitespace = (options & HtmlTextContentOptions.NormalizeWhitespace) != 0;
		var keepUnknownEntities = (options & HtmlTextContentOptions.KeepUnknownEntities) != 0;
		var targetDepth = _depth - 1;
		var destinationStart = _position;
		var ignoredDepth = !includeNonContentText && IsNonContentTag(_currentData)
			? targetDepth
			: -1;

		// Segments are stored as source-relative slices first because the method rewrites
		// the same buffer it is reading from, which could corrupt the open tag stack. Most
		// documents will not pool any memory for this, so this is still fairly cheap.
		Span<TextSegment> inlineSegmentStorage = stackalloc TextSegment[_options.InitialTextContentSegmentSize];
		using var segments = new ScratchBuffer<TextSegment>(inlineSegmentStorage);
		while (Read())
		{
			if (ignoredDepth < 0 && _token == HtmlToken.StartTag && !includeNonContentText && IsNonContentTag(_currentData))
			{
				ignoredDepth = _depth - 1;
			}
			else if (ignoredDepth < 0)
			{
				if (_token == HtmlToken.Text || _token == HtmlToken.Comment && includeComments)
				{
					var leadingWhitespace = false;
					var trailingWhitespace = false;
					var written = normalizeWhitespace
						? RewriteContentNormalized(_currentData, _currentData, keepUnknownEntities, out leadingWhitespace, out trailingWhitespace)
						: RewriteContentInPlace(_currentData, _currentData, keepUnknownEntities);
					if ((written > 0 || leadingWhitespace) && _data.Overlaps(_currentData, out var offset))
					{
						segments.Push(new TextSegment(offset, written, leadingWhitespace, trailingWhitespace), _options.MaxTextContentSegmentSize);
					}
				}
			}

			if (_token == HtmlToken.EndTag && _depth == ignoredDepth)
			{
				ignoredDepth = -1;
			}

			if (_token == HtmlToken.EndTag && _depth == targetDepth)
			{
				break;
			}
		}

		var destinationCursor = destinationStart;
		var hasOutput = false;
		var pendingWhitespace = false;
		foreach (var segment in segments)
		{
			if (segment.Length > 0)
			{
				if (hasOutput && (pendingWhitespace || segment.LeadingWhitespace))
				{
					_data[destinationCursor] = ' ';
					destinationCursor++;
				}

				_data.Slice(segment.Start, segment.Length).CopyTo(_data[destinationCursor..]);
				destinationCursor += segment.Length;
				hasOutput = true;
				pendingWhitespace = segment.TrailingWhitespace;
				continue;
			}

			if (hasOutput && segment.LeadingWhitespace)
			{
				pendingWhitespace = true;
			}
		}

		return _data[destinationStart..destinationCursor];
	}

	/// <summary>
	/// Consumes the current element subtree and writes its extracted text content into a caller-provided buffer.
	/// </summary>
	/// <remarks>
	/// Use this when truncated text is acceptable and you want to avoid some runtime overhead that comes
	/// with mutating the content in-place. The method still consumes the full subtree and leaves the reader
	/// on the matching end tag, regardless of the result of this function.
	/// </remarks>
	/// <param name="destination">Receives the extracted text content.</param>
	/// <param name="charsWritten">Receives the number of characters written to <paramref name="destination" />.</param>
	/// <returns>
	/// <see langword="true" /> when the entire text content fit in <paramref name="destination" />,
	/// <see langword="false" /> when the destination was exhausted and the result was truncated.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public bool TryGetTextContent(scoped Span<char> destination, out int charsWritten)
	{
		return TryGetTextContent(destination, HtmlTextContentOptions.None, out charsWritten);
	}

	/// <summary>
	/// Consumes the current element subtree and writes its extracted text content into a caller-provided buffer.
	/// </summary>
	/// <remarks>
	/// Use this when truncated text is acceptable and you want to avoid some runtime overhead that comes
	/// with mutating the content in-place. This overload supports the same inclusion and whitespace options
	/// as <see cref="GetDangerousTextContent(HtmlTextContentOptions)" />. The method still consumes the full subtree
	/// and leaves the reader on the matching end tag, regardless of the result of this function.
	/// </remarks>
	/// <param name="destination">Receives the extracted text content.</param>
	/// <param name="options">Controls which textual sources are included and whether whitespace is normalized.</param>
	/// <param name="charsWritten">Receives the number of characters written to <paramref name="destination" />.</param>
	/// <returns>
	/// <see langword="true" /> when the entire text content fit in <paramref name="destination" />,
	/// <see langword="false" /> when the destination was exhausted and the result was truncated.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public bool TryGetTextContent(scoped Span<char> destination, HtmlTextContentOptions options, out int charsWritten)
	{
		ThrowIfUnexpectedEntity(HtmlToken.StartTag);

		if (!IsPersistentStartTag())
		{
			SetCurrentEntity(HtmlToken.EndTag, _depth - 1, _currentData);
			charsWritten = 0;
			return true;
		}

		var includeComments = (options & HtmlTextContentOptions.IncludeComments) != 0;
		var includeNonContentText = (options & HtmlTextContentOptions.IncludeNonContentText) != 0;
		var normalizeWhitespace = (options & HtmlTextContentOptions.NormalizeWhitespace) != 0;
		var keepUnknownEntities = (options & HtmlTextContentOptions.KeepUnknownEntities) != 0;
		var targetDepth = _depth - 1;
		var destinationCursor = 0;
		var ignoredDepth = !includeNonContentText && IsNonContentTag(_currentData)
			? targetDepth
			: -1;
		var pendingWhitespace = false;
		var hasOutput = false;
		var truncated = false;

		while (Read())
		{
			if (!truncated && ignoredDepth < 0 && _token == HtmlToken.StartTag && !includeNonContentText && IsNonContentTag(_currentData))
			{
				ignoredDepth = _depth - 1;
			}
			else if (!truncated && ignoredDepth < 0 && (_token == HtmlToken.Text || _token == HtmlToken.Comment && includeComments))
			{
				var written = 0;
				var leadingWhitespace = false;
				var completed = TryRewriteContent(_currentData, destination[destinationCursor..], keepUnknownEntities, normalizeWhitespace, ref written, ref pendingWhitespace, ref hasOutput, ref leadingWhitespace);
				destinationCursor += written;
				truncated = !completed;
			}

			if (_token == HtmlToken.EndTag && _depth == ignoredDepth)
			{
				ignoredDepth = -1;
			}

			if (_token == HtmlToken.EndTag && _depth == targetDepth)
			{
				break;
			}
		}

		charsWritten = destinationCursor;
		return !truncated;
	}

	/// <summary>
	/// Tries to get the current element subtree's extracted text content without advancing the reader.
	/// </summary>
	/// <remarks>
	/// This overload preserves the reader state by snapshotting and restoring the current traversal state
	/// after the extraction completes. Use this when you need the text content and still want to inspect the
	/// same subtree again with the reader.
	/// </remarks>
	/// <param name="destination">Receives the extracted text content.</param>
	/// <param name="charsWritten">Receives the number of characters written to <paramref name="destination" />.</param>
	/// <returns>
	/// <see langword="true" /> when the entire text content fit in <paramref name="destination" />,
	/// <see langword="false" /> when the destination was exhausted and the result was truncated.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public bool TryPeekTextContent(scoped Span<char> destination, out int charsWritten)
	{
		return TryPeekTextContent(destination, HtmlTextContentOptions.None, out charsWritten);
	}

	/// <summary>
	/// Tries to get the current element subtree's extracted text content without advancing the reader.
	/// </summary>
	/// <remarks>
	/// This overload preserves the reader state by snapshotting and restoring the current traversal state
	/// after the extraction completes. Use this when you need the text content and still want to inspect the
	/// same subtree again with the reader. This overload supports the same inclusion and whitespace options
	/// as <see cref="GetDangerousTextContent(HtmlTextContentOptions)" />.
	/// </remarks>
	/// <param name="destination">Receives the extracted text content.</param>
	/// <param name="options">Controls which textual sources are included and whether whitespace is normalized.</param>
	/// <param name="charsWritten">Receives the number of characters written to <paramref name="destination" />.</param>
	/// <returns>
	/// <see langword="true" /> when the entire text content fit in <paramref name="destination" />,
	/// <see langword="false" /> when the destination was exhausted and the result was truncated.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the current token is not <see cref="HtmlToken.StartTag" />.
	/// </exception>
	public bool TryPeekTextContent(scoped Span<char> destination, HtmlTextContentOptions options, out int charsWritten)
	{
		ThrowIfUnexpectedEntity(HtmlToken.StartTag);

		if (!IsPersistentStartTag())
		{
			charsWritten = 0;
			return true;
		}

		var peekReader = new HtmlReader(_data, _options)
		{
			_token = HtmlToken.StartTag,
			_position = _position,
			_attributeStart = _attributeStart,
			_attributeEnd = _attributeEnd,
			_depth = 1,
			_currentData = _currentData,
		};
		peekReader.EnsureOpenTagStack();
		peekReader._openTagStack.Push(_openTagStack[^1]);
		try
		{
			// If nesting is deep, the open tag stack may fall back to pooled memory,
			// so we need to dispose of the reader to return the rented memory. However,
			// we cannot use a "using" declaration because it marks the local as readonly,
			// creating a defensive copy when we mutate the open tag stack.
			return peekReader.TryGetTextContent(destination, options, out charsWritten);
		}
		finally
		{
			peekReader.Dispose();
		}
	}

	private readonly bool IsPersistentStartTag()
	{
		return _openTagStack.Length > 0
			&& _depth == _openTagStack.Length
			&& GetOpenTagName(_openTagStack.Length - 1).Equals(_currentData, StringComparison.OrdinalIgnoreCase);
	}

	private static int RewriteContentInPlace(ReadOnlySpan<char> source, Span<char> destination, bool keepUnknownEntities)
	{
		var length = 0;
		var pendingWhitespace = false;
		var hasOutput = false;
		var leadingWhitespace = false;
		TryRewriteContent(source, destination, keepUnknownEntities, normalizeWhitespace: false, ref length, ref pendingWhitespace, ref hasOutput, ref leadingWhitespace);
		return length;
	}

	private static int RewriteContentNormalized(ReadOnlySpan<char> source, Span<char> destination, bool keepUnknownEntities, out bool leadingWhitespace, out bool trailingWhitespace)
	{
		var length = 0;
		var pendingWhitespace = false;
		var hasOutput = false;
		leadingWhitespace = false;
		TryRewriteContent(source, destination, keepUnknownEntities, normalizeWhitespace: true, ref length, ref pendingWhitespace, ref hasOutput, ref leadingWhitespace);
		trailingWhitespace = pendingWhitespace;
		return length;
	}

	private static bool TryRewriteContent(
		ReadOnlySpan<char> source,
		Span<char> destination,
		bool keepUnknownEntities,
		bool normalizeWhitespace,
		ref int length,
		ref bool pendingWhitespace,
		ref bool hasOutput,
		ref bool leadingWhitespace)
	{
		Span<char> decodedEntity = stackalloc char[4];
		var cursor = 0;
		var tokenStart = normalizeWhitespace ? _normalizedContentTokenStart : _characterReferenceStart;
		while (cursor < source.Length)
		{
			var tokenIndex = FindTokenStart(source, cursor, tokenStart);
			if (tokenIndex < 0)
			{
				if (normalizeWhitespace)
				{
					if (!AppendLiteral(source[cursor..], destination, ref length, ref pendingWhitespace))
					{
						return false;
					}

					if (length > 0)
					{
						hasOutput = true;
					}

					return true;
				}

				return AppendLiteral(source[cursor..], destination, ref length);
			}

			if (normalizeWhitespace)
			{
				if (!AppendLiteral(source[cursor..tokenIndex], destination, ref length, ref pendingWhitespace))
				{
					return false;
				}

				if (tokenIndex > cursor)
				{
					hasOutput = true;
				}

				cursor = tokenIndex;
				if (source[cursor] != '&')
				{
					cursor = SkipWhitespaceRun(source, cursor);
					if (hasOutput)
					{
						pendingWhitespace = true;
					}
					else
					{
						leadingWhitespace = true;
					}

					continue;
				}
			}
			else
			{
				if (!AppendLiteral(source[cursor..tokenIndex], destination, ref length))
				{
					return false;
				}

				cursor = tokenIndex;
			}

			if (!TryAppendCharacterReference(source, destination, keepUnknownEntities, normalizeWhitespace, ref cursor, ref length, ref pendingWhitespace, ref hasOutput, ref leadingWhitespace, decodedEntity))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryAppendCharacterReference(
		ReadOnlySpan<char> source,
		Span<char> destination,
		bool keepUnknownEntities,
		bool normalizeWhitespace,
		ref int cursor,
		ref int length,
		ref bool pendingWhitespace,
		ref bool hasOutput,
		ref bool leadingWhitespace,
		Span<char> decodedEntity)
	{
		if (cursor + 1 >= source.Length || !IsReferenceStart(source[cursor + 1]))
		{
			if (!AppendLiteral("&", destination, ref length, ref pendingWhitespace))
			{
				return false;
			}

			hasOutput = true;
			cursor++;
			return true;
		}

		var entityLength = source[cursor..].IndexOf(';');
		if (entityLength >= 0
			&& TryDecodeCharacterReference(source, cursor, entityLength, decodedEntity, out var charsWritten))
		{
			if (normalizeWhitespace && charsWritten == 1 && IsWhitespaceCharacter(decodedEntity[0]))
			{
				if (length > 0)
				{
					pendingWhitespace = true;
				}
				else
				{
					leadingWhitespace = true;
				}

				cursor += entityLength + 1;
				return true;
			}

			if (!AppendLiteral(decodedEntity[..charsWritten], destination, ref length, ref pendingWhitespace))
			{
				return false;
			}

			hasOutput = true;
			cursor += entityLength + 1;
			return true;
		}

		if (keepUnknownEntities && entityLength >= 0)
		{
			if (!AppendLiteral(source.Slice(cursor, entityLength + 1), destination, ref length, ref pendingWhitespace))
			{
				return false;
			}

			hasOutput = true;
			cursor += entityLength + 1;
			return true;
		}

		if (!AppendLiteral("&", destination, ref length, ref pendingWhitespace))
		{
			return false;
		}

		hasOutput = true;
		cursor++;
		return true;
	}

	private static int FindTokenStart(ReadOnlySpan<char> content, int start, SearchValues<char> tokens)
	{
		var index = content[start..].IndexOfAny(tokens);
		return index >= 0 ? start + index : -1;
	}

	private static int SkipWhitespaceRun(ReadOnlySpan<char> content, int start)
	{
		var index = content[start..].IndexOfAnyExcept(_normalizedWhitespace);
		return index >= 0 ? start + index : content.Length;
	}

	private static bool TryDecodeCharacterReference(
		ReadOnlySpan<char> source,
		int entityIndex,
		int entityLength,
		Span<char> decodedEntity,
		out int charsWritten)
	{
		if (entityLength < 0)
		{
			charsWritten = default;
			return false;
		}

		if (!CharacterReference.TryDecode(source.Slice(entityIndex, entityLength + 1), decodedEntity, out charsWritten))
		{
			charsWritten = default;
			return false;
		}

		return true;
	}

	private static bool AppendLiteral(ReadOnlySpan<char> literal, Span<char> destination, ref int length)
	{
		var available = destination.Length - length;
		if (available <= 0)
		{
			return false;
		}

		var copied = Math.Min(available, literal.Length);
		literal[..copied].CopyTo(destination[length..]);
		length += copied;
		return copied == literal.Length;
	}

	private static bool AppendLiteral(ReadOnlySpan<char> literal, Span<char> destination, ref int length, ref bool pendingWhitespace)
	{
		if (literal.IsEmpty)
		{
			return true;
		}

		if (pendingWhitespace)
		{
			if (length >= destination.Length)
			{
				return false;
			}

			destination[length] = ' ';
			length++;
			pendingWhitespace = false;
		}

		return AppendLiteral(literal, destination, ref length);
	}

	private static bool IsReferenceStart(char value)
	{
		return value == '#' || char.IsAsciiLetter(value);
	}

	private static bool IsWhitespaceCharacter(char value)
	{
		return IsHtmlWhitespace(value) || value == '\u00A0';
	}

	private static bool IsNonContentTag(ReadOnlySpan<char> name)
	{
		return name.Length switch
		{
			5 => name.Equals("style", StringComparison.OrdinalIgnoreCase),
			6 => name.Equals("script", StringComparison.OrdinalIgnoreCase),
			8 => name.Equals("textarea", StringComparison.OrdinalIgnoreCase) || name.Equals("template", StringComparison.OrdinalIgnoreCase),
			_ => false,
		};
	}

	private readonly record struct TextSegment(int Start, int Length, bool LeadingWhitespace, bool TrailingWhitespace)
	{
	}
}
