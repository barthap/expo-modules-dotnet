using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Lifts an ordinary codec into the scoped codec protocol.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. A union can use it to
/// pair an ordinary leaf codec with a scoped member. An ordinary value holds no leaf, so the
/// adapter never touches the scope.
/// </remarks>
/// <typeparam name="T">The value type.</typeparam>
/// <typeparam name="TCodec">The ordinary codec of <typeparamref name="T"/>.</typeparam>
public readonly struct GeneratedScopedOrdinaryCodec<T, TCodec>
    : IGeneratedScopedCodec<T>, IJavaScriptValueKindCodec
    where TCodec : IJavaScriptCodec<T>, IJavaScriptValueKindCodec
{
  /// <summary>Gets the JavaScript value kind that <typeparamref name="TCodec"/> decodes.</summary>
  public static JavaScriptValueKind Kind => TCodec.Kind;

  /// <summary>Decodes a JavaScript value with the ordinary codec and does not use the scope.</summary>
  public static T Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope) =>
      TCodec.Decode(value, runtime);

  /// <summary>Registers nothing, because an ordinary value holds no leaf.</summary>
  public static void Transfer(T value, GeneratedConversionScope scope)
  {
  }

  /// <summary>Encodes <paramref name="value"/> with the ordinary codec.</summary>
  public static JavaScriptValue EncodeBorrowed(T value, JavaScriptRuntime runtime) =>
      TCodec.Encode(value, runtime);
}
