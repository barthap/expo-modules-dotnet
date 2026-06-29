# JavaScriptPromise Design

Date: 2026-06-28
Repo: `<repo>`

## Context

The bridge already exposes low-level JSI concepts through opaque C ABI handles:
runtime, value, object, array, function, arguments, and deferred promises. The
next slice keeps that low-level promise primitive, then adds the C#-natural
shape for bridging managed `Task` work to a JS `Promise`.

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
`Promise` object whose resolver and rejecter are retained by native code, and
add a separate promise-value wrapper for managed async work.

The first slice supports only deferred promises:

```csharp
using var promise = runtime.CreatePromise();
using var value = promise.AsValue();
promise.Resolve(runtime.CreateNumber(42));
promise.Reject(runtime.CreateString("error"));
```

`Resolve` and `Reject` must be called while the caller has valid runtime access,
for example inside `JavaScriptRuntime.Execute`, `ScheduleAsync`, or a host
function callback.

The C#-natural helper creates a JS promise immediately, runs managed async work,
then schedules back onto the runtime thread to create the JS fulfillment or
rejection value:

```csharp
using var promise = runtime.CreatePromise(async cancellationToken =>
{
  var result = await service.LoadAsync(cancellationToken);
  return JavaScriptPromiseResult.Resolve(js => js.CreateString(result));
});

using var value = promise.AsValue();
```

## Non-Goals

Do not build in this slice:

- module-facing `Promise`;
- `AnyArgument` or generated binding conversion;
- `Resolve(object?)`;
- `Reject(string)` or `Reject(Exception)`;
- wrapping an existing JS promise;
- C# `await` support for JS promises;
- `JavaScriptValue.AsPromise()`;
- platform adapter scheduling changes.

## Public API

Add:

```csharp
public interface IJavaScriptValueRepresentable
{
  JavaScriptValue AsValue();
}

public sealed class JavaScriptPromise : IJavaScriptValueRepresentable, IDisposable
{
  public JavaScriptValue AsValue();
  public void Resolve(JavaScriptValue value);
  public void Reject(JavaScriptValue error);
}

public sealed class JavaScriptPromiseValue : IJavaScriptValueRepresentable, IDisposable
{
  public JavaScriptValue AsValue();
}

public sealed class JavaScriptErrorObject : IJavaScriptValueRepresentable, IDisposable
{
  public string Name { get; }
  public string Message { get; }
  public string? Stack { get; }
  public JavaScriptValue AsValue();
}

public readonly struct JavaScriptPromiseResult
{
  public static JavaScriptPromiseResult Resolve(
    Func<JavaScriptRuntime, JavaScriptValue> createValue);

  public static JavaScriptPromiseResult Reject(
    Func<JavaScriptRuntime, JavaScriptValue> createReason);
}

public sealed unsafe class JavaScriptRuntime
{
  public JavaScriptPromise CreatePromise();
  public JavaScriptPromiseValue CreatePromise(
    Func<CancellationToken, Task<JavaScriptPromiseResult>> operation,
    CancellationToken cancellationToken = default);
  public JavaScriptErrorObject CreateErrorObject(string message);
}
```

Rules:

- `JavaScriptPromise` owns an opaque native promise handle.
- `IJavaScriptValueRepresentable.AsValue()` returns a fresh owned
  `JavaScriptValue` that the caller may dispose.
- `JavaScriptValue` implements `IJavaScriptValueRepresentable` by cloning the
  underlying JS value, never by returning `this`.
- `Resolve` and `Reject` accept an existing `JavaScriptValue` and do not consume
  it.
- First settlement wins. Later settlement attempts are no-ops.
- Disposed promises throw `ObjectDisposedException` from public instance methods.
- `JavaScriptPromiseValue` owns only the returned promise value. The async helper
  owns and disposes the settlement-capability promise handle internally.
- Exceptions thrown by the managed async operation reject the JS promise with a
  real JS `Error` value created from the exception message.
- `JavaScriptErrorObject` represents a JS `Error` object value, not managed
  throwable semantics. Reserve `JavaScriptError` / `JavaScriptException` naming
  for future thrown-value snapshot or managed exception APIs.
- `JavaScriptErrorObject.Name` and `Message` best-effort coerce present values
  with JS string conversion and return an empty string for absent or null
  properties. `Stack` best-effort coerces present values and returns null for
  absent or null stack properties.
- `JavaScriptValue.IsPromise` / `AsPromiseValue()` and `IsError` /
  `AsErrorObject()` use native same-runtime `instanceof` validation.

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
clone_value(runtime, value) -> value_result
create_error(runtime, message, message_len) -> value_result
is_promise(runtime, value, error*) -> bool
is_error(runtime, value, error*) -> bool
coerce_to_string(runtime, value) -> string_result
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
- managed `Task` completion resolves a JS promise through a runtime-created JS
  value;
- managed `Task` exceptions reject with a JS `Error`;
- `JavaScriptValue.AsValue()` returns a disposable clone and does not dispose the
  original value;
- `JavaScriptErrorObject` creates a JS-visible `Error` and exposes
  Error-specific properties;
- `JavaScriptValue` can validate and wrap Promise/Error values;
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
