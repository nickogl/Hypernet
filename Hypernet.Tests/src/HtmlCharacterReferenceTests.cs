namespace Hypernet.Tests;

public sealed class HtmlCharacterReferenceTests
{
	[Fact]
	public void TryDecode_DecodesNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(HtmlCharacterReference.TryDecode("&amp;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("&", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesMultiCharacterNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(HtmlCharacterReference.TryDecode("&acE;", destination, out var charsWritten));
		Assert.Equal(2, charsWritten);
		Assert.Equal("\u223E\u0333", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesDecimalNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(HtmlCharacterReference.TryDecode("&#233;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("é", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesHexNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(HtmlCharacterReference.TryDecode("&#xE9;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("é", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesSupplementaryNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(HtmlCharacterReference.TryDecode("&#x1F600;", destination, out var charsWritten));
		Assert.Equal(2, charsWritten);
		Assert.Equal("😀", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_ReturnsFalseForUnknownNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.False(HtmlCharacterReference.TryDecode("&definitelynotreal;", destination, out var charsWritten));
		Assert.Equal(0, charsWritten);
	}

	[Fact]
	public void TryDecode_ReturnsFalseForMalformedNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.False(HtmlCharacterReference.TryDecode("&#xZZ;", destination, out var charsWritten));
		Assert.Equal(0, charsWritten);
	}
}
