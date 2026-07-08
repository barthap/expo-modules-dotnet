using Expo.JSI;

namespace Expo.ModulesCore;

public sealed class ModuleRegistry
{
  private readonly object gate = new();
  private readonly DotnetRuntimeContext? context;
  private readonly JavaScriptRuntime runtime;
  private readonly JavaScriptObjectFactory? objectFactory;
  private readonly Dictionary<string, ModuleEntry> moduleInstances = new(StringComparer.Ordinal);
  private readonly Dictionary<string, LazyModuleDefinition> lazyModules = new(StringComparer.Ordinal);
  private readonly Dictionary<string, JavaScriptObject> lazyModuleObjects = new(StringComparer.Ordinal);
  private JavaScriptObject? lazyModulesHostObject;
  private JavaScriptObject? lazyModulesBackingObject;
  private bool disposed;

  internal ModuleRegistry(JavaScriptRuntime runtime, JavaScriptObjectFactory objectFactory)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.objectFactory = objectFactory ?? throw new ArgumentNullException(nameof(objectFactory));
  }

  internal ModuleRegistry(DotnetRuntimeContext context, JavaScriptObjectFactory objectFactory)
      : this(context.Runtime, objectFactory)
  {
    this.context = context ?? throw new ArgumentNullException(nameof(context));
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

  public void RegisterLazyModule(LazyModuleDefinition definition)
  {
    ArgumentNullException.ThrowIfNull(definition);
    ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
    ArgumentNullException.ThrowIfNull(definition.CreateModule);

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (context is null)
      {
        throw new InvalidOperationException(
            "Lazy module registration requires a runtime context."
        );
      }
      lazyModules[definition.Name] = definition;
    }

    EnsureLazyDotnetModulesObject();
  }

  public JavaScriptObject GetOrCreateExpoModulesObject() =>
      WithLiveRegistry(() => GetOrCreateExpoModulesObject(runtime));

  public JavaScriptObject GetOrCreateDotnetModulesObject() =>
      WithLiveRegistry(() =>
      {
        if (lazyModulesBackingObject is not null)
        {
          using var backingValue = lazyModulesBackingObject.AsValue();
          return backingValue.AsObject();
        }
        return GetOrCreateDotnetModulesObject(runtime);
      });

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
      lazyModules.Clear();
      foreach (var module in lazyModuleObjects.Values)
      {
        module.Dispose();
      }
      lazyModuleObjects.Clear();
      lazyModulesHostObject?.Dispose();
      lazyModulesHostObject = null;
      lazyModulesBackingObject?.Dispose();
      lazyModulesBackingObject = null;
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

  private void EnsureLazyDotnetModulesObject()
  {
    JavaScriptObject? modulesHostObject;
    lock (gate)
    {
      ThrowIfDisposedLocked();
      modulesHostObject = lazyModulesHostObject;
    }

    if (modulesHostObject is not null)
    {
      return;
    }

    using var global = runtime.Global();
    using var expoDotnet = GetOrCreateObject(runtime, global, "_expoDotnet");
    using var existingModulesValue = expoDotnet.GetProperty("modules");
    var backingObject = existingModulesValue.IsObject
        ? existingModulesValue.AsObject()
        : runtime.CreateObject();
    var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
        GetLazyModuleProperty,
        Set: (_, propertyName, _, _) =>
        {
          throw new InvalidOperationException(
              $"Cannot set property '{propertyName}' on _expoDotnet.modules."
          );
        },
        GetPropertyNames: _ => GetLazyModuleNames()
    ));

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (lazyModulesHostObject is not null)
      {
        hostObject.Dispose();
        backingObject.Dispose();
        return;
      }

      lazyModulesHostObject = hostObject;
      lazyModulesBackingObject = backingObject;
      using var modulesValue = hostObject.AsValue();
      expoDotnet.SetProperty("modules", modulesValue);
    }
  }

  private JavaScriptValue GetLazyModuleProperty(
      JavaScriptRuntime callbackRuntime,
      string propertyName,
      object? state)
  {
    if (propertyName == "$$typeof")
    {
      return callbackRuntime.CreateUndefined();
    }

    LazyModuleDefinition definition;
    JavaScriptObject? cached;
    JavaScriptObject backingObject;
    DotnetRuntimeContext ownerContext;
    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!lazyModules.TryGetValue(propertyName, out definition!))
      {
        backingObject = lazyModulesBackingObject ?? throw new InvalidOperationException(
            "Lazy dotnet modules backing object is missing."
        );
        return backingObject.GetProperty(propertyName);
      }
      if (lazyModuleObjects.TryGetValue(propertyName, out cached))
      {
        return cached.AsValue();
      }
      backingObject = lazyModulesBackingObject ?? throw new InvalidOperationException(
          "Lazy dotnet modules backing object is missing."
      );
      ownerContext = context ?? throw new InvalidOperationException(
          "Lazy module registration requires a runtime context."
      );
    }

    var created = definition.CreateModule(ownerContext, backingObject);
    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (lazyModuleObjects.TryGetValue(propertyName, out cached))
      {
        created.Dispose();
        return cached.AsValue();
      }
      lazyModuleObjects.Add(propertyName, created);
      return created.AsValue();
    }
  }

  private IReadOnlyList<string> GetLazyModuleNames()
  {
    JavaScriptObject? backingObject;
    List<string> names;
    lock (gate)
    {
      ThrowIfDisposedLocked();
      names = lazyModules.Keys.ToList();
      backingObject = lazyModulesBackingObject;
    }

    if (backingObject is null)
    {
      return names;
    }

    foreach (var name in backingObject.GetOwnPropertyNames())
    {
      if (!names.Contains(name, StringComparer.Ordinal))
      {
        names.Add(name);
      }
    }
    return names;
  }

  private sealed record ModuleEntry(object Instance, Action<object>? OnDestroy);
}
