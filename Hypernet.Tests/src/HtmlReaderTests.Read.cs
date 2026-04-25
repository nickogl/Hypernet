namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Read_RecoversByEmittingLogicalEndTags()
	{
		var reader = HtmlReader.Create("<div><p>Hello</div>", _options);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.Equal("p", reader.TagName.ToString());
		Assert.Equal(2, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Text, reader.Kind);
		Assert.Equal("Hello", reader.TextNode.ToString());
		Assert.Equal(2, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("p", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_MatchesTagsCaseInsensitively()
	{
		var reader = HtmlReader.Create("<DIV><P>x</div>", _options);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal("DIV", reader.TagName.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal("P", reader.TagName.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal("x", reader.TextNode.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("P", reader.TagName.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("DIV", reader.TagName.ToString());
	}

	[Fact]
	public void Read_ImplicitlyClosesListItems()
	{
		var reader = HtmlReader.Create("<ul><li>One<li>Two</ul>", _options);

		AssertStartTag(ref reader, "ul", 1);
		AssertStartTag(ref reader, "li", 2);
		AssertText(ref reader, "One", 2);
		AssertEndTag(ref reader, "li", 1);
		AssertStartTag(ref reader, "li", 2);
		AssertText(ref reader, "Two", 2);
		AssertEndTag(ref reader, "li", 1);
		AssertEndTag(ref reader, "ul", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesDefinitionTermsAndDescriptions()
	{
		var reader = HtmlReader.Create("<dl><dt>Term<dt>Next</dl>", _options);

		AssertStartTag(ref reader, "dl", 1);
		AssertStartTag(ref reader, "dt", 2);
		AssertText(ref reader, "Term", 2);
		AssertEndTag(ref reader, "dt", 1);
		AssertStartTag(ref reader, "dt", 2);
		AssertText(ref reader, "Next", 2);
		AssertEndTag(ref reader, "dt", 1);
		AssertEndTag(ref reader, "dl", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesDefinitionDescriptions()
	{
		var reader = HtmlReader.Create("<dl><dd>First<dd>Second</dl>", _options);

		AssertStartTag(ref reader, "dl", 1);
		AssertStartTag(ref reader, "dd", 2);
		AssertText(ref reader, "First", 2);
		AssertEndTag(ref reader, "dd", 1);
		AssertStartTag(ref reader, "dd", 2);
		AssertText(ref reader, "Second", 2);
		AssertEndTag(ref reader, "dd", 1);
		AssertEndTag(ref reader, "dl", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_ImplicitlyClosesHeadings()
	{
		var reader = HtmlReader.Create("<div><h1>One<h2>Two</div>", _options);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "h1", 2);
		AssertText(ref reader, "One", 2);
		AssertEndTag(ref reader, "h1", 1);
		AssertStartTag(ref reader, "h2", 2);
		AssertText(ref reader, "Two", 2);
		AssertEndTag(ref reader, "h2", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_AllowsUnknownTagsToParticipateInNormalNesting()
	{
		var reader = HtmlReader.Create("<x-shell><x-item>hello</x-item></x-shell>", _options);

		AssertStartTag(ref reader, "x-shell", 1);
		AssertStartTag(ref reader, "x-item", 2);
		AssertText(ref reader, "hello", 2);
		AssertEndTag(ref reader, "x-item", 1);
		AssertEndTag(ref reader, "x-shell", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_TreatsUnterminatedCommentAsCommentToEndOfDocument()
	{
		var reader = HtmlReader.Create("<div><!--note", _options);

		AssertStartTag(ref reader, "div", 1);
		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Comment, reader.Kind);
		Assert.Equal("note", reader.Comment.ToString());
		Assert.Equal(1, reader.Depth);
		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_SkipsBogusMarkupAndContinuesParsing()
	{
		var reader = HtmlReader.Create("<div><?bogus?><!decl>ok</div>", _options);

		AssertStartTag(ref reader, "div", 1);
		AssertText(ref reader, "ok", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_TreatsInvalidMarkupPrefixAsSingleCharacterText()
	{
		var reader = HtmlReader.Create("<1", _options);

		AssertText(ref reader, "<", 0);
		AssertText(ref reader, "1", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_TreatsSelfClosingCustomTagAsNonPersistent()
	{
		var reader = HtmlReader.Create("<div><widget /></div>", _options);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "widget", 2);
		AssertEndTag(ref reader, "div", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_TreatsVoidElementsAsNonPersistentAndIgnoresStrayEndTags()
	{
		var reader = HtmlReader.Create("<div><input type=text>Hi</span></div>", _options);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.Equal("input", reader.TagName.ToString());
		Assert.Equal(2, reader.Depth);
		Assert.True(reader.TryGetAttribute("type", out var type));
		Assert.Equal("text", type.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Text, reader.Kind);
		Assert.Equal("Hi", reader.TextNode.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_SkipsUnmatchedEndTagAndContinuesParsing()
	{
		var reader = HtmlReader.Create("<div></span>ok</div>", _options);

		AssertStartTag(ref reader, "div", 1);
		AssertText(ref reader, "ok", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void Read_ThrowsWhenMaximumDepthIsExceeded()
	{
		var options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
			InitialDepthStackSize = 1,
			MaxDepth = 2,
		};

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("<a><b><c></c></b></a>", options);

			AssertStartTag(ref reader, "a", 1);
			AssertStartTag(ref reader, "b", 2);
			reader.Read();
		});
	}

	private static void AssertStartTag(ref HtmlReader reader, string expectedName, int expectedDepth)
	{
		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.Equal(expectedName, reader.TagName.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}

	private static void AssertEndTag(ref HtmlReader reader, string expectedName, int expectedDepth)
	{
		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal(expectedName, reader.TagName.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}

	private static void AssertText(ref HtmlReader reader, string expectedText, int expectedDepth)
	{
		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Text, reader.Kind);
		Assert.Equal(expectedText, reader.TextNode.ToString());
		Assert.Equal(expectedDepth, reader.Depth);
	}
}
