namespace Expo.JSI;

internal static class JavaScriptPromiseScheduler
{
  public static async Task SettleAsync(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Func<CancellationToken, Task<JavaScriptPromiseResult>> operation,
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
          js => RejectPromiseWithErrorAndDispose(js, promise, ex),
          JavaScriptTaskPriority.Immediate,
          CancellationToken.None
      ).ConfigureAwait(false);
      return;
    }

    await runtime.ScheduleAsync(
        js => SettlePromiseFromResultAndDispose(js, promise, result),
        JavaScriptTaskPriority.Immediate,
        CancellationToken.None
    ).ConfigureAwait(false);
  }

  private static void SettlePromiseFromResultAndDispose(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      JavaScriptPromiseResult result
  )
  {
    try
    {
      SettlePromiseFromResult(runtime, promise, result);
    }
    finally
    {
      promise.Dispose();
    }
  }

  private static void SettlePromiseFromResult(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      JavaScriptPromiseResult result
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
      RejectPromiseWithError(runtime, promise, ex);
    }
  }

  private static void RejectPromiseWithErrorAndDispose(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Exception exception
  )
  {
    try
    {
      RejectPromiseWithError(runtime, promise, exception);
    }
    finally
    {
      promise.Dispose();
    }
  }

  private static void RejectPromiseWithError(
      JavaScriptRuntime runtime,
      JavaScriptPromise promise,
      Exception exception
  )
  {
    using var error = runtime.CreateErrorObject(exception.Message);
    using var value = error.AsValue();
    promise.Reject(value);
  }
}
