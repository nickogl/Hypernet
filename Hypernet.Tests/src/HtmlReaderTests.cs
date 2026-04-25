namespace Hypernet.Tests;

public sealed partial class HtmlReaderTests : IDisposable
{
	private readonly AutoReleasingArrayPool<char> _textPool;
	private readonly AutoReleasingArrayPool<byte> _bytePool;
	private readonly HtmlReaderOptions _options;

	public HtmlReaderTests()
	{
		_textPool = new AutoReleasingArrayPool<char>();
		_bytePool = new AutoReleasingArrayPool<byte>();
		_options = new HtmlReaderOptions()
		{
			TextBufferPool = _textPool,
			ByteBufferPool = _bytePool,
		};
	}

	public void Dispose()
	{
		_bytePool.Dispose();
		_textPool.Dispose();
	}

	[Fact]
	public void TagName_Throws_WhenCurrentEntityKindIsUnexpected()
	{
		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("text", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			_ = reader.TagName;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("<!--note-->", _options);

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
			var reader = HtmlReader.Create("<div>", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
			_ = reader.TextNode;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("<!--note-->", _options);

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
			var reader = HtmlReader.Create("<div>", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.StartTag, reader.Kind);
			_ = reader.Comment;
		});

		Assert.Throws<InvalidOperationException>(() =>
		{
			var reader = HtmlReader.Create("text", _options);

			Assert.Equal(HtmlReadResult.Node, reader.Read());
			Assert.Equal(HtmlEntityKind.Text, reader.Kind);
			_ = reader.Comment;
		});
	}
}
