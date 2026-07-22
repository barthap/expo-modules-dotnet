using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore;

/// <summary>
/// Generated shared-object event glue. Authored code uses generated <see cref="EventAttribute"/>
/// members instead of calling this API directly.
/// </summary>
public static class GeneratedSharedObjectEvents
{
  public static void InstallPrototype(DotnetRuntimeContext context, JavaScriptObject prototype)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(prototype);
    context.SharedObjectEvents.InstallPrototype(prototype);
  }

  public static void Attach(DotnetRuntimeContext context, SharedObject instance, IReadOnlyList<string> eventNames)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(instance);
    ArgumentNullException.ThrowIfNull(eventNames);
    context.SharedObjectEvents.Attach(instance, eventNames);
  }

  public static async Task EmitAsync(DotnetRuntimeContext context, SharedObject instance, string eventName)
  {
    await context.SharedObjectEvents.EmitAsync(instance, eventName).ConfigureAwait(false);
  }

  public static async Task EmitAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      JavaScriptValue payload)
  {
    await context.SharedObjectEvents.EmitAsync(instance, eventName, payload).ConfigureAwait(false);
  }

  public static async Task EmitAsync(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      ArrayBuffer payload)
  {
    await context.SharedObjectEvents.EmitAsync(instance, eventName, payload).ConfigureAwait(false);
  }

  public static async Task EmitAsync<TCodec, T>(
      DotnetRuntimeContext context,
      SharedObject instance,
      string eventName,
      T payload)
      where TCodec : struct, IJavaScriptCodec<T>
  {
    await context.SharedObjectEvents.EmitAsync<TCodec, T>(instance, eventName, payload).ConfigureAwait(false);
  }
}

internal sealed class SharedObjectEventEmitter : IDisposable, ISharedObjectEventBinding
{
  private readonly object gate = new();
  private readonly DotnetRuntimeContext context;
  private readonly SharedObjectRegistry registry;
  private readonly EventEmitterRuntimeState listeners = new();
  private readonly Dictionary<SharedObject, SharedEventTarget> targets = new(ReferenceEqualityComparer.Instance);
  private bool disposed;

  internal SharedObjectEventEmitter(DotnetRuntimeContext context, SharedObjectRegistry registry)
  {
    this.context = context ?? throw new ArgumentNullException(nameof(context));
    this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
  }

  internal void InstallPrototype(JavaScriptObject prototype)
  {
    ThrowIfDisposed();
    EventEmitterPrototype.Install(
        context.Runtime,
        prototype,
        listeners,
        static function => function,
        retainEmitters: false,
        enableObservingHooks: false,
        onEmitterAttached: AttachEmitter
    );
  }

  internal void Attach(SharedObject instance, IReadOnlyList<string> eventNames)
  {
    ArgumentNullException.ThrowIfNull(instance);
    ArgumentNullException.ThrowIfNull(eventNames);
    var names = new HashSet<string>(eventNames, StringComparer.Ordinal);
    if (names.Count == 0 || names.Any(string.IsNullOrWhiteSpace))
    {
      throw new ArgumentException("Shared-object event declarations cannot be empty.", nameof(eventNames));
    }

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (targets.TryGetValue(instance, out var existing))
      {
        if (!existing.EventNames.SetEquals(names))
        {
          throw new InvalidOperationException("Shared-object event declarations cannot be rebound.");
        }
        return;
      }
      targets.Add(instance, new SharedEventTarget(names));
    }

