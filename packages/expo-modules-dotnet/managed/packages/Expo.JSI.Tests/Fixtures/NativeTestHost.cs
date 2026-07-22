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
  private static delegate* unmanaged[Cdecl]<nint, ExpoJsiError> collectGarbage;
  private static delegate* unmanaged[Cdecl]<nint, void> pauseRuntimeExecutor;
  private static delegate* unmanaged[Cdecl]<nint, void> resumeRuntimeExecutor;
  private static delegate* unmanaged[Cdecl]<nint, int, void> dropNextRuntimeTask;
  private static delegate* unmanaged[Cdecl]<nint, int, ExpoJsiError> waitUntilRuntimeTaskQueued;
  private static delegate* unmanaged[Cdecl]<nint, int, int, ExpoJsiError>
      waitUntilRuntimeTasksQueued;
  private static delegate* unmanaged[Cdecl]<nint, int, ExpoJsiError> dropQueuedRuntimeTask;
  private static delegate* unmanaged[Cdecl]<nint, void> releaseBridgeRuntimeHandle;
  private static delegate* unmanaged[Cdecl]<nint, void> poisonMutableBufferDispatch;
  private static delegate* unmanaged[Cdecl]<nint, byte, void> setSyncExecutionSupported;
  private static delegate* unmanaged[Cdecl]<nint, void> prepareRuntimeForInvalidation;
  private static delegate* unmanaged[Cdecl]<nint, void> failNextPromiseHandleAllocation;
  private static delegate* unmanaged[Cdecl]<nint, void> pauseNextPromiseRegistration;
  private static delegate* unmanaged[Cdecl]<nint, ExpoJsiError> waitUntilPromiseRegistrationPaused;
  private static delegate* unmanaged[Cdecl]<nint, void> resumePromiseRegistration;
  private static delegate* unmanaged[Cdecl]<nint, void>
      invalidateBridgeRuntimeStateWithoutDeletingHandle;
  private static delegate* unmanaged[Cdecl]<nint, int, void> pauseNextPromiseCall;
  private static delegate* unmanaged[Cdecl]<nint, ExpoJsiError> waitUntilPromiseCallBlocked;
  private static delegate* unmanaged[Cdecl]<nint, void> resumePromiseCall;

  internal enum PromiseCallOperation
  {
    AsValue = 1,
    Resolve = 2,
    Reject = 3,
  }
  private static delegate* unmanaged[Cdecl]<nint, byte, int, int, ExpoJsiError>
      validateArrayBufferSnapshot;
  private static delegate* unmanaged[Cdecl]<nint, ulong, ExpoJsiError>
      validateArrayBufferLength;
  private static delegate* unmanaged[Cdecl]<nint, void> invalidateRuntime;
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
    public readonly uint ReleasedNativeStates;
    public readonly uint ReleasedPromisesOffRuntimeThread;
    public readonly uint LongLivedArrayBuffersReleased;
    public readonly uint LongLivedArrayBuffersAbandoned;
    public readonly uint LongLivedWeakObjectsReleased;
    public readonly uint LongLivedWeakObjectsAbandoned;
    public readonly uint LongLivedPromisesReleased;
    public readonly uint LongLivedPromisesAbandoned;
    public readonly uint LongLivedObjectsRemaining;
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

  internal static void CollectGarbageForTesting(nint testHostRuntime)
  {
    EnsureLoaded();
    var error = collectGarbage(testHostRuntime);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to collect Hermes garbage.");
    }
  }

  internal static void SetSyncExecutionSupported(nint testHostRuntime, bool supported)
  {
    EnsureLoaded();
    setSyncExecutionSupported(testHostRuntime, supported ? (byte)1 : (byte)0);
  }

  internal static void PauseRuntimeExecutor(nint testHostRuntime)
  {
    EnsureLoaded();
    pauseRuntimeExecutor(testHostRuntime);
  }

  internal static void ResumeRuntimeExecutor(nint testHostRuntime)
  {
    EnsureLoaded();
    resumeRuntimeExecutor(testHostRuntime);
  }

  internal static void DropNextRuntimeTask(nint testHostRuntime, JavaScriptTaskPriority priority)
  {
    EnsureLoaded();
    dropNextRuntimeTask(testHostRuntime, (int)priority);
  }

  internal static void WaitUntilRuntimeTaskQueued(
      nint testHostRuntime,
      JavaScriptTaskPriority priority
  )
  {
    EnsureLoaded();
    var error = waitUntilRuntimeTaskQueued(testHostRuntime, (int)priority);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to observe queued runtime task.");
    }
  }

  internal static void WaitUntilRuntimeTasksQueued(
      nint testHostRuntime,
      JavaScriptTaskPriority priority,
      int count
  )
  {
    EnsureLoaded();
    var error = waitUntilRuntimeTasksQueued(testHostRuntime, (int)priority, count);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to observe queued runtime tasks.");
    }
  }

  internal static void DropQueuedRuntimeTask(
      nint testHostRuntime,
      JavaScriptTaskPriority priority
  )
  {
    EnsureLoaded();
    var error = dropQueuedRuntimeTask(testHostRuntime, (int)priority);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to drop queued runtime task.");
    }
  }

  internal static void ReleaseBridgeRuntimeHandle(nint testHostRuntime)
  {
    EnsureLoaded();
    releaseBridgeRuntimeHandle(testHostRuntime);
  }

  internal static void PoisonMutableBufferDispatch(nint testHostRuntime)
  {
    EnsureLoaded();
    poisonMutableBufferDispatch(testHostRuntime);
  }

  internal static void PrepareRuntimeForInvalidation(nint testHostRuntime)
  {
    EnsureLoaded();
    prepareRuntimeForInvalidation(testHostRuntime);
  }

  internal static void FailNextPromiseHandleAllocation(nint testHostRuntime)
  {
    EnsureLoaded();
    failNextPromiseHandleAllocation(testHostRuntime);
  }

  internal static void PauseNextPromiseRegistration(nint testHostRuntime)
  {
    EnsureLoaded();
    pauseNextPromiseRegistration(testHostRuntime);
  }

  internal static bool WaitUntilPromiseRegistrationPaused(nint testHostRuntime)
  {
    EnsureLoaded();
    var error = waitUntilPromiseRegistrationPaused(testHostRuntime);
    if (error.Code != 0)
    {
      error.GetMessageAndRelease();
      return false;
    }
    return true;
  }

  internal static void ResumePromiseRegistration(nint testHostRuntime)
  {
    EnsureLoaded();
    resumePromiseRegistration(testHostRuntime);
  }

  internal static void InvalidateBridgeRuntimeStateWithoutDeletingHandle(nint testHostRuntime)
  {
    EnsureLoaded();
    invalidateBridgeRuntimeStateWithoutDeletingHandle(testHostRuntime);
  }

  internal static void PauseNextPromiseCall(nint testHostRuntime, PromiseCallOperation operation)
  {
    EnsureLoaded();
    pauseNextPromiseCall(testHostRuntime, (int)operation);
  }

  internal static bool WaitUntilPromiseCallBlocked(nint testHostRuntime)
  {
    EnsureLoaded();
    var error = waitUntilPromiseCallBlocked(testHostRuntime);
    if (error.Code != 0)
    {
      error.GetMessageAndRelease();
      return false;
    }
    return true;
  }

  internal static void ResumePromiseCall(nint testHostRuntime)
  {
    EnsureLoaded();
    resumePromiseCall(testHostRuntime);
  }

  internal static void ValidateArrayBufferSnapshot(
      nint testHostRuntime,
      bool detached,
      int currentLength,
      int capturedLength
  )
  {
    EnsureLoaded();
    var error = validateArrayBufferSnapshot(
        testHostRuntime,
        detached ? (byte)1 : (byte)0,
        currentLength,
        capturedLength
    );
    if (error.Code != 0)
    {
      ThrowNativeError(error, "ArrayBuffer snapshot validation failed.");
    }
  }

  internal static void ValidateArrayBufferLength(nint testHostRuntime, ulong length)
  {
    EnsureLoaded();
    var error = validateArrayBufferLength(testHostRuntime, length);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "ArrayBuffer length validation failed.");
    }
  }

  internal static void InvalidateRuntime(nint testHostRuntime)
  {
    EnsureLoaded();
    invalidateRuntime(testHostRuntime);
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
    collectGarbage =
      (delegate* unmanaged[Cdecl]<nint, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_collect_garbage"
      );
    pauseRuntimeExecutor =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_pause_runtime_executor"
      );
    resumeRuntimeExecutor =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_resume_runtime_executor"
      );
    dropNextRuntimeTask =
      (delegate* unmanaged[Cdecl]<nint, int, void>)LoadExport(
          library,
          "expo_jsi_testhost_drop_next_runtime_task"
      );
    waitUntilRuntimeTaskQueued =
      (delegate* unmanaged[Cdecl]<nint, int, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_runtime_task_queued"
      );
    waitUntilRuntimeTasksQueued =
      (delegate* unmanaged[Cdecl]<nint, int, int, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_runtime_tasks_queued"
      );
    dropQueuedRuntimeTask =
      (delegate* unmanaged[Cdecl]<nint, int, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_drop_queued_runtime_task"
      );
    releaseBridgeRuntimeHandle =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_release_bridge_runtime_handle"
      );
    poisonMutableBufferDispatch =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_poison_mutable_buffer_dispatch"
      );
    setSyncExecutionSupported =
      (delegate* unmanaged[Cdecl]<nint, byte, void>)LoadExport(
          library,
          "expo_jsi_testhost_set_sync_execution_supported"
      );
    prepareRuntimeForInvalidation =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_prepare_runtime_for_invalidation"
      );
    failNextPromiseHandleAllocation =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_fail_next_promise_handle_allocation"
      );
    pauseNextPromiseRegistration =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_pause_next_promise_registration"
      );
    waitUntilPromiseRegistrationPaused =
      (delegate* unmanaged[Cdecl]<nint, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_promise_registration_paused"
      );
    resumePromiseRegistration =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_resume_promise_registration"
      );
    invalidateBridgeRuntimeStateWithoutDeletingHandle =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_invalidate_bridge_runtime_state_without_deleting_handle"
      );
    pauseNextPromiseCall =
      (delegate* unmanaged[Cdecl]<nint, int, void>)LoadExport(
          library,
          "expo_jsi_testhost_pause_next_promise_call"
      );
    waitUntilPromiseCallBlocked =
      (delegate* unmanaged[Cdecl]<nint, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_promise_call_blocked"
      );
    resumePromiseCall =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_resume_promise_call"
      );
    validateArrayBufferSnapshot =
      (delegate* unmanaged[Cdecl]<nint, byte, int, int, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_validate_array_buffer_snapshot"
      );
    validateArrayBufferLength =
      (delegate* unmanaged[Cdecl]<nint, ulong, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_validate_array_buffer_length"
      );
    invalidateRuntime =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_invalidate_runtime"
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
