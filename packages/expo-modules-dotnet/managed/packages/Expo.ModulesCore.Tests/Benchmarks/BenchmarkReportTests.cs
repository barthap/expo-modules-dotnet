using System.Globalization;
using Expo.ModulesCore.Benchmarks;
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Benchmarks;

// The table is the product of the tool. A shifted column, a culture-dependent number, or a missing
// marker would make a comparison between two machines or two runs wrong.
public sealed class BenchmarkReportTests
{
  private static BenchmarkRow Row(
      string name = "case",
      BenchmarkDirection direction = BenchmarkDirection.RoundTrip,
      double median = 100,
      double spread = 1.1,
      double? control = 40,
      bool isAsync = false,
      double step = 1.0,
      double? controlSpread = 1.0,
      double? controlStep = 1.0,
      string call = "m.f()")
  {
    return new BenchmarkRow(
        new BenchmarkCase(name, direction, "", call, control is null ? null : "n.f()", "true", isAsync),
        median, median - 1, spread, step, 8, 2, 1, 3, control,
        control is null ? null : controlSpread,
        control is null ? null : controlStep);
  }

  // The report ends lines with Environment.NewLine, so a line split on '\n' keeps a '\r' on Windows.
  private static string[] Cells(string line) =>
      [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

  [Fact]
  public void Table_HasTheElevenColumnsInOrder()
  {
    var lines = BenchmarkReport.Table([Row(median: 100)]).Split('\n');

    Assert.Equal(
        [
          "case", "direction", "median ns/call", "min ns/call", "spread", "bytes/call",
          "owned linked", "owned unlinked", "borrowed", "native ns/call", "bridge ns/call",
        ],
        Cells(lines[0]));
    Assert.Equal(
        ["case", "round trip", "100.0", "99.0", "1.10", "8.0", "2", "1", "3", "40.0", "60.0"],
        Cells(lines[2]));
  }

  [Fact]
  public void Table_MarksNoisyAndAsyncRows()
  {
    var table = BenchmarkReport.Table([Row(spread: 1.5), Row(name: "async", isAsync: true, control: null)]);

    Assert.Contains("1.50 (noisy)", table);
    Assert.Contains("async (testhost executor)", table);
    Assert.DoesNotContain("1.10 (noisy)", table);
  }

  [Fact]
  public void Table_MarksANoisyControlAfterTheNativeValue()
  {
    var cells = Cells(BenchmarkReport.Table([Row(controlSpread: 1.5)]).Split('\n')[2]);

    Assert.Equal("1.10", cells[4]);
    Assert.Equal("40.0 (noisy)", cells[9]);
  }

  [Fact]
  public void Table_MarksASteppedManagedRowInTheSpreadColumn()
  {
    var cells = Cells(BenchmarkReport.Table([Row(spread: 1.0, step: 1.5)]).Split('\n')[2]);

    Assert.Equal("1.00 (noisy)", cells[4]);
    Assert.Equal("40.0", cells[9]);
  }

  // The floor row makes no call, so it creates no handle. A zero would read as a measured result.
  [Fact]
  public void Table_PrintsDashesInTheHandleColumnsOfTheFloorRow()
  {
    var cells = Cells(BenchmarkReport.Table([Row(direction: BenchmarkDirection.None, control: null, call: "")]).Split('\n')[2]);

    Assert.Equal(["-", "-", "-"], cells[6..9]);
    Assert.Equal("8.0", cells[5]);
  }

  [Fact]
  public void Table_PrintsDashForMissingControl()
  {
    var row = Cells(BenchmarkReport.Table([Row(direction: BenchmarkDirection.None, control: null)]).Split('\n')[2]);

    Assert.Equal("-", row[1]);
    Assert.Equal("-", row[9]);
    Assert.Equal("-", row[10]);
  }

  [Fact]
  public void Table_UsesInvariantCulture()
  {
    var previous = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
    try
    {
      var table = BenchmarkReport.Table([Row(median: 1.5)]);

      Assert.Contains("1.5", table);
      Assert.DoesNotContain("1,5", table);
    }
    finally
    {
      CultureInfo.CurrentCulture = previous;
    }
  }

  [Fact]
  public void Header_HasNoMachineNameUserNameOrPath()
  {
    var header = BenchmarkReport.Header();

    Assert.DoesNotContain(Environment.MachineName, header, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(Environment.UserName, header, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(Path.DirectorySeparatorChar + Environment.UserName, header, StringComparison.OrdinalIgnoreCase);
    Assert.Contains(File.ReadAllText(HermesRefPath()).Trim(), header);
  }

  [Fact]
  public void Header_NamesTheJitAndTheTieredCompilationSettings()
  {
    var header = BenchmarkReport.Header();

    Assert.Contains("jit: RyuJIT", header);
    Assert.Contains("tiered compilation: ", header);
    Assert.Contains("tiered PGO: ", header);
    Assert.Contains("quick JIT for loops: ", header);
  }

  // The runtime lets a DOTNET_ environment variable win over the runtime config since .NET 9, so a
  // header that read the config first would print "off" while the runtime tiers. The seam passes the
  // environment and the switch in, so no test changes process-wide state.
  [Theory]
  [InlineData("1", false, "on (environment)")]
  [InlineData("0", true, "off (environment)")]
  [InlineData(null, false, "off (runtime config)")]
  [InlineData(null, true, "on (runtime config)")]
  [InlineData(null, null, "runtime default")]
  public void RuntimeSetting_TheEnvironmentVariableWinsOverTheRuntimeConfig(
      string? environmentValue,
      bool? configValue,
      string expected)
  {
    var text = BenchmarkReport.RuntimeSetting(
        "Some.Switch",
        "SomeSetting",
        name => name == "DOTNET_SomeSetting" ? environmentValue : null,
        _ => configValue);

    Assert.Equal(expected, text);
  }

  [Fact]
  public void RuntimeSetting_ReadsTheLegacyEnvironmentPrefixWhenTheNewOneIsUnset()
  {
    var text = BenchmarkReport.RuntimeSetting(
        "Some.Switch",
        "SomeSetting",
        name => name == "COMPlus_SomeSetting" ? "0" : null,
        _ => true);

    Assert.Equal("off (environment)", text);
  }

  [Fact]
  public void Header_ReportsWhetherTheNativeTesthostIsOptimized()
  {
    var expected = HermesTestRuntime.NativeTestHostIsOptimized ? "optimized" : "not optimized";

    Assert.Contains($"native testhost: {expected}{Environment.NewLine}", BenchmarkReport.Header());
  }

  [Fact]
  public void SettingWarnings_AreEmptyWhenTieringIsOffAndTheTesthostIsOptimized()
  {
    Assert.Empty(BenchmarkReport.SettingWarnings(tieredCompilationIsOff: true, nativeTesthostIsOptimized: true));
  }

  [Fact]
  public void SettingWarnings_NameEachSettingThatMakesTheMediansUnreliable()
  {
    var tiered = Assert.Single(BenchmarkReport.SettingWarnings(false, true));
    var native = Assert.Single(BenchmarkReport.SettingWarnings(true, false));

    Assert.Contains("tiered compilation is not off", tiered);
    Assert.Contains("native testhost is not optimized", native);
    Assert.Equal(2, BenchmarkReport.SettingWarnings(false, false).Count);
  }

  // The test process has its own runtime config, so the header of this process cannot show the switch of the
  // benchmark program. This proves that the benchmark build writes the switch to its runtime config, which
  // the runtime reads at start-up. A real run of the program shows the header line for it.
  [Fact]
  public void BenchmarkProgram_RuntimeConfigDisablesTieredCompilation()
  {
    var path = Path.Combine(AppContext.BaseDirectory, "Expo.ModulesCore.Benchmarks.runtimeconfig.json");
    using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

    var properties = document.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
    Assert.False(properties.GetProperty("System.Runtime.TieredCompilation").GetBoolean());
  }

  [Fact]
  public void NoisyRows_ReturnsRowsWithNoisyManagedOrControlSamples()
  {
    var limit = BenchmarkStatistics.NoisySpreadLimit;
    var quiet = Row(name: "quiet", spread: limit, step: limit, controlSpread: limit, controlStep: limit);
    var wide = Row(name: "wide", spread: limit + 0.01);
    var stepped = Row(name: "stepped", step: limit + 0.01);
    var controlWide = Row(name: "control wide", controlSpread: limit + 0.01);
    var controlStepped = Row(name: "control stepped", controlStep: limit + 0.01);

    Assert.Equal(
        [wide, stepped, controlWide, controlStepped],
        BenchmarkReport.NoisyRows([quiet, wide, stepped, controlWide, controlStepped]));
  }

  [Fact]
  public void NoiseWarnings_SayWhichSamplesAreNoisy()
  {
    var warnings = BenchmarkReport.NoiseWarnings(
        [Row(name: "a", spread: 1.5), Row(name: "b", controlStep: 1.3), Row(name: "c", spread: 1.5, controlSpread: 1.4)]);

    Assert.Equal(
        [
          "warning: a (round trip) has noisy managed samples (spread 1.50, step 1.00, limit 1.20)",
          "warning: b (round trip) has noisy control samples (spread 1.00, step 1.30, limit 1.20)",
          "warning: c (round trip) has noisy managed samples (spread 1.50, step 1.00, limit 1.20)",
          "warning: c (round trip) has noisy control samples (spread 1.40, step 1.00, limit 1.20)",
        ],
        warnings);
  }

  [Fact]
  public void NoiseWarnings_AreEmptyForQuietRows()
  {
    Assert.Empty(BenchmarkReport.NoiseWarnings([Row(), Row(control: null)]));
  }

  private static string HermesRefPath()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "scripts", "hermes-ref.txt")))
    {
      directory = directory.Parent;
    }

    return Path.Combine(directory!.FullName, "scripts", "hermes-ref.txt");
  }
}
