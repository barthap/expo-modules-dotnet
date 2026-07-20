# SharedObject Weak Identity Spike Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove an opaque weak-object bridge and one per-runtime internal registry that preserves same-runtime C#↔JavaScript identity and reaches one safe, exactly-once terminal release.

**Architecture:** C++ owns the JSI weak-reference payload behind an appended v23 ABI tail and uses the existing runtime-owned long-lived collection for release versus abandonment. `Expo.JSI` exposes only owned opaque weak handles. `Expo.ModulesCore` owns identity/release policy in an internal registry; its NativeState callback may terminally release the opaque weak handle, but may not enter JSI or use another wrapper.

**Tech Stack:** C++20, Hermes JSI, C ABI opaque handles, .NET NativeAOT-compatible function pointers, `Expo.JSI`, `Expo.ModulesCore`, xUnit, Hermes native testhost.

**Planned at:** `7d40f467`.

**Execution workspace:** current branch only. Do not create or use a git worktree.

---

## Scope, invariants, and execution precondition

This spike includes only the production weak-object ABI, public `JavaScriptWeakObject`, deterministic collection control, and internal identity/release proof. It excludes a public `SharedObject` base, `ExpoSharedObjectAttribute`, generator support, `SharedRef<T>`, TypeScript APIs, events, cross-runtime sharing, and a `JavaScriptObject` codec. `JavaScriptValue` already has its optional advanced codec; a `JavaScriptObject` codec remains a separate optional module-convertibles slice.

```text
Host owns JsiRuntimeConnector.
RuntimeHandle owns shared RuntimeState.
RuntimeState borrows the connector only while Active or Closing and owns LongLivedObjectCollection.
Collection entries retain RuntimeState until release or abandonment erases the entry.
Weak bridge handles own a collection lease, never a JavaScript object.
SharedObjectRegistry owns maps, entries, and opaque weak wrappers for one DotnetRuntimeContext; each JavaScript instance owns its installed prototype and release function through JavaScript references.
```

Every C# wrapper owns a bridge handle, not a JavaScript object. Scoped refs are access-frame-only. No release action receives a wrapper, scoped ref, token, or runtime. It must not access JSI or block.

- [ ] Review and commit this approved plan before source work:

  ```sh
  git add docs/changes/2026-07-19-sharedobject/plan.md
  git diff --cached --check
  git commit -m "docs: add sharedobject spike plan"
  ```

  Expected: one plan-only commit on the current branch after the privacy gate below passes.

## Drift check, source map, and privacy gate

Run this exact baseline check before implementation. It covers every path that this plan may create, modify, move, or use for verification.

```sh
git diff --stat 7d40f467..HEAD -- \
  packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h \
  packages/expo-modules-dotnet/native/packages/jsi/src \
  packages/expo-modules-dotnet/native/testhost \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests \
  docs/specs docs/plans/README.md \
  docs/changes/2026-07-19-sharedobject \
  docs/archive/changes/2026-07-19-sharedobject
```

Expected: no drift beyond this approved plan commit. If any listed contract changed, compare it to each task before editing. STOP if it changes ABI-tail placement, callback rules, lock order, ownership, or context disposal; reconcile the spec/plan before source work.

| Path | Responsibility |
| --- | --- |
| `native/include/expo_jsi.h` | v23 weak opaque handle, structured results, tail pointers. |
| `native/packages/jsi/{include/ExpoJsiBridge.h,src/ExpoJsiBridge.cpp}` | ABI functions and testhost bridge helpers. |
| `native/packages/jsi/src/{WeakObjectCapabilities.h,WeakObjectHandles.h}` | Type-erased JSI capability payload and long-lived weak entry. |
| `native/packages/jsi/src/{RuntimeState.h,RuntimeState.cpp,LongLivedObjectCollection.h,LongLivedObjectCollection.cpp}` | Weak counters and remaining-entry instrumentation. |
| `native/testhost/{CMakeLists.txt,include/expo_jsi_testhost.h,src/ExpoJsiTestHost.cpp,src/WeakObjectCapabilitiesLegacyCompileTest.cpp}` | GC export, counter layout, and actual legacy fallback compile. |
| `managed/packages/Expo.JSI/{JavaScriptObject.cs,JavaScriptWeakObject.cs,Internal/JavaScriptObjectInner.cs,Interop/ExpoJsiApi.cs,Interop/ExpoJsiHandles.cs,Interop/ExpoJsiTypes.cs}` | Public wrapper, gate, and ABI interop. |
| `managed/packages/Expo.JSI.Tests/{Fixtures/NativeTestHost.cs,Fixtures/HermesRuntimeFixture.cs,Interop/ExpoJsiApiTests.cs,Runtime/HermesGarbageCollectionTests.cs,Runtime/JavaScriptWeakObjectTests.cs}` | Low-level GC, ABI, affinity, concurrency, and native-lifetime proof. |
| `managed/packages/Expo.ModulesCore/{Expo.ModulesCore.csproj,DotnetRuntimeContext.cs,SharedObjectRegistry.cs,SharedObjectPrototype.cs}` | Test assembly visibility, resource-free registry, and per-pair prototype installation. |
| `managed/packages/Expo.ModulesCore.Tests/{Fixtures/NativeTestHost.cs,Fixtures/HermesRuntimeFixture.cs,SharedObjectRegistryTests.cs}` | By-value counter ABI and identity/release proof. |
| `docs/specs/{runtime-and-abi.md,managed-jsi-wrappers.md,ownership-and-scoped-refs.md,hermes-testhost.md,modules-core-boundary.md}` | GO-only current-state documentation. |
| `docs/changes/2026-07-19-sharedobject/` and `docs/archive/changes/2026-07-19-sharedobject/` | Result evidence and GO-only archive. |
| `docs/plans/README.md` | GO→DONE or NO-GO→BLOCKED/REJECTED status. |

Before every commit, stage only that task and run this generic, self-nonmatching privacy gate. It must print nothing and return zero. It deliberately detects user-specific home paths without encoding a username.

```sh
git diff --cached --check
if git diff --cached | rg -n '/(U)sers/[^/[:space:]]+|/(p)rivate/(var|tmp)/|[[:alnum:]-]+[.][l]ocal|[[:alnum:]-]+[.]internal'; then
  exit 1
fi
```

## Task 1: Add deterministic Hermes collection control

**Files:**

- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/HermesGarbageCollectionTests.cs`

**Produces:** testhost-only `expo_jsi_testhost_collect_garbage` and `CollectGarbageForTesting()` in both fixture families. It is not an `expo_jsi_api` member.

- [ ] **Step 1: Write RED test.**

  ```csharp
  [Fact]
  public void CollectGarbageForTestingRunsOnTheHermesRuntimeExecutor()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.CollectGarbageForTesting();
    using var result = fixture.Evaluate("40 + 2", "collect-garbage-afterward.js");
    Assert.Equal(42, result.AsDouble());
  }
  ```

- [ ] **Step 2: Verify RED.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~HermesGarbageCollectionTests
  ```

  Expected: compile failure for the missing fixture method/export. No timer, retry loop, managed GC, or JavaScript fallback is permitted.

