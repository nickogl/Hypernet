using BenchmarkDotNet.Running;
using Hypernet.Benchmarks;

if (args.Any(static arg => arg.Equals("--throughput", StringComparison.OrdinalIgnoreCase)))
{
	await ThroughputBenchmarks.RunAsync(new()
	{
		MaxDegreeOfParallelism = ParseConcurrency(args),
		Duration = ParseDuration(args),
		WarmupDuration = ParseWarmup(args),
	});
}
else
{
	BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

static int ParseConcurrency(string[] args)
{
	var value = GetOptionValue(args, "--concurrency");
	if (value is null)
	{
		return Environment.ProcessorCount;
	}

	if (int.TryParse(value, out var concurrency) && concurrency > 0)
	{
		return concurrency;
	}

	throw new ArgumentException($"Invalid --concurrency value '{value}'. Use a positive integer.");
}

static TimeSpan ParseDuration(string[] args)
{
	var value = GetOptionValue(args, "--duration");
	if (value is null)
	{
		return TimeSpan.FromSeconds(30);
	}

	if (TimeSpan.TryParse(value, out var duration))
	{
		return duration;
	}

	throw new ArgumentException($"Invalid --duration value '{value}'. Use a .NET TimeSpan format such as 00:00:10.");
}

static TimeSpan ParseWarmup(string[] args)
{
	var value = GetOptionValue(args, "--warmup");
	if (value is null)
	{
		return TimeSpan.FromSeconds(5);
	}

	if (TimeSpan.TryParse(value, out var duration))
	{
		return duration;
	}

	throw new ArgumentException($"Invalid --warmup value '{value}'. Use a .NET TimeSpan format such as 00:00:05.");
}

static string? GetOptionValue(string[] args, string optionName)
{
	for (var i = 0; i < args.Length; i++)
	{
		if (!args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase))
		{
			continue;
		}

		if (i + 1 >= args.Length)
		{
			throw new ArgumentException($"Missing value after {optionName}.");
		}

		return args[i + 1];
	}

	return null;
}
