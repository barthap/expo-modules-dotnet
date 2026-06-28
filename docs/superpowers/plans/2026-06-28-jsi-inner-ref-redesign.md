# JSI Inner-Based Ref Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the rejected native scoped-ref implementation with a C#-only ref facade built on shared `Inner` structs and existing ABI calls.

**Architecture:** Remove the `value_ref_*` ABI and native ref scope. Move ABI calls into internal `JavaScriptValueInner`, `JavaScriptObjectInner`, and `JavaScriptArrayInner` structs. Keep public `ref struct` wrappers as thin ownership facades that either wrap existing root handles or register temporary owned handles in an internal C# `JavaScriptHandleScope`.

**Tech Stack:** C ABI opaque handles, C++20 JSI bridge, .NET 10 unsafe interop, C# `ref struct`, xUnit v3, Hermes-backed `scripts/test-jsi.sh`, `scripts/format.sh`.

---

## Success Criteria

- No `expo_jsi_value_ref`, `expo_jsi_ref_scope_handle`, native `RefScopeHandle`, or `value_ref_*` ABI remains.
- `ExpoJsiApi.ExpectedVersion` returns to the version required by the non-ref ABI.
- `JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArrayRef` remain public natural APIs.
- Ref wrappers contain only one-line or near-one-line calls into `Inner` methods plus wrapping/tracking policy.
- `JavaScriptBorrowedValue` remains removed.
- Ref traversal releases temporary handles through existing `release_value`, `release_object`, and `release_array` functions.
- `scripts/test-jsi.sh`, `scripts/format.sh --check --all`, and `git diff --check` pass.

## File Map

- Modify `native/include/expo_jsi.h`: remove rejected ref-scope handle, value-ref structs, function pointer typedefs, and API table entries.
- Modify `native/packages/jsi/src/ExpoJsiBridge.cpp`: remove `RefScopeHandle`, `valueRef*` functions, and API table entries; restore `kApiVersion`.
- Modify `native/testhost/include/expo_jsi_testhost.h`: remove `released_ref_scopes`.
- Modify `native/testhost/src/ExpoJsiTestHost.cpp`: remove `countedReleaseRefScope` and counted API wiring.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`: remove `ReleasedRefScopes`.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`: remove `ExpoJsiRefScopeHandle`.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`: remove `ExpoJsiValueRef` and `ExpoJsiValueRefResult`.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: remove rejected ref function pointers/wrappers and restore expected version.
- Delete `managed/packages/Expo.JSI/JsiRefScope.cs`.
- Create `managed/packages/Expo.JSI/JavaScriptHandleScope.cs`: C#-only temporary owned-handle release bag.
- Create `managed/packages/Expo.JSI/JavaScriptValueInner.cs`: shared value ABI operations.
- Create `managed/packages/Expo.JSI/JavaScriptObjectInner.cs`: shared object ABI operations.
- Create `managed/packages/Expo.JSI/JavaScriptArrayInner.cs`: shared array ABI operations.
- Modify `managed/packages/Expo.JSI/JavaScriptValue.cs`: wrap `JavaScriptValueInner` and expose owned/ref policies.
- Modify `managed/packages/Expo.JSI/JavaScriptObject.cs`: wrap `JavaScriptObjectInner`.
- Modify `managed/packages/Expo.JSI/JavaScriptArray.cs`: wrap `JavaScriptArrayInner`.
- Modify `managed/packages/Expo.JSI/JavaScriptValueRef.cs`: become thin facade over `JavaScriptValueInner`.
- Modify `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`: become thin facade over `JavaScriptObjectInner`.
- Modify `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`: become thin facade over `JavaScriptArrayInner`.
- Modify `managed/packages/Expo.JSI/JavaScriptArguments.cs`: keep `GetValue`, but return ref over borrowed argument handle without tracking it for release.
- Modify `managed/packages/Expo.JSI/JavaScriptRuntime.cs`: use `JavaScriptHandleScope` around runtime execution and host callbacks.
- Modify tests under `managed/packages/Expo.JSI.Tests/Runtime`, `HostFunctions`, and `Modules`: keep ref API tests, remove ref-scope counter assertions, add existing-counter release assertions.

## Task 1: Add Red Test For Existing-ABI Temporary Release

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`

