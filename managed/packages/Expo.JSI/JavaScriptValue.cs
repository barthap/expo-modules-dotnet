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

  internal static JavaScriptValue FromOwnedHandle(
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(context, handle);

  internal ExpoJsiValueHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return handle;
    }
  }

  public JavaScriptValueKind Kind
  {
    get
    {
      ThrowIfDisposed();
      unsafe
      {
        ExpoJsiError error;
        var kind = context.Api->GetKind(context.RuntimeHandle, handle, &error);
        context.ThrowIfError(error, "Failed to read JavaScript value kind.");
        return (JavaScriptValueKind)kind;
      }
    }
  }

  public bool AsBool()
  {
    ThrowIfDisposed();
    unsafe
    {
      ExpoJsiError error;
      var value = context.Api->ReadBool(context.RuntimeHandle, handle, &error);
      context.ThrowIfError(error, "Failed to read JavaScript boolean.");
      return value;
    }
  }

  public double AsDouble()
  {
    ThrowIfDisposed();
    unsafe
    {
      ExpoJsiError error;
      var value = context.Api->ReadDouble(context.RuntimeHandle, handle, &error);
      context.ThrowIfError(error, "Failed to read JavaScript number.");
      return value;
    }
  }

  public string AsString()
  {
    ThrowIfDisposed();
    unsafe
    {
      return context.Api->ReadString(context.RuntimeHandle, handle);
    }
  }

  public JavaScriptObject AsObject()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertValueToObject(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Object == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript value to object."
        );
      }
      return new JavaScriptObject(context, result.Object);
    }
  }

  public JavaScriptArray AsArray()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertValueToArray(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Array == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript value to array."
        );
      }
      return new JavaScriptArray(context, result.Array);
    }
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->CloneJavaScriptValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript value.");
      }
      return FromOwnedHandle(context, result.Value);
    }
  }

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
