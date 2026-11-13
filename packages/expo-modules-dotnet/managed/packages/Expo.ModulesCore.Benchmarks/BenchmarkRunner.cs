using System.Diagnostics;
using Expo.ModulesCore.Testing;

namespace Expo.ModulesCore.Benchmarks;

internal sealed record BenchmarkRow(
    BenchmarkCase Case,
    double MedianNanoseconds,
    double MinNanoseconds,
    double Spread,
    double BytesPerCall,
    double OwnedLinkedPerCall,
    double OwnedUnlinkedPerCall,
    double BorrowedPerCall,
    double? ControlMedianNanoseconds)
{
  /// <summary>Managed median minus control median, when the case has a control.</summary>
  public double? BridgeNanoseconds => ControlMedianNanoseconds is { } control ? MedianNanoseconds - control : null;
}

internal sealed record RunnerOptions(int SampleCount, TimeSpan SampleTarget, int HandlePassCalls)
{
  public static RunnerOptions Default { get; } =
      new(BenchmarkStatistics.SampleCount, BenchmarkStatistics.SampleTarget, 1000);
}

internal static class BenchmarkRunner
{
  private const string DriverName = "__bench";
  private const string ControlDriverName = "__benchControl";
  private const int MaxCalibrationSteps = 40;
  private const long MaxGrowthPerStep = 10;
  private static readonly TimeSpan DriverTimeout = TimeSpan.FromMinutes(5);

  public static async Task<BenchmarkRow> RunAsync(
      ExpoModuleTestHost host,
      BenchmarkCase benchmarkCase,
      RunnerOptions options)
  {
    ArgumentNullException.ThrowIfNull(host);
    ArgumentNullException.ThrowIfNull(benchmarkCase);
    ArgumentNullException.ThrowIfNull(options);
    ArgumentOutOfRangeException.ThrowIfLessThan(options.SampleCount, 1);
    ArgumentOutOfRangeException.ThrowIfLessThan(options.HandlePassCalls, 1);

    try
    {
      return await MeasureAsync(host, benchmarkCase, options);
    }
    catch (Exception exception)
    {
      throw new InvalidOperationException(
          $"Benchmark case \"{benchmarkCase.Name}\" ({benchmarkCase.Direction}) failed: {exception.Message}",
          exception);
    }
  }

  private static async Task<BenchmarkRow> MeasureAsync(
      ExpoModuleTestHost host,
      BenchmarkCase benchmarkCase,
      RunnerOptions options)
  {
    await VerifyAsync(host, benchmarkCase);

    Install(host, DriverName, benchmarkCase, benchmarkCase.Call);
    var managed = await SampleAsync(host, DriverName, benchmarkCase.IsAsync, options);
    var bytesPerCall = await MeasureBytesAsync(host, DriverName, benchmarkCase.IsAsync, managed.Iterations);
    var handles = await CountHandlesAsync(host, DriverName, benchmarkCase.IsAsync, options.HandlePassCalls);

    double? controlMedian = null;
    if (benchmarkCase.ControlCall is not null)
    {
      Install(host, ControlDriverName, benchmarkCase, benchmarkCase.ControlCall);
      var control = await SampleAsync(host, ControlDriverName, benchmarkCase.IsAsync, options);
      controlMedian = BenchmarkStatistics.Median(control.NanosecondsPerCall);
    }

    return new BenchmarkRow(
        benchmarkCase,
        BenchmarkStatistics.Median(managed.NanosecondsPerCall),
        BenchmarkStatistics.Min(managed.NanosecondsPerCall),
        BenchmarkStatistics.Spread(managed.NanosecondsPerCall),
        bytesPerCall,
        handles.OwnedLinked,
        handles.OwnedUnlinked,
        handles.Borrowed,
        controlMedian);
  }

  private static async Task VerifyAsync(ExpoModuleTestHost host, BenchmarkCase benchmarkCase)
  {
    bool verified;
    if (benchmarkCase.IsAsync)
    {
      var source =
          $"(function () {{ {benchmarkCase.Prelude}\n return Promise.resolve({benchmarkCase.Call})" +
          $".then(function () {{ return !!({benchmarkCase.Verify}); }}); }})()";
      using var result = await host.EvaluatePromiseAsync(source, DriverTimeout);
      verified = host.Runtime.Execute(_ => result.AsBool());
    }
    else
    {
      var source =
          $"(function () {{ {benchmarkCase.Prelude}\n {benchmarkCase.Call};\n return !!({benchmarkCase.Verify}); }})()";
      verified = host.Runtime.Execute(_ =>
      {
        using var result = host.Evaluate(source, "benchmark-verify.js");
        return result.AsBool();
      });
    }

    if (!verified)
    {
      throw new InvalidOperationException($"Verify returned false: {benchmarkCase.Verify}");
    }
  }

