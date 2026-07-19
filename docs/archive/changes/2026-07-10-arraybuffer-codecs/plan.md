# ArrayBuffer And Binary Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver production ArrayBuffer ownership, scoped byte access, and generated binary codecs across the portable Expo.JSI bridge and Expo.ModulesCore.

**Architecture:** Native C++ owns a runtime-scoped long-lived-state collection plus opaque JavaScriptArrayBuffer and MutableBuffer handles. `Expo.JSI` exposes low-level owned/scoped wrappers, while `Expo.ModulesCore.ArrayBuffer` selects JavaScript-backed or native-backed storage and generated bindings enforce copy, borrow, transfer, and async cleanup rules.

**Tech Stack:** C++20, React Native JSI/Hermes, C ABI function table, .NET 10/C# unsafe interop, Roslyn incremental source generation, xUnit, repo-owned Hermes testhost.

## Global Constraints

- C++ owns JSI mechanics; C# owns module logic; only opaque handles cross the C ABI.
- Actual JavaScript `ArrayBuffer` values are accepted; TypedArray compatibility is excluded.
- Storage has exactly two semantic kinds: runtime-affine JavaScript-backed and runtime-neutral native-backed.
- Native-backed storage owns `shared_ptr<jsi::MutableBuffer>` and preserves byte aliasing, not JavaScript object identity.
- JavaScript-backed storage preserves identity only in its originating runtime and rejects cross-runtime encoding.
- Arbitrary managed arrays SHALL NOT be pinned or adopted for JavaScript lifetime.
- `byte[]` and span returns copy; `ArrayBuffer` is the move-capable return type.
- Spans SHALL NOT cross `await`, scheduled callback, or disposal boundaries.
- Generated methods MAY declare at most one `Span<byte>` or
  `ReadOnlySpan<byte>` parameter in total; this limit does not apply to
  `ArrayBuffer`, `byte[]`, or ordinary parameters.
- Runtime-owned JSI state SHALL be swept while JSI is valid; late invalidation SHALL never touch stale JSI.
- Native-backed async byte access runs inline and returns a completed,
  faulted, or canceled Task; callback exceptions SHALL NOT escape synchronously
  and the implementation SHALL NOT use `Task.Run`.
- Promise capability migration is excluded; only owned async-result abandonment cleanup is included.
- Do not add runtime reflection, dynamic invocation, JSON conversion, or platform UI dependencies.
- Do not commit local absolute paths, usernames, hostnames, or machine-specific install paths.

---

## File Map

### Native bridge and lifecycle

- Create `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h`: copy-safe entry state, active/closing/invalid lifecycle, exactly-once claim/release/abandon behavior.
- Create `packages/expo-modules-dotnet/native/packages/jsi/src/ArrayBufferHandles.h`: native-owned MutableBuffer implementation and opaque ArrayBuffer/MutableBuffer handle state.
- Modify `packages/expo-modules-dotnet/native/include/expo_jsi.h`: append ArrayBuffer handles, results, and function pointers; bump ABI version through the implementation table.
- Modify `packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h`: expose pre-invalidation runtime preparation to host adapters.
- Modify `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`: runtime state, ABI functions, validation, API table, and ArrayBuffer conversion.
- Modify Android, iOS, and macOS installers: run live-JSI preparation before connector invalidation.
- Modify Windows installer: keep its documented late-invalidation path and abandon entries without JSI access.
- Modify native testhost header/source: early/late teardown hooks, size-snapshot validation hook, and lifetime counters.

### Low-level managed wrappers

- Modify `ExpoJsiHandles.cs`, `ExpoJsiTypes.cs`, and `ExpoJsiApi.cs`: mirror the appended ABI exactly and set managed version 22.
- Create `JavaScriptArrayBufferRef.cs`, `JavaScriptArrayBuffer.cs`, `JavaScriptMutableBuffer.cs`, and `JavaScriptByteAccess.cs`: scoped/owned wrappers and span delegates.
- Modify `JavaScriptValueInner.cs`, `JavaScriptValueRef.cs`, `JavaScriptValue.cs`, and `JavaScriptRuntime.cs`: conversions and MutableBuffer-backed ArrayBuffer creation.
- Create `Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs`: low-level identity, aliasing, byte access, detachment, cross-runtime, and teardown coverage.
- Modify both managed `NativeTestHost.cs` fixtures and `HermesRuntimeFixture.cs` fixtures to mirror testhost hooks and counters.

### ModulesCore and generation

- Create `Expo.ModulesCore/ArrayBuffer.cs`: two-backing public abstraction, factories, explicit ownership, copy APIs, and scoped byte access.
- Create `Expo.ModulesCore/Codecs/ArrayBufferCodec.cs` and `ByteArrayCodec.cs`: owned wrapper and copy codecs.
- Modify `JavaScriptPromiseResult.cs` and `JavaScriptPromiseScheduler.cs`: copy-safe owned-result claim/abandon cleanup.
- Modify generator model, diagnostics, and emitter: owned ArrayBuffer locals,
  byte arrays, one scoped span parameter, copied span returns, and diagnostics
  for async or multiple span parameters.
- Add generator source-shape tests and Hermes-backed generated binary module tests.

### Durable documentation

- Merge the accepted delta into the seven affected living specs, including the
  Hermes testhost contract.
- Update `docs/roadmap.md` and `docs/plans/README.md` with ArrayBuffer completion and the focused Promise lifetime migration follow-up.
- Archive the obsolete spike plan and the completed change artifacts.

---

### Task 1: Add Runtime-owned State And ArrayBuffer ABI Primitives

**Files:**
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/ArrayBufferHandles.h`
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Interop/ExpoJsiApiTests.cs`

**Interfaces:**
- Consumes: existing `JsiRuntimeConnector`, `JsiRuntimeExecutor`, `ValueHandle`, structured ABI errors, and JSI `ArrayBuffer::tryGetMutableBuffer`/`detached`.
- Produces: ABI v22; opaque `expo_jsi_array_buffer_handle` and `expo_jsi_mutable_buffer_handle`; `prepareRuntimeHandleForInvalidation`; ArrayBuffer retain/access/encode functions used by Task 2.

- [ ] **Step 1: Write the ABI v22 failing test**

Update `ExpoJsiApiTests` to require version 22 and to reject a table truncated before the final ArrayBuffer function pointer:

```csharp
[Fact]
public unsafe void AbiVersionAndSizeIncludeArrayBufferTail()
{
  Assert.Equal(22u, ExpoJsiApi.ExpectedVersion);
  Assert.Equal((uint)sizeof(ExpoJsiApi), ExpoJsiApi.ExpectedSize);

  var truncated = new FakeExpoJsiApi
  {
    Size = ExpoJsiApi.ExpectedSize - (uint)IntPtr.Size,
    Version = ExpoJsiApi.ExpectedVersion,
  };
  Assert.Throws<InvalidOperationException>(() =>
    JavaScriptRuntime.FromNative((nint)(&truncated), 1));
}
```

- [ ] **Step 2: Run the focused test and verify the version failure**

Run:

```bash
scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests
```

Expected: FAIL because native and managed still report ABI version 21 and no ArrayBuffer tail exists.

- [ ] **Step 3: Append opaque handles and result structures to the C ABI**

Add C++ forward declarations/C aliases and C opaque declarations following the existing handle pattern:

