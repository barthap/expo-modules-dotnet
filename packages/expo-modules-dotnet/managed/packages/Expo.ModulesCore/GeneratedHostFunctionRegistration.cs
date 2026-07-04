using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class GeneratedHostFunctionRegistration : IDisposable
{
  private readonly object gate = new();
  private JavaScriptHostFunction? callback;
  private object? callbackState;
  private bool disposed;

  internal GeneratedHostFunctionRegistration(
      DotnetRuntimeContext runtimeContext,
      JavaScriptHostFunction callback,
      object callbackState)
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(callbackState);

    RuntimeContext = runtimeContext;
    this.callback = callback;
    this.callbackState = callbackState;
  }

  public DotnetRuntimeContext RuntimeContext { get; }

  public object CallbackState =>
      callbackState ?? throw new ObjectDisposedException(typeof(DotnetRuntimeContext).FullName);

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

    var previousContext = GeneratedFunction.SetCurrentRuntimeContext(RuntimeContext);
    try
    {
      return callbackSnapshot(runtime, thisValue, arguments, callbackStateSnapshot);
    }
    finally
    {
      GeneratedFunction.SetCurrentRuntimeContext(previousContext);
    }
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
