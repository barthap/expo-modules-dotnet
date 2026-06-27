namespace Expo.JSI;

public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptBorrowedValue thisValue,
    JavaScriptArguments arguments,
    object context);
