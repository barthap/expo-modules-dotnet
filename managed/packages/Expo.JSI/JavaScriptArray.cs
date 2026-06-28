namespace Expo.JSI;

public sealed class JavaScriptArray : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiArrayHandle handle;

  internal JavaScriptArray(JsiContext context, ExpoJsiArrayHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  private JavaScriptArrayInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptArrayInner(context, handle);
    }
  }

  public uint Length => Inner.Length;

  public JavaScriptValue GetValue(uint index) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetValue(index));

  public void SetValue(uint index, JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Inner.SetValue(index, value.Handle);
  }

  public JavaScriptObject AsObject() => new(context, Inner.AsObject());

  public JavaScriptValue AsValue() =>
    JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());

  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseArrayHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
