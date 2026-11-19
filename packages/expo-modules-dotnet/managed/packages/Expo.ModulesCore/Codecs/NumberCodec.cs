using System.Numerics;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NumberCodec<T> : IJavaScriptCodec<T>
    where T : struct, INumber<T>
{
  public static T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      T.CreateChecked(value.AsDouble());

  public static T Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      T.CreateChecked(value.AsDouble());

  public static JavaScriptValue Encode(T value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      runtime.CreateNumber(double.CreateChecked(value));
}
