using Expo.JSI;

namespace Expo.ModulesCore;

public static class ModuleRegistry
{
  public static JavaScriptObject DefineModule(JavaScriptRuntime runtime, string moduleName)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

    using var global = runtime.Global();
    using var expo = GetOrCreateObject(runtime, global, "expo");
    using var modules = GetOrCreateObject(runtime, expo, "modules");

    var module = runtime.CreateObject();
    using var moduleValue = module.AsValue();
    modules.SetProperty(moduleName, moduleValue);
    return module;
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
