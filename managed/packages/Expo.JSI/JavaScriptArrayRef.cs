using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly ref struct JavaScriptArrayRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope? scope;
  private readonly ExpoJsiValueHandle valueHandle;

  internal JavaScriptArrayRef(
      JsiContext context,
      JsiRefScope scope,
      ExpoJsiValueHandle valueHandle
  )
  {
    this.context = context;
    this.scope = scope;
    this.valueHandle = valueHandle;
  }

  private ExpoJsiValueRef NativeRef
  {
    get
    {
      if (scope is null || valueHandle == 0)
      {
        throw new ObjectDisposedException(nameof(JsiRefScope));
      }
      return new ExpoJsiValueRef(scope!.Handle, valueHandle);
    }
  }

  public uint Length
  {
    get
    {
      unsafe
      {
        return context.Api->GetValueRefArrayLength(context.RuntimeHandle, NativeRef);
      }
    }
  }

  public JavaScriptValueRef GetValue(uint index)
  {
    unsafe
    {
      var result = context.Api->GetValueRefAtIndex(context.RuntimeHandle, NativeRef, index);
      if (result.Ok == 0 || result.Value.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array ref value.");
      }
      return new JavaScriptValueRef(context, scope!, result.Value.Value);
    }
  }

  public JavaScriptArray Retain()
  {
    unsafe
    {
      var result = context.Api->RetainValueRefArray(context.RuntimeHandle, NativeRef);
      if (result.Ok == 0 || result.Array == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript array ref.");
      }
      return new JavaScriptArray(context, result.Array);
    }
  }

  public JavaScriptValue RetainAsValue() => new JavaScriptValueRef(context, scope!, valueHandle).Retain();
}
