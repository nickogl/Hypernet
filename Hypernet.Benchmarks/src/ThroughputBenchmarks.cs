using AngleSharp.Html.Parser;
using HtmlAgilityPack;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Hypernet.Benchmarks;

internal sealed record ThroughputBenchmarkOptions
{
	public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;
	public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(30);
	public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromSeconds(5);
}

internal static class ThroughputBenchmarks
{
	private static readonly string[] _fixtureNames =
	[
		"wikipedia_article_html_parser.html",
		"mdn_using_fetch.html",
		"github_dotnet_docker_repo.html",
		"reuters_technology_article.html",
		"stackoverflow_htmlagilitypack_question.html",
	];

	public static async Task RunAsync(ThroughputBenchmarkOptions options)
	{
		var fixtures = LoadFixtures();
		var measurements = fixtures.Count * 3;

		Console.WriteLine($"Concurrency: {options.MaxDegreeOfParallelism}");
		Console.WriteLine($"Warmup: {FormatDuration(options.WarmupDuration)}");
		Console.WriteLine($"Duration: {FormatDuration(options.Duration)}");
		Console.WriteLine();
		Console.WriteLine($"Warming up for {FormatDuration(options.WarmupDuration)} across all fixtures...");
		Console.WriteLine();
		var warmupOptions = options with { Duration = GetPerMeasurementDuration(options.WarmupDuration, measurements) };
		foreach (var (fileName, bytes) in fixtures)
		{
			_ = await MeasureAsync(fileName, "Hypernet", bytes, warmupOptions, ProcessHypernetAsync);
			_ = await MeasureAsync(fileName, "AngleSharp", bytes, warmupOptions, ProcessAngleSharpAsync);
			_ = await MeasureAsync(fileName, "HtmlAgilityPack", bytes, warmupOptions, ProcessHtmlAgilityPackAsync);
		}

		Console.WriteLine("| FileName | Library | Rate (c/s) | P50 (us) | P99 (us) |");
		Console.WriteLine("|---|---:|---:|---:|---:|");
		var measurementOptions = options with { Duration = GetPerMeasurementDuration(options.Duration, measurements) };
		var overall = new List<ThroughputResult>();
		foreach (var (fileName, bytes) in fixtures)
		{
			var hypernet = await MeasureAsync(fileName, "Hypernet", bytes, measurementOptions, ProcessHypernetAsync);
			overall.Add(hypernet);

			var angleSharp = await MeasureAsync(fileName, "AngleSharp", bytes, measurementOptions, ProcessAngleSharpAsync);
			overall.Add(angleSharp);

			var htmlAgilityPack = await MeasureAsync(fileName, "HtmlAgilityPack", bytes, measurementOptions, ProcessHtmlAgilityPackAsync);
			overall.Add(htmlAgilityPack);

			WriteComparisonRow(fileName, hypernet, angleSharp, htmlAgilityPack);
		}

		Console.WriteLine();
		WriteOverallRow(overall);
	}

	private static async Task<ThroughputResult> MeasureAsync(
		string fileName,
		string library,
		byte[] bytes,
		ThroughputBenchmarkOptions options,
		Func<byte[], CancellationToken, ValueTask> processor)
	{
		using var cts = new CancellationTokenSource(options.Duration);
		var parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
		};

		var processed = 0L;
		var latencies = new List<long>();
		var start = Stopwatch.GetTimestamp();
		await Parallel.ForEachAsync(
			Enumerable.Range(0, options.MaxDegreeOfParallelism),
			parallelOptions,
			async (_, _) =>
			{
				while (!cts.Token.IsCancellationRequested)
				{
					var itemStart = Stopwatch.GetTimestamp();
					try
					{
						await processor(bytes, cts.Token);
					}
					catch (OperationCanceledException) when (cts.IsCancellationRequested)
					{
						break;
					}

					Interlocked.Increment(ref processed);
					var latency = Stopwatch.GetTimestamp() - itemStart;
					lock (latencies)
					{
						latencies.Add(latency);
					}
				}
			});
		var elapsed = Stopwatch.GetElapsedTime(start);

