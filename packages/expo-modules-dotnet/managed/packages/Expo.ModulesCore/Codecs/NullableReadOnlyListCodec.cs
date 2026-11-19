using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to a read-only list container.</summary>
/// <remarks>
/// A nullable list container needs nullish handling that <see cref="JavaScriptArrayCodec{T, TCodec}"/>
/// does not have, so this adapter wraps it instead of composing through
/// <see cref="NullableReferenceCodec{T, TCodec}"/>.
/// </remarks>
public readonly struct NullableReadOnlyListCodec<T, TCodec> : IJavaScriptCodec<IReadOnlyList<T>?>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyList<T>? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptArrayCodec<T, TCodec>.DecodeToArray(value, runtime, context);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyList<T>? Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptArrayCodec<T, TCodec>.DecodeToArray(value.Ref, runtime, context);

  /// <summary>Encodes a null container as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(IReadOnlyList<T>? value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value is null
          ? runtime.CreateNull()
          : JavaScriptArrayCodec<T, TCodec>.Encode(value, runtime, context);

  /// <summary>Releases every element of a non-null list.</summary>
  public static void Release(IReadOnlyList<T>? value)
  {
    if (value is not null)
    {
      JavaScriptArrayCodec<T, TCodec>.Release(value);
    }
  }
}
