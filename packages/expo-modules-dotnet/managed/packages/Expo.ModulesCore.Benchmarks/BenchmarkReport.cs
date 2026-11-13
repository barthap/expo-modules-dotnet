using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Expo.ModulesCore.Benchmarks;

internal static class BenchmarkReport
{
  private static readonly string[] Columns =
  [
    "case",
    "direction",
    "median ns/call",
    "min ns/call",
    "spread",
    "bytes/call",
    "owned linked",
    "owned unlinked",
    "borrowed",
    "native ns/call",
    "bridge ns/call",
  ];

  /// <summary>Describes the machine class and the build, without a machine name, user name or path.</summary>
  public static string Header()
  {
    var builder = new StringBuilder();
    builder.AppendLine("call-path benchmark");
    builder.AppendLine($"configuration: {Configuration}");
    builder.AppendLine($"runtime: {RuntimeInformation.FrameworkDescription}");
    builder.AppendLine($"jit: {(RuntimeFeature.IsDynamicCodeCompiled ? "RyuJIT (dynamic code compiled)" : "NativeAOT (no JIT)")}");
    builder.AppendLine($"tiered compilation: {RuntimeSetting("System.Runtime.TieredCompilation", "TieredCompilation")}");
    builder.AppendLine($"tiered PGO: {RuntimeSetting("System.Runtime.TieredPGO", "TieredPGO")}");
    builder.AppendLine($"quick JIT for loops: {RuntimeSetting("System.Runtime.TieredCompilation.QuickJitForLoops", "TC_QuickJitForLoops")}");
    builder.AppendLine($"platform: {RuntimeInformation.RuntimeIdentifier}");
    builder.AppendLine($"logical processors: {Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)}");
    builder.Append($"hermes ref: {HermesRef()}");
    return builder.ToString();
  }

  public static string Table(IReadOnlyList<BenchmarkRow> rows)
  {
    var builder = new StringBuilder();
    builder.AppendLine(Line(Columns));
    builder.AppendLine(Line(Columns.Select(_ => "---")));
    foreach (var row in rows)
    {
      builder.AppendLine(Line(Cells(row)));
    }

    return builder.ToString().TrimEnd();
  }

  public static IReadOnlyList<BenchmarkRow> NoisyRows(IReadOnlyList<BenchmarkRow> rows)
  {
    return [.. rows.Where(row => row.Spread > BenchmarkStatistics.NoisySpreadLimit)];
  }

  public static string DirectionLabel(BenchmarkDirection direction) => direction switch
  {
    BenchmarkDirection.None => "-",
    BenchmarkDirection.Argument => "argument",
    BenchmarkDirection.Return => "return",
    BenchmarkDirection.RoundTrip => "round trip",
    _ => throw new ArgumentOutOfRangeException(nameof(direction)),
  };

  private static string Configuration =>
#if DEBUG
      "Debug";
#else
      "Release";
#endif

  // Reads a setting that the runtime config or a DOTNET_ environment variable sets. When neither
  // sets it, the runtime default is in effect, and this does not say what that default is.
  private static string RuntimeSetting(string configSwitch, string environmentName)
  {
    if (AppContext.TryGetSwitch(configSwitch, out var enabled))
    {
      return enabled ? "on (runtime config)" : "off (runtime config)";
    }

    var fromEnvironment = Environment.GetEnvironmentVariable($"DOTNET_{environmentName}")
        ?? Environment.GetEnvironmentVariable($"COMPlus_{environmentName}");
    return fromEnvironment is null ? "runtime default" : $"{(fromEnvironment == "0" ? "off" : "on")} (environment)";
  }

  // The file is embedded at build time, so the program does not depend on the current directory.
  private static string HermesRef()
  {
    using var stream = typeof(BenchmarkReport).Assembly.GetManifestResourceStream("hermes-ref.txt")
        ?? throw new InvalidOperationException("The embedded hermes-ref.txt resource is missing.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd().Trim();
  }

  private static IEnumerable<string> Cells(BenchmarkRow row)
  {
    var isNoisy = row.Spread > BenchmarkStatistics.NoisySpreadLimit;
    yield return row.Case.IsAsync ? $"{row.Case.Name} (testhost executor)" : row.Case.Name;
    yield return DirectionLabel(row.Case.Direction);
    yield return Nanoseconds(row.MedianNanoseconds);
    yield return Nanoseconds(row.MinNanoseconds);
    yield return row.Spread.ToString("0.00", CultureInfo.InvariantCulture) + (isNoisy ? " (noisy)" : "");
    yield return Nanoseconds(row.BytesPerCall);
    yield return Handles(row.OwnedLinkedPerCall);
    yield return Handles(row.OwnedUnlinkedPerCall);
    yield return Handles(row.BorrowedPerCall);
    yield return row.ControlMedianNanoseconds is { } control ? Nanoseconds(control) : "-";
    yield return row.BridgeNanoseconds is { } bridge ? Nanoseconds(bridge) : "-";
  }

  private static string Nanoseconds(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

  private static string Handles(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

  private static string Line(IEnumerable<string> cells) => $"| {string.Join(" | ", cells)} |";
}
