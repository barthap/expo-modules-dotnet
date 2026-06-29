using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal readonly unsafe struct JavaScriptObjectInner
{
  public JavaScriptObjectInner(JsiContext context, ExpoJsiValueHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiValueHandle Handle { get; }

  public void SetProperty(string name, ExpoJsiValueHandle value)
  {
    ArgumentNullException.ThrowIfNull(name);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var error = Context.Api->SetObjectProperty(
        Context.RuntimeHandle,
        Handle,
        nameBytes,
        value
    );
    Context.ThrowIfError(error, "Failed to set JavaScript object property.");
  }

  public ExpoJsiValueHandle GetProperty(string name)
  {
    ArgumentNullException.ThrowIfNull(name);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var result = Context.Api->GetObjectProperty(
        Context.RuntimeHandle,
        Handle,
        nameBytes
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript object property.");
    }
    return result.Value;
  }

  public ExpoJsiValueHandle AsValue()
  {
    var result = Context.Api->RetainValueAs(
        Context.RuntimeHandle,
        Handle,
        ExpoJsiValueExpectation.Object
    );
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript object value.");
    }
    return result.Value;
  }
}
