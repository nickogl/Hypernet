namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Read_EnumeratesStartTagAttributesTextAndEndTag()
	{
		using var content = HtmlContent.Create("<div class=\"hero\" disabled>hi</div>");
		var reader = new HtmlReader(content.Span);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
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

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Text, reader.Kind);
		Assert.Equal("hi", reader.TextNode.ToString());
		Assert.Equal(1, reader.Depth);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.EndTag, reader.Kind);
		Assert.Equal("div", reader.TagName.ToString());
		Assert.Equal(0, reader.Depth);

		Assert.Equal(HtmlReadResult.EndOfDocument, reader.Read());
	}

	[Fact]
	public void TryGetAttribute_MatchesCaseInsensitively_AndReturnsFirstDuplicate()
	{
		using var content = HtmlContent.Create("<div CLASS=hero class=shadow></div>");
		var reader = new HtmlReader(content.Span);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.True(reader.TryGetAttribute("class", out var value));
		Assert.Equal("hero", value.ToString());
	}

	[Fact]
	public void Attributes_YieldSourceOrder_AndExplicitEmptyValue()
	{
		using var content = HtmlContent.Create("<div c='3' a b=\"\" d=text></div>");
		var reader = new HtmlReader(content.Span);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);

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

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);

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

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.True(reader.TryGetAttribute("data-value", out var value));
		Assert.Equal("1>2", value.ToString());

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.Text, reader.Kind);
		Assert.Equal("ok", reader.TextNode.ToString());
	}

	[Fact]
	public void Attributes_And_TryGetAttribute_Throw_WhenCurrentEntityIsNotStartTag()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			reader.TryGetAttribute("class", out _);
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			foreach (var attribute in reader.Attributes)
			{
				_ = attribute;
			}
		});
	}
}
