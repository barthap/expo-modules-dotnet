using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class ModuleRegistry
{
  private readonly object gate = new();
  private readonly JavaScriptRuntime runtime;
  private readonly Dictionary<string, object> moduleInstances = new(StringComparer.Ordinal);
  private bool disposed;

  internal ModuleRegistry(JavaScriptRuntime runtime)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
  }

  public T GetOrCreateModule<T>(string moduleName, Func<T> factory)
      where T : class
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
    ArgumentNullException.ThrowIfNull(factory);

    lock (gate)
    {
      ThrowIfDisposedLocked();

      if (moduleInstances.TryGetValue(moduleName, out var existing))
      {
        return (T)existing;
      }

      var created = factory() ?? throw new InvalidOperationException(
          $"Module factory for '{moduleName}' returned null."
      );
      moduleInstances.Add(moduleName, created);
      return created;
    }
  }

  public JavaScriptObject DefineModule(JavaScriptObject modules, string moduleName) =>
      WithLiveRegistry(() => DefineModule(runtime, modules, moduleName));

  public JavaScriptObject GetOrCreateExpoModulesObject() =>
      WithLiveRegistry(() => GetOrCreateExpoModulesObject(runtime));

  public JavaScriptObject GetOrCreateDotnetModulesObject() =>
      WithLiveRegistry(() => GetOrCreateDotnetModulesObject(runtime));

  internal void Dispose()
  {
    List<IDisposable> disposableModules;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      disposableModules = moduleInstances.Values.OfType<IDisposable>().ToList();
      moduleInstances.Clear();
    }

    foreach (var module in disposableModules)
    {
      module.Dispose();
    }
  }

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

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, typeof(ModuleRegistry));
  }

  private T WithLiveRegistry<T>(Func<T> action)
  {
    lock (gate)
    {
      ThrowIfDisposedLocked();
    }
    return action();
  }
}
