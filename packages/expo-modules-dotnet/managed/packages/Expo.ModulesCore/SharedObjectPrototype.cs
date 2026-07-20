using Expo.JSI;

namespace Expo.ModulesCore;

internal static class SharedObjectPrototype
{
  internal static object CreateReleaseCallbackState(SharedObjectRegistry registry) =>
      new WeakReference<SharedObjectRegistry>(registry);

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