- [ ] **Step 3: Implement the native and managed hook.**

  Declare an `expo_jsi_error` export. In the native implementation, run exactly this on the connector executor and convert all validation/JSI exceptions to existing structured errors:

  ```cpp
  testhost->connector.runtimeExecutor().executeSync([](jsi::Runtime &runtime) {
    runtime.instrumentation().collectGarbage("expo-jsi-testhost");
  });
  ```

  Each managed `NativeTestHost` loads `delegate* unmanaged[Cdecl]<nint, ExpoJsiError>`, throws through existing `ThrowNativeError` on nonzero code, and exposes `CollectGarbageForTesting(nint)`. Each fixture forwards:

  ```csharp
  public void CollectGarbageForTesting() =>
      NativeTestHost.CollectGarbageForTesting(testHostRuntime);
  ```

- [ ] **Step 4: Verify GREEN and commit.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~HermesGarbageCollectionTests
  ```

  Expected: test passes and subsequent runtime evaluation works. Run the privacy gate, then:

  ```sh
  git add \
    packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h \
    packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/HermesGarbageCollectionTests.cs
  git commit -m "test(testhost): add deterministic Hermes collection control"
  ```

  STOP/NO-GO if the selected Hermes headers do not expose `Runtime::instrumentation().collectGarbage(std::string)` or executor access cannot invoke it. Record compiler/API evidence; do not replace this proof with timing.

## Task 2: Implement v23 opaque weak handles and a gated `Expo.JSI` wrapper

**Files:**

- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectCapabilities.h`
- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectHandles.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/{RuntimeState.h,RuntimeState.cpp,LongLivedObjectCollection.h,LongLivedObjectCollection.cpp}`
- Modify: `packages/expo-modules-dotnet/native/testhost/{CMakeLists.txt,include/expo_jsi_testhost.h,src/ExpoJsiTestHost.cpp}`
- Create: `packages/expo-modules-dotnet/native/testhost/src/WeakObjectCapabilitiesLegacyCompileTest.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/{JavaScriptObject.cs,JavaScriptWeakObject.cs,Internal/JavaScriptObjectInner.cs,Interop/ExpoJsiApi.cs,Interop/ExpoJsiHandles.cs,Interop/ExpoJsiTypes.cs}`
- Modify: both `managed/packages/{Expo.JSI.Tests,Expo.ModulesCore.Tests}/Fixtures/{NativeTestHost.cs,HermesRuntimeFixture.cs}`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Interop/ExpoJsiApiTests.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptWeakObjectTests.cs`

**Produces:** `expo_jsi_weak_object_handle`, structured create/lock results, `create_weak_object`, `weak_object_lock`, `weak_object_release` tail pointers, `JavaScriptObject.CreateWeak()`, and `JavaScriptWeakObject.Lock()`.

- [ ] **Step 1: Write the complete RED tests.**

  Add these tests, keeping every create/lock call inside `Runtime.Execute` unless the test asserts access-frame failure:

  ```csharp
  [Fact] public void CreateWeakAndLockRequireTheOriginatingAccessFrame();
  [Fact] public void CreateWeakOnDisposedObjectThrowsBeforeTheAbi();
  [Fact] public void LockRejectsAWrongRuntime();
  [Fact] public void CreateWeakAndLockRejectAnInvalidatedRuntime();
  [Fact] public void LockReturnsIndependentOwnedObjectsForALiveReferent();
  [Fact] public void LockReturnsNullAfterDeterministicCollection();
  [Fact] public void DisposeIsIdempotentAndLockAfterDisposeThrows();
  [Fact] public void LockAndDisposeAreSerialized();
  [Fact] public void QueuedWeakReleaseAbandonsAndErasesTheEntry();
  [Fact] public void EarlyPreparationReleasesAndErasesTheEntry();
  ```

  `CreateWeakAndLockRequireTheOriginatingAccessFrame` creates and retains both a strong object and weak wrapper inside one `Execute`, then independently calls `strongObject.CreateWeak()` and `weak.Lock()` outside access and asserts the existing scope error before native access. Dispose both afterward. `CreateWeakOnDisposedObject` disposes its object first and asserts `ObjectDisposedException`.

  Add internal low-level fixture helpers, modelled on `SetObjectPropertyRaw`, named `CreateWeakObjectRaw(ExpoJsiValueHandle)` and `LockWeakObjectRaw(ExpoJsiWeakObjectHandle)`. Add an internal weak-handle accessor for tests that validates wrapper disposal. `LockRejectsAWrongRuntime` passes A's weak handle to B's raw helper and asserts `Ok == 0` plus released structured error text. `CreateWeakAndLockRejectAnInvalidatedRuntime` creates the weak handle before invalidation, calls the existing `InvalidateRuntime()`/`InvalidateRuntimeForTesting()` control that invalidates the connector **without freeing `RuntimeHandle`**, then calls raw create with `default(ExpoJsiValueHandle)` (no `ValueHandle` survives invalidation) and raw lock with the pre-invalidation weak handle. Assert structured invalid-runtime errors with no result, then safely dispose the opaque weak wrapper. It must not call ABI through `ReleaseBridgeRuntimeHandle()`, which frees the raw pointer. The public wrapper calls remain blocked by access-frame validation before any native call.

  For independent locks, use named ownership:

  ```csharp
  using var first = Assert.IsType<JavaScriptObject>(weak.Lock());
  using var second = Assert.IsType<JavaScriptObject>(weak.Lock());
  using var firstValue = first.AsValue();
  using var secondValue = second.AsValue();
  Assert.True(runtime.StrictEquals(firstValue, secondValue));
  ```

  For collection, clear all JS/global references, dispose every strong managed wrapper, call `CollectGarbageForTesting`, then `WaitUntilIdle()` before entering `Runtime.Execute` to assert `weak.Lock()` is null. No elapsed-time predicate is allowed.

  The deterministic Dispose-wins test pauses the executor, starts `Task.Run(() => fixture.Runtime.Execute(_ => weak.Lock()))`, waits for its immediate runtime task with `WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate)`, calls `weak.Dispose()` while the lock task is still queued, then resumes and asserts the queued Lock throws `ObjectDisposedException` before calling the ABI. Separately, `LockAndDisposeAreSerialized` may use `Barrier(3)` as a simultaneous stress test and accepts either legal result, but does not claim the Barrier ordering is deterministic.

  The two long-lived tests pause normal release work, resume it after the invalidation/preparation transition, call `WaitUntilIdle()`, then assert counters and `LongLivedObjectsRemaining == 0` after either bridge-handle invalidation (abandon) or prepared invalidation (release). In the early preparation case use the established non-deadlocking order:

  ```csharp
  fixture.PauseRuntimeExecutor();
  var preparation = Task.Run(fixture.PrepareRuntimeForInvalidation,
      TestContext.Current.CancellationToken);
  fixture.WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate);
  fixture.ResumeRuntimeExecutor();
  await preparation;
  ```

  Update `ExpoJsiApiTests` for version 23, `sizeof(ExpoJsiApi)`, a truncated weak tail, and native/managed version mismatch text.

