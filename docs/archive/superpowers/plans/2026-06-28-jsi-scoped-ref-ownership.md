# JSI Scoped Ref Ownership Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add value-handle-centric scoped refs so temporary JavaScript inspection does not require disposable intermediate wrappers, while owned wrappers keep their current escaping behavior.

**Architecture:** Native owns a scoped temporary-value frame that stores values produced during ref traversal and releases them when runtime access exits. Managed code exposes `JavaScriptValueRef`, `JavaScriptObjectRef`, and `JavaScriptArrayRef` as `ref struct` views over value handles; owned wrappers still return owned wrappers from direct `As*()` and `Get*()` calls.

**Tech Stack:** C ABI opaque handles, C++20 JSI bridge, .NET 10 unsafe interop, C# `ref struct`, xUnit v3, Hermes-backed `scripts/test-jsi.sh`, `scripts/format.sh`.

---

## Success Criteria

- Existing owned APIs keep current behavior: owned receivers return owned disposable wrappers.
- `JavaScriptValue.Ref.AsObject().GetProperty(name)` reads properties without caller-owned disposable intermediates.
- `JavaScriptArguments.GetValue(index)` returns `JavaScriptValueRef`.
- Host-function `thisValue` uses `JavaScriptValueRef`.
- `JavaScriptValueRef.Retain()` returns an owned `JavaScriptValue` that survives after the scope exits.
- `.Ref` outside runtime access throws a clear `InvalidOperationException`.
- Ref traversal temporary handles are released when the runtime access scope exits.
- `JavaScriptErrorObject` accessors use the ref lane and no longer call `holder.AsValue(this)`.
- `scripts/test-jsi.sh`, `scripts/format.sh --check --all`, and `git diff --check` pass.

## File Map

- Modify `native/include/expo_jsi.h`: add `expo_jsi_ref_scope_handle`, `expo_jsi_value_ref`, result structs, function pointer typedefs, and API table entries.
- Modify `native/packages/jsi/src/ExpoJsiBridge.cpp`: add `RefScopeHandle`, ref-scope create/release, value-ref read/traversal/retain functions, and API version bump.
- Modify `native/testhost/src/ExpoJsiTestHost.cpp`: count ref-scope releases for tests.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`: expose the ref-scope release counter.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`: add managed aliases for ref-scope handles.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`: add managed value-ref and value-ref-result structs.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: bind and wrap new ref-scope/value-ref ABI entries; bump `ExpectedVersion`.
- Create `managed/packages/Expo.JSI/JsiRefScope.cs`: internal thread-local active-scope stack and scope disposal.
- Create `managed/packages/Expo.JSI/JavaScriptValueRef.cs`: scoped value-ref primitive reads, typed views, and retain.
- Create `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`: object-shaped scoped view over `JavaScriptValueRef`.
- Create `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`: array-shaped scoped view over `JavaScriptValueRef`.
- Modify `managed/packages/Expo.JSI/JavaScriptRuntime.cs`: create active ref scopes around runtime task and host-function callback execution; pass `JavaScriptValueRef` to host callbacks.
- Modify `managed/packages/Expo.JSI/JavaScriptValue.cs`: add `IsNullish`, `Ref`, `Retain`, and keep owned conversions.
- Modify `managed/packages/Expo.JSI/JavaScriptObject.cs`: keep owned `GetProperty`; do not add object-level `Ref` in this slice.
- Modify `managed/packages/Expo.JSI/JavaScriptArray.cs`: keep owned `GetValue`; do not add array-level `Ref` in this slice.
- Modify `managed/packages/Expo.JSI/JavaScriptArguments.cs`: replace `GetBorrowedValue` with `GetValue` returning `JavaScriptValueRef`.
- Modify `managed/packages/Expo.JSI/JavaScriptHostFunction.cs`: change `thisValue` parameter to `JavaScriptValueRef`.
- Modify `managed/packages/Expo.JSI/JavaScriptErrorObject.cs`: use `holder.Ref` and remove owner-only diagnostic flow.
- Modify `managed/packages/Expo.JSI/JavaScriptValueHolder.cs`: add `Ref`, remove `AsValue(object owner)`.
- Modify tests under `managed/packages/Expo.JSI.Tests/Runtime`, `HostFunctions`, and temporary `Modules` tests to use `JavaScriptValueRef`.

## Task 1: Add Red Tests For Scoped Ref Behavior

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueRepresentableTests.cs`

