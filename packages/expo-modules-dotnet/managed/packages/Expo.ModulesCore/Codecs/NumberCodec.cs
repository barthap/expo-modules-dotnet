using System.Numerics;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NumberCodec<T> : IJavaScriptCodec<T>
    where T : struct, INumber<T>
{
  public static T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      T.CreateChecked(value.AsDouble());

  public static T Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      T.CreateChecked(value.AsDouble());

  public static JavaScriptValue Encode(T value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(double.CreateChecked(value));
}
