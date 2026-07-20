using Expo.JSI;

namespace Expo.ModulesCore.Tests.Fixtures;

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
      var message = result.Error.GetMessageAndRelease();
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
    WaitUntilIdle();
  }

  public void WaitUntilIdle()
  {
    NativeTestHost.WaitUntilIdle(testHostRuntime);
  }

  public void CollectGarbageForTesting() => NativeTestHost.CollectGarbageForTesting(testHostRuntime);

  public void DisableSyncExecutionForTesting()
  {
    SetSyncExecutionSupportedForTesting(false);
  }

  public void SetSyncExecutionSupportedForTesting(bool supported)
  {
    NativeTestHost.SetSyncExecutionSupported(testHostRuntime, supported);
  }

  public void PauseRuntimeExecutor() => NativeTestHost.PauseRuntimeExecutor(testHostRuntime);

  public void ResumeRuntimeExecutor() => NativeTestHost.ResumeRuntimeExecutor(testHostRuntime);

  public void DropNextRuntimeTask(JavaScriptTaskPriority priority) =>
      NativeTestHost.DropNextRuntimeTask(testHostRuntime, priority);

  public void WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority priority) =>
      NativeTestHost.WaitUntilRuntimeTaskQueued(testHostRuntime, priority);

  public void DropQueuedRuntimeTask(JavaScriptTaskPriority priority) =>
      NativeTestHost.DropQueuedRuntimeTask(testHostRuntime, priority);

  public void ReleaseBridgeRuntimeHandle() =>
      NativeTestHost.ReleaseBridgeRuntimeHandle(testHostRuntime);

  public void InvalidateRuntimeForTesting()
  {
    NativeTestHost.InvalidateRuntime(testHostRuntime);
  }

  public void PrepareRuntimeForInvalidation()
  {
    NativeTestHost.PrepareRuntimeForInvalidation(testHostRuntime);
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
