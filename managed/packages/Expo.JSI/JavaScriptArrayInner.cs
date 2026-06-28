using Expo.JSI.Interop;

namespace Expo.JSI;

internal readonly unsafe struct JavaScriptArrayInner
{
  public JavaScriptArrayInner(JsiContext context, ExpoJsiArrayHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiArrayHandle Handle { get; }

  public uint Length
  {
    get
    {
      ExpoJsiError error;
      var length = Context.Api->GetArrayLength(Context.RuntimeHandle, Handle, &error);
      Context.ThrowIfError(error, "Failed to read JavaScript array length.");
      return length;
    }
  }

  public ExpoJsiValueHandle GetValue(uint index)
  {
    var result = Context.Api->GetArrayValueAtIndex(Context.RuntimeHandle, Handle, index);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array value.");
    }
    return result.Value;
  }

  public void SetValue(uint index, ExpoJsiValueHandle value)
  {
    var error = Context.Api->SetArrayValueAtIndex(
        Context.RuntimeHandle,
        Handle,
        index,
        value
    );
    Context.ThrowIfError(error, "Failed to set JavaScript array value.");
  }

  public ExpoJsiObjectHandle AsObject()
  {
    var result = Context.Api->ConvertArrayToObject(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to object.");
    }
    return result.Object;
  }

  public ExpoJsiValueHandle AsValue()
  {
    var result = Context.Api->ConvertArrayToValue(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to value.");
    }
    return result.Value;
  }
}
