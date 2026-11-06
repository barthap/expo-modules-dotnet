using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Converts a JavaScript value to and from a two-member <see cref="Either{TFirst, TSecond}"/>.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. Decode reads the value
/// kind once and calls the codec whose <see cref="IJavaScriptValueKindCodec.Kind"/> matches. It
/// never tries one member and falls back to the other. A value of any other kind throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
/// <typeparam name="TFirst">The first member type.</typeparam>
/// <typeparam name="TFirstCodec">The codec of <typeparamref name="TFirst"/>.</typeparam>
/// <typeparam name="TSecond">The second member type.</typeparam>
/// <typeparam name="TSecondCodec">The codec of <typeparamref name="TSecond"/>.</typeparam>
public readonly struct EitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IJavaScriptCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IJavaScriptCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IJavaScriptCodec<TSecond>, IJavaScriptValueKindCodec
{
  /// <summary>Decodes a JavaScript value into the member that matches its kind.</summary>
  public static Either<TFirst, TSecond> Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var kind = value.Kind;
    if (kind == TFirstCodec.Kind)
    {
      return Either<TFirst, TSecond>.FromFirst(TFirstCodec.Decode(value, runtime));
    }
    if (kind == TSecondCodec.Kind)
    {
      return Either<TFirst, TSecond>.FromSecond(TSecondCodec.Decode(value, runtime));
    }
    throw new InvalidOperationException(
        $"Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.");
  }

  /// <summary>Decodes a JavaScript value into the member that matches its kind.</summary>
  public static Either<TFirst, TSecond> Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Decode(value.Ref, runtime);

  /// <summary>Encodes the member that the union holds.</summary>
  public static JavaScriptValue Encode(Either<TFirst, TSecond> value, JavaScriptRuntime runtime) =>
      value.IsFirst
          ? TFirstCodec.Encode(value.First, runtime)
          : TSecondCodec.Encode(value.Second, runtime);
}
