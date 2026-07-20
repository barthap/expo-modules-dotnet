using System.Runtime.CompilerServices;
using Expo.JSI;

namespace Expo.ModulesCore;

internal interface ISharedObjectLifetime
{
  void ReleaseFromSharedObjectRegistry();
}

internal sealed class SharedObjectEntry(
    long id,
    ISharedObjectLifetime instance,
    JavaScriptWeakObject weakObject,
    SharedObjectNativeState nativeState)
{
  internal long Id { get; } = id;

  internal ISharedObjectLifetime Instance { get; } = instance;

  internal JavaScriptWeakObject WeakObject { get; } = weakObject;

  internal SharedObjectNativeState NativeState { get; } = nativeState;

  internal bool IsReleased { get; set; }
}

internal sealed class SharedObjectRegistry : IDisposable
{
  private readonly object gate = new();
  private readonly Dictionary<long, SharedObjectEntry> entriesById = [];
  private readonly Dictionary<ISharedObjectLifetime, SharedObjectEntry> entriesByInstance =
      new(ReferenceEqualityComparer.Instance);
  private readonly ConditionalWeakTable<ISharedObjectLifetime, ReleasedInstanceMarker> releasedInstances =
      new();
  private readonly List<SharedObjectEntry> deferredNativeStateEntries = [];
  private readonly JavaScriptRuntime runtime;
  private readonly Action? installFailureForTesting;
  private long nextEntryId = 1;
  private bool disposed;