- [ ] **Step 2: Verify RED.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptWeakObjectTests
  scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests
  ```

  Expected: compile failure for new wrapper/interops, fixture counter tail, and v23 API.

- [ ] **Step 3: Add ABI v23, type-erased native capability, and entry accounting.**

  In `expo_jsi.h`, forward-declare `WeakObjectHandle`, add its opaque handle, then append these result types and all three function-pointer members at the end of the existing ABI table:

  ```c
  typedef struct expo_jsi_weak_object_result {
    int32_t ok;
    expo_jsi_weak_object_handle weak_object;
    expo_jsi_error error;
  } expo_jsi_weak_object_result;

  typedef struct expo_jsi_weak_object_lock_result {
    int32_t ok;
    int32_t found;
    expo_jsi_value_handle value;
    expo_jsi_error error;
  } expo_jsi_weak_object_lock_result;
  ```

  Bump `kApiVersion` 22→23. An unavailable referent is `{1, 0, nullptr, makeOk()}`; only an error has `ok == 0`. Lock returns a fresh owned `ValueHandle` for a live object.

  `WeakObjectHandles.h` must never name or store `jsi::WeakObject`. It stores only `std::unique_ptr<WeakObjectPayload>` supplied by the capability layer:

  ```cpp
  class WeakObjectEntry final : public LongLivedObject {
    std::shared_ptr<RuntimeState> state_;
    std::unique_ptr<WeakObjectPayload> payload_;
    std::atomic<uint32_t> leases_{1};
    std::atomic<bool> terminal_{false};
  public:
    std::optional<jsi::Object> lock(jsi::Runtime &runtime);
    void release(jsi::Runtime &runtime) noexcept override;
    void abandon() noexcept override;
  };
  ```

  `WeakObjectCapabilities.h` defaults `EXPO_JSI_HAS_WEAK_OBJECT` to `1` when the build does not define it, forward-declares `WeakObjectPayload` to `WeakObjectHandles`, and exposes `createWeakObjectPayload`, `lockWeakObjectPayload`, `releaseWeakObjectPayloadOnRuntime`, and `abandonWeakObjectPayload`. Its `EXPO_JSI_HAS_WEAK_OBJECT == 1` branch is the only capability implementation that names `jsi::WeakObject`, storing it in a private payload implementation. Its `== 0` legacy branch names no unavailable JSI type and returns a structured "WeakObject is unsupported by this JSI capability" error. `WeakObjectHandles` calls only the erased API in both branches.

  Add an actual `expo_jsi_weak_object_capabilities_legacy_compile_test` target. Compile `WeakObjectHandles.h`'s actual create/lock call site through `WeakObjectCapabilitiesLegacyCompileTest.cpp` against the legacy fixture with `EXPO_JSI_HAS_WEAK_OBJECT=0`; extend the fixture only with the opaque `Runtime`, `Object`, and `Value` declarations needed by the erased signatures. The target must prove unsupported creation compiles and returns the structured capability error without a `jsi::WeakObject` name in the legacy branch.

  Match `ArrayBufferEntry` lease logic exactly. Final lease calls `RuntimeState::releaseLongLivedObject(id)`. Active release destroys payload on the runtime executor and calls `noteWeakObjectReleased`; late abandonment releases its payload storage without running a JSI destructor and calls `noteWeakObjectAbandoned`. Both collection transitions erase the entry. Add `LongLivedObjectCollection::size() const` under its mutex, `RuntimeState::longLivedObjectCount()`, and bridge helpers to retrieve/reset weak counters plus remaining count.

  Extend `expo_jsi_testhost_counters` at its tail with:

  ```c
  uint32_t long_lived_weak_objects_released;
  uint32_t long_lived_weak_objects_abandoned;
  uint32_t long_lived_objects_remaining;
  ```

  Populate all three during normal reads and bridge-handle release. Append corresponding fields, in the same order, to **both** managed by-value `Counters` structs and pass them through both fixture helpers. This layout change is atomic: header, native return construction, both C# structs, and test assertions land together.

- [ ] **Step 4: Implement the managed gate and interop.**

  Append weak aliases/result structs/function pointers/call-throughs/`Validate()` checks in the same order as native. `ExpectedVersion` becomes 23 and `ExpectedSize` remains `sizeof(ExpoJsiApi)`.

  `JavaScriptObject.CreateWeak()` delegates through `JavaScriptObjectInner.CreateWeak()` after object disposal and access-frame validation. Its XML documentation must say it requires the originating runtime's access frame, returns an opaque weak wrapper owned by the caller, and that the caller must dispose it. `JavaScriptWeakObject.Lock()` documentation must say it requires the originating runtime's access frame, returns either a fresh independently owned object wrapper or `null`, and that each returned wrapper must be disposed. `JavaScriptWeakObject.Dispose()` documentation must say it is idempotent, releases only the opaque handle, requires neither an access frame nor JSI access, and makes later `Lock()` calls throw `ObjectDisposedException`.

  Use this exact locking algorithm in `JavaScriptWeakObject`:

  ```csharp
  private readonly object gate = new();
  private ExpoJsiWeakObjectHandle handle;

  public JavaScriptObject? Lock()
  {
    lock (gate)
    {
      ObjectDisposedException.ThrowIf(handle == 0, this);
      JavaScriptHandleScope.CurrentFor(context);
      var result = context.Api->LockWeakObject(context.RuntimeHandle, handle);
      if (!result.IsOk) JsiContext.ThrowNativeError(result.Error, "Failed to lock JavaScript weak object.");
      return result.HasValue ? new JavaScriptObject(context, result.Value) : null;
    }
  }

  public void Dispose()
  {
    ExpoJsiWeakObjectHandle detached;
    lock (gate)
    {
      detached = handle;
      handle = IntPtr.Zero;
    }
    if (detached != 0) context.Api->ReleaseWeakObject(detached);
  }
  ```

  `Lock` holds the gate through handle validation, access-frame validation, native call, error translation, and owned-wrapper construction. `Dispose` detaches under the gate and releases outside it; it is nonblocking, does not require an access frame, never enters JSI, and never schedules synchronous runtime work. Document that NativeState callbacks may call only this `Dispose`; they must never call `CreateWeak`, `Lock`, `AsValue`, `Retain`, any other wrapper method, or access a scoped ref.

- [ ] **Step 5: Verify GREEN and commit.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptWeakObjectTests
  scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests
  ```

  Expected: all affinity, disposal/concurrency, deterministic collection, counter, and zero-remaining-entry tests pass. Run privacy gate, then:

  ```sh
  git add \
    packages/expo-modules-dotnet/native/include/expo_jsi.h \
    packages/expo-modules-dotnet/native/packages/jsi/include/ExpoJsiBridge.h \
    packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectCapabilities.h \
    packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectHandles.h \
    packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
    packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.h \
    packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.cpp \
    packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h \
    packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.cpp \
    packages/expo-modules-dotnet/native/testhost/CMakeLists.txt \
    packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h \
    packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp \
    packages/expo-modules-dotnet/native/testhost/src/WeakObjectCapabilitiesLegacyCompileTest.cpp \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObject.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptWeakObject.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/Internal/JavaScriptObjectInner.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Interop/ExpoJsiApiTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptWeakObjectTests.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs
  git commit -m "feat(jsi): add opaque weak object handles"
  ```

  STOP/NO-GO if an actual Hermes `WeakObject` contract cannot be adapted behind the erased capability boundary, a legacy compile names an unavailable member, a lock crosses runtimes, or either release path leaves a collection entry.

