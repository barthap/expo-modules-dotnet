using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);

  /// <summary>Releases every JavaScript-backed leaf that <paramref name="value"/> owns.</summary>
  /// <remarks>
  /// Generated bindings call this for a decoded argument after the module call and for a returned
  /// value after it is encoded. A leaf is a <see cref="JavaScriptValue"/>, an
  /// <see cref="ArrayBuffer"/>, or a <see cref="Uint8Array"/>. The default releases nothing.
  /// Release is idempotent and accepts a null value.
  /// </remarks>
  static virtual void Release(T value) { }
}
