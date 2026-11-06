using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Converts a JavaScript value to and from a two-member union with scoped member codecs.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. Decode reads the value
/// kind once and calls the scoped codec whose <see cref="IJavaScriptValueKindCodec.Kind"/> matches.
/// It never tries one member and falls back to the other, so a failed kind test registers nothing
/// in the scope. A value of any other kind throws <see cref="InvalidOperationException"/>.
/// </remarks>
/// <typeparam name="TFirst">The first member type.</typeparam>
/// <typeparam name="TFirstCodec">The scoped codec of <typeparamref name="TFirst"/>.</typeparam>
/// <typeparam name="TSecond">The second member type.</typeparam>
/// <typeparam name="TSecondCodec">The scoped codec of <typeparamref name="TSecond"/>.</typeparam>
public readonly struct GeneratedScopedEitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IGeneratedScopedCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IGeneratedScopedCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IGeneratedScopedCodec<TSecond>, IJavaScriptValueKindCodec
{
  /// <summary>Decodes a JavaScript value into the member that matches its kind.</summary>
  public static Either<TFirst, TSecond> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      GeneratedConversionScope scope)
  {
    var kind = value.Kind;
    if (kind == TFirstCodec.Kind)
    {
      return Either<TFirst, TSecond>.FromFirst(TFirstCodec.Decode(value, runtime, scope));
    }
    if (kind == TSecondCodec.Kind)
    {
      return Either<TFirst, TSecond>.FromSecond(TSecondCodec.Decode(value, runtime, scope));
    }
    throw new InvalidOperationException(
        $"Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.");
  }

  /// <summary>Registers the leaves of the member that the union holds.</summary>
  public static void Transfer(Either<TFirst, TSecond> value, GeneratedConversionScope scope)
  {
    if (value.IsFirst)
    {
      TFirstCodec.Transfer(value.First, scope);
    }
    else
    {
      TSecondCodec.Transfer(value.Second, scope);
    }
  }

  /// <summary>Encodes the member that the union holds and leaves its leaves usable.</summary>
  public static JavaScriptValue EncodeBorrowed(Either<TFirst, TSecond> value, JavaScriptRuntime runtime) =>
      value.IsFirst
          ? TFirstCodec.EncodeBorrowed(value.First, runtime)
          : TSecondCodec.EncodeBorrowed(value.Second, runtime);
}