## Task 3: Prove the internal per-context identity registry

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjectRegistryTests.cs`

**Produces:** internal-only `ISharedObjectLifetime`, `SharedObjectRegistry`, `SharedObjectEntry`, `SharedObjectNativeState`, and a per-pair static prototype installer with `release`. No generated or public feature is created.

- [ ] **Step 1: Write RED registry tests with complete test doubles.**

  Add `InternalsVisibleTo` in `Expo.ModulesCore.csproj`:

  ```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>Expo.ModulesCore.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
  ```

  In `SharedObjectRegistryTests.cs`, define:

  ```csharp
  private sealed class TestSharedObject : ISharedObjectLifetime
  {
    private readonly bool throwOnRelease;
    internal TestSharedObject(bool throwOnRelease = false) => this.throwOnRelease = throwOnRelease;
    public int ReleaseCount { get; private set; }
    public void ReleaseFromSharedObjectRegistry()
    {
      ReleaseCount++;
      if (throwOnRelease) throw new InvalidOperationException("test release failure");
    }
  }

  private sealed class CleanupTracker : IDisposable
  {
    public int DisposeCount { get; private set; }
    public void Dispose() => DisposeCount++;
  }

  private sealed class BlockingCleanupTracker(
      ManualResetEventSlim enteredCleanup,
      ManualResetEventSlim allowCleanupToFinish) : IDisposable
  {
    public void Dispose()
    {
      enteredCleanup.Set();
      allowCleanupToFinish.Wait();
    }
  }
  ```

  Add these test methods:

  ```csharp
  [Fact] public void ManagedToJavaScriptReturnsStrictlyEqualLiveObject();
  [Fact] public void JavaScriptObjectRoundTripsToTheSameManagedInstance();
  [Fact] public void ExplicitReleaseAndLaterNativeStateCallbackRunOnce();
  [Fact] public void DeterministicCollectionReleasesThePairOnce();
  [Fact] public void StaleAndForeignObjectsFailWithoutAllocatingAPair();
  [Fact] public void ContextTeardownDrainsTheRegistryWhileRuntimeIsActive();
  [Fact] public void ContextDisposeContinuesAfterRegistryFailureAndDrainsLaterOwners();
  [Fact] public async Task ConcurrentDisposeWaitsForTerminalState();
  [Fact] public void PairConstructionFailureReleasesTemporaryWrappersAndUsesWeakCallbackState();
  [Fact] public void RegistryDisposeAfterInvalidationUsesOnlyTeardownSafeWeakRelease();
  ```

  The identity skeleton is:

  ```csharp
  using var context = new DotnetRuntimeContext(runtime);
  var registry = context.SharedObjects;
  var instance = new TestSharedObject();
  using var first = context.SharedObjects.GetOrCreateJavaScriptObject(instance);
  using var second = context.SharedObjects.GetOrCreateJavaScriptObject(instance);
  using var firstValue = first.AsValue();
  using var secondValue = second.AsValue();
  Assert.True(runtime.StrictEquals(firstValue, secondValue));
  Assert.Same(instance, registry.ResolveManaged(first));
  ```

  For explicit release, publish an owned test object to global with a named `using var` value, evaluate `globalThis.__shared.release()`, assert count one and registry count zero, then evaluate `release()` again and assert it is a no-op with count still one. Assert `ResolveManaged` and `GetOrCreateJavaScriptObject(instance)` both throw and never form a pair. For collection, clear the global, dispose every strong wrapper, invoke `CollectGarbageForTesting`, call `WaitUntilIdle()`, then enter `Runtime.Execute` to assert count one/zero entries. For foreign input, use `runtime.CreateObject`; for stale input retain the released JS object before release; assert both throw and leave count unchanged.

  The context teardown test captures `var registry = context.SharedObjects`, creates a live pair, and calls `context.Dispose()` **inside** `fixture.Runtime.Execute` while ordinary context owners are valid. Exit that access frame, call `fixture.WaitUntilIdle()` to drain normal weak release work, then enter a new `Runtime.Execute` frame to assert release count one, `registry.Count == 0`, `LongLivedObjectsRemaining == 0`, retained `release` fails without incrementing `ReleaseCount`, and stale token resolution fails. This test scopes valid access to ordinary context-owner cleanup; registry terminal cleanup itself is separately proven safe after adapter invalidation. Low-level Closing/late-invalidation races remain exclusively in Task 2.

  The aggregate integration test creates two pairs and publishes the first pair's `release` function to JavaScript: `TestSharedObject(throwOnRelease: true)` increments then throws, while the second increments normally. Register `var tracker = context.RegisterRetainedCallback(new CleanupTracker())` after the pairs are installed, dispose all managed pair wrappers, and snapshot testhost counters. Inside `fixture.Runtime.Execute`, capture `Assert.Throws<AggregateException>(context.Dispose)`, assert it includes the release failure, both release counts are one, `registry.Count == 0`, `tracker.DisposeCount == 1`, and `context.SharedObjects`, `context.Objects`, and `context.Runtime` each throw `ObjectDisposedException`. This proves the registry aggregate did not skip the later retained-callback owner and that the context reached terminal state before throwing. Exit the frame, call `fixture.WaitUntilIdle()`, then assert the weak-release counter delta is exactly two and `LongLivedObjectsRemaining == 0`. In a fresh `Runtime.Execute` frame, assert the JavaScript-retained release function fails without another action. Do not assert a prototype released-value delta because normal context owners may release additional wrappers.

  For concurrent disposal, create a `BlockingCleanupTracker` registered through `RegisterRetainedCallback`; its `Dispose` signals `enteredCleanup` and waits on `allowCleanupToFinish`, without JSI access. Use this shape:

  ```csharp
  var context = fixture.Runtime.Execute(runtime =>
  {
    var created = new DotnetRuntimeContext(runtime);
    created.RegisterRetainedCallback(new BlockingCleanupTracker(
        enteredCleanup, allowCleanupToFinish));
    return created;
  });
  var first = Task.Run(() => fixture.Runtime.Execute(_ => context.Dispose()));
  Task? second = null;
  var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var secondReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  List<Exception> failures = [];
  try
  {
    enteredCleanup.Wait(TestContext.Current.CancellationToken);
    second = Task.Run(() =>
    {
      secondEntered.SetResult();
      context.Dispose();
      secondReturned.SetResult();
    });
    await secondEntered.Task;
    Assert.Throws<ObjectDisposedException>(() => { _ = context.SharedObjects; });
    Assert.False(secondReturned.Task.IsCompleted);
  }
  catch (Exception exception)
  {
    failures.Add(exception);
  }
  finally
  {
    allowCleanupToFinish.Set();
    try { await first; }
    catch (Exception exception) { failures.Add(exception); }
    if (second is not null)
    {
      try { await second; }
      catch (Exception exception) { failures.Add(exception); }
    }
  }
  if (failures.Count == 1)
    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
  if (failures.Count > 1) throw new AggregateException(failures);
  Assert.Throws<ObjectDisposedException>(() => { _ = context.SharedObjects; });
  ```

  The `try` begins immediately after `first` starts, so cancellation or an assertion failure while waiting for cleanup still reaches the `finally` unblock. The `finally` observes `first` and, when started, `second` independently before rethrowing one captured failure or an aggregate. The first, owner-draining call remains inside valid runtime access. The concurrent call only waits on lifecycle state and performs no wrapper cleanup; it is permitted outside the access frame for that coordination-only path. It must not return while the state remains Disposing.

  For per-pair construction rollback, create a registry with the injected per-instance seam and run `GetOrCreateJavaScriptObject` inside active access:

  ```csharp
  var before = fixture.Counters;
  using var registry = new SharedObjectRegistry(
      runtime, () => throw new InvalidOperationException("install failure"));
  var instance = new TestSharedObject();
  var failure = Assert.Throws<AggregateException>(
      () => { _ = registry.GetOrCreateJavaScriptObject(instance); });
  var installFailure = Assert.IsType<InvalidOperationException>(Assert.Single(failure.InnerExceptions));
  Assert.Equal("install failure", installFailure.Message);
  Assert.Equal(before.ReleasedValues + 3u, fixture.Counters.ReleasedValues);
  Assert.Equal(0, registry.Count);
  Assert.Equal(0, instance.ReleaseCount);

  var callbackState = SharedObjectPrototype.CreateReleaseCallbackState(registry);
  Assert.IsType<WeakReference<SharedObjectRegistry>>(callbackState);
  ```

  The per-instance seam executes after named temporary prototype/function/value wrappers are created but before property assignment. The delta proves those three wrappers are released; zero maps and actions prove rollback did not publish or terminally release an uncommitted entry. The successful-path assertion proves the exact callback-state type passed by the installer factory. Dispose the registry before leaving the active frame. There is no global seam, reset, reflection, or timing-based collection test.

  For invalidate-before-managed-teardown coverage, create a registry and pair directly inside `Runtime.Execute` without a `DotnetRuntimeContext`, dispose all strong pair wrappers, then call `fixture.InvalidateRuntimeForTesting()`. Call `registry.Dispose()` without performing any JSI access, then `fixture.WaitUntilIdle()`. Assert the managed action ran once, `registry.Count == 0`, the weak-abandon counter increased once, and `LongLivedObjectsRemaining == 0`. This test proves terminal cleanup after connector invalidation uses only opaque `JavaScriptWeakObject.Dispose` plus the managed action, not a retained prototype or other ordinary JSI wrapper; no platform-adapter source change is required.

  `Count` is deliberately safe after registry disposal: it takes only the registry lock and returns the current map count, which is zero after the terminal clear. It exposes no create, resolve, release, runtime, or wrapper operation after disposal.

- [ ] **Step 2: Verify RED.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectRegistryTests
  ```

  Expected: compile failure for the internal registry, context owner, and prototype. Do not add a public `SharedObject`, attribute, generated provider, `SharedRef`, facade, event, or codec.

