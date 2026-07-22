using System.Runtime.CompilerServices;
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
/// Callback that creates the authored managed instance for a generated shared-object class.
/// </summary>
/// <param name="runtime">Runtime that owns the callback invocation.</param>
/// <param name="arguments">
/// Scoped reference to the JavaScript constructor arguments array. It is valid only during the
/// callback. Generated code decodes elements positionally and calls the authored constructor
/// directly.
/// </param>
/// <param name="context">Opaque installation state; generated factories ignore it.</param>
/// <returns>
/// The authored managed instance. <see cref="GeneratedSharedObjectClass" /> pairs it through the
/// constructor-owned registry path, so a later pairing failure releases it exactly once.
/// </returns>
public delegate SharedObject GeneratedSharedObjectFactory(
    JavaScriptRuntime runtime,
    JavaScriptArrayRef arguments,
    object context);

/// <summary>
/// Creates generated shared-object constructors backed by managed callbacks and owns each
/// context's class installations (registration, shared prototype, and constructor).
/// </summary>
/// <remarks>
/// Constructor wrappers returned by <see cref="Define" /> are owned by the caller. Installations
/// created by <see cref="Install" /> are owned by the supplied <see cref="DotnetRuntimeContext" />
/// and are disposed with it. All callback registrations become unusable when that context is
/// disposed.
/// </remarks>
public static class GeneratedSharedObjectClass
{
  private static readonly ConditionalWeakTable<
      DotnetRuntimeContext,
      Dictionary<Type, Installation>> installations = new();

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
    return CreateConstructorProxy(
        runtimeContext,
        name,
        parameterCount,
        InvokeConstructTrap,
        constructorState,
        prototype: null
    );
  }

  /// <summary>
  /// Installs a generated shared-object class for one runtime context, exactly once per class.
  /// </summary>
  /// <param name="runtimeContext">The context that owns the installation until its disposal.</param>
  /// <param name="module">The owning module object that exposes the class constructor.</param>
  /// <param name="sharedObjectType">The exact authored shared-object type.</param>
  /// <param name="name">The JavaScript class name.</param>
  /// <param name="parameterCount">The declared JavaScript constructor parameter count.</param>
  /// <param name="constructorFactory">
  /// The generated factory that decodes constructor arguments and directly calls the authored
  /// constructor, or <see langword="null" /> for a native-created-only class. Only a class with a
  /// factory exposes a constructor as the module's class-name property.
  /// </param>
  /// <param name="memberInstaller">
  /// Generated callback that installs prototype methods and accessors on the shared class
  /// prototype, or <see langword="null" /> when the class declares no members.
  /// </param>
  /// <remarks>
  /// A repeated call for the same context and type reuses the existing installation and only
  /// re-exposes the constructor on <paramref name="module" />. The installation (class
  /// registration, shared prototype, and constructor wrapper) is disposed with the context.
  /// </remarks>
  public static void Install(
      DotnetRuntimeContext runtimeContext,
      JavaScriptObject module,
      Type sharedObjectType,
      string name,
      uint parameterCount,
      GeneratedSharedObjectFactory? constructorFactory,
      Action<DotnetRuntimeContext, JavaScriptObject>? memberInstaller,
      Action<DotnetRuntimeContext, SharedObject>? eventInitializer = null
  )
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(sharedObjectType);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);

    var contextInstallations = installations.GetOrCreateValue(runtimeContext);
    Installation? installation;
    lock (contextInstallations)
    {
      contextInstallations.TryGetValue(sharedObjectType, out installation);
    }

    if (installation is null)
    {
      installation = CreateInstallation(
          runtimeContext,
          sharedObjectType,
          name,
          parameterCount,
          constructorFactory,
          memberInstaller,
          eventInitializer
      );
      lock (contextInstallations)
      {
        contextInstallations.Add(sharedObjectType, installation);
      }
    }

    if (installation.Constructor is not null)
    {
      using var constructorValue = installation.Constructor.AsValue();
      module.SetProperty(name, constructorValue);
    }
  }

  /// <summary>
  /// Validates a generated constructor's argument count.
  /// </summary>
  /// <exception cref="ArgumentException">
  /// Thrown when the argument count is outside the declared range.
  /// </exception>
  public static void RequireArgumentCount(
      string className,
      JavaScriptArrayRef arguments,
      uint min,
      uint max
  )
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(className);
    if (min > max)
    {
      throw new ArgumentOutOfRangeException(nameof(min), "Minimum count cannot exceed maximum count.");
    }

    var count = arguments.Length;
    if (count < min || count > max)
    {
      throw new ArgumentException(
          min == max
              ? $"new {className} expects {min} arguments, got {count}."
              : $"new {className} expects between {min} and {max} arguments, got {count}."
      );
    }
  }

  /// <summary>
  /// Resolves the installed class registration for a shared-object type in one context.
  /// </summary>
  internal static SharedObjectClassRegistration GetRegistration(
      DotnetRuntimeContext runtimeContext,
      Type sharedObjectType
  )
  {
    if (installations.TryGetValue(runtimeContext, out var contextInstallations))
    {
      lock (contextInstallations)
      {
        if (contextInstallations.TryGetValue(sharedObjectType, out var installation))
        {
          return installation.Registration;
        }
      }
    }

    throw new InvalidOperationException(
        $"The shared object class '{sharedObjectType}' is not installed for this runtime context."
    );
  }

  private static Installation CreateInstallation(
      DotnetRuntimeContext runtimeContext,
      Type sharedObjectType,
      string name,
      uint parameterCount,
      GeneratedSharedObjectFactory? constructorFactory,
      Action<DotnetRuntimeContext, JavaScriptObject>? memberInstaller,
      Action<DotnetRuntimeContext, SharedObject>? eventInitializer
  )
  {
    var registration = SharedObjectClassRegistration.Create(
        runtimeContext.SharedObjects,
        sharedObjectType,
        eventInitializer is null ? null : sharedObject => eventInitializer(runtimeContext, sharedObject)
    );
    try
    {
      memberInstaller?.Invoke(runtimeContext, registration.Prototype);

      var installation = new Installation(registration);
      if (constructorFactory is not null)
      {
        var factoryState = new FactoryState(constructorFactory, installation);
        installation.Constructor = CreateConstructorProxy(
            runtimeContext,
            name,
            parameterCount,
            InvokePairingConstructTrap,
            factoryState,
            registration.Prototype
        );
      }

      try
      {
        runtimeContext.RegisterRetainedCallback(installation);
      }
      catch
      {
        installation.Constructor?.Dispose();
        throw;
      }
      return installation;
    }
    catch
    {
      registration.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Builds the constructable class proxy shared by <see cref="Define" /> and
  /// <see cref="Install" />.
  /// </summary>
  /// <remarks>
  /// When <paramref name="prototype" /> is supplied, it becomes the class target's
  /// <c>prototype</c> property before the proxy is created, so the constructor's visible
  /// prototype, the shared class prototype used to pair encoded instances, and the prototype of
  /// constructor-created instances are all the same object.
  /// </remarks>
  private static JavaScriptFunction CreateConstructorProxy(
      DotnetRuntimeContext runtimeContext,
      string name,
      uint parameterCount,
      JavaScriptHostFunction constructTrapCallback,
      object trapState,
      JavaScriptObject? prototype
  )
  {
    var constructRegistration = runtimeContext.RegisterHostFunction(
        constructTrapCallback,
        trapState
    );
    try
    {
      var applyRegistration = runtimeContext.RegisterHostFunction(
          RejectApply,
          new ApplyState(name)
      );
      try
      {
        using var constructorTarget = runtimeContext.Runtime.CreateClass(name);
        DefineTargetLength(runtimeContext.Runtime, constructorTarget, parameterCount);
        if (prototype is not null)
        {
          using var targetValue = constructorTarget.AsValue();
          using var targetObject = targetValue.AsObject();
          using var prototypeValue = prototype.AsValue();
          targetObject.SetProperty("prototype", prototypeValue);
        }

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
        using var visiblePrototypeValue = proxyFunctionObject.GetProperty("prototype");
        using var visiblePrototype = visiblePrototypeValue.AsObject();
        using var constructorValue = proxyFunction.AsValue();
        visiblePrototype.SetProperty("constructor", constructorValue);
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
    using var targetValue = constructorTarget.AsValue();
    using var lengthName = runtime.CreateString("length");
    using var defineResult = defineProperty.Call(targetValue, lengthName, descriptor);
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
      ReparentOntoNewTargetPrototype(runtime, instance, arguments);
      return instance;
    }
    catch
    {
      instance.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Runs the <c>construct</c> trap for an installed class: creates the authored instance
  /// through the generated factory and pairs it via the constructor-owned registry path.
  /// </summary>
  private static JavaScriptValue InvokePairingConstructTrap(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (FactoryState)context;
    var argumentsList = arguments.GetValue(1).AsArray();

    var authored = state.Factory(runtime, argumentsList, state.Installation);
    var registration = state.Installation.Registration;
    using var paired = registration.Registry.PairConstructorOwnedInstance(authored, registration);
    var instance = paired.AsValue();
    try
    {
      ReparentOntoNewTargetPrototype(runtime, instance, arguments);
      return instance;
    }
    catch
    {
      instance.Dispose();
      throw;
    }
  }

  private static void ReparentOntoNewTargetPrototype(
      JavaScriptRuntime runtime,
      JavaScriptValue instance,
      JavaScriptArguments arguments
  )
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
  }

  private static JavaScriptValue RejectApply(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context
  )
  {
    var state = (ApplyState)context;
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

  private sealed class FactoryState(
      GeneratedSharedObjectFactory factory,
      Installation installation
  )
  {
    public GeneratedSharedObjectFactory Factory { get; } = factory;

    public Installation Installation { get; } = installation;
  }

  private sealed class ApplyState(string name)
  {
    public string Name { get; } = name;
  }

  private sealed class Installation(SharedObjectClassRegistration registration) : IDisposable
  {
    public SharedObjectClassRegistration Registration { get; } = registration;

    public JavaScriptFunction? Constructor { get; set; }

    public void Dispose()
    {
      Constructor?.Dispose();
      Registration.Dispose();
    }
  }
}
