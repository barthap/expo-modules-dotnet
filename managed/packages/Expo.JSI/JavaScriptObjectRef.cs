using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly ref struct JavaScriptObjectRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope? scope;
  private readonly ExpoJsiValueHandle valueHandle;

  internal JavaScriptObjectRef(
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

  public JavaScriptValueRef GetProperty(string name)
  {
    ArgumentNullException.ThrowIfNull(name);

    var nameBytes = Encoding.UTF8.GetBytes(name);
    unsafe
    {
      var result = context.Api->GetValueRefProperty(
          context.RuntimeHandle,
          NativeRef,
          nameBytes
      );
      if (result.Ok == 0 || result.Value.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript object ref property.");
      }
      return new JavaScriptValueRef(context, scope!, result.Value.Value);
    }
  }

  public JavaScriptObject Retain()
  {
    unsafe
    {
      var result = context.Api->RetainValueRefObject(context.RuntimeHandle, NativeRef);
      if (result.Ok == 0 || result.Object == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript object ref.");
      }
      return new JavaScriptObject(context, result.Object);
    }
  }

  public JavaScriptValue RetainAsValue() => new JavaScriptValueRef(context, scope!, valueHandle).Retain();
}