```c
typedef struct expo_jsi_array_buffer_t *expo_jsi_array_buffer_handle;
typedef struct expo_jsi_mutable_buffer_t *expo_jsi_mutable_buffer_handle;

typedef struct expo_jsi_array_buffer_result {
  int32_t ok;
  expo_jsi_array_buffer_handle array_buffer;
  int32_t byte_length;
  expo_jsi_error error;
} expo_jsi_array_buffer_result;

typedef struct expo_jsi_mutable_buffer_result {
  int32_t ok;
  int32_t found;
  expo_jsi_mutable_buffer_handle mutable_buffer;
  int32_t byte_length;
  expo_jsi_error error;
} expo_jsi_mutable_buffer_result;

typedef struct expo_jsi_byte_span_result {
  int32_t ok;
  uint8_t *data;
  int32_t length;
  expo_jsi_error error;
} expo_jsi_byte_span_result;

typedef expo_jsi_array_buffer_result (*expo_jsi_array_buffer_retain_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value);
typedef expo_jsi_array_buffer_result (*expo_jsi_array_buffer_clone_handle_fn)(
  expo_jsi_array_buffer_handle array_buffer);
typedef expo_jsi_byte_span_result (*expo_jsi_array_buffer_get_bytes_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_array_buffer_handle array_buffer);
typedef expo_jsi_value_result (*expo_jsi_array_buffer_as_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_array_buffer_handle array_buffer);
typedef void (*expo_jsi_array_buffer_release_fn)(
  expo_jsi_array_buffer_handle array_buffer);
typedef expo_jsi_mutable_buffer_result
  (*expo_jsi_array_buffer_try_get_mutable_buffer_fn)(
    expo_jsi_runtime_handle runtime,
    expo_jsi_value_handle value);

typedef expo_jsi_mutable_buffer_result (*expo_jsi_mutable_buffer_allocate_fn)(
  int32_t length);
typedef expo_jsi_mutable_buffer_result (*expo_jsi_mutable_buffer_copy_fn)(
  const uint8_t *data,
  int32_t length);
typedef expo_jsi_mutable_buffer_result
  (*expo_jsi_mutable_buffer_clone_handle_fn)(
    expo_jsi_mutable_buffer_handle mutable_buffer);
typedef expo_jsi_byte_span_result (*expo_jsi_mutable_buffer_get_bytes_fn)(
  expo_jsi_mutable_buffer_handle mutable_buffer);
typedef expo_jsi_value_result (*expo_jsi_mutable_buffer_as_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_mutable_buffer_handle mutable_buffer);
typedef void (*expo_jsi_mutable_buffer_release_fn)(
  expo_jsi_mutable_buffer_handle mutable_buffer);
```

Every fallible result carries `ok` plus `error`. For
`expo_jsi_mutable_buffer_result`, `found` is meaningful only when `ok == 1`;
`ok == 1 && found == 0` is the successful JavaScript-heap-backed lookup case.
Allocation, copy, and clone operations set `found == 1` on success.
Every successful ArrayBuffer or MutableBuffer handle result sets
`byte_length` from a checked conversion to `int32_t`. ArrayBuffer clone results
reuse the entry's captured logical length; MutableBuffer clone results reuse the
storage length. A successful lookup with `found == 0` returns no handle and sets
`byte_length == 0`; the following ArrayBuffer retain operation returns the
logical length. Managed wrappers snapshot this field, so `ByteLength` never
calls `*_get_bytes` or enters JSI.
Returned byte spans are borrowed only for the duration of the managed callback
while the source handle remains leased.

Append, never insert, these function-pointer fields to `expo_jsi_api`:

```c
expo_jsi_array_buffer_retain_fn array_buffer_retain;
expo_jsi_array_buffer_clone_handle_fn array_buffer_clone_handle;
expo_jsi_array_buffer_get_bytes_fn array_buffer_get_bytes;
expo_jsi_array_buffer_as_value_fn array_buffer_as_value;
expo_jsi_array_buffer_release_fn array_buffer_release;
expo_jsi_array_buffer_try_get_mutable_buffer_fn array_buffer_try_get_mutable_buffer;
expo_jsi_mutable_buffer_allocate_fn mutable_buffer_allocate;
expo_jsi_mutable_buffer_copy_fn mutable_buffer_copy;
expo_jsi_mutable_buffer_clone_handle_fn mutable_buffer_clone_handle;
expo_jsi_mutable_buffer_get_bytes_fn mutable_buffer_get_bytes;
expo_jsi_mutable_buffer_as_value_fn mutable_buffer_as_value;
expo_jsi_mutable_buffer_release_fn mutable_buffer_release;
```

Use `int32_t` lengths after checked `size_t` conversion; reject values above `INT32_MAX`.
For zero-length storage, a successful `expo_jsi_byte_span_result` may contain
`data == nullptr`; managed code must construct an empty span instead of
rejecting the pointer.

- [ ] **Step 4: Implement the long-lived collection state machine**

Create a header-only internal implementation with these exact states and public operations:

```cpp
enum class LongLivedEntryState {
  Active,
  ReleaseQueued,
  ReleaseDeferred,
  Released,
  Invalidated
};

class LongLivedObjectCollection final {
public:
  uint64_t add(std::shared_ptr<LongLivedObject> object);
  void requestRelease(uint64_t id, JsiRuntimeExecutor &executor) noexcept;
  void sweep(jsi::Runtime &runtime) noexcept;
  void invalidateWithoutRuntime() noexcept;
  bool empty() const noexcept;
};
```

`requestRelease` and `sweep` must converge through one atomic/mutex-protected
transition. `requestRelease` captures a shared, copy-safe
`ScheduledReleaseToken`: invoking any scheduled copy releases the entry on the
runtime; destroying the final uninvoked copy transitions `ReleaseQueued` to
`ReleaseDeferred`. Multiple executor copies and cleanup paths must converge
exactly once. The next valid runtime access drains deferred releases before
performing its requested operation, and teardown sweeps queued and deferred
entries.

The token's shared state strongly owns `std::shared_ptr<RuntimeState>` plus the
entry id and an atomic completed flag. It never captures a raw collection,
entry, connector, executor, or runtime pointer. `run(jsi::Runtime&)` asks the
retained runtime state to complete the release and marks the token complete;
final uninvoked destruction asks the retained state to defer the release
without touching JSI. `RuntimeState` clears its borrowed connector/runtime
pointers when invalidated but remains a lifetime-safe tombstone until all
tokens drop. Collection entries keep only a weak back-reference to runtime
state, so this strong token edge cannot form a cycle. Both token paths no-op
when the state or entry is already `Invalidated` or `Released`.

`LongLivedObject::release(jsi::Runtime&)` destroys its retained JSI payload on
the runtime. The ArrayBuffer entry stores that payload behind a detachable
owner such as `std::unique_ptr<jsi::ArrayBuffer>`. On late invalidation,
`invalidateWithoutRuntime()` atomically calls `release()` on that owner without
running the JSI destructor, clears all accessible payload metadata, marks the
entry `Invalidated`, and increments the abandonment counter once. The detached
raw payload is an intentional bounded quarantine: do not put it in any owning
static container whose destructor could later run off-runtime. Later handle
disposal is a no-op for native payload destruction.

- [ ] **Step 5: Implement opaque native buffer handles**

Create `OwnedMutableBuffer` and handle classes in `ArrayBufferHandles.h`:

```cpp
class OwnedMutableBuffer final : public jsi::MutableBuffer {
public:
  explicit OwnedMutableBuffer(size_t size) : bytes_(size, 0) {}
  explicit OwnedMutableBuffer(std::span<const uint8_t> bytes)
    : bytes_(bytes.begin(), bytes.end()) {}

  size_t size() const override { return bytes_.size(); }
  uint8_t *data() override { return bytes_.data(); }

private:
  std::vector<uint8_t> bytes_;
};
```

`MutableBufferHandle` owns one `shared_ptr<jsi::MutableBuffer>`.
`ArrayBufferHandle` owns one lease on a long-lived entry containing the
originating runtime-state identity, captured length, and detachable original
`jsi::ArrayBuffer`. Handle cloning increments lease ownership without touching
JSI.

- [ ] **Step 6: Extend RuntimeHandle with closing and invalid states**

Replace the raw-connector-only lifetime with shared runtime state:

```cpp
enum class RuntimeStateKind { Active, Closing, Invalid };

class RuntimeState final : public std::enable_shared_from_this<RuntimeState> {
public:
  jsi::Runtime &runtime();
  JsiRuntimeExecutor &executor();
  void drainDeferredReleases(jsi::Runtime &runtime) noexcept;
  void prepareForInvalidation();
  void invalidateWithoutRuntime() noexcept;
  LongLivedObjectCollection &longLivedObjects() noexcept;
};
```

Every active-runtime ABI entry point calls `drainDeferredReleases` before its
requested operation. `prepareForInvalidation` first changes Active to Closing,
then uses the connector executor's synchronous path to sweep active, queued,
and deferred entries on the runtime. Calls that create or schedule new retained
work reject Closing. `releaseRuntimeHandle` calls `invalidateWithoutRuntime` if
preparation did not complete.

