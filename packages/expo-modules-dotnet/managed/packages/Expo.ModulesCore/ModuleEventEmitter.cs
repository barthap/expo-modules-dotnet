using Expo.JSI;
using Expo.ModulesCore.Codecs;

namespace Expo.ModulesCore;

public sealed class ModuleEventEmitter : IDisposable
{
  private readonly object gate = new();
  private readonly DotnetRuntimeContext context;
  private readonly Dictionary<object, EventTarget> targets = new(ReferenceEqualityComparer.Instance);
  private bool disposed;

  internal ModuleEventEmitter(DotnetRuntimeContext context)
  {
    this.context = context ?? throw new ArgumentNullException(nameof(context));
  }

  public void Attach(
      object module,
      JavaScriptObject target,
      string moduleName,
      IReadOnlyList<string> eventNames)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(target);
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
    ArgumentNullException.ThrowIfNull(eventNames);
    if (eventNames.Count == 0)
    {
      throw new ArgumentException("Module event declarations cannot be empty.", nameof(eventNames));
    }

    var declaredEventNames = new HashSet<string>(StringComparer.Ordinal);
    foreach (var eventName in eventNames)
    {
      if (string.IsNullOrWhiteSpace(eventName))
      {
        throw new ArgumentException("Module event declarations cannot contain empty names.", nameof(eventNames));
      }
      if (!declaredEventNames.Add(eventName))
      {
        throw new ArgumentException(
            $"Module event declaration '{eventName}' is duplicated.",
            nameof(eventNames)
        );
      }
    }

    using var targetValue = target.AsValue();
    JavaScriptObject? retainedTarget = targetValue.AsObject();
    try
    {
      lock (gate)
      {
        ThrowIfDisposedLocked();
        if (targets.Remove(module, out var existing))
        {
          existing.Dispose();
        }
        targets.Add(module, new EventTarget(moduleName, retainedTarget, declaredEventNames));
        retainedTarget = null;
      }
    }
    finally
    {
      retainedTarget?.Dispose();
    }
  }

  public async Task EmitAsync(
      object module,
      string eventName,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

    await ExecuteEventAsync(
        runtime => Emit(runtime, module, eventName),
        cancellationToken
    ).ConfigureAwait(false);
  }

  /// <summary>
  /// Emits a declared module event with an existing JavaScript value.
  /// </summary>
  /// <remarks>
  /// The caller retains ownership of <paramref name="payload" /> and must keep it alive until the
  /// returned task completes. The emitter retains and releases a separate invocation copy while
  /// running on the payload's runtime.
  /// </remarks>
  public async Task EmitAsync(
      object module,
      string eventName,
      JavaScriptValue payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    ArgumentNullException.ThrowIfNull(payload);

    await ExecuteEventAsync(
        runtime =>
        {
          using var invocationPayload = payload.Ref.Retain();
          return Emit(runtime, module, eventName, invocationPayload);
        },
        cancellationToken
    ).ConfigureAwait(false);
  }

  /// <summary>
  /// Emits a declared module event with an existing binary buffer.
  /// </summary>
  /// <remarks>
  /// The emitter retains an invocation lease before scheduling runtime work, so the caller may
  /// dispose <paramref name="payload" /> after this method returns. The lease is released when
  /// the returned task completes.
  /// </remarks>
  public async Task EmitAsync(
      object module,
      string eventName,
      ArrayBuffer payload,
      CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    ArgumentNullException.ThrowIfNull(payload);

    using var invocationPayload = payload.Retain();
    await ExecuteEventAsync(
        runtime =>
        {
          using var eventValue = invocationPayload.Encode(runtime);
          return Emit(runtime, module, eventName, eventValue);
        },
        cancellationToken
    ).ConfigureAwait(false);
  }

  public async Task EmitAsync<TCodec, T>(
      object module,
      string eventName,
      T payload,
      CancellationToken cancellationToken = default)
      where TCodec : struct, IJavaScriptCodec<T>
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

    await ExecuteEventAsync(
        runtime =>
        {
          using var payloadValue = TCodec.Encode(payload, runtime);
          return Emit(runtime, module, eventName, payloadValue);
        },
        cancellationToken
    ).ConfigureAwait(false);
  }

  public void Dispose()
  {
    List<EventTarget> retainedTargets;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      retainedTargets = [.. targets.Values];
      targets.Clear();
    }

    foreach (var target in retainedTargets)
    {
      target.Dispose();
    }
  }

  private EventTargetSnapshot GetTarget(object module, string eventName)
  {
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!targets.TryGetValue(module, out var target))
      {
        throw new InvalidOperationException("Module is not attached to a JavaScript event target.");
      }
      if (!target.EventNames.Contains(eventName))
      {
        throw new InvalidOperationException(
            $"Module '{target.ModuleName}' does not declare event '{eventName}'."
        );
      }
      using var targetValue = target.Target.AsValue();
      return new EventTargetSnapshot(targetValue.AsObject());
    }
  }

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, typeof(ModuleEventEmitter));
  }

  private Task ExecuteEventAsync(
      Func<JavaScriptRuntime, bool> emit,
      CancellationToken cancellationToken)
  {
    lock (gate)
    {
      ThrowIfDisposedLocked();
    }

    var runtime = context.Runtime;
    if (runtime.HasExclusiveRuntimeAccess)
    {
      // Event emission from an async module may resume later and need scheduling. Event emission
      // from a synchronous generated function is different: the current call already owns runtime
      // access, so dispatch the inherited JS `emit` method directly instead of re-entering the host
      // sync scheduler.
      cancellationToken.ThrowIfCancellationRequested();
      emit(runtime);
      return Task.CompletedTask;
    }

    if (runtime.CanExecuteSync)
    {
      cancellationToken.ThrowIfCancellationRequested();
      runtime.Execute(emit);
      return Task.CompletedTask;
    }

    return runtime.ExecuteAsync(emit, cancellationToken: cancellationToken);
  }

  private bool Emit(JavaScriptRuntime runtime, object module, string eventName)
  {
    using var target = GetTarget(module, eventName);
    using var eventNameValue = runtime.CreateString(eventName);
    using var emitValue = target.Target.GetProperty("emit");
    using var emit = emitValue.AsFunction();
    using var result = emit.CallWithThis(target.Target, eventNameValue);
    return true;
  }

  private bool Emit(
      JavaScriptRuntime runtime,
      object module,
      string eventName,
      JavaScriptValue payload)
  {
    using var target = GetTarget(module, eventName);
    using var eventNameValue = runtime.CreateString(eventName);
    using var emitValue = target.Target.GetProperty("emit");
    using var emit = emitValue.AsFunction();
    using var result = emit.CallWithThis(target.Target, eventNameValue, payload);
    return true;
  }

  private sealed class EventTarget : IDisposable
  {
    public EventTarget(string moduleName, JavaScriptObject target, HashSet<string> eventNames)
    {
      ModuleName = moduleName;
      Target = target;
      EventNames = eventNames;
    }

    public string ModuleName { get; }

    public JavaScriptObject Target { get; }

    public HashSet<string> EventNames { get; }

    public void Dispose()
    {
      Target.Dispose();
    }
  }

  private sealed class EventTargetSnapshot(JavaScriptObject target) : IDisposable
  {
    public JavaScriptObject Target { get; } = target;

    public void Dispose()
    {
      Target.Dispose();
    }
  }
}
