using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly struct JavaScriptBorrowedValue
{
  private readonly JsiContext context;
  private readonly ExpoJsiValueHandle handle;

  internal JavaScriptBorrowedValue(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public JavaScriptValueKind Kind
  {
    get
    {
      ThrowIfNull();
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
      ThrowIfNull();
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
      ThrowIfNull();
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
      ThrowIfNull();
      return Kind == JavaScriptValueKind.Bool;
    }
  }

  public bool AsBool()
  {
    ThrowIfNull();
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
      ThrowIfNull();
      return Kind == JavaScriptValueKind.Number;
    }
  }

  public double AsDouble()
  {
    ThrowIfNull();
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
      ThrowIfNull();
      return Kind == JavaScriptValueKind.String;
    }
  }

  public string AsString()
  {
    ThrowIfNull();
    unsafe
    {
      return context.Api->ReadString(context.RuntimeHandle, handle);
    }
  }

  public bool IsObject
  {
    get
    {
      ThrowIfNull();
      return Kind == JavaScriptValueKind.Object;
    }
  }

  public JavaScriptObject AsObject()
  {
    ThrowIfNull();
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
    ThrowIfNull();
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

  private void ThrowIfNull()
  {
    if (handle == 0)
    {
      throw new ObjectDisposedException(nameof(JavaScriptBorrowedValue));
    }
  }
}
