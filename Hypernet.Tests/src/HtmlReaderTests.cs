namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Constructor_ThrowsWhenMaxDepthIsNegative()
	{
		var options = new HtmlReaderOptions() { MaxDepth = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => new HtmlReader("<div/>".ToCharArray(), options));
	}

	[Fact]
	public void TagName_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			_ = reader.TagName;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<!--note-->");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Comment, reader.Kind);
			_ = reader.TagName;
		});
	}

	[Fact]
	public void TextNode_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
			_ = reader.TextNode;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<!--note-->");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Comment, reader.Kind);
			_ = reader.TextNode;
		});
	}

	[Fact]
	public void Comment_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
			_ = reader.Comment;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			_ = reader.Comment;
		});
	}
}
