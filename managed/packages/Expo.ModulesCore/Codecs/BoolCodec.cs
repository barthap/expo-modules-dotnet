using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct BoolCodec : IJavaScriptCodec<bool>
{
  public static bool Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsBool();

  public static bool Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsBool();

  public static JavaScriptValue Encode(bool value, JavaScriptRuntime runtime) =>
      runtime.CreateBool(value);
}
