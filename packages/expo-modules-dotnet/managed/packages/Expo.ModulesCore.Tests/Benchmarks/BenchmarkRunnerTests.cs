using Expo.ModulesCore.Benchmarks;
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Benchmarks;

// The runner turns a case into the numbers that the report compares. A wrong handle count, an async
// sample that ends before its promises settle, or a case that fails without a name would make the
// comparison wrong or hard to act on.
public sealed class BenchmarkRunnerTests
{
  private static readonly RunnerOptions Small = new(2, TimeSpan.FromMilliseconds(1), 10);

  private static BenchmarkCase Case(string name, BenchmarkDirection direction) =>
      BenchmarkCases.All.Single(benchmarkCase => benchmarkCase.Name == name && benchmarkCase.Direction == direction);

  private static double EvaluateNumber(ExpoModuleTestHost host, string source) =>
      host.Runtime.Execute(_ =>
      {
        using var result = host.Evaluate(source, "benchmark-test.js");
        return result.AsDouble();
      });

  [Fact]
  public async Task Run_NoArgumentCase_ReportsHandlesPerCallAsExactNumbers()
  {
    using var host = BenchmarkCases.CreateHost();

    var row = await BenchmarkRunner.RunAsync(host, Case("function: no arguments", BenchmarkDirection.None), Small);

    foreach (var value in new[] { row.OwnedLinkedPerCall, row.OwnedUnlinkedPerCall, row.BorrowedPerCall })
    {
      Assert.Equal(Math.Round(value), value);
    }

    Assert.NotNull(row.ControlMedianNanoseconds);
    Assert.True(row.MedianNanoseconds > 0);
  }

  [Fact]
  public async Task Run_NumbersVersusStrings_DifferOnlyInLinking()
  {
    using var host = BenchmarkCases.CreateHost();

    var numbers = await BenchmarkRunner.RunAsync(host, Case("array: 100 numbers", BenchmarkDirection.Argument), Small);
    var strings = await BenchmarkRunner.RunAsync(host, Case("array: 100 strings", BenchmarkDirection.Argument), Small);

    Assert.Equal(100, strings.OwnedLinkedPerCall - numbers.OwnedLinkedPerCall);
    Assert.Equal(100, numbers.OwnedUnlinkedPerCall - strings.OwnedUnlinkedPerCall);
  }

  [Fact]
  public async Task Run_AsyncCase_SettlesEveryPromiseBeforeItReturns()
  {
    using var host = BenchmarkCases.CreateHost();
    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate("globalThis.made = 0; globalThis.settled = 0; undefined", "benchmark-test.js");
      return true;
    });
    var asyncCase = new BenchmarkCase(
        "counting async",
        BenchmarkDirection.None,
        "",
        "(globalThis.made++, m.noopAsync().then(function () { globalThis.settled++; }))",
        null,
        "true",
        IsAsync: true);

    await BenchmarkRunner.RunAsync(host, asyncCase, Small);

    Assert.Equal(0, host.ActivePromiseEvaluationCount);
    var made = EvaluateNumber(host, "globalThis.made");
    Assert.True(made > 0);
    Assert.Equal(made, EvaluateNumber(host, "globalThis.settled"));
  }

  [Fact]
  public async Task Run_CaseWithFailingVerify_ThrowsAndNamesTheCase()
  {
    using var host = BenchmarkCases.CreateHost();
    var failing = new BenchmarkCase("never verifies", BenchmarkDirection.None, "", "m.noop()", null, "false");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => BenchmarkRunner.RunAsync(host, failing, Small));

    Assert.Contains("never verifies", exception.Message);
  }

  [Fact]
  public async Task Run_CaseWhoseCallThrows_ThrowsAndNamesTheCase()
  {
    using var host = BenchmarkCases.CreateHost();
    var throwing = new BenchmarkCase("call throws", BenchmarkDirection.None, "", "m.doesNotExist()", null, "true");

    var exception = await Assert.ThrowsAsync<InvalidOperationException>(
        () => BenchmarkRunner.RunAsync(host, throwing, Small));

    Assert.Contains("call throws", exception.Message);
  }

  [Fact]
  public async Task Run_FloorCase_HasNoControlAndZeroHandles()
  {
    using var host = BenchmarkCases.CreateHost();

    var row = await BenchmarkRunner.RunAsync(host, Case("JS: empty driver loop", BenchmarkDirection.None), Small);

    Assert.Null(row.ControlMedianNanoseconds);
    Assert.Equal(0, row.OwnedLinkedPerCall);
    Assert.Equal(0, row.OwnedUnlinkedPerCall);
    Assert.Equal(0, row.BorrowedPerCall);
  }
}
