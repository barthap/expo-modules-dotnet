# Generated Module Dispatch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded `AddOne` callback proof with a generated-looking module dispatch slice where JS calls `global.expo.modules.Math.add(41.5, true)` and receives `42.5`.

**Architecture:** Native C++ continues to own all JSI mechanics and exposes only opaque C ABI handles. Managed `Expo.JSI` wraps those handles, while the experiment assembly contains hand-written generated-looking provider code that decodes `JavaScriptArguments`, calls an authored C# module directly, and returns an owned `JavaScriptValue`.

**Tech Stack:** C++20, Hermes JSI, C ABI function table, .NET 10 unsafe function pointers, HostFXR experiment executable, focused shell verification.

---

## Constraints

- Do not use git worktrees.
- Do not introduce rn-macos, RNW, expo-desktop, app packaging, or view APIs.
- Do not introduce a source generator.
- Do not add runtime module scanning, `Assembly.GetTypes`, `MethodInfo.Invoke`, `Delegate.DynamicInvoke`, `object?[]`, JSON, `DllImport`, `LibraryImport`, or `NativeLibrary`.
- Do not expose raw `facebook::jsi::Runtime`, `Value`, `Object`, `Function`, or `Array` layouts to C#.
- Keep generated-looking module code in the HostFXR proof assembly for this slice.
- Keep reusable bridge code loader-neutral.
- Keep release-counting proof code experiment-only.
- Do not commit absolute local paths, usernames, machine names, or private hostnames.

## File Map

Modify:

- `native/include/expo_jsi.h`
  - Add opaque object/function/arguments handle declarations.
  - Add ABI result structs and function pointer typedefs for object/global/function/arguments operations.
  - Extend `expo_jsi_api`.

- `native/packages/jsi/src/ExpoJsiBridge.cpp`
  - Add native handle classes for `ObjectHandle`, `FunctionHandle`, and `ArgumentsHandle`.
  - Add API implementations for global object, object creation, object property set, object/function to value, host function creation, argument count, borrowed argument lookup, and release functions.

- `native/packages/jsi/include/ExpoJsiBridge.h`
  - Remove obsolete direct helper declarations after the experiment switches to
    C ABI host-function creation.

- `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
  - Add aliases for object, function, and arguments handles.

- `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
  - Add interop result structs for object/function/arguments-related ABI calls.

- `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
  - Add function pointer slots, validation, and named wrapper methods.

- `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  - Add `Global`, `CreateObject`, `CreateBool`, and `CreateHostFunction`.

- `managed/packages/Expo.JSI/JavaScriptValue.cs`
  - Split borrowed value behavior out of the owned wrapper.
  - Add conversion helpers needed by object/function wrappers.

Create:

- `managed/packages/Expo.JSI/JavaScriptBorrowedValue.cs`
  - Call-scoped borrowed value wrapper.

- `managed/packages/Expo.JSI/JavaScriptObject.cs`
  - Owned object wrapper with property setting and `AsValue`.

- `managed/packages/Expo.JSI/JavaScriptFunction.cs`
  - Owned function wrapper with `AsValue`.

- `managed/packages/Expo.JSI/JavaScriptArguments.cs`
  - Borrowed callback argument-buffer wrapper.

- `managed/packages/Expo.JSI/JavaScriptHostFunction.cs`
  - Managed delegate type for host callbacks.

- `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/MathModule.cs`
  - Authored module.

- `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/GeneratedModuleProvider.cs`
  - Hand-written generated-looking provider.

Modify:

- `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/EntryPoints.cs`
  - Replace `AddOne` export with a registration entry point, or keep `Run` plus provider registration if no separate export is needed.

- `experiments/hermes-console-hostfxr/native/main.cpp`
  - Stop installing hardcoded `callCSharp`.
  - Ask managed provider to register `global.expo.modules.Math.add`.
  - Evaluate `global.expo.modules.Math.add(41.5, true)`.

- `docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md`
  - Update final result after implementation succeeds.

## Task 1: Freeze ABI Additions In The Header

**Files:**
- Modify: `native/include/expo_jsi.h`

- [ ] **Step 1: Add opaque handle aliases**

Insert C++ forward declarations next to `RuntimeHandle` and `ValueHandle`:

```cpp
class ObjectHandle;
class FunctionHandle;
class ArgumentsHandle;
```

Extend the C++ aliases:

```cpp
using expo_jsi_object_t = expo::jsi::ObjectHandle;
using expo_jsi_function_t = expo::jsi::FunctionHandle;
using expo_jsi_arguments_t = expo::jsi::ArgumentsHandle;
```

Extend C and C++ handle typedefs:

```c
typedef expo_jsi_object_t *expo_jsi_object_handle;
typedef expo_jsi_function_t *expo_jsi_function_handle;
typedef expo_jsi_arguments_t *expo_jsi_arguments_handle;
```

and in the `#else` branch:

```c
typedef struct expo_jsi_object_t *expo_jsi_object_handle;
typedef struct expo_jsi_function_t *expo_jsi_function_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;
```

- [ ] **Step 2: Add result structs**

Add after `expo_jsi_value_result`:

```c
typedef struct expo_jsi_object_result {
  int32_t ok;
  expo_jsi_object_handle object;
  expo_jsi_error error;
} expo_jsi_object_result;

typedef struct expo_jsi_function_result {
  int32_t ok;
  expo_jsi_function_handle function;
  expo_jsi_error error;
} expo_jsi_function_result;
```

- [ ] **Step 3: Add host callback typedef**

