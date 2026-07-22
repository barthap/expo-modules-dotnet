using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>
/// Callback invoked when JavaScript constructs a generated shared-object class with <c>new</c>.
/// </summary>
/// <param name="runtime">Runtime that owns the callback invocation.</param>
/// <param name="arguments">
/// Scoped reference to the JavaScript constructor arguments array. It is valid only during the
/// callback. Read elements positionally with <see cref="JavaScriptArrayRef.GetValue" />.
/// </param>
/// <param name="context">Managed callback state supplied to
/// <see cref="GeneratedSharedObjectClass.Define" />.</param>
/// <returns>
/// An owned JavaScript object value that becomes the constructed instance. The helper re-parents
/// it onto the class prototype and returns it to JavaScript, taking ownership of the handle.
/// </returns>
public delegate JavaScriptValue GeneratedSharedObjectConstructor(
    JavaScriptRuntime runtime,
    JavaScriptArrayRef arguments,
    object context);

/// <summary>
/// Creates generated shared-object constructors backed by managed callbacks.
/// </summary>
/// <remarks>
/// The returned constructor wrapper is owned by the caller. Its callback registrations belong to
/// the supplied <see cref="DotnetRuntimeContext" /> and become unusable when that context is
/// disposed.
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
  /// <remarks>
  /// <para>
  /// A managed host function has no real <c>prototype</c> object, Hermes does not treat host
  /// functions as spec constructors (so <c>Reflect.construct</c> rejects them), and re-entering
  /// the native <c>callAsConstructor</c> path from inside another host-function callback is not
  /// supported. The constructor therefore never routes through host-function construction at all.
  /// </para>
  /// <para>
  /// Instead, a real <see cref="JavaScriptRuntime.CreateClass" /> target (an ordinary function
  /// with an ordinary <c>prototype</c> object) is wrapped in a <c>Proxy</c>. The proxy's
  /// <c>construct</c> trap is a context-owned host function that Hermes calls as a normal
  /// function with <c>(target, argumentsList, newTarget)</c>; it invokes the generated
  /// <see cref="GeneratedSharedObjectConstructor" /> callback with the arguments array, re-parents
  /// the returned instance onto <c>newTarget.prototype</c> (forwarded by the proxy to the class
  /// target's prototype), and returns it. The proxy's <c>apply</c> trap rejects calling the
  /// constructor without <c>new</c>.
  /// </para>
  /// </remarks>
  public static JavaScriptFunction Define(
      DotnetRuntimeContext runtimeContext,
      string name,
      uint parameterCount,
      GeneratedSharedObjectConstructor callback,
      object context
  )
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    var constructorState = new ConstructorState(name, callback, context);
    var constructRegistration = runtimeContext.RegisterHostFunction(
        InvokeConstructTrap,
        constructorState
    );
    try
    {
      var applyRegistration = runtimeContext.RegisterHostFunction(RejectApply, constructorState);
      try
      {
        using var constructorTarget = runtimeContext.Runtime.CreateClass(name);
        DefineTargetLength(runtimeContext.Runtime, constructorTarget, parameterCount);

        using var handler = runtimeContext.Runtime.CreateObject();

        using var constructTrap = runtimeContext.Runtime.CreateHostFunction(
            $"{name} construct",
            3,
            GeneratedFunction.InvokeGeneratedHostFunction,
            constructRegistration
        );
        using var constructTrapValue = constructTrap.AsValue();
        handler.SetProperty("construct", constructTrapValue);

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
        using var proxyFunction = proxyResult.AsFunction();
        using var proxyFunctionValue = proxyFunction.AsValue();
        using var proxyFunctionObject = proxyFunctionValue.AsObject();
        using var prototypeValue = proxyFunctionObject.GetProperty("prototype");
        using var prototype = prototypeValue.AsObject();
        using var constructorValue = proxyFunction.AsValue();
        prototype.SetProperty("constructor", constructorValue);
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
      constructRegistration.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Defines the declared constructor arity as the class target's <c>length</c>.
  /// </summary>
  /// <remarks>
  /// The <see cref="JavaScriptRuntime.CreateClass" /> target is a rest-parameter function whose
  /// own <c>length</c> is zero, so the declared arity is installed with
  /// <c>Object.defineProperty</c> (function <c>length</c> is configurable). The proxy forwards
  /// <c>length</c> reads to the target.
  /// </remarks>
  private static void DefineTargetLength(
      JavaScriptRuntime runtime,
      JavaScriptFunction constructorTarget,
      uint parameterCount
  )
  {
    using var descriptor = runtime.CreateObject();
    using var lengthValue = runtime.CreateNumber(parameterCount);
    descriptor.SetProperty("value", lengthValue);
    using var configurableValue = runtime.CreateBool(true);
    descriptor.SetProperty("configurable", configurableValue);

    using var global = runtime.Global();
    using var objectValue = global.GetProperty("Object");
    using var objectConstructor = objectValue.AsObject();
    using var definePropertyValue = objectConstructor.GetProperty("defineProperty");
    using var defineProperty = definePropertyValue.AsFunction();
    using var lengthName = runtime.CreateString("length");
    using var defineResult = defineProperty.Call(constructorTarget, lengthName, descriptor);
  }

  /// <summary>
  /// Runs the <c>Proxy</c> <c>construct</c> trap: <c>(target, argumentsList, newTarget)</c>.
  /// </summary>
  /// <remarks>
  /// Invokes the generated constructor callback with the arguments array, then re-parents the
  /// returned instance onto <c>newTarget.prototype</c> so it carries the exact class prototype,
  /// including subclassing through <c>newTarget</c>. No host-function construction happens here;
  /// the trap itself runs as an ordinary call.
  /// </remarks>
  private static JavaScriptValue InvokeConstructTrap(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (ConstructorState)context;
    var argumentsList = arguments.GetValue(1).AsArray();

    var instance = state.Callback(runtime, argumentsList, state.CallbackState);
    try
    {
      using var newTarget = arguments.GetValue(2).Retain();
      using var newTargetObject = newTarget.AsObject();
      using var newTargetPrototype = newTargetObject.GetProperty("prototype");

      using var global = runtime.Global();
      using var objectValue = global.GetProperty("Object");
      using var objectConstructor = objectValue.AsObject();
      using var setPrototypeOfValue = objectConstructor.GetProperty("setPrototypeOf");
      using var setPrototypeOf = setPrototypeOfValue.AsFunction();
      using var setPrototypeOfResult = setPrototypeOf.Call(instance, newTargetPrototype);

      return instance;
    }
    catch
    {
      instance.Dispose();
      throw;
    }
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
      GeneratedSharedObjectConstructor callback,
      object callbackState
  )
  {
    public string Name { get; } = name;

    public GeneratedSharedObjectConstructor Callback { get; } = callback;

    public object CallbackState { get; } = callbackState;
  }
}
