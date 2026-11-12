namespace Expo.JSI;

/// <summary>
/// Builds the JavaScript value that a failed host function throws or a failed promise rejects with.
/// </summary>
/// <param name="runtime">Runtime of the failed call. A handle scope is active.</param>
/// <param name="exception">The exception that the callback or the operation threw.</param>
/// <returns>
/// A new owned value. The caller takes ownership at once: the host-function trampoline detaches it
/// into the callback result and native throws it; the promise scheduler rejects with it and then
/// disposes it. If the factory throws or returns <see langword="null" />, the caller falls back to
/// its message-based error.
/// </returns>
public delegate JavaScriptValue JavaScriptErrorFactory(JavaScriptRuntime runtime, Exception exception);