Add this callback shape:

```c
typedef expo_jsi_value_result (*expo_jsi_host_function_callback_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle this_value,
  expo_jsi_arguments_handle arguments);

typedef void (*expo_jsi_release_callback_context_fn)(void *callback_context);
```

- [ ] **Step 4: Add function pointer typedefs**

Add these typedefs near the existing function pointer typedefs:

```c
typedef expo_jsi_value_result (*expo_jsi_create_bool_fn)(expo_jsi_runtime_handle runtime,
                                                         uint8_t value);

typedef expo_jsi_object_result (*expo_jsi_get_global_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_object_result (*expo_jsi_create_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_object_as_value_fn)(expo_jsi_runtime_handle runtime,
                                                             expo_jsi_object_handle object);

typedef void (*expo_jsi_release_object_fn)(expo_jsi_runtime_handle runtime,
                                           expo_jsi_object_handle object);

typedef void (*expo_jsi_release_function_fn)(expo_jsi_runtime_handle runtime,
                                             expo_jsi_function_handle function);

typedef expo_jsi_error (*expo_jsi_object_set_property_fn)(expo_jsi_runtime_handle runtime,
                                                          expo_jsi_object_handle object,
                                                          const char *name,
                                                          int32_t name_len,
                                                          expo_jsi_value_handle value);

typedef expo_jsi_function_result (*expo_jsi_create_host_function_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);

typedef expo_jsi_value_result (*expo_jsi_function_as_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_function_handle function);

typedef uint32_t (*expo_jsi_get_arguments_count_fn)(expo_jsi_runtime_handle runtime,
                                                    expo_jsi_arguments_handle arguments,
                                                    expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_get_argument_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_arguments_handle arguments,
  uint32_t index);
```

Use `uint8_t` for boolean ABI payloads. The C# wrapper should convert to and
from `bool`.

- [ ] **Step 5: Extend `expo_jsi_api`**

Add the new slots after existing primitive slots and before release slots:

```c
expo_jsi_create_bool_fn create_bool;
expo_jsi_get_global_object_fn get_global_object;
expo_jsi_create_object_fn create_object;
expo_jsi_object_as_value_fn object_as_value;
expo_jsi_object_set_property_fn object_set_property;
expo_jsi_create_host_function_fn create_host_function;
expo_jsi_function_as_value_fn function_as_value;
expo_jsi_get_arguments_count_fn get_arguments_count;
expo_jsi_get_argument_value_fn get_argument_value;
expo_jsi_release_object_fn release_object;
expo_jsi_release_function_fn release_function;
```

Keep `release_value` in the table.

- [ ] **Step 6: Verify header syntax as C**

Run:

```bash
printf '#include "native/include/expo_jsi.h"\nint main(void) { return 0; }\n' | cc -x c -fsyntax-only -
```

Expected: exit 0 with no output.

## Task 2: Implement Native Handles And ABI Functions

**Files:**
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `native/packages/jsi/include/ExpoJsiBridge.h`

- [ ] **Step 1: Add handle classes**

Add classes in `namespace expo::jsi` after `ValueHandle`.

Use this shape:

```cpp
class ObjectHandle final {
public:
  static std::unique_ptr<ObjectHandle> owned(facebook::jsi::Object object)
  {
    return std::unique_ptr<ObjectHandle>(
      new ObjectHandle(std::make_unique<facebook::jsi::Object>(std::move(object))));
  }

  facebook::jsi::Object &object()
  {
    return *object_;
  }

private:
  explicit ObjectHandle(std::unique_ptr<facebook::jsi::Object> object)
    : object_(std::move(object))
  {
  }

  std::unique_ptr<facebook::jsi::Object> object_;
};

class FunctionHandle final {
public:
  static std::unique_ptr<FunctionHandle> owned(facebook::jsi::Function function)
  {
    return std::unique_ptr<FunctionHandle>(
      new FunctionHandle(std::make_unique<facebook::jsi::Function>(std::move(function))));
  }

  facebook::jsi::Function &function()
  {
    return *function_;
  }

private:
  explicit FunctionHandle(std::unique_ptr<facebook::jsi::Function> function)
    : function_(std::move(function))
  {
  }

  std::unique_ptr<facebook::jsi::Function> function_;
};

class ArgumentsHandle final {
public:
  ArgumentsHandle(const facebook::jsi::Value *arguments, size_t count)
    : arguments_(arguments), count_(count)
  {
  }

  size_t count() const
  {
    return count_;
  }

  const facebook::jsi::Value &at(size_t index) const
  {
    if (index >= count_) {
      throw std::out_of_range("Argument index is out of range.");
    }
    return arguments_[index];
  }

private:
  const facebook::jsi::Value *arguments_;
  size_t count_;
};
```

- [ ] **Step 2: Add result helpers**

Add helpers near `makeValueResult`:

```cpp
expo_jsi_object_result makeObjectResult(std::unique_ptr<expo::jsi::ObjectHandle> object)
{
  return expo_jsi_object_result{
    1,
    object.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_function_result makeFunctionResult(std::unique_ptr<expo::jsi::FunctionHandle> function)
{
  return expo_jsi_function_result{
    1,
    function.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_object_result makeObjectErrorResult(int32_t code, const char *message)
{
  return expo_jsi_object_result{0, nullptr, makeError(code, message)};
}

expo_jsi_function_result makeFunctionErrorResult(int32_t code, const char *message)
{
  return expo_jsi_function_result{0, nullptr, makeError(code, message)};
}
```

