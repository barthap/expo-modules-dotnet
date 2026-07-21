using Expo.JSI;

namespace Expo.ModulesCore;

internal static class SharedObjectPrototype
{
  internal static object CreateReleaseCallbackState(SharedObjectRegistry registry) =>
      new WeakReference<SharedObjectRegistry>(registry);

  /// <summary>
  /// Creates the shared class prototype for a registered public shared-object class. The
  /// idempotent <c>release</c> host function is installed once on the prototype; its callback
  /// state holds the owning registry only weakly.
  /// </summary>
  internal static JavaScriptObject CreateClassPrototype(
      JavaScriptRuntime runtime,
      SharedObjectRegistry registry)
  {
    var prototype = runtime.CreateObject();
    try
    {
      using var releaseFunction = runtime.CreateHostFunction(
          "release",
          0,
          Release,
          CreateReleaseCallbackState(registry)
      );
      using var releaseValue = releaseFunction.AsValue();
      prototype.SetProperty("release", releaseValue);
      return prototype;
    }
    catch
    {
      prototype.Dispose();
      throw;
    }
  }

  internal static JavaScriptObject CreateInstance(
      JavaScriptRuntime runtime,
      SharedObjectRegistry registry,
      Action? installFailureForTesting)
  {
    using var prototype = runtime.CreateObject();
    using var releaseFunction = runtime.CreateHostFunction(
        "release",
        0,
        Release,
        CreateReleaseCallbackState(registry)
    );
    using var releaseValue = releaseFunction.AsValue();
    installFailureForTesting?.Invoke();
    prototype.SetProperty("release", releaseValue);
    return runtime.CreateObjectWithPrototype(prototype);
  }

  private static JavaScriptValue Release(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object callbackState)
  {
    var registryReference = (WeakReference<SharedObjectRegistry>)callbackState;
    if (!registryReference.TryGetTarget(out var registry))
    {
      throw new ObjectDisposedException(nameof(SharedObjectRegistry));
    }

    using var target = thisValue.AsObject().Retain();
    registry.ReleaseFromJavaScript(target);
    return runtime.CreateUndefined();
  }
}
