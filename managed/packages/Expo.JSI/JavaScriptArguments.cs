using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly struct JavaScriptArguments
{
  private readonly JsiContext context;
  private readonly ExpoJsiArgumentsHandle handle;

  internal JavaScriptArguments(JsiContext context, ExpoJsiArgumentsHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public uint Count
  {
    get
    {
      ThrowIfNull();
      unsafe
      {
        ExpoJsiError error;
        var count = context.Api->GetArgumentCount(context.RuntimeHandle, handle, &error);
        context.ThrowIfError(error, "Failed to read JavaScript argument count.");
        return count;
      }
    }
  }

  public JavaScriptValueRef GetValue(uint index)
  {
    ThrowIfNull();
    unsafe
    {
      var result = context.Api->GetArgument(context.RuntimeHandle, handle, index);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to read JavaScript argument.");
      }
      var scope = JsiRefScope.CurrentFor(context);
      return new JavaScriptValueRef(context, scope, result.Value);
    }
  }

  private void ThrowIfNull()
  {
    if (handle == 0)
    {
      throw new ObjectDisposedException(nameof(JavaScriptArguments));
    }
  }
}