- [ ] **Step 1: Add scoped-ref runtime tests**

Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptScopedRefTests
{
  [Fact]
  public void OwnedValueRefReadsNestedPropertyWithoutDisposableIntermediates()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ user: { name: 'expo' } })",
          "scoped-ref-nested-property.js"
      );

      var property = value.Ref.AsObject()
          .GetProperty("user")
          .AsObject()
          .GetProperty("name");

      Assert.Equal(JavaScriptValueKind.String, property.Kind);
      Assert.Equal("expo", property.AsString());
      return true;
    });
  }

  [Fact]
  public void RefRetainReturnsOwnedValueThatSurvivesScope()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using var retained = fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ message: 'retained' })",
          "scoped-ref-retain.js"
      );

      return value.Ref.AsObject().GetProperty("message").Retain();
    });

    Assert.Equal("retained", retained.AsString());
  }

  [Fact]
  public void RefOutsideRuntimeAccessThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.Execute(_ =>
      fixture.Evaluate("'outside'", "scoped-ref-outside.js"));

    var error = Assert.Throws<InvalidOperationException>(() => _ = value.Ref);
    Assert.Contains("Scoped JavaScript refs require active runtime access", error.Message);
  }

  [Fact]
  public void DefaultRefFailsBeforeTouchingNative()
  {
    JavaScriptValueRef value = default;

    var error = Assert.Throws<ObjectDisposedException>(() => value.AsString());
    Assert.Equal("JsiRefScope", error.ObjectName);
  }

  [Fact]
  public void RefFromDisposedOwnedValueThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var value = runtime.CreateString("disposed");
      value.Dispose();

      Assert.Throws<ObjectDisposedException>(() => _ = value.Ref);
      return true;
    });
  }
}
```

- [ ] **Step 2: Add host-function ref tests**

In `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`, update the host-function callback examples to use `arguments.GetValue(index)` and add this test:

```csharp
  [Fact]
  public void HostFunctionReceivesScopedThisAndArgumentRefs()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      using var global = runtime.Global();
      using var target = runtime.CreateObject();
      using var offset = runtime.CreateNumber(1.5);
      target.SetProperty("offset", offset);

      using var function = runtime.CreateHostFunction(
          "describe",
          1,
          (callbackRuntime, thisValue, arguments, context) =>
          {
            var offsetValue = thisValue.AsObject().GetProperty("offset");
            var name = arguments.GetValue(0).AsObject().GetProperty("name");
            return callbackRuntime.CreateString($"{name.AsString()}:{offsetValue.AsDouble()}");
          },
          new object()
      );

      using var functionValue = function.AsValue();
      target.SetProperty("describe", functionValue);
      using var targetValue = target.AsValue();
      global.SetProperty("target", targetValue);

      using var result = fixture.Evaluate(
          "globalThis.target.describe({ name: 'expo' })",
          "host-function-scoped-refs.js"
      );

      Assert.Equal("expo:1.5", result.AsString());
      return true;
    });
  }
```

- [ ] **Step 3: Update error-object expectation**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueRepresentableTests.cs`, keep the existing `JavaScriptErrorObjectAccessorsTolerateMutatedFields` assertions. This test becomes the regression proving error accessors move to the ref lane without changing behavior.

- [ ] **Step 4: Run red tests**

Run:

```sh
scripts/test-jsi.sh --filter "JavaScriptScopedRefTests|HostFunctionReceivesScopedThisAndArgumentRefs|JavaScriptErrorObjectAccessorsTolerateMutatedFields"
```

