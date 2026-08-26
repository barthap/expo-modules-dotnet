using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to a string-keyed dictionary container.</summary>
/// <remarks>
/// <see cref="JavaScriptDictionaryCodec{T, TCodec}"/> decodes through <c>DecodeToDictionary</c> and
/// does not implement <see cref="IJavaScriptCodec{T}"/>, so a nullable dictionary container needs its
/// own adapter instead of composing through <see cref="NullableReferenceCodec{T, TCodec}"/>.
/// </remarks>
public readonly struct NullableDictionaryCodec<T, TCodec> : IJavaScriptCodec<Dictionary<string, T>?>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static Dictionary<string, T>? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static Dictionary<string, T>? Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime);

  /// <summary>Encodes a null container as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(Dictionary<string, T>? value, JavaScriptRuntime runtime) =>
      value is null
          ? runtime.CreateNull()
          : JavaScriptDictionaryCodec<T, TCodec>.Encode(value, runtime);
}
