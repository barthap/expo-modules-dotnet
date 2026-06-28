namespace Expo.JSI;

public readonly ref struct JavaScriptValueRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptValueInner inner;

  private JavaScriptValueRef(JavaScriptHandleScope scope, JavaScriptValueInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptValueRef FromBorrowedRoot(
      JavaScriptHandleScope scope,
      JavaScriptValueInner inner
  ) => new(scope, inner);

  internal static JavaScriptValueRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptValueInner(context, scope.TrackValue(handle)));

  public JavaScriptValueKind Kind => Inner.Kind;

  public bool IsNullish => Inner.IsNullish;

  public bool IsBool => Kind == JavaScriptValueKind.Bool;

  public bool AsBool() => Inner.AsBool();

  public bool IsDouble => Kind == JavaScriptValueKind.Number;

  public double AsDouble() => Inner.AsDouble();

  public bool IsString => Kind == JavaScriptValueKind.String;

  public string AsString() => Inner.AsString();

  public string CoerceToString() => Inner.CoerceToString();

  public bool IsObject => Kind == JavaScriptValueKind.Object;

  public JavaScriptObjectRef AsObject()
  {
    var objectHandle = Inner.AsObject();
    return JavaScriptObjectRef.FromScopedHandle(Scope, Inner.Context, objectHandle);
  }

  public JavaScriptArrayRef AsArray()
  {
    var arrayHandle = Inner.AsArray();
    return JavaScriptArrayRef.FromScopedHandle(Scope, Inner.Context, arrayHandle);
  }

  public JavaScriptValue Retain() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.Retain());

  private JavaScriptValueInner Inner
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
