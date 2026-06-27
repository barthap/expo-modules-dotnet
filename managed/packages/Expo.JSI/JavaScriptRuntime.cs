using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed unsafe class JavaScriptRuntime
{
    private readonly ExpoJsiApi* api;
    private readonly ExpoJsiRuntimeHandle runtimeHandle;

    private JavaScriptRuntime(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
    {
        this.api = api;
        this.runtimeHandle = runtimeHandle;
    }

    public static JavaScriptRuntime FromNative(
        ExpoJsiApiHandle api,
        ExpoJsiRuntimeHandle runtimeHandle
    )
    {
        if (api == 0)
        {
            throw new ArgumentNullException(nameof(api));
        }
        if (runtimeHandle == 0)
        {
            throw new ArgumentNullException(nameof(runtimeHandle));
        }

        var nativeApi = (ExpoJsiApi*)api;
        nativeApi->Validate();

        return new JavaScriptRuntime(nativeApi, runtimeHandle);
    }

    public JavaScriptValue CreateNumber(double value)
    {
        var result = api->CreateNumberValue(runtimeHandle, value);
        if (result.Ok == 0 || result.Value == 0)
        {
            ThrowNativeError(result.Error, "Failed to create JavaScript number.");
        }
        return JavaScriptValue.FromOwnedHandle(this, result.Value);
    }

    public JavaScriptValue BorrowValue(ExpoJsiValueHandle valueHandle)
    {
        if (valueHandle == 0)
        {
            throw new ArgumentNullException(nameof(valueHandle));
        }
        return JavaScriptValue.FromBorrowedHandle(this, valueHandle);
    }

    internal JavaScriptValueKind GetValueKind(ExpoJsiValueHandle valueHandle)
    {
        ExpoJsiError error;
        var kind = api->GetKind(runtimeHandle, valueHandle, &error);
        ThrowIfError(error, "Failed to read JavaScript value kind.");
        return (JavaScriptValueKind)kind;
    }

    internal double GetDouble(ExpoJsiValueHandle valueHandle)
    {
        ExpoJsiError error;
        var value = api->ReadDouble(runtimeHandle, valueHandle, &error);
        ThrowIfError(error, "Failed to read JavaScript number.");
        return value;
    }

    internal void ReleaseValue(ExpoJsiValueHandle valueHandle)
    {
        api->ReleaseValueHandle(runtimeHandle, valueHandle);
    }

    private static void ThrowIfError(ExpoJsiError error, string fallback)
    {
        if (error.Code != 0)
        {
            ThrowNativeError(error, fallback);
        }
    }

    private static void ThrowNativeError(ExpoJsiError error, string fallback)
    {
        var message = error.GetMessage();
        if (string.IsNullOrEmpty(message))
        {
            message = fallback;
        }
        throw new InvalidOperationException($"Native JSI error {error.Code}: {message}");
    }
}
