using System.Buffers;

namespace Hypernet;

public ref partial struct HtmlReader
{
	private static readonly SearchValues<char> _characterReferenceStart = SearchValues.Create("&");

	/// <summary>
	/// Consumes the current element subtree and returns its extracted text content.
	/// </summary>
	/// <param name="options">Controls which textual sources are included and whether whitespace is normalized.</param>
	/// <returns>The extracted text content for the current element subtree.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the current entity kind is not <see cref="HtmlEntityKind.StartTag" />.</exception>
	public ReadOnlySpan<char> GetTextContent(HtmlTextContentOptions options = HtmlTextContentOptions.None)
	{
		ThrowIfUnexpectedEntity(HtmlEntityKind.StartTag);
		if (!IsPersistentStartTag())
		{
			SetCurrentEntity(HtmlEntityKind.EndTag, _depth - 1, _currentData);
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
		var segments = ArrayPool<TextSegment>.Shared.Rent(8);
		var segmentCount = 0;

		try
		{
			while (Read() == HtmlReadResult.Node)
			{
				if (ignoredDepth < 0 && _kind == HtmlEntityKind.StartTag && !includeNonContentText && IsNonContentTag(_currentData))
				{
					ignoredDepth = _depth - 1;
				}
				else if (ignoredDepth < 0)
				{
					if (_kind == HtmlEntityKind.Text || _kind == HtmlEntityKind.Comment && includeComments)
					{
						var written = RewriteContentInPlace(_currentData, keepUnknownEntities);
						if (written > 0)
						{
							if (_data.Overlaps(_currentData, out var offset))
							{
								AddSegment(ref segments, ref segmentCount, new TextSegment(offset, written));
							}
						}
					}
				}

				if (_kind == HtmlEntityKind.EndTag && _depth == ignoredDepth)
				{
					ignoredDepth = -1;
				}

				if (_kind == HtmlEntityKind.EndTag && _depth == targetDepth)
				{
					break;
				}
			}

			var destinationCursor = destinationStart;
			for (var i = 0; i < segmentCount; i++)
			{
				var segment = segments[i];
				_data.Slice(segment.Start, segment.Length).CopyTo(_data[destinationCursor..]);
				destinationCursor += segment.Length;
			}

			var result = _data.Slice(destinationStart, destinationCursor - destinationStart);
			if (normalizeWhitespace)
			{
				return result[..NormalizeWhitespaceInPlace(result)];
			}

			return result;
		}
		finally
		{
			ArrayPool<TextSegment>.Shared.Return(segments);
		}
	}

	private readonly bool IsPersistentStartTag()
	{
		return _stack.Count > 0
			&& _depth == _stack.Count
			&& GetOpenTagName(_stack.Count - 1).Equals(_currentData, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsContentWhitespace(char value)
	{
		return IsHtmlWhitespace(value) || value == '\u00A0';
	}

	private static int RewriteContentInPlace(Span<char> source, bool keepUnknownEntities)
	{
		Span<char> decodedEntity = stackalloc char[4];
		var length = 0;
		var cursor = 0;
		while (cursor < source.Length)
		{
			var entityIndex = source[cursor..].IndexOfAny(_characterReferenceStart);
			if (entityIndex < 0)
			{
				Append(source[cursor..], source, ref length);
				return length;
			}

			entityIndex += cursor;
			Append(source[cursor..entityIndex], source, ref length);

			var entityLength = source[entityIndex..].IndexOf(';');
			if (entityLength >= 0
				&& HtmlCharacterReference.TryDecode(source.Slice(entityIndex, entityLength + 1), decodedEntity, out var charsWritten))
			{
				Append(decodedEntity[..charsWritten], source, ref length);
				cursor = entityIndex + entityLength + 1;
				continue;
			}

			if (keepUnknownEntities)
			{
				if (entityLength >= 0)
				{
					Append(source.Slice(entityIndex, entityLength + 1), source, ref length);
					cursor = entityIndex + entityLength + 1;
				}
				else
				{
					Append('&', source, ref length);
					cursor = entityIndex + 1;
				}
			}
			else
			{
				Append('&', source, ref length);
				cursor = entityIndex + 1;
			}
		}

		return length;
	}

	private static int NormalizeWhitespaceInPlace(Span<char> content)
	{
		var length = 0;
		var pendingWhitespace = false;
		foreach (var value in content)
		{
			if (IsContentWhitespace(value))
			{
				if (length > 0)
				{
					pendingWhitespace = true;
				}

				continue;
			}

			if (pendingWhitespace)
			{
				content[length] = ' ';
				length++;
				pendingWhitespace = false;
			}

			content[length] = value;
			length++;
		}

		return length;
	}

	private static void Append(ReadOnlySpan<char> source, Span<char> destination, ref int length)
	{
		foreach (var value in source)
		{
			Append(value, destination, ref length);
		}
	}

	private static void Append(char value, Span<char> destination, ref int length)
	{
		destination[length] = value;
		length++;
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

	private static void AddSegment(ref TextSegment[] segments, ref int count, TextSegment segment)
	{
		if (count == segments.Length)
		{
			var newSegments = ArrayPool<TextSegment>.Shared.Rent(segments.Length * 2);
			Array.Copy(segments, newSegments, count);
			ArrayPool<TextSegment>.Shared.Return(segments);
			segments = newSegments;
		}

		segments[count] = segment;
		count++;
	}

	private readonly struct TextSegment
	{
		public TextSegment(int start, int length)
		{
			Start = start;
			Length = length;
		}

		public int Start { get; }
		public int Length { get; }
	}
}
