using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct JavaScriptValueCodec : IJavaScriptCodec<JavaScriptValue>
{
  public static JavaScriptValue Decode(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }

  public static JavaScriptValue Decode(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }

  public static JavaScriptValue Encode(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(value);
    ArgumentNullException.ThrowIfNull(runtime);
    return value.Retain();
  }
}
