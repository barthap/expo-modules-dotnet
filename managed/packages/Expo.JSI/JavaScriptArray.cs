using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed class JavaScriptArray : IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiArrayHandle handle;

  internal JavaScriptArray(JsiContext context, ExpoJsiArrayHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public uint Length
  {
    get
    {
      ThrowIfDisposed();
      unsafe
      {
        ExpoJsiError error;
        var length = context.Api->GetArrayLength(context.RuntimeHandle, handle, &error);
        context.ThrowIfError(error, "Failed to read JavaScript array length.");
        return length;
      }
    }
  }

  public JavaScriptValue GetValue(uint index)
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->GetArrayValueAtIndex(context.RuntimeHandle, handle, index);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array value.");
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  public void SetValue(uint index, JavaScriptValue value)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(value);
    unsafe
    {
      var error = context.Api->SetArrayValueAtIndex(
          context.RuntimeHandle,
          handle,
          index,
          value.Handle
      );
      context.ThrowIfError(error, "Failed to set JavaScript array value.");
    }
  }

  public JavaScriptObject AsObject()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertArrayToObject(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Object == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to object.");
      }
      return new JavaScriptObject(context, result.Object);
    }
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertArrayToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to value.");
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