  internal SharedObjectRegistry(
      JavaScriptRuntime runtime,
      Action? installFailureForTesting = null)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    this.installFailureForTesting = installFailureForTesting;
  }

  internal int Count
  {
    get
    {
      lock (gate)
      {
        return entriesById.Count;
      }
    }
  }

  internal JavaScriptObject GetOrCreateJavaScriptObject(ISharedObjectLifetime instance)
  {
    ArgumentNullException.ThrowIfNull(instance);
    SharedObjectEntry? deadEntry = null;
    JavaScriptObject? result = null;

    try
    {
      lock (gate)
      {
        ThrowIfDisposedLocked();
        if (releasedInstances.TryGetValue(instance, out _))
        {
          throw new InvalidOperationException("The shared object has already been released.");
        }
        if (!entriesByInstance.TryGetValue(instance, out var existing))
        {
          result = CreateEntryLocked(instance);
        }
        else
        {
          result = existing.WeakObject.Lock();
          if (result is null)
          {
            deadEntry = TakeTerminalEntryLocked(existing.Id);
          }
        }
      }
    }
    finally
    {
      DrainDeferredNativeStateEntries();
    }

    if (result is not null)
    {
      return result;
    }

    CompleteTerminalEntry(deadEntry!);
    throw new InvalidOperationException("The shared JavaScript object is no longer available.");
  }

  internal ISharedObjectLifetime ResolveManaged(JavaScriptObject value)
  {
    ArgumentNullException.ThrowIfNull(value);
    var state = value.GetNativeState<SharedObjectNativeState>();

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!state.Registry.TryGetTarget(out var owner) ||
          !ReferenceEquals(owner, this) ||
          !entriesById.TryGetValue(state.EntryId, out var entry) ||
          entry.IsReleased)
      {
        throw new InvalidOperationException("The JavaScript object is not an active shared object.");
      }

      return entry.Instance;
    }
  }

  internal void ReleaseFromJavaScript(JavaScriptObject value)
  {
    ArgumentNullException.ThrowIfNull(value);
    var state = value.GetNativeState<SharedObjectNativeState>();
    SharedObjectEntry? entry;

    lock (gate)
    {
      ThrowIfDisposedLocked();
      if (!state.Registry.TryGetTarget(out var owner) || !ReferenceEquals(owner, this))
      {
        throw new InvalidOperationException("The JavaScript object belongs to another registry.");
      }

      entry = TakeTerminalEntryLocked(state.EntryId);
    }

    if (entry is not null)
    {
      CompleteTerminalEntry(entry);
    }
  }

  internal void Release(long entryId)
  {
    var reentrant = Monitor.IsEntered(gate);
    SharedObjectEntry? entry;
    lock (gate)
    {
      entry = TakeTerminalEntryLocked(entryId);
      if (entry is not null && reentrant)
      {
        deferredNativeStateEntries.Add(entry);
        return;
      }
    }

    if (entry is not null)
    {
      CompleteTerminalEntry(entry);
    }
  }

  public void Dispose()
  {
    List<SharedObjectEntry> terminalEntries;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      terminalEntries = entriesById.Values.Where(entry => !entry.IsReleased).ToList();
      foreach (var entry in terminalEntries)
      {
        entry.IsReleased = true;
      }
      entriesById.Clear();
      entriesByInstance.Clear();
      terminalEntries.AddRange(deferredNativeStateEntries);
      deferredNativeStateEntries.Clear();
    }

    List<Exception>? failures = null;
    foreach (var entry in terminalEntries)
    {
      TryCleanup(entry.WeakObject.Dispose, ref failures);
      TryCleanup(entry.Instance.ReleaseFromSharedObjectRegistry, ref failures);
    }

    if (failures is not null)
    {
      throw new AggregateException(failures);
    }
  }

  private SharedObjectEntry? TakeTerminalEntryLocked(long entryId)
  {
    if (disposed || !entriesById.Remove(entryId, out var entry) || entry.IsReleased)
    {
      return null;
    }

    entry.IsReleased = true;
    entriesByInstance.Remove(entry.Instance);
    releasedInstances.Add(entry.Instance, ReleasedInstanceMarker.Instance);
    return entry;
  }

  private JavaScriptObject CreateEntryLocked(ISharedObjectLifetime instance)
  {
    var id = nextEntryId++;
    JavaScriptObject? value = null;
    JavaScriptWeakObject? weak = null;
    SharedObjectEntry? entry = null;
    var attached = false;

    try
    {
      value = SharedObjectPrototype.CreateInstance(runtime, this, installFailureForTesting);
      var nativeState = new SharedObjectNativeState(this, id);
      value.SetNativeState(nativeState);
      attached = true;
      weak = value.CreateWeak();
      entry = new SharedObjectEntry(id, instance, weak, nativeState);
      entriesById.Add(id, entry);
      entriesByInstance.Add(instance, entry);
      return value;
    }
    catch (Exception creationFailure)
    {
      List<Exception>? failures = [creationFailure];
      if (entry is not null)
      {
        entriesById.Remove(id);
        entriesByInstance.Remove(instance);
      }
      if (attached)
      {
        TryCleanup(value!.ClearNativeState<SharedObjectNativeState>, ref failures);
      }
      if (weak is not null)
      {
        TryCleanup(weak.Dispose, ref failures);
      }
      if (value is not null)
      {
        TryCleanup(value.Dispose, ref failures);
      }
      throw new AggregateException(failures!);
    }
  }

  private static void CompleteTerminalEntry(SharedObjectEntry entry)
  {
    List<Exception>? failures = null;
    TryCleanup(entry.WeakObject.Dispose, ref failures);
    TryCleanup(entry.Instance.ReleaseFromSharedObjectRegistry, ref failures);
    if (failures is not null)
    {
      throw new AggregateException(failures);
    }
  }

  private void DrainDeferredNativeStateEntries()
  {
    while (true)
    {
      List<SharedObjectEntry> entries;
      lock (gate)
      {
        if (deferredNativeStateEntries.Count == 0)
        {
          return;
        }

        entries = [.. deferredNativeStateEntries];
        deferredNativeStateEntries.Clear();
      }

      foreach (var entry in entries)
      {
        try
        {
          CompleteTerminalEntry(entry);
        }
        catch
        {
          // NativeStateRegistry invokes this path from an unmanaged callback and swallows errors.
        }
      }
    }
  }

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }

  private static void TryCleanup(Action action, ref List<Exception>? failures)
  {
    try
    {
      action();
    }
    catch (Exception exception)
    {
      (failures ??= []).Add(exception);
    }
  }

  private sealed class ReleasedInstanceMarker
  {
    internal static ReleasedInstanceMarker Instance { get; } = new();
  }
}

internal sealed class SharedObjectNativeState : IJavaScriptNativeState<SharedObjectNativeState>, IDisposable
{
  public static JavaScriptNativeStateTypeId TypeId { get; } =
      JavaScriptNativeStateTypeId.FromName(nameof(SharedObjectNativeState));

  internal WeakReference<SharedObjectRegistry> Registry { get; }

  internal long EntryId { get; }

  internal SharedObjectNativeState(SharedObjectRegistry registry, long entryId)
  {
    Registry = new WeakReference<SharedObjectRegistry>(registry);
    EntryId = entryId;
  }

  public void Dispose()
  {
    if (Registry.TryGetTarget(out var registry))
    {
      registry.Release(EntryId);
    }
  }
}