Expected: build fails because `JavaScriptValueRef`, `.Ref`, and `JavaScriptArguments.GetValue` do not exist yet.

## Task 2: Add Native ABI And Managed Interop Shape

**Files:**
- Modify: `native/include/expo_jsi.h`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`

- [ ] **Step 1: Add C ABI types**

In `native/include/expo_jsi.h`, add the forward declaration and handle typedef near the existing handle declarations:

```c
class RefScopeHandle;
using expo_jsi_ref_scope_t = expo::jsi::RefScopeHandle;
typedef expo_jsi_ref_scope_t *expo_jsi_ref_scope_handle;
```

In the non-C++ branch, add:

```c
typedef struct expo_jsi_ref_scope_t *expo_jsi_ref_scope_handle;
```

Add value-ref structs after `expo_jsi_value_result`:

```c
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

- [ ] **Step 2: Add C ABI function typedefs and table entries**

Add typedefs:

```c
typedef expo_jsi_ref_scope_handle (*expo_jsi_create_ref_scope_fn)(
  expo_jsi_runtime_handle runtime);

typedef void (*expo_jsi_release_ref_scope_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_ref_scope_handle scope);

typedef expo_jsi_value_kind (*expo_jsi_value_ref_get_kind_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  expo_jsi_error *error);

typedef uint8_t (*expo_jsi_value_ref_get_bool_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  expo_jsi_error *error);

typedef double (*expo_jsi_value_ref_get_double_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  expo_jsi_error *error);

typedef expo_jsi_string_result (*expo_jsi_value_ref_get_string_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value);

typedef expo_jsi_string_result (*expo_jsi_value_ref_coerce_to_string_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value);

typedef expo_jsi_value_ref_result (*expo_jsi_value_ref_get_property_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  const char *name,
  int32_t name_len);

typedef expo_jsi_value_ref_result (*expo_jsi_value_ref_get_value_at_index_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  uint32_t index);

typedef uint32_t (*expo_jsi_value_ref_get_array_length_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value,
  expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_value_ref_retain_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value);

typedef expo_jsi_object_result (*expo_jsi_value_ref_retain_object_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value);

typedef expo_jsi_array_result (*expo_jsi_value_ref_retain_array_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_ref value);
```

Append matching fields to `expo_jsi_api`.

- [ ] **Step 3: Add managed interop aliases and structs**

In `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`, add:

```csharp
global using ExpoJsiRefScopeHandle = System.IntPtr;
```

In `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`, add:

```csharp
namespace Expo.JSI.Interop;

public readonly struct ExpoJsiValueRef
{
  public readonly ExpoJsiRefScopeHandle Scope;
  public readonly ExpoJsiValueHandle Value;

  public ExpoJsiValueRef(ExpoJsiRefScopeHandle scope, ExpoJsiValueHandle value)
  {
    Scope = scope;
    Value = value;
  }
}

public readonly struct ExpoJsiValueRefResult
{
  public readonly int Ok;
  public readonly ExpoJsiValueRef Value;
  public readonly ExpoJsiError Error;
}
```

- [ ] **Step 4: Bind function pointers in `ExpoJsiApi`**

Add private delegates and public wrapper methods in `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs` using the same naming style as existing wrappers:

```csharp
public ExpoJsiRefScopeHandle CreateRefScope(ExpoJsiRuntimeHandle runtimeHandle);
public void ReleaseRefScope(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiRefScopeHandle scopeHandle);
public JavaScriptValueKind GetValueRefKind(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public bool ReadValueRefBool(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public double ReadValueRefDouble(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public string ReadValueRefString(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public string CoerceValueRefToString(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public ExpoJsiValueRefResult GetValueRefProperty(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef, ReadOnlySpan<byte> name);
public ExpoJsiValueRefResult GetValueRefAtIndex(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef, uint index);
public uint GetValueRefArrayLength(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public ExpoJsiValueResult RetainValueRef(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public ExpoJsiObjectResult RetainValueRefObject(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
public ExpoJsiArrayResult RetainValueRefArray(ExpoJsiRuntimeHandle runtimeHandle, ExpoJsiValueRef valueRef);
```

