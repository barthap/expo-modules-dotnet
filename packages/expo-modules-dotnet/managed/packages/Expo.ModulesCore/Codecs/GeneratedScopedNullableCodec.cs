using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Adds nullish handling to the scoped codec of a value type.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. JavaScript
/// <c>null</c> and <c>undefined</c> decode to <c>null</c> without calling
/// <typeparamref name="TCodec"/>, and <c>null</c> encodes as JavaScript <c>null</c>.
/// </remarks>
/// <typeparam name="T">The value type.</typeparam>
/// <typeparam name="TCodec">The scoped codec of <typeparamref name="T"/>.</typeparam>
public readonly struct GeneratedScopedNullableCodec<T, TCodec> : IGeneratedScopedCodec<T?>
    where T : struct
    where TCodec : IGeneratedScopedCodec<T>
{
  /// <summary>Decodes a scoped JavaScript value, mapping nullish input to <c>null</c>.</summary>
  public static T? Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime, scope);

  /// <summary>Registers the leaves of a non-null value. A null value registers nothing.</summary>
  public static void Transfer(T? value, GeneratedConversionScope scope)
  {
    if (value.HasValue)
    {
      TCodec.Transfer(value.Value, scope);
    }
  }

  /// <summary>Encodes <c>null</c> as JavaScript <c>null</c> and delegates every other value.</summary>
  public static JavaScriptValue EncodeBorrowed(T? value, JavaScriptRuntime runtime) =>
      value.HasValue ? TCodec.EncodeBorrowed(value.Value, runtime) : runtime.CreateNull();
}