- [ ] **Step 3: Implement primitive bool creation**

Add:

```cpp
expo_jsi_value_result createBool(expo_jsi_runtime_handle runtime, uint8_t value)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::owned(
      facebook::jsi::Value(value != 0)));
  } catch (const std::exception &ex) {
    return makeErrorResult(12, ex.what());
  } catch (...) {
    return makeErrorResult(13, "Unknown native exception while creating boolean.");
  }
}
```

- [ ] **Step 4: Implement object APIs**

Add:

```cpp
expo_jsi_object_result getGlobalObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }

  try {
    return makeObjectResult(
      expo::jsi::ObjectHandle::owned(runtimeHandle->runtime().global()));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(14, ex.what());
  } catch (...) {
    return makeObjectErrorResult(15, "Unknown native exception while getting global object.");
  }
}

expo_jsi_object_result createObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }

  try {
    return makeObjectResult(
      expo::jsi::ObjectHandle::owned(facebook::jsi::Object(runtimeHandle->runtime())));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(16, ex.what());
  } catch (...) {
    return makeObjectErrorResult(17, "Unknown native exception while creating object.");
  }
}

expo_jsi_value_result objectAsValue(expo_jsi_runtime_handle runtime,
                                    expo_jsi_object_handle object)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (object == nullptr) {
    return makeErrorResult(18, "Object handle is null.");
  }

  try {
    return makeValueResult(
      expo::jsi::ValueHandle::owned(facebook::jsi::Value(object->object())));
  } catch (const std::exception &ex) {
    return makeErrorResult(19, ex.what());
  } catch (...) {
    return makeErrorResult(20, "Unknown native exception while converting object to value.");
  }
}

expo_jsi_error objectSetProperty(expo_jsi_runtime_handle runtime,
                                 expo_jsi_object_handle object,
                                 const char *name,
                                 int32_t name_len,
                                 expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(21, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeError(22, "Object handle is null.");
  }
  if (name == nullptr || name_len < 0) {
    return makeError(23, "Property name is invalid.");
  }
  if (value == nullptr) {
    return makeError(24, "Value handle is null.");
  }

  try {
    auto propertyName = std::string(name, static_cast<size_t>(name_len));
    object->object().setProperty(runtimeHandle->runtime(), propertyName.c_str(), value->value());
    return expo_jsi_error{0, nullptr, 0};
  } catch (const std::exception &ex) {
    return makeError(25, ex.what());
  } catch (...) {
    return makeError(26, "Unknown native exception while setting object property.");
  }
}
```

If `Object::setProperty` overload resolution does not accept `Value &`, create
a local copy with `facebook::jsi::Value(runtimeHandle->runtime(), value->value())`
and pass that copy.

- [ ] **Step 5: Implement host function APIs**

Add native callback context:

```cpp
class HostFunctionContext final {
public:
  HostFunctionContext(expo_jsi_host_function_callback_fn callback,
                      void *callbackContext,
                      expo_jsi_release_callback_context_fn releaseCallbackContext,
                      expo_jsi_runtime_handle runtimeHandle)
    : callback_(callback),
      callbackContext_(callbackContext),
      releaseCallbackContext_(releaseCallbackContext),
      runtimeHandle_(runtimeHandle)
  {
  }

  ~HostFunctionContext()
  {
    if (releaseCallbackContext_ != nullptr && callbackContext_ != nullptr) {
      releaseCallbackContext_(callbackContext_);
    }
  }

  expo_jsi_value_result call(expo_jsi_value_handle thisValue,
                             expo_jsi_arguments_handle arguments) const
  {
    if (callback_ == nullptr) {
      return makeErrorResult(27, "Host function callback is null.");
    }
    return callback_(callbackContext_, runtimeHandle_, thisValue, arguments);
  }

private:
  expo_jsi_host_function_callback_fn callback_;
  void *callbackContext_;
  expo_jsi_release_callback_context_fn releaseCallbackContext_;
  expo_jsi_runtime_handle runtimeHandle_;
};
```

Add `createHostFunction`:

```cpp
expo_jsi_function_result createHostFunction(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_function_result{0, nullptr, error};
  }
  if (name == nullptr || name_len < 0) {
    return makeFunctionErrorResult(28, "Host function name is invalid.");
  }

  try {
    auto functionName = std::string(name, static_cast<size_t>(name_len));
    auto context = std::make_shared<HostFunctionContext>(
      callback, callback_context, release_callback_context, runtime);

    auto function = facebook::jsi::Function::createFromHostFunction(
      runtimeHandle->runtime(),
      facebook::jsi::PropNameID::forUtf8(runtimeHandle->runtime(), functionName),
      parameter_count,
      [context](facebook::jsi::Runtime &jsRuntime,
                const facebook::jsi::Value &thisValue,
                const facebook::jsi::Value *arguments,
                size_t count) -> facebook::jsi::Value {
        auto thisHandle = expo::jsi::ValueHandle::borrowed(thisValue);
        auto argumentsHandle = expo::jsi::ArgumentsHandle(arguments, count);
        expo_jsi_value_result result{};
        try {
          result = context->call(thisHandle.get(), &argumentsHandle);
          if (result.ok == 0 || result.value == nullptr) {
            const char *message = result.error.message != nullptr
              ? result.error.message
              : "Managed host function failed.";
            throw facebook::jsi::JSError(jsRuntime, message);
          }
          auto jsResult = facebook::jsi::Value(jsRuntime, result.value->value());
          delete result.value;
          return jsResult;
        } catch (const facebook::jsi::JSError &) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw;
        } catch (const std::exception &ex) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw facebook::jsi::JSError(jsRuntime, ex.what());
        }
      });

    return makeFunctionResult(expo::jsi::FunctionHandle::owned(std::move(function)));
  } catch (const std::exception &ex) {
    return makeFunctionErrorResult(29, ex.what());
  } catch (...) {
    return makeFunctionErrorResult(30, "Unknown native exception while creating host function.");
  }
}
```

