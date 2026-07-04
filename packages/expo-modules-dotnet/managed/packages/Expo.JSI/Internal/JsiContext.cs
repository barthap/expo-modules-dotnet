using Expo.JSI.Interop;

namespace Expo.JSI.Internal;

internal readonly unsafe struct JsiContext
{
  public JsiContext(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
  {
    Api = api;
    RuntimeHandle = runtimeHandle;
  }

  public ExpoJsiApi* Api { get; }
  public ExpoJsiRuntimeHandle RuntimeHandle { get; }

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
