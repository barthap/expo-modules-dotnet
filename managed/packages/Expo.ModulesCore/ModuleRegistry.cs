using Expo.JSI;

namespace Expo.ModulesCore;

public static class ModuleRegistry
{
  public static JavaScriptObject DefineModule(
      JavaScriptRuntime runtime,
      JavaScriptObject modules,
      string moduleName)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(modules);
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

    using var existingModuleValue = modules.GetProperty(moduleName);
    if (existingModuleValue.IsObject)
    {
      return existingModuleValue.AsObject();
    }

    var module = runtime.CreateObject();
    using var moduleValue = module.AsValue();
    modules.SetProperty(moduleName, moduleValue);
    return module;
  }

  public static JavaScriptObject GetOrCreateExpoModulesObject(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);

    using var global = runtime.Global();
    using var expo = GetOrCreateObject(runtime, global, "expo");
    return GetOrCreateObject(runtime, expo, "modules");
  }

  public static JavaScriptObject GetOrCreateDotnetModulesObject(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);

    using var global = runtime.Global();
    using var expoDotnet = GetOrCreateObject(runtime, global, "_expoDotnet");
    return GetOrCreateObject(runtime, expoDotnet, "modules");
  }

  private static JavaScriptObject GetOrCreateObject(
      JavaScriptRuntime runtime,
      JavaScriptObject owner,
      string propertyName)
  {
    using var existingValue = owner.GetProperty(propertyName);
    if (existingValue.IsObject)
    {
      return existingValue.AsObject();
    }

    var created = runtime.CreateObject();
    using var createdValue = created.AsValue();
    owner.SetProperty(propertyName, createdValue);
    return created;
  }
}
