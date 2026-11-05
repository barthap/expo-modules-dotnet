using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>
/// Nullable form of <see cref="SharedObjectCodec{T}" /> for one exact generated
/// <c>[ExpoSharedObject]</c> class.
/// </summary>
/// <remarks>
/// JavaScript <c>null</c> and <c>undefined</c> decode to C# <c>null</c>, and C# <c>null</c>
/// encodes as JavaScript <c>null</c>. Neither direction touches the registry, the class
/// registration, or pairing. Every other value goes to <see cref="SharedObjectCodec{T}" />
/// unchanged, so exact type, owning context, strict identity, and released-object rejection stay
/// as they are.
/// </remarks>
public static class NullableSharedObjectCodec<T>
    where T : SharedObject
{
  /// <summary>
  /// Decodes a scoped JavaScript value to the original managed instance, or <c>null</c> for
  /// JavaScript <c>null</c> and <c>undefined</c>.
  /// </summary>
  public static T? Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  ) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  /// <summary>
  /// Decodes an owned JavaScript value to the original managed instance, or <c>null</c> for
  /// JavaScript <c>null</c> and <c>undefined</c>.
  /// </summary>
  /// <remarks>
  /// This overload borrows <paramref name="value" />; ownership stays with the caller.
  /// </remarks>
  public static T? Decode(
      JavaScriptValue value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  ) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  /// <summary>
  /// Encodes a borrowed managed instance as its paired JavaScript object, or <c>null</c> as
  /// JavaScript <c>null</c>.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> is owned by the caller.
  /// </remarks>
  public static JavaScriptValue Encode(
      T? value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext runtimeContext
  ) =>
      value is null ? runtime.CreateNull() : SharedObjectCodec<T>.Encode(value, runtime, runtimeContext);
}
