using System.Runtime.InteropServices;

namespace Expo.JSI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiApi
{
    public readonly uint Size;
    public readonly uint Version;

    /// <summary>
    /// Native function pointer for creating an owned JavaScript number value.
    /// Signature: (runtimeHandle, value) => result.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        double,
        ExpoJsiValueResult> CreateNumber;

    /// <summary>
    /// Native function pointer for creating an owned JavaScript boolean value.
    /// Signature: (runtimeHandle, value) => result.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        byte,
        ExpoJsiValueResult> CreateBool;

    /// <summary>
    /// Native function pointer for getting the kind of a JavaScript value.
    /// Signature: (runtimeHandle, valueHandle, error) => kind.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiValueHandle,
        ExpoJsiError*,
        ExpoJsiValueKind> GetValueKind;

    /// <summary>
    /// Native function pointer for reading a JavaScript boolean value.
    /// Signature: (runtimeHandle, valueHandle, error) => boolean byte.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiValueHandle,
        ExpoJsiError*,
        byte> GetBool;

    /// <summary>
    /// Native function pointer for reading a JavaScript number value.
    /// Signature: (runtimeHandle, valueHandle, error) => value.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiValueHandle,
        ExpoJsiError*,
        double> GetDouble;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiObjectResult> GetGlobalObject;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiObjectResult> CreateObject;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiObjectHandle,
        ExpoJsiValueResult> ObjectAsValue;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiValueHandle,
        ExpoJsiObjectResult> ValueAsObject;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiObjectHandle,
        byte*,
        int,
        ExpoJsiValueHandle,
        ExpoJsiError> ObjectSetProperty;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        byte*,
        int,
        uint,
        delegate* unmanaged[Cdecl]<
            nint,
            ExpoJsiRuntimeHandle,
            ExpoJsiValueHandle,
            ExpoJsiArgumentsHandle,
            ExpoJsiValueResult>,
        nint,
        delegate* unmanaged[Cdecl]<nint, void>,
        ExpoJsiFunctionResult> CreateHostFunction;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiFunctionHandle,
        ExpoJsiValueResult> FunctionAsValue;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiArgumentsHandle,
        ExpoJsiError*,
        uint> GetArgumentsCount;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiArgumentsHandle,
        uint,
        ExpoJsiValueResult> GetArgumentValue;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiObjectHandle,
        void> ReleaseObject;

    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiFunctionHandle,
        void> ReleaseFunction;

    /// <summary>
    /// Native function pointer for releasing an owned JavaScript value handle.
    /// Signature: (runtimeHandle, valueHandle) => void.
    /// </summary>
    private readonly delegate* unmanaged[Cdecl]<
        ExpoJsiRuntimeHandle,
        ExpoJsiValueHandle,
        void> ReleaseValue;

