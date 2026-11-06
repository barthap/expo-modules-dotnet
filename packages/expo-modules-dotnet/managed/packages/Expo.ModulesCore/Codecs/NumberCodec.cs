using System.Numerics;
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NumberCodec<T> : IJavaScriptCodec<T>, IJavaScriptValueKindCodec
    where T : struct, INumber<T>
{
  /// <summary>Gets the JavaScript value kind that this codec decodes.</summary>
  public static JavaScriptValueKind Kind => JavaScriptValueKind.Number;

  public static T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      T.CreateChecked(value.AsDouble());

  public static T Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      T.CreateChecked(value.AsDouble());

  public static JavaScriptValue Encode(T value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(double.CreateChecked(value));
}
