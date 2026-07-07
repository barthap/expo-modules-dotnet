using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI;
using Expo.JSI.Internal;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostFunctionContext
{
  private byte* lastErrorMessage;
  private int lastErrorMessageLength;

  public HostFunctionContext(
      JsiContext jsiContext,
      JavaScriptHostFunction callback,
      object context
  )
  {
    JsiContext = jsiContext;
    Callback = callback;
    Context = context;
  }

  public JsiContext JsiContext { get; }
  public JavaScriptHostFunction Callback { get; }
  public object Context { get; }

  public ExpoJsiError CaptureException(Exception exception)
  {
    var message = exception.ToString();
    if (string.IsNullOrEmpty(message))
    {
      message = exception.GetType().FullName ?? exception.GetType().Name;
    }

    ReleaseLastErrorMessage();

    var length = Encoding.UTF8.GetByteCount(message);
    if (length == 0)
    {
      lastErrorMessageLength = 0;
      return new ExpoJsiError(100, null, 0, 0, null);
    }

    lastErrorMessage = (byte*)NativeMemory.Alloc((nuint)length);
    lastErrorMessageLength = length;
    Encoding.UTF8.GetBytes(message, new Span<byte>(lastErrorMessage, lastErrorMessageLength));
    return new ExpoJsiError(100, lastErrorMessage, lastErrorMessageLength, 0, null);
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
