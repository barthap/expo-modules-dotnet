using System.Text.RegularExpressions;
using Expo.ModulesCore.Benchmarks;

const int ExitNoisy = 1;
const int ExitNoMatch = 2;
const int ExitCaseFailed = 3;
const int ExitUsage = 64;

string? filter = null;
for (var index = 0; index < args.Length; index++)
{
  switch (args[index])
  {
    case "--help":
      Console.WriteLine("Usage: Expo.ModulesCore.Benchmarks [--filter <regex>] [--help]");
      Console.WriteLine("  --filter <regex>  run only the cases whose \"<name> (<direction>)\" text matches");
      Console.WriteLine("  exit codes: 0 success, 1 noisy rows, 2 no case matches, 3 a case failed");
      return 0;
    case "--filter" when index + 1 < args.Length:
      filter = args[++index];
      break;
    default:
      Console.Error.WriteLine($"Unknown or incomplete argument: {args[index]}. Use --help.");
      return ExitUsage;
  }
}

Regex? pattern;
try
{
  pattern = filter is null ? null : new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
catch (ArgumentException exception)
{
  Console.Error.WriteLine($"Invalid --filter regex: {exception.Message}");
  return ExitUsage;
}

// The filter text carries the direction, so one direction of a pair can be selected.
static string FilterText(BenchmarkCase benchmarkCase) => benchmarkCase.Direction == BenchmarkDirection.None
    ? benchmarkCase.Name
    : $"{benchmarkCase.Name} ({BenchmarkReport.DirectionLabel(benchmarkCase.Direction)})";

var selected = BenchmarkCases.All
    .Where(benchmarkCase => pattern is null || pattern.IsMatch(FilterText(benchmarkCase)))
    .ToList();
if (selected.Count == 0)
{
  Console.Error.WriteLine($"No case matches \"{filter}\". Available cases:");
  foreach (var benchmarkCase in BenchmarkCases.All)
  {
    Console.Error.WriteLine($"  {FilterText(benchmarkCase)}");
  }

  return ExitNoMatch;
}

var rows = new List<BenchmarkRow>();
using (var host = BenchmarkCases.CreateHost())
{
  foreach (var benchmarkCase in selected)
  {
    Console.Error.WriteLine($"running {FilterText(benchmarkCase)}");
    try
    {
      rows.Add(await BenchmarkRunner.RunAsync(host, benchmarkCase, RunnerOptions.Default));
    }
    catch (Exception exception)
    {
      // No partial table: the run stops and nothing goes to stdout.
      Console.Error.WriteLine($"Case \"{FilterText(benchmarkCase)}\" failed: {exception.Message}");
      return ExitCaseFailed;
    }
  }
}

Console.WriteLine(BenchmarkReport.Header());
Console.WriteLine();
Console.WriteLine(BenchmarkReport.Table(rows));
Console.WriteLine();
Console.WriteLine("async rows use the testhost executor");

var noisy = BenchmarkReport.NoisyRows(rows);
foreach (var row in noisy)
{
  Console.WriteLine(
      $"warning: {FilterText(row.Case)} is noisy (spread {row.Spread.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} above {BenchmarkStatistics.NoisySpreadLimit.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})");
}

return noisy.Count > 0 ? ExitNoisy : 0;
