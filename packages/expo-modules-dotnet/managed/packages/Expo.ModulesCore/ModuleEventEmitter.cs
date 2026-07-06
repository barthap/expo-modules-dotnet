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
        runtime =>
        {
          using var target = GetTarget(module, eventName);
          using var eventNameValue = runtime.CreateString(eventName);
          using var emitValue = target.Target.GetProperty("emit");
          using var emit = emitValue.AsFunction();
          using var result = emit.CallWithThis(target.Target, eventNameValue);
          return true;
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
          using var target = GetTarget(module, eventName);
          using var eventNameValue = runtime.CreateString(eventName);
          using var payloadValue = TCodec.Encode(payload, runtime);
          using var emitValue = target.Target.GetProperty("emit");
          using var emit = emitValue.AsFunction();
          using var result = emit.CallWithThis(target.Target, eventNameValue, payloadValue);
          return true;
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
    if (context.Runtime.HasExclusiveRuntimeAccess)
    {
      // Event emission from an async module may resume later and need scheduling. Event emission
      // from a synchronous generated function is different: the current call already owns runtime
      // access, so dispatch the inherited JS `emit` method directly instead of re-entering the host
      // sync scheduler.
      cancellationToken.ThrowIfCancellationRequested();
      emit(context.Runtime);
      return Task.CompletedTask;
    }

    if (context.Runtime.CanExecuteSync)
    {
      cancellationToken.ThrowIfCancellationRequested();
      context.Runtime.Execute(emit);
      return Task.CompletedTask;
    }

    return context.Runtime.ExecuteAsync(emit, cancellationToken: cancellationToken);
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
