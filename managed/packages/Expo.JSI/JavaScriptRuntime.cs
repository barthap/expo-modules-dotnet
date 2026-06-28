using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Expo.JSI.Interop;

namespace Expo.JSI;

public enum JavaScriptTaskPriority
{
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
}

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

  public bool CanExecuteSync => context.Api->CanExecuteSync(context.RuntimeHandle);

  public JavaScriptValue CreateNumber(double value)
  {
    var result = context.Api->CreateNumberValue(context.RuntimeHandle, value);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript number.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  public JavaScriptValue CreateBool(bool value)
  {
    var result = context.Api->CreateBoolValue(context.RuntimeHandle, value);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript boolean.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  public JavaScriptValue CreateString(string value)
  {
    ArgumentNullException.ThrowIfNull(value);

    var result = context.Api->CreateStringValue(context.RuntimeHandle, value);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript string.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  public JavaScriptObject Global()
  {
    var result = context.Api->GetGlobal(context.RuntimeHandle);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript global object.");
    }
    return new JavaScriptObject(context, result.Object);
  }

  public JavaScriptObject CreateObject()
  {
    var result = context.Api->CreateObjectValue(context.RuntimeHandle);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript object.");
    }
    return new JavaScriptObject(context, result.Object);
  }

  public JavaScriptArray CreateArray(uint length = 0)
  {
    var result = context.Api->CreateArrayValue(context.RuntimeHandle, length);
    if (result.Ok == 0 || result.Array == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript array.");
    }
    return new JavaScriptArray(context, result.Array);
  }

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

    if (result.Ok == 0 || result.Function == 0)
    {
      HostFunctionContext.Release(callbackContext);
      JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
    }

    return new JavaScriptFunction(context, result.Function);
  }

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
      var thisValue = new JavaScriptBorrowedValue(jsiContext, thisValueHandle);
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
