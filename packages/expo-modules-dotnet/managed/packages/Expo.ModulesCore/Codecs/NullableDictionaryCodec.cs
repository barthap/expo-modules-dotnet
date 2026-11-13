using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to a string-keyed dictionary container.</summary>
/// <remarks>
/// A nullable dictionary container needs nullish handling that
/// <see cref="JavaScriptDictionaryCodec{T, TCodec}"/> does not have, so this adapter wraps it instead
/// of composing through <see cref="NullableReferenceCodec{T, TCodec}"/>.
/// </remarks>
public readonly struct NullableDictionaryCodec<T, TCodec> : IJavaScriptCodec<Dictionary<string, T>?>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static Dictionary<string, T>? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime, context);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static Dictionary<string, T>? Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime, context);

  /// <summary>Encodes a null container as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(Dictionary<string, T>? value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value is null
          ? runtime.CreateNull()
          : JavaScriptDictionaryCodec<T, TCodec>.Encode(value, runtime, context);

  /// <summary>Releases every value of a non-null dictionary.</summary>
  public static void Release(Dictionary<string, T>? value)
  {
    if (value is not null)
    {
      JavaScriptDictionaryCodec<T, TCodec>.Release(value);
    }
  }
}
