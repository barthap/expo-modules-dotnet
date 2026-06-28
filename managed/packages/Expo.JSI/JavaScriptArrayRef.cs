namespace Expo.JSI;

public readonly ref struct JavaScriptArrayRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptArrayInner inner;

  private JavaScriptArrayRef(JavaScriptHandleScope scope, JavaScriptArrayInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptArrayRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiArrayHandle handle
  ) => new(scope, new JavaScriptArrayInner(context, scope.TrackArray(handle)));

  public uint Length => Inner.Length;

  public JavaScriptValueRef GetValue(uint index)
  {
    var handle = Inner.GetValue(index);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  public JavaScriptArray Retain()
  {
    using var value = RetainAsValue();
    return value.AsArray();
  }

  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptArrayInner Inner
  {
    get
    {
      _ = Scope;
      if (inner.Handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
      }
      return inner;
    }
  }

  private JavaScriptHandleScope Scope =>
    scope ?? throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
}
