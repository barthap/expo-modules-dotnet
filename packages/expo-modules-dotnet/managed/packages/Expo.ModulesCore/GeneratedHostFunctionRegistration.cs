using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class GeneratedHostFunctionRegistration : IDisposable
{
  private readonly object gate = new();
  private JavaScriptHostFunction? callback;
  private object? callbackState;
  private bool disposed;

  internal GeneratedHostFunctionRegistration(JavaScriptHostFunction callback, object callbackState)
  {
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(callbackState);

    this.callback = callback;
    this.callbackState = callbackState;
  }

  internal JavaScriptValue Invoke(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments
  )
  {
    JavaScriptHostFunction callbackSnapshot;
    object callbackStateSnapshot;

    lock (gate)
    {
      ObjectDisposedException.ThrowIf(disposed, typeof(DotnetRuntimeContext));
      callbackSnapshot = callback!;
      callbackStateSnapshot = callbackState!;
    }

    return callbackSnapshot(runtime, thisValue, arguments, callbackStateSnapshot);
  }

  public void Dispose()
  {
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      callback = null;
      callbackState = null;
      disposed = true;
    }
  }
}
