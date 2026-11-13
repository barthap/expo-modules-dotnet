using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NullableCodec<T, TCodec> : IJavaScriptCodec<T?>
    where T : struct
    where TCodec : IJavaScriptCodec<T>
{
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime, context);

  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime, context);

  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.HasValue ? TCodec.Encode(value.Value, runtime, context) : runtime.CreateNull();

  public static void Release(T? value)
  {
    if (value.HasValue)
    {
      TCodec.Release(value.Value);
    }
  }
}
