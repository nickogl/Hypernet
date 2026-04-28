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
			_ = reader.GetTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>text</div>");
			var reader = new HtmlReader(content.Span);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			_ = reader.GetTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>text</div>");
			var reader = new HtmlReader(content.Span);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			AssertEndTag(ref reader, "div", 0);
			_ = reader.GetTextContent();
		});
	}

	[Fact]
	public void GetTextContent_ConsumesSubtreeAndLeavesReaderOnMatchingEndTag()
	{
		using var content = HtmlContent.Create("<div class=\"card\">Hello <span class=\"emphasis\">world</span>!</div><p>next</p>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("Hello world!", reader.GetTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void GetTextContent_DecodesHtmlCharacterReferences()
	{
		using var content = HtmlContent.Create("<div>&amp;&nbsp;&#65;&#x42;&NotEqualTilde;</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("&\u00A0AB\u2242\u0338", reader.GetTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_PreservesUnknownCharacterReferences()
	{
		using var content = HtmlContent.Create("<div>a&bogus; b&#xZZ; c</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&bogus; b&#xZZ; c", reader.GetTextContent(HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_NormalizesWhitespace_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>  A\t&nbsp;<span>\r\nB</span>   C  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A B C", reader.GetTextContent(HtmlTextContentOptions.NormalizeWhitespace));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesCommentsByDefault()
	{
		using var content = HtmlContent.Create("<div>a<!--&amp;--><span>b</span></div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesComments_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a<!--&amp;--><span>b</span></div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&b", reader.GetTextContent(HtmlTextContentOptions.IncludeComments));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesNonContentTextByDefault()
	{
		using var content = HtmlContent.Create("<div>a<script>x</script><style>y</style><textarea>z</textarea><template>q</template>b</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesNonContentText_WhenRequested()
	{
		using var content = HtmlContent.Create("<div>a<script>x&amp;</script><style>y</style><textarea>z</textarea><template>q</template>b</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ax&yzqb", reader.GetTextContent(HtmlTextContentOptions.IncludeNonContentText));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_CanCombineAllOptions()
	{
		using var content = HtmlContent.Create("<div>  A<!-- &amp; --><script>\nB&nbsp;</script>  C&amp;  </div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A & B C&", reader.GetTextContent(
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

		Assert.Equal(string.Empty, reader.GetTextContent());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("input", reader.TagName);
		Assert.Equal(1, reader.Depth);

		AssertText(ref reader, "after", 1);
		AssertEndTag(ref reader, "div", 0);
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

		Assert.Equal(expectedText, reader.GetTextContent());
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

		Assert.Equal("Hello", reader.GetTextContent());
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

		Assert.Equal(expectedText, reader.GetTextContent(HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
	}
}
