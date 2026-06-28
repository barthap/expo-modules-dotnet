# JSI Inner-Based Ref Redesign

Date: 2026-06-28
Repo: `<repo>`

## Status

This spec supersedes `2026-06-28-jsi-scoped-ref-ownership-design.md`.

The previous design used a native ref scope, a duplicated `value_ref_*` ABI
surface, and duplicated C# ref-wrapper logic. That shape is rejected. It bloats
both ABI and public implementation structure, and it recreates the duplication
the ref design was meant to avoid.

## Context

The bridge has two different ergonomic needs:

- owned wrappers must remain explicit, disposable, and safe for escaping values;
- temporary inspection should not require callers to spell every disposable
  intermediate.

Existing ABI already has general operations for owned opaque handles:

```text
value_as_object
value_as_array
object_get_property
array_get_value_at_index
release_value
release_object
release_array
```

Those operations are enough for the ref API. The fix should reorganize managed
wrapper internals around shared implementation, not add another native handle
family or another native operation family.

## Goal

Expose natural public ref APIs:

```csharp
var name = value.Ref
    .AsObject()
    .GetProperty("user")
    .AsObject()
    .GetProperty("name")
    .AsString();
```

without duplicating C# object/value/array logic and without extending the C ABI.

Owned APIs stay direct and owned:

```csharp
using var obj = value.AsObject();
using var name = obj.GetProperty("name");
```

Host-function arguments should use the same ref vocabulary:

```csharp
public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptValueRef thisValue,
    JavaScriptArguments arguments,
    object context);

var label = arguments.GetValue(0).AsObject().GetProperty("label").AsString();
```

## Non-Goals

Do not add:

- native ref scopes;
- `expo_jsi_value_ref`;
- `value_ref_*` ABI functions;
- public `JsiRefScope` or public lifetime marker types;
- finalizers as the primary release mechanism;
- closure-style public helper APIs;
- a second implementation of object/array/value logic in ref wrappers.

## Design Summary

Introduce internal `Inner` structs that own ABI calls and validation:

```text
JavaScriptValueInner
JavaScriptObjectInner
JavaScriptArrayInner
```

Public wrappers become ownership policy shells:

```text
JavaScriptValue      -> owned wrapper over JavaScriptValueInner
JavaScriptValueRef   -> ref struct facade over JavaScriptValueInner

JavaScriptObject     -> owned wrapper over JavaScriptObjectInner
JavaScriptObjectRef  -> ref struct facade over JavaScriptObjectInner

JavaScriptArray      -> owned wrapper over JavaScriptArrayInner
JavaScriptArrayRef   -> ref struct facade over JavaScriptArrayInner
```

The `Inner` layer is the only place that calls ABI for the corresponding
operation. Owned and ref public wrappers differ only in how they wrap returned
handles.

## Inner Layer

An `Inner` struct stores the `JsiContext` plus one native handle:

```csharp
internal readonly struct JavaScriptObjectInner
{
  public JsiContext Context { get; }
  public ExpoJsiObjectHandle Handle { get; }

  public ExpoJsiValueHandle GetProperty(string name);
  public void SetProperty(string name, ExpoJsiValueHandle value);
  public ExpoJsiValueHandle AsValue();
}
```

`GetProperty` contains the single implementation for:

- null argument checks;
- UTF-8 encoding;
- ABI call;
- native error conversion;
- raw handle return.

The owned wrapper uses it like this:

```csharp
public JavaScriptValue GetProperty(string name) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetProperty(name));
```

The ref wrapper uses the same method:

```csharp
public JavaScriptValueRef GetProperty(string name)
{
  var handle = inner.GetProperty(name);
  return JavaScriptValueRef.FromScopedHandle(scope, scope.TrackValue(handle));
}
```

If a ref wrapper method contains UTF-8 encoding, ABI calls, or native error
translation itself, the design has regressed.

## Ref Wrappers

`JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArrayRef` remain
public because they make the API natural and typed:

```csharp
value.Ref.AsObject().GetProperty("x")
arrayRef.GetValue(0)
```

They must stay thin. Their responsibilities are:

- hold an `Inner`;
- hold an internal C# handle scope when temporary handles need cleanup;
- call the matching `Inner` method;
- choose ref vs owned wrapping for returned handles.

They must not:

