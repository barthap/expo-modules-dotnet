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

public static class JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
    where TArgs : struct
    where TArgsCodec : IJavaScriptArgsCodec<TArgs>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TArgs, TResult> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context)
  {
    var function = value.AsFunction();
    return JavaScriptCallback<TArgs, TResult>.FromOwnedFunction(
        context,
        function,
        static (args, jsRuntime) => TArgsCodec.Encode(args, jsRuntime),
        static (result, jsRuntime) => TResultCodec.Decode(result, jsRuntime)
    );
  }
}