- [ ] **Step 1: Replace the rejected ref-scope counter test**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`, replace `RuntimeAccessReleasesRefScope` with:

```csharp
[Fact]
public void RefTraversalReleasesTemporaryHandlesThroughExistingCounters()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.ResetCounters();

  fixture.Runtime.Execute(_ =>
  {
    using var value = fixture.Evaluate(
        "({ user: { name: 'expo' } })",
        "scoped-ref-release.js"
    );

    var name = value.Ref.AsObject()
        .GetProperty("user")
        .AsObject()
        .GetProperty("name");

    Assert.Equal("expo", name.AsString());
    return true;
  });

  Assert.True(fixture.Counters.ReleasedObjects >= 1);
  Assert.True(fixture.Counters.ReleasedValues >= 1);
}
```

- [ ] **Step 2: Run the focused test and verify it fails on current implementation**

Run:

```bash
scripts/test-jsi.sh --filter "RefTraversalReleasesTemporaryHandlesThroughExistingCounters"
```

Expected: FAIL because the rejected implementation releases a native ref scope instead of reporting temporary object/value releases through the existing counters.

- [ ] **Step 3: Keep the red test for the implementation pass**

Do not commit this test yet. It should be committed together with the
implementation once the focused suite is green.


## Task 2: Remove Rejected Native Ref ABI

**Files:**
- Modify: `native/include/expo_jsi.h`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `native/testhost/include/expo_jsi_testhost.h`
- Modify: `native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`

- [ ] **Step 1: Remove native declarations from `expo_jsi.h`**

Remove:

```c
class RefScopeHandle;
using expo_jsi_ref_scope_t = expo::jsi::RefScopeHandle;
typedef expo_jsi_ref_scope_t *expo_jsi_ref_scope_handle;
typedef struct expo_jsi_ref_scope_t *expo_jsi_ref_scope_handle;

typedef struct expo_jsi_value_ref {
  expo_jsi_ref_scope_handle scope;
  expo_jsi_value_handle value;
} expo_jsi_value_ref;

typedef struct expo_jsi_value_ref_result {
  int32_t ok;
  expo_jsi_value_ref value;
  expo_jsi_error error;
} expo_jsi_value_ref_result;
```

Remove all `expo_jsi_value_ref_*` typedefs and these fields from `expo_jsi_api`:

```c
expo_jsi_create_ref_scope_fn create_ref_scope;
expo_jsi_release_ref_scope_fn release_ref_scope;
expo_jsi_value_ref_get_kind_fn value_ref_get_kind;
expo_jsi_value_ref_get_bool_fn value_ref_get_bool;
expo_jsi_value_ref_get_double_fn value_ref_get_double;
expo_jsi_value_ref_get_string_fn value_ref_get_string;
expo_jsi_value_ref_coerce_to_string_fn value_ref_coerce_to_string;
expo_jsi_value_ref_get_property_fn value_ref_get_property;
expo_jsi_value_ref_get_value_at_index_fn value_ref_get_value_at_index;
expo_jsi_value_ref_get_array_length_fn value_ref_get_array_length;
expo_jsi_value_ref_retain_fn value_ref_retain;
expo_jsi_value_ref_retain_object_fn value_ref_retain_object;
expo_jsi_value_ref_retain_array_fn value_ref_retain_array;
```

- [ ] **Step 2: Remove native implementation from `ExpoJsiBridge.cpp`**

Remove `class RefScopeHandle final`, `makeValueRefResult`, `makeValueRefErrorResult`, `tryValueRef`, `createRefScope`, `releaseRefScope`, and all `valueRef*` functions.

Restore:

```cpp
constexpr uint32_t kApiVersion = 10;
```

Remove these entries from `kApi`:

```cpp
createRefScope,
releaseRefScope,
valueRefGetKind,
valueRefGetBool,
valueRefGetDouble,
valueRefGetString,
valueRefCoerceToString,
valueRefGetProperty,
valueRefGetValueAtIndex,
valueRefGetArrayLength,
valueRefRetain,
valueRefRetainObject,
valueRefRetainArray,
```

- [ ] **Step 3: Remove testhost ref-scope counter**

In `native/testhost/include/expo_jsi_testhost.h`, remove:

```c
uint32_t released_ref_scopes;
```

In `native/testhost/src/ExpoJsiTestHost.cpp`, remove:

```cpp
void countedReleaseRefScope(expo_jsi_runtime_handle runtime, expo_jsi_ref_scope_handle scope)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && scope != nullptr) {
    testhost->counters.released_ref_scopes++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  api->release_ref_scope(runtime, scope);
}
```

and remove:

```cpp
runtime.countedApi.release_ref_scope = countedReleaseRefScope;
```

- [ ] **Step 4: Remove managed testhost and interop ref ABI**

In `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`, remove:

```csharp
public readonly uint ReleasedRefScopes;
```

In `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`, remove:

```csharp
global using ExpoJsiRefScopeHandle = System.IntPtr;
```

In `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`, remove `ExpoJsiValueRef` and `ExpoJsiValueRefResult`.

In `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`, remove all `CreateRefScopeFunction`, `ReleaseRefScopeFunction`, `ValueRef*` function pointers, validation checks, and public wrapper methods. Restore:

```csharp
public const uint ExpectedVersion = 10;
```

- [ ] **Step 5: Run build and observe managed compile failures**

Run:

```bash
scripts/test-jsi.sh --filter "JavaScriptScopedRefTests"
```

Expected: FAIL at managed compile because `JsiRefScope` and ref wrappers still depend on removed interop.

- [ ] **Step 6: Keep ABI removal uncommitted until managed refs compile**

Do not commit this build-breaking intermediate state. Continue directly to the
managed `Inner` and ref-wrapper rewrite.

## Task 3: Add Shared Inner Types And C# Handle Scope

**Files:**
- Delete: `managed/packages/Expo.JSI/JsiRefScope.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptHandleScope.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptValueInner.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptObjectInner.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptArrayInner.cs`

- [ ] **Step 1: Delete rejected `JsiRefScope`**

Delete `managed/packages/Expo.JSI/JsiRefScope.cs`.

- [ ] **Step 2: Create `JavaScriptHandleScope`**

Create `managed/packages/Expo.JSI/JavaScriptHandleScope.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

