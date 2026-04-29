using AngleSharp.Dom;
using BenchmarkDotNet.Attributes;
using HtmlAgilityPack;

namespace Hypernet.Benchmarks;

[MemoryDiagnoser]
public class MetadataExtractionBenchmarks : BenchmarkBase
{
	[Benchmark(Baseline = true)]
	public int Hypernet_ReadMetadataChecksum()
	{
		using var content = HtmlContent.Create(Html);
		var reader = new HtmlReader(content.Span);
		try
		{
			var hash = 17;
			while (reader.Read())
			{
				if (reader.Token != HtmlToken.StartTag)
				{
					continue;
				}

				if (reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					hash = AddChecksum(hash, reader.GetDangerousTextContent());
					continue;
				}

				if (reader.TagName.Equals("link", StringComparison.OrdinalIgnoreCase))
				{
					if (reader.TryGetAttribute("rel", out var rel)
						&& rel.Equals("canonical", StringComparison.OrdinalIgnoreCase)
						&& reader.TryGetAttribute("href", out var href))
					{
						hash = AddChecksum(hash, href);
					}

					continue;
				}

				if (reader.TagName.Equals("meta", StringComparison.OrdinalIgnoreCase))
				{
					AddHypernetMetaChecksum(ref reader, ref hash);
				}
			}

			return hash;
		}
		finally
		{
			reader.Dispose();
		}
	}