		var latencySamples = latencies.ToArray();
		var (p50, p99) = CalculatePercentiles(latencySamples);
		return new ThroughputResult(fileName, library, processed, elapsed, p50, p99, latencySamples);
	}

	private static void WriteComparisonRow(string fileName, ThroughputResult hypernet, ThroughputResult angleSharp, ThroughputResult htmlAgilityPack)
	{
		Console.WriteLine(
			$"| {fileName} | Hypernet | {FormatRate(hypernet.ProcessedPerSecond)} | {FormatLatency(hypernet.P50)} | {FormatLatency(hypernet.P99)} |");
		Console.WriteLine(
			$"| {fileName} | AngleSharp | {FormatRate(angleSharp.ProcessedPerSecond)} | {FormatLatency(angleSharp.P50)} | {FormatLatency(angleSharp.P99)} |");
		Console.WriteLine(
			$"| {fileName} | HtmlAgilityPack | {FormatRate(htmlAgilityPack.ProcessedPerSecond)} | {FormatLatency(htmlAgilityPack.P50)} | {FormatLatency(htmlAgilityPack.P99)} |");
	}

	private static void WriteOverallRow(List<ThroughputResult> results)
	{
		var hypernet = Aggregate(results, "Hypernet");
		var angleSharp = Aggregate(results, "AngleSharp");
		var htmlAgilityPack = Aggregate(results, "HtmlAgilityPack");

		Console.WriteLine("Summary");
		Console.WriteLine("| Library | Rate (c/s) | P50 (us) | P99 (us) |");
		Console.WriteLine("|---|---:|---:|---:|");
		Console.WriteLine($"| Hypernet | {FormatRate(hypernet.ProcessedPerSecond)} | {FormatLatency(hypernet.P50)} | {FormatLatency(hypernet.P99)} |");
		Console.WriteLine($"| AngleSharp | {FormatRate(angleSharp.ProcessedPerSecond)} | {FormatLatency(angleSharp.P50)} | {FormatLatency(angleSharp.P99)} |");
		Console.WriteLine($"| HtmlAgilityPack | {FormatRate(htmlAgilityPack.ProcessedPerSecond)} | {FormatLatency(htmlAgilityPack.P50)} | {FormatLatency(htmlAgilityPack.P99)} |");
	}

	private static string FormatDuration(TimeSpan elapsed)
	{
		return elapsed.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " s";
	}

	private static string FormatRate(double value)
	{
		return $"{value:N0} c/s";
	}

	private static string FormatLatency(TimeSpan elapsed)
	{
		return elapsed.TotalMicroseconds.ToString("0.000", CultureInfo.InvariantCulture) + " us";
	}

	private static (TimeSpan P50, TimeSpan P99) CalculatePercentiles(long[] samples)
	{
		if (samples.Length == 0)
		{
			return (TimeSpan.Zero, TimeSpan.Zero);
		}

		var sorted = (long[])samples.Clone();
		Array.Sort(sorted);
		return (
			ToLatencyTimeSpan(GetPercentile(sorted, 50)),
			ToLatencyTimeSpan(GetPercentile(sorted, 99)));

		static long GetPercentile(long[] sortedSamples, double percentile)
		{
			var index = (int)Math.Ceiling((percentile / 100d) * sortedSamples.Length) - 1;
			index = Math.Clamp(index, 0, sortedSamples.Length - 1);
			return sortedSamples[index];
		}
	}

	private static TimeSpan ToLatencyTimeSpan(long stopwatchTicks)
	{
		return TimeSpan.FromSeconds(stopwatchTicks / (double)Stopwatch.Frequency);
	}

	private static ThroughputResult Aggregate(IEnumerable<ThroughputResult> results, string library)
	{
		var matching = results.Where(result => result.Library == library).ToArray();
		var samples = matching.SelectMany(result => result.LatencyTicks).ToArray();
		var (p50, p99) = CalculatePercentiles(samples);
		return new ThroughputResult(
			"Overall",
			library,
			matching.Sum(result => result.Processed),
			TimeSpan.FromTicks(matching.Sum(result => result.Elapsed.Ticks)),
			p50,
			p99,
			samples);
	}

	private static TimeSpan GetPerMeasurementDuration(TimeSpan duration, int measurements)
	{
		if (duration <= TimeSpan.Zero || measurements <= 0)
		{
			return TimeSpan.Zero;
		}

		return TimeSpan.FromTicks(Math.Max(1, duration.Ticks / measurements));
	}

	private static List<(string FileName, byte[] Bytes)> LoadFixtures()
	{
		var fixtures = new List<(string FileName, byte[] Bytes)>(_fixtureNames.Length);
		foreach (var fileName in _fixtureNames)
		{
			fixtures.Add((fileName, File.ReadAllBytes(GetFixturePath(fileName))));
		}

		return fixtures;
	}

	private static string GetFixturePath(string fileName)
	{
		return Path.Combine(AppContext.BaseDirectory, "data", fileName);
	}

	private static async ValueTask ProcessHypernetAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
		using var content = await HtmlContent.CreateAsync(stream, cancellationToken);
		var reader = new HtmlReader(content.Span);
		try
		{
			var buffer = new ArrayBufferWriter<byte>();
			using var writer = new Utf8JsonWriter(buffer);
			writer.WriteStartObject();
			writer.WritePropertyName("items");
			writer.WriteStartArray();
			while (reader.Read())
			{
				if (reader.Token != HtmlToken.StartTag)
				{
					continue;
				}

				if (reader.TagName.Equals("title", StringComparison.OrdinalIgnoreCase))
				{
					WriteItem(writer, "title", reader.GetDangerousTextContent());
					continue;
				}

				if (reader.TagName.Equals("link", StringComparison.OrdinalIgnoreCase))
				{
					if (reader.TryGetAttribute("rel", out var rel)
						&& rel.Equals("canonical", StringComparison.OrdinalIgnoreCase)
						&& reader.TryGetAttribute("href", out var href))
					{
						WriteItem(writer, "canonical", href);
					}

					continue;
				}

				if (reader.TagName.Equals("meta", StringComparison.OrdinalIgnoreCase))
				{
					WriteHypernetMetaItem(ref reader, writer);
					continue;
				}

				if (reader.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
					&& reader.TryGetAttribute("href", out var hrefValue))
				{
					WriteItem(writer, "link", hrefValue);
				}
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
			writer.Flush();
		}
		finally
		{
			reader.Dispose();
		}
	}

	private static async ValueTask ProcessAngleSharpAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		using var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
		var parser = new HtmlParser();
		var document = await parser.ParseDocumentAsync(stream, cancellationToken);

		var buffer = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(buffer);
		writer.WriteStartObject();
		writer.WritePropertyName("items");
		writer.WriteStartArray();
		WriteAngleSharpDocument(document.DocumentElement, writer);
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
	}

	private static ValueTask ProcessHtmlAgilityPackAsync(byte[] bytes, CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		using var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
		var document = new HtmlDocument();
		document.Load(stream);

		var buffer = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(buffer);
		writer.WriteStartObject();
		writer.WritePropertyName("items");
		writer.WriteStartArray();
		WriteHtmlAgilityPackDocument(document.DocumentNode, writer);
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		return ValueTask.CompletedTask;
	}

	private static void WriteHypernetMetaItem(ref HtmlReader reader, Utf8JsonWriter writer)
	{
		if (!reader.TryGetAttribute("content", out var content))
		{
			return;
		}

		if (reader.TryGetAttribute("property", out var key) || reader.TryGetAttribute("name", out key))
		{
			WriteMetadataItem(writer, key, content);
		}
	}

	private static void WriteAngleSharpDocument(AngleSharp.Dom.INode node, Utf8JsonWriter writer)
	{
		if (node is AngleSharp.Dom.IElement element)
		{
			WriteElementItem(writer, element.LocalName, element);
		}

		foreach (var child in node.ChildNodes)
		{
			WriteAngleSharpDocument(child, writer);
		}
	}

	private static void WriteHtmlAgilityPackDocument(HtmlNode node, Utf8JsonWriter writer)
	{
		if (node.NodeType == HtmlNodeType.Element)
		{
			WriteElementItem(writer, node.Name, node);
		}

		foreach (var child in node.ChildNodes)
		{
			WriteHtmlAgilityPackDocument(child, writer);
		}
	}

	private static void WriteElementItem(Utf8JsonWriter writer, string name, AngleSharp.Dom.IElement element)
	{
		if (name.Equals("title", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "title", element.TextContent);
			return;
		}

		if (name.Equals("link", StringComparison.OrdinalIgnoreCase))
		{
			if (IsCanonicalLink(element.GetAttribute("rel")) && element.GetAttribute("href") is { } href)
			{
				WriteItem(writer, "canonical", href);
			}

			return;
		}

		if (name.Equals("meta", StringComparison.OrdinalIgnoreCase))
		{
			WriteMetadataItem(writer, element.GetAttribute("property") ?? element.GetAttribute("name"), element.GetAttribute("content"));
			return;
		}

		if (name.Equals("a", StringComparison.OrdinalIgnoreCase) && element.GetAttribute("href") is { } anchorHref)
		{
			WriteItem(writer, "link", anchorHref);
		}
	}

	private static void WriteElementItem(Utf8JsonWriter writer, string name, HtmlNode node)
	{
		if (name.Equals("title", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "title", node.InnerText);
			return;
		}

		if (name.Equals("link", StringComparison.OrdinalIgnoreCase))
		{
			if (IsCanonicalLink(node.Attributes["rel"]?.Value) && node.Attributes["href"]?.Value is { } href)
			{
				WriteItem(writer, "canonical", href);
			}

			return;
		}

		if (name.Equals("meta", StringComparison.OrdinalIgnoreCase))
		{
			WriteMetadataItem(writer, node.Attributes["property"]?.Value ?? node.Attributes["name"]?.Value, node.Attributes["content"]?.Value);
			return;
		}

		if (name.Equals("a", StringComparison.OrdinalIgnoreCase) && node.Attributes["href"]?.Value is { } anchorHref)
		{
			WriteItem(writer, "link", anchorHref);
		}
	}

	private static void WriteMetadataItem(Utf8JsonWriter writer, ReadOnlySpan<char> key, ReadOnlySpan<char> content)
	{
		if (key.Equals("description", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "description", content);
		}
		else if (key.Equals("og:title", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "og:title", content);
		}
		else if (key.Equals("og:url", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "og:url", content);
		}
		else if (key.Equals("og:description", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "og:description", content);
		}
		else if (key.Equals("article:author", StringComparison.OrdinalIgnoreCase))
		{
			WriteItem(writer, "article:author", content);
		}
	}

	private static void WriteMetadataItem(Utf8JsonWriter writer, string? key, string? content)
	{
		if (key is null || content is null)
		{
			return;
		}

		WriteMetadataItem(writer, key.AsSpan(), content.AsSpan());
	}

	private static bool IsCanonicalLink(string? rel)
	{
		return rel is not null && rel.Equals("canonical", StringComparison.OrdinalIgnoreCase);
	}

	private static void WriteItem(Utf8JsonWriter writer, string kind, ReadOnlySpan<char> value)
	{
		writer.WriteStartObject();
		writer.WriteString("kind", kind);
		writer.WriteString("value", value);
		writer.WriteEndObject();
	}

	private readonly record struct ThroughputResult(string FileName, string Library, long Processed, TimeSpan Elapsed, TimeSpan P50, TimeSpan P99, long[] LatencyTicks)
	{
		public double ProcessedPerSecond => Processed / Math.Max(Elapsed.TotalSeconds, double.Epsilon);
	}
}
