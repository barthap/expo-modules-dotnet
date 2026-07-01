namespace Expo.JSI;

/// <summary>
/// Describes how to settle a managed-created JavaScript promise.
/// </summary>
/// <remarks>
/// The factory callback is invoked later on the JavaScript runtime. It must return a newly owned
/// <see cref="JavaScriptValue" />; the promise scheduler disposes that value after using it.
/// </remarks>
public readonly struct JavaScriptPromiseResult
{
  private readonly Func<JavaScriptRuntime, JavaScriptValue> createValue;

  private JavaScriptPromiseResult(
      bool isRejected,
      Func<JavaScriptRuntime, JavaScriptValue> createValue
  )
  {
    IsRejected = isRejected;
    this.createValue = createValue;
  }

  internal bool IsRejected { get; }

  /// <summary>
  /// Creates a result that resolves a JavaScript promise.
  /// </summary>
  /// <param name="createValue">
  /// Factory that creates an owned resolution value on the JavaScript runtime.
  /// </param>
  public static JavaScriptPromiseResult Resolve(
      Func<JavaScriptRuntime, JavaScriptValue> createValue
  )
  {
    ArgumentNullException.ThrowIfNull(createValue);
    return new JavaScriptPromiseResult(isRejected: false, createValue);
  }

  /// <summary>
  /// Creates a result that rejects a JavaScript promise.
  /// </summary>
  /// <param name="createReason">
  /// Factory that creates an owned rejection reason on the JavaScript runtime.
  /// </param>
  public static JavaScriptPromiseResult Reject(
      Func<JavaScriptRuntime, JavaScriptValue> createReason
  )
  {
    ArgumentNullException.ThrowIfNull(createReason);
    return new JavaScriptPromiseResult(isRejected: true, createReason);
  }

  internal JavaScriptValue CreateValue(JavaScriptRuntime runtime)
  {
    if (createValue is null)
    {
      throw new InvalidOperationException("Promise result was not initialized.");
    }
    return createValue(runtime);
  }
}
