using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct StringCodec : IJavaScriptCodec<string>
{
  public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value);
}
