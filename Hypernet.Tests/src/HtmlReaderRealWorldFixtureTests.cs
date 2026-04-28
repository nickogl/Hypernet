namespace Hypernet.Tests;

public sealed class HtmlReaderRealWorldFixtureTests
{
	[Fact]
	public void ExtractMetadata_WikipediaArticle()
	{
		var metadata = ExtractMetadata("wikipedia_article_html_parser.html");

		Assert.Equal("Parsing - Wikipedia", metadata.Title);
		Assert.Equal("https://en.wikipedia.org/wiki/Parsing#Parser", metadata.CanonicalUrl);
		Assert.Equal("Parsing - Wikipedia", metadata.OpenGraphTitle);
	}

	[Fact]
	public void ExtractMetadata_MdnUsingFetch()
	{
		var metadata = ExtractMetadata("mdn_using_fetch.html");

		Assert.Equal("Using the Fetch API - Web APIs | MDN", metadata.Title);
		Assert.Equal("https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API/Using_Fetch", metadata.CanonicalUrl);
		Assert.Equal("Using the Fetch API - Web APIs | MDN", metadata.OpenGraphTitle);
		Assert.Equal("The Fetch API provides a JavaScript interface for making HTTP requests and processing the responses.", metadata.Description);
	}

	[Fact]
	public void ExtractMetadata_GitHubDotnetDockerRepository()
	{
		var metadata = ExtractMetadata("github_dotnet_docker_repo.html");

		Assert.Equal("GitHub - dotnet/dotnet-docker: Official container images for .NET · GitHub", metadata.Title);
		Assert.Equal("GitHub - dotnet/dotnet-docker: Official container images for .NET", metadata.OpenGraphTitle);
		Assert.Equal("https://github.com/dotnet/dotnet-docker", metadata.OpenGraphUrl);
		Assert.Equal("Official container images for .NET. Contribute to dotnet/dotnet-docker development by creating an account on GitHub.", metadata.Description);
	}

	[Fact]
	public void ExtractMetadata_ReutersTechnologyArticle()
	{
		var metadata = ExtractMetadata("reuters_technology_article.html");

		Assert.Equal("Big Tech investors to gauge payoff as AI spending set to hit $600 billion | Reuters", metadata.Title);
		Assert.Equal("https://www.reuters.com/business/retail-consumer/big-tech-investors-gauge-payoff-ai-spending-set-hit-600-billion-2026-04-28/", metadata.CanonicalUrl);
		Assert.Equal("Big Tech investors to gauge payoff as AI spending set to hit $600 billion", metadata.OpenGraphTitle);
		Assert.Equal("Big Tech has spent hundreds of billions of dollars over three years to power the artificial intelligence boom. But investors still want one answer: will all this pay off?", metadata.Description);
		Assert.Equal("Aditya Soni,Deborah Mary Sophia", metadata.ArticleAuthor);
	}

	[Fact]
	public void ExtractMetadata_StackOverflowQuestion()
	{
		var metadata = ExtractMetadata("stackoverflow_htmlagilitypack_question.html");

		Assert.Equal("c# - Parsing HTML page with HtmlAgilityPack - Stack Overflow", metadata.Title);
		Assert.Equal("https://stackoverflow.com/questions/1512562/parsing-html-page-with-htmlagilitypack", metadata.CanonicalUrl);
		Assert.Equal("Parsing HTML page with HtmlAgilityPack", metadata.OpenGraphTitle);
	}

	[Fact]
	public void ExtractAnchorHrefs_WikipediaArticle()
	{
		var hrefs = ExtractAnchorHrefs("wikipedia_article_html_parser.html");

		Assert.Contains("/wiki/Lexical_analysis", hrefs);
		Assert.Contains("/wiki/LL_parser", hrefs);
		Assert.Contains("/wiki/LR_parser", hrefs);
		Assert.Contains("/wiki/Parsing_expression_grammar", hrefs);
		Assert.Contains("/wiki/Category:Compiler_construction", hrefs);
	}

	[Fact]
	public void ExtractAnchorHrefs_MdnUsingFetch()
	{
		var hrefs = ExtractAnchorHrefs("mdn_using_fetch.html");

		Assert.Contains("/en-US/docs/Web/API/Fetch_API", hrefs);
		Assert.Contains("/en-US/docs/Web/API/XMLHttpRequest", hrefs);
		Assert.Contains("/en-US/docs/Web/API/Window/fetch", hrefs);
		Assert.Contains("/en-US/docs/Web/API/RequestInit#mode", hrefs);
		Assert.Contains("#including_credentials", hrefs);
	}

