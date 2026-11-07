using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>
/// Decodes a parameter typed as an authored abstract shared-object base to the original instance
/// of whichever generated sealed subclass JavaScript passed.
/// </summary>
/// <remarks>
/// <para>
/// Decoding resolves the registry entry as <see cref="SharedObjectCodec{T}" /> does and accepts
/// the managed instance when it is a <typeparamref name="TBase" />. Only generated sealed
/// classes are ever registered, so that test is complete. Plain, foreign, and released objects
/// fail in the registry.
/// </para>
/// <para>
/// There is no <c>Encode</c>, because a JavaScript prototype belongs to one exact class. The
/// managed instance stays caller-borrowed.
/// </para>
/// </remarks>
public static class SharedObjectBaseCodec<TBase>
    where TBase : SharedObject
{
  /// <summary>
  /// Decodes a scoped JavaScript value to the original managed instance.
  /// </summary>
  public static TBase Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  )
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(runtimeContext);

    using var owned = value.Retain();
    return DecodeOwned(owned, runtimeContext);
  }

  /// <summary>
  /// Decodes an owned JavaScript value to the original managed instance.
  /// </summary>
  /// <remarks>
  /// This overload borrows <paramref name="value" />; ownership stays with the caller.
  /// </remarks>
  public static TBase Decode(
      JavaScriptValue value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  )
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(runtimeContext);

    return DecodeOwned(value, runtimeContext);
  }

  private static TBase DecodeOwned(JavaScriptValue value, DotnetRuntimeContext runtimeContext)
  {
    using var objectValue = value.AsObject();
    var managed = runtimeContext.SharedObjects.ResolveManaged(objectValue);
    if (managed is not TBase instance)
    {
      throw new InvalidOperationException(
          $"The shared object receiver is not a '{typeof(TBase)}' instance."
      );
    }
    return instance;
  }
}