internal sealed unsafe class JavaScriptHandleScope : IDisposable
{
  [ThreadStatic]
  private static JavaScriptHandleScope? current;

  private readonly JsiContext context;
  private readonly JavaScriptHandleScope? previous;
  private List<ExpoJsiValueHandle>? values;
  private List<ExpoJsiObjectHandle>? objects;
  private List<ExpoJsiArrayHandle>? arrays;
  private bool disposed;

  private JavaScriptHandleScope(JsiContext context, JavaScriptHandleScope? previous)
  {
    this.context = context;
    this.previous = previous;
  }

  public static JavaScriptHandleScope Enter(JsiContext context)
  {
    var scope = new JavaScriptHandleScope(context, current);
    current = scope;
    return scope;
  }

  public static JavaScriptHandleScope CurrentFor(JsiContext context)
  {
    var scope = current;
    if (scope is null || scope.disposed || scope.context.Api != context.Api
        || scope.context.RuntimeHandle != context.RuntimeHandle)
    {
      throw new InvalidOperationException(
          "Scoped JavaScript refs require active runtime access."
      );
    }
    return scope;
  }

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

  public ExpoJsiObjectHandle TrackObject(ExpoJsiObjectHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      objects ??= [];
      objects.Add(handle);
    }
    return handle;
  }

  public ExpoJsiArrayHandle TrackArray(ExpoJsiArrayHandle handle)
  {
    ThrowIfDisposed();
    if (handle != 0)
    {
      arrays ??= [];
      arrays.Add(handle);
    }
    return handle;
  }

  public void Dispose()
  {
    if (!ReferenceEquals(current, this))
    {
      throw new InvalidOperationException("JavaScript handle scopes must be disposed in stack order.");
    }

    current = previous;
    if (disposed)
    {
      return;
    }

    disposed = true;
    if (arrays is not null)
    {
      for (var index = arrays.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseArrayHandle(context.RuntimeHandle, arrays[index]);
      }
    }
    if (objects is not null)
    {
      for (var index = objects.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseObjectHandle(context.RuntimeHandle, objects[index]);
      }
    }
    if (values is not null)
    {
      for (var index = values.Count - 1; index >= 0; index--)
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, values[index]);
      }
    }
  }

  private void ThrowIfDisposed()
  {
    if (disposed)
    {
      throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
    }
  }
}
```

- [ ] **Step 3: Create `JavaScriptValueInner`**

Create `managed/packages/Expo.JSI/JavaScriptValueInner.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

