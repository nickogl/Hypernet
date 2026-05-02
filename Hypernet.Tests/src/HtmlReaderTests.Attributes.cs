namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Read_EnumeratesStartTagAttributesTextAndEndTag()
	{
		using var content = HtmlContent.Create("<div class=\"hero\" disabled>hi</div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(1, reader.Depth);
		Assert.True(reader.TryGetAttribute("class", out var classValue));
		Assert.Equal("hero", classValue.ToString());
		Assert.True(reader.TryGetAttribute("disabled", out var disabledValue));
		Assert.True(disabledValue.IsEmpty);
		Assert.False(reader.TryGetAttribute("missing", out _));

		var attributes = new List<string>();
		foreach (var attribute in reader.Attributes)
		{
			attributes.Add($"{attribute.Name}={attribute.Value}");
		}
		Assert.Equal(["class=hero", "disabled="], attributes);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Text, reader.Token);
		Assert.Equal("hi", reader.Text.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.EndTag, reader.Token);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.False(reader.Read());
	}

	[Fact]
	public void TryGetAttribute_MatchesCaseInsensitively_AndReturnsFirstDuplicate()
	{
		using var content = HtmlContent.Create("<div CLASS=hero class=shadow></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("class", out var value));
		Assert.Equal("hero", value.ToString());
	}

	[Fact]
	public void TryGetAttribute_DoesNotMatchSuffixInsideOtherAttributeName()
	{
		using var content = HtmlContent.Create("<div data-id=42 id=7></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("id", out var value));
		Assert.Equal("7", value.ToString());
	}

	[Fact]
	public void Attributes_YieldSourceOrder_AndExplicitEmptyValue()
	{
		using var content = HtmlContent.Create("<div c='3' a b=\"\" d=text></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);

		var attributes = new List<string>();
		foreach (var attribute in reader.Attributes)
		{
			attributes.Add($"{attribute.Name.ToString()}={attribute.Value.ToString()}");
		}

		Assert.Equal(["c=3", "a=", "b=", "d=text"], attributes);
		Assert.True(reader.TryGetAttribute("b", out var emptyValue));
		Assert.True(emptyValue.IsEmpty);
	}

	[Fact]
	public void Attributes_EnumerateEmptySequence_WhenStartTagHasNoAttributes()
	{
		using var content = HtmlContent.Create("<div></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);

		var attributes = new List<string>();
		foreach (var attribute in reader.Attributes)
		{
			attributes.Add(attribute.Name.ToString());
		}

		Assert.Empty(attributes);
	}

	[Fact]
	public void Read_AllowsQuotedAttributeValuesContainingMarkupTerminators()
	{
		using var content = HtmlContent.Create("<div data-value=\"1>2\">ok</div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("data-value", out var value));
		Assert.Equal("1>2", value.ToString());

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.Text, reader.Token);
		Assert.Equal("ok", reader.Text.ToString());
	}

	[Fact]
	public void TryGetAttribute_UnquotedValue_CanContainSlash()
	{
		using var content = HtmlContent.Create("<a href=https://example.com/path></a>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("href", out var value));
		Assert.Equal("https://example.com/path", value.ToString());
	}

	[Fact]
	public void TryGetAttribute_IgnoresNameLikeTextInsideQuotedAttributeValues()
	{
		using var content = HtmlContent.Create("<div data-note=\"id=shadow\" id=real></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("id", out var value));
		Assert.Equal("real", value.ToString());
	}

	[Fact]
	public void TryGetAttribute_SupportsWhitespaceAroundEquals()
	{
		using var content = HtmlContent.Create("<div class = \"hero\"></div>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);
		Assert.True(reader.TryGetAttribute("class", out var value));
		Assert.Equal("hero", value.ToString());
	}

	[Fact]
	public void Attributes_EnumerateUnquotedSlashValueWithoutTruncation()
	{
		using var content = HtmlContent.Create("<a href=https://example.com/path data-x=1></a>");
		var reader = new HtmlReader(content.Span);

		Assert.True(reader.Read());
		Assert.Equal(HtmlToken.StartTag, reader.Token);

		var attributes = new List<string>();
		foreach (var attribute in reader.Attributes)
		{
			attributes.Add($"{attribute.Name}={attribute.Value}");
		}

		Assert.Equal(["href=https://example.com/path", "data-x=1"], attributes);
	}

	[Fact]
	public void Attributes_And_TryGetAttribute_Throw_WhenCurrentEntityIsNotStartTag()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Text, reader.Token);
			reader.TryGetAttribute("class", out _);
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Text, reader.Token);
			foreach (var attribute in reader.Attributes)
			{
				_ = attribute;
			}
		});
	}
}
