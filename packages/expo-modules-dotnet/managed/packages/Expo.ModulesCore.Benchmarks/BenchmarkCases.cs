using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;

namespace Expo.ModulesCore.Benchmarks;

internal static class BenchmarkCases
{
  public const string ModuleGlobal = "m";

  private const string Native = "__expoBenchNative";

  public static IReadOnlyList<BenchmarkCase> All { get; } = Build();

  /// <summary>Creates a host with the benchmark module bound to <c>globalThis.m</c> and the native controls installed.</summary>
  public static ExpoModuleTestHost CreateHost()
  {
    var host = ExpoModuleTestHost.Create((context, modules) =>
        ExpoModulesProvider_Expo_ModulesCore_Benchmarks.Register(context, modules));
    try
    {
      host.TestRuntime.InstallBenchmarkControls();
      host.Runtime.Execute(_ =>
      {
        using var result = host.Evaluate(
            $"globalThis.{ModuleGlobal} = globalThis._expoDotnet.modules.Benchmark; undefined",
            "benchmark-setup.js");
        return true;
      });
      return host;
    }
    catch
    {
      host.Dispose();
      throw;
    }
  }

  private static List<BenchmarkCase> Build()
  {
    var cases = new List<BenchmarkCase>
    {
      new("JS: empty driver loop", BenchmarkDirection.None, "", "", null, "true"),
      new("function: no arguments", BenchmarkDirection.None, "", "m.noop()", $"{Native}.noop()", "m.noop() === undefined"),
      RoundTrip("function: two doubles, double result", "addDoubles(1.5, 2)", "3.5"),
      new(
          "function: async, no arguments",
          BenchmarkDirection.None,
          "",
          "m.noopAsync()",
          null,
          "m.noopAsync() instanceof Promise",
          IsAsync: true),
      RoundTrip("primitive: boolean", "echoBoolean(true)", "true"),
      RoundTrip("primitive: int", "echoInt(7)", "7"),
      RoundTrip("primitive: double", "echoDouble(0.5)", "0.5"),
    };

    AddPair(cases, "string: 16 characters", "String", "", 16, 209);
    AddPair(cases, "record: 4 fields", "Record4", "", 4, 5);
    AddPair(cases, "record: 12 fields", "Record12", "", 12, 11);
    AddPair(cases, "record: nested, 48 values", "Nested48", "", 48, 9);
    AddPair(cases, "dictionary: 4 numbers", "Dictionary", "4", 4, 3);
    AddPair(cases, "dictionary: 12 numbers", "Dictionary", "12", 12, 3);
    AddPair(cases, "array: 100 numbers", "Numbers", "100", 100, 99);
    AddPair(cases, "array: 1,000 numbers", "Numbers", "1000", 1000, 999);
    AddPair(cases, "array: 100 strings", "Strings", "100", 100, 5);
    AddPair(cases, "array: 100 records, 4 fields", "Records4", "100", 100, 5);
    AddPair(cases, "array: 100 mixed values", "Mixed", "100", 100, 99);
    AddPair(cases, "binary: ArrayBuffer, 1,024 bytes", "ArrayBuffer", "1024", 1024, 0);
    AddPair(cases, "binary: Uint8Array, 1,024 bytes", "Uint8Array", "1024", 1024, 0);

    return cases;
  }

  private static BenchmarkCase RoundTrip(string name, string call, string expected) =>
      new(name, BenchmarkDirection.RoundTrip, "", $"m.{call}", $"{Native}.{call}", $"m.{call} === {expected}");

  // The argument case hands the managed side a value that the native control built, so both sides
  // decode identical input. The return case compares the managed result with the control's value.
  // `shape` names the take and make methods, and `makeArguments` is the argument of the make call.
  private static void AddPair(
      List<BenchmarkCase> cases,
      string name,
      string shape,
      string makeArguments,
      int expectedCount,
      double expectedChecksum)
  {
    var make = $"make{shape}({makeArguments})";
    cases.Add(new BenchmarkCase(
        $"{name} (argument)",
        BenchmarkDirection.Argument,
        $"m.resetReceived(); var a = {Native}.{make};",
        $"m.take{shape}(a)",
        $"{Native}.take{shape}(a)",
        $"m.receivedCount() === {expectedCount} && m.receivedChecksum() === {expectedChecksum}"));
    cases.Add(new BenchmarkCase(
        $"{name} (return)",
        BenchmarkDirection.Return,
        "",
        $"m.{make}",
        $"{Native}.{make}",
        ReturnVerify(shape, make)));
  }

  private static string ReturnVerify(string shape, string make) => shape switch
  {
    "ArrayBuffer" => $"m.{make} instanceof ArrayBuffer && m.{make}.byteLength === 1024",
    "Uint8Array" => $"m.{make} instanceof Uint8Array && m.{make}.byteLength === 1024",
    _ => $"JSON.stringify(m.{make}) === JSON.stringify({Native}.{make})",
  };
}
