using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

/// <summary>Converts between a managed <typeparamref name="T"/> and a JavaScript value.</summary>
/// <remarks>
/// Every call receives the <see cref="DotnetRuntimeContext"/> that owns the binding that runs. A
/// composite codec passes <c>runtime</c> and <c>context</c> to each child unchanged. A leaf codec
/// ignores the context and does not null-check it.
/// </remarks>
public interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime, DotnetRuntimeContext context);

  /// <summary>Releases every JavaScript-backed leaf that <paramref name="value"/> owns.</summary>
  /// <remarks>
  /// Generated bindings call this for a decoded argument after the module call and for a returned
  /// value after it is encoded. A leaf is a <see cref="JavaScriptValue"/>, an
  /// <see cref="ArrayBuffer"/>, or a <see cref="Uint8Array"/>. The default releases nothing.
  /// Release is idempotent and accepts a null value.
  /// </remarks>
  static virtual void Release(T value) { }
}
