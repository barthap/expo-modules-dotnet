using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed unsafe class JavaScriptRuntime
{
    private readonly JsiContext context;

    internal JavaScriptRuntime(ExpoJsiApi* api, ExpoJsiRuntimeHandle runtimeHandle)
        : this(new JsiContext(api, runtimeHandle))
    {
    }

    internal JavaScriptRuntime(JsiContext context)
    {
        this.context = context;
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
        var result = context.Api->CreateNumberValue(context.RuntimeHandle, value);
        if (result.Ok == 0 || result.Value == 0)
        {
            JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript number.");
        }
        return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }

    public JavaScriptValue CreateBool(bool value)
    {
        var result = context.Api->CreateBoolValue(context.RuntimeHandle, value);
        if (result.Ok == 0 || result.Value == 0)
        {
            JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript boolean.");
        }
        return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }

    public JavaScriptObject Global()
    {
        var result = context.Api->GetGlobal(context.RuntimeHandle);
        if (result.Ok == 0 || result.Object == 0)
        {
            JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript global object.");
        }
        return new JavaScriptObject(context, result.Object);
    }

    public JavaScriptObject CreateObject()
    {
        var result = context.Api->CreateObjectValue(context.RuntimeHandle);
        if (result.Ok == 0 || result.Object == 0)
        {
            JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript object.");
        }
        return new JavaScriptObject(context, result.Object);
    }

    public JavaScriptFunction CreateHostFunction(
        string name,
        uint parameterCount,
        JavaScriptHostFunction callback,
        object callbackState
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(callbackState);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var callbackContext = new HostFunctionContext(context.Api, callback, callbackState).ToIntPtr();

        var result = context.Api->CreateHostFunctionValue(
            context.RuntimeHandle,
            nameBytes,
            parameterCount,
            &InvokeHostFunction,
            callbackContext,
            &ReleaseHostFunctionContext
        );

        if (result.Ok == 0 || result.Function == 0)
        {
            HostFunctionContext.Release(callbackContext);
            JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
        }

        return new JavaScriptFunction(context, result.Function);
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
            var jsiContext = new JsiContext(context.Api, runtimeHandle);
            var runtime = new JavaScriptRuntime(jsiContext);
            var thisValue = new JavaScriptBorrowedValue(jsiContext, thisValueHandle);
            var arguments = new JavaScriptArguments(jsiContext, argumentsHandle);
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
}
