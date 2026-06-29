# JSI ABI Value Handle Slimming Design

Date: 2026-06-29
Repo: `<repo>`

## Context

The bridge currently exposes separate opaque handle families for JavaScript
values, objects, arrays, promises, functions, and host-function arguments:

```text
expo_jsi_value_handle
expo_jsi_object_handle
expo_jsi_array_handle
expo_jsi_promise_handle
expo_jsi_function_handle
expo_jsi_arguments_handle
```

That shape grew naturally as each wrapper was added, but it now creates bridge
surface that mostly converts between typed handles:

```text
object_as_value
value_as_object
array_as_value
array_as_object
value_as_array
function_as_value
```

It also forces separate release functions and result structs for object, array,
and function handles.

The governing architecture does not change:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

C# must still never observe raw `facebook::jsi::Runtime`,
`facebook::jsi::Value`, `facebook::jsi::Object`, `facebook::jsi::Array`, or
`facebook::jsi::Function` layouts.

## Assumptions

- The current low-level public wrappers should remain:
  `JavaScriptValue`, `JavaScriptObject`, `JavaScriptArray`,
  `JavaScriptFunction`, and `JavaScriptPromise`.
- Public ownership semantics matter more than preserving the current native
  handle taxonomy.
- The ABI may break in this research phase if the new shape is simpler and the
  tests prove equivalent behavior.
- Promise capability is different from an ordinary JavaScript promise value
  because it owns resolve/reject functions and settlement state.
- Host-function arguments remain a distinct call-scoped handle because they
  represent an argument list, not one JavaScript value.

## Goal

Make `expo_jsi_value_handle` the normal ABI carrier for all ordinary JavaScript
values, including objects, arrays, and functions.

The C# API should still feel typed:

```csharp
using var array = runtime.CreateArray();
using var value = array.AsValue();

var length = array.Length;
var first = array.GetValue(0);
```

But internally `JavaScriptArray` should hold an owned value handle whose native
payload is validated as an array when array-specific operations run.

The main simplification is:

```text
JavaScriptObject    -> owns ExpoJsiValueHandle
JavaScriptArray     -> owns ExpoJsiValueHandle
JavaScriptFunction  -> owns ExpoJsiValueHandle
JavaScriptValue     -> owns ExpoJsiValueHandle
```

`JavaScriptPromise` remains separate because it owns a promise capability:

```text
JavaScriptPromise   -> owns ExpoJsiPromiseHandle
```

## Non-Goals

Do not build in this slice:

- raw C++ JSI layout exposure to C#;
- a tagged universal `void *` handle that C# can route manually;
- public wrapper collapse into only `JavaScriptValue`;
- function invocation support beyond the existing host-function creation and
  value conversion behavior;
- source-generator or `Expo.ModulesCore` changes;
- ArrayBuffer support;
- a native ref-scope or `value_ref_*` ABI surface;
- finalizers as the primary release mechanism;
- platform adapter changes.

## Alternatives Considered

### A. Keep Current Typed Native Handles

This keeps maximum native-side type vocabulary, but the API table keeps growing
with conversions and release functions. It preserves implementation inertia,
not a clear semantic boundary.

### B. Collapse Object, Array, And Function Into Value Handles

This keeps C++ in charge of all JSI validation while making the ABI smaller.
Managed wrappers stay typed, but their native storage is one ordinary owned
value handle.

This is the recommended direction.

### C. Collapse Promise Capability Too

This is rejected. A promise capability is not only the JS promise object. It
also owns resolve/reject functions and settlement state, so it should remain a
separate opaque handle.

## Proposed ABI Shape

Remove these ordinary JS handle typedefs:

```c
typedef struct expo_jsi_object_t *expo_jsi_object_handle;
typedef struct expo_jsi_array_t *expo_jsi_array_handle;
typedef struct expo_jsi_function_t *expo_jsi_function_handle;
```

Keep:

```c
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
typedef struct expo_jsi_promise_t *expo_jsi_promise_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;
```

Remove typed result structs for ordinary object, array, and function handles:

```c
expo_jsi_object_result
expo_jsi_array_result
expo_jsi_function_result
```

Factories and conversions should return `expo_jsi_value_result` when the
created thing is an ordinary JavaScript value:

```c
create_object(runtime) -> value_result
create_array(runtime, length) -> value_result
create_host_function(...) -> value_result
get_global_object(runtime) -> value_result
```

