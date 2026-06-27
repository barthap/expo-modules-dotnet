namespace Expo.JSI.Tests.Fixtures;

public sealed class HermesRuntimeFixture : IDisposable
{
  private nint testHostRuntime;

  private HermesRuntimeFixture(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    Runtime = runtime;
    this.testHostRuntime = testHostRuntime;
    TestRuntime = new JavaScriptTestRuntime(runtime, testHostRuntime);
  }

  public JavaScriptRuntime Runtime { get; }

  public JavaScriptTestRuntime TestRuntime { get; }

  internal NativeTestHost.Counters Counters => NativeTestHost.GetCounters(testHostRuntime);

  public static HermesRuntimeFixture Create()
  {
    var result = NativeTestHost.CreateRuntime();
    if (result.Ok == 0 || result.Api == 0 || result.Runtime == 0 || result.TestHostRuntime == 0)
    {
      var message = result.Error.GetMessage();
      throw new InvalidOperationException(
          string.IsNullOrEmpty(message) ? "Failed to create Hermes test runtime." : message
      );
    }

    var runtime = JavaScriptRuntime.FromNative(result.Api, result.Runtime);
    return new HermesRuntimeFixture(runtime, result.TestHostRuntime);
  }

  public void ResetCounters()
  {
    NativeTestHost.ResetCounters(testHostRuntime);
  }

  public void DrainTasks()
  {
    NativeTestHost.DrainTasks(testHostRuntime);
  }

  public void DisableSyncExecutionForTesting()
  {
    NativeTestHost.SetSyncExecutionSupported(testHostRuntime, false);
  }

  public JavaScriptValue Evaluate(string source, string sourceUrl = "expo-jsi-test.js")
  {
    return TestRuntime.Evaluate(source, sourceUrl);
  }

  public void Dispose()
  {
    if (testHostRuntime != 0)
    {
      NativeTestHost.ReleaseRuntime(testHostRuntime);
      testHostRuntime = 0;
    }
  }
}
