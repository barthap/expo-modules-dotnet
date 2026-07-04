using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI.Tests.Fixtures;

internal static unsafe class NativeTestHost
{
  private const string LibraryEnvVar = "EXPO_JSI_TESTHOST_LIBRARY";
  private static readonly Lazy<nint> LibraryHandle = new(LoadLibrary);

  private static delegate* unmanaged[Cdecl]<CreateResult> createRuntime;
  private static delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, ExpoJsiValueResult>
    evaluateScript;
  private static delegate* unmanaged[Cdecl]<nint, Counters> getCounters;
  private static delegate* unmanaged[Cdecl]<nint, void> resetCounters;
  private static delegate* unmanaged[Cdecl]<nint, void> drainTasks;
  private static delegate* unmanaged[Cdecl]<nint, ExpoJsiError> waitUntilIdle;
  private static delegate* unmanaged[Cdecl]<nint, byte, void> setSyncExecutionSupported;
  private static delegate* unmanaged[Cdecl]<nint, void> releaseRuntime;

  private static bool initialized;

  [StructLayout(LayoutKind.Sequential)]
  internal readonly struct CreateResult
  {
    public readonly int Ok;
    public readonly nint Api;
    public readonly nint Runtime;
    public readonly nint TestHostRuntime;
    public readonly ExpoJsiError Error;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal readonly struct Counters
  {
    public readonly uint ReleasedValues;
    public readonly uint ReleasedPromises;
    public readonly uint ReleasedStrings;
    public readonly uint ReleasedErrors;
    public readonly uint ReleasedTaskContexts;
    public readonly uint SyncExecuteCalls;
    public readonly uint PrimitiveValueCreates;
    public readonly uint DeprecatedNumberCreates;
    public readonly uint DeprecatedBoolCreates;
  }

  internal static CreateResult CreateRuntime()
  {
    EnsureLoaded();
    return createRuntime();
  }

  internal static JavaScriptValue Evaluate(
      JavaScriptRuntime runtime,
      nint testHostRuntime,
      string source,
      string sourceUrl
  )
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(sourceUrl);
    EnsureLoaded();

    var sourceBytes = Encoding.UTF8.GetBytes(source);
    var sourceUrlBytes = Encoding.UTF8.GetBytes(sourceUrl);

    fixed (byte* sourcePtr = sourceBytes)
    fixed (byte* sourceUrlPtr = sourceUrlBytes)
    {
      var result = evaluateScript(
          testHostRuntime,
          sourcePtr,
          sourceBytes.Length,
          sourceUrlPtr,
          sourceUrlBytes.Length
      );
      if (!result.IsOk)
      {
        ThrowNativeError(result.Error, "Failed to evaluate JavaScript.");
      }
      return runtime.FromOwnedValueHandle(result.Value);
    }
  }

  internal static Counters GetCounters(nint testHostRuntime)
  {
    EnsureLoaded();
    return getCounters(testHostRuntime);
  }

  internal static void ResetCounters(nint testHostRuntime)
  {
    EnsureLoaded();
    resetCounters(testHostRuntime);
  }

  internal static void DrainTasks(nint testHostRuntime)
  {
    EnsureLoaded();
    drainTasks(testHostRuntime);
  }

  internal static void WaitUntilIdle(nint testHostRuntime)
  {
    EnsureLoaded();
    var error = waitUntilIdle(testHostRuntime);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to wait for Hermes runtime idle.");
    }
  }

  internal static void SetSyncExecutionSupported(nint testHostRuntime, bool supported)
  {
    EnsureLoaded();
    setSyncExecutionSupported(testHostRuntime, supported ? (byte)1 : (byte)0);
  }

  internal static void ReleaseRuntime(nint testHostRuntime)
  {
    EnsureLoaded();
    releaseRuntime(testHostRuntime);
  }

  private static void EnsureLoaded()
  {
    if (initialized)
    {
      return;
    }

    var library = LibraryHandle.Value;
    createRuntime =
      (delegate* unmanaged[Cdecl]<CreateResult>)LoadExport(
          library,
          "expo_jsi_testhost_create_runtime"
      );
    evaluateScript =
      (delegate* unmanaged[Cdecl]<
          nint,
          byte*,
          int,
          byte*,
          int,
          ExpoJsiValueResult>)LoadExport(library, "expo_jsi_testhost_evaluate_script");
    getCounters =
      (delegate* unmanaged[Cdecl]<nint, Counters>)LoadExport(
          library,
          "expo_jsi_testhost_get_counters"
      );
    resetCounters =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_reset_counters"
      );
    drainTasks =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_drain_tasks"
      );
    waitUntilIdle =
      (delegate* unmanaged[Cdecl]<nint, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_idle"
      );
    setSyncExecutionSupported =
      (delegate* unmanaged[Cdecl]<nint, byte, void>)LoadExport(
          library,
          "expo_jsi_testhost_set_sync_execution_supported"
      );
    releaseRuntime =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_release_runtime"
      );
    initialized = true;
  }

  private static nint LoadLibrary()
  {
    var path = Environment.GetEnvironmentVariable(LibraryEnvVar);
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new InvalidOperationException($"{LibraryEnvVar} is not set. Run scripts/test-managed.sh.");
    }
    if (!File.Exists(path))
    {
      throw new FileNotFoundException($"{LibraryEnvVar} points to a missing library.", path);
    }
    return NativeLibrary.Load(path);
  }

  private static nint LoadExport(nint library, string name)
  {
    if (!NativeLibrary.TryGetExport(library, name, out var symbol))
    {
      throw new MissingMethodException($"Native testhost export not found: {name}");
    }
    return symbol;
  }

  private static void ThrowNativeError(ExpoJsiError error, string fallback)
  {
    var message = error.GetMessageAndRelease();
    throw new InvalidOperationException(string.IsNullOrEmpty(message) ? fallback : message);
  }
}
