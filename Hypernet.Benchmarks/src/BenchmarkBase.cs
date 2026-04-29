using AngleSharp.Html.Parser;
using BenchmarkDotNet.Attributes;
using System.Buffers;
using System.Text.Json;

namespace Hypernet.Benchmarks;

public abstract class BenchmarkBase
{
	protected const int BodyExcerptLength = 5_000;

	[Params(
		"wikipedia_article_html_parser.html",
		"mdn_using_fetch.html",
		"github_dotnet_docker_repo.html",
		"reuters_technology_article.html",
		"stackoverflow_htmlagilitypack_question.html")]
	public string FileName { get; set; } = "";

	protected string Html { get; private set; } = "";

	protected HtmlParser AngleSharpParser { get; private set; } = null!;

	protected ArrayBufferWriter<byte> JsonBuffer { get; private set; } = null!;

	[GlobalSetup]
	public void GlobalSetup()
	{
		Html = File.ReadAllText(GetFixturePath(FileName));
		AngleSharpParser = new HtmlParser();
		JsonBuffer = new ArrayBufferWriter<byte>();
	}

	protected Utf8JsonWriter CreateJsonWriter()
	{
		JsonBuffer.Clear();
		return new Utf8JsonWriter(JsonBuffer);
	}

	protected static string GetFixturePath(string fileName)
	{
		return Path.Combine(AppContext.BaseDirectory, "data", fileName);
	}

	protected static int AddChecksum(int hash, ReadOnlySpan<char> value)
	{
		unchecked
		{
			for (var i = 0; i < value.Length; i++)
			{
				hash = (hash * 31) + value[i];
			}

			return hash;
		}
	}

	protected static int AddChecksum(int hash, string? value)
	{
		return value is null ? hash : AddChecksum(hash, value.AsSpan());
	}

	public sealed class PageMetadata
	{
		public string? Title { get; set; }
		public string? Description { get; set; }
		public string? CanonicalUrl { get; set; }
		public string? OpenGraphTitle { get; set; }
		public string? OpenGraphUrl { get; set; }
		public string? OpenGraphDescription { get; set; }
		public string? ArticleAuthor { get; set; }
	}
}
