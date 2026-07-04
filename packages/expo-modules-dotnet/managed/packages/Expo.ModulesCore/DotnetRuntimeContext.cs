using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>
/// Owns managed module state for one JavaScript runtime.
///
/// This type is the C# bridge's narrow equivalent of the ownership role that
/// Expo's native <c>AppContext</c> plays upstream. Expo's <c>AppContext</c>
/// ties together a module registry, runtime access, lifecycle hooks, event
/// dispatch, scheduler integration, shared objects, and other app services for
/// a React runtime. <c>DotnetRuntimeContext</c> intentionally starts smaller: it owns
/// the generated module instances and generated host-function registrations
/// that are scoped to a single runtime.
///
/// The context exists so React Native host adapters can tear managed state down
/// deterministically when a runtime reloads or is destroyed. Without a
/// runtime-scoped owner, registration would install JSI host functions and then
/// rely on native host-function finalizers or ordinary GC timing to release
/// managed callback pins and module instances. A host adapter can instead call
/// <see cref="Dispose" /> for the context that belongs to the invalidated
/// runtime, releasing managed state promptly and making future use fail loudly.
///
/// Future module-facing APIs may expose a fuller <c>AppContext</c>-style object
/// for authored modules. This type should remain the low-level ownership
/// primitive for the generated binding layer unless that broader context takes
/// over the same lifetime responsibilities.
/// </summary>
public sealed class DotnetRuntimeContext : IDisposable
{
  private readonly object gate = new();
  private readonly List<GeneratedHostFunctionRegistration> hostFunctionRegistrations = [];
  private readonly List<IDisposable> retainedCallbacks = [];
  private readonly Dictionary<string, object> moduleInstances = new(StringComparer.Ordinal);
  private bool disposed;

  public DotnetRuntimeContext(JavaScriptRuntime runtime)
  {
    Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
  }

  public JavaScriptRuntime Runtime { get; }

  public JavaScriptObject GetOrCreateExpoModulesObject()
  {
    ThrowIfDisposed();
    return ModuleRegistry.GetOrCreateExpoModulesObject(Runtime);
  }

  public JavaScriptObject GetOrCreateDotnetModulesObject()
  {
    ThrowIfDisposed();
    return ModuleRegistry.GetOrCreateDotnetModulesObject(Runtime);
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

  internal GeneratedHostFunctionRegistration RegisterHostFunction(
      JavaScriptHostFunction callback,
      object callbackState
  )
  {
    var registration = new GeneratedHostFunctionRegistration(callback, callbackState);

    lock (gate)
    {
      try
      {
        ThrowIfDisposedLocked();
        hostFunctionRegistrations.Add(registration);
      }
      catch
      {
        registration.Dispose();
        throw;
      }
    }

    return registration;
  }

  internal T RegisterRetainedCallback<T>(T callback)
      where T : IDisposable
  {
    ArgumentNullException.ThrowIfNull(callback);
    lock (gate)
    {
      try
      {
        ThrowIfDisposedLocked();
        retainedCallbacks.Add(callback);
      }
      catch
      {
        callback.Dispose();
        throw;
      }
    }

    return callback;
  }

  public void Dispose()
  {
    List<GeneratedHostFunctionRegistration> registrations;
    List<IDisposable> callbacks;
    List<IDisposable> disposableModules;

    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      registrations = [.. hostFunctionRegistrations];
      hostFunctionRegistrations.Clear();
      callbacks = [.. retainedCallbacks];
      retainedCallbacks.Clear();
      disposableModules = moduleInstances.Values.OfType<IDisposable>().ToList();
      moduleInstances.Clear();
    }

    foreach (var registration in registrations)
    {
      registration.Dispose();
    }

    foreach (var callback in callbacks)
    {
      callback.Dispose();
    }

    foreach (var module in disposableModules)
    {
      module.Dispose();
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), typeof(DotnetRuntimeContext));
  }

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, typeof(DotnetRuntimeContext));
  }
}
