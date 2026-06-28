# JavaScriptPromise Design

Date: 2026-06-28
Repo: `<repo>`

## Context

The bridge already exposes low-level JSI concepts through opaque C ABI handles:
runtime, value, object, array, function, and arguments. The next slice is a
low-level deferred promise wrapper that mirrors the useful first piece of
Swift's `JavaScriptPromise` from `expo-modules-jsi`.

The governing rule remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

This design is deliberately below the module layer. It does not introduce a
module-facing `Promise` argument, typed conversion, coded exception policy, or
source-generator behavior.

## Goal

Add `Expo.JSI.JavaScriptPromise` as an owned low-level wrapper for a real JS
`Promise` object whose resolver and rejecter are retained by native code.

The first slice supports only deferred promises:

```csharp
using var promise = runtime.CreatePromise();
using var value = promise.AsValue();
promise.Resolve(runtime.CreateNumber(42));
promise.Reject(runtime.CreateString("error"));
```

`Resolve` and `Reject` must be called while the caller has valid runtime access,
for example inside `JavaScriptRuntime.Execute`, `ScheduleAsync`, or a host
function callback. Cross-thread settlement is a future feature.

## Non-Goals

Do not build in this slice:

- module-facing `Promise`;
- `AnyArgument` or generated binding conversion;
- `Resolve(object?)`;
- `Reject(string)` or `Reject(Exception)`;
- JS `Error` construction policy;
- wrapping an existing JS promise;
- C# `await` support for JS promises;
- `JavaScriptValue.AsPromise()`;
- platform adapter scheduling changes.

## Public API

Add:

```csharp
public sealed class JavaScriptPromise : IDisposable
{
  public JavaScriptValue AsValue();
  public void Resolve(JavaScriptValue value);
  public void Reject(JavaScriptValue error);
}

public sealed unsafe class JavaScriptRuntime
{
  public JavaScriptPromise CreatePromise();
}
```

Rules:

- `JavaScriptPromise` owns an opaque native promise handle.
- `AsValue()` returns an owned `JavaScriptValue` containing the JS promise
  object.
- `Resolve` and `Reject` accept an existing `JavaScriptValue` and do not consume
  it.
- First settlement wins. Later settlement attempts are no-ops.
- Disposed promises throw `ObjectDisposedException` from public instance methods.

## Native ABI

Add:

```c
typedef struct expo_jsi_promise_t *expo_jsi_promise_handle;

typedef struct expo_jsi_promise_result {
  int32_t ok;
  expo_jsi_promise_handle promise;
  expo_jsi_error error;
} expo_jsi_promise_result;
```

Add function table entries:

```c
create_promise(runtime) -> promise_result
promise_as_value(runtime, promise) -> value_result
promise_resolve(runtime, promise, value) -> error
promise_reject(runtime, promise, value) -> error
release_promise(runtime, promise) -> void
```

Bump the ABI version and managed expected version together.

Native `PromiseHandle` owns:

- the JS `Promise` object;
- optional JS `resolve` function;
- optional JS `reject` function;
- settlement state.

`create_promise` calls the global JS `Promise` constructor with a host executor
function that captures constructor arguments `resolve` and `reject`. Settlement
uses the retained JS function on the runtime thread.

## Testing

Add low-level tests under `Expo.JSI.Tests/Runtime`:

- creating a promise yields a JS-visible `Promise`;
- `AsValue()` can be assigned to `globalThis`;
- `Resolve` fulfills `then`;
- `Reject` triggers `catch`;
- second settlement is ignored;
- disposal increments a promise release counter;
- using a disposed promise throws `ObjectDisposedException`.

Verification commands:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
```

If formatting check fails because files need formatting, run
`scripts/format.sh`, then repeat both commands.