If the compiler rejects direct access to `result.value->value()` because the
handle type is incomplete in the lambda context, keep the lambda in
`ExpoJsiBridge.cpp` and use the existing concrete `ValueHandle` class directly.

- [ ] **Step 6: Implement function conversion and argument APIs**

Add:

```cpp
expo_jsi_value_result functionAsValue(expo_jsi_runtime_handle runtime,
                                      expo_jsi_function_handle function)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (function == nullptr) {
    return makeErrorResult(31, "Function handle is null.");
  }

  try {
    return makeValueResult(
      expo::jsi::ValueHandle::owned(facebook::jsi::Value(function->function())));
  } catch (const std::exception &ex) {
    return makeErrorResult(32, ex.what());
  } catch (...) {
    return makeErrorResult(33, "Unknown native exception while converting function to value.");
  }
}

uint32_t getArgumentsCount(expo_jsi_runtime_handle runtime,
                           expo_jsi_arguments_handle arguments,
                           expo_jsi_error *error)
{
  if (tryRuntimeHandle(runtime, error) == nullptr) {
    return 0;
  }
  if (arguments == nullptr) {
    writeError(error, 34, "Arguments handle is null.");
    return 0;
  }
  clearError(error);
  return static_cast<uint32_t>(arguments->count());
}

expo_jsi_value_result getArgumentValue(expo_jsi_runtime_handle runtime,
                                       expo_jsi_arguments_handle arguments,
                                       uint32_t index)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (arguments == nullptr) {
    return makeErrorResult(35, "Arguments handle is null.");
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::borrowed(arguments->at(index)));
  } catch (const std::exception &ex) {
    return makeErrorResult(36, ex.what());
  } catch (...) {
    return makeErrorResult(37, "Unknown native exception while reading argument.");
  }
}
```

The returned argument value is borrowed. The managed wrapper must not release it
as owned.

- [ ] **Step 7: Implement release APIs**

Add:

```cpp
void releaseObject(expo_jsi_runtime_handle, expo_jsi_object_handle object)
{
  delete object;
}

void releaseFunction(expo_jsi_runtime_handle, expo_jsi_function_handle function)
{
  delete function;
}
```

- [ ] **Step 8: Extend `kApi`**

Update `const expo_jsi_api kApi{...}` in the exact field order from
`native/include/expo_jsi.h`.

- [ ] **Step 9: Build native target**

Run:

```bash
cmake --build build/hermes-console-hostfxr --target hermes_console_hostfxr
```

Expected: native target builds. It may still run the old hardcoded proof until
later tasks replace `main.cpp`.

## Task 3: Add Managed Interop Surface

**Files:**
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`

- [ ] **Step 1: Add handle aliases**

Append to `ExpoJsiHandles.cs`:

```csharp
global using ExpoJsiObjectHandle = System.IntPtr;
global using ExpoJsiFunctionHandle = System.IntPtr;
global using ExpoJsiArgumentsHandle = System.IntPtr;
```

- [ ] **Step 2: Add result structs**

Append to `ExpoJsiTypes.cs`:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiObjectResult
{
  public readonly int Ok;
  public readonly ExpoJsiObjectHandle Object;
  public readonly ExpoJsiError Error;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiFunctionResult
{
  public readonly int Ok;
  public readonly ExpoJsiFunctionHandle Function;
  public readonly ExpoJsiError Error;
}
```

- [ ] **Step 3: Add callback delegate shapes**

Add to `ExpoJsiApi.cs` above `ExpoJsiApi`:

```csharp
internal unsafe delegate* unmanaged[Cdecl]<
    nint,
    ExpoJsiRuntimeHandle,
    ExpoJsiValueHandle,
    ExpoJsiArgumentsHandle,
    ExpoJsiValueResult> HostFunctionCallbackPointer;
```

If C# does not allow a named function pointer type alias in this position, do
not add this alias. Use the expanded function pointer type directly in the API
field and wrapper method.

- [ ] **Step 4: Add function pointer fields**

Add private readonly fields to `ExpoJsiApi` in the same order as the C struct:

```csharp
private readonly delegate* unmanaged[Cdecl]<
    ExpoJsiRuntimeHandle,
    byte,
    ExpoJsiValueResult> CreateBool;

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
```

- [ ] **Step 5: Extend validation**

Add every new field to `Validate()` null checks. Expected missing-function
message remains:

```text
Expo JSI API table is missing required functions.
```

- [ ] **Step 6: Add named wrapper methods**

Add wrapper methods:

