namespace Hypernet.Tests;

public sealed class CharacterReferenceTests
{
	[Fact]
	public void TryDecode_DecodesNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(CharacterReference.TryDecode("&amp;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("&", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesMultiCharacterNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(CharacterReference.TryDecode("&acE;", destination, out var charsWritten));
		Assert.Equal(2, charsWritten);
		Assert.Equal("\u223E\u0333", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesDecimalNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(CharacterReference.TryDecode("&#233;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("é", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesHexNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(CharacterReference.TryDecode("&#xE9;", destination, out var charsWritten));
		Assert.Equal(1, charsWritten);
		Assert.Equal("é", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_DecodesSupplementaryNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.True(CharacterReference.TryDecode("&#x1F600;", destination, out var charsWritten));
		Assert.Equal(2, charsWritten);
		Assert.Equal("😀", destination[..charsWritten]);
	}

	[Fact]
	public void TryDecode_ReturnsFalseForUnknownNamedEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.False(CharacterReference.TryDecode("&definitelynotreal;", destination, out var charsWritten));
		Assert.Equal(0, charsWritten);
	}

	[Fact]
	public void TryDecode_ReturnsFalseForMalformedNumericEntity()
	{
		Span<char> destination = stackalloc char[2];
		Assert.False(CharacterReference.TryDecode("&#xZZ;", destination, out var charsWritten));
		Assert.Equal(0, charsWritten);
	}
}
