# JSI Scoped Ref Ownership Design

Date: 2026-06-28
Repo: `<repo>`

## Context

The bridge currently exposes owned wrappers for durable JavaScript handles:
`JavaScriptValue`, `JavaScriptObject`, `JavaScriptArray`,
`JavaScriptFunction`, and `JavaScriptPromise`. Those wrappers are disposable
because native C++ owns the actual JSI mechanics and C# owns opaque handles that
must be released exactly once.

The bridge also exposes `JavaScriptBorrowedValue` for host-function arguments,
but borrowedness is shallow. Primitive reads are non-owning, while object and
array traversal immediately returns owned disposable wrappers. That forces code
like:

```csharp
using var value = holder.AsValue();
using var objectValue = value.AsObject();
using var property = objectValue.GetProperty("message");
return property.Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null
    ? null
    : property.CoerceToString();
```

The governing rule remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

This design changes wrapper ownership ergonomics without exposing raw JSI
layouts to C#.

## Goal

Add a scoped, non-disposable ref lane for temporary JavaScript inspection:

```csharp
var property = holder.Ref.AsObject().GetProperty("message");
return property.IsNullish ? null : property.CoerceToString();
```

Owned wrappers remain direct and ergonomic:

```csharp
using var objectValue = value.AsObject();
using var property = objectValue.GetProperty("name");
```

Receiver ownership decides result ownership:

```text
owned.AsObject() -> owned JavaScriptObject
ref.AsObject() -> JavaScriptObjectRef
```

## Non-Goals

Do not build in this slice:

- finalizers as the primary release mechanism;
- per-type or per-property native helpers such as `get_error_message`;
- public `JavaScriptBorrowedObject`, `JavaScriptBorrowedArray`, or
  `JavaScriptBorrowedFunction` classes;
- a hidden `ownsHandle` mode on one public wrapper type as the main model;
- module source-generator changes beyond updating temporary proof tests;
- async-safe refs;
- refs that can be stored in fields, captured by lambdas, or returned from
  runtime access callbacks.

## Public API Direction

Break the current public API if needed.

Keep owned wrappers:

```csharp
public sealed class JavaScriptValue : IJavaScriptValueRepresentable, IDisposable
{
  public JavaScriptValueKind Kind { get; }
  public bool IsNullish { get; }

  public bool AsBool();
  public double AsDouble();
  public string AsString();
  public string CoerceToString();

  public JavaScriptObject AsObject();
  public JavaScriptArray AsArray();
  public JavaScriptFunction AsFunction();

  public JavaScriptValueRef Ref { get; }
  public JavaScriptValue Retain();
  public JavaScriptValue AsValue();
  public void Dispose();
}
```

Add scoped refs:

```csharp
public readonly ref struct JavaScriptValueRef
{
  public JavaScriptValueKind Kind { get; }
  public bool IsNullish { get; }

  public bool AsBool();
  public double AsDouble();
  public string AsString();
  public string CoerceToString();

  public JavaScriptObjectRef AsObject();
  public JavaScriptArrayRef AsArray();

  public JavaScriptValue Retain();
}

public readonly ref struct JavaScriptObjectRef
{
  public JavaScriptValueRef GetProperty(string name);
  public void SetProperty(string name, JavaScriptValue value);
  public JavaScriptObject Retain();
  public JavaScriptValue RetainAsValue();
}

public readonly ref struct JavaScriptArrayRef
{
  public uint Length { get; }
  public JavaScriptValueRef GetValue(uint index);
  public void SetValue(uint index, JavaScriptValue value);
  public JavaScriptArray Retain();
  public JavaScriptValue RetainAsValue();
}
```

Replace public `JavaScriptBorrowedValue` with `JavaScriptValueRef`:

```csharp
public readonly struct JavaScriptArguments
{
  public uint Count { get; }
  public JavaScriptValueRef GetValue(uint index);
}

public delegate JavaScriptValue JavaScriptHostFunction(
  JavaScriptRuntime runtime,
  JavaScriptValueRef thisValue,
  JavaScriptArguments arguments,
  object context);
```

`JavaScriptBorrowedValue` may temporarily remain as an obsolete forwarding type
during migration, but new code should use `JavaScriptValueRef`.

## Scoped Lifetime Model

Refs are valid only inside an active runtime access frame. The active frame is
created by:

- `JavaScriptRuntime.Execute(...)`;
- `JavaScriptRuntime.ExecuteAsync(...)` while running the scheduled body;
- `JavaScriptRuntime.ScheduleAsync(...)` while running the scheduled body;
- managed host-function callback invocation.

The frame owns native temporary values created by ref traversal. When the frame
exits, native releases all temporary values created through that frame.

Rules:

