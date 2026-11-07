using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to a string-keyed read-only dictionary container.</summary>
/// <remarks>
/// A nullable read-only dictionary container needs nullish handling that
/// <see cref="JavaScriptDictionaryCodec{T, TCodec}"/> does not have, so this adapter wraps it instead
/// of composing through <see cref="NullableReferenceCodec{T, TCodec}"/>.
/// </remarks>
public readonly struct NullableReadOnlyDictionaryCodec<T, TCodec>
    : IJavaScriptCodec<IReadOnlyDictionary<string, T>?>
    where TCodec : IJavaScriptCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyDictionary<string, T>? Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime);

  /// <summary>Decodes an owned JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static IReadOnlyDictionary<string, T>? Decode(
      JavaScriptValue value,
      JavaScriptRuntime runtime) =>
      value.IsNullish ? null : JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary(value, runtime);

  /// <summary>Encodes a null container as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue Encode(
      IReadOnlyDictionary<string, T>? value,
      JavaScriptRuntime runtime) =>
      value is null
          ? runtime.CreateNull()
          : JavaScriptDictionaryCodec<T, TCodec>.Encode(value, runtime);

  /// <summary>Releases every value of a non-null dictionary.</summary>
  public static void Release(IReadOnlyDictionary<string, T>? value)
  {
    if (value is not null)
    {
      JavaScriptDictionaryCodec<T, TCodec>.Release(value);
    }
  }
}