internal readonly unsafe struct JavaScriptValueInner
{
  public JavaScriptValueInner(JsiContext context, ExpoJsiValueHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiValueHandle Handle { get; }

  public JavaScriptValueKind Kind
  {
    get
    {
      ExpoJsiError error;
      var kind = Context.Api->GetKind(Context.RuntimeHandle, Handle, &error);
      Context.ThrowIfError(error, "Failed to read JavaScript value kind.");
      return (JavaScriptValueKind)kind;
    }
  }

  public bool IsNullish => Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;

  public bool AsBool()
  {
    ExpoJsiError error;
    var value = Context.Api->ReadBool(Context.RuntimeHandle, Handle, &error);
    Context.ThrowIfError(error, "Failed to read JavaScript boolean.");
    return value;
  }

  public double AsDouble()
  {
    ExpoJsiError error;
    var value = Context.Api->ReadDouble(Context.RuntimeHandle, Handle, &error);
    Context.ThrowIfError(error, "Failed to read JavaScript number.");
    return value;
  }

  public string AsString() => Context.Api->ReadString(Context.RuntimeHandle, Handle);

  public string CoerceToString() =>
    Context.Api->CoerceJavaScriptValueToString(Context.RuntimeHandle, Handle);

  public bool IsPromise => Context.Api->IsPromiseValue(Context.RuntimeHandle, Handle);

  public bool IsError => Context.Api->IsErrorValue(Context.RuntimeHandle, Handle);

  public ExpoJsiObjectHandle AsObject()
  {
    var result = Context.Api->ConvertValueToObject(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to object.");
    }
    return result.Object;
  }

  public ExpoJsiArrayHandle AsArray()
  {
    var result = Context.Api->ConvertValueToArray(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Array == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to array.");
    }
    return result.Array;
  }

  public ExpoJsiValueHandle Retain()
  {
    var result = Context.Api->CloneJavaScriptValue(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to clone JavaScript value.");
    }
    return result.Value;
  }
}
```

- [ ] **Step 4: Create `JavaScriptObjectInner`**

Create `managed/packages/Expo.JSI/JavaScriptObjectInner.cs`:

```csharp
using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI;

internal readonly unsafe struct JavaScriptObjectInner
{
  public JavaScriptObjectInner(JsiContext context, ExpoJsiObjectHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiObjectHandle Handle { get; }

  public void SetProperty(string name, ExpoJsiValueHandle value)
  {
    ArgumentNullException.ThrowIfNull(name);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var error = Context.Api->SetObjectProperty(
        Context.RuntimeHandle,
        Handle,
        nameBytes,
        value
    );
    Context.ThrowIfError(error, "Failed to set JavaScript object property.");
  }

  public ExpoJsiValueHandle GetProperty(string name)
  {
    ArgumentNullException.ThrowIfNull(name);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var result = Context.Api->GetObjectProperty(
        Context.RuntimeHandle,
        Handle,
        nameBytes
    );
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript object property.");
    }
    return result.Value;
  }

  public ExpoJsiValueHandle AsValue()
  {
    var result = Context.Api->ConvertObjectToValue(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript object to value.");
    }
    return result.Value;
  }
}
```

- [ ] **Step 5: Create `JavaScriptArrayInner`**

Create `managed/packages/Expo.JSI/JavaScriptArrayInner.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

internal readonly unsafe struct JavaScriptArrayInner
{
  public JavaScriptArrayInner(JsiContext context, ExpoJsiArrayHandle handle)
  {
    Context = context;
    Handle = handle;
  }

  public JsiContext Context { get; }
  public ExpoJsiArrayHandle Handle { get; }

  public uint Length
  {
    get
    {
      ExpoJsiError error;
      var length = Context.Api->GetArrayLength(Context.RuntimeHandle, Handle, &error);
      Context.ThrowIfError(error, "Failed to read JavaScript array length.");
      return length;
    }
  }

  public ExpoJsiValueHandle GetValue(uint index)
  {
    var result = Context.Api->GetArrayValueAtIndex(Context.RuntimeHandle, Handle, index);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array value.");
    }
    return result.Value;
  }

  public void SetValue(uint index, ExpoJsiValueHandle value)
  {
    var error = Context.Api->SetArrayValueAtIndex(
        Context.RuntimeHandle,
        Handle,
        index,
        value
    );
    Context.ThrowIfError(error, "Failed to set JavaScript array value.");
  }

  public ExpoJsiObjectHandle AsObject()
  {
    var result = Context.Api->ConvertArrayToObject(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to object.");
    }
    return result.Object;
  }

  public ExpoJsiValueHandle AsValue()
  {
    var result = Context.Api->ConvertArrayToValue(Context.RuntimeHandle, Handle);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to value.");
    }
    return result.Value;
  }
}
```

- [ ] **Step 6: Run compile and observe wrapper failures**

Run:

```bash
scripts/test-jsi.sh --filter "JavaScriptScopedRefTests"
```

Expected: FAIL because public wrappers still use the removed `JsiRefScope` shape.

## Task 4: Rewire Public Owned Wrappers To Inner Types

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptValue.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptObject.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArray.cs`

