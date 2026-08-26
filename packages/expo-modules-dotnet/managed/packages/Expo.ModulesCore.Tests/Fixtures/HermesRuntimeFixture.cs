using Expo.JSI;
using Expo.ModulesCore.Testing;
using Expo.ModulesCore.Testing.Internal;

namespace Expo.ModulesCore.Tests.Fixtures;

public sealed class HermesRuntimeFixture : IDisposable
{
  private readonly HermesTestRuntime testRuntime;

  private HermesRuntimeFixture(HermesTestRuntime testRuntime)
  {
    this.testRuntime = testRuntime;
  }

  public JavaScriptRuntime Runtime => testRuntime.Runtime;

  internal NativeTestHost.Counters Counters => testRuntime.Counters;

  public static HermesRuntimeFixture Create()
  {
    return new HermesRuntimeFixture(HermesTestRuntime.Create());
  }

  public void ResetCounters()
  {
    testRuntime.ResetCounters();
  }

  public void DrainTasks()
  {
    WaitUntilIdle();
  }

  public void WaitUntilIdle()
  {
    testRuntime.WaitUntilIdle();
  }

  public void CollectGarbageForTesting() => testRuntime.CollectGarbageForTesting();

  public void DisableSyncExecutionForTesting()
  {
    SetSyncExecutionSupportedForTesting(false);
  }

  public void SetSyncExecutionSupportedForTesting(bool supported)
  {
    testRuntime.SetSyncExecutionSupportedForTesting(supported);
  }

  public void PauseRuntimeExecutor() => testRuntime.PauseRuntimeExecutor();

  public void ResumeRuntimeExecutor() => testRuntime.ResumeRuntimeExecutor();

  public void DropNextRuntimeTask(JavaScriptTaskPriority priority) =>
      testRuntime.DropNextRuntimeTask(priority);

  public void WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority priority) =>
      testRuntime.WaitUntilRuntimeTaskQueued(priority);

  public void DropQueuedRuntimeTask(JavaScriptTaskPriority priority) =>
      testRuntime.DropQueuedRuntimeTask(priority);

  public void ReleaseBridgeRuntimeHandle() =>
      testRuntime.ReleaseBridgeRuntimeHandle();

  public void InvalidateRuntimeForTesting()
  {
    testRuntime.InvalidateRuntimeForTesting();
  }

  public void PrepareRuntimeForInvalidation()
  {
    testRuntime.PrepareRuntimeForInvalidation();
  }

  public JavaScriptValue Evaluate(string source, string sourceUrl = "expo-jsi-test.js")
  {
    return testRuntime.Evaluate(source, sourceUrl);
  }

  public void Dispose()
  {
    testRuntime.Dispose();
  }
}