```csharp
public ExpoJsiValueResult CreateBoolValue(ExpoJsiRuntimeHandle runtimeHandle, bool value)
{
    return CreateBool(runtimeHandle, value ? (byte)1 : (byte)0);
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
    ExpoJsiObjectHandle objectHandle)
{
    return ObjectAsValue(runtimeHandle, objectHandle);
}

public ExpoJsiError SetObjectProperty(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiObjectHandle objectHandle,
    ReadOnlySpan<byte> name,
    ExpoJsiValueHandle valueHandle)
{
    fixed (byte* namePtr = name)
    {
        return ObjectSetProperty(runtimeHandle, objectHandle, namePtr, name.Length, valueHandle);
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
    delegate* unmanaged[Cdecl]<nint, void> releaseCallbackContext)
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
            releaseCallbackContext);
    }
}

public ExpoJsiValueResult ConvertFunctionToValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiFunctionHandle functionHandle)
{
    return FunctionAsValue(runtimeHandle, functionHandle);
}

public uint GetArgumentCount(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArgumentsHandle argumentsHandle,
    ExpoJsiError* error)
{
    return GetArgumentsCount(runtimeHandle, argumentsHandle, error);
}

public ExpoJsiValueResult GetArgument(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiArgumentsHandle argumentsHandle,
    uint index)
{
    return GetArgumentValue(runtimeHandle, argumentsHandle, index);
}

public void ReleaseObjectHandle(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiObjectHandle objectHandle)
{
    ReleaseObject(runtimeHandle, objectHandle);
}

public void ReleaseFunctionHandle(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiFunctionHandle functionHandle)
{
    ReleaseFunction(runtimeHandle, functionHandle);
}
```

- [ ] **Step 7: Build managed project**

Run:

```bash
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
```

Expected: build succeeds with 0 errors. If the named function-pointer alias from
Step 3 fails, remove only that alias and keep the expanded function pointer type
in the field and wrapper method before moving on.

## Task 4: Add Managed Wrapper Types

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptValue.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptBorrowedValue.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptObject.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptFunction.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptArguments.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptHostFunction.cs`

- [ ] **Step 1: Add host delegate**

Create `JavaScriptHostFunction.cs`:

```csharp
namespace Expo.JSI;

public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptBorrowedValue thisValue,
    JavaScriptArguments arguments,
    object context);
```

- [ ] **Step 2: Add borrowed value wrapper**

Create `JavaScriptBorrowedValue.cs`:

```csharp
namespace Expo.JSI;

