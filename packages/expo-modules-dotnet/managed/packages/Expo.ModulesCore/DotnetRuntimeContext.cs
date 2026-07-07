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
  private readonly ModuleRegistry moduleRegistry;
  private readonly JavaScriptObjectFactory objects;
  private readonly ModuleEventEmitter events;
  private bool disposed;

  public DotnetRuntimeContext(JavaScriptRuntime runtime)
  {
    Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    objects = new JavaScriptObjectFactory(Runtime);
    events = new ModuleEventEmitter(this);
    moduleRegistry = new ModuleRegistry(Runtime, objects);
  }

  /// <summary>
  /// Gets the low-level JavaScript runtime wrapper for advanced module code.
  /// </summary>
  /// <remarks>
  /// This accessor does not marshal work onto the JavaScript runtime thread and does not make JSI
  /// access thread-safe. Callers must already be running during valid runtime access or schedule work
  /// through the runtime scheduling APIs when needed. Values, objects, functions, and other owned
  /// wrappers created from this runtime must be disposed according to their ownership contracts and
  /// must not be used after this context is disposed or the host tears the runtime down.
  /// </remarks>
  public JavaScriptRuntime Runtime { get; }

  public JavaScriptObjectFactory Objects
  {
    get
    {
      ThrowIfDisposed();
      return objects;
    }
  }

  public ModuleRegistry ModuleRegistry
  {
    get
    {
      ThrowIfDisposed();
      return moduleRegistry;
    }
  }

  public ModuleEventEmitter Events
  {
    get
    {
      ThrowIfDisposed();
      return events;
    }
  }

  internal GeneratedHostFunctionRegistration RegisterHostFunction(
      JavaScriptHostFunction callback,
      object callbackState
  )
  {
    var registration = new GeneratedHostFunctionRegistration(this, callback, callbackState);

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
    }

    List<Exception>? exceptions = null;

    foreach (var registration in registrations)
    {
      DisposeAndCapture(registration, ref exceptions);
    }
    foreach (var callback in callbacks)
    {
      if (callback is IRuntimeContextRetainedCallback retainedCallback)
      {
        DisposeAndCapture(retainedCallback.DisposeFromRuntimeContext, ref exceptions);
      }
      else
      {
        DisposeAndCapture(callback, ref exceptions);
      }
    }

    DisposeAndCapture(moduleRegistry.Dispose, ref exceptions);
    DisposeAndCapture(events, ref exceptions);
    DisposeAndCapture(objects, ref exceptions);

    if (exceptions is not null)
    {
      throw new AggregateException(exceptions);
    }
  }

  private static void DisposeAndCapture(IDisposable disposable, ref List<Exception>? exceptions) =>
      DisposeAndCapture(disposable.Dispose, ref exceptions);

  private static void DisposeAndCapture(Action dispose, ref List<Exception>? exceptions)
  {
    try
    {
      dispose();
    }
    catch (AggregateException exception)
    {
      (exceptions ??= []).AddRange(exception.InnerExceptions);
    }
    catch (Exception exception)
    {
      (exceptions ??= []).Add(exception);
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