Object, array, and function operations should accept `expo_jsi_value_handle`
and validate the expected kind inside native code:

```c
object_set_property(runtime, object_value, name, name_len, value) -> error
object_get_property(runtime, object_value, name, name_len) -> value_result

array_get_length(runtime, array_value, error*) -> uint32
array_get_value_at_index(runtime, array_value, index) -> value_result
array_set_value_at_index(runtime, array_value, index, value) -> error
```

Typed managed conversions from `JavaScriptValue` back to object, array, or
function wrappers still need native validation. Replace the current typed-handle
conversion functions with one checked-retain operation:

```c
typedef enum expo_jsi_value_expectation {
  EXPO_JSI_EXPECT_OBJECT = 1,
  EXPO_JSI_EXPECT_ARRAY = 2,
  EXPO_JSI_EXPECT_FUNCTION = 3
} expo_jsi_value_expectation;

value_retain_as(runtime, value, expectation) -> value_result
```

`EXPO_JSI_EXPECT_OBJECT` accepts any JavaScript object value. Arrays and
functions still satisfy object semantics for object property operations.
`EXPO_JSI_EXPECT_ARRAY` and `EXPO_JSI_EXPECT_FUNCTION` are stricter checks.

Remove conversion functions made unnecessary by the unified ordinary value
handle:

```text
object_as_value
value_as_object
array_as_value
array_as_object
value_as_array
function_as_value
```

`value_retain_as` is the single replacement for the validation-and-clone half
of `value_as_object` and `value_as_array`. `object_as_value`,
`array_as_value`, and `function_as_value` disappear entirely because those
wrappers already store value handles.

Remove release functions made unnecessary by the unified ordinary value handle:

```text
release_object
release_array
release_function
```

`release_value` becomes the only release function for ordinary JS wrappers.

## Promise Capability

Keep `expo_jsi_promise_handle` and `expo_jsi_promise_result`.

`create_promise(runtime)` should continue returning a promise capability handle.
`promise_as_value(runtime, promise)` remains necessary because it converts the
capability's promise object into an ordinary owned JavaScript value.

Merge promise settlement while changing the ABI:

```c
typedef enum expo_jsi_promise_settlement {
  EXPO_JSI_PROMISE_RESOLVE = 0,
  EXPO_JSI_PROMISE_REJECT = 1
} expo_jsi_promise_settlement;

promise_settle(runtime, promise, settlement, value) -> error
```

This replaces `promise_resolve` and `promise_reject`. The managed
`JavaScriptPromise.Resolve(...)` and `JavaScriptPromise.Reject(...)` APIs should
remain separate public methods that call the merged ABI with different
settlement values.

## Native Bridge Direction

`ValueHandle` should become the owning native box for every ordinary JS value.
It should be able to hold an owned or borrowed `facebook::jsi::Value`, as it
does today.

Native helpers should provide checked accessors:

```cpp
facebook::jsi::Object checkedObject(facebook::jsi::Runtime &, ValueHandle &);
facebook::jsi::Array checkedArray(facebook::jsi::Runtime &, ValueHandle &);
facebook::jsi::Function checkedFunction(facebook::jsi::Runtime &, ValueHandle &);
```

These helpers should:

- reject null handles;
- reject wrong JavaScript kinds with structured ABI errors;
- keep all JSI operations and exception handling in C++;
- avoid exposing typed native object/array/function boxes to C#.

`ObjectHandle`, `ArrayHandle`, and `FunctionHandle` can be removed after their
callers migrate.

## Managed Wrapper Direction

Keep public wrapper types. Change their stored native handle type:

```text
JavaScriptObject    ExpoJsiObjectHandle   -> ExpoJsiValueHandle
JavaScriptArray     ExpoJsiArrayHandle    -> ExpoJsiValueHandle
JavaScriptFunction  ExpoJsiFunctionHandle -> ExpoJsiValueHandle
```

`JavaScriptObject.AsValue()`, `JavaScriptArray.AsValue()`, and
`JavaScriptFunction.AsValue()` should become managed retains/clones of their
stored value handle, not ABI-level typed-handle conversions.

