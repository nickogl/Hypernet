namespace Hypernet;

public ref partial struct HtmlReader
{
	internal readonly struct Input
	{
		public char[] Buffer { get; init; }
		public int Length { get; init; }
		public HtmlReaderOptions Options { get; init; }
	}
}
