namespace Expo.JSI;

public readonly ref struct JavaScriptObjectRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptObjectInner inner;

  private JavaScriptObjectRef(JavaScriptHandleScope scope, JavaScriptObjectInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptObjectRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiObjectHandle handle
  ) => new(scope, new JavaScriptObjectInner(context, scope.TrackObject(handle)));

  public JavaScriptValueRef GetProperty(string name)
  {
    var handle = Inner.GetProperty(name);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  public JavaScriptObject Retain()
  {
    using var value = RetainAsValue();
    return value.AsObject();
  }

  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptObjectInner Inner
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