`JavaScriptValue.AsObject()` and `JavaScriptValue.AsArray()` should call
`value_retain_as` and return typed managed wrappers that own cloned value
handles. Native should validate the expected shape first, then clone only after
validation succeeds. The operation still matters publicly because it validates
the expected shape and creates a typed wrapper with its own disposal
responsibility.

`JavaScriptHandleScope` should track one ordinary value-handle list for
temporary refs instead of separate value/object/array lists.

The `Inner` pattern remains the implementation boundary. `Inner` structs own
ABI calls and validation; public wrappers choose ownership policy.

## Host Function Arguments

`expo_jsi_arguments_handle` should remain. It represents the call-scoped list
of arguments and can return `expo_jsi_value_handle` for a specific argument.

Borrowed argument values must stay call-scoped. `release_value` should continue
to ignore borrowed value handles or otherwise be safe for the current borrowed
argument path.

`JavaScriptArguments.GetValue(index)` should continue returning
`JavaScriptValueRef`, backed by the current active `JavaScriptHandleScope`.

## Error Handling

The ABI should keep the existing structured error style:

- result structs for handles with `ok`, handle, and `error`;
- out-parameter errors for primitive reads where the return value has a valid
  zero/false state;
- no C++ exceptions crossing the C ABI;
- no managed exceptions crossing into C++ callbacks without conversion.

Wrong-type errors should become more important after this migration because
object and array operations receive ordinary value handles.

## Migration Outline

The later implementation plan should sequence this carefully:

1. Add tests that prove typed wrappers still dispose exactly once and wrong-type
   calls fail loudly.
2. Change the ABI header and managed interop declarations to value-handle-first
   signatures.
3. Migrate native implementation from typed object/array/function handles to
   checked operations over `ValueHandle`.
4. Migrate managed `Inner` structs and public wrappers to store value handles.
5. Collapse `JavaScriptHandleScope` temporary tracking to value handles.
6. Remove unused typed result structs, release functions, conversions, and
   native handle classes.
7. Merge `promise_resolve` and `promise_reject` into `promise_settle`.
8. Bump the ABI version and managed expected version together.
9. Run the Hermes-backed suite and formatting checks.

Do not split the codebase into a half-migrated state where some public wrappers
own typed handles and others own value handles unless a temporary commit needs
that state for verification.

## Testing

Required behavior tests:

- `CreateObject`, `CreateArray`, `CreateHostFunction`, and `Global` still return
  typed managed wrappers.
- `JavaScriptObject.AsValue()`, `JavaScriptArray.AsValue()`, and
  `JavaScriptFunction.AsValue()` return independently owned values.
- `JavaScriptValue.AsObject()` and `JavaScriptValue.AsArray()` return typed
  wrappers that can be used after the source value is disposed, if retained.
- object property get/set still works through owned and scoped refs;
- array length, get, and set still work through owned and scoped refs;
- host-function `thisValue` and arguments still use scoped refs safely;
- release counters prove ordinary object/array/function wrappers release via
  `release_value`;
- wrong-type object/array operations surface structured native errors;
- promise creation, `AsValue()`, resolve, reject, and disposal keep current
  behavior.

Verification commands:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

If formatting check fails because files need formatting, run
`scripts/format.sh`, then repeat the checks.

## Success Criteria

- The production ABI no longer exposes object, array, or function handle
  typedefs.
- The production ABI no longer exposes object, array, or function result
  structs.
- The production ABI has one release function for ordinary JS values:
  `release_value`.
- Public typed wrappers still exist and keep their current ownership contract.
- `JavaScriptPromise` remains capability-backed and separate from ordinary
  value wrappers.
- `JavaScriptHandleScope` tracks only ordinary value handles for temporary ref
  traversal.
- The native bridge still owns all JSI mechanics and catches all native
  exceptions.
- Managed code still consumes only opaque handles and ABI function pointers.
- `ExpoJsiApi.ExpectedVersion` matches the native ABI version.
- Required tests and verification commands pass.

## Resolved Decisions

- Use approach B: collapse object, array, and function native handles into
  ordinary value handles.
- Merge `promise_resolve` and `promise_reject` into `promise_settle` in the same
  ABI migration.
- Validate `JavaScriptValue.AsObject()` and `JavaScriptValue.AsArray()` first,
  then clone only after success.
- Rename internal managed fields only where it makes the migration clearer.
- Collapse test release counters where type-specific counters no longer map to
  meaningful production ABI behavior.
