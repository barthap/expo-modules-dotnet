using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Holds a returned value and its conversion scope until a Promise result settles.</summary>
/// <remarks>
/// This helper is for generated bindings, not ordinary module author code. <c>Resolve</c> transfers
/// the leaves of the value into the scope. The runtime factory encodes the value and then disposes
/// the scope, and an abandoned result disposes the scope without encoding. <c>Resolve</c> never
/// disposes the scope itself: when <c>Transfer</c> throws, the generated Promise lambda does.
/// </remarks>
/// <typeparam name="T">The returned type.</typeparam>
/// <typeparam name="TCodec">The scoped codec of <typeparamref name="T"/>.</typeparam>
public sealed class GeneratedTransferredResult<T, TCodec>
    where TCodec : IGeneratedScopedCodec<T>
{
  private readonly T value;
  private readonly GeneratedConversionScope scope;

  private GeneratedTransferredResult(T value, GeneratedConversionScope scope)
  {
    this.value = value;
    this.scope = scope;
  }

  /// <summary>Transfers the leaves of <paramref name="value"/> and returns the Promise result.</summary>
  public static JavaScriptPromiseResult Resolve(T value, GeneratedConversionScope scope)
  {
    TCodec.Transfer(value, scope);
    return JavaScriptPromiseResult.ResolveOwned(
        new GeneratedTransferredResult<T, TCodec>(value, scope),
        static (runtime, state) =>
        {
          try
          {
            return TCodec.EncodeBorrowed(state.value, runtime);
          }
          finally
          {
            state.scope.Dispose();
          }
        },
        static state => state.scope.Dispose());
  }
}
