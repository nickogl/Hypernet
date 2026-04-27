namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Read_RecoversByEmittingLogicalEndTags()
	{
		using var content = HtmlContent.Create("<div><p>Hello</div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("p", reader.TagName.ToString());
		Assert.Equal(2, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Text, reader.Token);
		Assert.Equal("Hello", reader.TextNode.ToString());
		Assert.Equal(2, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("p", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_MatchesTagsCaseInsensitively()
	{
		using var content = HtmlContent.Create("<DIV><P>x</div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal("DIV", reader.TagName.ToString());

		Assert.True(reader.Read());
		Assert.Equal("P", reader.TagName.ToString());

		Assert.True(reader.Read());
		Assert.Equal("x", reader.TextNode.ToString());

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("P", reader.TagName.ToString());

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("DIV", reader.TagName.ToString());
	}

	[Fact]
	public void Read_ImplicitlyClosesListItems()
	{
		using var content = HtmlContent.Create("<ul><li>One<li>Two</ul>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "ul", 1);
		AssertStartTag(ref reader, "li", 2);
		AssertText(ref reader, "One", 2);
		AssertEndTag(ref reader, "li", 1);
		AssertStartTag(ref reader, "li", 2);
		AssertText(ref reader, "Two", 2);
		AssertEndTag(ref reader, "li", 1);
		AssertEndTag(ref reader, "ul", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesDefinitionTermsAndDescriptions()
	{
		using var content = HtmlContent.Create("<dl><dt>Term<dt>Next</dl>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "dl", 1);
		AssertStartTag(ref reader, "dt", 2);
		AssertText(ref reader, "Term", 2);
		AssertEndTag(ref reader, "dt", 1);
		AssertStartTag(ref reader, "dt", 2);
		AssertText(ref reader, "Next", 2);
		AssertEndTag(ref reader, "dt", 1);
		AssertEndTag(ref reader, "dl", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesDefinitionDescriptions()
	{
		using var content = HtmlContent.Create("<dl><dd>First<dd>Second</dl>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "dl", 1);
		AssertStartTag(ref reader, "dd", 2);
		AssertText(ref reader, "First", 2);
		AssertEndTag(ref reader, "dd", 1);
		AssertStartTag(ref reader, "dd", 2);
		AssertText(ref reader, "Second", 2);
		AssertEndTag(ref reader, "dd", 1);
		AssertEndTag(ref reader, "dl", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesHeadings()
	{
		using var content = HtmlContent.Create("<div><h1>One<h2>Two</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "h1", 2);
		AssertText(ref reader, "One", 2);
		AssertEndTag(ref reader, "h1", 1);
		AssertStartTag(ref reader, "h2", 2);
		AssertText(ref reader, "Two", 2);
		AssertEndTag(ref reader, "h2", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_AllowsUnknownTagsToParticipateInNormalNesting()
	{
		using var content = HtmlContent.Create("<x-shell><x-item>hello</x-item></x-shell>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "x-shell", 1);
		AssertStartTag(ref reader, "x-item", 2);
		AssertText(ref reader, "hello", 2);
		AssertEndTag(ref reader, "x-item", 1);
		AssertEndTag(ref reader, "x-shell", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_TreatsUnterminatedCommentAsCommentToEndOfDocument()
	{
		using var content = HtmlContent.Create("<div><!--note");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Comment, reader.Token);
		Assert.Equal("note", reader.Comment.ToString());
		Assert.Equal(1, reader.Depth);
		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_SkipsBogusMarkupAndContinuesParsing()
	{
		using var content = HtmlContent.Create("<div><?bogus?><!decl>ok</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		AssertText(ref reader, "ok", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_TreatsInvalidMarkupPrefixAsSingleCharacterText()
	{
		using var content = HtmlContent.Create("<1");
		var reader = new HtmlReader(content.Span);

		AssertText(ref reader, "<", 0);
		AssertText(ref reader, "1", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_TreatsSelfClosingCustomTagAsNonPersistent()
	{
		using var content = HtmlContent.Create("<div><widget /></div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "widget", 2);
		AssertEndTag(ref reader, "div", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_TreatsVoidElementsAsNonPersistentAndIgnoresStrayEndTags()
	{
		using var content = HtmlContent.Create("<div><input type=text>Hi</span></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("input", reader.TagName.ToString());
		Assert.Equal(2, reader.Depth);
		Assert.True(reader.TryGetAttribute("type", out var type));
		Assert.Equal("text", type.ToString());

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Text, reader.Token);
		Assert.Equal("Hi", reader.TextNode.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_SkipsUnmatchedEndTagAndContinuesParsing()
	{
		using var content = HtmlContent.Create("<div></span>ok</div>");
		var reader = new HtmlReader(content.Span);

		AssertStartTag(ref reader, "div", 1);
		AssertText(ref reader, "ok", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.False(reader.Read());
	}

	[Fact]
	public void Read_ThrowsWhenMaximumDepthIsExceeded()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<a><b><c></c></b></a>");
			var reader = new HtmlReader(content.Span, new HtmlReaderOptions() { MaxDepth = 2 });

			AssertStartTag(ref reader, "a", 1);
			AssertStartTag(ref reader, "b", 2);
			reader.Read();
		});
	}

	private static void AssertStartTag(ref HtmlReader reader, string expectedName, int expectedDepth)
	{
		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal(expectedName, reader.TagName.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}

	private static void AssertEndTag(ref HtmlReader reader, string expectedName, int expectedDepth)
	{
		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal(expectedName, reader.TagName.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}

	private static void AssertText(ref HtmlReader reader, string expectedText, int expectedDepth)
	{
		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Text, reader.Token);
		Assert.Equal(expectedText, reader.TextNode.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}
}
