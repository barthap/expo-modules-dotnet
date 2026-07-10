using Expo.JSI.Interop;

namespace Expo.JSI.Tests.Fixtures;

public sealed unsafe class HermesRuntimeFixture : IDisposable
{
  private readonly ExpoJsiApi* api;
  private readonly nint runtimeHandle;
  private nint testHostRuntime;

  private HermesRuntimeFixture(
      JavaScriptRuntime runtime,
      ExpoJsiApi* api,
      nint runtimeHandle,
      nint testHostRuntime
  )
  {
    Runtime = runtime;
    this.api = api;
    this.runtimeHandle = runtimeHandle;
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
    return new HermesRuntimeFixture(
        runtime,
        (ExpoJsiApi*)result.Api,
        result.Runtime,
        result.TestHostRuntime
    );
  }

  internal ExpoJsiError SetObjectPropertyRaw(
      JavaScriptObject target,
      ReadOnlySpan<byte> name,
      JavaScriptValue value
  )
  {
    using var targetValue = target.AsValue();
    return api->SetObjectProperty(runtimeHandle, targetValue.Handle, name, value.Handle);
  }

  internal ExpoJsiValueResult GetObjectPropertyRaw(
      JavaScriptObject target,
      ReadOnlySpan<byte> name
  )
  {
    using var targetValue = target.AsValue();
    return api->GetObjectProperty(runtimeHandle, targetValue.Handle, name);
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

  public void DisableSyncExecutionForTesting()
  {
    SetSyncExecutionSupportedForTesting(false);
  }

  public void SetSyncExecutionSupportedForTesting(bool supported)
  {
    NativeTestHost.SetSyncExecutionSupported(testHostRuntime, supported);
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
