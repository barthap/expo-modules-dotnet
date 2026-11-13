using Expo.ModulesCore.Benchmarks;
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Benchmarks;

// The benchmark reports one number per case. A case that is missing, shares a name, has no control,
// or whose managed call does not do what its Verify says would turn the report into a wrong comparison.
public sealed class BenchmarkCasesTests
{
  private static bool EvaluateBoolean(ExpoModuleTestHost host, string source)
  {
    return host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(source, "benchmark-case.js");
      return result.AsBool();
    });
  }

  private static void Run(ExpoModuleTestHost host, string source)
  {
    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(source, "benchmark-case.js");
      return true;
    });
  }

  private static string EvaluateString(ExpoModuleTestHost host, string source)
  {
    return host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(source, "benchmark-case.js");
      return result.AsString();
    });
  }

  [Fact]
  public void CaseNamesWithDirectionAreUnique()
  {
    var names = BenchmarkCases.All.Select(benchmarkCase => benchmarkCase.Name).ToList();

    Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    Assert.Contains(BenchmarkCases.All, benchmarkCase => benchmarkCase.Direction == BenchmarkDirection.Argument);
    Assert.Contains(BenchmarkCases.All, benchmarkCase => benchmarkCase.Direction == BenchmarkDirection.Return);
  }

  [Fact]
  public void EveryCaseExceptFloorAndAsyncHasAControl()
  {
    foreach (var benchmarkCase in BenchmarkCases.All)
    {
      var needsNoControl = benchmarkCase.Call.Length == 0 || benchmarkCase.IsAsync;
      Assert.True(
          needsNoControl || benchmarkCase.ControlCall is not null,
          $"{benchmarkCase.Name} has no control call.");
    }
  }

  [Fact]
  public void ArrayOfNumbersAndArrayOfStringsExist()
  {
    var names = BenchmarkCases.All.Select(benchmarkCase => benchmarkCase.Name).ToHashSet();

    Assert.Contains("array: 100 numbers (argument)", names);
    Assert.Contains("array: 100 numbers (return)", names);
    Assert.Contains("array: 100 strings (argument)", names);
    Assert.Contains("array: 100 strings (return)", names);
  }

  [Fact]
  public async Task EveryManagedCallVerifies()
  {
    using var host = BenchmarkCases.CreateHost();

    foreach (var benchmarkCase in BenchmarkCases.All)
    {
      Run(host, benchmarkCase.Prelude.Length == 0 ? "undefined" : benchmarkCase.Prelude);
      if (benchmarkCase.Call.Length > 0)
      {
        if (benchmarkCase.IsAsync)
        {
          using var settled = await host.EvaluatePromiseAsync(benchmarkCase.Call, TestContext.Current.CancellationToken);
        }
        else
        {
          Run(host, benchmarkCase.Call);
        }
      }

      Assert.True(EvaluateBoolean(host, benchmarkCase.Verify), $"{benchmarkCase.Name} did not verify.");
    }
  }

  [Fact]
  public void EveryControlCallRunsWithoutThrowing()
  {
    using var host = BenchmarkCases.CreateHost();

    foreach (var benchmarkCase in BenchmarkCases.All.Where(benchmarkCase => benchmarkCase.ControlCall is not null))
    {
      Run(host, benchmarkCase.Prelude.Length == 0 ? "undefined" : benchmarkCase.Prelude);
      var exception = Record.Exception(() => Run(host, benchmarkCase.ControlCall!));
      Assert.True(exception is null, $"{benchmarkCase.Name} control threw: {exception?.Message}");
    }
  }

  [Fact]
  public void TwelveFieldRecordMatchesItsControlValue()
  {
    using var host = BenchmarkCases.CreateHost();

    Assert.Equal(
        EvaluateString(host, "JSON.stringify(__expoBenchNative.makeRecord12())"),
        EvaluateString(host, "JSON.stringify(m.makeRecord12())"));
  }
}
