namespace Expo.JSI;

public sealed class JavaScriptValue : IDisposable
{
  private readonly JavaScriptRuntime runtime;
  private nint handle;

  internal JavaScriptValue(JavaScriptRuntime runtime, nint handle)
  {
    this.runtime = runtime;
    this.handle = handle;
  }

  public JavaScriptValueKind Kind
  {
    get
    {
      ThrowIfDisposed();
      return runtime.GetValueKind(handle);
    }
  }

  public double AsDouble()
  {
    ThrowIfDisposed();
    return runtime.GetDouble(handle);
  }

  public void Dispose()
  {
    if (handle != 0) {
      runtime.ReleaseValue(handle);
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
