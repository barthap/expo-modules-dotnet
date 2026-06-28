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

  public bool IsPromise
  {
    get
    {
      ThrowIfDisposed();
      unsafe
      {
        return context.Api->IsPromiseValue(context.RuntimeHandle, handle);
      }
    }
  }

  public bool IsError
  {
    get
    {
      ThrowIfDisposed();
      unsafe
      {
        return context.Api->IsErrorValue(context.RuntimeHandle, handle);
      }
    }
  }

  public bool IsBool
  {
    get
    {
      ThrowIfDisposed();
      return Kind == JavaScriptValueKind.Bool;
    }
  }

  public bool IsNullish
  {
    get
    {
      ThrowIfDisposed();
      return Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;
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

  public bool IsDouble
  {
    get
    {
      ThrowIfDisposed();
      return Kind == JavaScriptValueKind.Number;
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

  public bool IsString
  {
    get
    {
      ThrowIfDisposed();
      return Kind == JavaScriptValueKind.String;
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

  internal string CoerceToString()
  {
    ThrowIfDisposed();
    unsafe
    {
      return context.Api->CoerceJavaScriptValueToString(context.RuntimeHandle, handle);
    }
  }

  public bool IsObject
  {
    get
    {
      ThrowIfDisposed();
      return Kind == JavaScriptValueKind.Object;
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
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to object.");
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
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to array.");
      }
      return new JavaScriptArray(context, result.Array);
    }
  }

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

  public JavaScriptValue Retain() => AsValue();

  public JavaScriptValueRef Ref
  {
    get
    {
      ThrowIfDisposed();
      var scope = JsiRefScope.CurrentFor(context);
      return new JavaScriptValueRef(context, scope, handle);
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
