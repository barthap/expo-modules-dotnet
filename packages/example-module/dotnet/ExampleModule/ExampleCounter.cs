using Expo.ModulesCore;

namespace ExampleModule;

/// <summary>
/// Example shared-object handle: a counter whose identity is shared between C# and JavaScript.
/// </summary>
/// <remarks>
/// JavaScript constructs one with <c>new module.ExampleCounter(start)</c> or receives one from
/// <see cref="ExampleMathModule.MakeCounter" />. Both sides observe the same instance; call
/// <c>release()</c> from JavaScript when the handle is no longer needed.
/// </remarks>
[ExpoSharedObject]
public sealed partial class ExampleCounter : SharedObject
{
  private bool resourcesReleased;

  [JS]
  public ExampleCounter(double start)
  {
    Count = start;
  }

  [JS]
  public double Count { get; private set; }

  [JS("increment")]
  public double Increment(double by)
  {
    Count += by;
    return Count;
  }

  /// <summary>
  /// Cleans up the counter's resources; the guard keeps the cleanup idempotent even though the
  /// runtime already guarantees at most one invocation.
  /// </summary>
  protected override void OnRelease()
  {
    if (resourcesReleased)
    {
      return;
    }

    resourcesReleased = true;
    Count = 0;
  }
}