Bump `ExpectedVersion` after the native `kApiVersion` bump in Task 3.

- [ ] **Step 5: Run build to verify expected native-missing failures**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptScopedRefTests
```

Expected: native build or API validation fails because the new native entries are declared but not implemented.

## Task 3: Implement Native Ref Scope And Ref Operations

**Files:**
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`

- [ ] **Step 1: Add `RefScopeHandle`**

In `native/packages/jsi/src/ExpoJsiBridge.cpp`, add near `ArgumentsHandle`:

```cpp
class RefScopeHandle final {
public:
  expo_jsi_value_ref borrow(expo_jsi_value_handle value)
  {
    return expo_jsi_value_ref{this, value};
  }

  expo_jsi_value_ref store(std::unique_ptr<ValueHandle> value)
  {
    auto *handle = value.get();
    temporaryValues_.push_back(std::move(value));
    return expo_jsi_value_ref{this, handle};
  }

  bool owns(expo_jsi_value_handle value) const
  {
    for (auto const &temporary : temporaryValues_) {
      if (temporary.get() == value) {
        return true;
      }
    }
    return false;
  }

private:
  std::vector<std::unique_ptr<ValueHandle>> temporaryValues_;
};
```

- [ ] **Step 2: Add validation helpers**

Add helpers in the anonymous namespace:

```cpp
expo_jsi_error makeRefError(int32_t code, const char *message);
expo_jsi_value_ref_result makeValueRefErrorResult(int32_t code, const char *message);
expo_jsi_value_ref_result makeValueRefResult(expo_jsi_value_ref value);

expo_jsi_value_handle tryValueRef(expo_jsi_value_ref value, expo_jsi_error *error)
{
  if (value.scope == nullptr || value.value == nullptr) {
    if (error != nullptr) {
      *error = makeError(90, "JavaScript value ref is invalid.");
    }
    return nullptr;
  }
  return value.value;
}
```

Use the existing `makeError` / result style and assign unused error codes in the next contiguous range.

- [ ] **Step 3: Implement scope creation and release**

Add:

```cpp
expo_jsi_ref_scope_handle createRefScope(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return nullptr;
  }
  return new expo::jsi::RefScopeHandle();
}

void releaseRefScope(expo_jsi_runtime_handle, expo_jsi_ref_scope_handle scope)
{
  delete scope;
}
```

- [ ] **Step 4: Implement value-ref reads**

Implement value-ref kind, bool, double, string, and coerce-to-string by validating the ref and delegating to the same JSI operations used by owned `ValueHandle`.

Required behavior:

```text
undefined/null/bool/number/string reads match JavaScriptValue behavior.
invalid refs return structured native errors.
string results still use pointer + length + release callback.
```

- [ ] **Step 5: Implement object and array traversal**

Implement `valueRefGetProperty`, `valueRefGetValueAtIndex`, and `valueRefGetArrayLength`.

`valueRefGetProperty` must:

```cpp
auto &jsRuntime = runtimeHandle->runtime();
auto *value = tryValueRef(valueRef, &error);
if (!value->value().isObject()) {
  return makeValueRefErrorResult(code, "Value ref is not an object.");
}
auto object = value->value().asObject(jsRuntime);
auto propertyName = facebook::jsi::PropNameID::forUtf8(
  jsRuntime, reinterpret_cast<const uint8_t *>(name), static_cast<size_t>(name_len));
return makeValueRefResult(valueRef.scope->store(
  expo::jsi::ValueHandle::owned(object.getProperty(jsRuntime, propertyName))));
```

`valueRefGetValueAtIndex` and `valueRefGetArrayLength` must validate that the ref is an array object before reading.

- [ ] **Step 6: Implement ref retain operations**

Implement:

