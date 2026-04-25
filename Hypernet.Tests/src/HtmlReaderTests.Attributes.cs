namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Read_EnumeratesStartTagAttributesTextAndEndTag()
	{
		var reader = HtmlReader.Create("<div class=\"hero\" disabled>hi</div>", _options);

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
			attributes.Add($"{attribute.Name.ToString()}={attribute.Value.ToString()}");
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
		var reader = HtmlReader.Create("<div CLASS=hero class=shadow></div>", _options);

		Assert.Equal(HtmlReadResult.Node, reader.Read());
		Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
		Assert.True(reader.TryGetAttribute("class", out var value));
		Assert.Equal("hero", value.ToString());
	}

	[Fact]
	public void Attributes_YieldSourceOrder_AndExplicitEmptyValue()
	{
		var reader = HtmlReader.Create("<div c='3' a b=\"\" d=text></div>", _options);

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
		var reader = HtmlReader.Create("<div></div>", _options);

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
		var reader = HtmlReader.Create("<div data-value=\"1>2\">ok</div>", _options);

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
			var reader = HtmlReader.Create("text", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			reader.TryGetAttribute("class", out _);
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("text", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			foreach (var attribute in reader.Attributes)
			{
				_ = attribute;
			}
		});
	}
}
