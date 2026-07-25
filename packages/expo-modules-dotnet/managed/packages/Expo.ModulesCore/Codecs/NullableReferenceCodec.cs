using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to the codec of a supported reference type.</summary>
/// <remarks>
/// JavaScript <c>null</c> and <c>undefined</c> decode to C# <c>null</c> without calling
/// <typeparamref name="TCodec"/>, and C# <c>null</c> encodes as JavaScript <c>null</c>. Every other
/// value delegates to <typeparamref name="TCodec"/>, so the codec of the non-nullable type keeps its
/// own strictness. <see cref="NullableCodec{T, TCodec}"/> covers nullable value types.
/// </remarks>
public readonly struct NullableReferenceCodec<T, TCodec> : IJavaScriptCodec<T?>
    where T : class
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  /// <summary>Encodes <c>null</c> as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime) =>
      value is null ? runtime.CreateNull() : TCodec.Encode(value, runtime);
}