```text
valueRefRetain -> copy current jsi::Value into owned ValueHandle
valueRefRetainObject -> assert object and return owned ObjectHandle
valueRefRetainArray -> assert array and return owned ArrayHandle
```

These are the only escape hatch from scoped refs to owned wrappers.

- [ ] **Step 7: Wire API table and version**

Append the new functions to `apiTable` and bump native `kApiVersion` by one. Then update `ExpoJsiApi.ExpectedVersion` to the same value.

- [ ] **Step 8: Add testhost counting**

In `native/testhost/src/ExpoJsiTestHost.cpp`, wrap `release_ref_scope` so tests can prove scopes release. Add `released_ref_scopes` to the native counters struct and increment it from the wrapper before delegating to the real release function.

In `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`, add the matching field to `Counters`:

```csharp
public readonly uint ReleasedRefScopes;
```

- [ ] **Step 9: Run focused native verification**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptScopedRefTests
```

Expected: managed compile still fails until ref wrapper types are added.

## Task 4: Add Managed Scope And Ref Wrapper Types

**Files:**
- Create: `managed/packages/Expo.JSI/JsiRefScope.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptValueRef.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptArrayRef.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptValue.cs`

- [ ] **Step 1: Add `JsiRefScope`**

Create `managed/packages/Expo.JSI/JsiRefScope.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

internal sealed unsafe class JsiRefScope : IDisposable
{
  [ThreadStatic]
  private static JsiRefScope? current;

  private readonly JsiContext context;
  private readonly JsiRefScope? previous;
  private ExpoJsiRefScopeHandle handle;

  private JsiRefScope(JsiContext context, ExpoJsiRefScopeHandle handle)
  {
    this.context = context;
    this.handle = handle;
    previous = current;
    current = this;
  }

  public ExpoJsiRefScopeHandle Handle
  {
    get
    {
      ThrowIfDisposed();
      return handle;
    }
  }

  public JsiContext Context => context;

  public static JsiRefScope CurrentFor(JsiContext context)
  {
    if (current is null || current.handle == 0 || current.context.RuntimeHandle != context.RuntimeHandle)
    {
      throw new InvalidOperationException(
          "Scoped JavaScript refs require active runtime access through Execute, scheduled runtime work, or a host function callback."
      );
    }
    return current;
  }

  public static JsiRefScope Enter(JsiContext context)
  {
    var handle = context.Api->CreateRefScope(context.RuntimeHandle);
    if (handle == 0)
    {
      throw new InvalidOperationException("Failed to create a scoped JavaScript ref frame.");
    }
    return new JsiRefScope(context, handle);
  }

  public void Dispose()
  {
    if (handle != 0)
    {
      context.Api->ReleaseRefScope(context.RuntimeHandle, handle);
      handle = 0;
    }
    current = previous;
  }