- [ ] **Step 3: Implement exact registry/prototype ownership and locking.**

  Define these internal shapes in `SharedObjectRegistry.cs`:

  ```csharp
  internal interface ISharedObjectLifetime { void ReleaseFromSharedObjectRegistry(); }

  internal sealed class SharedObjectEntry(
      long id,
      ISharedObjectLifetime instance,
      JavaScriptWeakObject weakObject,
      SharedObjectNativeState nativeState)
  {
    internal long Id { get; } = id;
    internal ISharedObjectLifetime Instance { get; } = instance;
    internal JavaScriptWeakObject WeakObject { get; } = weakObject;
    internal SharedObjectNativeState NativeState { get; } = nativeState;
    internal bool IsReleased { get; set; }
  }

  internal sealed class SharedObjectRegistry : IDisposable
  {
    private readonly object gate = new();
    private readonly Dictionary<long, SharedObjectEntry> entriesById = [];
    private readonly Dictionary<ISharedObjectLifetime, SharedObjectEntry> entriesByInstance =
        new(ReferenceEqualityComparer.Instance);
    private readonly JavaScriptRuntime runtime;
    private readonly Action? installFailureForTesting;
    private long nextEntryId = 1;
    private bool disposed;
    internal SharedObjectRegistry(
        JavaScriptRuntime runtime,
        Action? installFailureForTesting = null)
    {
      this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
      this.installFailureForTesting = installFailureForTesting;
    }

    internal int Count { get { lock (gate) return entriesById.Count; } }

    internal JavaScriptObject GetOrCreateJavaScriptObject(ISharedObjectLifetime instance)
    {
      ArgumentNullException.ThrowIfNull(instance);
      SharedObjectEntry? deadEntry = null;
      lock (gate)
      {
        ThrowIfDisposed();
        if (entriesByInstance.TryGetValue(instance, out var existing))
        {
          var locked = existing.WeakObject.Lock();
          if (locked is not null) return locked;
          deadEntry = TakeTerminalEntryLocked(existing.Id);
        }
        else
        {
          return CreateEntryLocked(instance);
        }
      }
      CompleteTerminalEntry(deadEntry!);
      throw new InvalidOperationException("The shared JavaScript object is no longer available.");
    }

    internal ISharedObjectLifetime ResolveManaged(JavaScriptObject value)
    {
      ArgumentNullException.ThrowIfNull(value);
      var state = value.GetNativeState<SharedObjectNativeState>();
      lock (gate)
      {
        ThrowIfDisposed();
        if (!state.Registry.TryGetTarget(out var owner) || !ReferenceEquals(owner, this) ||
            !entriesById.TryGetValue(state.EntryId, out var entry) || entry.IsReleased)
          throw new InvalidOperationException("The JavaScript object is not an active shared object.");
        return entry.Instance;
      }
    }

    internal void ReleaseFromJavaScript(JavaScriptObject value);

    internal void Release(long entryId)
    {
      SharedObjectEntry? entry;
      lock (gate) entry = TakeTerminalEntryLocked(entryId);
      if (entry is not null) CompleteTerminalEntry(entry);
    }

    private SharedObjectEntry? TakeTerminalEntryLocked(long entryId)
    {
      if (disposed || !entriesById.Remove(entryId, out var entry) || entry.IsReleased) return null;
      entry.IsReleased = true;
      entriesByInstance.Remove(entry.Instance);
      return entry;
    }

    private JavaScriptObject CreateEntryLocked(ISharedObjectLifetime instance);
    private static void CompleteTerminalEntry(SharedObjectEntry entry);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose();
  }
  ```

  Fill in the private methods without a fallback replacement path. Their terminal helper always attempts weak-handle disposal and the managed action separately, and it throws an aggregate only after both have been attempted:

  ```csharp
  private JavaScriptObject CreateEntryLocked(ISharedObjectLifetime instance)
  {
    var id = nextEntryId++;
    JavaScriptObject? value = null;
    JavaScriptWeakObject? weak = null;
    SharedObjectEntry? entry = null;
    var attached = false;
    try
    {
      value = SharedObjectPrototype.CreateInstance(runtime, this, installFailureForTesting);
      var nativeState = new SharedObjectNativeState(this, id);
      value.SetNativeState(nativeState);
      attached = true;
      weak = value.CreateWeak();
      entry = new SharedObjectEntry(id, instance, weak, nativeState);
      entriesById.Add(id, entry);
      entriesByInstance.Add(instance, entry);
      return value;
    }
    catch (Exception creationFailure)
    {
      List<Exception>? failures = [creationFailure];
      if (entry is not null)
      {
        entriesById.Remove(id);
        entriesByInstance.Remove(instance);
      }
      if (attached) TryCleanup(value!.ClearNativeState<SharedObjectNativeState>, ref failures);
      if (weak is not null) TryCleanup(weak.Dispose, ref failures);
      if (value is not null) TryCleanup(value.Dispose, ref failures);
      throw new AggregateException(failures!);
    }
  }

  private static void CompleteTerminalEntry(SharedObjectEntry entry)
  {
    List<Exception>? failures = null;
    TryCleanup(entry.WeakObject.Dispose, ref failures);
    TryCleanup(entry.Instance.ReleaseFromSharedObjectRegistry, ref failures);
    if (failures is not null) throw new AggregateException(failures);
  }
  ```

  The following bodies replace the `CreateEntryLocked`, `CompleteTerminalEntry`, and `ReleaseFromJavaScript` declarations in the shape above. Add the release callback entry point and NativeState token exactly as follows. `SharedObjectNativeState` stores `WeakReference<SharedObjectRegistry>` and `long entryId`, never a strong registry reference. Its `Dispose` calls `TryGetTarget`, then `registry.Release(entryId)` only when the registry still exists. That callback path may execute `JavaScriptWeakObject.Dispose` only because it is opaque-handle-only/nonblocking/no-access-frame/no-JSI/no-sync-scheduling. It must not call `CreateWeak`, `Lock`, `AsValue`, `Retain`, another wrapper method, or use a scoped ref.

  ```csharp
  internal void ReleaseFromJavaScript(JavaScriptObject value)
  {
    ArgumentNullException.ThrowIfNull(value);
    SharedObjectEntry? entry;
    lock (gate)
    {
      ThrowIfDisposed();
      var state = value.GetNativeState<SharedObjectNativeState>();
      if (!state.Registry.TryGetTarget(out var owner) || !ReferenceEquals(owner, this))
        throw new InvalidOperationException("The JavaScript object belongs to another registry.");
      entry = TakeTerminalEntryLocked(state.EntryId);
    }
    if (entry is not null) CompleteTerminalEntry(entry);
  }

  internal sealed class SharedObjectNativeState :
      IJavaScriptNativeState<SharedObjectNativeState>, IDisposable
  {
    public static JavaScriptNativeStateTypeId TypeId { get; } =
        JavaScriptNativeStateTypeId.FromName(nameof(SharedObjectNativeState));
    internal WeakReference<SharedObjectRegistry> Registry { get; }
    internal long EntryId { get; }
    internal SharedObjectNativeState(SharedObjectRegistry registry, long entryId)
    {
      Registry = new WeakReference<SharedObjectRegistry>(registry);
      EntryId = entryId;
    }
    public void Dispose()
    {
      if (Registry.TryGetTarget(out var registry)) registry.Release(EntryId);
    }
  }
  ```

  `ReleaseFromJavaScript` uses the same snapshot pattern as `Release`: validate and capture the entry under `registry.gate`, leave the lock, then call `CompleteTerminalEntry`. `ResolveManaged` likewise requires `TryGetTarget(out var tokenRegistry)` and `ReferenceEquals(tokenRegistry, this)` before accepting the id.

  Apply this lock algorithm exactly:

  1. `GetOrCreateJavaScriptObject` locks `registry.gate`. If an active entry exists, it calls `entry.WeakObject.Lock()` **while holding `registry.gate`**. The weak wrapper then takes its private weak gate. It wraps/returns the owned result before releasing either lock. If Lock returns null, the current method marks the entry released, removes both maps, and snapshots weak/lifetime while still holding `registry.gate`; it releases the gate, disposes the weak wrapper, invokes the lifetime action, then throws. It never calls `Release` while holding `registry.gate`, and it never creates a replacement object.
  2. For a new entry, under `registry.gate` allocate a monotonically increasing id, call the per-pair installer to create the owned instance, create `SharedObjectNativeState`, attach it, create the owned weak wrapper, create `SharedObjectEntry`, then add both maps as the final observable operation. The installer has already disposed its temporary prototype/function/value wrappers before it returns this instance. On each later failure: if attached, clear only that NativeState while the object is still valid; its callback may reenter `registry.Release(id)`, but the entry is not mapped yet so it is a no-op. Then dispose the weak wrapper if created, dispose the instance, and leave both maps unchanged. This follows registry gate → weak gate order and never invokes the lifetime release action for an uncommitted entry.
  3. `ResolveManaged` reads the token through the passed object before entering `registry.gate`. Under the gate it rejects a disposed registry, requires the token's weak registry reference to resolve to this registry, verifies id lookup and `!IsReleased`, then returns `entry.Instance`. Missing, collected, foreign, and stale tokens throw `InvalidOperationException` and allocate nothing.
  4. `Release` locks `registry.gate`, finds an active entry, sets `IsReleased`, and removes both maps. It snapshots `WeakObject` and `Instance`, releases `registry.gate`, calls `weak.Dispose()`, then calls `instance.ReleaseFromSharedObjectRegistry()` outside **both** gates. Missing/released ids and NativeState callbacks after registry disposal are no-ops. `ReleaseFromJavaScript` instead rejects a disposed registry before reading its token, so a retained release function cannot invoke a lifetime action after teardown.
  5. No code takes `registry.gate` while holding a weak wrapper gate. The only nesting is registry gate → weak gate in `GetOrCreate`; `Dispose` never calls back to the registry. Therefore terminal release cannot begin while a lock is creating/returning an owned object, and no lock can begin after the terminal map removal.

  Registry `Dispose` uses the same terminal transition without recursively acquiring the gate. It terminally marks and clears every map before attempting cleanup, attempts both operations for every entry even when either throws, and throws one aggregate only after all work completes. It has no ordinary JSI wrapper or prototype owner to dispose:

  ```csharp
  public void Dispose()
  {
    List<SharedObjectEntry> terminalEntries;
    lock (gate)
    {
      if (disposed) return;
      disposed = true;
      terminalEntries = entriesById.Values.Where(entry => !entry.IsReleased).ToList();
      foreach (var entry in terminalEntries) entry.IsReleased = true;
      entriesById.Clear();
      entriesByInstance.Clear();
    }
    List<Exception>? failures = null;
    foreach (var entry in terminalEntries)
    {
      TryCleanup(entry.WeakObject.Dispose, ref failures);
      TryCleanup(entry.Instance.ReleaseFromSharedObjectRegistry, ref failures);
    }
    if (failures is not null) throw new AggregateException(failures);
  }

  private static void TryCleanup(Action action, ref List<Exception>? failures)
  {
    try { action(); }
    catch (Exception exception) { (failures ??= []).Add(exception); }
  }
  ```

  No terminal release action runs while `registry.gate` or a weak gate is held. A late NativeState callback observes no map entry and returns.

  Implement `SharedObjectPrototype` as a static per-pair installer, not an `IDisposable` owner. It must not store a field, retain a wrapper, or survive `CreateInstance`:

  ```csharp
  internal static class SharedObjectPrototype
  {
    internal static object CreateReleaseCallbackState(SharedObjectRegistry registry) =>
        new WeakReference<SharedObjectRegistry>(registry);

    internal static JavaScriptObject CreateInstance(
        JavaScriptRuntime runtime,
        SharedObjectRegistry registry,
        Action? installFailureForTesting)
    {
      using var prototype = runtime.CreateObject();
      using var releaseFunction = runtime.CreateHostFunction(
          "release", 0, Release, CreateReleaseCallbackState(registry));
      using var releaseValue = releaseFunction.AsValue();
      installFailureForTesting?.Invoke();
      prototype.SetProperty("release", releaseValue);
      return runtime.CreateObjectWithPrototype(prototype);
    }

    private static JavaScriptValue Release(
        JavaScriptRuntime runtime, JavaScriptValueRef thisValue,
        JavaScriptArguments arguments, object callbackState)
    {
      var registryReference = (WeakReference<SharedObjectRegistry>)callbackState;
      if (!registryReference.TryGetTarget(out var registry))
        throw new ObjectDisposedException(nameof(SharedObjectRegistry));
      using var target = thisValue.AsObject().Retain();
      registry.ReleaseFromJavaScript(target);
      return runtime.CreateUndefined();
    }
  }
  ```

  The named `using` locals make failure transactional and dispose the temporary prototype/function/value wrappers whether installation throws or the instance is returned. `CreateObjectWithPrototype` transfers the JavaScript references: the returned instance retains the prototype, and the prototype retains `release`; the returned instance is the only managed ordinary wrapper that escapes the method. The callback-state factory returns `WeakReference<SharedObjectRegistry>` and is the exact object passed to `CreateHostFunction`, so neither the JavaScript function nor NativeState can pin the registry.

  `SharedObjectPrototype` must not call `GeneratedFunction.DefineSync`, `DotnetRuntimeContext.RegisterHostFunction`, or retain a `JavaScriptObject`, `JavaScriptFunction`, `JavaScriptValue`, or `GeneratedHostFunctionRegistration`. Its callback casts only the weak registry state, retains its JavaScript `this` value for the callback, then delegates to `ReleaseFromJavaScript`.

  `ReleaseFromJavaScript` rejects a disposed registry, gets the token/id, and calls `Release`; it creates no wrapper. Existing generated host registrations remain an unrelated later `DotnetRuntimeContext` cleanup owner; this per-pair release callback is not one of them. A JavaScript function retained after teardown either finds no registry through its weak reference or finds a disposed registry, and in both cases throws `ObjectDisposedException` before invoking a lifetime action. There is no JavaScript-visible id, constructor, round-trip function, event, or method beyond `release`; NativeState is the sole identity token.

  In `DotnetRuntimeContext`, replace the boolean lifetime flag with this exact lifecycle state and guard every owner accessor with `gate`:

  ```csharp
  private enum LifecycleState { Active, Disposing, Disposed }

  private readonly JavaScriptRuntime runtime;
  private readonly SharedObjectRegistry sharedObjects;
  private LifecycleState state = LifecycleState.Active;
  private int disposingThreadId;

  public JavaScriptRuntime Runtime
  {
    get { lock (gate) { ThrowIfNotActiveLocked(); return runtime; } }
  }

  public JavaScriptObjectFactory Objects
  {
    get { lock (gate) { ThrowIfNotActiveLocked(); return objects; } }
  }

  public ModuleRegistry ModuleRegistry
  {
    get { lock (gate) { ThrowIfNotActiveLocked(); return moduleRegistry; } }
  }

  public ModuleEventEmitter Events
  {
    get { lock (gate) { ThrowIfNotActiveLocked(); return events; } }
  }

  internal SharedObjectRegistry SharedObjects
  {
    get { lock (gate) { ThrowIfNotActiveLocked(); return sharedObjects; } }
  }
  ```

  Use this constructor so production passes no test seam:

  ```csharp
  public DotnetRuntimeContext(JavaScriptRuntime runtimeArgument)
  {
    runtime = runtimeArgument ?? throw new ArgumentNullException(nameof(runtimeArgument));
    objects = new JavaScriptObjectFactory(runtime);
    events = new ModuleEventEmitter(this);
    moduleRegistry = new ModuleRegistry(this, objects);
    sharedObjects = new SharedObjectRegistry(runtime);
  }
  ```

  This preserves the existing context owner-construction order and adds the resource-free registry afterward. The registry constructor performs no JSI work and retains no ordinary wrapper, so prototype installation cannot fail during context construction or create a new transactional-constructor leak. `RegisterHostFunction` and `RegisterRetainedCallback` keep their existing registration/rollback behavior, but use `ThrowIfNotActiveLocked()` under `gate` so they reject both Disposing and Disposed.

  Implement `Dispose` with one owner-draining caller, waiting concurrent callers, and a same-thread reentrancy no-op that avoids a cleanup callback deadlock:

  ```csharp
  public void Dispose()
  {
    List<GeneratedHostFunctionRegistration> registrations;
    List<IDisposable> callbacks;
    lock (gate)
    {
      if (state == LifecycleState.Disposed) return;
      if (state == LifecycleState.Disposing)
      {
        if (disposingThreadId == Environment.CurrentManagedThreadId) return;
        while (state == LifecycleState.Disposing) Monitor.Wait(gate);
        return;
      }

      state = LifecycleState.Disposing;
      disposingThreadId = Environment.CurrentManagedThreadId;
      registrations = [.. hostFunctionRegistrations];
      hostFunctionRegistrations.Clear();
      callbacks = [.. retainedCallbacks];
      retainedCallbacks.Clear();
    }

    List<Exception>? exceptions = null;
    try
    {
      DisposeAndCapture(sharedObjects, ref exceptions);
      foreach (var registration in registrations)
        DisposeAndCapture(registration, ref exceptions);
      foreach (var callback in callbacks)
      {
        if (callback is IRuntimeContextRetainedCallback retainedCallback)
          DisposeAndCapture(retainedCallback.DisposeFromRuntimeContext, ref exceptions);
        else
          DisposeAndCapture(callback, ref exceptions);
      }
      DisposeAndCapture(moduleRegistry.Dispose, ref exceptions);
      DisposeAndCapture(events, ref exceptions);
      DisposeAndCapture(objects, ref exceptions);
    }
    finally
    {
      lock (gate)
      {
        state = LifecycleState.Disposed;
        disposingThreadId = 0;
        Monitor.PulseAll(gate);
      }
    }

    if (exceptions is not null) throw new AggregateException(exceptions);
  }

  private void ThrowIfNotActiveLocked()
  {
    if (state != LifecycleState.Active)
      throw new ObjectDisposedException(typeof(DotnetRuntimeContext).Name);
  }
  ```

  `ThrowIfNotActiveLocked` throws `ObjectDisposedException` whenever state is not Active. The first `Dispose` atomically changes Active to Disposing before it snapshots or invokes any owner. Every accessor, registration path, and concurrent call therefore rejects or waits without using an owner during cleanup. A same-thread reentrant `Dispose` returns as a no-op solely to avoid deadlock; accessors remain rejected until the outer cleanup reaches Disposed. A different-thread disposer waits on `Monitor.Wait` and returns only after the outer caller sets Disposed and pulses, even if the outer call subsequently throws its final aggregate. Do not add production test-count state.

  The owner teardown order is `sharedObjects` (maps → weak handles → lifetime actions only), generated host registrations, retained callbacks, module registry, event state, and object factory. `DisposeAndCapture` flattens the registry's `AggregateException`, continues every later owner, and the `finally` transition to Disposed happens before the first caller returns normally or throws the final aggregate.

