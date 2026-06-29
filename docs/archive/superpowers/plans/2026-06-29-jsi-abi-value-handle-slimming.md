# JSI ABI Value Handle Slimming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse ordinary object, array, and function native handles into `expo_jsi_value_handle`, and merge promise resolution/rejection into one settlement ABI.

**Architecture:** C++ continues to own all JSI mechanics and validation. The C ABI keeps opaque runtime, value, promise-capability, and arguments handles; managed wrappers remain typed by policy while storing ordinary value handles for objects, arrays, and functions. `JavaScriptPromise` stays capability-backed because it owns resolve/reject state.

**Tech Stack:** C++20 Hermes JSI bridge, C ABI function table, .NET 10 unsafe interop, C# wrapper types, xUnit v3, `scripts/test-jsi.sh`, `scripts/format.sh`.

---

## File Structure

- Modify `docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md`
  - Keep this spec aligned with decisions made during implementation.
- Modify `native/include/expo_jsi.h`
  - Remove object/array/function handle typedefs and result structs.
  - Add `expo_jsi_value_expectation`, `expo_jsi_promise_settlement`, `value_retain_as`, and `promise_settle`.
- Modify `native/packages/jsi/src/ExpoJsiBridge.cpp`
  - Remove `ObjectHandle`, `ArrayHandle`, and `FunctionHandle`.
  - Implement checked object/array/function access over `ValueHandle`.
  - Update API table and version.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
  - Remove object/array/function handle aliases.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
  - Remove object/array/function result structs.
  - Add value expectation and promise settlement enums.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
  - Update function pointer table to the new ABI.
  - Add `RetainValueAs(...)` and `SettlePromise(...)`.
- Modify `managed/packages/Expo.JSI/JavaScriptValueInner.cs`
  - Use `value_retain_as` for validated typed wrapper conversion.
- Modify `managed/packages/Expo.JSI/JavaScriptObjectInner.cs`
  - Store `ExpoJsiValueHandle`.
  - Keep property operations object-centric in C# while calling value-handle ABI.
- Modify `managed/packages/Expo.JSI/JavaScriptArrayInner.cs`
  - Store `ExpoJsiValueHandle`.
  - Keep array operations array-centric in C# while calling value-handle ABI.
- Modify `managed/packages/Expo.JSI/JavaScriptObject.cs`
  - Store and release ordinary value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptArray.cs`
  - Store and release ordinary value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptFunction.cs`
  - Store and release ordinary value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
  - Track temporary object refs as value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`
  - Track temporary array refs as value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptHandleScope.cs`
  - Track only ordinary value handles.
- Modify `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  - Create object/array/function wrappers from value results.
- Modify `managed/packages/Expo.JSI/JavaScriptPromise.cs`
  - Keep public `Resolve` and `Reject`; route both through `promise_settle`.
- Modify `native/testhost/include/expo_jsi_testhost.h`
  - Collapse release counters that only tracked removed handle families.
- Modify `native/testhost/src/ExpoJsiTestHost.cpp`
  - Count ordinary object/array/function wrapper releases through `release_value`.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
  - Match the collapsed counter struct.
- Modify tests under `managed/packages/Expo.JSI.Tests/Runtime/`
  - Update ownership, wrong-type, scoped ref, promise, and counter expectations.
- Modify `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
  - Update function release counter expectation.

