using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal readonly unsafe struct JavaScriptValueInner
{
  public JavaScriptValueInner(JsiContext context, ExpoJsiValueHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiValueHandle Handle { get; }

  public JavaScriptValueKind Kind
  {
    get
    {
      ExpoJsiError error;
      var kind = Context.Api->GetKind(Context.RuntimeHandle, Handle, &error);
      Context.ThrowIfError(error, "Failed to read JavaScript value kind.");
      return (JavaScriptValueKind)kind;
    }
  }

  public bool IsNullish => Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;

  public bool AsBool()
  {
    ExpoJsiError error;
    var value = Context.Api->ReadBool(Context.RuntimeHandle, Handle, &error);
    Context.ThrowIfError(error, "Failed to read JavaScript boolean.");
    return value;
  }

  public double AsDouble()
  {
    ExpoJsiError error;
    var value = Context.Api->ReadDouble(Context.RuntimeHandle, Handle, &error);
    Context.ThrowIfError(error, "Failed to read JavaScript number.");
    return value;
  }

  public string AsString() => Context.Api->ReadString(Context.RuntimeHandle, Handle);

  public string CoerceToString() =>
    Context.Api->CoerceJavaScriptValueToString(Context.RuntimeHandle, Handle);

  public bool IsPromise => Context.Api->IsPromiseValue(Context.RuntimeHandle, Handle);

  public bool IsError => Context.Api->IsErrorValue(Context.RuntimeHandle, Handle);

  public bool IsArrayBuffer => Kind == JavaScriptValueKind.ArrayBuffer;

  public ExpoJsiArrayBufferResult RetainArrayBuffer() =>
    Context.Api->RetainArrayBuffer(Context.RuntimeHandle, Handle);

  public ExpoJsiMutableBufferResult TryGetMutableBuffer() =>
    Context.Api->TryGetMutableBuffer(Context.RuntimeHandle, Handle);

  public ExpoJsiValueHandle AsObject()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Object
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to object.");
    }
    return result.Value;
  }

  public ExpoJsiValueHandle AsArray()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Array
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to array.");
    }
    return result.Value;
  }

  public ExpoJsiValueHandle AsFunction()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Function
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to function.");
    }

    return result.Value;
  }

  public ExpoJsiValueHandle Retain()
  {
    var result = Context.Api->CloneJavaScriptValue(Context.RuntimeHandle, Handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript value.");
    }
    return result.Value;
  }
}