public readonly struct JavaScriptBorrowedValue
{
    private readonly JavaScriptRuntime runtime;
    private readonly ExpoJsiValueHandle handle;

    internal JavaScriptBorrowedValue(JavaScriptRuntime runtime, ExpoJsiValueHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public JavaScriptValueKind Kind
    {
        get
        {
            ThrowIfNull();
            return runtime.GetValueKind(handle);
        }
    }

    public bool AsBool()
    {
        ThrowIfNull();
        return runtime.GetBool(handle);
    }

    public double AsDouble()
    {
        ThrowIfNull();
        return runtime.GetDouble(handle);
    }

    private void ThrowIfNull()
    {
        if (handle == 0)
        {
            throw new ObjectDisposedException(nameof(JavaScriptBorrowedValue));
        }
    }
}
```

- [ ] **Step 3: Remove borrowed ownership from `JavaScriptValue`**

In `JavaScriptValue.cs`, remove `ownsHandle` and `FromBorrowedHandle`. The
class should own and release every non-zero handle it contains.

Use:

```csharp
private JavaScriptValue(JavaScriptRuntime runtime, ExpoJsiValueHandle handle)
{
    this.runtime = runtime;
    this.handle = handle;
}

internal static JavaScriptValue FromOwnedHandle(
    JavaScriptRuntime runtime,
    ExpoJsiValueHandle handle
) => new(runtime, handle);

public void Dispose()
{
    if (handle != 0)
    {
        runtime.ReleaseValue(handle);
    }
    handle = 0;
}
```

Keep `Detach()` for ownership transfer.

- [ ] **Step 4: Add `JavaScriptObject`**

Create `JavaScriptObject.cs`:

```csharp
using System.Text;

namespace Expo.JSI;

public sealed class JavaScriptObject : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private ExpoJsiObjectHandle handle;

    internal JavaScriptObject(JavaScriptRuntime runtime, ExpoJsiObjectHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public void SetProperty(string name, JavaScriptValue value)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        runtime.SetObjectProperty(handle, nameBytes, value.Handle);
    }

    public JavaScriptValue AsValue()
    {
        ThrowIfDisposed();
        return runtime.ObjectAsValue(handle);
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            runtime.ReleaseObject(handle);
            handle = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
```

This requires `JavaScriptValue.Handle` as an internal property in the next step.

- [ ] **Step 5: Add internal handle accessor**

In `JavaScriptValue.cs`, add:

```csharp
internal ExpoJsiValueHandle Handle
{
    get
    {
        ThrowIfDisposed();
        return handle;
    }
}
```

- [ ] **Step 6: Add `JavaScriptFunction`**

Create `JavaScriptFunction.cs`:

```csharp
namespace Expo.JSI;

public sealed class JavaScriptFunction : IDisposable
{
    private readonly JavaScriptRuntime runtime;
    private ExpoJsiFunctionHandle handle;

    internal JavaScriptFunction(JavaScriptRuntime runtime, ExpoJsiFunctionHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public JavaScriptValue AsValue()
    {
        ThrowIfDisposed();
        return runtime.FunctionAsValue(handle);
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            runtime.ReleaseFunction(handle);
            handle = 0;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
    }
}
```

- [ ] **Step 7: Add `JavaScriptArguments`**

Create `JavaScriptArguments.cs`:

```csharp
namespace Expo.JSI;

public readonly struct JavaScriptArguments
{
    private readonly JavaScriptRuntime runtime;
    private readonly ExpoJsiArgumentsHandle handle;

    internal JavaScriptArguments(JavaScriptRuntime runtime, ExpoJsiArgumentsHandle handle)
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    public uint Count
    {
        get
        {
            ThrowIfNull();
            return runtime.GetArgumentsCount(handle);
        }
    }

    public JavaScriptBorrowedValue GetBorrowedValue(uint index)
    {
        ThrowIfNull();
        return runtime.GetBorrowedArgument(handle, index);
    }

    private void ThrowIfNull()
    {
        if (handle == 0)
        {
            throw new ObjectDisposedException(nameof(JavaScriptArguments));
        }
    }
}
```

- [ ] **Step 8: Extend `JavaScriptRuntime`**

Add public methods:

```csharp
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
```

Add internal methods:

```csharp
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
    ExpoJsiValueHandle valueHandle)
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
    uint index)
{
    var result = api->GetArgument(runtimeHandle, argumentsHandle, index);
    if (result.Ok == 0 || result.Value == 0)
    {
        ThrowNativeError(result.Error, "Failed to read JavaScript argument.");
    }
    return new JavaScriptBorrowedValue(this, result.Value);
}

internal void ReleaseObject(ExpoJsiObjectHandle objectHandle)
{
    api->ReleaseObjectHandle(runtimeHandle, objectHandle);
}

internal void ReleaseFunction(ExpoJsiFunctionHandle functionHandle)
{
    api->ReleaseFunctionHandle(runtimeHandle, functionHandle);
}
```

- [ ] **Step 9: Build managed project**

Run:

```bash
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
```

Expected: build succeeds with 0 errors. Fix visibility or unsafe access issues
before moving on.

## Task 5: Add Managed Host Function Context And Registration

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Create: `managed/packages/Expo.JSI/Interop/HostFunctionContext.cs` if keeping context private to `Expo.JSI`
- Modify: `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/EntryPoints.cs`
- Create: `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/MathModule.cs`
- Create: `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/GeneratedModuleProvider.cs`

- [ ] **Step 1: Add managed callback context**

Create `managed/packages/Expo.JSI/Interop/HostFunctionContext.cs`:

```csharp
using System.Runtime.InteropServices;
using Expo.JSI;

namespace Expo.JSI.Interop;

internal sealed unsafe class HostFunctionContext
{
    public HostFunctionContext(
        ExpoJsiApi* api,
        JavaScriptHostFunction callback,
        object context)
    {
        Api = api;
        Callback = callback;
        Context = context;
    }

    public ExpoJsiApi* Api { get; }
    public JavaScriptHostFunction Callback { get; }
    public object Context { get; }

    public nint ToIntPtr()
    {
        return GCHandle.ToIntPtr(GCHandle.Alloc(this));
    }

    public static HostFunctionContext FromIntPtr(nint pointer)
    {
        return (HostFunctionContext)GCHandle.FromIntPtr(pointer).Target!;
    }

    public static void Release(nint pointer)
    {
        if (pointer == 0)
        {
            return;
        }
        GCHandle.FromIntPtr(pointer).Free();
    }
}
```

- [ ] **Step 2: Add unmanaged callback trampoline**

In `JavaScriptRuntime.cs`, add static unmanaged methods inside the class:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
private static ExpoJsiValueResult InvokeHostFunction(
    nint callbackContext,
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle thisValueHandle,
    ExpoJsiArgumentsHandle argumentsHandle)
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
```

The required outcome is a static unmanaged trampoline that can reconstruct
`JavaScriptRuntime`, borrowed `this`, and `JavaScriptArguments`.

- [ ] **Step 3: Add constructor helpers**

Change the existing `JavaScriptRuntime` constructor from `private` to `internal`
so the trampoline can create:

```csharp
var runtime = new JavaScriptRuntime(context.Api, runtimeHandle);
```

Add this constructor in `ExpoJsiValueResult`:

```csharp
public ExpoJsiValueResult(int ok, ExpoJsiValueHandle value, ExpoJsiError error)
{
    Ok = ok;
    Value = value;
    Error = error;
}
```

For managed exception error messages, this proof may return
`new ExpoJsiValueResult(0, 0, default)` and write the exception to stderr. A
later slice can add managed-owned error-message buffers.

- [ ] **Step 4: Add release trampoline**

In `JavaScriptRuntime.cs` add:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
private static void ReleaseHostFunctionContext(nint callbackContext)
{
    HostFunctionContext.Release(callbackContext);
}
```

- [ ] **Step 5: Add `CreateHostFunction`**

Add to `JavaScriptRuntime`:

```csharp
public unsafe JavaScriptFunction CreateHostFunction(
    string name,
    uint parameterCount,
    JavaScriptHostFunction callback,
    object context)
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
        &ReleaseHostFunctionContext);

    if (result.Ok == 0 || result.Function == 0)
    {
        HostFunctionContext.Release(callbackContext);
        ThrowNativeError(result.Error, "Failed to create JavaScript host function.");
    }

    return new JavaScriptFunction(this, result.Function);
}
```

Add these usings to `JavaScriptRuntime.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using Expo.JSI.Interop;
```

- [ ] **Step 6: Add authored module**

Create `MathModule.cs`:

```csharp
namespace HostFxrJSIProof;

internal sealed class MathModule
{
    public double Add(double value, bool shouldAddOne)
    {
        return shouldAddOne ? value + 1.0 : value;
    }
}
```

- [ ] **Step 7: Add generated-looking provider**

Create `GeneratedModuleProvider.cs`:

```csharp
using Expo.JSI;

namespace HostFxrJSIProof;

