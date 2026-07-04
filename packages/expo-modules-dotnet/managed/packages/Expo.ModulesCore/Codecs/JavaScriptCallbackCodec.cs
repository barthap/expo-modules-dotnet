using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class JavaScriptCallbackCodec<TResult, TResultCodec>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TResult> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var function = value.AsFunction();
    return JavaScriptCallback<TResult>.FromOwnedFunction(
        context,
        function,
        static (result, jsRuntime) => TResultCodec.Decode(result, jsRuntime)
    );
  }
}

public static class JavaScriptCallbackCodec<T1, T1Codec, TResult, TResultCodec>
    where T1Codec : IJavaScriptCodec<T1>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<T1, TResult> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var function = value.AsFunction();
    return JavaScriptCallback<T1, TResult>.FromOwnedFunction(
        context,
        function,
        static (arg, jsRuntime) => T1Codec.Encode(arg, jsRuntime),
        static (result, jsRuntime) => TResultCodec.Decode(result, jsRuntime)
    );
  }
}

public static class JavaScriptCallbackCodec<T1, T1Codec, T2, T2Codec, TResult, TResultCodec>
    where T1Codec : IJavaScriptCodec<T1>
    where T2Codec : IJavaScriptCodec<T2>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<T1, T2, TResult> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var function = value.AsFunction();
    return JavaScriptCallback<T1, T2, TResult>.FromOwnedFunction(
        context,
        function,
        static (arg, jsRuntime) => T1Codec.Encode(arg, jsRuntime),
        static (arg, jsRuntime) => T2Codec.Encode(arg, jsRuntime),
        static (result, jsRuntime) => TResultCodec.Decode(result, jsRuntime)
    );
  }
}
