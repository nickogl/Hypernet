using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;

namespace Hypernet.Benchmarks;

[MemoryDiagnoser]
public class BodyExcerptBenchmarks : BenchmarkBase
{
	private static readonly HtmlReaderOptions _readerOptions = new() { MaxTextContentSegmentSize = 8192 };

	[Benchmark]
	public int Hypernet_ReadBodyExcerptChecksum()
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span, _readerOptions);
		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
			{
				var text = reader.GetDangerousTextContent(HtmlTextContentOptions.IncludeNonContentText);
				var excerpt = text[..Math.Min(text.Length, BodyExcerptLength)];
				return AddChecksum(17, excerpt);
			}
		}

		return 0;
	}

	[Benchmark]
	public int AngleSharp_ReadBodyExcerptChecksum()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		if (document.Body is null)
		{
			return 0;
		}

		return AddChecksum(17, SliceExcerpt(document.Body.TextContent).AsSpan());
	}

	[Benchmark]
	public int HtmlAgilityPack_ReadBodyExcerptChecksum()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);

		var body = FindHtmlAgilityPackBody(document.DocumentNode);
		if (body is null)
		{
			return 0;
		}

		return AddChecksum(17, SliceExcerpt(body.InnerText).AsSpan());
	}

	[Benchmark]
	public string Hypernet_ReadBodyExcerptString()
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span, _readerOptions);
		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
			{
				var text = reader.GetDangerousTextContent(HtmlTextContentOptions.IncludeNonContentText);
				return text[..Math.Min(text.Length, BodyExcerptLength)].ToString();
			}
		}

		return string.Empty;
	}

	[Benchmark]
	public string AngleSharp_ReadBodyExcerptString()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		return document.Body is null
			? string.Empty
			: SliceExcerpt(document.Body.TextContent);
	}

	[Benchmark]
	public string HtmlAgilityPack_ReadBodyExcerptString()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);

		var body = FindHtmlAgilityPackBody(document.DocumentNode);
		return body is null
			? string.Empty
			: SliceExcerpt(body.InnerText);
	}

	[Benchmark]
	public int Hypernet_WriteBodyExcerptToJson()
	{
		using var content = HtmlContent.Create(Html);
		var reader = new HtmlReader(content.Span, _readerOptions);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		try
		{
			while (reader.Read())
			{
				if (reader.Token == HtmlToken.StartTag
					&& reader.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
				{
					var text = reader.GetDangerousTextContent(HtmlTextContentOptions.IncludeNonContentText);
					writer.WritePropertyName("text");
					writer.WriteStringValue(text[..Math.Min(text.Length, BodyExcerptLength)]);
					break;
				}
			}
		}
		finally
		{
			reader.Dispose();
		}
		writer.WriteEndObject();
		writer.Flush();

		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int AngleSharp_WriteBodyExcerptToJson()
	{
		var document = AngleSharpParser.ParseDocument(Html);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		if (document.Body is not null)
		{
			writer.WriteString("text", SliceExcerpt(document.Body.TextContent));
		}
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int HtmlAgilityPack_WriteBodyExcerptToJson()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		var body = FindHtmlAgilityPackBody(document.DocumentNode);
		if (body is not null)
		{
			writer.WriteString("text", SliceExcerpt(body.InnerText));
		}
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int Hypernet_TryGetBodyExcerptToJson()
	{
		return Hypernet_TryGetBodyExcerptToJson(HtmlTextContentOptions.IncludeNonContentText);
	}

	[Benchmark]
	public int Hypernet_TryGetBodyExcerptNormalizedToJson()
	{
		return Hypernet_TryGetBodyExcerptToJson(HtmlTextContentOptions.IncludeNonContentText | HtmlTextContentOptions.NormalizeWhitespace);
	}

	private static string SliceExcerpt(string text)
	{
		return text.Length <= BodyExcerptLength ? text : text[..BodyExcerptLength];
	}

	private int Hypernet_TryGetBodyExcerptToJson(HtmlTextContentOptions options)
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span, _readerOptions);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("body", StringComparison.OrdinalIgnoreCase))
			{
				Span<char> buffer = stackalloc char[BodyExcerptLength];
				reader.TryGetTextContent(buffer, options, out var charsWritten);
				writer.WritePropertyName("text");
				writer.WriteStringValue(buffer[..Math.Min(charsWritten, BodyExcerptLength)]);
				break;
			}
		}
		writer.WriteEndObject();
		writer.Flush();

		return JsonBuffer.WrittenCount;
	}

	private static HtmlNode? FindHtmlAgilityPackBody(HtmlNode node)
	{
		if (node.NodeType == HtmlNodeType.Element
			&& node.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
		{
			return node;
		}

		foreach (var child in node.ChildNodes)
		{
			var body = FindHtmlAgilityPackBody(child);
			if (body is not null)
			{
				return body;
			}
		}

		return null;
	}
}
