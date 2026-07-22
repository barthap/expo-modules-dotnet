using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI;
using Expo.JSI.Internal;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostFunctionContext
{
  private static readonly ConcurrentDictionary<nint, HostFunctionContext> activeContexts = new();
  private static long nextContextId;

  private byte* lastErrorMessage;
  private int lastErrorMessageLength;
  private readonly Action<object>? disposeCallbackState;
  private int callbackStateDisposed;

  public HostFunctionContext(
      JsiContext jsiContext,
      JavaScriptHostFunction callback,
      object context,
      Action<object>? disposeCallbackState
  )
  {
    JsiContext = jsiContext;
    Callback = callback;
    Context = context;
    this.disposeCallbackState = disposeCallbackState;
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
    while (true)
    {
      var pointer = unchecked((nint)Interlocked.Increment(ref nextContextId));
      if (pointer != 0 && activeContexts.TryAdd(pointer, this))
      {
        return pointer;
      }
    }
  }

  public static HostFunctionContext FromIntPtr(nint pointer)
  {
    if (
      pointer == 0
      || !activeContexts.TryGetValue(pointer, out var context)
    )
    {
      throw new ObjectDisposedException(nameof(HostFunctionContext));
    }
    return context;
  }

  public static void Release(nint pointer)
  {
    if (pointer == 0 || !activeContexts.TryRemove(pointer, out var context))
    {
      return;
    }
    context.DisposeCallbackState();
    context.ReleaseLastErrorMessage();
  }

  public static void ReportException(Exception exception)
  {
    try
    {
      Console.Error.WriteLine(exception);
    }
    catch
    {
      // Reporting is best-effort because release may run across an unmanaged boundary.
    }
  }

  private void DisposeCallbackState()
  {
    if (
      disposeCallbackState is null
      || Interlocked.Exchange(ref callbackStateDisposed, 1) != 0
    )
    {
      return;
    }

    try
    {
      disposeCallbackState(Context);
    }
    catch (Exception ex)
    {
      ReportException(ex);
    }
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
