using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Priority used when scheduling managed work on the JavaScript runtime.
/// </summary>
public enum JavaScriptTaskPriority
{
  /// <summary>
  /// Run as soon as the runtime can accept work.
  /// </summary>
  Immediate = 1,

  /// <summary>
  /// Run work that blocks user-visible progress.
  /// </summary>
  UserBlocking = 2,

  /// <summary>
  /// Run normal priority work.
  /// </summary>
  Normal = 3,

  /// <summary>
  /// Run low priority work.
  /// </summary>
  Low = 4,

  /// <summary>
  /// Run idle work.
  /// </summary>
  Idle = 5,
}

/// <summary>
/// Managed access point for a JavaScript runtime exposed through the Expo JSI C ABI.
/// </summary>
/// <remarks>
/// Values and objects created by this runtime are owned wrappers unless a method explicitly returns
/// a scoped ref. Owned wrappers must be disposed by the caller. Scoped refs are valid only while
/// code is running inside <see cref="Execute{T}" />, scheduled runtime work, or a host-function
/// callback.
/// </remarks>
public sealed unsafe class JavaScriptRuntime
{
  private readonly JsiContext context;

  internal JavaScriptRuntime(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
      : this(new JsiContext(api, runtimeHandle))
  {
  }

  internal JavaScriptRuntime(JsiContext context)
  {
    this.context = context;
  }

  internal JavaScriptValue FromOwnedValueHandle(ExpoJsiValueHandle valueHandle)
  {
    if (valueHandle == 0)
    {
      throw new ArgumentNullException(nameof(valueHandle));
    }

    return JavaScriptValue.FromOwnedHandle(context, valueHandle);
  }

  /// <summary>
  /// Creates a managed runtime wrapper from native Expo JSI handles.
  /// </summary>
  /// <remarks>
  /// This method does not take ownership of <paramref name="api" /> or
  /// <paramref name="runtimeHandle" />. The native host remains responsible for keeping them valid
  /// for the lifetime of the managed wrapper.
  /// </remarks>
  public static JavaScriptRuntime FromNative(
      ExpoJsiApiHandle api,
      ExpoJsiRuntimeHandle runtimeHandle
  )
  {
    if (api == 0)
    {
      throw new ArgumentNullException(nameof(api));
    }
    if (runtimeHandle == 0)
    {
      throw new ArgumentNullException(nameof(runtimeHandle));
    }

    var nativeApi = (ExpoJsiApi*)api;
    nativeApi->Validate();

    return new JavaScriptRuntime(nativeApi, runtimeHandle);
  }

  /// <summary>
  /// Gets whether this runtime supports synchronous execution through <see cref="Execute{T}" />.
  /// </summary>
  public bool CanExecuteSync => context.Api->CanExecuteSync(context.RuntimeHandle);

  /// <summary>
  /// Creates an owned JavaScript number value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue CreateNumber(double value)
  {
    var result = context.Api->CreateNumberValue(context.RuntimeHandle, value);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript number.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  /// <summary>
  /// Creates an owned JavaScript boolean value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue CreateBool(bool value)
  {
    var result = context.Api->CreateBoolValue(context.RuntimeHandle, value);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript boolean.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  /// <summary>
  /// Creates an owned JavaScript string value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue CreateString(string value)
  {
    ArgumentNullException.ThrowIfNull(value);

    var result = context.Api->CreateStringValue(context.RuntimeHandle, value);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript string.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  /// <summary>
  /// Gets the JavaScript global object as an owned wrapper.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObject" /> owns a handle and must be disposed by the caller.
  /// </remarks>
  public JavaScriptObject Global()
  {
    var result = context.Api->GetGlobal(context.RuntimeHandle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript global object.");
    }
    return new JavaScriptObject(context, result.Value);
  }

  /// <summary>
  /// Creates an owned JavaScript object.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObject" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptObject CreateObject()
  {
    var result = context.Api->CreateObjectValue(context.RuntimeHandle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript object.");
    }
    return new JavaScriptObject(context, result.Value);
  }

  /// <summary>
  /// Creates an owned JavaScript array.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptArray" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptArray CreateArray(uint length = 0)
  {
    var result = context.Api->CreateArrayValue(context.RuntimeHandle, length);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript array.");
    }
    return new JavaScriptArray(context, result.Value);
  }

  /// <summary>
  /// Creates an owned JavaScript promise capability.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptPromise" /> must be resolved, rejected, or disposed by the
  /// caller.
  /// </remarks>
  public JavaScriptPromise CreatePromise()
  {
    var result = context.Api->CreatePromiseValue(context.RuntimeHandle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript promise.");
    }
    return new JavaScriptPromise(context, result.Promise);
  }

  /// <summary>
  /// Creates a JavaScript promise value backed by an asynchronous managed operation.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptPromiseValue" /> owns the promise value and must be disposed
  /// by the caller. The internal promise capability is released after the operation settles.
  /// </remarks>
  public JavaScriptPromiseValue CreatePromise(
      Func<CancellationToken, Task<JavaScriptPromiseResult>> operation,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(operation);

    var promise = CreatePromise();
    try
    {
      var promiseValue = promise.AsValue();
      _ = JavaScriptPromiseScheduler.SettleAsync(this, promise, operation, cancellationToken);
      return new JavaScriptPromiseValue(promiseValue);
    }
    catch
    {
      promise.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Creates an owned JavaScript Error object.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptErrorObject" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptErrorObject CreateErrorObject(string message)
  {
    ArgumentNullException.ThrowIfNull(message);

    var result = context.Api->CreateErrorValue(context.RuntimeHandle, message);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript error.");
    }
    return new JavaScriptErrorObject(JavaScriptValue.FromOwnedHandle(context, result.Value));
  }

  /// <summary>
  /// Creates an owned JavaScript host function backed by a managed callback.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptFunction" /> must be disposed by the caller. During callback
  /// invocation, <see cref="JavaScriptHostFunction" /> receives scoped refs for <c>this</c> and
  /// arguments; retain those refs before storing them beyond the callback.
  /// </remarks>
  public JavaScriptFunction CreateHostFunction(
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object callbackState
  )
  {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(callbackState);

    var nameBytes = Encoding.UTF8.GetBytes(name);
    var callbackContext = new HostFunctionContext(context.Api, callback, callbackState).ToIntPtr();

    var result = context.Api->CreateHostFunctionValue(
        context.RuntimeHandle,
        nameBytes,
        parameterCount,
        &InvokeHostFunction,
        callbackContext,
        &ReleaseHostFunctionContext
    );

    if (!result.IsOk)
    {
      HostFunctionContext.Release(callbackContext);
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
    }

    return new JavaScriptFunction(context, result.Value);
  }

  /// <summary>
  /// Schedules managed work to run on the JavaScript runtime.
  /// </summary>
  /// <remarks>
  /// Scoped refs created while <paramref name="body" /> runs are valid only until the body returns.
  /// Owned wrappers returned or created inside the body keep their normal disposal requirements.
  /// </remarks>
  public Task ScheduleAsync(
      Action<JavaScriptRuntime> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Normal,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(body);
    return ScheduleCore(
        js =>
        {
          body(js);
          return null;
        },
        priority,
        cancellationToken
    );
  }

  /// <summary>
  /// Schedules managed work to run on the JavaScript runtime and returns its result.
  /// </summary>
  /// <remarks>
  /// Scoped refs created while <paramref name="body" /> runs are valid only until the body returns.
  /// Retain refs or return owned wrappers when values must escape the scheduled body.
  /// </remarks>
  public Task<T> ExecuteAsync<T>(
      Func<JavaScriptRuntime, T> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Immediate,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(body);
    var scheduledTask = ScheduleCore(js => body(js), priority, cancellationToken);
    var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    scheduledTask.ContinueWith(
        task =>
        {
          if (task.IsCanceled)
          {
            completion.TrySetCanceled(cancellationToken);
            return;
          }
          if (task.IsFaulted)
          {
            completion.TrySetException(task.Exception!.InnerExceptions);
            return;
          }

          completion.TrySetResult((T)task.Result!);
        },
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default
    );
    return completion.Task;
  }

  /// <summary>
  /// Executes managed work synchronously on the JavaScript runtime.
  /// </summary>
  /// <remarks>
  /// Scoped refs created while <paramref name="body" /> runs are valid only until the body returns.
  /// Retain refs or return owned wrappers when values must escape the execution frame.
  /// </remarks>
  public T Execute<T>(Func<JavaScriptRuntime, T> body)
  {
    ArgumentNullException.ThrowIfNull(body);
    if (!CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript runtime execution is not supported by this host."
      );
    }

    var taskContext = RuntimeTaskContext.Allocate(context, js => body(js), CancellationToken.None);
    var task = RuntimeTaskContext.TaskFor(taskContext);
    var error = context.Api->ExecuteRuntimeTaskSync(
        context.RuntimeHandle,
        &InvokeScheduledRuntimeTask,
        taskContext,
        &ReleaseScheduledRuntimeTaskContext
    );
    if (error.Code != 0)
    {
      // The native runtime-task ABI owns taskContext after this call returns,
      // including failure paths where queued sync work is released during shutdown.
      JsiContext.ThrowNativeError(error, "Failed to execute JavaScript runtime task.");
    }

    var result = task.GetAwaiter().GetResult();
    return (T)result!;
  }

  private Task<object?> ScheduleCore(
      Func<JavaScriptRuntime, object?> body,
      JavaScriptTaskPriority priority,
      CancellationToken cancellationToken
  )
  {
    if (cancellationToken.IsCancellationRequested)
    {
      return Task.FromCanceled<object?>(cancellationToken);
    }

    var taskContext = RuntimeTaskContext.Allocate(context, body, cancellationToken);
    var task = RuntimeTaskContext.TaskFor(taskContext);
    var error = context.Api->ScheduleRuntimeTask(
        context.RuntimeHandle,
        ToNativePriority(priority),
        &InvokeScheduledRuntimeTask,
        taskContext,
        &ReleaseScheduledRuntimeTaskContext
    );
    if (error.Code != 0)
    {
      // The native runtime-task ABI owns taskContext after this call returns,
      // including early errors after native wraps the managed callback.
      JsiContext.ThrowNativeError(error, "Failed to schedule JavaScript runtime task.");
      return task;
    }

    return task;
  }

  private static ExpoJsiTaskPriority ToNativePriority(JavaScriptTaskPriority priority)
  {
    return priority switch
    {
      JavaScriptTaskPriority.Immediate => ExpoJsiTaskPriority.Immediate,
      JavaScriptTaskPriority.UserBlocking => ExpoJsiTaskPriority.UserBlocking,
      JavaScriptTaskPriority.Low => ExpoJsiTaskPriority.Low,
      JavaScriptTaskPriority.Idle => ExpoJsiTaskPriority.Idle,
      _ => ExpoJsiTaskPriority.Normal,
    };
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static ExpoJsiValueResult InvokeHostFunction(
      nint callbackContext,
      ExpoJsiRuntimeHandle runtimeHandle,
      ExpoJsiValueHandle thisValueHandle,
      ExpoJsiArgumentsHandle argumentsHandle
  )
  {
    HostFunctionContext? context = null;
    try
    {
      context = HostFunctionContext.FromIntPtr(callbackContext);
      var jsiContext = new JsiContext(context.Api, runtimeHandle);
      var runtime = new JavaScriptRuntime(jsiContext);
      using var scope = JavaScriptHandleScope.Enter(jsiContext);
      var thisValue = JavaScriptValueRef.FromBorrowedRoot(
          scope,
          new JavaScriptValueInner(jsiContext, thisValueHandle)
      );
      var arguments = new JavaScriptArguments(jsiContext, argumentsHandle);
      using var result = context.Callback(runtime, thisValue, arguments, context.Context);
      return new ExpoJsiValueResult(1, result.Detach(), default);
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return new ExpoJsiValueResult(0, 0, context?.CaptureException(ex) ?? default);
    }
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseHostFunctionContext(nint callbackContext)
  {
    HostFunctionContext.Release(callbackContext);
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void InvokeScheduledRuntimeTask(nint taskContext)
  {
    RuntimeTaskContext.FromIntPtr(taskContext).Invoke();
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseScheduledRuntimeTaskContext(nint taskContext)
  {
    RuntimeTaskContext.Release(taskContext);
  }

  private sealed class RuntimeTaskContext
  {
    private readonly JsiContext context;
    private readonly Func<JavaScriptRuntime, object?> body;
    private readonly CancellationToken cancellationToken;
    private readonly TaskCompletionSource<object?> completion =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RuntimeTaskContext(
        JsiContext context,
        Func<JavaScriptRuntime, object?> body,
        CancellationToken cancellationToken
    )
    {
      this.context = context;
      this.body = body;
      this.cancellationToken = cancellationToken;
    }

    public Task<object?> Task => completion.Task;

    public static nint Allocate(
        JsiContext context,
        Func<JavaScriptRuntime, object?> body,
        CancellationToken cancellationToken
    )
    {
      var handle = GCHandle.Alloc(new RuntimeTaskContext(context, body, cancellationToken));
      return GCHandle.ToIntPtr(handle);
    }

    public static RuntimeTaskContext FromIntPtr(nint pointer)
    {
      return (RuntimeTaskContext)GCHandle.FromIntPtr(pointer).Target!;
    }

    public static Task<object?> TaskFor(nint pointer)
    {
      return FromIntPtr(pointer).Task;
    }

    public static void Release(nint pointer)
    {
      if (pointer == 0)
      {
        return;
      }

      var handle = GCHandle.FromIntPtr(pointer);
      if (handle.Target is RuntimeTaskContext context)
      {
        context.FaultIfReleasedBeforeRunning();
      }
      handle.Free();
    }

    public void Invoke()
    {
      if (cancellationToken.IsCancellationRequested)
      {
        completion.TrySetCanceled(cancellationToken);
        return;
      }

      try
      {
        using var scope = JavaScriptHandleScope.Enter(context);
        completion.TrySetResult(body(new JavaScriptRuntime(context)));
      }
      catch (Exception ex)
      {
        completion.TrySetException(ex);
      }
    }

    private void FaultIfReleasedBeforeRunning()
    {
      if (completion.Task.IsCompleted)
      {
        return;
      }

      completion.TrySetException(new ObjectDisposedException(
          nameof(JavaScriptRuntime),
          "Scheduled JavaScript runtime work was released before it ran."
      ));
    }
  }
}