## Task 1: Encode The New Semantics In Tests

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/NativeTestHostCounterTests.cs`

- [ ] **Step 1: Add object conversion lifetime coverage**

Add this test to `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`:

```csharp
[Fact]
public void JavaScriptValueAsObjectRetainsAfterValidation()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(_ =>
  {
    using var value = fixture.Evaluate("({ answer: 42 })", "object-retain-as-object.js");
    using var target = value.AsObject();
    value.Dispose();

    using var actual = target.GetProperty("answer");
    Assert.Equal(42, actual.AsDouble());
    return true;
  });
}
```

- [ ] **Step 2: Add object wrong-type coverage**

Add this test to `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`:

```csharp
[Fact]
public void JavaScriptValueAsObjectRejectsNonObjectBeforeReturningWrapper()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var value = runtime.CreateNumber(7);
    var error = Assert.Throws<InvalidOperationException>(() =>
    {
      using var _ = value.AsObject();
    });

    Assert.Contains("object", error.Message, StringComparison.OrdinalIgnoreCase);
    return true;
  });
}
```

- [ ] **Step 3: Add array conversion lifetime coverage**

Add this test to `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`:

```csharp
[Fact]
public void JavaScriptValueAsArrayRetainsAfterValidation()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(_ =>
  {
    using var value = fixture.Evaluate("[10, 20, 30]", "array-retain-as-array.js");
    using var array = value.AsArray();
    value.Dispose();

    Assert.Equal(3u, array.Length);
    using var element = array.GetValue(2);
    Assert.Equal(30, element.AsDouble());
    return true;
  });
}
```

- [ ] **Step 4: Add array wrong-type coverage**

Add this test to `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`:

```csharp
[Fact]
public void JavaScriptValueAsArrayRejectsNonArrayBeforeReturningWrapper()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(_ =>
  {
    using var value = fixture.Evaluate("({ length: 3 })", "array-wrong-type.js");
    var error = Assert.Throws<InvalidOperationException>(() =>
    {
      using var _ = value.AsArray();
    });

    Assert.Contains("array", error.Message, StringComparison.OrdinalIgnoreCase);
    return true;
  });
}
```

- [ ] **Step 5: Update scoped-ref release expectation**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`, replace:

```csharp
Assert.True(fixture.Counters.ReleasedObjects >= 1);
Assert.True(fixture.Counters.ReleasedValues >= 1);
```

with:

```csharp
Assert.True(fixture.Counters.ReleasedValues >= 3);
```

This encodes that temporary object and property traversal releases through the ordinary value release lane.

- [ ] **Step 6: Update object and array release counter tests**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`, replace:

```csharp
Assert.True(counters.ReleasedObjects >= 1);
```

with:

```csharp
Assert.True(counters.ReleasedValues >= 1);
```

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`, replace:

```csharp
Assert.True(fixture.Counters.ReleasedObjects >= 1);
```

with:

```csharp
Assert.True(fixture.Counters.ReleasedValues >= 1);
```

- [ ] **Step 7: Update function release counter test**

In `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`, replace the assertion that checks `ReleasedFunctions` with:

```csharp
Assert.True(counters.ReleasedValues >= 1);
```

- [ ] **Step 8: Keep promise public behavior tests unchanged**

Do not change `ResolveFulfillsPromiseWithProvidedValue`, `RejectRejectsPromiseWithProvidedValue`, or `SecondSettlementIsIgnored` in `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`. These tests must continue to prove public behavior while the ABI underneath changes to `promise_settle`.

- [ ] **Step 9: Run the focused tests and capture the expected failure**

Run:

```sh
scripts/test-jsi.sh --filter "FullyQualifiedName~JavaScriptObjectTests|FullyQualifiedName~JavaScriptArrayTests|FullyQualifiedName~JavaScriptScopedRefTests|FullyQualifiedName~HostFunctionTests"
```

Expected: fail before implementation because counters still expose typed object/array/function release lanes and the new wrong-type/retain semantics still use the old ABI.

- [ ] **Step 10: Commit the red tests**

```sh
git add managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs
git commit -m "test: pin value-handle ABI wrapper semantics"
```

## Task 2: Update The ABI Contract And Managed Interop Types

**Files:**
- Modify: `native/include/expo_jsi.h`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`

- [ ] **Step 1: Remove typed ordinary handle aliases from the C ABI**

In `native/include/expo_jsi.h`, remove C++ aliases and C typedefs for:

```c
expo_jsi_object_handle
expo_jsi_array_handle
expo_jsi_function_handle
```

Keep these aliases:

```c
typedef expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef expo_jsi_value_t *expo_jsi_value_handle;
typedef expo_jsi_promise_t *expo_jsi_promise_handle;
typedef expo_jsi_arguments_t *expo_jsi_arguments_handle;
```

and the matching non-C++ `struct` typedefs.

- [ ] **Step 2: Add value expectation and promise settlement enums**

Add to `native/include/expo_jsi.h` after `expo_jsi_task_priority`:

```c
typedef enum expo_jsi_value_expectation {
  EXPO_JSI_EXPECT_OBJECT = 1,
  EXPO_JSI_EXPECT_ARRAY = 2,
  EXPO_JSI_EXPECT_FUNCTION = 3
} expo_jsi_value_expectation;