- own ABI logic;
- duplicate validation or encoding logic;
- expose lifetime-marker details;
- implement `IDisposable`.

## C# Handle Scope

Existing ABI traversal returns owned handles. For example:

```text
value_as_object      -> object handle that must be released
object_get_property  -> value handle that must be released
```

`JavaScriptValue.Ref` itself should not allocate or track anything when it can
reuse an existing owned root handle. Tracking is needed only when a ref
operation calls existing ABI and receives a new owned handle that the caller
will not dispose manually.

Use an internal C#-only scope for those temporary handles:

```csharp
internal sealed class JavaScriptHandleScope : IDisposable
{
  public ExpoJsiValueHandle TrackValue(ExpoJsiValueHandle handle);
  public ExpoJsiObjectHandle TrackObject(ExpoJsiObjectHandle handle);
  public ExpoJsiArrayHandle TrackArray(ExpoJsiArrayHandle handle);
}
```

The scope releases tracked handles through existing ABI release functions on
dispose. It is not public API and it is not represented in native code.

`JavaScriptHandleScope` should have minimal reach:

- it is opened around synchronous runtime execution frames and host-function
  callback invocation;
- it is accessed only by ref wrapper factories and runtime/argument plumbing;
- it does not change owned wrapper behavior;
- it does not require an ABI version bump.

## Owned Roots

`JavaScriptValue.Ref` creates a ref facade over the existing owned value handle:

```csharp
public JavaScriptValueRef Ref
{
  get
  {
    ThrowIfDisposed();
    var scope = JavaScriptHandleScope.CurrentFor(context);
    return JavaScriptValueRef.FromBorrowedRoot(scope, Inner);
  }
}
```

The owned root still controls the original handle. The scope only tracks new
temporary handles produced after traversal.

Using a ref after disposing its owned root should fail before touching native
where practical. The root-backed ref can carry a small C# lifetime marker tied
to the owner, but this marker remains internal and managed-only.

## Host Function Arguments

Host-function argument handles are already call-scoped by native
`ArgumentsHandle`. `JavaScriptArguments.GetValue(index)` should return
`JavaScriptValueRef` using the existing `get_argument_value` ABI. No native ref
scope is needed.

If `get_argument_value` returns a borrowed handle owned by `ArgumentsHandle`, it
should not be registered for release by `JavaScriptHandleScope`. Only handles
returned by existing owned-producing traversal calls should be tracked.

## Error Object Accessors

`JavaScriptErrorObject` should use the ref API to avoid disposable intermediate
ceremony:

```csharp
private string? GetNullableStringProperty(string name)
{
  var property = holder.Ref.AsObject().GetProperty(name);
  return property.IsNullish ? null : property.CoerceToString();
}
```

This must use the shared `Inner` implementation and C# temporary-handle scope,
not native special-case error helpers.

## Migration From Rejected Implementation

Revert or remove:

- native `RefScopeHandle`;
- `expo_jsi_ref_scope_handle`;
- `expo_jsi_value_ref`;
- all `value_ref_*` function pointer typedefs and API table entries;
- managed `ExpoJsiValueRef` interop structs;
- managed `ExpoJsiRefScopeHandle`;
- public/internal `JsiRefScope`;
- testhost ref-scope release counters.

Keep conceptually, but reimplement:

- public `JavaScriptValueRef`;
- public `JavaScriptObjectRef`;
- public `JavaScriptArrayRef`;
- `JavaScriptArguments.GetValue(index)`;
- host-function `thisValue` as `JavaScriptValueRef`;
- tests that assert public ref ergonomics.

## Success Criteria

- No new native ABI entries are required for refs.
- After removing the rejected ref ABI, this redesign does not require a new
  `ExpoJsiApi.ExpectedVersion` bump.
- `JavaScriptObjectRef` and `JavaScriptArrayRef` public methods are thin
  wrappers around `Inner` methods.
- Source has no public `JsiRefScope` and no native `value_ref_*` surface.
- `JavaScriptBorrowedValue` is replaced by `JavaScriptValueRef`.
- Existing owned APIs keep current behavior and disposal requirements.
- Ref traversal releases temporary handles through existing release functions.
- Hermes-backed tests cover nested ref traversal, host-function refs, retained
  values escaping the scope, default/ref misuse, and release counters.
- `scripts/test-jsi.sh`, `scripts/format.sh --check --all`, and
  `git diff --check` pass.
