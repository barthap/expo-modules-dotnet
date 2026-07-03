using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct UriCodec : IJavaScriptCodec<Uri>
{
  public static Uri Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      new(value.AsString(), UriKind.Absolute);

  public static Uri Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      new(value.AsString(), UriKind.Absolute);

  public static JavaScriptValue Encode(Uri value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value.AbsoluteUri);
}