typedef enum expo_jsi_promise_settlement {
  EXPO_JSI_PROMISE_RESOLVE = 0,
  EXPO_JSI_PROMISE_REJECT = 1
} expo_jsi_promise_settlement;
```

- [ ] **Step 3: Collapse ordinary typed result structs**

Remove these structs from `native/include/expo_jsi.h`:

```c
expo_jsi_object_result
expo_jsi_array_result
expo_jsi_function_result
```

Keep `expo_jsi_value_result`, `expo_jsi_promise_result`, and `expo_jsi_string_result`.

- [ ] **Step 4: Rewrite ordinary object/array/function typedefs**

Change these typedefs in `native/include/expo_jsi.h` to use `expo_jsi_value_result` and `expo_jsi_value_handle`:

```c
typedef expo_jsi_value_result (*expo_jsi_get_global_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_create_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_value_retain_as_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_value_expectation expectation);

typedef expo_jsi_value_result (*expo_jsi_create_array_fn)(expo_jsi_runtime_handle runtime,
                                                          uint32_t length);

typedef uint32_t (*expo_jsi_array_get_length_fn)(expo_jsi_runtime_handle runtime,
                                                 expo_jsi_value_handle array,
                                                 expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_array_get_value_at_index_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle array, uint32_t index);

typedef expo_jsi_error (*expo_jsi_array_set_value_at_index_fn)(expo_jsi_runtime_handle runtime,
                                                               expo_jsi_value_handle array,
                                                               uint32_t index,
                                                               expo_jsi_value_handle value);

typedef expo_jsi_error (*expo_jsi_object_set_property_fn)(expo_jsi_runtime_handle runtime,
                                                          expo_jsi_value_handle object,
                                                          const char *name,
                                                          int32_t name_len,
                                                          expo_jsi_value_handle value);

typedef expo_jsi_value_result (*expo_jsi_object_get_property_fn)(expo_jsi_runtime_handle runtime,
                                                                 expo_jsi_value_handle object,
                                                                 const char *name,
                                                                 int32_t name_len);

typedef expo_jsi_value_result (*expo_jsi_create_host_function_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);
```

Remove the typedefs for `object_as_value`, `value_as_object`, `array_as_value`, `array_as_object`, `value_as_array`, and `function_as_value`.

- [ ] **Step 5: Merge promise settlement typedefs**

In `native/include/expo_jsi.h`, remove:

```c
expo_jsi_promise_resolve_fn
expo_jsi_promise_reject_fn
```

Add:

```c
typedef expo_jsi_error (*expo_jsi_promise_settle_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_promise_handle promise,
  expo_jsi_promise_settlement settlement,
  expo_jsi_value_handle value);
```

- [ ] **Step 6: Rewrite the API table**

In `native/include/expo_jsi.h`, update `expo_jsi_api` so the relevant fields are:

```c
expo_jsi_get_global_object_fn get_global_object;
expo_jsi_create_object_fn create_object;
expo_jsi_value_retain_as_fn value_retain_as;
expo_jsi_create_array_fn create_array;
expo_jsi_array_get_length_fn array_get_length;
expo_jsi_array_get_value_at_index_fn array_get_value_at_index;
expo_jsi_array_set_value_at_index_fn array_set_value_at_index;
expo_jsi_create_promise_fn create_promise;
expo_jsi_promise_as_value_fn promise_as_value;
expo_jsi_promise_settle_fn promise_settle;
expo_jsi_object_set_property_fn object_set_property;
expo_jsi_object_get_property_fn object_get_property;
expo_jsi_create_host_function_fn create_host_function;
expo_jsi_get_arguments_count_fn get_arguments_count;
expo_jsi_get_argument_value_fn get_argument_value;
expo_jsi_release_promise_fn release_promise;
expo_jsi_release_value_fn release_value;
```

Do not keep `release_object`, `release_array`, or `release_function` fields.

- [ ] **Step 7: Update managed handle aliases**

In `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`, remove:

```csharp
global using ExpoJsiObjectHandle = System.IntPtr;
global using ExpoJsiArrayHandle = System.IntPtr;
global using ExpoJsiFunctionHandle = System.IntPtr;
```

- [ ] **Step 8: Update managed interop types**

In `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`, remove `ExpoJsiObjectResult`, `ExpoJsiArrayResult`, and `ExpoJsiFunctionResult`.

Add:

```csharp
internal enum ExpoJsiValueExpectation : int
{
  Object = 1,
  Array = 2,
  Function = 3,
}

