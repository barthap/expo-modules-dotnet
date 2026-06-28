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
    try
    {
      var result = await operation(cancellationToken).ConfigureAwait(false);
      await runtime.ScheduleAsync(
          js => SettlePromiseFromResult(js, promise, result),
          JavaScriptTaskPriority.Immediate,
          CancellationToken.None
      ).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      await runtime.ScheduleAsync(
          js => RejectPromiseWithError(js, promise, ex),
          JavaScriptTaskPriority.Immediate,
          CancellationToken.None
      ).ConfigureAwait(false);
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