- [ ] **Step 1: Rewire `JavaScriptValue`**

In `JavaScriptValue.cs`, add:

```csharp
private JavaScriptValueInner Inner
{
  get
  {
    ThrowIfDisposed();
    return new JavaScriptValueInner(context, handle);
  }
}
```

Replace existing property/method bodies so they delegate to `Inner`:

```csharp
public JavaScriptValueKind Kind => Inner.Kind;
public bool IsNullish => Inner.IsNullish;
public bool IsPromise => Inner.IsPromise;
public bool IsError => Inner.IsError;
public bool IsBool => Kind == JavaScriptValueKind.Bool;
public bool IsDouble => Kind == JavaScriptValueKind.Number;
public bool IsString => Kind == JavaScriptValueKind.String;
public bool IsObject => Kind == JavaScriptValueKind.Object;
public bool AsBool() => Inner.AsBool();
public double AsDouble() => Inner.AsDouble();
public string AsString() => Inner.AsString();
internal string CoerceToString() => Inner.CoerceToString();

public JavaScriptObject AsObject() =>
  new(context, Inner.AsObject());

public JavaScriptArray AsArray() =>
  new(context, Inner.AsArray());

public JavaScriptValue AsValue() =>
  FromOwnedHandle(context, Inner.Retain());

public JavaScriptValue Retain() => AsValue();

public JavaScriptValueRef Ref =>
  JavaScriptValueRef.FromBorrowedRoot(JavaScriptHandleScope.CurrentFor(context), Inner);
```

- [ ] **Step 2: Rewire `JavaScriptObject`**

In `JavaScriptObject.cs`, add:

```csharp
private JavaScriptObjectInner Inner
{
  get
  {
    ThrowIfDisposed();
    return new JavaScriptObjectInner(context, handle);
  }
}
```

Replace method bodies:

```csharp
public void SetProperty(string name, JavaScriptValue value)
{
  ArgumentNullException.ThrowIfNull(value);
  Inner.SetProperty(name, value.Handle);
}

public JavaScriptValue GetProperty(string name) =>
  JavaScriptValue.FromOwnedHandle(context, Inner.GetProperty(name));

public JavaScriptValue AsValue() =>
  JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());
```

- [ ] **Step 3: Rewire `JavaScriptArray`**

In `JavaScriptArray.cs`, add:

```csharp
private JavaScriptArrayInner Inner
{
  get
  {
    ThrowIfDisposed();
    return new JavaScriptArrayInner(context, handle);
  }
}
```

Replace method bodies:

```csharp
public uint Length => Inner.Length;

public JavaScriptValue GetValue(uint index) =>
  JavaScriptValue.FromOwnedHandle(context, Inner.GetValue(index));

public void SetValue(uint index, JavaScriptValue value)
{
  ArgumentNullException.ThrowIfNull(value);
  Inner.SetValue(index, value.Handle);
}

public JavaScriptObject AsObject() =>
  new(context, Inner.AsObject());

public JavaScriptValue AsValue() =>
  JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());
```

- [ ] **Step 4: Check owned wrapper compile state**

Run:

```bash
scripts/test-jsi.sh --filter "JavaScriptPrimitiveTests|JavaScriptObjectTests|JavaScriptArrayTests"
```

Expected test result: compile may still fail if ref wrappers are not yet
rewired. If it fails only for ref wrappers, continue to Task 5. If it fails in
owned wrappers or `Inner` types, fix those errors before continuing.

## Task 5: Rewire Ref Wrappers As Thin Facades

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptValueRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArguments.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`

- [ ] **Step 1: Replace `JavaScriptValueRef` implementation**

`managed/packages/Expo.JSI/JavaScriptValueRef.cs` should become:

```csharp
namespace Expo.JSI;

