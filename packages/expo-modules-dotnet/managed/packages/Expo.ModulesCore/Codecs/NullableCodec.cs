using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NullableCodec<T, TCodec> : IJavaScriptCodec<T?>
    where T : struct
    where TCodec : IJavaScriptCodec<T>
{
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime) =>
      value.HasValue ? TCodec.Encode(value.Value, runtime) : runtime.CreateNull();
}
