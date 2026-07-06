using Expo.JSI;

namespace Expo.ModulesCore;

internal sealed class EventEmitterRuntimeState : IDisposable
{
  private const string EmitterIdKey = "__expo_dotnet_emitter_id__";

  private readonly Dictionary<int, EmitterState> emitters = [];
  private int nextEmitterId = 1;
  private int nextListenerId = 1;
  private bool disposed;

  public int GetOrCreateEmitterId(JavaScriptRuntime runtime, JavaScriptObject emitter)
  {
    ThrowIfDisposed();
    var existing = GetEmitterId(emitter);
    if (existing is not null)
    {
      return existing.Value;
    }

    var id = nextEmitterId++;
    using var value = runtime.CreateNumber(id);
    emitter.SetProperty(EmitterIdKey, value);
    emitters.Add(id, new EmitterState(emitter));
    return id;
  }

  public int? GetEmitterId(JavaScriptObject emitter)
  {
    ThrowIfDisposed();
    using var value = emitter.GetProperty(EmitterIdKey);
    return value.IsDouble ? checked((int)value.AsDouble()) : null;
  }

  public JavaScriptObject GetEmitter(int emitterId)
  {
    ThrowIfDisposed();
    using var value = emitters[emitterId].Emitter.AsValue();
    return value.AsObject();
  }

  public int AddListener(int emitterId, string eventName, JavaScriptFunction listener)
  {
    ThrowIfDisposed();
    var emitter = emitters[emitterId];
    if (!emitter.Listeners.TryGetValue(eventName, out var listeners))
    {
      listeners = [];
      emitter.Listeners.Add(eventName, listeners);
    }

    var id = nextListenerId++;
    listeners.Add(new ListenerEntry(id, RetainFunction(listener)));
    return id;
  }

  public void RemoveListener(int emitterId, string eventName, int listenerId)
  {
    ThrowIfDisposed();
    if (!emitters.TryGetValue(emitterId, out var emitter) ||
        !emitter.Listeners.TryGetValue(eventName, out var listeners))
    {
      return;
    }

    var index = listeners.FindIndex(listener => listener.Id == listenerId);
    if (index < 0)
    {
      return;
    }

    listeners[index].Dispose();
    listeners.RemoveAt(index);
  }

  public void RemoveListeners(
      JavaScriptRuntime runtime,
      int emitterId,
      string eventName,
      JavaScriptValue listener)
  {
    ThrowIfDisposed();
    if (!emitters.TryGetValue(emitterId, out var emitter) ||
        !emitter.Listeners.TryGetValue(eventName, out var listeners))
    {
      return;
    }

    for (var index = listeners.Count - 1; index >= 0; index--)
    {
      using var storedValue = listeners[index].Function.AsValue();
      if (!runtime.StrictEquals(storedValue, listener))
      {
        continue;
      }

      listeners[index].Dispose();
      listeners.RemoveAt(index);
    }
  }

  public int RemoveAll(int emitterId, string eventName)
  {
    ThrowIfDisposed();
    if (!emitters.TryGetValue(emitterId, out var emitter) ||
        !emitter.Listeners.Remove(eventName, out var listeners))
    {
      return 0;
    }

    foreach (var listener in listeners)
    {
      listener.Dispose();
    }
    return listeners.Count;
  }

  public int ListenerCount(int emitterId, string eventName)
  {
    ThrowIfDisposed();
    return emitters.TryGetValue(emitterId, out var emitter) &&
        emitter.Listeners.TryGetValue(eventName, out var listeners)
            ? listeners.Count
            : 0;
  }

  public IReadOnlyList<JavaScriptFunction> GetListeners(int emitterId, string eventName)
  {
    ThrowIfDisposed();
    return emitters.TryGetValue(emitterId, out var emitter) &&
        emitter.Listeners.TryGetValue(eventName, out var listeners)
            ? listeners.Select(listener => RetainFunction(listener.Function)).ToArray()
            : [];
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    foreach (var emitter in emitters.Values)
    {
      emitter.Dispose();
    }
    emitters.Clear();
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }

  private static JavaScriptFunction RetainFunction(JavaScriptFunction function)
  {
    using var value = function.AsValue();
    return value.AsFunction();
  }

  private sealed class EmitterState(JavaScriptObject emitter) : IDisposable
  {
    public JavaScriptObject Emitter { get; } = RetainObject(emitter);

    public Dictionary<string, List<ListenerEntry>> Listeners { get; } = new(StringComparer.Ordinal);

    public void Dispose()
    {
      foreach (var listener in Listeners.Values.SelectMany(listeners => listeners))
      {
        listener.Dispose();
      }
      Listeners.Clear();
      Emitter.Dispose();
    }
  }

  private sealed class ListenerEntry(int id, JavaScriptFunction function) : IDisposable
  {
    public int Id { get; } = id;

    public JavaScriptFunction Function { get; } = function;

    public void Dispose() => Function.Dispose();
  }

  private static JavaScriptObject RetainObject(JavaScriptObject obj)
  {
    using var value = obj.AsValue();
    return value.AsObject();
  }
}
