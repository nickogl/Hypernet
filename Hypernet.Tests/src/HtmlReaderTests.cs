namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests
{
	[Fact]
	public void Constructor_Throws_WhenMaxDepthIsNegative()
	{
		var options = new HtmlReaderOptions() { MaxDepth = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => new HtmlReader("<div/>".ToCharArray(), options));
	}

	[Fact]
	public void Constructor_Throws_WhenInitialTextContentSegmentSizeIsNegative()
	{
		var options = new HtmlReaderOptions() { InitialTextContentSegmentSize = -1 };

		Assert.Throws<ArgumentOutOfRangeException>(() => new HtmlReader("<div/>".ToCharArray(), options));
	}

	[Fact]
	public void TagName_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Text, reader.Token);
			_ = reader.TagName;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<!--note-->");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Comment, reader.Token);
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

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.StartTag, reader.Token);
			_ = reader.Text;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<!--note-->");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Comment, reader.Token);
			_ = reader.Text;
		});
	}

	[Fact]
	public void Comment_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("<div>");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.StartTag, reader.Token);
			_ = reader.Comment;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			using var content = HtmlContent.Create("text");
			var reader = new HtmlReader(content.Span);

			Assert.True(reader.Read());
			Assert.Equal(HtmlToken.Text, reader.Token);
			_ = reader.Comment;
		});
	}
}
