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
  /// Defines an enumerable, configurable own accessor property on a generated module object.
  /// </summary>
  /// <param name="runtimeContext">The active context that owns the accessor registrations.</param>
  /// <param name="module">The module object that receives the own property.</param>
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
        getterRegistration
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
          setterRegistration
      );
      using var setterValue = setterFunction.AsValue();
      descriptor.SetProperty("set", setterValue);
    }

    using var ignoredResult = defineProperty.CallWithThis(objectConstructor, module, propertyName, descriptor);
  }
}
