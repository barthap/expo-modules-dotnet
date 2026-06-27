using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostFunctionContext
{
  private byte* lastErrorMessage;
  private int lastErrorMessageLength;

  public HostFunctionContext(
      ExpoJsiApi* api,
      JavaScriptHostFunction callback,
      object context
  )
  {
    Api = api;
    Callback = callback;
    Context = context;
  }

  public ExpoJsiApi* Api { get; }
  public JavaScriptHostFunction Callback { get; }
  public object Context { get; }

  public ExpoJsiError CaptureException(Exception exception)
  {
    var message = exception.Message;
    if (string.IsNullOrEmpty(message))
    {
      message = exception.GetType().FullName ?? exception.GetType().Name;
    }

    ReleaseLastErrorMessage();

    var length = Encoding.UTF8.GetByteCount(message);
    if (length == 0)
    {
      lastErrorMessageLength = 0;
      return new ExpoJsiError(100, null, 0);
    }

    lastErrorMessage = (byte*)NativeMemory.Alloc((nuint)length);
    lastErrorMessageLength = length;
    Encoding.UTF8.GetBytes(message, new Span<byte>(lastErrorMessage, lastErrorMessageLength));
    return new ExpoJsiError(100, lastErrorMessage, lastErrorMessageLength);
  }

  public nint ToIntPtr()
  {
    return GCHandle.ToIntPtr(GCHandle.Alloc(this));
  }

  public static HostFunctionContext FromIntPtr(nint pointer)
  {
    return (HostFunctionContext)GCHandle.FromIntPtr(pointer).Target!;
  }

  public static void Release(nint pointer)
  {
    if (pointer == 0)
    {
      return;
    }
    var handle = GCHandle.FromIntPtr(pointer);
    if (handle.Target is HostFunctionContext context)
    {
      context.ReleaseLastErrorMessage();
    }
    handle.Free();
  }

  private void ReleaseLastErrorMessage()
  {
    if (lastErrorMessage != null)
    {
      NativeMemory.Free(lastErrorMessage);
      lastErrorMessage = null;
      lastErrorMessageLength = 0;
    }
  }
}