internal enum ExpoJsiPromiseSettlement : int
{
  Resolve = 0,
  Reject = 1,
}
```

- [ ] **Step 9: Update `ExpoJsiApi` function pointers**

In `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`, change object/array/function function pointers to value handles and value results. The core shape should be:

```csharp
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
  ExpoJsiPromiseHandle,
  ExpoJsiPromiseSettlement,
  ExpoJsiValueHandle,
  ExpoJsiError> PromiseSettle;
```

Remove pointer fields for typed conversions and typed releases.

- [ ] **Step 10: Add managed interop wrappers**

In `ExpoJsiApi.cs`, replace `ConvertValueToObject` and `ConvertValueToArray` with:

```csharp
public ExpoJsiValueResult RetainValueAs(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle valueHandle,
    ExpoJsiValueExpectation expectation
)
{
  return ValueRetainAs(runtimeHandle, valueHandle, expectation);
}
```

Replace `ResolvePromise` and `RejectPromise` with:

```csharp
public ExpoJsiError SettlePromise(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiPromiseHandle promiseHandle,
    ExpoJsiPromiseSettlement settlement,
    ExpoJsiValueHandle valueHandle
)
{
  return PromiseSettle(runtimeHandle, promiseHandle, settlement, valueHandle);
}
```

- [ ] **Step 11: Bump the managed expected version**

In `ExpoJsiApi.cs`, change:

```csharp
public const uint ExpectedVersion = 10;
```

to:

```csharp
public const uint ExpectedVersion = 11;
```

- [ ] **Step 12: Run build through the repo test runner and capture expected failure**

Run:

```sh
scripts/test-jsi.sh
```

Expected: fail because native implementation and managed wrappers still reference removed typed handles.

- [ ] **Step 13: Commit ABI contract changes**

```sh
git add native/include/expo_jsi.h managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs
git commit -m "refactor: collapse ordinary JSI handles in ABI"
```

## Task 3: Migrate Native Bridge Implementation

**Files:**
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`

- [ ] **Step 1: Remove typed handle classes**

Delete `ObjectHandle`, `ArrayHandle`, and `FunctionHandle` classes from `native/packages/jsi/src/ExpoJsiBridge.cpp`.

Keep `ValueHandle`, `PromiseHandle`, and `ArgumentsHandle`.

- [ ] **Step 2: Add checked object helper**

Add this helper in the anonymous namespace near other validation helpers:

```cpp
facebook::jsi::Object checkedObject(facebook::jsi::Runtime &runtime,
                                    expo_jsi_value_handle value,
                                    int32_t nullCode,
                                    int32_t typeCode)
{
  if (value == nullptr) {
    throw std::runtime_error("Value handle is null.");
  }

  auto &jsValue = value->value();
  if (!jsValue.isObject()) {
    throw facebook::jsi::JSError(runtime, "Value is not an object.");
  }
  return jsValue.asObject(runtime);
}
```

If local error-code style makes direct throwing awkward, keep the same signature but catch and map at each ABI boundary using existing `makeErrorResult(...)` / `makeError(...)` helpers.

- [ ] **Step 3: Add checked array helper**

Add:

```cpp
facebook::jsi::Array checkedArray(facebook::jsi::Runtime &runtime,
                                  expo_jsi_value_handle value)
{
  auto object = checkedObject(runtime, value, 0, 0);
  if (!object.isArray(runtime)) {
    throw facebook::jsi::JSError(runtime, "Value is not an array.");
  }
  return object.asArray(runtime);
}
```

- [ ] **Step 4: Add checked function helper**

Add:

```cpp
facebook::jsi::Function checkedFunction(facebook::jsi::Runtime &runtime,
                                        expo_jsi_value_handle value)
{
  auto object = checkedObject(runtime, value, 0, 0);
  if (!object.isFunction(runtime)) {
    throw facebook::jsi::JSError(runtime, "Value is not a function.");
  }
  return object.asFunction(runtime);
}
```

- [ ] **Step 5: Implement `valueRetainAs` with validate-first semantics**

Add:

