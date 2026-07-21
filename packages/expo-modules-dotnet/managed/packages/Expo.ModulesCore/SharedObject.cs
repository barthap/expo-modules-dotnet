namespace Expo.ModulesCore;

/// <summary>
/// Base class for authored shared objects that pair a managed instance with a JavaScript object.
/// </summary>
/// <remarks>
/// Registry identity and lifetime state are internal; authored code only observes the
/// <see cref="OnRelease"/> hook. The hook runs exactly once on whichever thread wins terminal
/// release and must not touch JSI wrappers or schedule JavaScript runtime work.
/// </remarks>
public abstract class SharedObject : ISharedObjectLifetime
{
  private enum PairingState
  {
    Unpaired,
    Reserved,
    Paired,
    Terminal,
  }

  private readonly object pairingGate = new();
  private PairingState pairingState;
  private SharedObjectRegistry? pairingRegistry;
  private int isReleased;

  /// <summary>
  /// Called exactly once when the instance becomes terminally released, whether from explicit
  /// JavaScript <c>release()</c>, JavaScript collection, or runtime context teardown.
  /// </summary>
  protected virtual void OnRelease()
  {
  }

  /// <summary>
  /// Claims the instance for pairing before any JSI work happens. Returns <c>true</c> when this
  /// call installed a reservation for <paramref name="registry"/> and <c>false</c> when the
  /// instance is already paired to that same registry. A released instance, an in-flight
  /// reservation, or another owning runtime context fails loudly here instead of creating
  /// duplicate or cross-context pairing state.
  /// </summary>
  internal bool TryReserveForPairing(SharedObjectRegistry registry)
  {
    lock (pairingGate)
    {
      switch (pairingState)
      {
        case PairingState.Unpaired:
          pairingState = PairingState.Reserved;
          pairingRegistry = registry;
          return true;
        case PairingState.Reserved when ReferenceEquals(pairingRegistry, registry):
          throw new InvalidOperationException(
              "The shared object is already being paired in this runtime context."
          );
        case PairingState.Paired when ReferenceEquals(pairingRegistry, registry):
          return false;
        case PairingState.Terminal:
          throw new InvalidOperationException("The shared object has already been released.");
        default:
          throw new InvalidOperationException(
              "The shared object is owned by another runtime context."
          );
      }
    }
  }

  internal void CommitPairing(SharedObjectRegistry registry)
  {
    lock (pairingGate)
    {
      if (pairingState != PairingState.Reserved || !ReferenceEquals(pairingRegistry, registry))
      {
        throw new InvalidOperationException("The shared object reservation is no longer active.");
      }

      pairingState = PairingState.Paired;
    }
  }

  internal void RollBackReservation(SharedObjectRegistry registry)
  {
    lock (pairingGate)
    {
      if (pairingState == PairingState.Reserved && ReferenceEquals(pairingRegistry, registry))
      {
        pairingState = PairingState.Unpaired;
        pairingRegistry = null;
      }
    }
  }

  void ISharedObjectLifetime.ReleaseFromSharedObjectRegistry()
  {
    lock (pairingGate)
    {
      pairingState = PairingState.Terminal;
      pairingRegistry = null;
    }

    if (Interlocked.Exchange(ref isReleased, 1) == 0)
    {
      OnRelease();
    }
  }
}