public readonly ref struct JavaScriptValueRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptValueInner inner;

  private JavaScriptValueRef(JavaScriptHandleScope scope, JavaScriptValueInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptValueRef FromBorrowedRoot(
      JavaScriptHandleScope scope,
      JavaScriptValueInner inner
  ) => new(scope, inner);

  internal static JavaScriptValueRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiValueHandle handle
  ) => new(scope, new JavaScriptValueInner(context, scope.TrackValue(handle)));

  public JavaScriptValueKind Kind => Inner.Kind;
  public bool IsNullish => Inner.IsNullish;
  public bool IsBool => Kind == JavaScriptValueKind.Bool;
  public bool IsDouble => Kind == JavaScriptValueKind.Number;
  public bool IsString => Kind == JavaScriptValueKind.String;
  public bool IsObject => Kind == JavaScriptValueKind.Object;
  public bool AsBool() => Inner.AsBool();
  public double AsDouble() => Inner.AsDouble();
  public string AsString() => Inner.AsString();
  public string CoerceToString() => Inner.CoerceToString();

  public JavaScriptObjectRef AsObject()
  {
    var objectHandle = Inner.AsObject();
    return JavaScriptObjectRef.FromScopedHandle(Scope, Inner.Context, objectHandle);
  }

  public JavaScriptArrayRef AsArray()
  {
    var arrayHandle = Inner.AsArray();
    return JavaScriptArrayRef.FromScopedHandle(Scope, Inner.Context, arrayHandle);
  }

  public JavaScriptValue Retain() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.Retain());

  private JavaScriptValueInner Inner
  {
    get
    {
      _ = Scope;
      if (inner.Handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
      }
      return inner;
    }
  }

  private JavaScriptHandleScope Scope =>
    scope ?? throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
}
```

- [ ] **Step 2: Replace `JavaScriptObjectRef` implementation**

`managed/packages/Expo.JSI/JavaScriptObjectRef.cs` should become:

```csharp
namespace Expo.JSI;

public readonly ref struct JavaScriptObjectRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptObjectInner inner;

  private JavaScriptObjectRef(JavaScriptHandleScope scope, JavaScriptObjectInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptObjectRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiObjectHandle handle
  ) => new(scope, new JavaScriptObjectInner(context, scope.TrackObject(handle)));

  public JavaScriptValueRef GetProperty(string name)
  {
    var handle = Inner.GetProperty(name);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  public JavaScriptObject Retain()
  {
    using var value = RetainAsValue();
    return value.AsObject();
  }

  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptObjectInner Inner
  {
    get
    {
      _ = Scope;
      if (inner.Handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
      }
      return inner;
    }
  }

  private JavaScriptHandleScope Scope =>
    scope ?? throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
}
```

- [ ] **Step 3: Replace `JavaScriptArrayRef` implementation**

`managed/packages/Expo.JSI/JavaScriptArrayRef.cs` should become:

```csharp
namespace Expo.JSI;