```cpp
expo_jsi_value_result valueRetainAs(expo_jsi_runtime_handle runtime,
                                    expo_jsi_value_handle value,
                                    expo_jsi_value_expectation expectation)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (value == nullptr) {
    return makeErrorResult(38, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    switch (expectation) {
      case EXPO_JSI_EXPECT_OBJECT:
        checkedObject(jsRuntime, value, 0, 0);
        break;
      case EXPO_JSI_EXPECT_ARRAY:
        checkedArray(jsRuntime, value);
        break;
      case EXPO_JSI_EXPECT_FUNCTION:
        checkedFunction(jsRuntime, value);
        break;
      default:
        return makeErrorResult(39, "Unknown value expectation.");
    }

    return makeValueResult(expo::jsi::ValueHandle::owned(
      facebook::jsi::Value(jsRuntime, value->value())));
  } catch (const std::exception &ex) {
    return makeErrorResult(40, ex.what());
  } catch (...) {
    return makeErrorResult(41, "Unknown native exception while retaining checked value.");
  }
}
```

Preserve existing error text where possible if tests assert it.

- [ ] **Step 6: Return value handles from factories**

Change `getGlobalObject`, `createObject`, `createArray`, and `createHostFunction` to return `expo_jsi_value_result`.

Use these return expressions:

```cpp
return makeValueResult(expo::jsi::ValueHandle::owned(
  facebook::jsi::Value(runtimeHandle->runtime(), runtimeHandle->runtime().global())));
```

```cpp
return makeValueResult(expo::jsi::ValueHandle::owned(
  facebook::jsi::Value(runtimeHandle->runtime(),
                       facebook::jsi::Object(runtimeHandle->runtime()))));
```

```cpp
auto array = facebook::jsi::Array(runtimeHandle->runtime(), length);
return makeValueResult(expo::jsi::ValueHandle::owned(
  facebook::jsi::Value(runtimeHandle->runtime(), array)));
```

```cpp
return makeValueResult(expo::jsi::ValueHandle::owned(
  facebook::jsi::Value(runtimeHandle->runtime(), std::move(function))));
```

- [ ] **Step 7: Rewrite object operations over value handles**

In `objectSetProperty`, replace `object->object()` usage with:

```cpp
auto object = checkedObject(jsRuntime, objectValue, 0, 0);
object.setProperty(jsRuntime, propertyName, value->value());
```

In `objectGetProperty`, replace `object->object()` usage with:

```cpp
auto object = checkedObject(jsRuntime, objectValue, 0, 0);
return makeValueResult(
  expo::jsi::ValueHandle::owned(object.getProperty(jsRuntime, propertyName)));
```

- [ ] **Step 8: Rewrite array operations over value handles**

Use `checkedArray(...)` in `arrayGetLength`, `arrayGetValueAtIndex`, and `arraySetValueAtIndex`:

```cpp
auto array = checkedArray(runtimeHandle->runtime(), arrayValue);
return static_cast<uint32_t>(array.length(runtimeHandle->runtime()));
```

```cpp
auto array = checkedArray(runtimeHandle->runtime(), arrayValue);
return makeValueResult(expo::jsi::ValueHandle::owned(
  array.getValueAtIndex(runtimeHandle->runtime(), index)));
```

```cpp
auto array = checkedArray(runtimeHandle->runtime(), arrayValue);
array.setValueAtIndex(runtimeHandle->runtime(), index, value->value());
return makeOk();
```

- [ ] **Step 9: Merge promise settlement**

Replace `promiseResolve` and `promiseReject` with:

```cpp
expo_jsi_error promiseSettle(expo_jsi_runtime_handle runtime,
                             expo_jsi_promise_handle promise,
                             expo_jsi_promise_settlement settlement,
                             expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(91, "Runtime handle is invalid.");
  }
  if (promise == nullptr) {
    return makeError(92, "Promise handle is null.");
  }
  if (value == nullptr) {
    return makeError(93, "Value handle is null.");
  }

  try {
    if (settlement == EXPO_JSI_PROMISE_REJECT) {
      promise->reject(runtimeHandle->runtime(), value->value());
    } else {
      promise->resolve(runtimeHandle->runtime(), value->value());
    }
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(94, ex.what());
  } catch (...) {
    return makeError(95, "Unknown native exception while settling promise.");
  }
}
```

- [ ] **Step 10: Update native API version and table**

Change:

```cpp
constexpr uint32_t kApiVersion = 10;
```

to:

```cpp
constexpr uint32_t kApiVersion = 11;
```

