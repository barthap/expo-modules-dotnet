using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed class JavaScriptPromise : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiPromiseHandle handle;

  internal JavaScriptPromise(JsiContext context, ExpoJsiPromiseHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertPromiseToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript promise to value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  public void Resolve(JavaScriptValue value)
  {
    Settle(value, reject: false);
  }

  public void Reject(JavaScriptValue error)
  {
    Settle(error, reject: true);
  }

  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleasePromiseHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void Settle(JavaScriptValue value, bool reject)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(value);
    unsafe
    {
      var error = reject
          ? context.Api->RejectPromise(context.RuntimeHandle, handle, value.Handle)
          : context.Api->ResolvePromise(context.RuntimeHandle, handle, value.Handle);
      context.ThrowIfError(error, reject
          ? "Failed to reject JavaScript promise."
          : "Failed to resolve JavaScript promise.");
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
