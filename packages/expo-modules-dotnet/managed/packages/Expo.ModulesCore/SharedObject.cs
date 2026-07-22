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
  private int isReleased;

  /// <summary>
  /// Called exactly once when the instance becomes terminally released, whether from explicit
  /// JavaScript <c>release()</c>, JavaScript collection, or runtime context teardown.
  /// </summary>
  protected virtual void OnRelease()
  {
  }

  void ISharedObjectLifetime.ReleaseFromSharedObjectRegistry()
  {
    if (Interlocked.Exchange(ref isReleased, 1) == 0)
    {
      OnRelease();
    }
  }
}