public readonly ref struct JavaScriptArrayRef
{
  private readonly JavaScriptHandleScope? scope;
  private readonly JavaScriptArrayInner inner;

  private JavaScriptArrayRef(JavaScriptHandleScope scope, JavaScriptArrayInner inner)
  {
    this.scope = scope;
    this.inner = inner;
  }

  internal static JavaScriptArrayRef FromScopedHandle(
      JavaScriptHandleScope scope,
      JsiContext context,
      ExpoJsiArrayHandle handle
  ) => new(scope, new JavaScriptArrayInner(context, scope.TrackArray(handle)));

  public uint Length => Inner.Length;

  public JavaScriptValueRef GetValue(uint index)
  {
    var handle = Inner.GetValue(index);
    return JavaScriptValueRef.FromScopedHandle(Scope, Inner.Context, handle);
  }

  public JavaScriptArray Retain()
  {
    using var value = RetainAsValue();
    return value.AsArray();
  }

  public JavaScriptValue RetainAsValue() =>
    JavaScriptValue.FromOwnedHandle(Inner.Context, Inner.AsValue());

  private JavaScriptArrayInner Inner
  {
    get
    {
      _ = Scope;
      if (inner.Handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
      }
      return inner;
    }
  }

  private JavaScriptHandleScope Scope =>
    scope ?? throw new ObjectDisposedException(nameof(JavaScriptHandleScope));
}
```

- [ ] **Step 4: Rewire `JavaScriptArguments.GetValue`**

In `JavaScriptArguments.cs`, return a borrowed root ref without tracking:

```csharp
public JavaScriptValueRef GetValue(uint index)
{
  ThrowIfNull();
  unsafe
  {
    var result = context.Api->GetArgument(context.RuntimeHandle, handle, index);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to read JavaScript argument.");
    }

    var scope = JavaScriptHandleScope.CurrentFor(context);
    return JavaScriptValueRef.FromBorrowedRoot(
        scope,
        new JavaScriptValueInner(context, result.Value)
    );
  }
}
```

- [ ] **Step 5: Rewire runtime scopes**

In `JavaScriptRuntime.cs`, replace `JsiRefScope.Enter` with `JavaScriptHandleScope.Enter` in both host-function invocation and runtime task invocation:

```csharp
using var scope = JavaScriptHandleScope.Enter(jsiContext);
var thisValue = JavaScriptValueRef.FromBorrowedRoot(
    scope,
    new JavaScriptValueInner(jsiContext, thisValueHandle)
);
```

and:

```csharp
using var scope = JavaScriptHandleScope.Enter(context);
completion.TrySetResult(body(new JavaScriptRuntime(context)));
```

- [ ] **Step 6: Run focused ref tests**

Run:

```bash
scripts/test-jsi.sh --filter "JavaScriptScopedRefTests|HostFunctionReceivesScopedThisAndArgumentRefs|JavaScriptValueRefAsArrayWorksInsideHostFunction|GeneratedLookingCodeDecodesJavaScriptArrayIntoReadOnlyListParameter"
```

Expected: PASS.

- [ ] **Step 7: Commit green inner/ref rewrite**

Run:

```bash
git add managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs managed/packages/Expo.JSI/JavaScriptHandleScope.cs managed/packages/Expo.JSI/JavaScriptValueInner.cs managed/packages/Expo.JSI/JavaScriptObjectInner.cs managed/packages/Expo.JSI/JavaScriptArrayInner.cs managed/packages/Expo.JSI/JavaScriptValue.cs managed/packages/Expo.JSI/JavaScriptObject.cs managed/packages/Expo.JSI/JavaScriptArray.cs managed/packages/Expo.JSI/JavaScriptValueRef.cs managed/packages/Expo.JSI/JavaScriptObjectRef.cs managed/packages/Expo.JSI/JavaScriptArrayRef.cs managed/packages/Expo.JSI/JavaScriptArguments.cs managed/packages/Expo.JSI/JavaScriptRuntime.cs managed/packages/Expo.JSI/JsiRefScope.cs native/include/expo_jsi.h native/packages/jsi/src/ExpoJsiBridge.cpp native/testhost/include/expo_jsi_testhost.h native/testhost/src/ExpoJsiTestHost.cpp
git commit -m "refactor: use inner-based scoped refs"
```

## Task 6: Cleanup, Source Guards, And Full Verification

**Files:**
- Modify: tests if compile errors remain after removing rejected ref-scope names.

- [ ] **Step 1: Verify rejected names are gone**

Run:

```bash
rg -n "JsiRefScope|ExpoJsiRefScopeHandle|ExpoJsiValueRef|expo_jsi_value_ref|expo_jsi_ref_scope|value_ref_|RefScopeHandle|ReleasedRefScopes|released_ref_scopes" managed native
```

Expected: no output.

- [ ] **Step 2: Verify full suite**

Run:

```bash
scripts/test-jsi.sh
```

Expected: `Passed!` with all tests passing.

- [ ] **Step 3: Verify formatting**

Run:

```bash
scripts/format.sh --check --all
```

Expected: `Formatting check passed.`

If it fails, run:

```bash
scripts/format.sh
scripts/format.sh --check --all
```

- [ ] **Step 4: Verify whitespace and local path safety**

Run:

```bash
git diff --check
git diff --cached --check
git diff | rg -n "^\\+.*(/Users/|~/|BKLOCEK|19046C77)" -S
```

Expected: no output from all commands except `git diff --cached --check` may be empty if nothing is staged.

- [ ] **Step 5: Commit final cleanup**

Run:

```bash
git add managed native
git commit -m "test: verify inner-based ref cleanup"
```

Only create this commit if Task 6 changed files after Task 5. If there are no file changes, skip this commit and record that no cleanup commit was needed.
