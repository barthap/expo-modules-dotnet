using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace Expo.ModulesCore.Tests.Errors;

// JavaScript code ported from upstream branches on error.code and reads error.message. These tests
// check what JavaScript sees at every failure point, and that the error value is released.
public sealed class CodedErrorModuleTests
{
  // Expression templates; KIND is replaced with a quoted CodedErrorCases kind.
  private static readonly Dictionary<string, (string Expression, string Mode)> Points = new()
  {
    ["method"] = ("m.fail(KIND)", "sync"),
    ["getter"] = ("(m.mode = KIND, m.failingGetter)", "sync"),
    ["setter"] = ("(m.failingSetter = KIND)", "sync"),
    ["sharedMethod"] = ("entry().fail(KIND)", "sync"),
    ["constructor"] = ("new m.CodedErrorEntry(KIND)", "sync"),
    ["beforeTask"] = ("m.failBeforeTask(KIND)", "async"),
  };

  public static TheoryData<string, string> PointsAndKinds()
  {
    var data = new TheoryData<string, string>();
    foreach (var point in Points.Keys)
    {
      foreach (var kind in CodedErrorCases.Expected.Keys)
      {
        data.Add(point, kind);
      }
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(PointsAndKinds))]
  public async Task EveryFailurePointConvertsAndReleasesTheError(string point, string kind)
  {
    using var host = CreateHost();
    var (template, mode) = Points[point];
    var expression = template.Replace("KIND", $"'{kind}'", StringComparison.Ordinal);
    // Warm-up: the first call creates the lazy module, the class and the cached entry.
    await CatchAsync(host, expression);
    host.TestRuntime.WaitUntilIdle();
    var tracked = host.TestRuntime.Counters.OwnedValuesTracked;

    var outcome = await CatchAsync(host, expression);
    host.TestRuntime.WaitUntilIdle();

    var (code, message) = CodedErrorCases.Expected[kind];
    Assert.Equal(mode, outcome.Mode);
    Assert.True(outcome.IsError);
    Assert.Equal("Error", outcome.Name);
    Assert.Equal(code is not null, outcome.HasCode);
    Assert.Equal(code ?? "undefined", outcome.Code);
    Assert.Equal(message, outcome.Message);
    // The thrown or rejected value is released: no owned handle stays after a failing call.
    Assert.Equal(tracked, host.TestRuntime.Counters.OwnedValuesTracked);
  }

  [Fact]
  public async Task EventEmitterFunctionThrowsTheCoreMessage()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.addListener()");

