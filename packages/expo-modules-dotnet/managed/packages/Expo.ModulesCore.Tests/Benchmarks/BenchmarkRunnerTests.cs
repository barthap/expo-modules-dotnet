using Expo.ModulesCore.Benchmarks;
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Benchmarks;

// The bytes counter is process-wide, so these tests run alone instead of beside other tests.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BenchmarkRunnerCollection
{
  public const string Name = "benchmark runner";
}

// The runner turns a case into the numbers that the report compares. A wrong handle count, an async
// sample that ends before its promises settle, or a case that fails without a name would make the
// comparison wrong or hard to act on.
[Collection(BenchmarkRunnerCollection.Name)]
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

  // The native ns/call and bridge ns/call columns come from the control samples, so the row keeps the
  // control's spread and step to flag a noisy control.
  [Fact]
  public async Task Run_CaseWithAControl_KeepsTheSpreadAndStepOfBothSampleSets()
  {
    using var host = BenchmarkCases.CreateHost();

    var row = await BenchmarkRunner.RunAsync(host, Case("function: no arguments", BenchmarkDirection.None), Small);

    Assert.True(row.Spread >= 1);
    Assert.True(row.Step >= 1);
    Assert.True(row.ControlSpread >= 1);
    Assert.True(row.ControlStep >= 1);
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
    Assert.Null(row.ControlSpread);
    Assert.Null(row.ControlStep);
    Assert.False(row.IsControlNoisy);
    Assert.Equal(0, row.OwnedLinkedPerCall);
    Assert.Equal(0, row.OwnedUnlinkedPerCall);
    Assert.Equal(0, row.BorrowedPerCall);
  }

  // A slow call calibrates to one call per sample, so any harness allocation that the runner does not
  // subtract would show up in full as bytes per call.
  [Fact]
  public async Task Run_CaseThatAllocatesNothing_ReportsNearZeroBytesEvenAtOneCallPerSample()
  {
    using var host = BenchmarkCases.CreateHost();
    var slow = new BenchmarkCase(
        "slow without allocation",
        BenchmarkDirection.None,
        "",
        "(function () { for (var k = 0; k < 300000; k++); })()",
        null,
        "true");

    var row = await BenchmarkRunner.RunAsync(host, slow, Small);

    Assert.True(row.BytesPerCall < 64, $"bytes/call was {row.BytesPerCall}");
  }

  // The last call of a loop leaves its buffer lease release on the executor. A timed sample must start
  // with no such work pending, or its cost would be charged to the next sample or to the control.
  [Fact]
  public void Settle_LeavesNoDeferredWorkPending()
  {
    using var host = BenchmarkCases.CreateHost();
    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "globalThis.a = __expoBenchNative.makeArrayBuffer(1024); for (var i = 0; i < 1000; i++) { m.takeArrayBuffer(a); } undefined",
          "benchmark-test.js");
      return true;
    });

    BenchmarkRunner.Settle(host);

    Assert.Equal(0u, host.TestRuntime.Counters.LongLivedObjectsRemaining);
  }
}
