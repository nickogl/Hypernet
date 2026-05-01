namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void GetTextContent_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			_ = reader.GetDangerousTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>text</div>");
			var reader = new HtmlReader(content.Span);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			_ = reader.GetDangerousTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>text</div>");
			var reader = new HtmlReader(content.Span);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			AssertEndTag(ref reader, "div", 0);
			_ = reader.GetDangerousTextContent();
		});
	}

	[Fact]
	public void GetTextContent_ConsumesSubtreeAndLeavesReaderOnMatchingEndTag()
	{
		using var content = HtmlContent.Create("<div class=\"card\">Hello <span class=\"emphasis\">world</span>!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("Hello world!", reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void TryGetTextContent_DecodesHtmlCharacterReferences()
	{
		using var content = HtmlContent.Create("<div>&amp;&nbsp;&#65;&#x42;&NotEqualTilde;</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[16];
		Assert.True(reader.TryGetTextContent(destination, HtmlTextContentOptions.None, out var charsWritten));
		Assert.Equal("&\u00A0AB\u2242\u0338", destination[..charsWritten].ToString());
		Assert.Equal(6, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void TryGetTextContent_PreservesUnknownCharacterReferences_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a&bogus; b&#xZZ; c</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[32];
		Assert.True(reader.TryGetTextContent(destination, HtmlTextContentOptions.KeepUnknownEntities, out var charsWritten));
		Assert.Equal("a&bogus; b&#xZZ; c", destination[..charsWritten].ToString());
		Assert.Equal(18, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void TryGetTextContent_NormalizesWhitespace_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>  A\t&nbsp;<span>\r\nB</span>   C  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[16];
		Assert.True(reader.TryGetTextContent(destination, HtmlTextContentOptions.NormalizeWhitespace, out var charsWritten));
		Assert.Equal("A B C", destination[..charsWritten].ToString());
		Assert.Equal(5, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void TryGetTextContent_CombinesOptions_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>  A<!-- &amp; --><script>\nB&nbsp;</script>  C&amp;  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[32];
		Assert.True(reader.TryGetTextContent(
			destination,
			HtmlTextContentOptions.NormalizeWhitespace
			| HtmlTextContentOptions.IncludeComments
			| HtmlTextContentOptions.IncludeNonContentText
			| HtmlTextContentOptions.KeepUnknownEntities,
			out var charsWritten));
		Assert.Equal("A & B C&", destination[..charsWritten].ToString());
		Assert.Equal(8, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void TryGetTextContent_PreservesDanglingOrUnterminatedCharacterReferences_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a& b&bogus; c&amp d</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[32];
		Assert.True(reader.TryGetTextContent(destination, HtmlTextContentOptions.KeepUnknownEntities, out var charsWritten));
		Assert.Equal("a& b&bogus; c&amp d", destination[..charsWritten].ToString());
		Assert.Equal(19, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_DecodesHtmlCharacterReferences()
	{
		using var content = HtmlContent.Create("<div>&amp;&nbsp;&#65;&#x42;&NotEqualTilde;</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("&\u00A0AB\u2242\u0338", reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_PreservesUnknownCharacterReferences()
	{
		using var content = HtmlContent.Create("<div>a&bogus; b&#xZZ; c</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&bogus; b&#xZZ; c", reader.GetDangerousTextContent(HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_NormalizesWhitespace_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>  A\t&nbsp;<span>\r\nB</span>   C  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A B C", reader.GetDangerousTextContent(HtmlTextContentOptions.NormalizeWhitespace));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesCommentsByDefault()
	{
		using var content = HtmlContent.Create("<div>a<!--&amp;--><span>b</span></div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesComments_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a<!--&amp;--><span>b</span></div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&b", reader.GetDangerousTextContent(HtmlTextContentOptions.IncludeComments));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesNonContentTextByDefault()
	{
		using var content = HtmlContent.Create("<div>a<script>x</script><style>y</style><textarea>z</textarea><template>q</template>b</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesNonContentText_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a<script>x&amp;</script><style>y</style><textarea>z</textarea><template>q</template>b</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ax&yzqb", reader.GetDangerousTextContent(HtmlTextContentOptions.IncludeNonContentText));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_CanCombineAllOptions()
	{
		using var content = HtmlContent.Create("<div>  A<!-- &amp; --><script>\nB&nbsp;</script>  C&amp;  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A & B C&", reader.GetDangerousTextContent(
			HtmlTextContentOptions.NormalizeWhitespace
			| HtmlTextContentOptions.IncludeComments
			| HtmlTextContentOptions.IncludeNonContentText
			| HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ReturnsEmptyForVoidElementAndLeavesLogicalEndTag()
	{
		using var content = HtmlContent.Create("<div><input value=test>after</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "input", 2);

		Assert.Equal(string.Empty, reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("input", reader.TagName);
		Assert.Equal(1, reader.Depth);

		AssertText(ref reader, "after", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void TryGetTextContent_WritesFullContent_WhenDestinationIsLargeEnough()
	{
		using var content = HtmlContent.Create("<div>Hello <span>world</span>!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[16];
		Assert.True(reader.TryGetTextContent(destination, out var charsWritten));
		Assert.Equal("Hello world!", destination[..charsWritten].ToString());
		Assert.Equal(12, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void TryGetTextContent_ReturnsFalse_WhenDestinationIsTooSmall()
	{
		using var content = HtmlContent.Create("<div>Hello world!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[5];
		Assert.False(reader.TryGetTextContent(destination, out var charsWritten));
		Assert.Equal("Hello", destination[..charsWritten].ToString());
		Assert.Equal(5, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void TryGetTextContent_ReturnsFalse_WhenDestinationIsTooSmall_AndTextContainsEntity()
	{
		using var content = HtmlContent.Create("<div>&amp;abc</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[2];
		Assert.False(reader.TryGetTextContent(destination, HtmlTextContentOptions.None, out var charsWritten));
		Assert.Equal("&a", destination[..charsWritten].ToString());
		Assert.Equal(2, charsWritten);
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void GetTextContent_HandlesMoreSegmentsThanInitialSegmentStorage()
	{
		var innerHtml = string.Concat(Enumerable.Range(0, 40).Select(i => $"{i}<span></span>"));
		var expectedText = string.Concat(Enumerable.Range(0, 40).Select(i => i.ToString()));

		using var content = HtmlContent.Create($"<div>{innerHtml}</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal(expectedText, reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);
		Assert.False(reader.Read());
	}

	[Fact]
	public void GetTextContent_HandlesRecoveredMissingEndTags()
	{
		using var content = HtmlContent.Create("<div><p>Hello</div><span>next</span>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("Hello", reader.GetDangerousTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "span", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "span", 0);
		Assert.False(reader.Read());
	}

	[Theory]
	[InlineData("<div>a& b</div>", "a& b")]
	[InlineData("<div>a&bogus b</div>", "a&bogus b")]
	[InlineData("<div>a&amp b</div>", "a&amp b")]
	public void GetTextContent_PreservesDanglingOrUnterminatedCharacterReferences_WhenRequested(
		string html,
		string expectedText)
	{
		using var content = HtmlContent.Create(html);
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal(expectedText, reader.GetDangerousTextContent(HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
	}

	[Fact]
	public void TryPeekTextContent_PreservesReaderState()
	{
		using var content = HtmlContent.Create("<div class=\"card\">Hello <span class=\"emphasis\">world</span>!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[16];
		Assert.True(reader.TryPeekTextContent(destination, out var charsWritten));
		Assert.Equal("Hello world!", destination[..charsWritten].ToString());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.TryGetTextContent(destination, out var consumedCharsWritten));
		Assert.Equal(charsWritten, consumedCharsWritten);
		Assert.Equal("Hello world!", destination[..consumedCharsWritten].ToString());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void TryPeekTextContent_RestoresReaderState_WhenDestinationIsTooSmall()
	{
		using var content = HtmlContent.Create("<div>Hello world!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Span<char> destination = stackalloc char[5];
		Assert.False(reader.TryPeekTextContent(destination, out var charsWritten));
		Assert.Equal("Hello", destination[..charsWritten].ToString());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(1, reader.Depth);

		Span<char> fullDestination = stackalloc char[16];
		Assert.True(reader.TryGetTextContent(fullDestination, out var fullCharsWritten));
		Assert.Equal("Hello world!", fullDestination[..fullCharsWritten].ToString());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}
}