    try
    {
      instance.AttachEventBinding(this);
    }
    catch
    {
      lock (gate)
      {
        targets.Remove(instance);
      }
      throw;
    }
  }

  internal Task EmitAsync(SharedObject instance, string eventName) =>
      ExecuteEventAsync(instance, eventName, runtime => Emit(runtime, instance, eventName));

  internal Task EmitAsync(SharedObject instance, string eventName, JavaScriptValue payload) =>
      ExecuteEventAsync(
          instance,
          eventName,
          runtime =>
          {
            using var invocationPayload = payload.Ref.Retain();
            return Emit(runtime, instance, eventName, invocationPayload);
          }
      );

  internal async Task EmitAsync(SharedObject instance, string eventName, ArrayBuffer payload)
  {
    using var invocationPayload = payload.Retain();
    await ExecuteEventAsync(
        instance,
        eventName,
        runtime =>
        {
          using var eventValue = invocationPayload.Encode(runtime);
          return Emit(runtime, instance, eventName, eventValue);
        }
    ).ConfigureAwait(false);
  }

  internal Task EmitAsync<TCodec, T>(SharedObject instance, string eventName, T payload)
      where TCodec : struct, IJavaScriptCodec<T> =>
      ExecuteEventAsync(
          instance,
          eventName,
          runtime =>
          {
            using var payloadValue = TCodec.Encode(payload, runtime);
            return Emit(runtime, instance, eventName, payloadValue);
          }
      );

  void ISharedObjectEventBinding.Release(SharedObject instance)
  {
    int? emitterId = null;
    lock (gate)
    {
      if (targets.Remove(instance, out var target))
      {
        emitterId = target.EmitterId;
      }
    }

    if (emitterId is not null && !disposed)
    {
      listeners.RemoveEmitter(emitterId.Value);
    }
  }

  public void Dispose()
  {
    lock (gate)
    {
      if (disposed)
      {
        return;
      }
      disposed = true;
      targets.Clear();
    }
    listeners.Dispose();
  }

  private Task ExecuteEventAsync(
      SharedObject instance,
      string eventName,
      Func<JavaScriptRuntime, bool> emit)
  {
    ArgumentNullException.ThrowIfNull(instance);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    EnsureAttached(instance, eventName);

    var runtime = context.Runtime;
    if (runtime.HasExclusiveRuntimeAccess)
    {
      emit(runtime);
      return Task.CompletedTask;
    }
    if (runtime.CanExecuteSync)
    {
      runtime.Execute(emit);
      return Task.CompletedTask;
    }
    return runtime.ExecuteAsync(emit);
  }

  private bool Emit(JavaScriptRuntime runtime, SharedObject instance, string eventName)
  {
    using var target = registry.GetLiveJavaScriptObject(instance);
    using var eventNameValue = runtime.CreateString(eventName);
    using var emitValue = target.GetProperty("emit");
    using var emit = emitValue.AsFunction();
    using var result = emit.CallWithThis(target, eventNameValue);
    return true;
  }

  private bool Emit(JavaScriptRuntime runtime, SharedObject instance, string eventName, JavaScriptValue payload)
  {
    using var target = registry.GetLiveJavaScriptObject(instance);
    using var eventNameValue = runtime.CreateString(eventName);
    using var emitValue = target.GetProperty("emit");
    using var emit = emitValue.AsFunction();
    using var result = emit.CallWithThis(target, eventNameValue, payload);
    return true;
  }

  private void AttachEmitter(JavaScriptObject emitter, int emitterId)
  {
    var instance = registry.ResolveManaged(emitter) as SharedObject
        ?? throw new InvalidOperationException("The event target is not a shared object.");
    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!targets.TryGetValue(instance, out var target))
      {
        throw new InvalidOperationException("The shared object event target has been released.");
      }
      target.EmitterId = emitterId;
    }
  }

  private void EnsureAttached(SharedObject instance, string eventName)
  {
    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!targets.TryGetValue(instance, out var target))
      {
        throw new InvalidOperationException("The shared object is not attached to a JavaScript event target.");
      }
      if (!target.EventNames.Contains(eventName))
      {
        throw new InvalidOperationException($"The shared object does not declare event '{eventName}'.");
      }
    }
  }

  private void ThrowIfDisposed()
  {
    lock (gate)
    {
      ThrowIfDisposedLocked();
    }
  }

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }

  private sealed class SharedEventTarget(HashSet<string> eventNames)
  {
    internal HashSet<string> EventNames { get; } = eventNames;

    internal int? EmitterId { get; set; }
  }
}
