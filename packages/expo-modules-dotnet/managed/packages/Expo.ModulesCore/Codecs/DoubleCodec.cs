using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct DoubleCodec : IJavaScriptCodec<double>
{
  public static double Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.AsDouble();

  public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.AsDouble();

  public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      runtime.CreateNumber(value);
}