Update `kApi` entries to match the header order and use `valueRetainAs` and `promiseSettle`.

- [ ] **Step 11: Remove dead conversion and release functions**

Delete native functions for:

```text
objectAsValue
valueAsObject
arrayAsValue
arrayAsObject
valueAsArray
functionAsValue
releaseObject
releaseArray
releaseFunction
```

Keep `releaseValue`; it must remain safe for borrowed argument handles.

- [ ] **Step 12: Run native build through the test script**

Run:

```sh
scripts/test-jsi.sh
```

Expected: native bridge should compile farther; managed wrappers may still fail because they still store typed handles.

- [ ] **Step 13: Commit native bridge migration**

```sh
git add native/packages/jsi/src/ExpoJsiBridge.cpp
git commit -m "refactor: store ordinary JSI wrappers as value handles natively"
```

## Task 4: Migrate Managed Wrappers To Value Handles

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptValueInner.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptObjectInner.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArrayInner.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptObject.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArray.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptFunction.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptHandleScope.cs`

- [ ] **Step 1: Update `JavaScriptValueInner` typed conversions**

In `JavaScriptValueInner.cs`, change `AsObject()` and `AsArray()` to return `ExpoJsiValueHandle`:

```csharp
public ExpoJsiValueHandle AsObject()
{
  var result = Context.Api->RetainValueAs(
      Context.RuntimeHandle,
      Handle,
      ExpoJsiValueExpectation.Object
  );
  if (result.Ok == 0 || result.Value == 0)
  {
    JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to object.");
  }
  return result.Value;
}

public ExpoJsiValueHandle AsArray()
{
  var result = Context.Api->RetainValueAs(
      Context.RuntimeHandle,
      Handle,
      ExpoJsiValueExpectation.Array
  );
  if (result.Ok == 0 || result.Value == 0)
  {
    JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to array.");
  }
  return result.Value;
}
```

- [ ] **Step 2: Update `JavaScriptObjectInner`**

Change the constructor and `Handle` property to `ExpoJsiValueHandle`.

Change `AsValue()` to retain:

```csharp
public ExpoJsiValueHandle AsValue()
{
  var result = Context.Api->CloneJavaScriptValue(Context.RuntimeHandle, Handle);
  if (result.Ok == 0 || result.Value == 0)
  {
    JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript object value.");
  }
  return result.Value;
}
```

Keep `GetProperty` and `SetProperty` object-centric, but pass `Handle` as an ordinary value handle to the ABI.

- [ ] **Step 3: Update `JavaScriptArrayInner`**

Change the constructor and `Handle` property to `ExpoJsiValueHandle`.

Remove `AsObject()` unless a current caller still needs it. If `JavaScriptArray.AsObject()` remains public, implement it with `RetainValueAs(..., ExpoJsiValueExpectation.Object)` over the stored value handle.

Change `AsValue()` to clone:

```csharp
public ExpoJsiValueHandle AsValue()
{
  var result = Context.Api->CloneJavaScriptValue(Context.RuntimeHandle, Handle);
  if (result.Ok == 0 || result.Value == 0)
  {
    JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript array value.");
  }
  return result.Value;
}
```

- [ ] **Step 4: Update owned object wrapper**

In `JavaScriptObject.cs`, change:

```csharp
private ExpoJsiObjectHandle handle;
internal JavaScriptObject(JsiContext context, ExpoJsiObjectHandle handle)
```

to:

```csharp
private ExpoJsiValueHandle handle;
internal JavaScriptObject(JsiContext context, ExpoJsiValueHandle handle)
```

Change `Dispose()` to call:

```csharp
context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
```

- [ ] **Step 5: Update owned array wrapper**

In `JavaScriptArray.cs`, change the stored handle and constructor to `ExpoJsiValueHandle`.

Change `Dispose()` to call:

```csharp
context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
```

If `AsObject()` remains public, use the updated `Inner.AsObject()` and return `new JavaScriptObject(context, valueHandle)`.

- [ ] **Step 6: Update owned function wrapper**

In `JavaScriptFunction.cs`, change the stored handle and constructor to `ExpoJsiValueHandle`.

Change `AsValue()` to clone the stored value:

```csharp
var result = context.Api->CloneJavaScriptValue(context.RuntimeHandle, handle);
if (result.Ok == 0 || result.Value == 0)
{
  JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript function value.");
}
return JavaScriptValue.FromOwnedHandle(context, result.Value);
```

Change `Dispose()` to call `ReleaseValueHandle`.

- [ ] **Step 7: Collapse `JavaScriptHandleScope` tracking**

In `JavaScriptHandleScope.cs`, remove `objects`, `arrays`, `TrackObject`, and `TrackArray`.

Keep only:

```csharp
private List<ExpoJsiValueHandle>? values;