  public void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, nameof(JsiRefScope));
  }
}
```

- [ ] **Step 2: Enter scopes around runtime work**

In `JavaScriptRuntime.InvokeHostFunction`, wrap callback invocation:

```csharp
using var scope = JsiRefScope.Enter(jsiContext);
var runtime = new JavaScriptRuntime(jsiContext);
var thisValue = new JavaScriptValueRef(jsiContext, scope, thisValueHandle);
var arguments = new JavaScriptArguments(jsiContext, argumentsHandle);
using var result = context.Callback(runtime, thisValue, arguments, context.Context);
```

In `RuntimeTaskContext.Invoke`, wrap `body(new JavaScriptRuntime(context))`:

```csharp
using var scope = JsiRefScope.Enter(context);
completion.TrySetResult(body(new JavaScriptRuntime(context)));
```

- [ ] **Step 3: Add `JavaScriptValueRef`**

Create `managed/packages/Expo.JSI/JavaScriptValueRef.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly ref struct JavaScriptValueRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope? scope;
  private readonly ExpoJsiValueHandle valueHandle;

  internal JavaScriptValueRef(JsiContext context, JsiRefScope scope, ExpoJsiValueHandle valueHandle)
  {
    this.context = context;
    this.scope = scope;
    this.valueHandle = valueHandle;
  }

  private ExpoJsiValueRef NativeRef => new(scope!.Handle, valueHandle);

  public JavaScriptValueKind Kind
  {
    get
    {
      ThrowIfInvalid();
      unsafe
      {
        ExpoJsiError error;
        var kind = context.Api->GetValueRefKind(context.RuntimeHandle, NativeRef, &error);
        context.ThrowIfError(error, "Failed to read JavaScript value ref kind.");
        return (JavaScriptValueKind)kind;
      }
    }
  }

  public bool IsNullish => Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;

  public bool AsBool()
  {
    ThrowIfInvalid();
    return context.Api->ReadValueRefBool(context.RuntimeHandle, NativeRef);
  }

  public double AsDouble()
  {
    ThrowIfInvalid();
    return context.Api->ReadValueRefDouble(context.RuntimeHandle, NativeRef);
  }

  public string AsString()
  {
    ThrowIfInvalid();
    return context.Api->ReadValueRefString(context.RuntimeHandle, NativeRef);
  }

  public string CoerceToString()
  {
    ThrowIfInvalid();
    return context.Api->CoerceValueRefToString(context.RuntimeHandle, NativeRef);
  }

  public JavaScriptObjectRef AsObject()
  {
    ThrowIfInvalid();
    if (Kind != JavaScriptValueKind.Object)
    {
      throw new InvalidOperationException("Value ref is not a JavaScript object.");
    }
    return new JavaScriptObjectRef(context, scope!, valueHandle);
  }

  public JavaScriptArrayRef AsArray()
  {
    ThrowIfInvalid();
    return new JavaScriptArrayRef(context, scope!, valueHandle);
  }

  public JavaScriptValue Retain()
  {
    ThrowIfInvalid();
    var result = context.Api->RetainValueRef(context.RuntimeHandle, NativeRef);
    if (result.Ok == 0 || result.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript value ref.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  private void ThrowIfInvalid()
  {
    if (scope is null || valueHandle == 0)
    {
      throw new ObjectDisposedException(nameof(JsiRefScope));
    }
    scope.ThrowIfDisposed();
  }
}
```

- [ ] **Step 4: Add object and array refs**

Create `JavaScriptObjectRef.cs` with:

```csharp
using System.Text;
using Expo.JSI.Interop;

namespace Expo.JSI;

public readonly ref struct JavaScriptObjectRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope scope;
  private readonly ExpoJsiValueHandle valueHandle;

  internal JavaScriptObjectRef(JsiContext context, JsiRefScope scope, ExpoJsiValueHandle valueHandle)
  {
    this.context = context;
    this.scope = scope;
    this.valueHandle = valueHandle;
  }

  private ExpoJsiValueRef NativeRef
  {
    get
    {
      ObjectDisposedException.ThrowIf(valueHandle == 0, nameof(JsiRefScope));
      return new ExpoJsiValueRef(scope.Handle, valueHandle);
    }
  }

  public JavaScriptValueRef GetProperty(string name)
  {
    ArgumentNullException.ThrowIfNull(name);
    var nameBytes = Encoding.UTF8.GetBytes(name);
    var result = context.Api->GetValueRefProperty(context.RuntimeHandle, NativeRef, nameBytes);
    if (result.Ok == 0 || result.Value.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript object ref property.");
    }
    return new JavaScriptValueRef(context, scope, result.Value.Value);
  }

  public JavaScriptObject Retain()
  {
    var result = context.Api->RetainValueRefObject(context.RuntimeHandle, NativeRef);
    if (result.Ok == 0 || result.Object == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript object ref.");
    }
    return new JavaScriptObject(context, result.Object);
  }

  public JavaScriptValue RetainAsValue() => new JavaScriptValueRef(context, scope, valueHandle).Retain();
}
```

Create `JavaScriptArrayRef.cs` with:

```csharp
namespace Expo.JSI;

