using Expo.JSI.Interop;

namespace Expo.JSI;

internal static class JavaScriptPromiseScheduler
{
  public static async Task SettleAsync(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Func<CancellationToken, Task<JavaScriptPromiseResult>> operation,
      JavaScriptErrorFactory? createError,
      CancellationToken cancellationToken
  )
  {
    JavaScriptPromiseResult result;
    try
    {
      result = await operation(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      await runtime.ScheduleAsync(
          js => RejectPromiseWithErrorAndDispose(js, promise, ex, createError),
          JavaScriptTaskPriority.Immediate,
          CancellationToken.None
      ).ConfigureAwait(false);
      return;
    }

    try
    {
      await runtime.ScheduleAsync(
          js => SettlePromiseFromResultAndDispose(js, promise, result, createError),
          JavaScriptTaskPriority.Immediate,
          CancellationToken.None
      ).ConfigureAwait(false);
    }
    finally
    {
      result.Abandon();
    }
  }

  private static void SettlePromiseFromResultAndDispose(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      JavaScriptPromiseResult result,
      JavaScriptErrorFactory? createError
  )
  {
    try
    {
      SettlePromiseFromResult(runtime, promise, result, createError);
    }
    finally
    {
      promise.Dispose();
    }
  }

  private static void SettlePromiseFromResult(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      JavaScriptPromiseResult result,
      JavaScriptErrorFactory? createError
  )
  {
    try
    {
      using var value = result.CreateValue(runtime);
      if (result.IsRejected)
      {
        promise.Reject(value);
      }
      else
      {
        promise.Resolve(value);
      }
    }
    catch (Exception ex)
    {
      RejectPromiseWithError(runtime, promise, ex, createError);
    }
  }

  private static void RejectPromiseWithErrorAndDispose(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Exception exception,
      JavaScriptErrorFactory? createError
  )
  {
    try
    {
      RejectPromiseWithError(runtime, promise, exception, createError);
    }
    finally
    {
      promise.Dispose();
    }
  }

  private static void RejectPromiseWithError(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Exception exception,
      JavaScriptErrorFactory? createError
  )
  {
    var reason = createError is null ? null : TryCreateReason(runtime, exception, createError);
    if (reason is not null)
    {
      using (reason)
      {
        promise.Reject(reason);
      }
      return;
    }

    using var error = runtime.CreateErrorObject(exception.Message);
    using var value = error.AsValue();
    promise.Reject(value);
  }

  // The factory's value is owned here until Reject returns. A failing factory has already released
  // what it created, and a null result owns nothing; the caller falls back to an Error built from
  // the original Message, so the promise always settles. A failing factory is reported the same way
  // as on the host-function path.
  private static JavaScriptValue? TryCreateReason(
      JavaScriptRuntime runtime,
      Exception exception,
      JavaScriptErrorFactory createError
  )
  {
    try
    {
      return createError(runtime, exception);
    }
    catch (Exception factoryException)
    {
      HostFunctionContext.ReportException(factoryException);
      return null;
    }
  }
}
