using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class NullableJavaScriptCallbackCodec<TResult, TResultCodec>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TResult>? Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptCallbackCodec<TResult, TResultCodec>.Decode(value, runtime, context);
}

public static class NullableJavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
    where TArgs : struct
    where TArgsCodec : IJavaScriptArgsCodec<TArgs>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TArgs, TResult>? Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context) =>
      value.IsNullish
          ? null
          : JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>.Decode(value, runtime, context);
}
