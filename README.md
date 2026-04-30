![Hypernet logo](./Hypernet.png)

High-throughput HTML parsing for .NET, built for extraction workloads.

It is designed for cases where you want to read HTML once, pick out the pieces you care about, and move on.

Example use cases:
- link extraction
- metadata extraction
- title/body text extraction
- site classification engines
- search-engine style crawling and indexing

## What It Is Not

Hypernet is not a DOM builder.

It does not aim to provide:
- DOM mutation
- tree editing
- browser-style layout or rendering
- a general-purpose, fully materialized HTML object model

## Quick start

```sh
dotnet add package Hypernet
```

```csharp
using var content = await HtmlContent.CreateAsync(stream, cancellationToken);
using var reader = new HtmlReader(content.Span);
while (reader.Read())
{
	if (reader.Token == HtmlToken.StartTag
		&& reader.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
		&& reader.TryGetAttribute("href", out var href))
	{
		// Process the link.
	}
}
```
- Knobs for creating `HtmlContent`: [HtmlContentOptions](./Hypernet/src/HtmlContentOptions.cs)
- Knobs for `HtmlReader`: [HtmlReaderOptions](./Hypernet/src/HtmlReaderOptions.cs)


## Benchmarks

All benchmarks compare Hypernet against AngleSharp and HtmlAgilityPack on the same real-world fixtures,
such as a Wikipedia article.

Specs:
```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8246/25H2/2025Update/HudsonValley2)
Intel Core Ultra 5 228V 2.10GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.202
  [Host]     : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.6 (10.0.6, 10.0.626.17701), X64 RyuJIT x86-64-v3
```

### Throughput benchmark

The throughput benchmark measures a search-engine style workload:
- read HTML from streams
- parse metadata and links while reading
- write the selected data to small structured JSON
- avoid intermediate lists or arrays

This benchmark exists because microbenchmarks alone do not tell the full story.
In real ingestion workloads, GC pressure, allocation profile, and sustained parallel processing matter just as much
as single-operation latency. The goal is to model the kind of work a crawler or indexer actually does:
consume HTML once, extract what matters, and keep moving.

#### Results

```
dotnet run -c Release --project Hypernet.Benchmarks
  -- \
  --throughput \
  --duration 00:05:00 \
  --warmup 00:00:10 \
  --concurrency 16
```

| Library | Rate (c/s) | P50 (us) | P99 (us) |
|---|---:|---:|---:|
| Hypernet | 24,499 c/s | 237.200 us | 10171.300 us |
| AngleSharp | 181 c/s | 81988.400 us | 192281.900 us |
| HtmlAgilityPack | 162 c/s | 90109.700 us | 227061.900 us |

Findings:
- On this machine, Hypernet is processing roughly `24.5k` documents per second in this workload, while AngleSharp and HtmlAgilityPack are both around `160-180 c/s`.
- The median document latency for Hypernet is about `237 us`, versus roughly `82-90 ms` for the other two libraries.
- Hypernet's P99 is about `10 ms`, which is still far below the comparison libraries' `190-227 ms` tail latencies.
- The gap is large enough that it should be read as a workload-specific throughput result rather than a small tuning improvement.

### Microbenchmarks

These benchmarks are useful for watching regression risk and allocation profile, but they are still microbenchmarks rather than a full usage model.

