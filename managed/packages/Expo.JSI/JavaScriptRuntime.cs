using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed unsafe class JavaScriptRuntime
{
    private readonly ExpoJsiApi* api;
    private readonly ExpoJsiRuntimeHandle runtimeHandle;

    internal JavaScriptRuntime(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
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

    public JavaScriptValue CreateBool(bool value)
    {
        var result = api->CreateBoolValue(runtimeHandle, value);
        if (result.Ok == 0 || result.Value == 0)
        {
            ThrowNativeError(result.Error, "Failed to create JavaScript boolean.");
        }
        return JavaScriptValue.FromOwnedHandle(this, result.Value);
    }

    public JavaScriptObject Global()
    {
        var result = api->GetGlobal(runtimeHandle);
        if (result.Ok == 0 || result.Object == 0)
        {
            ThrowNativeError(result.Error, "Failed to get JavaScript global object.");
        }
        return new JavaScriptObject(this, result.Object);
    }

    public JavaScriptObject CreateObject()
    {
        var result = api->CreateObjectValue(runtimeHandle);
        if (result.Ok == 0 || result.Object == 0)
        {
            ThrowNativeError(result.Error, "Failed to create JavaScript object.");
        }
        return new JavaScriptObject(this, result.Object);
    }

    public JavaScriptFunction CreateHostFunction(
        string name,
        uint parameterCount,
        JavaScriptHostFunction callback,
        object context
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(context);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var callbackContext = new HostFunctionContext(api, callback, context).ToIntPtr();

        var result = api->CreateHostFunctionValue(
            runtimeHandle,
            nameBytes,
            parameterCount,
            &InvokeHostFunction,
            callbackContext,
            &ReleaseHostFunctionContext
        );

        if (result.Ok == 0 || result.Function == 0)
        {
            HostFunctionContext.Release(callbackContext);
            ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
        }

        return new JavaScriptFunction(this, result.Function);
    }

    internal JavaScriptValue ObjectAsValue(ExpoJsiObjectHandle objectHandle)
    {
        var result = api->ConvertObjectToValue(runtimeHandle, objectHandle);
        if (result.Ok == 0 || result.Value == 0)
        {
            ThrowNativeError(result.Error, "Failed to convert JavaScript object to value.");
        }
        return JavaScriptValue.FromOwnedHandle(this, result.Value);
    }

    internal JavaScriptValue FunctionAsValue(ExpoJsiFunctionHandle functionHandle)
    {
        var result = api->ConvertFunctionToValue(runtimeHandle, functionHandle);
        if (result.Ok == 0 || result.Value == 0)
        {
            ThrowNativeError(result.Error, "Failed to convert JavaScript function to value.");
        }
        return JavaScriptValue.FromOwnedHandle(this, result.Value);
    }

    internal void SetObjectProperty(
        ExpoJsiObjectHandle objectHandle,
        ReadOnlySpan<byte> name,
        ExpoJsiValueHandle valueHandle
    )
    {
        var error = api->SetObjectProperty(runtimeHandle, objectHandle, name, valueHandle);
        ThrowIfError(error, "Failed to set JavaScript object property.");
    }

    internal uint GetArgumentsCount(ExpoJsiArgumentsHandle argumentsHandle)
    {
        ExpoJsiError error;
        var count = api->GetArgumentCount(runtimeHandle, argumentsHandle, &error);
        ThrowIfError(error, "Failed to read JavaScript argument count.");
        return count;
    }

    internal JavaScriptBorrowedValue GetBorrowedArgument(
        ExpoJsiArgumentsHandle argumentsHandle,
        uint index
    )
    {
        var result = api->GetArgument(runtimeHandle, argumentsHandle, index);
        if (result.Ok == 0 || result.Value == 0)
        {
            ThrowNativeError(result.Error, "Failed to read JavaScript argument.");
        }
        return new JavaScriptBorrowedValue(this, result.Value);
    }

    internal JavaScriptValueKind GetValueKind(ExpoJsiValueHandle valueHandle)
    {
        ExpoJsiError error;
        var kind = api->GetKind(runtimeHandle, valueHandle, &error);
        ThrowIfError(error, "Failed to read JavaScript value kind.");
        return (JavaScriptValueKind)kind;
    }

    internal bool GetBool(ExpoJsiValueHandle valueHandle)
    {
        ExpoJsiError error;
        var value = api->ReadBool(runtimeHandle, valueHandle, &error);
        ThrowIfError(error, "Failed to read JavaScript boolean.");
        return value;
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

    internal void ReleaseObject(ExpoJsiObjectHandle objectHandle)
    {
        api->ReleaseObjectHandle(runtimeHandle, objectHandle);
    }

    internal void ReleaseFunction(ExpoJsiFunctionHandle functionHandle)
    {
        api->ReleaseFunctionHandle(runtimeHandle, functionHandle);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ExpoJsiValueResult InvokeHostFunction(
        nint callbackContext,
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle thisValueHandle,
        ExpoJsiArgumentsHandle argumentsHandle
    )
    {
        try
        {
            var context = HostFunctionContext.FromIntPtr(callbackContext);
            var runtime = new JavaScriptRuntime(context.Api, runtimeHandle);
            var thisValue = new JavaScriptBorrowedValue(runtime, thisValueHandle);
            var arguments = new JavaScriptArguments(runtime, argumentsHandle);
            using var result = context.Callback(runtime, thisValue, arguments, context.Context);
            return new ExpoJsiValueResult(1, result.Detach(), default);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return new ExpoJsiValueResult(0, 0, default);
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ReleaseHostFunctionContext(nint callbackContext)
    {
        HostFunctionContext.Release(callbackContext);
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
