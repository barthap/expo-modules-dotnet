using System.Runtime.InteropServices;
using System.Text;

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
    uint,
    ExpoJsiArrayResult> CreateArray;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiArrayHandle,
    ExpoJsiValueResult> ArrayAsValue;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiArrayHandle,
    ExpoJsiObjectResult> ArrayAsObject;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiArrayResult> ValueAsArray;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiArrayHandle,
    ExpoJsiError*,
    uint> ArrayGetLength;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiArrayHandle,
    uint,
    ExpoJsiValueResult> ArrayGetValueAtIndex;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiArrayHandle,
    uint,
    ExpoJsiValueHandle,
    ExpoJsiError> ArraySetValueAtIndex;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiPromiseResult> CreatePromise;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiPromiseHandle,
    ExpoJsiValueResult> PromiseAsValue;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiPromiseHandle,
    ExpoJsiValueHandle,
    ExpoJsiError> PromiseResolve;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiPromiseHandle,
    ExpoJsiValueHandle,
    ExpoJsiError> PromiseReject;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiObjectHandle,
    byte*,
    int,
    ExpoJsiValueHandle,
    ExpoJsiError> ObjectSetProperty;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiObjectHandle,
    byte*,
    int,
    ExpoJsiValueResult> ObjectGetProperty;

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
    ExpoJsiArrayHandle,
    void> ReleaseArray;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiPromiseHandle,
    void> ReleasePromise;

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

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    byte*,
    int,
    ExpoJsiValueResult> CreateString;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueResult> CloneValue;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    byte*,
    int,
    ExpoJsiValueResult> CreateError;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiStringResult> GetString;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiTaskPriority,
    delegate* unmanaged[Cdecl]<nint, void>,
    nint,
    delegate* unmanaged[Cdecl]<nint, void>,
    ExpoJsiError> RuntimeScheduleTask;

  private readonly delegate* unmanaged[Cdecl]<ExpoJsiRuntimeHandle, byte> RuntimeCanExecuteSync;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    delegate* unmanaged[Cdecl]<nint, void>,
    nint,
    delegate* unmanaged[Cdecl]<nint, void>,
    ExpoJsiError> RuntimeExecuteSync;

  private readonly delegate* unmanaged[Cdecl]<ExpoJsiRuntimeHandle, ExpoJsiError> RuntimeDrainTasks;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiError*,
    byte> IsPromise;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiError*,
    byte> IsError;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiStringResult> CoerceToString;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiRefScopeHandle> CreateRefScopeFunction;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiRefScopeHandle,
    void> ReleaseRefScopeFunction;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiError*,
    ExpoJsiValueKind> ValueRefGetKind;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiError*,
    byte> ValueRefGetBool;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiError*,
    double> ValueRefGetDouble;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiStringResult> ValueRefGetString;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiStringResult> ValueRefCoerceToString;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    byte*,
    int,
    ExpoJsiValueRefResult> ValueRefGetProperty;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    uint,
    ExpoJsiValueRefResult> ValueRefGetValueAtIndex;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiError*,
    uint> ValueRefGetArrayLength;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiValueResult> ValueRefRetain;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiObjectResult> ValueRefRetainObject;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueRef,
    ExpoJsiArrayResult> ValueRefRetainArray;

  private static readonly UTF8Encoding StrictUtf8 = new(
    encoderShouldEmitUTF8Identifier: false,
    throwOnInvalidBytes: true
  );

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
      throw new InvalidOperationException($"Unsupported Expo JSI API version {this.Version}.");
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
      || this.CreateArray is null
      || this.ArrayAsValue is null
      || this.ArrayAsObject is null
      || this.ValueAsArray is null
      || this.ArrayGetLength is null
      || this.ArrayGetValueAtIndex is null
      || this.ArraySetValueAtIndex is null
      || this.CreatePromise is null
      || this.PromiseAsValue is null
      || this.PromiseResolve is null
      || this.PromiseReject is null
      || this.ObjectSetProperty is null
      || this.ObjectGetProperty is null
      || this.CreateHostFunction is null
      || this.FunctionAsValue is null
      || this.GetArgumentsCount is null
      || this.GetArgumentValue is null
      || this.ReleaseObject is null
      || this.ReleaseArray is null
      || this.ReleasePromise is null
      || this.ReleaseFunction is null
      || this.ReleaseValue is null
      || this.CreateString is null
      || this.CloneValue is null
      || this.CreateError is null
      || this.GetString is null
      || this.RuntimeScheduleTask is null
      || this.RuntimeCanExecuteSync is null
      || this.RuntimeExecuteSync is null
      || this.RuntimeDrainTasks is null
      || this.IsPromise is null
      || this.IsError is null
      || this.CoerceToString is null
      || this.CreateRefScopeFunction is null
      || this.ReleaseRefScopeFunction is null
      || this.ValueRefGetKind is null
      || this.ValueRefGetBool is null
      || this.ValueRefGetDouble is null
      || this.ValueRefGetString is null
      || this.ValueRefCoerceToString is null
      || this.ValueRefGetProperty is null
      || this.ValueRefGetValueAtIndex is null
      || this.ValueRefGetArrayLength is null
      || this.ValueRefRetain is null
      || this.ValueRefRetainObject is null
      || this.ValueRefRetainArray is null
    )
    {
      throw new InvalidOperationException("Expo JSI API table is missing required functions.");
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

  public ExpoJsiValueResult CreateStringValue(ExpoJsiRuntimeHandle runtimeHandle, string value)
  {
    var bytes = StrictUtf8.GetBytes(value);
    fixed (byte* bytesPtr = bytes)
    {
      return CreateString(runtimeHandle, bytesPtr, bytes.Length);
    }
  }

  public ExpoJsiValueResult CloneJavaScriptValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle valueHandle
  )
  {
    return CloneValue(runtimeHandle, valueHandle);
  }

  public ExpoJsiValueResult CreateErrorValue(ExpoJsiRuntimeHandle runtimeHandle, string message)
  {
    var bytes = StrictUtf8.GetBytes(message);
    fixed (byte* bytesPtr = bytes)
    {
      return CreateError(runtimeHandle, bytesPtr, bytes.Length);
    }
  }

  public bool IsPromiseValue(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueHandle valueHandle)
  {
    ExpoJsiError error;
    var result = IsPromise(runtimeHandle, valueHandle, &error);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to check JavaScript Promise value.");
    }
    return result != 0;
  }

  public bool IsErrorValue(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueHandle valueHandle)
  {
    ExpoJsiError error;
    var result = IsError(runtimeHandle, valueHandle, &error);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to check JavaScript Error object value.");
    }
    return result != 0;
  }

  public string CoerceJavaScriptValueToString(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle valueHandle
  )
  {
    var result = CoerceToString(runtimeHandle, valueHandle);
    return DecodeStringResult(result, "Failed to coerce JavaScript value to string.");
  }

  public ExpoJsiRefScopeHandle CreateRefScope(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return CreateRefScopeFunction(runtimeHandle);
  }

  public void ReleaseRefScope(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiRefScopeHandle refScopeHandle
  )
  {
    ReleaseRefScopeFunction(runtimeHandle, refScopeHandle);
  }

  public ExpoJsiValueKind GetValueRefKind(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef,
    ExpoJsiError* error
  )
  {
    return ValueRefGetKind(runtimeHandle, valueRef, error);
  }

  public bool ReadValueRefBool(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef)
  {
    ExpoJsiError error;
    var value = ValueRefGetBool(runtimeHandle, valueRef, &error) != 0;
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to read JavaScript boolean ref.");
    }
    return value;
  }

  public double ReadValueRefDouble(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef)
  {
    ExpoJsiError error;
    var value = ValueRefGetDouble(runtimeHandle, valueRef, &error);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to read JavaScript number ref.");
    }
    return value;
  }

  public string ReadValueRefString(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef)
  {
    var result = ValueRefGetString(runtimeHandle, valueRef);
    return DecodeStringResult(result, "Failed to read JavaScript string ref.");
  }

  public string CoerceValueRefToString(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef)
  {
    var result = ValueRefCoerceToString(runtimeHandle, valueRef);
    return DecodeStringResult(result, "Failed to coerce JavaScript value ref to string.");
  }

  public ExpoJsiValueRefResult GetValueRefProperty(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef,
    ReadOnlySpan<byte> name
  )
  {
    fixed (byte* namePtr = name)
    {
      return ValueRefGetProperty(runtimeHandle, valueRef, namePtr, name.Length);
    }
  }

  public ExpoJsiValueRefResult GetValueRefAtIndex(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef,
    uint index
  )
  {
    return ValueRefGetValueAtIndex(runtimeHandle, valueRef, index);
  }

  public uint GetValueRefArrayLength(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef
  )
  {
    ExpoJsiError error;
    var length = ValueRefGetArrayLength(runtimeHandle, valueRef, &error);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to read JavaScript array ref length.");
    }
    return length;
  }

  public ExpoJsiValueResult RetainValueRef(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef
  )
  {
    return ValueRefRetain(runtimeHandle, valueRef);
  }

  public ExpoJsiObjectResult RetainValueRefObject(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef
  )
  {
    return ValueRefRetainObject(runtimeHandle, valueRef);
  }

  public ExpoJsiArrayResult RetainValueRefArray(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueRef valueRef
  )
  {
    return ValueRefRetainArray(runtimeHandle, valueRef);
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

  public string ReadString(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueHandle valueHandle)
  {
    var result = GetString(runtimeHandle, valueHandle);
    return DecodeStringResult(result, "Failed to read JavaScript string.");
  }

  private static string DecodeStringResult(ExpoJsiStringResult result, string fallback)
  {
    if (result.Ok == 0)
    {
      ThrowNativeError(result.Error, fallback);
    }

    try
    {
      return StrictUtf8.GetString(new ReadOnlySpan<byte>(result.Data, result.Length));
    }
    finally
    {
      if (result.Release is not null)
      {
        result.Release(result.ReleaseContext);
      }
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

  public ExpoJsiArrayResult CreateArrayValue(ExpoJsiRuntimeHandle runtimeHandle, uint length)
  {
    return CreateArray(runtimeHandle, length);
  }

  public ExpoJsiValueResult ConvertArrayToValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArrayHandle arrayHandle
  )
  {
    return ArrayAsValue(runtimeHandle, arrayHandle);
  }

  public ExpoJsiObjectResult ConvertArrayToObject(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArrayHandle arrayHandle
  )
  {
    return ArrayAsObject(runtimeHandle, arrayHandle);
  }

  public ExpoJsiArrayResult ConvertValueToArray(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle valueHandle
  )
  {
    return ValueAsArray(runtimeHandle, valueHandle);
  }

  public uint GetArrayLength(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArrayHandle arrayHandle,
    ExpoJsiError* error
  )
  {
    return ArrayGetLength(runtimeHandle, arrayHandle, error);
  }

  public ExpoJsiValueResult GetArrayValueAtIndex(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArrayHandle arrayHandle,
    uint index
  )
  {
    return ArrayGetValueAtIndex(runtimeHandle, arrayHandle, index);
  }

  public ExpoJsiError SetArrayValueAtIndex(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArrayHandle arrayHandle,
    uint index,
    ExpoJsiValueHandle valueHandle
  )
  {
    return ArraySetValueAtIndex(runtimeHandle, arrayHandle, index, valueHandle);
  }

  public ExpoJsiPromiseResult CreatePromiseValue(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return CreatePromise(runtimeHandle);
  }

  public ExpoJsiValueResult ConvertPromiseToValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle
  )
  {
    return PromiseAsValue(runtimeHandle, promiseHandle);
  }

  public ExpoJsiError ResolvePromise(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle,
    ExpoJsiValueHandle valueHandle
  )
  {
    return PromiseResolve(runtimeHandle, promiseHandle, valueHandle);
  }

  public ExpoJsiError RejectPromise(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle,
    ExpoJsiValueHandle valueHandle
  )
  {
    return PromiseReject(runtimeHandle, promiseHandle, valueHandle);
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
      return ObjectSetProperty(runtimeHandle, objectHandle, namePtr, name.Length, valueHandle);
    }
  }

  public ExpoJsiValueResult GetObjectProperty(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiObjectHandle objectHandle,
    ReadOnlySpan<byte> name
  )
  {
    fixed (byte* namePtr = name)
    {
      return ObjectGetProperty(runtimeHandle, objectHandle, namePtr, name.Length);
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

  public void ReleaseArrayHandle(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiArrayHandle arrayHandle)
  {
    ReleaseArray(runtimeHandle, arrayHandle);
  }

  public void ReleasePromiseHandle(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle
  )
  {
    ReleasePromise(runtimeHandle, promiseHandle);
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
  public void ReleaseValueHandle(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueHandle valueHandle)
  {
    ReleaseValue(runtimeHandle, valueHandle);
  }

  public ExpoJsiError ScheduleRuntimeTask(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiTaskPriority priority,
    delegate* unmanaged[Cdecl]<nint, void> callback,
    nint taskContext,
    delegate* unmanaged[Cdecl]<nint, void> releaseTaskContext
  )
  {
    return RuntimeScheduleTask(runtimeHandle, priority, callback, taskContext, releaseTaskContext);
  }

  public bool CanExecuteSync(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return RuntimeCanExecuteSync(runtimeHandle) != 0;
  }

  public ExpoJsiError ExecuteRuntimeTaskSync(
    ExpoJsiRuntimeHandle runtimeHandle,
    delegate* unmanaged[Cdecl]<nint, void> callback,
    nint taskContext,
    delegate* unmanaged[Cdecl]<nint, void> releaseTaskContext
  )
  {
    return RuntimeExecuteSync(runtimeHandle, callback, taskContext, releaseTaskContext);
  }

  public ExpoJsiError DrainRuntimeTasks(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return RuntimeDrainTasks(runtimeHandle);
  }

  public static uint ExpectedSize => (uint)sizeof(ExpoJsiApi);
  public const uint ExpectedVersion = 11;
}
