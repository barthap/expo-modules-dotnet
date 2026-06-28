namespace Expo.JSI;

public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptValueRef thisValue,
    JavaScriptArguments arguments,
    object context);