public readonly ref struct JavaScriptArrayRef
{
  private readonly JsiContext context;
  private readonly JsiRefScope scope;
  private readonly Expo.JSI.Interop.ExpoJsiValueHandle valueHandle;

  internal JavaScriptArrayRef(
      JsiContext context,
      JsiRefScope scope,
      Expo.JSI.Interop.ExpoJsiValueHandle valueHandle
  )
  {
    this.context = context;
    this.scope = scope;
    this.valueHandle = valueHandle;
  }

  private Expo.JSI.Interop.ExpoJsiValueRef NativeRef
  {
    get
    {
      ObjectDisposedException.ThrowIf(valueHandle == 0, nameof(JsiRefScope));
      return new Expo.JSI.Interop.ExpoJsiValueRef(scope.Handle, valueHandle);
    }
  }

  public uint Length => context.Api->GetValueRefArrayLength(context.RuntimeHandle, NativeRef);

  public JavaScriptValueRef GetValue(uint index)
  {
    var result = context.Api->GetValueRefAtIndex(context.RuntimeHandle, NativeRef, index);
    if (result.Ok == 0 || result.Value.Value == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array ref value.");
    }
    return new JavaScriptValueRef(context, scope, result.Value.Value);
  }

  public JavaScriptArray Retain()
  {
    var result = context.Api->RetainValueRefArray(context.RuntimeHandle, NativeRef);
    if (result.Ok == 0 || result.Array == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript array ref.");
    }
    return new JavaScriptArray(context, result.Array);
  }

  public JavaScriptValue RetainAsValue() => new JavaScriptValueRef(context, scope, valueHandle).Retain();
}
```

- [ ] **Step 5: Add `Ref` and `Retain` to owned wrappers**

In `JavaScriptValue.cs`, add:

```csharp
public bool IsNullish
{
  get
  {
    ThrowIfDisposed();
    return Kind is JavaScriptValueKind.Undefined or JavaScriptValueKind.Null;
  }
}

public JavaScriptValueRef Ref
{
  get
  {
    ThrowIfDisposed();
    var scope = JsiRefScope.CurrentFor(context);
    return new JavaScriptValueRef(context, scope, handle);
  }
}

public JavaScriptValue Retain() => AsValue();
```

Do not add `JavaScriptObject.Ref` or `JavaScriptArray.Ref` in this slice. A
property returning a ref from a temporary `AsValue()` would either dispose the
root too early or hide an owned value. Keep the first slice value-centric.

- [ ] **Step 6: Run focused tests**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptScopedRefTests
```

Expected: scoped-ref runtime tests pass except host-function/API migration tests that still reference `JavaScriptBorrowedValue`.

## Task 5: Migrate Host Function Arguments And Error Accessors

**Files:**
- Modify: `managed/packages/Expo.JSI/JavaScriptHostFunction.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptArguments.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptErrorObject.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptValueHolder.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`

- [ ] **Step 1: Change host-function delegate**

Change `JavaScriptHostFunction.cs`:

```csharp
namespace Expo.JSI;

public delegate JavaScriptValue JavaScriptHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptValueRef thisValue,
    JavaScriptArguments arguments,
    object context
);
```

- [ ] **Step 2: Replace `GetBorrowedValue`**