internal static class GeneratedModuleProvider
{
    public static void Register(JavaScriptRuntime runtime)
    {
        using var global = runtime.Global();
        using var expo = runtime.CreateObject();
        using var modules = runtime.CreateObject();
        using var math = runtime.CreateObject();

        var module = new MathModule();
        using var add = runtime.CreateHostFunction("add", 2, MathAddHostFunction, module);

        using var addValue = add.AsValue();
        math.SetProperty("add", addValue);

        using var mathValue = math.AsValue();
        modules.SetProperty("Math", mathValue);

        using var modulesValue = modules.AsValue();
        expo.SetProperty("modules", modulesValue);

        using var expoValue = expo.AsValue();
        global.SetProperty("expo", expoValue);

        Console.WriteLine("registered generated-looking Math module");
    }

    private static JavaScriptValue MathAddHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
        if (arguments.Count != 2)
        {
            throw new ArgumentException($"Math.add expects 2 arguments, got {arguments.Count}.");
        }

        var module = (MathModule)context;
        var value = arguments.GetBorrowedValue(0).AsDouble();
        var shouldAddOne = arguments.GetBorrowedValue(1).AsBool();
        return runtime.CreateNumber(module.Add(value, shouldAddOne));
    }
}
```

- [ ] **Step 8: Replace `AddOne` export**

In `EntryPoints.cs`, remove the `AddOne` unmanaged export and add:

```csharp
[UnmanagedCallersOnly(
    EntryPoint = "hostfxr_jsi_register_modules",
    CallConvs = new[] { typeof(CallConvCdecl) }
)]
public static int RegisterModules(nint api, nint runtimeHandle)
{
    try
    {
        var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
        GeneratedModuleProvider.Register(runtime);
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}
```

Keep `Run` as the independent managed value smoke test.

- [ ] **Step 9: Build managed project**

Run:

```bash
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
```

Expected: build succeeds with 0 errors. If unmanaged trampoline syntax fails,
fix it before touching native `main.cpp`.

## Task 6: Replace Hardcoded Native Callback With Module Registration

**Files:**
- Modify: `experiments/hermes-console-hostfxr/native/main.cpp`

- [ ] **Step 1: Replace delegate typedef**

Replace `add_one_fn` with:

```cpp
using register_modules_fn =
  int(CORECLR_DELEGATE_CALLTYPE *)(const expo_jsi_api *, expo_jsi_runtime_handle);
```

- [ ] **Step 2: Simplify `CSharpAPI`**

Replace `CSharpAPI` with:

```cpp
struct CSharpAPI {
  register_modules_fn register_modules;
  const expo_jsi_api *api;
  expo_jsi_runtime_handle runtime_handle;
};
```

- [ ] **Step 3: Replace `jsi_main`**

Replace the hardcoded `callCSharp` installation with:

```cpp
void jsi_main(jsi::Runtime &rt, CSharpAPI &cs)
{
  int register_rc = cs.register_modules(cs.api, cs.runtime_handle);
  if (register_rc != 0) {
    throw std::runtime_error("Managed module registration failed with code " +
                             std::to_string(register_rc));
  }

  auto callback_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>(
      "global.expo.modules.Math.add(41.5, true);"),
    "generated-module-dispatch.js");
  if (!callback_result.isNumber() || callback_result.asNumber() != 42.5) {
    throw std::runtime_error("Generated module dispatch proof failed.");
  }
  std::cout << "JS called generated-looking C# module: " << callback_result.asNumber()
            << std::endl;
}
```

- [ ] **Step 4: Resolve managed delegate**

Replace `add_one` resolution with:

```cpp
register_modules_fn register_modules = nullptr;
rc = load_assembly(assembly.c_str(),
                   type_name,
                   STR("RegisterModules"),
                   UNMANAGEDCALLERSONLY_METHOD,
                   nullptr,
                   reinterpret_cast<void **>(&register_modules));
if (rc != 0 || register_modules == nullptr) {
  throw std::runtime_error("Failed to resolve managed RegisterModules entry point: " +
                           std::to_string(rc));
}
```

Use the same `type_name` pattern that currently resolves `Run` and `AddOne`.

- [ ] **Step 5: Construct `CSharpAPI` with registration delegate**

Change:

```cpp
auto cs = CSharpAPI{add_one, &release_counter.api, runtime_handle};
```

to:

```cpp
auto cs = CSharpAPI{register_modules, &release_counter.api, runtime_handle};
```

- [ ] **Step 6: Update expected release count**

Run the proof once after implementation. Count owned releases from:

- `Run` value disposal;
- `GeneratedModuleProvider.Register` object/function/value wrapper disposal;
- managed callback return value copied back to JSI.

Set the proof assertion to the observed intentional count only after manually
checking each release maps to an owned wrapper. Do not count borrowed argument
handles.

- [ ] **Step 7: Build native target**

Run:

```bash
cmake --build build/hermes-console-hostfxr --target hermes_console_hostfxr
```

Expected: build succeeds.

## Task 7: Run Proof And Harden Error Paths

**Files:**
- Modify as needed: files touched in Tasks 2-6

- [ ] **Step 1: Run managed build**

Run:

```bash
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
```

Expected:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 2: Run native build**

Run:

```bash
cmake --build build/hermes-console-hostfxr --target hermes_console_hostfxr
```

Expected: `[100%] Built target hermes_console_hostfxr`.

- [ ] **Step 3: Run proof**

Run:

```bash
./build/hermes-console-hostfxr/hermes_console_hostfxr
```

Expected meaningful output:

```text
Loaded HostFXR path:
Created Hermes-backed JSI runtime
registered generated-looking Math module
JS called generated-looking C# module: 42.5
managed JSI proof: number kind=Number value=42.5
Released owned value handles:
hermes console hostfxr proof: ok
```

- [ ] **Step 4: Fix null-error ambiguity**

If managed callback failure currently returns null without an error message,
make native throw:

```text
Managed host function failed.
```

This is acceptable for this slice. Do not add managed-owned error buffers in
this task unless the proof cannot diagnose failures otherwise.

- [ ] **Step 5: Run forbidden-dispatch scan**

Run:

```bash
if rg -n "Assembly\\.GetTypes|MethodInfo\\.Invoke|DynamicInvoke|object\\?\\[\\]|JsonSerializer|JsonConvert|DllImport|LibraryImport|NativeLibrary" managed experiments/hermes-console-hostfxr; then
  exit 1