#### Link Extraction
| Method | FileName | Mean | Ratio | Allocated |
|---|---|---|---|---|
| Hypernet_CountAnchorHrefs | githu(...).html [30] | 212.85 us | 1.00 | - |
| AngleSharp_CountAnchorHrefs | githu(...).html [30] | 6,772.55 us | 31.82 | 4577273 B |
| HtmlAgilityPack_CountAnchorHrefs | githu(...).html [30] | 3,529.78 us | 16.58 | 4543601 B |
| Hypernet_ExtractAnchorHrefsToList | githu(...).html [30] | 229.56 us | 1.08 | 37888 B |
| AngleSharp_ExtractAnchorHrefsToList | githu(...).html [30] | 7,018.12 us | 32.97 | 4744697 B |
| HtmlAgilityPack_ExtractAnchorHrefsToList | githu(...).html [30] | 3,549.43 us | 16.68 | 4581489 B |
| Hypernet_WriteAnchorHrefsToJson | githu(...).html [30] | 220.54 us | 1.04 | 136 B |
| AngleSharp_WriteAnchorHrefsToJson | githu(...).html [30] | 7,041.27 us | 33.08 | 4736517 B |
| HtmlAgilityPack_WriteAnchorHrefsToJson | githu(...).html [30] | 3,632.62 us | 17.07 | 4573241 B |
|  |  |  |  |  |
| Hypernet_CountAnchorHrefs | mdn_using_fetch.html | 93.32 us | 1.00 | - |
| AngleSharp_CountAnchorHrefs | mdn_using_fetch.html | 2,731.02 us | 29.27 | 1823846 B |
| HtmlAgilityPack_CountAnchorHrefs | mdn_using_fetch.html | 1,376.60 us | 14.75 | 1997000 B |
| Hypernet_ExtractAnchorHrefsToList | mdn_using_fetch.html | 98.67 us | 1.06 | 35712 B |
| AngleSharp_ExtractAnchorHrefsToList | mdn_using_fetch.html | 2,810.41 us | 30.12 | 1936985 B |
| HtmlAgilityPack_ExtractAnchorHrefsToList | mdn_using_fetch.html | 1,416.88 us | 15.18 | 2032712 B |
| Hypernet_WriteAnchorHrefsToJson | mdn_using_fetch.html | 101.57 us | 1.09 | 136 B |
| AngleSharp_WriteAnchorHrefsToJson | mdn_using_fetch.html | 2,732.50 us | 29.28 | 1873134 B |
| HtmlAgilityPack_WriteAnchorHrefsToJson | mdn_using_fetch.html | 1,397.58 us | 14.98 | 2024464 B |
|  |  |  |  |  |
| Hypernet_CountAnchorHrefs | reute(...).html [31] | 130.56 us | 1.00 | - |
| AngleSharp_CountAnchorHrefs | reute(...).html [31] | 5,349.41 us | 40.97 | 5419691 B |
| HtmlAgilityPack_CountAnchorHrefs | reute(...).html [31] | 1,901.57 us | 14.56 | 2336968 B |
| Hypernet_ExtractAnchorHrefsToList | reute(...).html [31] | 147.13 us | 1.13 | 27984 B |
| AngleSharp_ExtractAnchorHrefsToList | reute(...).html [31] | 5,447.00 us | 41.72 | 5492696 B |
| HtmlAgilityPack_ExtractAnchorHrefsToList | reute(...).html [31] | 1,933.83 us | 14.81 | 2364632 B |
| Hypernet_WriteAnchorHrefsToJson | reute(...).html [31] | 155.11 us | 1.19 | 136 B |
| AngleSharp_WriteAnchorHrefsToJson | reute(...).html [31] | 5,419.58 us | 41.51 | 5420065 B |
| HtmlAgilityPack_WriteAnchorHrefsToJson | reute(...).html [31] | 1,890.98 us | 14.48 | 2360504 B |
|  |  |  |  |  |
| Hypernet_CountAnchorHrefs | stack(...).html [43] | 106.81 us | 1.00 | - |
| AngleSharp_CountAnchorHrefs | stack(...).html [43] | 3,623.81 us | 33.93 | 2608396 B |
| HtmlAgilityPack_CountAnchorHrefs | stack(...).html [43] | 1,732.72 us | 16.22 | 2254408 B |
| Hypernet_ExtractAnchorHrefsToList | stack(...).html [43] | 111.35 us | 1.04 | 31632 B |
| AngleSharp_ExtractAnchorHrefsToList | stack(...).html [43] | 3,747.46 us | 35.08 | 2736198 B |
| HtmlAgilityPack_ExtractAnchorHrefsToList | stack(...).html [43] | 1,668.15 us | 15.62 | 2286112 B |
| Hypernet_WriteAnchorHrefsToJson | stack(...).html [43] | 115.64 us | 1.08 | 136 B |
| AngleSharp_WriteAnchorHrefsToJson | stack(...).html [43] | 3,809.69 us | 35.67 | 2731829 B |
| HtmlAgilityPack_WriteAnchorHrefsToJson | stack(...).html [43] | 1,685.31 us | 15.78 | 2281984 B |
|  |  |  |  |  |
| Hypernet_CountAnchorHrefs | wikip(...).html [34] | 151.37 us | 1.00 | - |
| AngleSharp_CountAnchorHrefs | wikip(...).html [34] | 3,995.21 us | 26.39 | 2535074 B |
| HtmlAgilityPack_CountAnchorHrefs | wikip(...).html [34] | 2,266.46 us | 14.97 | 3428337 B |
| Hypernet_ExtractAnchorHrefsToList | wikip(...).html [34] | 165.75 us | 1.10 | 75064 B |
| AngleSharp_ExtractAnchorHrefsToList | wikip(...).html [34] | 4,096.24 us | 27.06 | 2736295 B |
| HtmlAgilityPack_ExtractAnchorHrefsToList | wikip(...).html [34] | 2,389.58 us | 15.79 | 3503401 B |
| Hypernet_WriteAnchorHrefsToJson | wikip(...).html [34] | 173.61 us | 1.15 | 136 B |
| AngleSharp_WriteAnchorHrefsToJson | wikip(...).html [34] | 4,164.53 us | 27.51 | 2719485 B |
| HtmlAgilityPack_WriteAnchorHrefsToJson | wikip(...).html [34] | 2,402.65 us | 15.87 | 3486937 B |