- [ ] **Step 7: Implement ArrayBuffer ABI operations and append the API table**

Implement all appended functions with structured errors. The critical selection and validation logic is:

```cpp
auto arrayBuffer = checkedArrayBuffer(runtime, value);
if (arrayBuffer.detached(runtime)) {
  throw std::invalid_argument("ArrayBuffer is detached.");
}
auto size = checkedManagedLength(arrayBuffer.size(runtime));

if (auto mutableBuffer = arrayBuffer.tryGetMutableBuffer(runtime)) {
  return makeMutableBufferResult(std::move(mutableBuffer));
}
return makeArrayBufferResult(runtimeState.retain(std::move(arrayBuffer), size));
```

Every JS-backed access and same-runtime conversion re-checks `detached(runtime)` and exact size equality. Native-backed conversion calls `runtime.createArrayBuffer(sharedBuffer)` and therefore preserves bytes but creates a new object.

Set native `kApiVersion = 22`, mirror every field in `ExpoJsiApi`, add managed handle aliases/result structs, set `ExpectedVersion = 22`, and extend `Validate()` to require the complete appended tail.

- [ ] **Step 8: Run ABI tests and format checks**

Run:

```bash
scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests
scripts/format.sh --check --all
git diff --check
```

Expected: all commands PASS; the native testhost and managed table agree on ABI v22.

- [ ] **Step 9: Commit the ABI foundation**

```bash
git add packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
  packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ArrayBufferHandles.h \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Interop/ExpoJsiApiTests.cs
git commit -m "feat(jsi): add ArrayBuffer ABI storage primitives"
```

### Task 2: Add Low-level Expo.JSI ArrayBuffer Wrappers

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptByteAccess.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptArrayBufferRef.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptArrayBuffer.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptMutableBuffer.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Internal/JavaScriptValueInner.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueRef.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs`

**Interfaces:**
- Consumes: Task 1 ABI v22 operations.
- Produces: scoped `JavaScriptArrayBufferRef`, owned runtime-affine `JavaScriptArrayBuffer`, runtime-neutral `JavaScriptMutableBuffer`, and runtime conversion used by ModulesCore.

- [ ] **Step 1: Write failing low-level wrapper tests**

Create tests for all low-level ownership branches:

```csharp
[Fact]
public void JavaScriptHeapBufferRetainsIdentityAndMutations()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var original = fixture.Evaluate("new ArrayBuffer(4)", "array-buffer.js");
    using var buffer = original.Ref.AsArrayBuffer().Retain();
    buffer.WithBytes(bytes => bytes[1] = 42);
    using var returned = buffer.AsValue(runtime);
    Assert.True(runtime.StrictEquals(original, returned));
    using var returnedBuffer = returned.Ref.AsArrayBuffer().Retain();
    returnedBuffer.WithReadOnlyBytes(bytes => Assert.Equal(42, bytes[1]));
    return true;
  });
}

[Fact]
public void NativeMutableBufferCreatesAliasingDistinctObjects()
{
  using var fixture = HermesRuntimeFixture.Create();
  using var storage = JavaScriptMutableBuffer.Allocate(4);
  storage.WithBytes(bytes => bytes[0] = 7);
  fixture.Runtime.Execute(runtime =>
  {
    using var left = runtime.CreateArrayBufferValue(storage);
    using var right = runtime.CreateArrayBufferValue(storage);
    Assert.False(runtime.StrictEquals(left, right));
    using var leftBuffer = left.Ref.AsArrayBuffer().Retain();
    using var rightBuffer = right.Ref.AsArrayBuffer().Retain();
    leftBuffer.WithBytes(bytes => bytes[0] = 31);
    rightBuffer.WithReadOnlyBytes(bytes => Assert.Equal(31, bytes[0]));
    return true;
  });
}
```

Also cover zero-filled allocation, `CopyFrom`, read-only access, zero length, disposed access, `int.MaxValue` overflow rejection through an injected native result, TypedArray conversion rejection, and `HermesInternal.detachArrayBuffer` invalidation.

- [ ] **Step 2: Run tests and verify missing-type failures**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptArrayBufferTests
```

Expected: FAIL because the wrapper types and conversions do not exist.

- [ ] **Step 3: Add span callback delegates**

Define non-async custom delegates so spans cannot be stored in `Func<T>` state that outlives access:

```csharp
public delegate void JavaScriptBytesAction(Span<byte> bytes);
public delegate TResult JavaScriptBytesFunc<TResult>(Span<byte> bytes);
public delegate void JavaScriptReadOnlyBytesAction(ReadOnlySpan<byte> bytes);
public delegate TResult JavaScriptReadOnlyBytesFunc<TResult>(ReadOnlySpan<byte> bytes);
```

- [ ] **Step 4: Implement scoped and owned wrappers**

`JavaScriptArrayBufferRef` is a `readonly ref struct` tied to `JavaScriptHandleScope`; `Retain()` creates the first runtime-owned long-lived lease. `TryGetMutableBuffer()` returns an independently owned `JavaScriptMutableBuffer` when native storage exists.

Keep `JavaScriptArrayBufferRef` conversion/retention-only. It does not expose a
direct byte or length ABI call: retaining returns the handle and checked length
in one operation, and all byte access proceeds through the resulting owned
wrapper. This avoids a temporary long-lived entry for a property peek.

`JavaScriptArrayBuffer` implements `IDisposable`, keeps its originating `JsiContext`, and exposes:

```csharp
public int ByteLength { get; }
public JavaScriptArrayBuffer Retain();
public void WithBytes(JavaScriptBytesAction action);
public TResult WithBytes<TResult>(JavaScriptBytesFunc<TResult> action);
public void WithReadOnlyBytes(JavaScriptReadOnlyBytesAction action);
public TResult WithReadOnlyBytes<TResult>(JavaScriptReadOnlyBytesFunc<TResult> action);
public Task WithBytesAsync(
    JavaScriptBytesAction action,
    CancellationToken cancellationToken = default);
public Task<TResult> WithBytesAsync<TResult>(
    JavaScriptBytesFunc<TResult> action,
    CancellationToken cancellationToken = default);
public Task WithReadOnlyBytesAsync(
    JavaScriptReadOnlyBytesAction action,
    CancellationToken cancellationToken = default);
public Task<TResult> WithReadOnlyBytesAsync<TResult>(
    JavaScriptReadOnlyBytesFunc<TResult> action,
    CancellationToken cancellationToken = default);
public JavaScriptValue AsValue(JavaScriptRuntime runtime);
public void Dispose();
```

`WithBytes` and `WithReadOnlyBytes` verify
`JavaScriptHandleScope.IsCurrentFor(context)` before calling the ABI. Async
access takes a scheduling lease before it queues work and releases that lease
on success, callback failure, scheduling failure, cancellation, dropped work,
or teardown. The callback itself remains synchronous so no `Span<byte>` or
`ReadOnlySpan<byte>` crosses an `await`:

```csharp
public Task<TResult> WithBytesAsync<TResult>(
    JavaScriptBytesFunc<TResult> action,
    CancellationToken cancellationToken = default)
{
  ArgumentNullException.ThrowIfNull(action);
  var schedulingLease = Retain();
  return ExecuteWithLeaseAsync(schedulingLease, action, cancellationToken);
}

private static async Task<TResult> ExecuteWithLeaseAsync<TResult>(
    JavaScriptArrayBuffer schedulingLease,
    JavaScriptBytesFunc<TResult> action,
    CancellationToken cancellationToken)
{
  try
  {
    return await new JavaScriptRuntime(schedulingLease.context).ExecuteAsync(
        _ => schedulingLease.WithBytes(action),
        JavaScriptTaskPriority.Immediate,
        cancellationToken
    ).ConfigureAwait(false);
  }
  finally
  {
    schedulingLease.Dispose();
  }
}
```

Implement the action and read-only overloads through the same lease-owning
core, rather than scheduling a borrowed span or adding an untracked task path.
`AsValue` rejects a different runtime context before native access.

`JavaScriptMutableBuffer` implements `IDisposable`, is runtime-neutral, and
exposes `Allocate`, `CopyFrom`, `Retain`, `ByteLength`, mutable/read-only direct
callback access, and conversion through
`JavaScriptRuntime.CreateArrayBufferValue`.