public ExpoJsiValueHandle TrackValue(ExpoJsiValueHandle handle)
{
  ThrowIfDisposed();
  if (handle != 0)
  {
    values ??= [];
    values.Add(handle);
  }
  return handle;
}
```

In `Dispose()`, release only `values` through `ReleaseValueHandle`.

- [ ] **Step 8: Update ref wrappers**

In `JavaScriptObjectRef.cs`, change `FromScopedHandle` to accept `ExpoJsiValueHandle` and track it with `TrackValue`.

In `JavaScriptArrayRef.cs`, change `FromScopedHandle` to accept `ExpoJsiValueHandle` and track it with `TrackValue`.

The factories should look like:

```csharp
internal static JavaScriptObjectRef FromScopedHandle(
    JavaScriptHandleScope scope,
    JsiContext context,
    ExpoJsiValueHandle handle
) => new(scope, new JavaScriptObjectInner(context, scope.TrackValue(handle)));
```

- [ ] **Step 9: Update runtime factories**

In `JavaScriptRuntime.cs`, update `Global()`, `CreateObject()`, `CreateArray()`, and `CreateHostFunction(...)` to read `result.Value` and create typed wrappers from that value handle.

Example:

```csharp
var result = context.Api->CreateObjectValue(context.RuntimeHandle);
if (result.Ok == 0 || result.Value == 0)
{
  JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript object.");
}
return new JavaScriptObject(context, result.Value);
```

- [ ] **Step 10: Run focused managed build through test script**

Run:

```sh
scripts/test-jsi.sh
```

Expected: compile should move past removed typed handles. Remaining failures should be testhost counters or promise settlement if not yet migrated.

- [ ] **Step 11: Commit managed wrapper migration**

```sh
git add managed/packages/Expo.JSI/JavaScriptValueInner.cs managed/packages/Expo.JSI/JavaScriptObjectInner.cs managed/packages/Expo.JSI/JavaScriptArrayInner.cs managed/packages/Expo.JSI/JavaScriptObject.cs managed/packages/Expo.JSI/JavaScriptArray.cs managed/packages/Expo.JSI/JavaScriptFunction.cs managed/packages/Expo.JSI/JavaScriptObjectRef.cs managed/packages/Expo.JSI/JavaScriptArrayRef.cs managed/packages/Expo.JSI/JavaScriptRuntime.cs managed/packages/Expo.JSI/JavaScriptHandleScope.cs
git commit -m "refactor: back typed managed JSI wrappers with value handles"
```

## Task 5: Migrate Promise Settlement And Testhost Counters

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptPromise.cs`
- Modify: `native/testhost/include/expo_jsi_testhost.h`
- Modify: `native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/NativeTestHostCounterTests.cs`

- [ ] **Step 1: Update managed promise settlement**

In `JavaScriptPromise.cs`, replace the conditional call to `ResolvePromise` / `RejectPromise` with:

```csharp
var error = context.Api->SettlePromise(
    context.RuntimeHandle,
    handle,
    reject ? ExpoJsiPromiseSettlement.Reject : ExpoJsiPromiseSettlement.Resolve,
    value.Handle
);
context.ThrowIfError(error, reject
    ? "Failed to reject JavaScript promise."
    : "Failed to resolve JavaScript promise.");
```

- [ ] **Step 2: Collapse testhost counter struct**

In `native/testhost/include/expo_jsi_testhost.h`, remove:

```c
uint32_t released_objects;
uint32_t released_functions;
```

Keep:

```c
uint32_t released_values;
uint32_t released_promises;
uint32_t released_strings;
uint32_t released_task_contexts;
uint32_t sync_execute_calls;
```

- [ ] **Step 3: Remove counted typed release functions**

In `native/testhost/src/ExpoJsiTestHost.cpp`, delete `countedReleaseObject`, `countedReleaseArray`, and `countedReleaseFunction`.

In `makeCountedApi`, remove assignments to:

