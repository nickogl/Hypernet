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

		var traversalState = new TextTraversalState(options, _depth - 1, IsNonContentTag(_currentData));
		var destinationStart = _position;

		// Segments are stored as source-relative slices first because the method rewrites
		// the same buffer it is reading from, which could corrupt the open tag stack. Most
		// documents will not pool any memory for this, so this is still fairly cheap.
		Span<TextSegment> inlineSegmentStorage = stackalloc TextSegment[_options.InitialTextContentSegmentSize];
		using var segments = new ScratchBuffer<TextSegment>(inlineSegmentStorage);
		while (Read())
		{
			if (traversalState.ShouldCapture(_token))
			{
				if (TryCaptureCurrentTokenAsSegment(ref traversalState, out var segment))
				{
					segments.Push(segment, _options.MaxTextContentSegmentSize);
				}
			}

			if (ShouldStopTraversal(ref traversalState, _token, _currentData, _depth))
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

		var traversalState = new TextTraversalState(options, _depth - 1, IsNonContentTag(_currentData));
		var rewriteState = new TextRewriteState(traversalState.NormalizeWhitespace);
		var truncated = false;
		while (Read())
		{
			if (!truncated && traversalState.ShouldCapture(_token))
			{
				var completed = TryRewriteContent(_currentData, destination, traversalState.KeepUnknownEntities, ref rewriteState);
				truncated = !completed;
			}

			if (ShouldStopTraversal(ref traversalState, _token, _currentData, _depth))
			{
				break;
			}
		}

		charsWritten = rewriteState.Length;
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

	private static bool ShouldStopTraversal(ref TextTraversalState state, HtmlToken token, ReadOnlySpan<char> tagName, int depth)
	{
		state.Advance(token, tagName, depth, out var shouldStopTraversal);
		return shouldStopTraversal;
	}

	private readonly bool TryCaptureCurrentTokenAsSegment(ref TextTraversalState state, out TextSegment segment)
	{
		var rewriteState = new TextRewriteState(state.NormalizeWhitespace);
		_ = TryRewriteContent(_currentData, _currentData, state.KeepUnknownEntities, ref rewriteState);
		var written = rewriteState.Length;
		var leadingWhitespace = rewriteState.LeadingWhitespace;
		if ((written > 0 || leadingWhitespace) && _data.Overlaps(_currentData, out var offset))
		{
			segment = new TextSegment(offset, written, leadingWhitespace, rewriteState.TrailingWhitespace);
			return true;
		}

		segment = default;
		return false;
	}

	private static bool TryRewriteContent(
		ReadOnlySpan<char> source,
		scoped Span<char> destination,
		bool keepUnknownEntities,
		scoped ref TextRewriteState state)
	{
		var cursor = 0;
		var tokenStart = state.TokenStart;
		while (cursor < source.Length)
		{
			var tokenIndex = FindTokenStart(source, cursor, tokenStart);
			if (tokenIndex < 0)
			{
				if (!state.TryAppendLiteral(source[cursor..], destination))
				{
					return false;
				}

				if (state.Length > 0)
				{
					state.MarkHasOutput();
				}

				return true;
			}

			if (state.NormalizeWhitespace)
			{
				if (!state.TryAppendLiteral(source[cursor..tokenIndex], destination))
				{
					return false;
				}
				if (tokenIndex > cursor)
				{
					state.MarkHasOutput();
				}

				cursor = tokenIndex;
				if (source[cursor] != '&')
				{
					cursor = SkipWhitespaceRun(source, cursor);
					state.MarkWhitespaceRun();
					continue;
				}
			}
			else
			{
				if (!state.TryAppendLiteral(source[cursor..tokenIndex], destination))
				{
					return false;
				}
				cursor = tokenIndex;
			}

			if (!TryAppendCharacterReference(source, destination, keepUnknownEntities, ref cursor, ref state))
			{
				return false;
			}
		}

		return true;
	}

	private static bool TryAppendCharacterReference(
		ReadOnlySpan<char> source,
		scoped Span<char> destination,
		bool keepUnknownEntities,
		ref int cursor,
		scoped ref TextRewriteState state)
	{
		if (cursor + 1 >= source.Length || !IsReferenceStart(source[cursor + 1]))
		{
			if (!state.TryAppendLiteral("&", destination))
			{
				return false;
			}
			state.MarkHasOutput();
			cursor++;
			return true;
		}

		Span<char> decodedEntity = stackalloc char[4];
		var entityLength = source[cursor..].IndexOf(';');
		if (entityLength >= 0 && TryDecodeCharacterReference(source, cursor, entityLength, decodedEntity, out var charsWritten))
		{
			if (state.NormalizeWhitespace && charsWritten == 1 && IsWhitespaceCharacter(decodedEntity[0]))
			{
				state.MarkWhitespaceRun();
				cursor += entityLength + 1;
				return true;
			}

			if (!state.TryAppendLiteral(decodedEntity[..charsWritten], destination))
			{
				return false;
			}
			state.MarkHasOutput();
			cursor += entityLength + 1;
			return true;
		}

		if (keepUnknownEntities && entityLength >= 0)
		{
			if (!state.TryAppendLiteral(source.Slice(cursor, entityLength + 1), destination))
			{
				return false;
			}
			state.MarkHasOutput();
			cursor += entityLength + 1;
			return true;
		}

		if (!state.TryAppendLiteral("&", destination))
		{
			return false;
		}
		state.MarkHasOutput();
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

	private ref struct TextTraversalState
	{
		private readonly bool _includeComments;
		private readonly bool _includeNonContentText;
		private readonly int _targetDepth;
		private int _ignoredDepth;

		public TextTraversalState(HtmlTextContentOptions options, int targetDepth, bool startsInIgnoredSubtree)
		{
			_includeComments = (options & HtmlTextContentOptions.IncludeComments) != 0;
			_includeNonContentText = (options & HtmlTextContentOptions.IncludeNonContentText) != 0;
			NormalizeWhitespace = (options & HtmlTextContentOptions.NormalizeWhitespace) != 0;
			KeepUnknownEntities = (options & HtmlTextContentOptions.KeepUnknownEntities) != 0;
			_targetDepth = targetDepth;
			_ignoredDepth = !_includeNonContentText && startsInIgnoredSubtree
				? targetDepth
				: -1;
		}

		public readonly bool NormalizeWhitespace { get; }
		public readonly bool KeepUnknownEntities { get; }

		public readonly bool ShouldCapture(HtmlToken token)
		{
			return _ignoredDepth < 0
				&& (token == HtmlToken.Text || token == HtmlToken.Comment && _includeComments);
		}

		public void Advance(HtmlToken token, ReadOnlySpan<char> tagName, int depth, out bool shouldStopTraversal)
		{
			shouldStopTraversal = false;

			if (_ignoredDepth < 0 && !_includeNonContentText && token == HtmlToken.StartTag && IsNonContentTag(tagName))
			{
				_ignoredDepth = depth - 1;
			}

			if (token != HtmlToken.EndTag)
			{
				return;
			}

			if (depth == _ignoredDepth)
			{
				_ignoredDepth = -1;
			}

			shouldStopTraversal = depth == _targetDepth;
		}
	}

	private ref struct TextRewriteState
	{
		private readonly bool _normalizeWhitespace;
		private int _length;
		private bool _pendingWhitespace;
		private bool _hasOutput;
		private bool _leadingWhitespace;
		private readonly SearchValues<char> _tokenStart;

		public TextRewriteState(bool normalizeWhitespace)
		{
			_normalizeWhitespace = normalizeWhitespace;
			_tokenStart = normalizeWhitespace ? _normalizedContentTokenStart : _characterReferenceStart;
		}

		public readonly int Length => _length;
		public readonly bool HasOutput => _hasOutput;
		public readonly bool LeadingWhitespace => _leadingWhitespace;
		public readonly SearchValues<char> TokenStart => _tokenStart;
		public readonly bool NormalizeWhitespace => _normalizeWhitespace;
		public readonly bool TrailingWhitespace => _pendingWhitespace;

		public void MarkHasOutput()
		{
			_hasOutput = true;
		}

		public void MarkWhitespaceRun()
		{
			if (_hasOutput)
			{
				_pendingWhitespace = true;
			}
			else
			{
				_leadingWhitespace = true;
			}
		}

		public bool TryAppendLiteral(scoped ReadOnlySpan<char> literal, scoped Span<char> destination)
		{
			if (literal.IsEmpty)
			{
				return true;
			}

			if (_normalizeWhitespace && _pendingWhitespace)
			{
				if (_length >= destination.Length)
				{
					return false;
				}

				destination[_length] = ' ';
				_length++;
				_pendingWhitespace = false;
			}

			var available = destination.Length - _length;
			if (available <= 0)
			{
				return false;
			}

			var copied = Math.Min(available, literal.Length);
			literal[..copied].CopyTo(destination[_length..]);
			_length += copied;
			return copied == literal.Length;
		}
	}
}
