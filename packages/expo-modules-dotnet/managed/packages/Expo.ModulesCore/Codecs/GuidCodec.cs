using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct GuidCodec : IJavaScriptCodec<Guid>
{
  public static Guid Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      Guid.Parse(value.AsString());

  public static Guid Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      Guid.Parse(value.AsString());

  public static JavaScriptValue Encode(Guid value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      runtime.CreateString(value.ToString("D"));
}
