using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal sealed class NativeStateRegistry : IDisposable
{
  private readonly object gate = new();
  private readonly Dictionary<Key, Entry> entries = [];
  private readonly Dictionary<ulong, Type> typeIds = [];
  private ulong nextRegistryId = 1;
  private bool disposed;

  public NativeStateRegistration Register<TState>(TState state)
      where TState : class, IJavaScriptNativeState<TState>
  {
    ArgumentNullException.ThrowIfNull(state);
    var typeId = TState.TypeId.Value;
    if (typeId == 0)
    {
      throw new InvalidOperationException("NativeState type id cannot be zero.");
    }

    lock (gate)
    {
      ThrowIfDisposedLocked();
      RegisterTypeIdLocked<TState>(typeId);
      var token = new ExpoJsiNativeStateToken(typeId, nextRegistryId++, 1);
      entries.Add(new Key(token), new Entry(typeof(TState), state));
      var context = NativeStateReleaseContext.Allocate(this, token);
      return new NativeStateRegistration(token, context);
    }
  }

  public TState Resolve<TState>(ExpoJsiNativeStateToken token)
      where TState : class, IJavaScriptNativeState<TState>
  {
    if (TryResolve<TState>(token, out var state))
    {
      return state!;
    }
    throw new InvalidOperationException(
        $"NativeState entry for {typeof(TState).Name} is missing or stale."
    );
  }

  public bool TryResolve<TState>(ExpoJsiNativeStateToken token, out TState? state)
      where TState : class, IJavaScriptNativeState<TState>
  {
    state = null;
    if (token.TypeId != TState.TypeId.Value)
    {
      return false;
    }

    lock (gate)
    {
      ThrowIfDisposedLocked();
      RegisterTypeIdLocked<TState>(token.TypeId);
      if (!entries.TryGetValue(new Key(token), out var entry) ||
          entry.State is not TState typed)
      {
        return false;
      }

      state = typed;
      return true;
    }
  }

  public void Release(ExpoJsiNativeStateToken token)
  {
    Entry? entry;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }
      if (!entries.Remove(new Key(token), out entry))
      {
        return;
      }
    }

    if (entry.State is IDisposable disposable)
    {
      disposable.Dispose();
    }
  }

  public void Dispose()
  {
    List<Entry> snapshot;
    lock (gate)
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      snapshot = [.. entries.Values];
      entries.Clear();
      typeIds.Clear();
    }

    foreach (var entry in snapshot)
    {
      if (entry.State is IDisposable disposable)
      {
        disposable.Dispose();
      }
    }
  }

  private void RegisterTypeIdLocked<TState>(ulong typeId)
  {
    var type = typeof(TState);
    if (typeIds.TryGetValue(typeId, out var existing))
    {
      if (existing != type)
      {
        throw new InvalidOperationException(
            $"NativeState type id {typeId:x16} is already registered for {existing.Name}."
        );
      }
      return;
    }

    typeIds.Add(typeId, type);
  }

  private void ThrowIfDisposedLocked()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
  }

  private readonly record struct Key(ulong TypeId, ulong RegistryId, uint Generation)
  {
    public Key(ExpoJsiNativeStateToken token)
        : this(token.TypeId, token.RegistryId, token.Generation)
    {
    }
  }

  private sealed record Entry(Type Type, object State);

  internal readonly record struct NativeStateRegistration(
      ExpoJsiNativeStateToken Token,
      nint ReleaseContext);

  private sealed class NativeStateReleaseContext
  {
    private NativeStateReleaseContext(
        NativeStateRegistry registry,
        ExpoJsiNativeStateToken token)
    {
      Registry = registry;
      Token = token;
    }

    public NativeStateRegistry Registry { get; }
    public ExpoJsiNativeStateToken Token { get; }

    public static nint Allocate(NativeStateRegistry registry, ExpoJsiNativeStateToken token)
    {
      return GCHandle.ToIntPtr(GCHandle.Alloc(new NativeStateReleaseContext(registry, token)));
    }
  }

  public static void ReleaseContext(nint context)
  {
    if (context == 0)
    {
      return;
    }

    var handle = GCHandle.FromIntPtr(context);
    handle.Free();
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  public static void ReleaseNativeState(
      nint context,
      ulong typeId,
      ulong registryId,
      uint generation)
  {
    if (context == 0)
    {
      return;
    }

    var handle = GCHandle.FromIntPtr(context);
    try
    {
      if (handle.Target is NativeStateReleaseContext releaseContext)
      {
        var token = new ExpoJsiNativeStateToken(typeId, registryId, generation);
        try
        {
          releaseContext.Registry.Release(token);
        }
        catch
        {
        }
      }
    }
    finally
    {
      handle.Free();
    }
  }
}
