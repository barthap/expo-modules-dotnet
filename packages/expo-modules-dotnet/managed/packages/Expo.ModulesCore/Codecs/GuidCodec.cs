using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct GuidCodec : IJavaScriptCodec<Guid>
{
  public static Guid Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      Guid.Parse(value.AsString());

  public static Guid Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Guid.Parse(value.AsString());

  public static JavaScriptValue Encode(Guid value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value.ToString("D"));
}