  private static void Install(ExpoModuleTestHost host, string driverName, BenchmarkCase benchmarkCase, string call)
  {
    var statement = call.Length == 0 ? ";" : $"{call};";
    var source = benchmarkCase.IsAsync
        ? $"globalThis.{driverName} = function (n) {{ {benchmarkCase.Prelude}\n var __promises = [];" +
          $" for (let i = 0; i < n; i++) {{ __promises.push({call}); }} return Promise.all(__promises); }}; undefined"
        : $"globalThis.{driverName} = function (n) {{ {benchmarkCase.Prelude}\n" +
          $" for (let i = 0; i < n; i++) {{ {statement} }} }}; undefined";
    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(source, "benchmark-driver.js");
      return true;
    });
  }

  // A sync driver call returns when the loop ends. An async driver returns a promise that settles
  // after every promise of the loop has settled, so the sample covers the whole async work.
  private static async Task RunDriverAsync(ExpoModuleTestHost host, string driverName, bool isAsync, int n)
  {
    var source = $"{driverName}({n})";
    if (isAsync)
    {
      using var settled = await host.EvaluatePromiseAsync(source, DriverTimeout);
      return;
    }

    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(source, "benchmark-run.js");
      return true;
    });
  }

  private static async Task<double> TimeDriverAsync(ExpoModuleTestHost host, string driverName, bool isAsync, int n)
  {
    var start = Stopwatch.GetTimestamp();
    await RunDriverAsync(host, driverName, isAsync, n);
    var ticks = Stopwatch.GetTimestamp() - start;
    return ticks * 1e9 / Stopwatch.Frequency;
  }

  private static async Task<(int Iterations, List<double> NanosecondsPerCall)> SampleAsync(
      ExpoModuleTestHost host,
      string driverName,
      bool isAsync,
      RunnerOptions options)
  {
    var iterations = await CalibrateAsync(host, driverName, isAsync, options.SampleTarget);

    await TimeDriverAsync(host, driverName, isAsync, iterations);
    var samples = new List<double>(options.SampleCount);
    for (var index = 0; index < options.SampleCount; index++)
    {
      CollectGarbage(host);
      samples.Add(await TimeDriverAsync(host, driverName, isAsync, iterations) / iterations);
    }

    return (iterations, samples);
  }

  private static async Task<int> CalibrateAsync(
      ExpoModuleTestHost host,
      string driverName,
      bool isAsync,
      TimeSpan target)
  {
    var iterations = 1;
    for (var step = 0; step < MaxCalibrationSteps; step++)
    {
      var elapsed = TimeSpan.FromTicks((long)(await TimeDriverAsync(host, driverName, isAsync, iterations) / 100));
      if (BenchmarkStatistics.IsCalibrated(elapsed, target))
      {
        return iterations;
      }

      var next = Math.Min(BenchmarkStatistics.NextIterations(iterations, elapsed, target), iterations * MaxGrowthPerStep);
      iterations = (int)Math.Min(next, int.MaxValue);
    }

    throw new InvalidOperationException($"Not calibrated after {MaxCalibrationSteps} steps.");
  }

  // Host functions run on the testhost executor thread, so only the process-wide counter sees their
  // allocations. The delta also holds the allocations of the harness call, which n makes negligible.
  private static async Task<double> MeasureBytesAsync(
      ExpoModuleTestHost host,
      string driverName,
      bool isAsync,
      int iterations)
  {
    CollectGarbage(host);
    var before = GC.GetTotalAllocatedBytes(precise: true);
    await RunDriverAsync(host, driverName, isAsync, iterations);
    var after = GC.GetTotalAllocatedBytes(precise: true);
    return (double)(after - before) / iterations;
  }

  // The evaluate call of the harness creates one result handle of its own. A pass with n = 0 runs the
  // same prelude and the same evaluate call without any loop iteration, so subtracting it leaves only
  // the handles of the calls.
  private static async Task<(double OwnedLinked, double OwnedUnlinked, double Borrowed)> CountHandlesAsync(
      ExpoModuleTestHost host,
      string driverName,
      bool isAsync,
      int calls)
  {
    var baseline = await HandlePassAsync(host, driverName, isAsync, 0);
    var pass = await HandlePassAsync(host, driverName, isAsync, calls);
    return (
        (double)(pass.Linked - baseline.Linked) / calls,
        (double)(pass.Unlinked - baseline.Unlinked) / calls,
        (double)(pass.Borrowed - baseline.Borrowed) / calls);
  }

  private static async Task<(long Linked, long Unlinked, long Borrowed)> HandlePassAsync(
      ExpoModuleTestHost host,
      string driverName,
      bool isAsync,
      int n)
  {
    host.TestRuntime.ResetCounters();
    await RunDriverAsync(host, driverName, isAsync, n);
    host.TestRuntime.WaitUntilIdle();
    var counters = host.TestRuntime.Counters;
    return (counters.OwnedValuesCreatedLinked, counters.OwnedValuesCreatedUnlinked, counters.BorrowedValuesCreated);
  }

  private static void CollectGarbage(ExpoModuleTestHost host)
  {
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    host.TestRuntime.CollectGarbageForTesting();
  }
}
