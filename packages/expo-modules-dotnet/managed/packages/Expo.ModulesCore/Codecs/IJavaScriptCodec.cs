using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
}