In `JavaScriptArguments.cs`, replace `GetBorrowedValue` with:

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

    var scope = JsiRefScope.CurrentFor(context);
    return new JavaScriptValueRef(context, scope, result.Value);
  }
}
```

Remove `GetBorrowedValue` after updating all call sites in this plan.

- [ ] **Step 3: Update runtime host callback**

In `JavaScriptRuntime.InvokeHostFunction`, create `thisValue` as:

```csharp
using var scope = JsiRefScope.Enter(jsiContext);
var runtime = new JavaScriptRuntime(jsiContext);
var thisValue = new JavaScriptValueRef(jsiContext, scope, thisValueHandle);
var arguments = new JavaScriptArguments(jsiContext, argumentsHandle);
using var result = context.Callback(runtime, thisValue, arguments, context.Context);
return new ExpoJsiValueResult(1, result.Detach(), default);
```

- [ ] **Step 4: Simplify `JavaScriptValueHolder`**

Replace `JavaScriptValueHolder` with:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Expo.JSI;

internal sealed class JavaScriptValueHolder : IJavaScriptValueRepresentable, IDisposable
{
  private JavaScriptValue? value;

  public JavaScriptValueHolder(JavaScriptValue value)
  {
    this.value = value;
  }

  public JavaScriptValueRef Ref
  {
    get
    {
      ThrowIfDisposed();
      return value.Ref;
    }
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    return value.AsValue();
  }

  public void Dispose()
  {
    value?.Dispose();
    value = null;
  }

  [MemberNotNull(nameof(value))]
  private void ThrowIfDisposed() =>
    ObjectDisposedException.ThrowIf(value is null, this);
}
```

- [ ] **Step 5: Move error accessors to refs**

In `JavaScriptErrorObject.cs`, replace `GetNullableStringProperty` with:

```csharp
private string? GetNullableStringProperty(string name)
{
  var property = holder.Ref.AsObject().GetProperty(name);
  return property.IsNullish ? null : property.CoerceToString();
}
```

- [ ] **Step 6: Update temporary module conversion proof**

In `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`, change codecs from `JavaScriptBorrowedValue` to `JavaScriptValueRef`:

```csharp
private interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
}
```

Update call sites:

```csharp
TCodec.Decode(arguments.GetValue(0), runtime)
```

For array decode, use:

```csharp
var array = value.AsArray();
var length = array.Length;
```

and `array.GetValue(index)` for element refs.

- [ ] **Step 7: Run host and module tests**

Run:

```sh
scripts/test-jsi.sh --filter "HostFunctionTests|ArrayConversionTests|JavaScriptValueRepresentableTests"
```

Expected: selected tests pass.

## Task 6: Prove Scope Release And Finish Verification

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptScopedRefTests.cs`

- [ ] **Step 1: Add release-counter assertion**

Add a test to `JavaScriptScopedRefTests.cs`:

```csharp
  [Fact]
  public void RefTraversalTemporariesAreReleasedWhenScopeExits()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    fixture.Runtime.Execute(_ =>
    {
      using var value = fixture.Evaluate(
          "({ a: { b: { c: 'done' } } })",
          "scoped-ref-release.js"
      );

      var c = value.Ref.AsObject()
          .GetProperty("a")
          .AsObject()
          .GetProperty("b")
          .AsObject()
          .GetProperty("c");

      Assert.Equal("done", c.AsString());
      return true;
    });

    var counters = fixture.Counters;
    Assert.True(counters.ReleasedRefScopes >= 1);
  }
```

- [ ] **Step 2: Run full JSI suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all Hermes-backed JSI tests pass.

- [ ] **Step 3: Run formatting checks**

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

- [ ] **Step 4: Run whitespace check**

Run:

```sh
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 5: Inspect diff for local-path leaks**

Run:

```sh
git diff -- . ':!docs/superpowers/plans/2026-06-28-jsi-scoped-ref-ownership.md'
```

Expected: no committed docs or source changes contain concrete local paths, usernames, machine names, private hostnames, or machine-specific install paths.

## Self-Review Notes

- Spec coverage: tasks cover native ABI, native scope storage, managed interop, managed ref types, runtime/host-function scope entry, argument migration, error accessor cleanup, and verification.
- Scope choice: the plan does not use worktrees because this repo's instructions forbid worktrees unless the user explicitly asks for them.
- Public API break: `JavaScriptBorrowedValue` is replaced by `JavaScriptValueRef` in new public callback/argument APIs.
- Deferred item: `JavaScriptFunctionRef` is not implemented in this first plan because function calls are not part of the current wrapper surface.
