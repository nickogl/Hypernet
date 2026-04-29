using AngleSharp.Dom;
using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;

namespace Hypernet.Benchmarks;

[MemoryDiagnoser]
public class LinkExtractionBenchmarks : BenchmarkBase
{
	[Benchmark(Baseline = true)]
	public int Hypernet_CountAnchorHrefs()
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span);

		var count = 0;
		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
				&& reader.TryGetAttribute("href", out _))
			{
				count++;
			}
		}

		return count;
	}

	[Benchmark]
	public int AngleSharp_CountAnchorHrefs()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		return CountAngleSharpAnchorHrefs(document.DocumentElement);
	}

	[Benchmark]
	public int HtmlAgilityPack_CountAnchorHrefs()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);
		return CountHtmlAgilityPackAnchorHrefs(document.DocumentNode);
	}

	[Benchmark]
	public List<string> Hypernet_ExtractAnchorHrefsToList()
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span);

		var hrefs = new List<string>();
		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
				&& reader.TryGetAttribute("href", out var href))
			{
				hrefs.Add(href.ToString());
			}
		}

		return hrefs;
	}

	[Benchmark]
	public List<string> AngleSharp_ExtractAnchorHrefsToList()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		var hrefs = new List<string>();
		CollectAngleSharpAnchorHrefs(document.DocumentElement, hrefs);
		return hrefs;
	}

	[Benchmark]
	public List<string> HtmlAgilityPack_ExtractAnchorHrefsToList()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);
		var hrefs = new List<string>();
		CollectHtmlAgilityPackAnchorHrefs(document.DocumentNode, hrefs);
		return hrefs;
	}

	[Benchmark]
	public int Hypernet_WriteAnchorHrefsToJson()
	{
		using var content = HtmlContent.Create(Html);
		using var reader = new HtmlReader(content.Span);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		writer.WritePropertyName("links");
		writer.WriteStartArray();

		while (reader.Read())
		{
			if (reader.Token == HtmlToken.StartTag
				&& reader.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
				&& reader.TryGetAttribute("href", out var href))
			{
				writer.WriteStringValue(href);
			}
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int AngleSharp_WriteAnchorHrefsToJson()
	{
		var document = AngleSharpParser.ParseDocument(Html);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		writer.WritePropertyName("links");
		writer.WriteStartArray();
		WriteAngleSharpAnchorHrefs(document.DocumentElement, writer);
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int HtmlAgilityPack_WriteAnchorHrefsToJson()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);

		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		writer.WritePropertyName("links");
		writer.WriteStartArray();
		WriteHtmlAgilityPackAnchorHrefs(document.DocumentNode, writer);
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	private static int CountAngleSharpAnchorHrefs(INode node)
	{
		var count = 0;
		if (node is IElement { LocalName: "a" } element && element.GetAttribute("href") is not null)
		{
			count++;
		}

		foreach (var child in node.ChildNodes)
		{
			count += CountAngleSharpAnchorHrefs(child);
		}

		return count;
	}

	private static void CollectAngleSharpAnchorHrefs(INode node, List<string> hrefs)
	{
		if (node is IElement { LocalName: "a" } element && element.GetAttribute("href") is { } href)
		{
			hrefs.Add(href);
		}

		foreach (var child in node.ChildNodes)
		{
			CollectAngleSharpAnchorHrefs(child, hrefs);
		}
	}

	private static void WriteAngleSharpAnchorHrefs(INode node, System.Text.Json.Utf8JsonWriter writer)
	{
		if (node is IElement { LocalName: "a" } element && element.GetAttribute("href") is { } href)
		{
			writer.WriteStringValue(href);
		}

		foreach (var child in node.ChildNodes)
		{
			WriteAngleSharpAnchorHrefs(child, writer);
		}
	}

	private static int CountHtmlAgilityPackAnchorHrefs(HtmlNode node)
	{
		var count = 0;
		if (node.NodeType == HtmlNodeType.Element
			&& node.Name.Equals("a", StringComparison.OrdinalIgnoreCase)
			&& node.Attributes["href"] is not null)
		{
			count++;
		}

		foreach (var child in node.ChildNodes)
		{
			count += CountHtmlAgilityPackAnchorHrefs(child);
		}

		return count;
	}

	private static void CollectHtmlAgilityPackAnchorHrefs(HtmlNode node, List<string> hrefs)
	{
		if (node.NodeType == HtmlNodeType.Element
			&& node.Name.Equals("a", StringComparison.OrdinalIgnoreCase)
			&& node.Attributes["href"] is { } href)
		{
			hrefs.Add(href.Value);
		}

		foreach (var child in node.ChildNodes)
		{
			CollectHtmlAgilityPackAnchorHrefs(child, hrefs);
		}
	}

	private static void WriteHtmlAgilityPackAnchorHrefs(HtmlNode node, System.Text.Json.Utf8JsonWriter writer)
	{
		if (node.NodeType == HtmlNodeType.Element
			&& node.Name.Equals("a", StringComparison.OrdinalIgnoreCase)
			&& node.Attributes["href"] is { } href)
		{
			writer.WriteStringValue(href.Value);
		}

		foreach (var child in node.ChildNodes)
		{
			WriteHtmlAgilityPackAnchorHrefs(child, writer);
		}
	}
}
