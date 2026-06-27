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
            || this.GetValueKind is null
            || this.GetBool is null
            || this.GetDouble is null
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
    public const uint ExpectedVersion = 1;
}
