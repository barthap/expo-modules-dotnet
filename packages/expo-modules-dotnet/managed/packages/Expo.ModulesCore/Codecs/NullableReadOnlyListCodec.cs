using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to a read-only list container.</summary>
/// <remarks>
/// <see cref="JavaScriptArrayCodec{T, TCodec}"/> decodes through <c>DecodeToArray</c> and does not
/// implement <see cref="IJavaScriptCodec{T}"/>, so a nullable list container needs its own adapter
/// instead of composing through <see cref="NullableReferenceCodec{T, TCodec}"/>.
/// </remarks>
public readonly struct NullableReadOnlyListCodec<T, TCodec> : IJavaScriptCodec<IReadOnlyList<T>?>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyList<T>? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptArrayCodec<T, TCodec>.DecodeToArray(value, runtime);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyList<T>? Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptArrayCodec<T, TCodec>.DecodeToArray(value.Ref, runtime);

  /// <summary>Encodes a null container as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(IReadOnlyList<T>? value, JavaScriptRuntime runtime) =>
      value is null
          ? runtime.CreateNull()
          : JavaScriptArrayCodec<T, TCodec>.Encode(value, runtime);
}