Both owned wrapper types store the successful ABI result's `byte_length` in a
readonly managed field. Their `ByteLength` getters return that field after the
disposed-state check; they never project bytes or enter a runtime. Add tests
that read `ByteLength` outside a runtime frame, after handle cloning, and for a
zero-length buffer whose native data pointer is null.

- [ ] **Step 5: Add value and runtime conversions**

Add `IsArrayBuffer` and `AsArrayBuffer()` to both value surfaces. The conversion must check `JavaScriptValueKind.ArrayBuffer`, not `IsObject`, so TypedArray views fail.

Add the native-backed conversion without creating a retained JS-backed wrapper:

```csharp
public JavaScriptValue CreateArrayBufferValue(JavaScriptMutableBuffer storage)
{
  ArgumentNullException.ThrowIfNull(storage);
  return JavaScriptValue.FromOwnedHandle(context, storage.CreateValueHandle(context));
}
```

Document every returned owned wrapper and every borrowed callback.

- [ ] **Step 6: Run low-level tests**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptArrayBufferTests
scripts/format.sh --check --all
git diff --check
```

Expected: PASS with JS-backed identity and native-backed alias tests green.

- [ ] **Step 7: Commit low-level wrappers**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.JSI \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs
git commit -m "feat(jsi): add managed ArrayBuffer wrappers"
```

### Task 3: Integrate Early And Late Runtime Teardown

**Files:**
- Modify: `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`
- Modify: `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs`

**Interfaces:**
- Consumes: `prepareRuntimeHandleForInvalidation` and runtime-state counters from Task 1.
- Produces: deterministic release/sweep interleavings, early sweep for
  Android/iOS/macOS/testhost, and explicit no-JSI abandonment for the
  Windows/late test path.

- [ ] **Step 1: Add failing teardown tests and counters**

Extend the testhost counter struct in native and both managed mirrors with the same ordered fields:

```c
uint32_t long_lived_array_buffers_released;
uint32_t long_lived_array_buffers_abandoned;
```

These are consumer-specific test metrics for ArrayBuffer entries, not totals
for the generic collection. Keep the names in both native and managed mirrors
so future collection consumers can add their own counters without changing the
meaning of these fields.

Add fixture methods `PrepareRuntimeForInvalidation()` and `InvalidateRuntime()` and tests:

```csharp
[Fact]
public void EarlyTeardownSweepsJavaScriptBackedBufferExactlyOnce()
{
  using var fixture = HermesRuntimeFixture.Create();
  var retained = fixture.Runtime.Execute(_ =>
  {
    using var value = fixture.Evaluate("new ArrayBuffer(8)", "retained-buffer.js");
    return value.Ref.AsArrayBuffer().Retain();
  });

  fixture.PrepareRuntimeForInvalidation();
  fixture.InvalidateRuntime();
  retained.Dispose();

  Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased);
  Assert.Equal(0u, fixture.Counters.LongLivedArrayBuffersAbandoned);
}
```

Add the complementary late invalidation test expecting zero stale-JSI releases, one abandoned entry, failing access, and idempotent disposal.

Add deterministic tests for both last-lease-release/teardown interleavings and
for dropped scheduled work. Extend the Hermes test executor with test-only
pause/resume and drop-next-task controls; do not use sleeps:

```text
sweep wins:
  pause executor
  dispose final lease (queues Normal-priority release)
  start PrepareRuntimeForInvalidation on a worker (queues Immediate sync sweep)
  wait until the testhost observes the Immediate task in the paused queue
  resume executor
  await preparation and drain the losing release callback
  assert released == 1, abandoned == 0

scheduled release wins:
  dispose final lease
  drain executor until release completes
  call PrepareRuntimeForInvalidation
  assert released == 1, abandoned == 0

scheduled work is dropped while runtime remains active:
  configure executor to drop the next Normal-priority task
  dispose final lease
  perform a benign runtime access to drain ReleaseDeferred
  assert released == 1, abandoned == 0

queued token outlives runtime handle, invoked path:
  pause executor
  dispose final lease and wait until its Normal-priority task is queued
  release only the bridge runtime handle, leaving the testhost executor alive
  resume executor so the queued token observes the invalid tombstone
  assert released == 0, abandoned == 1, with no crash or second transition

queued token outlives runtime handle, dropped path:
  pause executor
  dispose final lease and wait until its Normal-priority task is queued
  release only the bridge runtime handle
  destroy the queued callable without invoking it
  assert released == 0, abandoned == 1, with no crash or second transition
```

The queue-observation barrier, rather than timing, proves that the Immediate
sweep is present before resume and therefore wins priority ordering. The pause
control must let the sync prepare call block on a worker without blocking the
test thread that resumes the executor. The counter assertions are made only
after all competing callbacks have been drained, proving that the losing path
cannot release twice.

Add low-level `WithBytesAsync`/`WithReadOnlyBytesAsync` cleanup tests after the
counters exist. In every case, start async access, dispose the caller's original
lease while work remains pending, and then assert the scheduling lease reaches
exactly one terminal counter transition:

```text
callback throws              => Task faults, released == 1, abandoned == 0
token canceled before run    => Task cancels, released == 1, abandoned == 0
Immediate task is dropped    => Task faults, released == 1, abandoned == 0
early teardown while pending => released == 1, abandoned == 0
late invalidation pending    => released == 0, abandoned == 1
```

Gate cancellation, drop, and teardown with pause/queue-observation controls;
drain all surviving callbacks before reading counters. Assert the byte callback
does not run for pre-run cancellation, dropped work, or teardown.

- [ ] **Step 2: Run teardown tests and verify missing hooks**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptArrayBufferTests
```

Expected: FAIL because the testhost has no prepare hook or counters.

- [ ] **Step 3: Implement testhost early/late hooks**

Export:

```c
void expo_jsi_testhost_prepare_runtime_for_invalidation(
  expo_jsi_testhost_runtime_handle runtime);
```

The implementation calls `prepareRuntimeHandleForInvalidation` while the connector is valid. Existing `expo_jsi_testhost_invalidate_runtime` remains the late path and does not attempt JSI cleanup.

Also export test-only executor controls through the testhost ABI and both
managed fixture mirrors:

```c
void expo_jsi_testhost_pause_runtime_executor(
  expo_jsi_testhost_runtime_handle runtime);
void expo_jsi_testhost_resume_runtime_executor(
  expo_jsi_testhost_runtime_handle runtime);
void expo_jsi_testhost_drop_next_runtime_task(
  expo_jsi_testhost_runtime_handle runtime,
  int32_t priority);
expo_jsi_error expo_jsi_testhost_wait_until_runtime_task_queued(
  expo_jsi_testhost_runtime_handle runtime,
  int32_t priority);
expo_jsi_error expo_jsi_testhost_drop_queued_runtime_task(
  expo_jsi_testhost_runtime_handle runtime,
  int32_t priority);
void expo_jsi_testhost_release_bridge_runtime_handle(
  expo_jsi_testhost_runtime_handle runtime);
```

Mirror these as fixture methods named `PauseRuntimeExecutor`,
`ResumeRuntimeExecutor`, `DropNextRuntimeTask`, `WaitUntilRuntimeTaskQueued`,
`DropQueuedRuntimeTask`, and `ReleaseBridgeRuntimeHandle`. Use
`JavaScriptTaskPriority` at managed call sites and perform the checked enum
conversion in `NativeTestHost`.

Dropping a task destroys its copied callable without invoking it, which must
exercise `ScheduledReleaseToken` rather than a separate test-only state change.
`wait_until_runtime_task_queued` is a condition-variable barrier and must not
poll or sleep. `release_bridge_runtime_handle` calls `releaseRuntimeHandle`,
but first unregisters the original handle from `counterRuntimes`; only then does
it set the inner bridge handle to null. The ordinary final testhost release must
skip counter unregistration when that field is already null, preventing both a
dangling registry entry and double-unregistration. The helper deliberately
leaves the testhost/connector alive so held callable invocation and destruction
can be tested safely; final testhost release remains responsible for the
connector and outer allocation.

Also export a test-only `expo_jsi_testhost_validate_array_buffer_snapshot(detached, current, captured)` wrapper around the production validation helper. This provides deterministic resize-mismatch coverage because the pinned Hermes revision does not implement ResizableArrayBuffer.

- [ ] **Step 4: Reorder early-capable adapters without broad lifecycle changes**

Android, iOS, and macOS teardown use:

```cpp
if (runtimeHandle != nullptr && connector != nullptr && connector->isRuntimeValid()) {
  expo::dotnet::prepareRuntimeHandleForInvalidation(runtimeHandle);
}
connector->invalidate();
```

Then run existing managed teardown and runtime-handle release in their established order. Windows keeps connector invalidation first because its observed notification is late; runtime-handle release must call `invalidateWithoutRuntime` and never dispatch JSI work.

- [ ] **Step 5: Run lifecycle and platform compile gates**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptArrayBufferTests
scripts/test-managed.sh --filter FullyQualifiedName~RuntimeInvalidationTests
scripts/format.sh --check --all
git diff --check
```

