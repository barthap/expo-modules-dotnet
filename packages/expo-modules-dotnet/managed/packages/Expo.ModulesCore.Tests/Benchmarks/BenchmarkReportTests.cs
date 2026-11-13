using System.Globalization;
using Expo.ModulesCore.Benchmarks;
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
      bool isAsync = false)
  {
    return new BenchmarkRow(
        new BenchmarkCase(name, direction, "", "m.f()", control is null ? null : "n.f()", "true", isAsync),
        median, median - 1, spread, 8, 2, 1, 3, control);
  }

  private static string[] Cells(string line) =>
      [.. line.Trim('|').Split('|').Select(cell => cell.Trim())];

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
  public void NoisyRows_ReturnsOnlyRowsAboveTheLimit()
  {
    var quiet = Row(name: "quiet", spread: BenchmarkStatistics.NoisySpreadLimit);
    var noisy = Row(name: "noisy", spread: BenchmarkStatistics.NoisySpreadLimit + 0.01);

    Assert.Equal([noisy], BenchmarkReport.NoisyRows([quiet, noisy]));
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
