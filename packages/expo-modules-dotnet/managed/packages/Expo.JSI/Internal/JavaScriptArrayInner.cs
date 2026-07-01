using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal readonly unsafe struct JavaScriptArrayInner
{
  public JavaScriptArrayInner(JsiContext context, ExpoJsiValueHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiValueHandle Handle { get; }

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
    if (!result.IsOk)
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

  public ExpoJsiValueHandle AsObject()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Object
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript array as object.");
    }
    return result.Value;
  }

  public ExpoJsiValueHandle AsValue()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Array
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript array value.");
    }
    return result.Value;
  }
}
