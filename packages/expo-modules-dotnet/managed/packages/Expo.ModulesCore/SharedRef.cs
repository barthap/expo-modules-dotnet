namespace Expo.ModulesCore;

/// <summary>
/// A non-owning shared-object carrier that holds a strong managed reference to a value.
/// </summary>
/// <remarks>
/// <see cref="SharedRef{T}"/> never infers ownership of <typeparamref name="T"/> and never
/// disposes it; its default release behavior is a no-op for the carried value. A subclass that
/// owns the resource overrides <see cref="SharedObject.OnRelease"/> and cleans up explicitly.
/// Only a concrete, sealed, non-generic <c>[ExpoSharedObject]</c> subclass crosses the generated
/// boundary; this base is a managed carrier, not a generated codec surface.
/// </remarks>
public class SharedRef<T> : SharedObject
{
  public SharedRef(T reference)
  {
    Ref = reference;
  }

  /// <summary>The carried value, exactly as supplied to the constructor.</summary>
  public T Ref { get; }
}