	[Benchmark]
	public int AngleSharp_ReadMetadataChecksum()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		var hash = 17;
		ReadAngleSharpMetadataChecksum(document.DocumentElement, ref hash);
		return hash;
	}

	[Benchmark]
	public int HtmlAgilityPack_ReadMetadataChecksum()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);
		var hash = 17;
		ReadHtmlAgilityPackMetadataChecksum(document.DocumentNode, ref hash);
		return hash;
	}

	[Benchmark]
	public PageMetadata Hypernet_ReadMetadataDto()
	{
		using var content = HtmlContent.Create(Html);
		var reader = new HtmlReader(content.Span);
		try
		{
			var metadata = new PageMetadata();
			while (reader.Read())
			{
				if (reader.Token != HtmlToken.StartTag)
				{
					continue;
				}

				if (reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					metadata.Title ??= reader.GetDangerousTextContent().ToString();
					continue;
				}

				if (reader.TagName.Equals("link", StringComparison.OrdinalIgnoreCase))
				{
					if (reader.TryGetAttribute("rel", out var rel)
						&& rel.Equals("canonical", StringComparison.OrdinalIgnoreCase)
						&& reader.TryGetAttribute("href", out var href))
					{
						metadata.CanonicalUrl ??= href.ToString();
					}

					continue;
				}

				if (reader.TagName.Equals("meta", StringComparison.OrdinalIgnoreCase))
				{
					AddHypernetMetaDto(ref reader, metadata);
				}
			}

			return metadata;
		}
		finally
		{
			reader.Dispose();
		}
	}

	[Benchmark]
	public PageMetadata AngleSharp_ReadMetadataDto()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		var metadata = new PageMetadata();
		ReadAngleSharpMetadataDto(document.DocumentElement, metadata);
		return metadata;
	}

	[Benchmark]
	public PageMetadata HtmlAgilityPack_ReadMetadataDto()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);
		var metadata = new PageMetadata();
		ReadHtmlAgilityPackMetadataDto(document.DocumentNode, metadata);
		return metadata;
	}

	[Benchmark]
	public int Hypernet_WriteMetadataToJson()
	{
		using var content = HtmlContent.Create(Html);
		var reader = new HtmlReader(content.Span);

		using var writer = CreateJsonWriter();
		try
		{
			writer.WriteStartObject();
			while (reader.Read())
			{
				if (reader.Token != HtmlToken.StartTag)
				{
					continue;
				}

				if (reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					writer.WritePropertyName("title");
					writer.WriteStringValue(reader.GetDangerousTextContent());
					continue;
				}

				if (reader.TagName.Equals("link", StringComparison.OrdinalIgnoreCase))
				{
					if (reader.TryGetAttribute("rel", out var rel)
						&& rel.Equals("canonical", StringComparison.OrdinalIgnoreCase)
						&& reader.TryGetAttribute("href", out var href))
					{
						writer.WritePropertyName("canonical");
						writer.WriteStringValue(href);
					}

					continue;
				}

				if (reader.TagName.Equals("meta", StringComparison.OrdinalIgnoreCase))
				{
					WriteHypernetMetaJson(ref reader, writer);
				}
			}

			writer.WriteEndObject();
			writer.Flush();
			return JsonBuffer.WrittenCount;
		}
		finally
		{
			reader.Dispose();
		}
	}

	[Benchmark]
	public int AngleSharp_WriteMetadataToJson()
	{
		var document = AngleSharpParser.ParseDocument(Html);
		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		WriteAngleSharpMetadataJson(document.DocumentElement, writer);
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	[Benchmark]
	public int HtmlAgilityPack_WriteMetadataToJson()
	{
		var document = new HtmlDocument();
		document.LoadHtml(Html);
		using var writer = CreateJsonWriter();
		writer.WriteStartObject();
		WriteHtmlAgilityPackMetadataJson(document.DocumentNode, writer);
		writer.WriteEndObject();
		writer.Flush();
		return JsonBuffer.WrittenCount;
	}

	private static void AddHypernetMetaChecksum(ref HtmlReader reader, ref int hash)
	{
		if (!reader.TryGetAttribute("content", out var content))
		{
			return;
		}

		if (!TryGetHypernetMetaKey(ref reader, out var key))
		{
			return;
		}

		if (IsMetadataKey(key))
		{
			hash = AddChecksum(hash, content);
		}
	}

	private static void AddHypernetMetaDto(ref HtmlReader reader, PageMetadata metadata)
	{
		if (!reader.TryGetAttribute("content", out var content) || !TryGetHypernetMetaKey(ref reader, out var key))
		{
			return;
		}

		if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
		{
			metadata.Description ??= content.ToString();
		}
		else if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphTitle ??= content.ToString();
		}
		else if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphUrl ??= content.ToString();
		}
		else if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphDescription ??= content.ToString();
		}
		else if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
		{
			metadata.ArticleAuthor ??= content.ToString();
		}
	}

	private static void WriteHypernetMetaJson(ref HtmlReader reader, System.Text.Json.Utf8JsonWriter writer)
	{
		if (!reader.TryGetAttribute("content", out var content) || !TryGetHypernetMetaKey(ref reader, out var key))
		{
			return;
		}

		if (TryGetJsonPropertyName(key, out var propertyName))
		{
			writer.WritePropertyName(propertyName);
			writer.WriteStringValue(content);
		}
	}

	private static bool TryGetHypernetMetaKey(ref HtmlReader reader, out ReadOnlySpan<char> key)
	{
		if (reader.TryGetAttribute("property", out key) || reader.TryGetAttribute("name", out key))
		{
			return true;
		}

		key = default;
		return false;
	}

	private static void ReadAngleSharpMetadataChecksum(INode node, ref int hash)
	{
		if (node is IElement element)
		{
			if (element.LocalName == "title")
			{
				hash = AddChecksum(hash, element.TextContent);
			}
			else if (element.LocalName == "link")
			{
				if (IsCanonicalLink(element.GetAttribute("rel")) && element.GetAttribute("href") is { } href)
				{
					hash = AddChecksum(hash, href);
				}
			}
			else if (element.LocalName == "meta")
			{
				var key = element.GetAttribute("property") ?? element.GetAttribute("name");
				var content = element.GetAttribute("content");
				if (key is not null && content is not null && IsMetadataKey(key))
				{
					hash = AddChecksum(hash, content);
				}
			}
		}

		foreach (var child in node.ChildNodes)
		{
			ReadAngleSharpMetadataChecksum(child, ref hash);
		}
	}

	private static void ReadAngleSharpMetadataDto(INode node, PageMetadata metadata)
	{
		if (node is IElement element)
		{
			if (element.LocalName == "title")
			{
				metadata.Title ??= element.TextContent;
			}
			else if (element.LocalName == "link")
			{
				if (IsCanonicalLink(element.GetAttribute("rel")) && element.GetAttribute("href") is { } href)
				{
					metadata.CanonicalUrl ??= href;
				}
			}
			else if (element.LocalName == "meta")
			{
				ApplyMetadataValue(metadata, element.GetAttribute("property") ?? element.GetAttribute("name"), element.GetAttribute("content"));
			}
		}

		foreach (var child in node.ChildNodes)
		{
			ReadAngleSharpMetadataDto(child, metadata);
		}
	}

	private static void WriteAngleSharpMetadataJson(INode node, System.Text.Json.Utf8JsonWriter writer)
	{
		if (node is IElement element)
		{
			if (element.LocalName == "title")
			{
				writer.WriteString("title", element.TextContent);
			}
			else if (element.LocalName == "link")
			{
				if (IsCanonicalLink(element.GetAttribute("rel")) && element.GetAttribute("href") is { } href)
				{
					writer.WriteString("canonical", href);
				}
			}
			else if (element.LocalName == "meta")
			{
				WriteMetadataJson(writer, element.GetAttribute("property") ?? element.GetAttribute("name"), element.GetAttribute("content"));
			}
		}

		foreach (var child in node.ChildNodes)
		{
			WriteAngleSharpMetadataJson(child, writer);
		}
	}

	private static void ReadHtmlAgilityPackMetadataChecksum(HtmlNode node, ref int hash)
	{
		if (node.NodeType == HtmlNodeType.Element)
		{
			if (node.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
			{
				hash = AddChecksum(hash, node.InnerText);
			}
			else if (node.Name.Equals("link", StringComparison.OrdinalIgnoreCase))
			{
				if (IsCanonicalLink(node.Attributes["rel"]?.Value) && node.Attributes["href"]?.Value is { } href)
				{
					hash = AddChecksum(hash, href);
				}
			}
			else if (node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
			{
				var key = node.Attributes["property"]?.Value ?? node.Attributes["name"]?.Value;
				var content = node.Attributes["content"]?.Value;
				if (key is not null && content is not null && IsMetadataKey(key))
				{
					hash = AddChecksum(hash, content);
				}
			}
		}

		foreach (var child in node.ChildNodes)
		{
			ReadHtmlAgilityPackMetadataChecksum(child, ref hash);
		}
	}

	private static void ReadHtmlAgilityPackMetadataDto(HtmlNode node, PageMetadata metadata)
	{
		if (node.NodeType == HtmlNodeType.Element)
		{
			if (node.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
			{
				metadata.Title ??= node.InnerText;
			}
			else if (node.Name.Equals("link", StringComparison.OrdinalIgnoreCase))
			{
				if (IsCanonicalLink(node.Attributes["rel"]?.Value) && node.Attributes["href"]?.Value is { } href)
				{
					metadata.CanonicalUrl ??= href;
				}
			}
			else if (node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
			{
				ApplyMetadataValue(metadata, node.Attributes["property"]?.Value ?? node.Attributes["name"]?.Value, node.Attributes["content"]?.Value);
			}
		}

		foreach (var child in node.ChildNodes)
		{
			ReadHtmlAgilityPackMetadataDto(child, metadata);
		}
	}

	private static void WriteHtmlAgilityPackMetadataJson(HtmlNode node, System.Text.Json.Utf8JsonWriter writer)
	{
		if (node.NodeType == HtmlNodeType.Element)
		{
			if (node.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
			{
				writer.WriteString("title", node.InnerText);
			}
			else if (node.Name.Equals("link", StringComparison.OrdinalIgnoreCase))
			{
				if (IsCanonicalLink(node.Attributes["rel"]?.Value) && node.Attributes["href"]?.Value is { } href)
				{
					writer.WriteString("canonical", href);
				}
			}
			else if (node.Name.Equals("meta", StringComparison.OrdinalIgnoreCase))
			{
				WriteMetadataJson(writer, node.Attributes["property"]?.Value ?? node.Attributes["name"]?.Value, node.Attributes["content"]?.Value);
			}
		}

		foreach (var child in node.ChildNodes)
		{
			WriteHtmlAgilityPackMetadataJson(child, writer);
		}
	}

	private static bool IsCanonicalLink(string? rel)
	{
		return rel is not null && rel.Equals("canonical", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsMetadataKey(ReadOnlySpan<char> key)
	{
		return key.Equals("description", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:title", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:url", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:description", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("article:author", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsMetadataKey(string key)
	{
		return key.Equals("description", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:title", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:url", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("og:description", StringComparison.OrdinalIgnoreCase)
			|| key.Equals("article:author", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryGetJsonPropertyName(ReadOnlySpan<char> key, out string propertyName)
	{
		if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "description";
			return true;
		}
		if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogTitle";
			return true;
		}
		if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogUrl";
			return true;
		}
		if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogDescription";
			return true;
		}
		if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "articleAuthor";
			return true;
		}

		propertyName = "";
		return false;
	}

	private static bool TryGetJsonPropertyName(string key, out string propertyName)
	{
		if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "description";
			return true;
		}
		if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogTitle";
			return true;
		}
		if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogUrl";
			return true;
		}
		if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "ogDescription";
			return true;
		}
		if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
		{
			propertyName = "articleAuthor";
			return true;
		}

		propertyName = "";
		return false;
	}

	private static void ApplyMetadataValue(PageMetadata metadata, string? key, string? content)
	{
		if (key is null || content is null)
		{
			return;
		}

		if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
		{
			metadata.Description ??= content;
		}
		else if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphTitle ??= content;
		}
		else if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphUrl ??= content;
		}
		else if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
		{
			metadata.OpenGraphDescription ??= content;
		}
		else if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
		{
			metadata.ArticleAuthor ??= content;
		}
	}

	private static void WriteMetadataJson(System.Text.Json.Utf8JsonWriter writer, string? key, string? content)
	{
		if (key is null || content is null || !TryGetJsonPropertyName(key, out var propertyName))
		{
			return;
		}

		writer.WriteString(propertyName, content);
	}
}