- [ ] **Step 4: Verify GREEN and commit.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectRegistryTests
  scripts/test-managed.sh --filter FullyQualifiedName~DotnetRuntimeContextTests
  ```

  Expected: strict identity, round-trip reference identity, explicit/GC/teardown exactly-once release, stale/foreign rejection, no deadlock, and zero native remaining entries. Run privacy gate, then:

  ```sh
  git add \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjectRegistryTests.cs
  git commit -m "feat(modules-core): prove shared object identity registry"
  ```

  STOP/NO-GO if this requires generated bindings, a public feature, a JS-visible id, hot-path reflection, a replacement pair, reverse lock order, or NativeState JSI work.

## Task 4: Verify, document, and close GO or NO-GO

**Files on GO:** `docs/specs/runtime-and-abi.md`, `managed-jsi-wrappers.md`, `ownership-and-scoped-refs.md`, `hermes-testhost.md`, `modules-core-boundary.md`, `docs/changes/2026-07-19-sharedobject/spec.md`, `docs/plans/README.md`, and moved archive package.

**Files on NO-GO:** only `docs/changes/2026-07-19-sharedobject/spec.md` and `docs/plans/README.md`.

- [ ] **Step 1: Run verification and classify all new ownership findings.**

  ```sh
  scripts/test-managed.sh --filter FullyQualifiedName~HermesGarbageCollectionTests
  scripts/test-managed.sh --filter FullyQualifiedName~JavaScriptWeakObjectTests
  scripts/test-managed.sh --filter FullyQualifiedName~ExpoJsiApiTests
  scripts/test-managed.sh --filter FullyQualifiedName~SharedObjectRegistryTests
  scripts/test-managed.sh --filter FullyQualifiedName~DotnetRuntimeContextTests
  scripts/test-managed.sh
  scripts/format.sh --check --all
  git diff --check
  git diff --unified=0 7d40f467..HEAD -- packages/expo-modules-dotnet | \
    rg '^\+[^+].*\.AsValue\(\)\.(AsObject|AsArray|AsFunction)\(|^\+[^+].*\.(AsObject|AsArray|AsFunction)\(\)\.(AsValue|AsObject|GetProperty)\(' || true
  git diff --unified=0 7d40f467..HEAD -- packages/expo-modules-dotnet/managed/packages | \
    rg '^\+[^+].*(Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer)' || true
  ```

  Expected: all test/format/diff commands pass. The final two commands may print baseline-independent additions; manually classify **every** printed line in the spike record. Each new owned conversion must have named ownership (`using var`), a documented transfer, or a scoped-ref alternative. Each reflection match must be absent or rejected as outside the generated hot path. Do not require repository-wide zero matches because legitimate baseline code is preserved.

- [ ] **Step 2: Record evidence and choose the gate.**

  Replace the delta spec's creation-time result with actual commands/results, artifacts, all owned/retain/detach paths, callback restrictions, lock order, release/abandon/remaining-entry counters, collection scheduler evidence, and GO/NO-GO result.

  GO requires all focused tests, deterministic dead weak-lock collection and collection-triggered registry release, zero remaining entries after release and abandonment, full managed suite, format, and safe manual classifications.

  On GO, merge only implemented current behavior: v23 weak ABI/lifetime into `runtime-and-abi`; wrapper gate/affinity/Dispose callback rule into `managed-jsi-wrappers`; weak ownership into `ownership-and-scoped-refs`; GC control/counters into `hermes-testhost`; registry identity/token/callback/teardown order, per-pair prototype installation, and the absence of any retained ordinary JSI wrapper into `modules-core-boundary`. State deferred public/generator/SharedRef/TS/events/cross-runtime/object-codec scope explicitly. Then `git mv docs/changes/2026-07-19-sharedobject docs/archive/changes/2026-07-19-sharedobject`, mark only Plan 007 `DONE`, privacy-scan, and commit `docs: close sharedobject weak identity spike`.

  On NO-GO, do not merge living specs or archive. Update Plan 007 to `BLOCKED — <environment/capability condition and unblock>` when the invariant cannot be proved in this environment, or `REJECTED — <design invariant failure>` when the design cannot meet the contract. Commit the result evidence and status narrowly as `docs: record sharedobject spike no-go` after the privacy gate. Never mark DONE or use timing-based collection coverage.

## STOP conditions

- Deterministic executor-thread collection is unavailable or incompatible.
- The erased capability payload cannot provide opaque weak create/lock/release with legacy structured unsupported behavior.
- Any release/abandon path touches invalid JSI or leaves a remaining collection entry/`RuntimeState` retention.
- NativeState callback needs JSI, an access frame, blocking, synchronous runtime scheduling, an arbitrary wrapper, a scoped ref, raw managed pointer, or JS-visible id.
- Registry proof needs a public SharedObject API, generator, SharedRef, TypeScript, events, cross-runtime pairing, HostObject-first object, or JavaScriptObject codec.
- Lock order would reverse weak gate → registry gate, terminal transition can overlap a successful post-terminal lock, or lifetime action would run under either gate.

## Plan self-review

- [ ] **Coverage:** Tasks 1–3 cover every accepted weak, GC, NativeState, counter, registry, identity, and teardown requirement; Task 4 covers full verification and both terminal documentation paths.
- [ ] **Type consistency:** ABI create returns `expo_jsi_weak_object_result`; ABI lock returns `expo_jsi_weak_object_lock_result`; managed API is `CreateWeak(): JavaScriptWeakObject` and `Lock(): JavaScriptObject?`; the lifetime action is `ReleaseFromSharedObjectRegistry()`.
- [ ] **Scope:** no public SharedObject, attribute, generator, SharedRef, TypeScript, events, cross-runtime feature, or object codec appears in implementation tasks.
- [ ] **No incomplete markers:**

  ```sh
  if rg -n 'T(O)DO|T(B)D|implement[ ]later|fill[ ]in[ ]details|Add[ ]appropriate[ ]error[ ]handling|Write[ ]tests[ ]for[ ]the[ ]above|Similar[ ]to[ ]Task' docs/changes/2026-07-19-sharedobject/plan.md; then exit 1; fi
  ```

  Expected: no output, zero exit.
- [ ] **Privacy/path:** run `git diff --check` and the generic privacy gate; confirm paths are repo-relative and no machine-local data appears.
