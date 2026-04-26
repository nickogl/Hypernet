namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void GetTextContent_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("text", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			_ = reader.GetTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("<div>text</div>", _options);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			_ = reader.GetTextContent();
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("<div>text</div>", _options);

			AssertStartTag(ref reader, "div", 1);
			AssertText(ref reader, "text", 1);
			AssertEndTag(ref reader, "div", 0);
			_ = reader.GetTextContent();
		});
	}

	[Fact]
	public void GetTextContent_ConsumesSubtreeAndLeavesReaderOnMatchingEndTag()
	{
		var reader = HtmlReader.Create("<div class=\"card\">Hello <span class=\"emphasis\">world</span>!</div><p>next</p>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("Hello world!", reader.GetTextContent());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
		Assert.Equal(0, reader.Depth);

		AssertStartTag(ref reader, "p", 1);
		AssertText(ref reader, "next", 1);
		AssertEndTag(ref reader, "p", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void GetTextContent_DecodesHtmlCharacterReferences()
	{
		var reader = HtmlReader.Create("<div>&amp;&nbsp;&#65;&#x42;&NotEqualTilde;</div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("&\u00A0AB\u2242\u0338", reader.GetTextContent());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_PreservesUnknownCharacterReferences()
	{
		var reader = HtmlReader.Create("<div>a&bogus; b&#xZZ; c</div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&bogus; b&#xZZ; c", reader.GetTextContent(HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_NormalizesWhitespaceWhenRequested()
	{
		var reader = HtmlReader.Create("<div>  A\t&nbsp;<span>\r\nB</span>   C  </div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A B C", reader.GetTextContent(HtmlTextContentOptions.NormalizeWhitespace));
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesCommentsByDefault()
	{
		var reader = HtmlReader.Create("<div>a<!--&amp;--><span>b</span></div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetTextContent());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesCommentsWhenRequested()
	{
		var reader = HtmlReader.Create("<div>a<!--&amp;--><span>b</span></div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("a&b", reader.GetTextContent(HtmlTextContentOptions.IncludeComments));
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ExcludesNonContentTextByDefault()
	{
		var reader = HtmlReader.Create("<div>a<script>x</script><style>y</style><textarea>z</textarea><template>q</template>b</div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ab", reader.GetTextContent());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_IncludesNonContentTextWhenRequested()
	{
		var reader = HtmlReader.Create("<div>a<script>x&amp;</script><style>y</style><textarea>z</textarea><template>q</template>b</div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("ax&yzqb", reader.GetTextContent(HtmlTextContentOptions.IncludeNonContentText));
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_CanCombineAllOptions()
	{
		var reader = HtmlReader.Create("<div>  A<!-- &amp; --><script>\nB&nbsp;</script>  C&amp;  </div>", _options);

		AssertStartTag(ref reader, "div", 1);

		Assert.Equal("A & B C&", reader.GetTextContent(
			HtmlTextContentOptions.NormalizeWhitespace
			| HtmlTextContentOptions.IncludeComments
			| HtmlTextContentOptions.IncludeNonContentText
			| HtmlTextContentOptions.KeepUnknownEntities));
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName);
	}

	[Fact]
	public void GetTextContent_ReturnsEmptyForVoidElementAndLeavesLogicalEndTag()
	{
		var reader = HtmlReader.Create("<div><input value=test>after</div>", _options);

		AssertStartTag(ref reader, "div", 1);
		AssertStartTag(ref reader, "input", 2);

		Assert.Equal(string.Empty, reader.GetTextContent());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("input", reader.TagName);
		Assert.Equal(1, reader.Depth);

		AssertText(ref reader, "after", 1);
		AssertEndTag(ref reader, "div", 0);
		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}
}