Expected: both release/sweep orders and the active-runtime dropped-work case
release exactly once; early release and late abandonment counters are exact;
existing invalidation tests remain green.

Compile every edited platform adapter. On Android:

```bash
(cd apps/mobile-app/android && ./gradlew :app:assembleDebug)
```

Expected: `BUILD SUCCESSFUL`, including the native ExpoModulesDotnet sources.

On Apple, use the repo's required filtered `xcodebuild` output:

```bash
xcodebuild build \
  -workspace apps/mobile-app/ios/mobileapp.xcworkspace \
  -scheme mobileapp \
  -configuration Debug \
  -destination 'platform=iOS Simulator,id=19046C77-3797-4356-97D2-B372A3F01383' \
  CODE_SIGNING_ALLOWED=NO 2>&1 | xcsift -f toon
```

Expected: the filtered output reports a successful build including the iOS
installer. Compile the separate macOS adapter as well:

```bash
xcodebuild build \
  -workspace apps/desktop-app/macos/desktopapp.xcworkspace \
  -scheme desktopapp-macOS \
  -configuration Debug \
  -destination 'platform=macOS' 2>&1 | xcsift -f toon
```

Expected: the filtered output reports a successful macOS build including its
installer.

On a Windows machine or Windows CI:

```powershell
MSBuild.exe apps\desktop-app\windows\DesktopApp.sln /m /p:Configuration=Debug /p:Platform=x64
```

Expected: the solution builds with the Windows installer changes. Task 3 is
not complete until Android, Apple, and Windows compile evidence is recorded;
an unavailable local platform is surfaced and completed in its required CI or
remote environment rather than silently skipped.

- [ ] **Step 6: Commit lifecycle integration**

```bash
git add packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp \
  packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm \
  packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm \
  packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp \
  packages/expo-modules-dotnet/native/testhost \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures
git commit -m "fix(jsi): sweep long-lived state before runtime invalidation"
```

### Task 4: Add The Module-facing ArrayBuffer And Copy Codecs

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ArrayBuffer.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ArrayBufferByteAccess.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ArrayBufferCodec.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ByteArrayCodec.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ArrayBufferCodecTests.cs`

**Interfaces:**
- Consumes: Task 2 low-level wrappers and runtime execution APIs.
- Produces: `Expo.ModulesCore.ArrayBuffer`, `ArrayBufferCodec`, and `ByteArrayCodec` used by generated bindings.

- [ ] **Step 1: Write failing storage and codec tests**

Cover both storage branches, factories, copy isolation, native async inline execution, JS async scheduling, cross-runtime rejection, byte-array copies, and pass-through retention:

```csharp
[Fact]
public void ByteArrayReturnIsCopiedIntoIndependentStorage()
{
  var source = new byte[] { 1, 2, 3 };
  using var buffer = ArrayBuffer.CopyFrom(source);
  source[0] = 99;
  Assert.Equal(new byte[] { 1, 2, 3 }, buffer.ToArray());
}

[Fact]
public async Task NativeBackedAsyncAccessRunsInline()
{
  using var buffer = ArrayBuffer.Allocate(1);
  var thread = Environment.CurrentManagedThreadId;
  var callbackThread = await buffer.WithBytesAsync(bytes =>
  {
    bytes[0] = 5;
    return Environment.CurrentManagedThreadId;
  });
  Assert.Equal(thread, callbackThread);
}
```

- [ ] **Step 2: Run codec tests and verify missing API failures**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~ArrayBufferCodecTests
```

Expected: FAIL because `Expo.ModulesCore.ArrayBuffer` and its codecs do not exist.

- [ ] **Step 3: Implement the two-field backing representation**

Keep the implementation to one discriminated owner rather than introducing a storage hierarchy:

```csharp
public sealed class ArrayBuffer : IDisposable
{
  private JavaScriptArrayBuffer? javaScriptBacking;
  private JavaScriptMutableBuffer? nativeBacking;

  internal ArrayBuffer(JavaScriptArrayBuffer backing) => javaScriptBacking = backing;
  internal ArrayBuffer(JavaScriptMutableBuffer backing) => nativeBacking = backing;

  public int ByteLength => GetLiveBackingLength();
  public static ArrayBuffer Allocate(int byteLength) =>
    new(JavaScriptMutableBuffer.Allocate(byteLength));
  public static ArrayBuffer CopyFrom(ReadOnlySpan<byte> bytes) =>
    new(JavaScriptMutableBuffer.CopyFrom(bytes));
}
```

All methods switch on exactly one non-null backing. `Retain` duplicates that backing's lease. `Dispose` atomically clears and disposes one backing. No backing-kind property is public.

- [ ] **Step 4: Implement callback access and explicit copies**

Expose the complete mutable/read-only action and result surface. These custom
delegates keep both span types out of generic `Func<T>` storage:

```csharp
public delegate void ArrayBufferBytesAction(Span<byte> bytes);
public delegate TResult ArrayBufferBytesFunc<TResult>(Span<byte> bytes);
public delegate void ArrayBufferReadOnlyBytesAction(ReadOnlySpan<byte> bytes);
public delegate TResult ArrayBufferReadOnlyBytesFunc<TResult>(ReadOnlySpan<byte> bytes);

public void WithBytes(ArrayBufferBytesAction callback);
public T WithBytes<T>(ArrayBufferBytesFunc<T> callback);
public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction callback);
public T WithReadOnlyBytes<T>(ArrayBufferReadOnlyBytesFunc<T> callback);
public Task WithBytesAsync(
    ArrayBufferBytesAction callback,
    CancellationToken cancellationToken = default);
public Task<T> WithBytesAsync<T>(
    ArrayBufferBytesFunc<T> callback,
    CancellationToken cancellationToken = default);
public Task WithReadOnlyBytesAsync(
    ArrayBufferReadOnlyBytesAction callback,
    CancellationToken cancellationToken = default);
public Task<T> WithReadOnlyBytesAsync<T>(
    ArrayBufferReadOnlyBytesFunc<T> callback,
    CancellationToken cancellationToken = default);
```

For native backing, call directly but convert cancellation and callback failure
into Task terminal states. For JS backing, delegate scheduling and lease
cleanup to Task 2's low-level wrapper:

```csharp
public T WithBytes<T>(ArrayBufferBytesFunc<T> callback) =>
  GetJavaScriptBacking().WithBytes(bytes => callback(bytes));

public Task<T> WithBytesAsync<T>(
    ArrayBufferBytesFunc<T> callback,
    CancellationToken cancellationToken = default
)
{
  ArgumentNullException.ThrowIfNull(callback);
  var native = nativeBacking;
  if (native is not null)
  {
    return InvokeInlineAsync(
        () => native.WithBytes(bytes => callback(bytes)),
        cancellationToken
    );
  }

  // Resolve the live backing before observing cancellation. Disposed-state
  // misuse stays visible as ObjectDisposedException rather than a canceled Task.
  var javaScript = GetJavaScriptBacking();
  return javaScript.WithBytesAsync(
      bytes => callback(bytes),
      cancellationToken
  );
}

private static Task<T> InvokeInlineAsync<T>(
    Func<T> callback,
    CancellationToken cancellationToken)
{
  if (cancellationToken.IsCancellationRequested)
  {
    return Task.FromCanceled<T>(cancellationToken);
  }

  try
  {
    return Task.FromResult(callback());
  }
  catch (Exception exception)
  {
    return Task.FromException<T>(exception);
  }
}

private static Task InvokeInlineAsync(
    Action callback,
    CancellationToken cancellationToken)
{
  if (cancellationToken.IsCancellationRequested)
  {
    return Task.FromCanceled(cancellationToken);
  }

  try
  {
    callback();
    return Task.CompletedTask;
  }
  catch (Exception exception)
  {
    return Task.FromException(exception);
  }
}
```

