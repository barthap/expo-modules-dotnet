namespace Expo.JSI;

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

  public static JavaScriptPromiseResult Resolve(
      Func<JavaScriptRuntime, JavaScriptValue> createValue
  )
  {
    ArgumentNullException.ThrowIfNull(createValue);
    return new JavaScriptPromiseResult(isRejected: false, createValue);
  }

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