    AssertUncodedSyncError(outcome);
    Assert.Equal("addListener expects an event name and listener.", outcome.Message);
  }

  [Fact]
  public async Task ObservingHookThrowsTheCodedError()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.stopObserving('onPing')");

    Assert.Equal("sync", outcome.Mode);
    Assert.Equal("ERR_INVALID_PATH", outcome.Code);
    Assert.Equal("observing stopped", outcome.Message);
  }

  // remove() calls the module's stopObserving through a nested JavaScript call. The hook's coded
  // value becomes a message there, and remove's own converter turns it into an uncoded Error.
  [Fact]
  public async Task SubscriptionRemoveConvertsTheNestedHookFailure()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.addListener('onPing', () => {}).remove()");

    AssertUncodedSyncError(outcome);
    Assert.StartsWith("Native JSI error 107: ", outcome.Message);
    Assert.Contains("observing stopped", outcome.Message);
  }

  [Fact]
  public async Task SharedObjectClassReleaseThrowsTheCoreMessage()
  {
    using var host = CreateHost();

    var outcome = await CatchAsync(host, "m.CodedErrorEntry.prototype.release.call({})");

    AssertUncodedSyncError(outcome);
    Assert.Equal("NativeState entry for SharedObjectNativeState is missing.", outcome.Message);
  }

  // Reentrancy: removeSubscription calls the subscription's remove, which calls a failing member.
  [Fact]
  public async Task NestedCodedErrorIsCatchableInsideTheOuterCall()
  {
    using var host = CreateHost();

    var value = await host.EvaluatePromiseAsync(
        """
        (() => {
          const m = globalThis._expoDotnet.modules.CodedErrors;
          let inner = 'none';
          m.removeSubscription({ remove() { try { m.fail('derived'); } catch (e) { inner = `${e.code}:${e.message}`; } } });
          return Promise.resolve(inner);
        })()
        """,
        TestContext.Current.CancellationToken
    );

    Assert.Equal("ERR_INVALID_PATH:bad path", ReadString(host, value));
  }

  [Fact]
  public async Task NestedHostFunctionFailureCrossesTheOuterCallAsAMessage()
  {
    using var host = CreateHost();
    const string expression = "m.removeSubscription({ remove() { m.fail('derived'); } })";
    await CatchAsync(host, expression);
    host.TestRuntime.WaitUntilIdle();
    var tracked = host.TestRuntime.Counters.OwnedValuesTracked;

    var outcome = await CatchAsync(host, expression);
    host.TestRuntime.WaitUntilIdle();

    AssertUncodedSyncError(outcome);
    Assert.StartsWith("Native JSI error 107: ", outcome.Message);
    Assert.Contains("bad path", outcome.Message);
    Assert.Equal(tracked, host.TestRuntime.Counters.OwnedValuesTracked);
  }

  // If the converter fails, the sync path still throws an Error, built from ToString() as before.
  [Fact]
  public async Task ConverterFailureOnSyncPathFallsBackToTheMessageResult()
  {
    using var host = CreateHost();
    await CatchAsync(host, "m.fail('plain')");
    BreakCodeProperty(host);
    host.TestRuntime.WaitUntilIdle();
    var tracked = host.TestRuntime.Counters.OwnedValuesTracked;

    var outcome = await CatchAsync(host, "m.fail('chain')");
    host.TestRuntime.WaitUntilIdle();

    Assert.Equal("sync", outcome.Mode);
    Assert.True(outcome.IsError);
    Assert.StartsWith("Expo.ModulesCore.CodedException: outer", outcome.Message);
    Assert.Contains("System.IO.IOException: inner", outcome.Message);
    Assert.Equal(tracked, host.TestRuntime.Counters.OwnedValuesTracked);
  }

  internal static ExpoModuleTestHost CreateHost() =>
      ExpoModuleTestHost.Create(ExpoModulesProvider_Expo_ModulesCore_Tests.Register);

  internal static void BreakCodeProperty(ExpoModuleTestHost host)
  {
    host.Runtime.Execute(_ =>
    {
      using var result = host.Evaluate(
          "Object.defineProperty(Error.prototype, 'code', { set() { throw new Error('code setter failed'); }, configurable: true }); true",
          "break-code-property.js"
      );
      return true;
    });
  }

  internal static void AssertUncodedSyncError(ErrorOutcome outcome)
  {
    Assert.Equal("sync", outcome.Mode);
    Assert.True(outcome.IsError);
    Assert.Equal("Error", outcome.Name);
    Assert.False(outcome.HasCode);
    AssertNoDotnetStackTrace(outcome.Message);
  }

  // A .NET frame is "   at Namespace.Type.Method(". A nested JavaScript error can carry its own
  // JavaScript stack ("    at fail (native)"), which is not a .NET stack trace.
  internal static void AssertNoDotnetStackTrace(string message)
  {
    Assert.DoesNotMatch(@"   at [\w.`<>+]+\.[\w`<>+]+\(", message);
    Assert.DoesNotContain("System.", message);
  }

  // Runs the expression, catches a sync throw or an async rejection, and describes the error.
  internal static async Task<ErrorOutcome> CatchAsync(ExpoModuleTestHost host, string expression)
  {
    var value = await host.EvaluatePromiseAsync(
        $$"""
        (() => {
          const m = globalThis._expoDotnet.modules.CodedErrors;
          const entry = () => globalThis.__codedEntry || (globalThis.__codedEntry = new m.CodedErrorEntry('none'));
          const describe = (e, mode) => [
            mode,
            e instanceof Error,
            e && e.name,
            e !== null && typeof e === 'object' && 'code' in e,
            String(e && e.code),
            e && e.message
          ].join('\u001f');
          let result;
          try { result = {{expression}}; } catch (e) { return Promise.resolve(describe(e, 'sync')); }
          return Promise.resolve(result).then(() => 'no error', e => describe(e, 'async'));
        })()
        """,
        TestContext.Current.CancellationToken
    );
    return ErrorOutcome.Parse(ReadString(host, value));
  }

  internal static string ReadString(ExpoModuleTestHost host, Expo.JSI.JavaScriptValue value) =>
      host.Runtime.Execute(_ =>
      {
        using (value)
        {
          return value.AsString();
        }
      });
}

internal sealed record ErrorOutcome(
    string Mode,
    bool IsError,
    string Name,
    bool HasCode,
    string Code,
    string Message)
{
  public static ErrorOutcome Parse(string text)
  {
    var parts = text.Split('\u001f', 6);
    Assert.True(parts.Length == 6, $"Expected a caught error, got: {text}");
    return new ErrorOutcome(parts[0], parts[1] == "true", parts[2], parts[3] == "true", parts[4], parts[5]);
  }
}