Implement the action and read-only overloads through these helpers and the same
branch rule. Add tests proving a pre-canceled native call does not invoke the
callback and a throwing native callback returns a faulted Task without throwing
from the method call. Add a precedence test that disposes the wrapper, pre-cancels the
token, and asserts that the `WithBytesAsync` call itself throws
`ObjectDisposedException`. `Copy` and `ToArray` delegate through read-only
access. Their async forms schedule only for JavaScript backing.

```csharp
[Fact]
public void DisposedBufferValidationPrecedesAsyncCancellation()
{
  var buffer = ArrayBuffer.Allocate(1);
  buffer.Dispose();
  using var cancellation = new CancellationTokenSource();
  cancellation.Cancel();

  Assert.Throws<ObjectDisposedException>(() =>
    buffer.WithBytesAsync(bytes => bytes[0] = 1, cancellation.Token)
  );
}
```

- [ ] **Step 5: Implement codecs**

`ArrayBufferCodec.Decode` performs the accepted selection:

```csharp
var arrayBuffer = value.AsArrayBuffer();
if (arrayBuffer.TryGetMutableBuffer() is { } native)
{
  return new ArrayBuffer(native);
}
return new ArrayBuffer(arrayBuffer.Retain());
```

`Encode` borrows the module wrapper. JS backing returns the original object only to the same runtime; native backing creates a new object over retained storage. `ByteArrayCodec.Decode` calls `ToArray`; `Encode` performs one `CopyFrom` and disposes the temporary module wrapper after creating an owned JavaScript value.

- [ ] **Step 6: Run codec tests**

```bash
scripts/test-managed.sh --filter FullyQualifiedName~ArrayBufferCodecTests
scripts/format.sh --check --all
git diff --check
```

Expected: PASS; byte-array aliasing is absent and native-backed async access stays on the caller thread.

- [ ] **Step 7: Commit ModulesCore storage and codecs**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ArrayBufferCodecTests.cs
git commit -m "feat(modules-core): add ArrayBuffer storage and codecs"
```

### Task 5: Make Async Owned Results Claim Or Abandon Exactly Once

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromiseResult.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromiseScheduler.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**
- Consumes: existing Promise scheduler and owned `JavaScriptValue` convention.
- Produces: `JavaScriptPromiseResult.ResolveOwned<TState>` and scheduler abandonment cleanup used by generated ArrayBuffer returns.

- [ ] **Step 1: Write failing success and abandonment tests**

Add a disposable probe and verify both paths:

```csharp
private sealed class OwnedProbe : IDisposable
{
  public int DisposeCount { get; private set; }
  public void Dispose() => DisposeCount++;
}

[Fact]
public async Task OwnedResultIsAbandonedWhenRuntimeCannotClaimIt()
{
  using var fixture = HermesRuntimeFixture.Create();
  var probe = new OwnedProbe();
  var operationGate = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously);
  var disposed = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously);
  using var promise = fixture.Runtime.CreatePromise(async _ =>
  {
    await operationGate.Task.ConfigureAwait(false);
    return JavaScriptPromiseResult.ResolveOwned(
        probe,
        static (runtime, _) => runtime.CreateUndefined(),
        state =>
        {
          state.Dispose();
          disposed.TrySetResult();
        }
    );
  });
  fixture.InvalidateRuntime();
  operationGate.SetResult();
  await disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
  Assert.Equal(1, probe.DisposeCount);
}
```

Add a claimed test where the factory runs once and the abandon callback does not run. Update generator source assertions so `Task<JavaScriptValue>` uses `ResolveOwned`.

Also force the testhost to drop the Immediate-priority settlement callback. The
existing native scheduled-task release callback must fault `ScheduleAsync`, so
the scheduler's `finally` wins the owned result and abandons it once:

```csharp
[Fact]
public async Task OwnedResultIsAbandonedWhenSettlementTaskIsDropped()
{
  using var fixture = HermesRuntimeFixture.Create();
  var probe = new OwnedProbe();
  var disposed = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously);

  using var promise = fixture.Runtime.Execute(runtime =>
  {
    // Execute's own Immediate sync task is already running. Arm the drop here
    // so the next Immediate task is the settlement scheduled synchronously by
    // CreatePromise's already-completed operation.
    fixture.DropNextRuntimeTask(JavaScriptTaskPriority.Immediate);
    return runtime.CreatePromise(_ => Task.FromResult(
        JavaScriptPromiseResult.ResolveOwned(
            probe,
            static (js, _) => js.CreateUndefined(),
            state =>
            {
              state.Dispose();
              disposed.TrySetResult();
            }
        )
    ));
  });

  await disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
  Assert.Equal(1, probe.DisposeCount);
}
```

Do not add a timeout-based implementation workaround: this test verifies the
existing `ScheduledTaskContext` destruction ->
`ReleaseScheduledRuntimeTaskContext` -> faulted scheduling Task chain. Keep the
operation as `Task.FromResult`: the ordering depends on `SettleAsync` reaching
`ScheduleAsync` synchronously before the active Execute callback returns. If
that scheduler behavior changes, replace this assumption with a queue-identity
barrier rather than re-arming the drop outside the runtime frame.

- [ ] **Step 2: Run focused tests and verify missing ResolveOwned**

```bash
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptPromiseTests|FullyQualifiedName~GeneratorSupportsJavaScriptValueArgumentsAndReturns"
```

Expected: FAIL because the owned-state result API does not exist.

- [ ] **Step 3: Implement a copy-safe owned result state**

Keep the public result struct copyable by storing a shared reference state:

```csharp
public static JavaScriptPromiseResult ResolveOwned<TState>(
    TState state,
    Func<JavaScriptRuntime, TState, JavaScriptValue> createValue,
    Action<TState> abandon
) where TState : class
```

The private state uses `Interlocked.Exchange(ref state, null)` in both `CreateValue` and `Abandon`. `CreateValue` transfers the claimed state to the factory; after claim the factory owns transfer/disposal. `Abandon` invokes its callback only when it wins the pending state.

- [ ] **Step 4: Guarantee scheduler abandonment on every exit**

After the managed operation produces a result, wrap runtime scheduling:

```csharp
try
{
  await runtime.ScheduleAsync(
      js => SettlePromiseFromResultAndDispose(js, promise, result),
      JavaScriptTaskPriority.Immediate,
      CancellationToken.None
  ).ConfigureAwait(false);
}
finally
{
  result.Abandon();
}
```

The scheduled callback claims before encoding. Existing non-owned results have a no-op `Abandon`.

- [ ] **Step 5: Migrate generated Task<JavaScriptValue> returns**

Emit:

```csharp
return global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned(
    __expoResult,
    static (_, value) => value,
    static value => value.Dispose()
);
```

On success the scheduler owns and disposes the returned `JavaScriptValue`; before claim the abandonment callback owns disposal.

- [ ] **Step 6: Run Promise and generator tests**

```bash
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptPromiseTests|FullyQualifiedName~ExpoModulesGeneratorTests"
scripts/format.sh --check --all
git diff --check
```

Expected: PASS with one disposal on claimed and abandoned paths.

- [ ] **Step 7: Commit async result cleanup**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromiseResult.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromiseScheduler.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git commit -m "fix(jsi): release abandoned async results"
```

