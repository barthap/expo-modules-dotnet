using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct StringCodec : IJavaScriptCodec<string>, IJavaScriptValueKindCodec
{
  /// <summary>Gets the JavaScript value kind that this codec decodes.</summary>
  public static JavaScriptValueKind Kind => JavaScriptValueKind.String;

  public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value);
}