	[Fact]
	public void ExtractAnchorHrefs_GitHubDotnetDockerRepository()
	{
		var hrefs = ExtractAnchorHrefs("github_dotnet_docker_repo.html");

		Assert.Contains("#start-of-content", hrefs);
		Assert.Contains("/dotnet/dotnet-docker/blob/main/.gitattributes", hrefs);
		Assert.Contains("/dotnet/dotnet-docker/blob/main/README.md", hrefs);
		Assert.Contains("/dotnet/dotnet-docker/blob/main/Microsoft.DotNet.Docker.slnx", hrefs);
		Assert.Contains("/dotnet/dotnet-docker/blob/main/manifest.versions.json", hrefs);
	}

	[Fact]
	public void ExtractAnchorHrefs_ReutersTechnologyArticle()
	{
		var hrefs = ExtractAnchorHrefs("reuters_technology_article.html");

		Assert.Contains("#main-content", hrefs);
		Assert.Contains("/", hrefs);
		Assert.Contains("/world/", hrefs);
		Assert.Contains("/business/", hrefs);
		Assert.Contains("/technology/", hrefs);
		Assert.Contains("/authors/aditya-soni/", hrefs);
		Assert.Contains("/authors/deborah-mary-sophia/", hrefs);
	}

	[Fact]
	public void ExtractAnchorHrefs_StackOverflowQuestion()
	{
		var hrefs = ExtractAnchorHrefs("stackoverflow_htmlagilitypack_question.html");

		Assert.Contains("https://stackoverflow.com", hrefs);
		Assert.Contains("/questions/1512562/parsing-html-page-with-htmlagilitypack", hrefs);
		Assert.Contains("/questions/linked/1512562", hrefs);
		Assert.Contains("/questions/20852762/parsing-html-page-with-htmlagilitypack-using-linq", hrefs);
		Assert.Contains("/questions/2254772/c-parsing-html-page-using-html-agility-pack", hrefs);
	}

	private static PageMetadata ExtractMetadata(string fileName)
	{
		using var content = HtmlContent.Create(File.ReadAllText(GetFixturePath(fileName)));
		using var reader = new HtmlReader(content.Span);

		var metadata = new PageMetadata();
		var inTitle = false;
		while (reader.Read())
		{
			if (inTitle)
			{
				if (reader.Token == HtmlToken.Text)
				{
					metadata.Title ??= reader.Text.ToString().Trim();
				}
				else if (reader.Token == HtmlToken.EndTag
					&& reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					inTitle = false;
				}

				continue;
			}

			if (reader.Token != HtmlToken.StartTag)
			{
				continue;
			}

			if (reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
			{
				inTitle = true;
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

			if (!reader.TagName.Equals("meta", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (!reader.TryGetAttribute("content", out var contentAttribute))
			{
				continue;
			}

			ReadOnlySpan<char> key = default;
			if (reader.TryGetAttribute("property", out var property))
			{
				key = property;
			}
			else if (reader.TryGetAttribute("name", out var name))
			{
				key = name;
			}
			if (key.IsEmpty)
			{
				continue;
			}

			if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
			{
				metadata.Description ??= contentAttribute.ToString();
			}
			else if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
			{
				metadata.OpenGraphTitle ??= contentAttribute.ToString();
			}
			else if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
			{
				metadata.OpenGraphUrl ??= contentAttribute.ToString();
			}
			else if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
			{
				metadata.Description ??= contentAttribute.ToString();
			}
			else if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
			{
				metadata.ArticleAuthor ??= contentAttribute.ToString();
			}
		}

		return metadata;
	}

	private static List<string> ExtractAnchorHrefs(string fileName)
	{
		using var content = HtmlContent.Create(File.ReadAllText(GetFixturePath(fileName)));
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

	private static string GetFixturePath(string fileName)
	{
		return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "data", fileName);
	}

	private sealed class PageMetadata
	{
		public string? Title { get; set; }
		public string? CanonicalUrl { get; set; }
		public string? Description { get; set; }
		public string? OpenGraphTitle { get; set; }
		public string? OpenGraphUrl { get; set; }
		public string? ArticleAuthor { get; set; }
	}
}
