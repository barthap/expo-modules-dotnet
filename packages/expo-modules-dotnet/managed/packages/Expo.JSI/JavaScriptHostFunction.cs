namespace Expo.JSI;

/// <summary>
/// Callback invoked by JavaScript when a managed host function is called.
/// </summary>
/// <param name="runtime">Runtime that owns the callback invocation.</param>
/// <param name="thisValue">
/// Scoped reference to the JavaScript <c>this</c> value. It is valid only during the callback.
/// Use <see cref="JavaScriptValueRef.Retain" /> to keep it after returning.
/// </param>
/// <param name="arguments">
/// Call-scoped argument list. Values returned by <see cref="JavaScriptArguments.GetValue" /> are
/// valid only during the callback unless retained.
/// </param>
/// <param name="context">Managed callback state supplied when the host function was created.</param>
/// <returns>
/// An owned JavaScript value returned to JavaScript. The bridge takes ownership of the returned
/// handle after the callback returns.
/// </returns>
public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptValueRef thisValue,
    JavaScriptArguments arguments,
    object context);
