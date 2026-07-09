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
    ExpoJsiValueResult> GetGlobalObject;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueResult> CreateObject;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueExpectation,
    ExpoJsiValueResult> ValueRetainAs;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    uint,
    ExpoJsiValueResult> CreateArray;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiError*,
    uint> ArrayGetLength;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    uint,
    ExpoJsiValueResult> ArrayGetValueAtIndex;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
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
    ExpoJsiPromiseSettlement,
    ExpoJsiValueHandle,
    ExpoJsiError> PromiseSettle;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    byte*,
    int,
    ExpoJsiValueHandle,
    ExpoJsiError> ObjectSetProperty;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    byte*,
    int,
    ExpoJsiValueResult> ObjectGetProperty;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiPropertyNamesResult> ObjectGetOwnPropertyNames;
  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueHandle*,
    uint,
    ExpoJsiValueResult> FunctionCall;
  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueHandle*,
    uint,
    ExpoJsiValueResult> FunctionCallWithThis;
  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueHandle*,
    uint,
    ExpoJsiValueResult> FunctionCallAsConstructor;

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
    ExpoJsiValueResult> CreateHostFunction;

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
    ExpoJsiPromiseHandle,
    void> ReleasePromise;

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
    ExpoJsiValueKind,
    ulong,
    ExpoJsiValueResult> CreatePrimitiveValue;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueResult> CreateObjectWithPrototype;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    byte*,
    int,
    ExpoJsiValueResult> CreateClass;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    byte*,
    int,
    ExpoJsiValueHandle,
    ExpoJsiValueResult> CreateClassWithSuperclass;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiValueHandle,
    ExpoJsiError*,
    byte> StrictEquals;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiNativeStateToken,
    nint,
    delegate* unmanaged[Cdecl]<nint, ulong, ulong, uint, void>,
    ExpoJsiError> ObjectSetNativeState;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ulong,
    ExpoJsiNativeStateResult> ObjectGetNativeState;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ulong,
    ExpoJsiError> ObjectClearNativeState;

  private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      byte*,
      int,
      ExpoJsiValueResult>,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      byte*,
      int,
      ExpoJsiValueHandle,
      ExpoJsiError>,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      ExpoJsiPropertyNamesResult>,
    nint,
    delegate* unmanaged[Cdecl]<nint, void>,
    ExpoJsiValueResult> CreateHostObject;

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
      throw new InvalidOperationException(
        $"Expo JSI ABI version mismatch: native={this.Version} managed={ExpoJsiApi.ExpectedVersion}."
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
      || this.ValueRetainAs is null
      || this.CreateArray is null
      || this.ArrayGetLength is null
      || this.ArrayGetValueAtIndex is null
      || this.ArraySetValueAtIndex is null
      || this.CreatePromise is null
      || this.PromiseAsValue is null
      || this.PromiseSettle is null
      || this.ObjectSetProperty is null
      || this.ObjectGetProperty is null
      || this.ObjectGetOwnPropertyNames is null
      || this.FunctionCall is null
      || this.FunctionCallWithThis is null
      || this.FunctionCallAsConstructor is null
      || this.CreateHostFunction is null
      || this.GetArgumentsCount is null
      || this.GetArgumentValue is null
      || this.ReleasePromise is null
      || this.ReleaseValue is null
      || this.CreateString is null
      || this.CloneValue is null
      || this.CreateError is null
      || this.GetString is null
      || this.RuntimeScheduleTask is null
      || this.RuntimeCanExecuteSync is null
      || this.RuntimeExecuteSync is null
      || this.IsPromise is null
      || this.IsError is null
      || this.CoerceToString is null
      || this.CreatePrimitiveValue is null
      || this.CreateObjectWithPrototype is null
      || this.CreateClass is null
      || this.CreateClassWithSuperclass is null
      || this.StrictEquals is null
      || this.ObjectSetNativeState is null
      || this.ObjectGetNativeState is null
      || this.ObjectClearNativeState is null
      || this.CreateHostObject is null
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
    return CreatePrimitiveValue(
      runtimeHandle,
      ExpoJsiValueKind.Number,
      BitConverter.DoubleToUInt64Bits(value)
    );
  }

  public ExpoJsiValueResult CreateBoolValue(ExpoJsiRuntimeHandle runtimeHandle, bool value)
  {
    return CreatePrimitiveValue(runtimeHandle, ExpoJsiValueKind.Bool, value ? 1u : 0u);
  }

  public ExpoJsiValueResult CreateUndefinedValue(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return CreatePrimitiveValue(runtimeHandle, ExpoJsiValueKind.Undefined, 0);
  }

  public ExpoJsiValueResult CreateNullValue(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return CreatePrimitiveValue(runtimeHandle, ExpoJsiValueKind.Null, 0);
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
    if (!result.IsOk)
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
    var message = error.GetMessageAndRelease();
    if (string.IsNullOrEmpty(message))
    {
      message = fallback;
    }
    throw new InvalidOperationException($"Native JSI error {error.Code}: {message}");
  }

  public ExpoJsiValueResult GetGlobal(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return GetGlobalObject(runtimeHandle);
  }

  public ExpoJsiValueResult CreateObjectValue(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return CreateObject(runtimeHandle);
  }

  public ExpoJsiValueResult CreateObjectWithPrototypeValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle prototypeHandle
  )
  {
    return CreateObjectWithPrototype(runtimeHandle, prototypeHandle);
  }

  public ExpoJsiValueResult CreateClassValue(ExpoJsiRuntimeHandle runtimeHandle, string name)
  {
    var bytes = StrictUtf8.GetBytes(name);
    fixed (byte* bytesPtr = bytes)
    {
      return CreateClass(runtimeHandle, bytesPtr, bytes.Length);
    }
  }

  public ExpoJsiValueResult CreateClassWithSuperclassValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    string name,
    ExpoJsiValueHandle superclassHandle
  )
  {
    var bytes = StrictUtf8.GetBytes(name);
    fixed (byte* bytesPtr = bytes)
    {
      return CreateClassWithSuperclass(runtimeHandle, bytesPtr, bytes.Length, superclassHandle);
    }
  }

  public byte StrictEqualsValues(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle leftHandle,
    ExpoJsiValueHandle rightHandle,
    ExpoJsiError* error
  )
  {
    return StrictEquals(runtimeHandle, leftHandle, rightHandle, error);
  }

  public ExpoJsiValueResult RetainValueAs(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle valueHandle,
    ExpoJsiValueExpectation expectation
  )
  {
    return ValueRetainAs(runtimeHandle, valueHandle, expectation);
  }

  public ExpoJsiValueResult CreateArrayValue(ExpoJsiRuntimeHandle runtimeHandle, uint length)
  {
    return CreateArray(runtimeHandle, length);
  }

  public uint GetArrayLength(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle arrayHandle,
    ExpoJsiError* error
  )
  {
    return ArrayGetLength(runtimeHandle, arrayHandle, error);
  }

  public ExpoJsiValueResult GetArrayValueAtIndex(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle arrayHandle,
    uint index
  )
  {
    return ArrayGetValueAtIndex(runtimeHandle, arrayHandle, index);
  }

  public ExpoJsiError SetArrayValueAtIndex(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle arrayHandle,
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

  public ExpoJsiError SettlePromise(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle,
    ExpoJsiPromiseSettlement settlement,
    ExpoJsiValueHandle valueHandle
  )
  {
    return PromiseSettle(runtimeHandle, promiseHandle, settlement, valueHandle);
  }

  public ExpoJsiError SetObjectProperty(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle objectHandle,
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
    ExpoJsiValueHandle objectHandle,
    ReadOnlySpan<byte> name
  )
  {
    fixed (byte* namePtr = name)
    {
      return ObjectGetProperty(runtimeHandle, objectHandle, namePtr, name.Length);
    }
  }

  public ExpoJsiPropertyNamesResult GetObjectOwnPropertyNames(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle objectHandle
  ) => ObjectGetOwnPropertyNames(runtimeHandle, objectHandle);

  public ExpoJsiError SetObjectNativeState(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle objectHandle,
    ExpoJsiNativeStateToken token,
    nint releaseContext,
    delegate* unmanaged[Cdecl]<nint, ulong, ulong, uint, void> release
  )
  {
    return ObjectSetNativeState(runtimeHandle, objectHandle, token, releaseContext, release);
  }

  public ExpoJsiNativeStateResult GetObjectNativeState(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle objectHandle,
    ulong typeId
  )
  {
    return ObjectGetNativeState(runtimeHandle, objectHandle, typeId);
  }

  public ExpoJsiError ClearObjectNativeState(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle objectHandle,
    ulong typeId
  )
  {
    return ObjectClearNativeState(runtimeHandle, objectHandle, typeId);
  }

  public ExpoJsiValueResult CallFunction(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle functionHandle,
    ReadOnlySpan<ExpoJsiValueHandle> arguments
  )
  {
    fixed (ExpoJsiValueHandle* argumentsPtr = arguments)
    {
      return FunctionCall(runtimeHandle, functionHandle, argumentsPtr, checked((uint)arguments.Length));
    }
  }

  public ExpoJsiValueResult CallFunctionWithThis(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle functionHandle,
    ExpoJsiValueHandle thisObjectHandle,
    ReadOnlySpan<ExpoJsiValueHandle> arguments
  )
  {
    fixed (ExpoJsiValueHandle* argumentsPtr = arguments)
    {
      return FunctionCallWithThis(
          runtimeHandle,
          functionHandle,
          thisObjectHandle,
          argumentsPtr,
          checked((uint)arguments.Length)
      );
    }
  }

  public ExpoJsiValueResult CallFunctionAsConstructor(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle functionHandle,
    ReadOnlySpan<ExpoJsiValueHandle> arguments
  )
  {
    fixed (ExpoJsiValueHandle* argumentsPtr = arguments)
    {
      return FunctionCallAsConstructor(
          runtimeHandle,
          functionHandle,
          argumentsPtr,
          checked((uint)arguments.Length)
      );
    }
  }

  public ExpoJsiValueResult CreateHostFunctionValue(
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

  public ExpoJsiValueResult CreateHostObjectValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      byte*,
      int,
      ExpoJsiValueResult> get,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      byte*,
      int,
      ExpoJsiValueHandle,
      ExpoJsiError> set,
    delegate* unmanaged[Cdecl]<
      nint,
      ExpoJsiRuntimeHandle,
      ExpoJsiPropertyNamesResult> getPropertyNames,
    nint callbackContext,
    delegate* unmanaged[Cdecl]<nint, void> releaseCallbackContext
  )
  {
    return CreateHostObject(
        runtimeHandle,
        get,
        set,
        getPropertyNames,
        callbackContext,
        releaseCallbackContext
    );
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

  public void ReleasePromiseHandle(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle
  )
  {
    ReleasePromise(runtimeHandle, promiseHandle);
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

  public static uint ExpectedSize => (uint)sizeof(ExpoJsiApi);
  public const uint ExpectedVersion = 21;
}
