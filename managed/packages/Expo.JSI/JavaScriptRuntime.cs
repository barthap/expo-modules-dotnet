using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed unsafe class JavaScriptRuntime
{
  private readonly ExpoJsiApi* api;
  private readonly nint runtimeHandle;

  private JavaScriptRuntime(ExpoJsiApi* api, nint runtimeHandle)
  {
    this.api = api;
    this.runtimeHandle = runtimeHandle;
  }

  public static JavaScriptRuntime FromNative(nint api, nint runtimeHandle)
  {
    if (api == 0) {
      throw new ArgumentNullException(nameof(api));
    }
    if (runtimeHandle == 0) {
      throw new ArgumentNullException(nameof(runtimeHandle));
    }

    var nativeApi = (ExpoJsiApi*)api;
    if (nativeApi->Size < ExpoJsiApi.ExpectedSize) {
      throw new InvalidOperationException(
        $"Expo JSI API table is too small. Expected at least {ExpoJsiApi.ExpectedSize}, got {nativeApi->Size}.");
    }
    if (nativeApi->Version != ExpoJsiApi.ExpectedVersion) {
      throw new InvalidOperationException(
        $"Unsupported Expo JSI API version {nativeApi->Version}.");
    }
    if (nativeApi->CreateNumber is null ||
        nativeApi->GetValueKind is null ||
        nativeApi->GetDouble is null ||
        nativeApi->ReleaseValue is null) {
      throw new InvalidOperationException("Expo JSI API table is missing required functions.");
    }

    return new JavaScriptRuntime(nativeApi, runtimeHandle);
  }

  public JavaScriptValue CreateNumber(double value)
  {
    var result = api->CreateNumber(runtimeHandle, value);
    if (result.Ok == 0 || result.Value == 0) {
      ThrowNativeError(result.Error, "Failed to create JavaScript number.");
    }
    return JavaScriptValue.FromOwnedHandle(this, result.Value);
  }

  public JavaScriptValue BorrowValue(nint valueHandle)
  {
    if (valueHandle == 0) {
      throw new ArgumentNullException(nameof(valueHandle));
    }
    return JavaScriptValue.FromBorrowedHandle(this, valueHandle);
  }

  internal JavaScriptValueKind GetValueKind(nint valueHandle)
  {
    ExpoJsiError error;
    var kind = api->GetValueKind(runtimeHandle, valueHandle, &error);
    ThrowIfError(error, "Failed to read JavaScript value kind.");
    return (JavaScriptValueKind)kind;
  }

  internal double GetDouble(nint valueHandle)
  {
    ExpoJsiError error;
    var value = api->GetDouble(runtimeHandle, valueHandle, &error);
    ThrowIfError(error, "Failed to read JavaScript number.");
    return value;
  }

  internal void ReleaseValue(nint valueHandle)
  {
    api->ReleaseValue(runtimeHandle, valueHandle);
  }

  private static void ThrowIfError(ExpoJsiError error, string fallback)
  {
    if (error.Code != 0) {
      ThrowNativeError(error, fallback);
    }
  }

  private static void ThrowNativeError(ExpoJsiError error, string fallback)
  {
    var message = error.GetMessage();
    if (string.IsNullOrEmpty(message)) {
      message = fallback;
    }
    throw new InvalidOperationException($"Native JSI error {error.Code}: {message}");
  }
}
