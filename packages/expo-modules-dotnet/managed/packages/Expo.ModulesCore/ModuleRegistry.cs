using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class ModuleRegistry
{
  private readonly object gate = new();
  private readonly JavaScriptRuntime runtime;
  private readonly JavaScriptObjectFactory? objectFactory;
  private readonly Dictionary<string, ModuleEntry> moduleInstances = new(StringComparer.Ordinal);
  private bool disposed;

  internal ModuleRegistry(JavaScriptRuntime runtime, JavaScriptObjectFactory objectFactory)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
  }

  public T GetOrCreateModule<T>(string moduleName, Func<T> factory)
      where T : class
  {
    return GetOrCreateModule(moduleName, factory, null, null);
  }

  public T GetOrCreateModule<T>(
      string moduleName,
      Func<T> factory,
      Action<T>? onCreate,
      Action<T>? onDestroy)
      where T : class
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
    ArgumentNullException.ThrowIfNull(factory);

    T created;
    lock (gate)
    {
      ThrowIfDisposedLocked();

      if (moduleInstances.TryGetValue(moduleName, out var existing))
      {
        return (T)existing.Instance;
      }

      created = factory() ?? throw new InvalidOperationException(
          $"Module factory for '{moduleName}' returned null."
      );
      moduleInstances.Add(moduleName, new ModuleEntry(
          created,
          onDestroy is null ? null : module => onDestroy((T)module)
      ));
    }

    onCreate?.Invoke(created);
    return created;
  }

  public JavaScriptObject DefineModule(JavaScriptObject modules, string moduleName) =>
      WithLiveRegistry(() => DefineModule(runtime, modules, moduleName));

  public JavaScriptObject DefineNativeModule(JavaScriptObject modules, string moduleName) =>
      WithLiveRegistry(() =>
      {
        if (objectFactory is null)
        {
          throw new InvalidOperationException(
              "Native module creation requires a runtime object factory."
          );
        }
        return DefineNativeModule(objectFactory, modules, moduleName);
      });

  public JavaScriptObject GetOrCreateExpoModulesObject() =>
      WithLiveRegistry(() => GetOrCreateExpoModulesObject(runtime));

  public JavaScriptObject GetOrCreateDotnetModulesObject() =>
      WithLiveRegistry(() => GetOrCreateDotnetModulesObject(runtime));

  internal void Dispose()
  {
    List<ModuleEntry> modules;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      modules = moduleInstances.Values.ToList();
      moduleInstances.Clear();
    }

    List<Exception>? exceptions = null;
    foreach (var module in modules)
    {
      try
      {
        module.OnDestroy?.Invoke(module.Instance);
      }
      catch (Exception exception)
      {
        (exceptions ??= []).Add(exception);
      }

      if (module.Instance is not IDisposable disposable)
      {
        continue;
      }

      try
      {
        disposable.Dispose();
      }
      catch (Exception exception)
      {
        (exceptions ??= []).Add(exception);
      }
    }

    if (exceptions is not null)
    {
      throw new AggregateException(exceptions);
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

  public static JavaScriptObject DefineNativeModule(
      JavaScriptObjectFactory objectFactory,
      JavaScriptObject modules,
      string moduleName)
  {
    ArgumentNullException.ThrowIfNull(objectFactory);
    ArgumentNullException.ThrowIfNull(modules);
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

    using var existingModuleValue = modules.GetProperty(moduleName);
    if (existingModuleValue.IsObject)
    {
      return existingModuleValue.AsObject();
    }

    var module = objectFactory.CreateExpoClassInstance("NativeModule");
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

  private sealed record ModuleEntry(object Instance, Action<object>? OnDestroy);
}
