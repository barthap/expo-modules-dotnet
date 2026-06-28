using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed class JavaScriptValue : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  private JavaScriptValue(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  internal static JavaScriptValue FromOwnedHandle(JsiContext context, ExpoJsiValueHandle handle) =>
    new(context, handle);

  internal ExpoJsiValueHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return handle;
    }
  }

  private JavaScriptValueInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptValueInner(context, handle);
    }
  }

  public JavaScriptValueKind Kind => Inner.Kind;

  public bool IsPromise => Inner.IsPromise;

  public bool IsError => Inner.IsError;

  public bool IsBool => Kind == JavaScriptValueKind.Bool;

  public bool IsNullish => Inner.IsNullish;

  public bool AsBool() => Inner.AsBool();

  public bool IsDouble => Kind == JavaScriptValueKind.Number;

  public double AsDouble() => Inner.AsDouble();

  public bool IsString => Kind == JavaScriptValueKind.String;

  public string AsString() => Inner.AsString();

  internal string CoerceToString() => Inner.CoerceToString();

  public bool IsObject => Kind == JavaScriptValueKind.Object;

  public JavaScriptObject AsObject() => new(context, Inner.AsObject());

  public JavaScriptArray AsArray() => new(context, Inner.AsArray());

  public JavaScriptPromiseValue AsPromiseValue()
  {
    ThrowIfDisposed();
    if (!IsPromise)
    {
      throw new InvalidOperationException("Value is not a JavaScript Promise.");
    }
    return new JavaScriptPromiseValue(AsValue());
  }

  public JavaScriptErrorObject AsErrorObject()
  {
    ThrowIfDisposed();
    if (!IsError)
    {
      throw new InvalidOperationException("Value is not a JavaScript Error object.");
    }
    return new JavaScriptErrorObject(AsValue());
  }

  public JavaScriptValue AsValue() => FromOwnedHandle(context, Inner.Retain());

  public JavaScriptValue Retain() => AsValue();

  public JavaScriptValueRef Ref =>
    JavaScriptValueRef.FromBorrowedRoot(
        JavaScriptHandleScope.CurrentFor(context),
        Inner
    );

  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
      }
    }
    handle = 0;
  }

  public ExpoJsiValueHandle Detach()
  {
    ThrowIfDisposed();
    var detached = handle;
    handle = 0;
    return detached;
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