- Refs are `ref struct` values so C# prevents most accidental escapes.
- Refs do not implement `IDisposable`.
- Refs do not release native handles individually.
- `Retain()` is the only way to convert a ref to an owned escaping wrapper.
- Each ref carries the managed `JsiRefScope` object so default refs or manually
  invalidated scope refs fail loudly before touching native memory.
- Using a ref after its owned root was disposed must fail loudly.
- The ref lane is for synchronous inspection only.

## Native ABI Direction

Add a scoped temporary-value frame to the ABI. The frame is an opaque handle
owned by native runtime execution or host-function invocation.

Conceptual ABI additions:

```c
typedef struct expo_jsi_ref_scope_t *expo_jsi_ref_scope_handle;

typedef struct expo_jsi_value_ref {
  expo_jsi_ref_scope_handle scope;
  expo_jsi_value_handle value;
} expo_jsi_value_ref;
```

Function table additions should be general value-ref operations:

```c
value_ref_kind(runtime, value_ref, error*) -> value_kind
value_ref_is_promise(runtime, value_ref, error*) -> bool
value_ref_is_error(runtime, value_ref, error*) -> bool
value_ref_read_bool(runtime, value_ref, error*) -> bool
value_ref_read_double(runtime, value_ref, error*) -> double
value_ref_read_string(runtime, value_ref) -> string_result
value_ref_coerce_to_string(runtime, value_ref) -> string_result

value_ref_get_property(runtime, value_ref, name, name_len) -> value_ref_result
value_ref_get_element(runtime, value_ref, index) -> value_ref_result
value_ref_get_array_length(runtime, value_ref, error*) -> uint32

value_ref_retain(runtime, value_ref) -> value_result
value_ref_retain_object(runtime, value_ref) -> object_result
value_ref_retain_array(runtime, value_ref) -> array_result
```

Do not add separate borrowed object or array native handle families. Object and
array refs are C# typed views over `expo_jsi_value_ref`.

Native property and element reads for refs store returned `jsi::Value`s in the
active ref scope and return refs to those temporary slots. This avoids a
per-read disposable owned wrapper in C# while keeping C++ responsible for JSI
value storage and cleanup.

## Managed Runtime Scope

`JavaScriptRuntime` should create an internal managed scope object for each
runtime access callback. That scope carries:

- `JsiContext`;
- native `expo_jsi_ref_scope_handle`;
- a disposed/exited flag for debug checks.
- a thread-local previous-scope link so nested runtime execution restores the
  outer active scope.

`JavaScriptRuntime` instances passed to callback bodies should know their active
scope. Runtime instances outside an active scope may still create owned values,
but `.Ref` on owned values should require an active scope.

`JavaScriptValue.Ref` returns a scoped ref rooted in the active scope. If no
scope is active, throw an `InvalidOperationException` explaining that scoped
refs require runtime access through `Execute`, scheduled runtime work, or a host
function callback.

`JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArrayRef` should
store the managed `JsiRefScope` object, not only the native scope handle. Native
calls can materialize `expo_jsi_value_ref` from `scope.Handle` and the value
handle immediately before calling the ABI.

## Diagnostics

Remove owner parameters used only for exception cosmetics. Diagnostics must
follow ownership; they must not shape the API.

Bad:

```csharp
holder.AsValue(this)
```

Good:

```csharp
holder.AsValue()
holder.Ref
```

If nicer disposed exceptions are needed, store diagnostic names internally.

## Testing

Add low-level tests under `Expo.JSI.Tests/Runtime` and host-function tests:

- owned `JavaScriptValue.AsObject()` still returns an owned disposable object;
- owned `JavaScriptObject.GetProperty()` still returns an owned disposable
  value;
- `JavaScriptValue.Ref.AsObject().GetProperty(...)` reads without requiring
  disposable intermediates;
- `JavaScriptArguments.GetValue(index)` returns a scoped ref usable for
  primitive and object property reads;
- `thisValue` in host functions is a scoped ref;
- `JavaScriptValueRef.Retain()` returns an owned value that survives the scope;
- using `.Ref` outside active runtime access throws;
- default or invalid scoped refs fail loudly before touching native memory;
- C# compile-time restrictions prevent normal ref escapes from runtime access
  callbacks;
- `JavaScriptErrorObject` accessors use refs and do not clone value/object/
  property handles for simple reads;
- release counters prove ref traversal temporary values are released by the
  scope, not by C# `Dispose`.

Verification commands:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

If formatting check fails because files need formatting, run
`scripts/format.sh`, then repeat the checks.

## Open Decisions

- Whether obsolete `JavaScriptBorrowedValue` should be removed immediately or
  kept briefly as a compatibility shim in tests.
- Whether ref use-after-scope checks should be debug/test-only or always-on.
- Whether a future function-call slice should add `JavaScriptFunctionRef`.