else
  echo "forbidden-dispatch scan: clean"
fi
```

Expected:

```text
forbidden-dispatch scan: clean
```

- [ ] **Step 6: Run machine-detail scan**

Run:

```bash
if rg -n "EXPO_REPO_PATH|expo-repo" \
  experiments/hermes-console-hostfxr native managed docs scripts \
  -g '!**/bin/**' -g '!**/obj/**' -g '!**/.cxx/**' -g '!**/build/**'; then
  exit 1
else
  echo "machine-detail scan: clean"
fi
```

Expected:

```text
machine-detail scan: clean
```

## Task 8: Update Docs And Result Notes

**Files:**
- Modify: `experiments/hermes-console-hostfxr/README.md`
- Modify: `docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md`

- [ ] **Step 1: Update experiment README**

Update the opening paragraph to say the proof now verifies:

```text
JavaScript installs and calls a generated-looking module function at
global.expo.modules.Math.add. Native C++ owns the JSI host function plumbing,
while C# generated-looking code decodes borrowed arguments, calls MathModule.Add,
and returns an owned JavaScript value handle.
```

- [ ] **Step 2: Update spike result meaningful output**

Replace old callback output with:

```text
Created Hermes-backed JSI runtime
registered generated-looking Math module
JS called generated-looking C# module: 42.5
managed JSI proof: number kind=Number value=42.5
Released owned value handles:
hermes console hostfxr proof: ok
```

Use the exact release count from Task 7 when editing the result note.

- [ ] **Step 3: Update ownership findings**

Document:

- global/object/function wrappers are owned handles released by C#;
- `JavaScriptArguments` and `JavaScriptBorrowedValue` are callback-scoped and
  not released by C#;
- the host-function callback context is retained by managed `GCHandle` and
  released through native host-function context teardown;
- return values from managed callbacks are owned handles copied back to JSI and
  released exactly once by native.

- [ ] **Step 4: Update stop/go decision**

Set stop/go decision to:

```text
Go. The headless generated-looking dispatch shape works. Next slice should add
string conversion or run a NativeAOT compatibility audit before real host
adapter work.
```

- [ ] **Step 5: Verify docs**

Run:

```bash
git diff --check -- experiments/hermes-console-hostfxr/README.md docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md
```

Expected: exit 0 with no output.

## Task 9: Final Verification And Commit

**Files:**
- All files touched by previous tasks.

- [ ] **Step 1: Run full verification**

Run:

```bash
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
cmake --build build/hermes-console-hostfxr --target hermes_console_hostfxr
./build/hermes-console-hostfxr/hermes_console_hostfxr
printf '#include "native/include/expo_jsi.h"\nint main(void) { return 0; }\n' | cc -x c -fsyntax-only -
git diff --check
```

Expected:

- managed build succeeds;
- native target builds;
- proof prints `hermes console hostfxr proof: ok`;
- C header syntax check exits 0;
- `git diff --check` exits 0.

- [ ] **Step 2: Run final scans**

Run:

```bash
if rg -n "Assembly\\.GetTypes|MethodInfo\\.Invoke|DynamicInvoke|object\\?\\[\\]|JsonSerializer|JsonConvert|DllImport|LibraryImport|NativeLibrary" managed experiments/hermes-console-hostfxr; then
  exit 1
else
  echo "forbidden-dispatch scan: clean"
fi

if rg -n "EXPO_REPO_PATH|expo-repo" \
  experiments/hermes-console-hostfxr native managed docs scripts \
  -g '!**/bin/**' -g '!**/obj/**' -g '!**/.cxx/**' -g '!**/build/**'; then
  exit 1
else
  echo "machine-detail scan: clean"
fi
```

Expected:

```text
forbidden-dispatch scan: clean
machine-detail scan: clean
```

- [ ] **Step 3: Review diff**

Run:

```bash
git diff --stat
git diff -- native/include/expo_jsi.h native/packages/jsi/src/ExpoJsiBridge.cpp managed/packages/Expo.JSI experiments/hermes-console-hostfxr docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md
```

Expected: diff contains only generated-dispatch-slice changes and docs updates.

- [ ] **Step 4: Commit**

Run:

```bash
git add native/include/expo_jsi.h \
  native/packages/jsi/src/ExpoJsiBridge.cpp \
  native/packages/jsi/include/ExpoJsiBridge.h \
  managed/packages/Expo.JSI \
  experiments/hermes-console-hostfxr \
  docs/spike-results/2026-06-27-hermes-console-jsi-hostfxr.md
git commit -m "Add generated-looking JSI module dispatch proof"
```

Expected: commit succeeds. Do not commit if scans found local absolute paths or
if verification did not pass.