```cpp
runtime.countedApi.release_object
runtime.countedApi.release_array
runtime.countedApi.release_function
```

Keep `countedReleaseValue`, `countedReleasePromise`, string, and task wrappers.

- [ ] **Step 4: Update managed counter struct**

In `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`, remove:

```csharp
public readonly uint ReleasedObjects;
public readonly uint ReleasedFunctions;
```

Keep the remaining fields in native struct order:

```csharp
public readonly uint ReleasedValues;
public readonly uint ReleasedPromises;
public readonly uint ReleasedStrings;
public readonly uint ReleasedTaskContexts;
public readonly uint SyncExecuteCalls;
```

- [ ] **Step 5: Verify runtime counter isolation stays value-based**

`NativeTestHostCounterTests.ReleaseCountersStayAttachedToTheirRuntime` already checks `ReleasedValues`; keep that assertion unchanged.

- [ ] **Step 6: Run the promise and counter tests**

Run:

```sh
scripts/test-jsi.sh --filter "FullyQualifiedName~JavaScriptPromiseTests|FullyQualifiedName~NativeTestHostCounterTests"
```

Expected: pass. If the script does not support `--filter`, run the full `scripts/test-jsi.sh` and inspect the named tests in the output.

- [ ] **Step 7: Commit promise and counter migration**

```sh
git add managed/packages/Expo.JSI/JavaScriptPromise.cs native/testhost/include/expo_jsi_testhost.h native/testhost/src/ExpoJsiTestHost.cpp managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs managed/packages/Expo.JSI.Tests/Runtime/NativeTestHostCounterTests.cs
git commit -m "refactor: merge promise settlement and collapse release counters"
```

## Task 6: Remove Dead Interop Names And Verify The Whole Slice

**Files:**
- Modify: any files found by the searches below
- Modify: `docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md` if implementation details differ from the spec

- [ ] **Step 1: Search for removed native ABI names**

Run:

```sh
rg -n "expo_jsi_(object|array|function)_handle|expo_jsi_(object|array|function)_result|object_as_value|value_as_object|array_as_value|array_as_object|value_as_array|function_as_value|release_object|release_array|release_function|promise_resolve|promise_reject" native managed
```

Expected: no production hits. Historical docs and old plans may still mention old names; do not edit historical docs unless they claim to describe the current ABI.

- [ ] **Step 2: Search for removed managed aliases and counters**

Run:

```sh
rg -n "ExpoJsi(Object|Array|Function)(Handle|Result)|ReleasedObjects|ReleasedFunctions|ResolvePromise|RejectPromise|PromiseResolve|PromiseReject|Release(Object|Array|Function)Handle" managed native
```

Expected: no production or current-test hits.

- [ ] **Step 3: Run full Hermes-backed JSI suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all tests pass.

- [ ] **Step 4: Run formatting check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: formatting check passes.

If it fails because files need formatting, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

Expected: formatting check passes after formatting.

- [ ] **Step 5: Run diff whitespace check**

Run:

```sh
git diff --check
```

Expected: no output and exit code 0.

- [ ] **Step 6: Commit cleanup and verification**

```sh
git add native/include/expo_jsi.h native/packages/jsi/src/ExpoJsiBridge.cpp native/testhost/include/expo_jsi_testhost.h native/testhost/src/ExpoJsiTestHost.cpp managed/packages/Expo.JSI managed/packages/Expo.JSI.Tests docs/superpowers/specs/2026-06-29-jsi-abi-value-handle-slimming-design.md
git commit -m "test: verify value-handle ABI slimming"
```

## Self-Review Checklist

- Spec coverage: approach B is implemented by Tasks 2-4; promise settlement merge is implemented by Tasks 2, 3, and 5; validate-first clone-after-success is implemented by Task 3 Step 5 and Task 4 Step 1; collapsed counters are implemented by Task 5.
- Placeholder scan: every step names concrete files, commands, or code snippets.
- Type consistency: public wrappers remain `JavaScriptObject`, `JavaScriptArray`, `JavaScriptFunction`, and `JavaScriptPromise`; object/array/function native storage becomes `ExpoJsiValueHandle`; `JavaScriptPromise` remains `ExpoJsiPromiseHandle`.
- Verification: final proof is `scripts/test-jsi.sh`, `scripts/format.sh --check --all`, and `git diff --check`.
