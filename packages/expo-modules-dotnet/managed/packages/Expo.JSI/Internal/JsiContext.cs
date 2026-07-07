using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal readonly unsafe struct JsiContext
{
  public JsiContext(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
      : this(api, runtimeHandle, new NativeStateRegistry())
  {
  }

  private JsiContext(
      ExpoJsiApi* api,
      ExpoJsiRuntimeHandle runtimeHandle,
      NativeStateRegistry nativeStates)
  {
    Api = api;
    RuntimeHandle = runtimeHandle;
    NativeStates = nativeStates;
  }

  public ExpoJsiApi* Api { get; }
  public ExpoJsiRuntimeHandle RuntimeHandle { get; }
  public NativeStateRegistry NativeStates { get; }

  public void ThrowIfError(ExpoJsiError error, string fallback)
  {
    if (error.Code != 0)
    {
      ThrowNativeError(error, fallback);
    }
  }

  public static void ThrowNativeError(ExpoJsiError error, string fallback)
  {
    var message = error.GetMessageAndRelease();
    if (string.IsNullOrEmpty(message))
    {
      message = fallback;
    }
    throw new InvalidOperationException($"Native JSI error {error.Code}: {message}");
  }
}
