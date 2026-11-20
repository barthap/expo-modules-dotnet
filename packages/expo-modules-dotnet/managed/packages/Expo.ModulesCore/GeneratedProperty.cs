using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>
/// Installs an accessor property for generated module binding glue.
/// </summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. The supplied runtime
/// context owns getter and setter registrations through its teardown. The helper disposes all
/// temporary owned wrappers after synchronously calling <c>Object.defineProperty</c>; JavaScript
/// retains the accessor values installed in the descriptor.
/// </remarks>
public static class GeneratedProperty
{
  /// <summary>
  /// Defines an enumerable, configurable own accessor property on a generated module object or a
  /// generated shared-object class prototype.
  /// </summary>
  /// <param name="runtimeContext">The active context that owns the accessor registrations.</param>
  /// <param name="module">
  /// The object that receives the own property: the module object for module-level accessors, or
  /// the shared class prototype installed by <see cref="GeneratedSharedObjectClass.Install" />
  /// for shared-object accessors (whose getter and setter resolve their receiver from
  /// <c>thisValue</c> instead of this object).
  /// </param>
  /// <param name="name">The JavaScript property name.</param>
  /// <param name="getter">The zero-argument getter callback.</param>
  /// <param name="setter">The optional one-argument setter callback.</param>
  /// <param name="context">The generated callback state.</param>
  public static void Define(
      DotnetRuntimeContext runtimeContext,
      JavaScriptObject module,
      string name,
      JavaScriptHostFunction getter,
      JavaScriptHostFunction? setter,
      object context
  )
  {
    ArgumentNullException.ThrowIfNull(runtimeContext);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(getter);
    ArgumentNullException.ThrowIfNull(context);

    var getterRegistration = runtimeContext.RegisterHostFunction(getter, context);
    var setterRegistration = setter is null
        ? null
        : runtimeContext.RegisterHostFunction(setter, context);
    var runtime = runtimeContext.Runtime;

    using var global = runtime.Global();
    using var objectValue = global.GetProperty("Object");
    using var objectConstructor = objectValue.AsObject();
    using var definePropertyValue = objectConstructor.GetProperty("defineProperty");
    using var defineProperty = definePropertyValue.AsFunction();
    using var propertyName = runtime.CreateString(name);
    using var descriptor = runtime.CreateObject();
    using var enumerable = runtime.CreateBool(true);
    using var configurable = runtime.CreateBool(true);
    using var getterFunction = runtime.CreateHostFunction(
        $"{name} getter",
        0,
        GeneratedFunction.InvokeGeneratedHostFunction,
        getterRegistration,
        null,
        GeneratedFunction.CreateJavaScriptError
    );
    using var getterValue = getterFunction.AsValue();

    descriptor.SetProperty("enumerable", enumerable);
    descriptor.SetProperty("configurable", configurable);
    descriptor.SetProperty("get", getterValue);

    if (setterRegistration is not null)
    {
      using var setterFunction = runtime.CreateHostFunction(
          $"{name} setter",
          1,
          GeneratedFunction.InvokeGeneratedHostFunction,
          setterRegistration,
          null,
          GeneratedFunction.CreateJavaScriptError
      );
      using var setterValue = setterFunction.AsValue();
      descriptor.SetProperty("set", setterValue);
    }

    using var ignoredResult = defineProperty.CallWithThis(objectConstructor, module, propertyName, descriptor);
  }

  /// <summary>
  /// Caches a constant's encoded value on the JavaScript <c>this</c> as a non-writable, configurable
  /// data property. Does nothing when <paramref name="thisValue" /> is not an object.
  /// </summary>
  /// <remarks>
  /// A define that fails (a frozen or sealed receiver, or a non-extensible one without the own
  /// property) returns <c>false</c> and leaves the accessor in place, so the read still returns
  /// <paramref name="value" />. This method never disposes <paramref name="value" /> and keeps no
  /// handle to it: the JavaScript property slot retains it. It disposes every wrapper it creates,
  /// whether the call succeeds or throws.
  /// </remarks>
  /// <param name="runtime">The runtime that runs the current host-function call.</param>
  /// <param name="thisValue">The callback <c>this</c>, which receives the property.</param>
  /// <param name="name">The JavaScript property name.</param>
  /// <param name="enumerable">Whether the cached property is enumerable.</param>
  /// <param name="value">The encoded value that the getter also returns.</param>
  // @ref docs/specs/modules-core-functions-and-properties.md "Constant Properties Read Their Getter Once"
  // Reflect.defineProperty returns false for a frozen receiver where Object.defineProperty would
  // throw, so do not swap them: a frozen receiver must still get the value.
  public static void CacheConstant(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      string name,
      bool enumerable,
      JavaScriptValue value
  )
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(value);

    if (!thisValue.IsObject)
    {
      return;
    }

    using var receiver = thisValue.Retain();
    using var global = runtime.Global();
    using var reflectValue = global.GetProperty("Reflect");
    using var reflect = reflectValue.AsObject();
    using var definePropertyValue = reflect.GetProperty("defineProperty");
    using var defineProperty = definePropertyValue.AsFunction();
    using var propertyName = runtime.CreateString(name);
    using var descriptor = runtime.CreateObject();
    using var writable = runtime.CreateBool(false);
    using var enumerableValue = runtime.CreateBool(enumerable);
    using var configurable = runtime.CreateBool(true);

    descriptor.SetProperty("value", value);
    descriptor.SetProperty("writable", writable);
    descriptor.SetProperty("enumerable", enumerableValue);
    descriptor.SetProperty("configurable", configurable);

    using var ignoredResult = defineProperty.CallWithThis(reflect, receiver, propertyName, descriptor);
  }
}
