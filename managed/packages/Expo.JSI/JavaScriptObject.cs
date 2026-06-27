using System.Text;

namespace Expo.JSI;

public sealed class JavaScriptObject : IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiObjectHandle handle;

  internal JavaScriptObject(JsiContext context, ExpoJsiObjectHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public void SetProperty(string name, JavaScriptValue value)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(value);

    var nameBytes = Encoding.UTF8.GetBytes(name);
    unsafe
    {
      var error = context.Api->SetObjectProperty(
          context.RuntimeHandle,
          handle,
          nameBytes,
          value.Handle
      );
      context.ThrowIfError(error, "Failed to set JavaScript object property.");
    }
  }

  public JavaScriptValue GetProperty(string name)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(name);

    var nameBytes = Encoding.UTF8.GetBytes(name);
    unsafe
    {
      var result = context.Api->GetObjectProperty(
          context.RuntimeHandle,
          handle,
          nameBytes
      );
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to get JavaScript object property."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertObjectToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript object to value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

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