The link extraction benchmarks stay strongly in Hypernet's favor across all fixtures. The JSON-writing paths still only carry the small Utf8JsonWriter baseline allocation, while the comparison libraries remain much higher in both time and memory.

#### Metadata Extraction
| Method | FileName | Mean | Ratio | Allocated |
|---|---|---|---|---|
| Hypernet_ReadMetadataChecksum | githu(...).html [30] | 218.28 us | 1.00 | - |
| AngleSharp_ReadMetadataChecksum | githu(...).html [30] | 6,903.39 us | 31.63 | 4579661 B |
| HtmlAgilityPack_ReadMetadataChecksum | githu(...).html [30] | 3,582.15 us | 16.41 | 4556017 B |
| Hypernet_ReadMetadataDto | githu(...).html [30] | 214.92 us | 0.98 | 1016 B |
| AngleSharp_ReadMetadataDto | githu(...).html [30] | 6,872.29 us | 31.48 | 4579808 B |
| HtmlAgilityPack_ReadMetadataDto | githu(...).html [30] | 3,580.84 us | 16.41 | 4556092 B |
| Hypernet_WriteMetadataToJson | githu(...).html [30] | 249.60 us | 1.14 | 136 B |
| AngleSharp_WriteMetadataToJson | githu(...).html [30] | 6,915.74 us | 31.68 | 4579856 B |
| HtmlAgilityPack_WriteMetadataToJson | githu(...).html [30] | 3,604.68 us | 16.51 | 4556153 B |
|  |  |  |  |  |
| Hypernet_ReadMetadataChecksum | mdn_using_fetch.html | 91.14 us | 1.00 | - |
| AngleSharp_ReadMetadataChecksum | mdn_using_fetch.html | 2,722.97 us | 29.88 | 1874411 B |
| HtmlAgilityPack_ReadMetadataChecksum | mdn_using_fetch.html | 1,392.04 us | 15.27 | 2001408 B |
| Hypernet_ReadMetadataDto | mdn_using_fetch.html | 88.72 us | 0.97 | 1048 B |
| AngleSharp_ReadMetadataDto | mdn_using_fetch.html | 2,736.65 us | 30.03 | 1873490 B |
| HtmlAgilityPack_ReadMetadataDto | mdn_using_fetch.html | 1,376.71 us | 15.11 | 2000960 B |
| Hypernet_WriteMetadataToJson | mdn_using_fetch.html | 89.64 us | 0.98 | 136 B |
| AngleSharp_WriteMetadataToJson | mdn_using_fetch.html | 2,745.84 us | 30.13 | 1825369 B |
| HtmlAgilityPack_WriteMetadataToJson | mdn_using_fetch.html | 1,397.51 us | 15.33 | 2001544 B |
|  |  |  |  |  |
| Hypernet_ReadMetadataChecksum | reute(...).html [31] | 150.25 us | 1.00 | - |
| AngleSharp_ReadMetadataChecksum | reute(...).html [31] | 5,349.36 us | 35.60 | 5427372 B |
| HtmlAgilityPack_ReadMetadataChecksum | reute(...).html [31] | 1,900.22 us | 12.65 | 2351304 B |
| Hypernet_ReadMetadataDto | reute(...).html [31] | 130.39 us | 0.87 | 1800 B |
| AngleSharp_ReadMetadataDto | reute(...).html [31] | 5,254.84 us | 34.98 | 5420188 B |
| HtmlAgilityPack_ReadMetadataDto | reute(...).html [31] | 1,947.88 us | 12.96 | 2347824 B |
| Hypernet_WriteMetadataToJson | reute(...).html [31] | 139.54 us | 0.93 | 136 B |
| AngleSharp_WriteMetadataToJson | reute(...).html [31] | 5,631.06 us | 37.48 | 5427874 B |
| HtmlAgilityPack_WriteMetadataToJson | reute(...).html [31] | 1,896.97 us | 12.63 | 2351440 B |
|  |  |  |  |  |
| Hypernet_ReadMetadataChecksum | stack(...).html [43] | 107.32 us | 1.00 | - |
| AngleSharp_ReadMetadataChecksum | stack(...).html [43] | 3,614.80 us | 33.68 | 2608805 B |
| HtmlAgilityPack_ReadMetadataChecksum | stack(...).html [43] | 1,710.12 us | 15.94 | 2259024 B |
| Hypernet_ReadMetadataDto | stack(...).html [43] | 107.15 us | 1.00 | 1256 B |
| AngleSharp_ReadMetadataDto | stack(...).html [43] | 3,751.54 us | 34.96 | 2662685 B |
| HtmlAgilityPack_ReadMetadataDto | stack(...).html [43] | 1,719.45 us | 16.02 | 2259096 B |
| Hypernet_WriteMetadataToJson | stack(...).html [43] | 108.08 us | 1.01 | 136 B |
| AngleSharp_WriteMetadataToJson | stack(...).html [43] | 3,641.04 us | 33.93 | 2608895 B |
| HtmlAgilityPack_WriteMetadataToJson | stack(...).html [43] | 1,711.42 us | 15.95 | 2259160 B |
|  |  |  |  |  |
| Hypernet_ReadMetadataChecksum | wikip(...).html [34] | 142.05 us | 1.00 | - |
| AngleSharp_ReadMetadataChecksum | wikip(...).html [34] | 3,993.75 us | 28.12 | 2535439 B |
| HtmlAgilityPack_ReadMetadataChecksum | wikip(...).html [34] | 2,270.94 us | 15.99 | 3432209 B |
| Hypernet_ReadMetadataDto | wikip(...).html [34] | 141.58 us | 1.00 | 312 B |
| AngleSharp_ReadMetadataDto | wikip(...).html [34] | 3,992.51 us | 28.11 | 2535602 B |
| HtmlAgilityPack_ReadMetadataDto | wikip(...).html [34] | 2,233.47 us | 15.72 | 3432281 B |
| Hypernet_WriteMetadataToJson | wikip(...).html [34] | 143.66 us | 1.01 | 136 B |
| AngleSharp_WriteMetadataToJson | wikip(...).html [34] | 3,960.76 us | 27.88 | 2535783 B |
| HtmlAgilityPack_WriteMetadataToJson | wikip(...).html [34] | 2,209.28 us | 15.55 | 3432345 B |