### Task 6: Generate ArrayBuffer And byte[] Bindings

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModules.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs`

**Interfaces:**
- Consumes: `ArrayBufferCodec`, `ByteArrayCodec`, `ResolveOwned`, and generated owned-argument disposal.
- Produces: sync/async generated parameters and returns for module-facing `ArrayBuffer` and `byte[]`.

- [ ] **Step 1: Write failing generator source-shape tests**

Use authored methods covering sync/async input, pass-through retention, `Task<ArrayBuffer>`, and byte arrays. Assert these exact ownership shapes:

```csharp
Assert.Contains("using var __expoArg0 = ArrayBufferCodec.Decode", source);
Assert.Contains("global::Expo.ModulesCore.ArrayBuffer? __expoArg0 = null;", source);
Assert.Contains("return global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned", source);
Assert.Contains("static value => value.Dispose()", source);
Assert.Contains("ByteArrayCodec.Decode", source);
```

- [ ] **Step 2: Write failing Hermes module tests**

Create a generated `Binary` module with methods:

```csharp
[JS] public ArrayBuffer Echo(ArrayBuffer value) => value.Retain();
[JS] public Task<ArrayBuffer> EchoAsync(ArrayBuffer value) => Task.FromResult(value.Retain());
[JS] public byte[] EchoBytes(byte[] value) => value;
[JS] public ArrayBuffer Allocate(int length) => ArrayBuffer.Allocate(length);
```

Test JS-backed `Echo(buffer) === buffer`, native-backed output mutation aliasing, async identity, byte-array copy isolation, TypedArray argument rejection, and zero-length behavior.

- [ ] **Step 3: Run generator and Hermes tests to verify unsupported-type failures**

```bash
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~Binary
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedBinaryModuleTests
```

Expected: FAIL with unsupported parameter/return diagnostics for ArrayBuffer and byte arrays.

- [ ] **Step 4: Generalize owned decoded locals without weakening ownership**

Keep `OwnsDecodedValue`, but emit the parameter's actual type instead of hard-coding `JavaScriptValue`:

```csharp
builder.AppendLine($"    {parameter.TypeName}? {GetParameterLocalName(index)} = null;");
```

Mark `ArrayBufferCodec` as owned just like `JavaScriptValueCodec`. Sync uses `using var`; async disposes in the existing completion/failure finally paths.

- [ ] **Step 5: Map supported codecs and owned return handling**

Add metadata-name checks before nullable/record logic:

```csharp
if (typeName == "global::Expo.ModulesCore.ArrayBuffer") return "ArrayBufferCodec";
if (typeSymbol is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
  return "ByteArrayCodec";
```

For sync `ArrayBuffer` returns, emit an owned local, encode inside `try`, and dispose in `finally`. For async returns, emit `ResolveOwned` with a factory that calls `ArrayBufferCodec.Encode` and disposes the wrapper in `finally`; the abandon callback disposes when settlement never claims it.

- [ ] **Step 6: Run generated binary tests**

```bash
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~Binary
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedBinaryModuleTests
scripts/format.sh --check --all
git diff --check
```

Expected: PASS with identity for JS backing and copies for byte arrays.

- [ ] **Step 7: Commit ArrayBuffer and byte-array generation**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModules.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs
git commit -m "feat(generator): support ArrayBuffer and byte arrays"
```

### Task 7: Generate One Scoped Span Parameter And Copied Span Returns

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs`

**Interfaces:**
- Consumes: module-facing callback byte access and `ArrayBufferCodec.EncodeCopy(ReadOnlySpan<byte>, runtime)`.
- Produces: one generator-specialized mutable/read-only span argument, copied
  span returns, diagnostic `EXPOJSI012` for async span parameters, and
  diagnostic `EXPOJSI013` for multiple span parameters.

- [ ] **Step 1: Add failing generator tests for span shapes**

Cover one mutable span, one read-only span, mixed ordinary/span arguments,
mutable/read-only returns, an async span parameter, and methods containing two
mutable/read-only span parameters in every combination. Require `EXPOJSI012`
with the method and parameter names for the async case. Require `EXPOJSI013`
with the method name and both span parameter names for every multiple-span
case, while methods with multiple `ArrayBuffer` or `byte[]` parameters remain
valid.

Assert the supported single-span shape and the absence of nested span
callbacks:

```csharp
Assert.Contains("__expoSpanBuffer0.WithBytes(__expoArg0 =>", source);
Assert.Contains("module.Fill(__expoArg0)", source);
Assert.DoesNotContain("__expoSpanBuffer1", source);
Assert.DoesNotContain("IJavaScriptCodec<global::System.Span<byte>>", source);
```

- [ ] **Step 2: Add failing Hermes span tests**

Add authored methods backed by module-owned arrays:

```csharp
[JS] public void Fill(Span<byte> bytes) => bytes.Fill(9);
[JS] public int Sum(ReadOnlySpan<byte> bytes) => bytes.ToArray().Sum(value => value);
[JS] public ReadOnlySpan<byte> ReturnView() => returnedBytes;
```

Verify mutable input changes the source JavaScript ArrayBuffer, read-only input reads without mutation, and two calls returning the same module field produce independent JavaScript ArrayBuffers.

- [ ] **Step 3: Run tests and verify span diagnostics/unsupported returns**

```bash
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~Span
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedBinaryModuleTests
```

Expected: FAIL because spans have no generator-specialized path.

- [ ] **Step 4: Add explicit parameter and return passing kinds**

Add model enums rather than encoding span behavior in codec-name strings:

```csharp
internal enum ExpoParameterPassingKind { Codec, MutableByteSpan, ReadOnlyByteSpan }
internal enum ExpoReturnPassingKind { Codec, MutableByteSpan, ReadOnlyByteSpan }
```

Assign span kinds from fully qualified metadata names. Before ordinary codec
resolution, report `EXPOJSI012` when `IsAsync` and any parameter is a mutable or
read-only span. For synchronous methods, report `EXPOJSI013` when more than one
parameter has either span kind. Do not count `ArrayBuffer`, `byte[]`, or
ordinary parameters toward this limit.

Add the exact diagnostic descriptors:

```csharp
public static readonly DiagnosticDescriptor AsyncSpanParameter = new(
    id: "EXPOJSI012",
    title: "Async Expo module methods cannot borrow spans",
    messageFormat: "Method '{0}' parameter '{1}' uses '{2}', which is supported only by synchronous Expo module methods",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);

public static readonly DiagnosticDescriptor MultipleSpanParameters = new(
    id: "EXPOJSI013",
    title: "Expo module method has multiple span parameters",
    messageFormat: "Method '{0}' declares multiple span parameters ({1}); at most one Span<byte> or ReadOnlySpan<byte> parameter is supported",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);
```

Put this internal developer note beside the arity check in
`ExpoModulesGenerator.cs`:

```csharp
// Multiple Span<byte>/ReadOnlySpan<byte> parameters need a grouped access
// primitive. Nesting the current callbacks would make the inner lambda capture
// the outer ref-struct parameter, which C# rejects with CS9108. Keep this
// diagnostic until one callback can receive all requested spans together.
```

- [ ] **Step 5: Emit the single scoped callback**

Decode the single span's JavaScript argument into an owned `ArrayBuffer` local,
then invoke and encode the authored method inside that storage callback:

```csharp
using var __expoSpanBuffer0 = ArrayBufferCodec.Decode(arguments.GetValue(0), runtime);
return __expoSpanBuffer0.WithReadOnlyBytes(__expoArg0 =>
  NumberCodec<int>.Encode(module.Sum(__expoArg0), runtime));
```

Ordinary owned arguments retain their existing `using`/finally behavior outside
the span callback. The generator must issue `EXPOJSI013` before emission, so no
recursive callback emitter or captured ref-struct local exists.

- [ ] **Step 6: Copy span returns immediately**

Add this codec helper:

```csharp
internal static JavaScriptValue EncodeCopy(
    ReadOnlySpan<byte> bytes,
    JavaScriptRuntime runtime
)
{
  using var buffer = ArrayBuffer.CopyFrom(bytes);
  return Encode(buffer, runtime);
}
```

Emit `ArrayBufferCodec.EncodeCopy(module.ReturnView(), runtime)` for both mutable and read-only synchronous span returns. No span return is captured in an async result.

- [ ] **Step 7: Run span tests**

```bash
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~Span
scripts/test-managed.sh --filter FullyQualifiedName~GeneratedBinaryModuleTests
scripts/format.sh --check --all
git diff --check
```

Expected: PASS; supported source contains one scoped callback, async spans
produce `EXPOJSI012`, multiple spans produce `EXPOJSI013`, and neither
diagnostic restricts `ArrayBuffer` or `byte[]` arity.

- [ ] **Step 8: Commit span generation**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated
git commit -m "feat(generator): add scoped byte span bindings"
```

### Task 8: Complete Edge-case And Full-suite Verification

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ArrayBufferCodecTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs`

**Interfaces:**
- Consumes: complete low-level and generated binary surface.
- Produces: regression coverage for all accepted ownership and failure contracts.

- [ ] **Step 1: Add the final ownership matrix tests**

Add tests for:

```text
JS-backed + same runtime     => strict identity preserved
JS-backed + another runtime  => explicit failure, no copy
Native-backed + same runtime => distinct objects, shared mutations
Native-backed + other runtime=> succeeds, shared mutations
Detached before decode       => decode failure
Detached after retain        => next access/encode failure
Captured/current size differ => validation failure through testhost helper
ByteLength outside JS frame  => captured logical length, no runtime access
Zero length + null pointer   => successful empty span
Disposed wrapper             => managed failure before native access
Copy/CopyAsync               => independent native-backed bytes
ToArray/ToArrayAsync         => independent managed bytes
Release queued, sweep wins   => one runtime-thread release
Release runs, sweep follows  => one runtime-thread release
Queued release is dropped    => next active runtime access drains it once
Queued token outlives handle => invoke/drop observes safe invalid tombstone
Late invalidation            => one quarantined abandonment, no JSI destructor
Async callback failure       => scheduling lease releases once
Async pre-run cancellation   => scheduling lease releases once, no callback
Async scheduled-task drop    => scheduling lease releases once, no callback
Async pending at teardown    => one release/abandon according to teardown phase
Native async pre-canceled    => canceled Task, callback not invoked
Native async callback throws => faulted Task, no synchronous throw
Disposed + pre-canceled      => synchronous ObjectDisposedException
```

For cross-runtime behavior, keep two `HermesRuntimeFixture` instances alive, decode in runtime A, and call the codec with runtime B. Verify failure text identifies runtime affinity without exposing pointer values.

- [ ] **Step 2: Run all focused binary tests**

```bash
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptArrayBufferTests|FullyQualifiedName~ArrayBufferCodecTests|FullyQualifiedName~GeneratedBinaryModuleTests"
```

Expected: PASS with no skipped tests.

- [ ] **Step 3: Run the canonical managed and formatting gates**

```bash
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
```

Expected: managed suite and format PASS; `git diff --check` is silent; the reflection scan has no new generated-binding hot-path matches.

- [ ] **Step 4: Commit final regression coverage**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayBufferTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ArrayBufferCodecTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs
git commit -m "test(arraybuffer): cover storage and lifetime matrix"
```

### Task 9: Merge The Delta Into Living Specs And Record Promise Follow-up

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/ownership-and-scoped-refs.md`
- Modify: `docs/specs/runtime-scheduling.md`
- Modify: `docs/specs/promises.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/hermes-testhost.md`
- Modify: `docs/roadmap.md`
- Modify: `docs/plans/README.md`
- Move: `docs/plans/006-arraybuffer-codec-spike.md` to `docs/archive/agent-plan/006-arraybuffer-codec-spike.md`
- Move after verification: `docs/changes/2026-07-10-arraybuffer-codecs/` to `docs/archive/changes/2026-07-10-arraybuffer-codecs/`

**Interfaces:**
- Consumes: verified implementation and accepted delta requirements.
- Produces: authoritative current-state specs, durable Promise migration follow-up, and archived transient artifacts.

- [ ] **Step 1: Merge each accepted delta into its authoritative spec**

Apply the requirements by responsibility:

```text
runtime-and-abi.md            opaque handles, ABI v22, runtime collection, teardown phases
managed-jsi-wrappers.md       low-level ArrayBuffer/MutableBuffer wrappers
ownership-and-scoped-refs.md  retain/dispose/transfer and scoped span lifetime
runtime-scheduling.md         JS-backed async access and early/late invalidation
promises.md                   owned result claim/abandon cleanup only
modules-core-boundary.md      module ArrayBuffer and binary codec matrix,
                              including one-span arity and JS/native identity
hermes-testhost.md            deterministic executor controls, snapshot hook,
                              and consumer-specific lifetime counters
```

Remove `ArrayBuffer Is Not Yet Wrapped`; do not leave contradictory future
wording. In `modules-core-boundary.md`, keep the observable distinction
explicit: returning JS-backed storage to its originating runtime preserves
strict identity, while returning native-backed storage creates a distinct
JavaScript object sharing the same bytes. Also document that the one-span
parameter limit does not apply to `ArrayBuffer` or `byte[]`.

- [ ] **Step 2: Update roadmap and plan index**

Mark ArrayBuffer/binary data complete in the richer-runtime and codec sections. Add this durable architecture follow-up:

```markdown
- **P2 — Unify Promise long-lived JSI state:** ArrayBuffer introduced the
  runtime-owned long-lived-state collection. Migrate retained Promise
  capability state onto it without changing settlement scheduling. Cover
  unresolved-promise teardown, settlement/teardown races, and idempotent late
  disposal.
```

In `docs/plans/README.md`, mark plan 006 as DONE through the production change and state that the focused Promise migration is a follow-up, not part of 006.

- [ ] **Step 3: Archive obsolete and completed planning artifacts**

```bash
git mv docs/plans/006-arraybuffer-codec-spike.md docs/archive/agent-plan/006-arraybuffer-codec-spike.md
git mv docs/changes/2026-07-10-arraybuffer-codecs docs/archive/changes/2026-07-10-arraybuffer-codecs
```

The archived spec and plan remain provenance only; living specs become authoritative.

- [ ] **Step 4: Run final documentation and implementation gates**

```bash
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: code gates PASS; diff check is silent; forbidden legacy phrases have no matches.

- [ ] **Step 5: Commit living-spec closeout**

```bash
git add docs/specs docs/roadmap.md docs/plans/README.md docs/archive docs/changes docs/plans
! git diff --cached --text | rg -F "$HOME"
! git diff --cached --text | rg -F "$(id -un)"
! git diff --cached --text | rg -F "$(hostname)"
git commit -m "docs(specs): merge ArrayBuffer and binary codec contract"
```

## Final Acceptance Checklist

- [ ] ABI table is version 22 and native/managed layouts match exactly.
- [ ] Successful ArrayBuffer/MutableBuffer handle results carry checked logical
  byte lengths; owned `ByteLength` getters never enter JSI.
- [ ] JS-backed buffers preserve same-runtime identity and reject cross-runtime encoding.
- [ ] Native-backed buffers create distinct JavaScript objects that alias the same bytes.
- [ ] Managed arrays and span returns copy; no arbitrary managed memory is pinned.
- [ ] Generated methods accept at most one synchronous scoped span parameter;
  async spans produce `EXPOJSI012` and multiple spans produce `EXPOJSI013`
  without restricting `ArrayBuffer` or `byte[]` arity.
- [ ] JS-backed access validates runtime, detachment, and unchanged byte length every time.
- [ ] Runtime release and teardown races are forced in both orders and converge
  to exactly one runtime-thread release.
- [ ] Dropped scheduled release work is drained by the next valid runtime access.
- [ ] A queued release token safely survives runtime-handle release; later
  invocation and dropped-callable destruction are tested separately.
- [ ] Late invalidation detaches the JSI payload into no-destructor quarantine,
  records one abandonment, and makes later disposal a no-op.
- [ ] Low-level async byte access releases its scheduling lease exactly once on
  callback failure, pre-run cancellation, dropped work, and both teardown paths.
- [ ] Native-backed async byte access validates disposal first; live wrappers
  execute inline and return canceled or callback-faulted Tasks without
  synchronous callback exceptions.
- [ ] Owned async results are disposed on claim, codec failure, scheduling
  failure, cancellation, dropped settlement work, and teardown.
- [ ] Promise capability migration is documented but not implemented in this change.
- [ ] Android, Apple, and Windows adapter compile gates have recorded evidence.
- [ ] Full managed suite, formatting, diff checks, and hot-path reflection scan pass.
- [ ] Accepted requirements are merged into all seven living specs, including
  `hermes-testhost.md`, and transient artifacts are archived.
