namespace Expo.JSI;

public sealed class JavaScriptObject : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiObjectHandle handle;

  internal JavaScriptObject(JsiContext context, ExpoJsiObjectHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  private JavaScriptObjectInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptObjectInner(context, handle);
    }
  }

  public void SetProperty(string name, JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Inner.SetProperty(name, value.Handle);
  }

  public JavaScriptValue GetProperty(string name) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetProperty(name));

  public JavaScriptValue AsValue() =>
    JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());

  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseObjectHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