The metadata extraction benchmarks follow the same shape as the link results: Hypernet is much faster across the full fixture set, and its allocation footprint stays tiny except for the small JSON-writing baseline.

#### Body Excerpt
| Method | FileName | Mean | Gen0 | Gen1 | Gen2 | Allocated |
|---|---|---|---|---|---|---|
| Hypernet_ReadBodyExcerptChecksum | githu(...).html [30] | 263.83 us | 5.3711 | - | - | 23136 B |
| AngleSharp_ReadBodyExcerptChecksum | githu(...).html [30] | 7,186.74 us | 906.2500 | 750.0000 | 250.0000 | 4833371 B |
| HtmlAgilityPack_ReadBodyExcerptChecksum | githu(...).html [30] | 3,914.31 us | 785.1563 | 625.0000 | 35.1563 | 4710332 B |
| Hypernet_ReadBodyExcerptString | githu(...).html [30] | 249.17 us | 7.8125 | - | - | 33160 B |
| AngleSharp_ReadBodyExcerptString | githu(...).html [30] | 7,026.00 us | 921.8750 | 742.1875 | 250.0000 | 4835382 B |
| HtmlAgilityPack_ReadBodyExcerptString | githu(...).html [30] | 3,767.79 us | 781.2500 | 636.7188 | 39.0625 | 4710333 B |
| Hypernet_WriteBodyExcerptToJson | githu(...).html [30] | 256.70 us | 5.3711 | - | - | 23272 B |
| AngleSharp_WriteBodyExcerptToJson | githu(...).html [30] | 7,001.15 us | 921.8750 | 742.1875 | 250.0000 | 4835215 B |
| HtmlAgilityPack_WriteBodyExcerptToJson | githu(...).html [30] | 3,708.45 us | 789.0625 | 625.0000 | 27.3438 | 4710465 B |
| Hypernet_TryGetBodyExcerptToJson | githu(...).html [30] | 217.42 us | - | - | - | 136 B |
| Hypernet_TryGetBodyExcerptNormalizedToJson | githu(...).html [30] | 224.40 us | - | - | - | 136 B |
| Hypernet_ReadBodyExcerptChecksum | mdn_using_fetch.html | 123.31 us | 5.3711 | - | - | 23136 B |
| AngleSharp_ReadBodyExcerptChecksum | mdn_using_fetch.html | 2,806.27 us | 332.0313 | 214.8438 | 109.3750 | 2012765 B |
| HtmlAgilityPack_ReadBodyExcerptChecksum | mdn_using_fetch.html | 1,540.85 us | 392.5781 | 314.4531 | - | 2313544 B |
| Hypernet_ReadBodyExcerptString | mdn_using_fetch.html | 123.39 us | 7.8125 | - | - | 33160 B |
| AngleSharp_ReadBodyExcerptString | mdn_using_fetch.html | 2,976.24 us | 332.0313 | 214.8438 | 109.3750 | 2012765 B |
| HtmlAgilityPack_ReadBodyExcerptString | mdn_using_fetch.html | 1,576.69 us | 392.5781 | 314.4531 | - | 2313544 B |
| Hypernet_WriteBodyExcerptToJson | mdn_using_fetch.html | 124.00 us | 5.3711 | - | - | 23272 B |
| AngleSharp_WriteBodyExcerptToJson | mdn_using_fetch.html | 2,780.19 us | 332.0313 | 214.8438 | 109.3750 | 2012901 B |
| HtmlAgilityPack_WriteBodyExcerptToJson | mdn_using_fetch.html | 1,461.73 us | 392.5781 | 314.4531 | - | 2313680 B |
| Hypernet_TryGetBodyExcerptToJson | mdn_using_fetch.html | 90.64 us | - | - | - | 136 B |
| Hypernet_TryGetBodyExcerptNormalizedToJson | mdn_using_fetch.html | 98.31 us | - | - | - | 136 B |
| Hypernet_ReadBodyExcerptChecksum | reute(...).html [31] | 428.65 us | 0.9766 | - | - | 4656 B |
| AngleSharp_ReadBodyExcerptChecksum | reute(...).html [31] | 5,792.35 us | 1015.6250 | 953.1250 | 609.3750 | 6463348 B |
| HtmlAgilityPack_ReadBodyExcerptChecksum | reute(...).html [31] | 1,991.48 us | 421.8750 | 332.0313 | - | 2418056 B |
| Hypernet_ReadBodyExcerptString | reute(...).html [31] | 428.22 us | 3.4180 | - | - | 14680 B |
| AngleSharp_ReadBodyExcerptString | reute(...).html [31] | 5,777.43 us | 976.5625 | 906.2500 | 570.3125 | 6463222 B |
| HtmlAgilityPack_ReadBodyExcerptString | reute(...).html [31] | 1,998.85 us | 421.8750 | 324.2188 | - | 2418056 B |
| Hypernet_WriteBodyExcerptToJson | reute(...).html [31] | 433.45 us | 0.9766 | - | - | 4792 B |
| AngleSharp_WriteBodyExcerptToJson | reute(...).html [31] | 5,769.06 us | 992.1875 | 921.8750 | 585.9375 | 6463528 B |
| HtmlAgilityPack_WriteBodyExcerptToJson | reute(...).html [31] | 1,861.98 us | 421.8750 | 320.3125 | - | 2418192 B |
| Hypernet_TryGetBodyExcerptToJson | reute(...).html [31] | 144.79 us | - | - | - | 136 B |
| Hypernet_TryGetBodyExcerptNormalizedToJson | reute(...).html [31] | 144.30 us | - | - | - | 136 B |
| Hypernet_ReadBodyExcerptChecksum | stack(...).html [43] | 152.68 us | 5.3711 | - | - | 23136 B |
| AngleSharp_ReadBodyExcerptChecksum | stack(...).html [43] | 3,851.11 us | 500.0000 | 296.8750 | 195.3125 | 2999834 B |
| HtmlAgilityPack_ReadBodyExcerptChecksum | stack(...).html [43] | 1,748.56 us | 404.2969 | 378.9063 | - | 2491296 B |
| Hypernet_ReadBodyExcerptString | stack(...).html [43] | 149.78 us | 7.8125 | - | - | 33160 B |
| AngleSharp_ReadBodyExcerptString | stack(...).html [43] | 3,823.25 us | 492.1875 | 289.0625 | 195.3125 | 2999834 B |
| HtmlAgilityPack_ReadBodyExcerptString | stack(...).html [43] | 1,741.12 us | 404.2969 | 378.9063 | - | 2491296 B |
| Hypernet_WriteBodyExcerptToJson | stack(...).html [43] | 154.95 us | 5.3711 | - | - | 23272 B |
| AngleSharp_WriteBodyExcerptToJson | stack(...).html [43] | 3,848.50 us | 492.1875 | 289.0625 | 195.3125 | 2999970 B |
| HtmlAgilityPack_WriteBodyExcerptToJson | stack(...).html [43] | 1,833.20 us | 404.2969 | 378.9063 | - | 2491432 B |
| Hypernet_TryGetBodyExcerptToJson | stack(...).html [43] | 110.15 us | - | - | - | 136 B |
| Hypernet_TryGetBodyExcerptNormalizedToJson | stack(...).html [43] | 111.76 us | - | - | - | 136 B |
| Hypernet_ReadBodyExcerptChecksum | wikip(...).html [34] | 210.19 us | 11.2305 | - | - | 47736 B |
| AngleSharp_ReadBodyExcerptChecksum | wikip(...).html [34] | 4,229.08 us | 453.1250 | 296.8750 | 148.4375 | 2781090 B |
| HtmlAgilityPack_ReadBodyExcerptChecksum | wikip(...).html [34] | 2,556.33 us | 609.3750 | 554.6875 | 27.3438 | 3758329 B |
| Hypernet_ReadBodyExcerptString | wikip(...).html [34] | 203.49 us | 13.6719 | - | - | 57760 B |
| AngleSharp_ReadBodyExcerptString | wikip(...).html [34] | 4,164.81 us | 453.1250 | 296.8750 | 148.4375 | 2781090 B |
| HtmlAgilityPack_ReadBodyExcerptString | wikip(...).html [34] | 2,574.30 us | 609.3750 | 554.6875 | 27.3438 | 3758329 B |
| Hypernet_WriteBodyExcerptToJson | wikip(...).html [34] | 216.62 us | 11.2305 | - | - | 47872 B |
| AngleSharp_WriteBodyExcerptToJson | wikip(...).html [34] | 4,168.85 us | 460.9375 | 312.5000 | 148.4375 | 2779746 B |
| HtmlAgilityPack_WriteBodyExcerptToJson | wikip(...).html [34] | 2,530.90 us | 605.4688 | 566.4063 | 27.3438 | 3758465 B |
| Hypernet_TryGetBodyExcerptToJson | wikip(...).html [34] | 151.11 us | - | - | - | 136 B |
| Hypernet_TryGetBodyExcerptNormalizedToJson | wikip(...).html [34] | 161.10 us | - | - | - | 136 B |

The body excerpt benchmarks show the same pattern across all fixtures: Hypernet stays much faster and lower-allocation than the comparison libraries, with the TryGet variants remaining the leanest.

* The `136 B` allocations in the JSON-writing benchmarks are the Utf8JsonWriter baseline.