    /// <summary>
    /// Validates if everything is in place.
    /// </summary>
    internal void Validate()
    {
        if (this.Size < ExpoJsiApi.ExpectedSize)
        {
            throw new InvalidOperationException(
                $"Expo JSI API table is too small. Expected at least {ExpoJsiApi.ExpectedSize}, got {this.Size}."
            );
        }
        if (this.Version != ExpoJsiApi.ExpectedVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported Expo JSI API version {this.Version}."
            );
        }
        if (
            this.CreateNumber is null
            || this.CreateBool is null
            || this.GetValueKind is null
            || this.GetBool is null
            || this.GetDouble is null
            || this.GetGlobalObject is null
            || this.CreateObject is null
            || this.ObjectAsValue is null
            || this.ValueAsObject is null
            || this.ObjectSetProperty is null
            || this.CreateHostFunction is null
            || this.FunctionAsValue is null
            || this.GetArgumentsCount is null
            || this.GetArgumentValue is null
            || this.ReleaseObject is null
            || this.ReleaseFunction is null
            || this.ReleaseValue is null
        )
        {
            throw new InvalidOperationException(
                "Expo JSI API table is missing required functions."
            );
        }
    }

    /// <summary>
    /// Creates an owned JavaScript number value through the native API table.
    /// </summary>
    /// <param name="runtimeHandle">Opaque expo_jsi_runtime_handle.</param>
    /// <param name="value">The numeric value to create.</param>
    public ExpoJsiValueResult CreateNumberValue(ExpoJsiRuntimeHandle runtimeHandle, double value)
    {
        return CreateNumber(runtimeHandle, value);
    }

    public ExpoJsiValueResult CreateBoolValue(ExpoJsiRuntimeHandle runtimeHandle, bool value)
    {
        return CreateBool(runtimeHandle, value ? (byte)1 : (byte)0);
    }

    /// <summary>
    /// Gets the kind of a JavaScript value through the native API table.
    /// </summary>
    /// <param name="runtimeHandle">Opaque expo_jsi_runtime_handle.</param>
    /// <param name="valueHandle">Opaque expo_jsi_value_handle.</param>
    /// <param name="error">Receives structured error details.</param>
    public ExpoJsiValueKind GetKind(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle valueHandle,
        ExpoJsiError* error
    )
    {
        return GetValueKind(runtimeHandle, valueHandle, error);
    }

    /// <summary>
    /// Reads a JavaScript boolean value through the native API table.
    /// </summary>
    /// <param name="runtimeHandle">Opaque expo_jsi_runtime_handle.</param>
    /// <param name="valueHandle">Opaque expo_jsi_value_handle.</param>
    /// <param name="error">Receives structured error details.</param>
    public bool ReadBool(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle valueHandle,
        ExpoJsiError* error
    )
    {
        // Native bool payloads are ABI bytes. The error parameter carries failure state.
        return GetBool(runtimeHandle, valueHandle, error) != 0;
    }

    /// <summary>
    /// Reads a JavaScript number value through the native API table.
    /// </summary>
    /// <param name="runtimeHandle">Opaque expo_jsi_runtime_handle.</param>
    /// <param name="valueHandle">Opaque expo_jsi_value_handle.</param>
    /// <param name="error">Receives structured error details.</param>
    public double ReadDouble(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle valueHandle,
        ExpoJsiError* error
    )
    {
        return GetDouble(runtimeHandle, valueHandle, error);
    }

    public ExpoJsiObjectResult GetGlobal(ExpoJsiRuntimeHandle runtimeHandle)
    {
        return GetGlobalObject(runtimeHandle);
    }

    public ExpoJsiObjectResult CreateObjectValue(ExpoJsiRuntimeHandle runtimeHandle)
    {
        return CreateObject(runtimeHandle);
    }

    public ExpoJsiValueResult ConvertObjectToValue(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiObjectHandle objectHandle
    )
    {
        return ObjectAsValue(runtimeHandle, objectHandle);
    }

    public ExpoJsiObjectResult ConvertValueToObject(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle valueHandle
    )
    {
        return ValueAsObject(runtimeHandle, valueHandle);
    }

    public ExpoJsiError SetObjectProperty(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiObjectHandle objectHandle,
        ReadOnlySpan<byte> name,
        ExpoJsiValueHandle valueHandle
    )
    {
        fixed (byte* namePtr = name)
        {
            return ObjectSetProperty(
                runtimeHandle,
                objectHandle,
                namePtr,
                name.Length,
                valueHandle
            );
        }
    }

    public ExpoJsiFunctionResult CreateHostFunctionValue(
        ExpoJsiRuntimeHandle runtimeHandle,
        ReadOnlySpan<byte> name,
        uint parameterCount,
        delegate* unmanaged[Cdecl]<
            nint,
            ExpoJsiRuntimeHandle,
            ExpoJsiValueHandle,
            ExpoJsiArgumentsHandle,
            ExpoJsiValueResult> callback,
        nint callbackContext,
        delegate* unmanaged[Cdecl]<nint, void> releaseCallbackContext
    )
    {
        fixed (byte* namePtr = name)
        {
            return CreateHostFunction(
                runtimeHandle,
                namePtr,
                name.Length,
                parameterCount,
                callback,
                callbackContext,
                releaseCallbackContext
            );
        }
    }

    public ExpoJsiValueResult ConvertFunctionToValue(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiFunctionHandle functionHandle
    )
    {
        return FunctionAsValue(runtimeHandle, functionHandle);
    }

    public uint GetArgumentCount(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiArgumentsHandle argumentsHandle,
        ExpoJsiError* error
    )
    {
        return GetArgumentsCount(runtimeHandle, argumentsHandle, error);
    }

    public ExpoJsiValueResult GetArgument(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiArgumentsHandle argumentsHandle,
        uint index
    )
    {
        return GetArgumentValue(runtimeHandle, argumentsHandle, index);
    }

    public void ReleaseObjectHandle(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiObjectHandle objectHandle
    )
    {
        ReleaseObject(runtimeHandle, objectHandle);
    }

    public void ReleaseFunctionHandle(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiFunctionHandle functionHandle
    )
    {
        ReleaseFunction(runtimeHandle, functionHandle);
    }

    /// <summary>
    /// Releases an owned JavaScript value handle through the native API table.
    /// </summary>
    /// <param name="runtimeHandle">Opaque expo_jsi_runtime_handle.</param>
    /// <param name="valueHandle">Opaque expo_jsi_value_handle.</param>
    public void ReleaseValueHandle(
        ExpoJsiRuntimeHandle runtimeHandle,
        ExpoJsiValueHandle valueHandle
    )
    {
        ReleaseValue(runtimeHandle, valueHandle);
    }

    public static uint ExpectedSize => (uint)sizeof(ExpoJsiApi);
    public const uint ExpectedVersion = 2;
}
