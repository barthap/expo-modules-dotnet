using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Expo.ModulesCore.Testing;

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
    builder.AppendLine($"tiered compilation: {TieredCompilation}");
    builder.AppendLine($"tiered PGO: {RuntimeSetting("System.Runtime.TieredPGO", "TieredPGO")}");
    builder.AppendLine($"quick JIT for loops: {RuntimeSetting("System.Runtime.TieredCompilation.QuickJitForLoops", "TC_QuickJitForLoops")}");
    builder.AppendLine($"native testhost: {(HermesTestRuntime.NativeTestHostIsOptimized ? "optimized" : "not optimized")}");
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
    return [.. rows.Where(row => row.IsNoisy || row.IsControlNoisy)];
  }

  /// <summary>The case name, with the direction in parentheses when the case has one. The filter matches this text.</summary>
  public static string CaseLabel(BenchmarkCase benchmarkCase) => benchmarkCase.Direction == BenchmarkDirection.None
      ? benchmarkCase.Name
      : $"{benchmarkCase.Name} ({DirectionLabel(benchmarkCase.Direction)})";

  /// <summary>One line per noisy sample set. Each line says whether the managed or the control samples are noisy.</summary>
  public static IReadOnlyList<string> NoiseWarnings(IReadOnlyList<BenchmarkRow> rows)
  {
    var warnings = new List<string>();
    foreach (var row in rows)
    {
      if (row.IsNoisy)
      {
        warnings.Add(NoiseWarning(row, "managed", row.Spread, row.Step));
      }

      if (row.IsControlNoisy)
      {
        warnings.Add(NoiseWarning(row, "control", row.ControlSpread!.Value, row.ControlStep!.Value));
      }
    }

    return warnings;
  }

  private static string NoiseWarning(BenchmarkRow row, string samples, double spread, double step) =>
      $"warning: {CaseLabel(row.Case)} has noisy {samples} samples " +
      $"(spread {Ratio(spread)}, step {Ratio(step)}, limit {Ratio(BenchmarkStatistics.NoisySpreadLimit)})";

  private static string Ratio(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

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

  internal const string TieredCompilationSwitch = "System.Runtime.TieredCompilation";

  private static string TieredCompilation => RuntimeSetting(TieredCompilationSwitch, "TieredCompilation");

  /// <summary>Whether tiered compilation is explicitly off in this process. The default is on.</summary>
  public static bool TieredCompilationIsOff() => ReadSetting(TieredCompilationSwitch, "TieredCompilation").Enabled == false;

  /// <summary>
  /// One line per setting that makes the medians unreliable: tiered compilation that is not off, and a
  /// native testhost that is not optimized.
  /// </summary>
  public static IReadOnlyList<string> SettingWarnings(bool tieredCompilationIsOff, bool nativeTesthostIsOptimized)
  {
    var warnings = new List<string>();
    if (!tieredCompilationIsOff)
    {
      warnings.Add(
          "warning: tiered compilation is not off, so the medians are not steady state " +
          "(unset DOTNET_TieredCompilation, or set it to 0)");
    }

    if (!nativeTesthostIsOptimized)
    {
      warnings.Add(
          "warning: the native testhost is not optimized, so every native cost is inflated " +
          "(use scripts/bench-managed.sh, or build the testhost with CMAKE_BUILD_TYPE=Release)");
    }

    return warnings;
  }

  // Reads a setting that the runtime config or a DOTNET_ environment variable sets. The environment
  // variable wins over the runtime config, as the runtime does since .NET 9. When neither sets it,
  // the runtime default is in effect, and this does not say what that default is.
  internal static string RuntimeSetting(
      string configSwitch,
      string environmentName,
      Func<string, string?>? getEnvironment = null,
      Func<string, bool?>? getSwitch = null)
  {
    var (enabled, source) = ReadSetting(configSwitch, environmentName, getEnvironment, getSwitch);
    return enabled is null ? "runtime default" : $"{(enabled.Value ? "on" : "off")} ({source})";
  }

  private static (bool? Enabled, string Source) ReadSetting(
      string configSwitch,
      string environmentName,
      Func<string, string?>? getEnvironment = null,
      Func<string, bool?>? getSwitch = null)
  {
    getEnvironment ??= Environment.GetEnvironmentVariable;
    getSwitch ??= name => AppContext.TryGetSwitch(name, out var value) ? value : null;

    var fromEnvironment = getEnvironment($"DOTNET_{environmentName}") ?? getEnvironment($"COMPlus_{environmentName}");
    if (fromEnvironment is not null)
    {
      return (fromEnvironment != "0", "environment");
    }

    return getSwitch(configSwitch) is { } enabled ? (enabled, "runtime config") : (null, "");
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
    // The floor row has no call, so no handle is created and the handle columns do not apply.
    var hasCall = row.Case.Call.Length > 0;
    yield return row.Case.IsAsync ? $"{row.Case.Name} (testhost executor)" : row.Case.Name;
    yield return DirectionLabel(row.Case.Direction);
    yield return Nanoseconds(row.MedianNanoseconds);
    yield return Nanoseconds(row.MinNanoseconds);
    yield return Ratio(row.Spread) + (row.IsNoisy ? " (noisy)" : "");
    yield return Nanoseconds(row.BytesPerCall);
    yield return hasCall ? Handles(row.OwnedLinkedPerCall) : "-";
    yield return hasCall ? Handles(row.OwnedUnlinkedPerCall) : "-";
    yield return hasCall ? Handles(row.BorrowedPerCall) : "-";
    yield return row.ControlMedianNanoseconds is { } control
        ? Nanoseconds(control) + (row.IsControlNoisy ? " (noisy)" : "")
        : "-";
    yield return row.BridgeNanoseconds is { } bridge ? Nanoseconds(bridge) : "-";
  }

  private static string Nanoseconds(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

  private static string Handles(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

  private static string Line(IEnumerable<string> cells) => $"| {string.Join(" | ", cells)} |";
}
