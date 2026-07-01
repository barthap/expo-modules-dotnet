using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct DoubleCodec : IJavaScriptCodec<double>
{
  public static double Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(value);
}
