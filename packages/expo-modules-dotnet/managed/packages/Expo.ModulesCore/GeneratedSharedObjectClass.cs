using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>
/// Creates generated shared-object constructors backed by managed callbacks.
/// </summary>
/// <remarks>
/// The returned constructor wrapper is owned by the caller. Its callback registration belongs to
/// <paramref name="runtimeContext" /> and becomes unusable when that context is disposed.
/// </remarks>
public static class GeneratedSharedObjectClass
{
  /// <summary>
  /// Creates a JavaScript constructor for a generated shared-object class.
  /// </summary>
  /// <param name="runtimeContext">The context that owns the constructor callback registration.</param>
  /// <param name="name">The JavaScript class name.</param>
  /// <param name="parameterCount">The declared JavaScript constructor parameter count.</param>
  /// <param name="callback">The generated constructor callback.</param>
  /// <param name="context">The generated callback state.</param>
  /// <returns>An owned JavaScript constructor wrapper.</returns>
  public static JavaScriptFunction Define(
      DotnetRuntimeContext runtimeContext,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context
  )
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    var constructorState = new ConstructorState(name, callback, context);
    var constructorRegistration = runtimeContext.RegisterHostFunction(
        InvokeConstructor,
        constructorState
    );
    try
    {
      using var constructorTarget = runtimeContext.Runtime.CreateHostFunction(
          name,
          parameterCount,
          GeneratedFunction.InvokeGeneratedHostFunction,
          constructorRegistration
      );
      var applyRegistration = runtimeContext.RegisterHostFunction(RejectApply, constructorState);
      try
      {
        using var handler = runtimeContext.Runtime.CreateObject();
        using var applyTrap = runtimeContext.Runtime.CreateHostFunction(
            $"{name} apply",
            3,
            GeneratedFunction.InvokeGeneratedHostFunction,
            applyRegistration
        );
        using var applyTrapValue = applyTrap.AsValue();
        handler.SetProperty("apply", applyTrapValue);

        using var global = runtimeContext.Runtime.Global();
        using var proxyValue = global.GetProperty("Proxy");
        using var proxy = proxyValue.AsFunction();
        using var proxyResult = proxy.CallAsConstructor(constructorTarget, handler);
        return proxyResult.AsFunction();
      }
      catch
      {
        applyRegistration.Dispose();
        throw;
      }
    }
    catch
    {
      constructorRegistration.Dispose();
      throw;
    }
  }

  private static JavaScriptValue InvokeConstructor(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (ConstructorState)context;
    return state.Callback(runtime, thisValue, arguments, state.CallbackState);
  }

  private static JavaScriptValue RejectApply(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (ConstructorState)context;
    throw new InvalidOperationException($"{state.Name} must be called with new.");
  }

  private sealed class ConstructorState(
      string name,
      JavaScriptHostFunction callback,
      object callbackState
  )
  {
    public string Name { get; } = name;

    public JavaScriptHostFunction Callback { get; } = callback;

    public object CallbackState { get; } = callbackState;
  }
}
