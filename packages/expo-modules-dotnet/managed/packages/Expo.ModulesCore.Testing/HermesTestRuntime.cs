using Expo.JSI;
using Expo.ModulesCore.Testing.Internal;

namespace Expo.ModulesCore.Testing;

public sealed class HermesTestRuntime : IDisposable
{
  private nint testHostRuntime;

  private HermesTestRuntime(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    Runtime = runtime;
    this.testHostRuntime = testHostRuntime;
  }

  public JavaScriptRuntime Runtime { get; }

  internal NativeTestHost.Counters Counters => NativeTestHost.GetCounters(GetTestHostRuntime());

  public static HermesTestRuntime Create()
  {
    var result = NativeTestHost.CreateRuntime();
    if (result.Ok == 0 || result.Api == 0 || result.Runtime == 0 || result.TestHostRuntime == 0)
    {
      var message = result.Error.GetMessageAndRelease();
      throw new InvalidOperationException(
          string.IsNullOrEmpty(message) ? "Failed to create Hermes test runtime." : message
      );
    }

    return new HermesTestRuntime(
        JavaScriptRuntime.FromNative(result.Api, result.Runtime),
        result.TestHostRuntime
    );
  }

  public JavaScriptValue Evaluate(
      string source,
      string sourceUrl = "expo-modules-test-core.js"
  )
  {
    return NativeTestHost.Evaluate(Runtime, GetTestHostRuntime(), source, sourceUrl);
  }

  public void DrainTasks() => WaitUntilIdle();

  public void WaitUntilIdle()
  {
    NativeTestHost.WaitUntilIdle(GetTestHostRuntime());
  }

  internal void ResetCounters()
  {
    NativeTestHost.ResetCounters(GetTestHostRuntime());
  }

  internal void CollectGarbageForTesting()
  {
    NativeTestHost.CollectGarbageForTesting(GetTestHostRuntime());
  }

  internal void SetSyncExecutionSupportedForTesting(bool supported)
  {
    NativeTestHost.SetSyncExecutionSupported(GetTestHostRuntime(), supported);
  }

  internal void PauseRuntimeExecutor()
  {
    NativeTestHost.PauseRuntimeExecutor(GetTestHostRuntime());
  }

  internal void ResumeRuntimeExecutor()
  {
    NativeTestHost.ResumeRuntimeExecutor(GetTestHostRuntime());
  }

  internal void DropNextRuntimeTask(JavaScriptTaskPriority priority)
  {
    NativeTestHost.DropNextRuntimeTask(GetTestHostRuntime(), priority);
  }

  internal void WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority priority)
  {
    NativeTestHost.WaitUntilRuntimeTaskQueued(GetTestHostRuntime(), priority);
  }

  internal void DropQueuedRuntimeTask(JavaScriptTaskPriority priority)
  {
    NativeTestHost.DropQueuedRuntimeTask(GetTestHostRuntime(), priority);
  }

  internal void ReleaseBridgeRuntimeHandle()
  {
    NativeTestHost.ReleaseBridgeRuntimeHandle(GetTestHostRuntime());
  }

  internal void InvalidateRuntimeForTesting()
  {
    NativeTestHost.InvalidateRuntime(GetTestHostRuntime());
  }

  internal void PrepareRuntimeForInvalidation()
  {
    NativeTestHost.PrepareRuntimeForInvalidation(GetTestHostRuntime());
  }

  public void Dispose()
  {
    var runtime = Interlocked.Exchange(ref testHostRuntime, 0);
    if (runtime != 0)
    {
      NativeTestHost.ReleaseRuntime(runtime);
    }
  }

  private nint GetTestHostRuntime()
  {
    var runtime = Volatile.Read(ref testHostRuntime);
    ObjectDisposedException.ThrowIf(runtime == 0, this);
    return runtime;
  }
}
