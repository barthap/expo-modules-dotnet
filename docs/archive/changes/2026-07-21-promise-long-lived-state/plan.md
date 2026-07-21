# Promise Long-Lived Capability State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` to implement this plan task-by-task, with a fresh implementer and spec/code-quality review after each task. Use `superpowers:test-driven-development` for every behavior change and `expo-jsi-managed-handle-lifetime` for every ownership review. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register every native Promise capability as runtime-owned long-lived state, make settlement and teardown reentrant-safe, and prevent managed disposal from releasing an opaque handle while a native call uses it.

**Architecture:** `PromiseEntry` owns all JSI Promise state and remains in `RuntimeState::longLivedObjects()` until release or abandonment. `PromiseHandle` is only an opaque coordination wrapper holding shared runtime/entry state and the collection id. Native settlement and lifetime termination use independent state machines, while `JavaScriptPromise` protects calls with non-blocking handle leases.

**Tech Stack:** C++20, Hermes JSI, C ABI opaque handles, .NET/C#, xUnit, native Hermes testhost.

**Execution status: COMPLETE (2026-07-22), all tasks done.**

## Global Constraints

- Work on the current `development` branch from the reviewed baseline at commit `4e32d07d`; do not create a branch or worktree.
- C++ owns JSI mechanics, C# owns module logic, and only opaque handles cross the C ABI.
- Do not modify any signature or add any entry in `packages/expo-modules-dotnet/native/include/expo_jsi.h`.
- Keep `LongLivedObjectCollection::add(std::shared_ptr<LongLivedObject>)` and its ArrayBuffer/weak-object callers unchanged; Promise registration alone uses `tryAdd`.
- Do not modify `JavaScriptPromiseScheduler` or its scheduled-callback settlement/disposal ordering.
- Never hold the Promise-entry mutex while calling JavaScript or destroying JSI state.
- Preserve the existing `LongLivedObjectCollection` callback-under-collection-mutex discipline for this slice. `PromiseEntry::release` and `PromiseEntry::abandon` SHALL NOT call back into the collection or user JavaScript; resolver calls and Promise-entry JSI-state destruction remain outside the Promise-entry mutex. If implementation reveals a concrete deadlock that requires changing collection lock discipline, stop and report instead of changing ArrayBuffer/weak-object behavior outside this approved scope.
- Successful settlement clears both resolver functions but retains the Promise object and collection entry until release or teardown.
- Release/abandon counters change only when terminal cleanup completes, not at settlement, wrapper deletion, cleanup request, or pending cleanup.
- Tests use synchronous re-entry or testhost condition-variable gates, never sleeps as correctness assertions.
- Before every commit, inspect staged content for absolute paths, usernames, machine names, private hostnames, and machine-specific install paths.
- Stop if the change requires an `expo_jsi.h` ABI edit, changes `add()` semantics, changes async settlement ordering, or fails the same verification twice after a reasonable correction.

## Baseline and drift check

- [ ] Confirm the branch and tree before Task 1.

Run:

```sh
git status --short --branch
git diff --stat 4e32d07d..HEAD -- \
  packages/expo-modules-dotnet/native/packages/jsi \
  packages/expo-modules-dotnet/native/testhost \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests \
  docs/specs/promises.md
```

Expected: branch `development`; no unreviewed in-scope source drift. Preserve unrelated operator changes and do not stage them.

---

### Task 1: Terminal registration, opaque ownership, rollback, and counters

**Files:**

- Create: `packages/expo-modules-dotnet/native/packages/jsi/src/PromiseHandles.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.cpp`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridgeTestHooks.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`

**Interfaces:**

- Produces: `std::optional<uint64_t> LongLivedObjectCollection::tryAdd(std::shared_ptr<LongLivedObject> object)`.
- Preserves: `uint64_t LongLivedObjectCollection::add(std::shared_ptr<LongLivedObject> object)` exactly for existing callers.
- Produces: `PromiseEntry`, derived from `LongLivedObject`, with `promiseValue(jsi::Runtime &)`, `resolve(jsi::Runtime &, const jsi::Value &)`, `reject(jsi::Runtime &, const jsi::Value &)`, `release(jsi::Runtime &) noexcept`, and `abandon() noexcept`.
- Produces: `PromiseHandle(std::shared_ptr<RuntimeState>, std::shared_ptr<PromiseEntry>, uint64_t)`, `~PromiseHandle()`, and `entry() -> std::shared_ptr<PromiseEntry>`.
- Produces: `RuntimeState::{notePromiseReleased,notePromiseAbandoned,promisesReleased,promisesAbandoned,resetPromiseCounters}`.
- Produces: `RuntimeLongLivedCounters::{promisesReleased,promisesAbandoned}` and matching testhost fields `long_lived_promises_released` / `long_lived_promises_abandoned`.
- Produces test-only controls: `failNextPromiseHandleAllocationForTesting() noexcept`, a one-shot Promise-registration gate in `ExpoJsiBridgeTestHooks.h`, and matching testhost exports for fail/pause/wait/resume.

- [ ] **Step 1: Write registration and rollback tests first**

Add these named tests to `JavaScriptPromiseTests.cs`:

```csharp
[Fact]
public void UnresolvedPromiseIsRegisteredAndAbruptTeardownAbandonsItExactlyOnce()
// Create one promise in Runtime.Execute, assert Remaining == 1 and both
// Promise terminal counters == 0, call ReleaseBridgeRuntimeHandle(), then
// assert PromisesAbandoned == 1 and Remaining == 0. Dispose the managed
// wrapper twice and assert the captured terminal counts remain 0/1/0.

[Fact]
public void PromiseConstructorReenteringPreparationCannotRegisterAfterTerminalSweep()
// Install a managed host function named prepareRuntime that calls
// fixture.PrepareRuntimeForInvalidation(). The callback first creates its
// owned undefined return value, then prepares, then transfers that existing
// value without allocating after preparation. Replace globalThis.Promise with
// a constructor that invokes prepareRuntime(), then invokes its executor with
// resolve/reject functions and returns an object. Assert CreatePromise throws
// InvalidOperationException, Remaining == 0, and both Promise counters == 0.

[Fact]
public void PromiseHandleAllocationFailureRollsBackTheRegisteredEntry()
// Call fixture.FailNextPromiseHandleAllocation(), assert CreatePromise throws,
// then assert Remaining == 0, PromisesReleased == 1, PromisesAbandoned == 0.

[Fact]
public async Task PromiseRegistrationRacingPreparationIsEitherRejectedOrSwept()
// Pause after the user Promise constructor returns but before tryAdd. Start a
// Runtime.Execute callback that attempts CreatePromise and, on success,
// disposes that wrapper inside the same callback before returning. Wait for
// the registration gate, queue PrepareRuntimeForInvalidation, then resume in
// unconditional finally and await both operations. If creation succeeded, its
// Closing-state deferred release is swept by preparation. Assert Remaining ==
// 0 and exactly one release only when an entry registered; if creation failed,
// assert both terminal counters stayed zero. Never carry the wrapper out of
// the originating Runtime.Execute callback or dispose it from the test thread.
```

Extend the native and both managed counter structs in identical field order:

```text
... long_lived_weak_objects_released
... long_lived_weak_objects_abandoned
... long_lived_promises_released
... long_lived_promises_abandoned
... long_lived_objects_remaining
```

Add `NativeTestHost.FailNextPromiseHandleAllocation(nint)` and `HermesRuntimeFixture.FailNextPromiseHandleAllocation()`. Add one-shot `PauseNextPromiseRegistration`, `WaitUntilPromiseRegistrationPaused`, and `ResumePromiseRegistration` fixture methods backed by a mutex/condition-variable gate in `ExpoJsiBridge.cpp`. The gate runs immediately before `tryAdd`; it is disabled unless the test explicitly arms it. Scope the armed gate to one creation attempt on the exact testhost runtime. An opaque Promise handle does not exist at this pre-registration point, so the runtime plus one-shot attempt is the identity; never pause another runtime's registration.

Wrap every test-side gate use in `try/finally`: `ResumePromiseRegistration()` runs unconditionally, even when setup, preparation, creation, or an assertion throws. Native gate exit also disarms the one-shot state on success and exception paths.

- [ ] **Step 2: Run the Promise tests and verify RED**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
```

Expected: FAIL because Promise entries are not registered, terminal registration is absent, and the allocation-failure control is not implemented. If failure is only a spelling/layout mismatch, correct the test/harness declaration and rerun until behavior fails for the intended reason.

- [ ] **Step 3: Make collection termination atomic with registration**

Add `#include <optional>`, `bool terminal_ = false`, and this interface:

```cpp
std::optional<uint64_t> tryAdd(std::shared_ptr<LongLivedObject> object);
```

Implement `tryAdd` with the same null rejection as `add`, then acquire `mutex_`, return `std::nullopt` if `terminal_`, otherwise insert an `Active` entry and return its id. Set `terminal_ = true` while holding `mutex_` at the start of both `sweep` and `invalidateWithoutRuntime`, before inspecting or erasing entries. Do not route `add()` through `tryAdd()` and do not edit ArrayBuffer or weak-object registration.

- [ ] **Step 4: Add Promise entry/handle ownership and terminal counters**

Use this exact state ownership in `PromiseHandles.h`:

```cpp
enum class PromiseSettlementState { Active, Settling, Settled };
enum class PromiseCleanupState { None, ReleasePending, AbandonPending, Terminal };

class PromiseEntry final : public LongLivedObject {
public:
  PromiseEntry(std::shared_ptr<RuntimeState> state,
               std::unique_ptr<jsi::Object> promise,
               std::unique_ptr<jsi::Function> resolve,
               std::unique_ptr<jsi::Function> reject);
  jsi::Value promiseValue(jsi::Runtime &runtime);
  void resolve(jsi::Runtime &runtime, const jsi::Value &value);
  void reject(jsi::Runtime &runtime, const jsi::Value &value);
  void release(jsi::Runtime &runtime) noexcept override;
  void abandon() noexcept override;
};

class PromiseHandle final {
public:
  PromiseHandle(std::shared_ptr<RuntimeState> state,
                std::shared_ptr<PromiseEntry> entry,
                uint64_t entryId);
  ~PromiseHandle();
  std::shared_ptr<PromiseEntry> entry() const noexcept;
};
```

Store the Promise object and each resolver behind `std::unique_ptr` so runtime release can destroy them outside the entry lock and abandonment can call `.release()` without invoking JSI destructors after runtime loss. `PromiseHandle::~PromiseHandle()` calls `state_->releaseLongLivedObject(entryId_)`; it never directly clears JSI state. Add dedicated atomic Promise counters to `RuntimeState`, wire get/reset/release snapshots through `RuntimeLongLivedCounters`, and keep the existing wrapper `released_promises` observation separate.

- [ ] **Step 5: Register only after user-controlled construction and roll back on every failure**

In `createPromise`, keep `global.Promise` lookup and `callAsConstructor` before registration. After capturing the object/resolvers:

1. Copy `runtimeHandle->state()` into a local strong reference.
2. Construct a local `shared_ptr<PromiseEntry>`.
3. Call `state->longLivedObjects().tryAdd(entry)`.
4. If it returns empty, return a Promise error result and let locals die on the current runtime path; no terminal counter is incremented because no entry registered.
5. Allocate `PromiseHandle(state, entry, id)` only after registration.
6. If the test-only failure flag fires or allocation throws, call `completeRelease(id, jsRuntime)` before returning the existing Promise error shape. This removes the registered entry, breaks the entry/state cycle, and records one release.
7. Return the opaque handle only after all preceding steps succeed.

Change `promiseAsValue` and `promiseSettle` to copy `auto entry = promise->entry()` before using it. `promiseAsValue` calls `entry->promiseValue(runtime)`; `promiseSettle` delegates to `entry->resolve` or `entry->reject`. `releasePromise` remains `delete promise`, because deleting the wrapper now requests collection removal through `RuntimeState`.

Implement the allocation hook with one atomic one-shot flag in `ExpoJsiBridge.cpp`. Implement the registration gate with test-hook setter/wait/resume functions and a condition variable, and enter it immediately before `tryAdd`. The gate records the exact runtime/creation attempt it armed, ignores unrelated creation attempts, and clears its armed/blocked state on every exit. Expose these controls only through `ExpoJsiBridgeTestHooks.h` and the testhost. Do not add them to `expo_jsi.h`.

- [ ] **Step 6: Verify GREEN and review ownership**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
```

Expected: PASS, including registration, terminal constructor re-entry, allocation rollback, and existing Promise tests. Confirm `git diff --check` passes and verify no raw Promise/resolve/reject JSI owner remains in the opaque wrapper.

- [ ] **Step 7: Commit Task 1**

Stage only Task 1 files, run the privacy scan in Global Constraints, then commit:

```sh
git commit -m "fix(jsi): register promise capabilities as long-lived state"
```

---

### Task 2: Reentrant settlement and pending terminal cleanup

**Files:**

- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/PromiseHandles.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridgeTestHooks.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`

**Interfaces:**

- Consumes: `PromiseEntry::{resolve,reject,release,abandon,promiseValue}` from Task 1.
- Produces: independent `PromiseSettlementState` and `PromiseCleanupState` transitions with exactly one cleanup outcome.
- Produces private test-only `invalidateRuntimeStateWithoutDeletingHandleForTesting(expo_jsi_runtime_handle) noexcept`, testhost export `expo_jsi_testhost_invalidate_bridge_runtime_state_without_deleting_handle(...)`, and managed fixture method `InvalidateBridgeRuntimeStateWithoutDeletingHandle()`.
- Preserves: successful post-settlement `promiseValue`, the existing error codes/messages from `promiseSettle`, and all scheduler behavior.

- [ ] **Step 1: Add settlement-state tests before changing the state machine**

Add separate tests with these exact observations:

```text
ResolveKeepsPromiseEntryRegisteredAndAsValueUsable
  resolve; assert Promise release/abandon == 0/0 and Remaining == 1;
  call AsValue again and prove it is the same JS Promise with StrictEquals;
  dispose, drain, assert 1/0/0.

RejectKeepsPromiseEntryRegisteredAndAsValueUsable
  reject; assert 0/0/1; call AsValue; dispose, drain; assert 1/0/0.

ThrowingResolverReturnsToActiveAndCanBeRetried
  replace Promise with a constructor whose resolve increments a JS counter,
  throws "resolver boom" on call 1, and returns on call 2; assert the first
  Resolve surfaces InvalidOperationException and the second returns; assert
  the recorded counter is 2.

ThenableGetterReenteringResolveIsANonBlockingNoOp
  expose a host function that calls Resolve on the same capability; resolve
  with an object whose `then` getter invokes that host function; record host
  invocation count, return status, and exception text in managed variables;
  assert after the outer Resolve returns that count == 1, returned == true,
  and exception text is empty. Do not assert from inside the callback.

PreparationDuringResolverDefersReleaseUntilResolverReturns
  use a custom Promise resolver that calls a host function invoking
  PrepareRuntimeForInvalidation; the host callback creates its owned undefined
  return value before preparation and transfers it after preparation; record
  that code after the host call still executes; after outer Resolve returns,
  dispose the Promise inside the same Runtime.Execute callback before that
  callback returns; assert 1/0/0 after Runtime.Execute completes.

PreparationDuringThrowingResolverCompletesPendingReleaseAndSurfacesTheError
  custom resolver calls PrepareRuntimeForInvalidation through a host callback
  that pre-creates its owned undefined return before preparation, then throws
  "resolver after teardown"; capture the managed Resolve error text, then
  dispose the Promise inside the same Runtime.Execute callback before it
  returns; after Runtime.Execute completes, assert the captured text, code
  after preparation, and counters 1/0/0.

StateInvalidationDuringResolverDefersAbandonUntilResolverReturns
  custom resolver calls a host function invoking
  InvalidateBridgeRuntimeStateWithoutDeletingHandle; the callback creates its
  owned undefined return value before invalidation and transfers it afterward;
  record that resolver code after the call still runs; after outer Resolve
  returns, dispose the Promise inside the same Runtime.Execute callback before
  it returns; after Runtime.Execute completes, assert 0/1/0, then release the
  bridge runtime handle outside the active ABI call and assert the counts stay
  unchanged.

StateInvalidationDuringThrowingResolverCompletesPendingAbandonAndSurfacesTheError
  custom resolver calls InvalidateBridgeRuntimeStateWithoutDeletingHandle
  through a host callback that pre-creates its owned undefined return before
  invalidation, then throws "abandoned resolver"; capture the error after the
  resolver unwinds, then dispose the Promise inside the same Runtime.Execute
  callback before it returns; after Runtime.Execute completes, assert the
  captured error and Promise release/abandon/remaining == 0/1/0, release the
  bridge runtime handle outside the active ABI call, and assert neither
  terminal counter changes again.
```

All host callbacks record outcomes into locals or JavaScript globals. Assertions run after the outer `Runtime.Execute` returns so Promise machinery cannot turn assertion failures into rejected Promises. Every host callback that calls `PrepareRuntimeForInvalidation` or `InvalidateBridgeRuntimeStateWithoutDeletingHandle` first creates the owned undefined value it will return, then invalidates/prepares, then returns that existing value; it performs no runtime allocation after invalidation. In all four preparation/state-invalidation resolver tests, keep the Promise local to the original `Runtime.Execute` callback and dispose it there, in `finally` after the outer Resolve/Reject outcome is captured and before the callback returns. Do not depend on Task 3 off-runtime forwarding. For state-invalidation cases, call `ReleaseBridgeRuntimeHandle()` only after `Runtime.Execute` has returned.

- [ ] **Step 2: Run the Promise tests and verify RED**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
```

Expected: FAIL on resolver retry, re-entry, or cleanup-during-settlement behavior. A hang is a valid RED for the old lock/re-entry behavior; terminate it and record the failing test name before implementation.

- [ ] **Step 3: Implement the settlement protocol without locks across JavaScript**

For both resolve and reject:

1. Lock the entry mutex.
2. Return immediately unless settlement is `Active` and cleanup is `None`.
3. Move the selected `unique_ptr<jsi::Function>` into a local and set settlement to `Settling`.
4. Unlock before `resolver->call(runtime, value)`.
5. Capture success or `std::exception_ptr` without touching shared state from the catch path.
6. Re-lock. If cleanup is still `None`, then on success move both stored resolver owners out for destruction and set `Settled`; on failure move the selected resolver back and set `Active`.
7. If release/abandon became pending, set cleanup to `Terminal`, move every remaining JSI owner to local cleanup state, and remember the selected terminal outcome; settlement does not return to `Active` even when the resolver threw.
8. Unlock. Destroy runtime-release state or leak abandoned JSI wrappers, then increment exactly the selected terminal counter. Only after cleanup completes, rethrow the captured resolver error so `promiseSettle` preserves existing error translation.

Never hold the entry mutex during `resolver->call`, `unique_ptr` reset/destruction, counter callbacks, or exception rethrow.

- [ ] **Step 4: Implement exactly-once release and abandonment during settlement**

`release(runtime)` and `abandon()` use the same locked arbitration:

```text
cleanup == Terminal              -> return
settlement == Settling, None     -> set ReleasePending or AbandonPending; return
settlement == Settling, pending  -> return; first terminal source wins
Active or Settled, None          -> set Terminal; move out JSI state
```

After unlocking, release destroys moved JSI owners and calls `notePromiseReleased()` once. Abandon calls `.release()` on moved JSI `unique_ptr`s so no JSI destructor runs without runtime access, then calls `notePromiseAbandoned()` once. The entry keeps its Promise object after normal settlement; only lifetime cleanup moves it out.

Add `invalidateRuntimeStateWithoutDeletingHandleForTesting` in `ExpoJsiBridgeTestHooks.h`/`ExpoJsiBridge.cpp`. It calls the existing `RuntimeHandle::invalidateWithoutRuntime()` path so `RuntimeState` invalidates and the collection abandons entries, but it does not unregister or delete the opaque `RuntimeHandle`. The testhost export forwards to this hook and leaves `testhost->runtime` intact, which keeps counters queryable. Tests release that bridge handle only after the active Promise ABI call has returned. Never call `ReleaseBridgeRuntimeHandle()` from a resolver or host callback.

Do not refactor `LongLivedObjectCollection` to invoke callbacks outside its mutex in this plan. `PromiseEntry::release` and `PromiseEntry::abandon` must satisfy the existing `LongLivedObject` callback contract by never re-entering the collection and never invoking user JavaScript. Their Promise-entry mutex protects only state transfer; JSI owner destruction and resolver calls happen after that mutex is released. If a concrete deadlock proves this contract insufficient, treat it as a STOP condition and report the needed cross-kind collection change.

- [ ] **Step 5: Verify GREEN and commit Task 2**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
git diff --check
```

Expected: PASS; no deadlock; resolver errors remain visible; every cleanup case reports exactly one terminal outcome and zero remaining entries.

Commit:

```sh
git commit -m "fix(jsi): coordinate promise settlement with teardown"
```

---

### Task 3: Managed opaque-handle leases, off-runtime forwarding, and disposal races

**Files:**

- Modify: `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromise.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`

**Interfaces:**

- Produces test-only enum values `AsValue = 1`, `Resolve = 2`, `Reject = 3` and exports `pause_next_promise_call`, `wait_until_promise_call_blocked`, and `resume_promise_call` in the testhost API only.
- Produces private managed `HandleLease AcquireHandle()`, `void ReleaseLease()`, and `void ReleaseNativeHandle(ExpoJsiPromiseHandle)`.
- Preserves public `JavaScriptPromise::{AsValue,Resolve,Reject,Dispose}` signatures and disposed-wrapper exception behavior.

- [ ] **Step 1: Add a safe deterministic native-call gate**

Add a condition-variable gate to `expo_jsi_testhost_runtime_t`. Override counted API `promise_as_value` and `promise_settle` with wrappers that:

1. Match the configured one-shot operation.
2. Record the exact `expo_jsi_promise_handle` passed to that call, mark the call blocked, and notify `wait_until_promise_call_blocked`.
3. Wait until `resume_promise_call`.
4. Forward to the inner API.
5. Mark the call complete and forward any Promise release deferred by the gate.

While a gated call is blocked, `countedReleasePromise` still increments `released_promises`, but stores the `(runtime, promise)` release and returns without deleting the opaque handle only when `promise` exactly equals the gate's recorded target handle. It forwards releases for every unrelated Promise immediately. This safety behavior is active only for an explicitly paused test call; it lets the RED test observe an early managed release without inducing use-after-free. When the matching call completes, forward the stored matching release exactly once outside the gate mutex.

The native wrapper uses an RAII exit guard so success, inner-API error results, and C++ exceptions all clear the exact target handle, disarm the one-shot gate, notify waiters, and forward a deferred matching release. Every managed test wraps a paused call in `try/finally`; that finally captures any `ResumePromiseCall()` error, then independently awaits/captures the operation outcome, then independently drains/captures the executor outcome. No assertion runs until all three cleanup actions have been attempted. No test may leave the gate armed or skip await/drain after an assertion, operation, or resume failure.

- [ ] **Step 2: Write lease-race and reentrant-disposal tests**

Add a theory over AsValue, Resolve, and Reject:

```text
DisposeDuringBlockedPromiseCallDefersNativeRelease(operation)
  create a Promise (and settlement value for Resolve/Reject), pause the chosen
  call, launch it through Runtime.Execute, wait until the native wrapper is
  blocked, call Dispose from the test thread, and capture
  `releasedBeforeResume = fixture.Counters.ReleasedPromises` without asserting.
  In unconditional finally, resume/disarm the gate, await and capture the
  operation outcome, and drain the runtime executor. Capture resume, operation,
  and drain exceptions separately so one cleanup failure cannot skip the later
  cleanup actions. Only after all cleanup completes assert that Dispose and the
  operation succeeded, every cleanup exception is null,
  `releasedBeforeResume == 0` (the safe RED against the old wrapper is 1), final
  wrapper releases == 1, and exactly one Promise terminal counter changed.
```

Add:

```text
ResolverReenteringDisposeReturnsWithoutWaitingForItsOwnLease
  resolve with a thenable whose getter invokes a managed host function that
  calls Dispose on the same JavaScriptPromise; record callback entry/return
  and errors, assert after outer Resolve returns that disposal returned and
  wrapper release == 1, then call WaitUntilIdle before asserting Promise
  release/abandonment/remaining == 1/0/0 because entry release is queued.
```

Do not use `Task.Delay` or elapsed time as a correctness assertion. Native gate waits and callback completion are the only ordering signals.

- [ ] **Step 3: Run tests and verify RED against the old managed wrapper**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
```

Expected: FAIL because `Dispose` calls native release while AsValue/Resolve/Reject is blocked, making `ReleasedPromises` equal 1 before resume; reentrant disposal may also release during the active call.

- [ ] **Step 4: Implement non-blocking managed handle leases**

Use one private gate and these exact fields in `JavaScriptPromise`:

```csharp
private readonly object handleGate = new();
private ExpoJsiPromiseHandle handle;
private int activeLeases;
private bool disposeRequested;
```

`AcquireHandle` locks `handleGate` and throws `ObjectDisposedException` when `disposeRequested` or `handle == 0`. While still under the lock, it first allocates a private sealed `HandleLease(this, handle)`, then increments `activeLeases`, then returns the already-created lease. Therefore an allocation failure leaves `activeLeases` unchanged. If implementation performs the increment before allocation for language/runtime reasons, it must catch allocation failure and decrement under the same lock before rethrowing; never leave a phantom lease. `AsValue` and `Settle` acquire that lease before the native call and dispose it in `finally`/`using`; every native call uses `lease.Handle`, never a second read of the field.

`Dispose` locks the gate, returns if disposal was already requested, marks `disposeRequested = true`, and, only when `activeLeases == 0`, moves `handle` to a local and zeros the field. It unlocks before calling `ReleasePromiseHandle`. It never waits.

`ReleaseLease` decrements the count under the gate. If it is the last lease and disposal is pending, it moves/zeros the handle, unlocks, and calls `ReleasePromiseHandle` exactly once. The private sealed lease uses `Interlocked.Exchange(ref owner, null)` in `Dispose` so copied/disposed references cannot release twice. Preserve the current order in `Settle`: disposed check through lease acquisition precedes `ArgumentNullException.ThrowIfNull(value)`.

- [ ] **Step 5: Verify the lease slice before adding forwarding coverage**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
git diff --check
```

Expected: PASS; all three operations report zero native wrapper releases while blocked, one afterward, and reentrant disposal returns without deadlock.

#### Task 3B: Off-runtime forwarding, late disposal, and async ordering regression coverage

**Files:**

- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPromiseTests.cs`

**Interfaces:**

- Consumes: existing `released_promises` and `released_promises_off_runtime_thread` wrapper-call observations.
- Consumes: Task 1 `LongLivedPromisesReleased`, `LongLivedPromisesAbandoned`, and `LongLivedObjectsRemaining` counters.
- Preserves: `JavaScriptPromiseScheduler` code and scheduled callback disposal order without edits.

- [ ] **Step 1: Add the off-runtime and final exactly-once tests**

Add these named tests:

```text
OffRuntimeDisposeAlwaysForwardsAndTerminatesTheEntry
  create a Promise inside Runtime.Execute, dispose it on the test thread,
  wait for the runtime executor to drain, then assert wrapper releases == 1,
  off-runtime observations == 1, Promise released/abandoned == 1/0,
  Remaining == 0.

DoubleDisposeAfterSettlementCountsOneTerminalOutcome
  resolve, dispose twice, drain, assert wrapper releases == 1 and Promise
  released/abandoned/remaining == 1/0/0.

LateDisposeAfterPreparedTeardownDoesNotCountAgain
  create and settle, PrepareRuntimeForInvalidation, capture 1/0/0, dispose
  twice, and assert the Promise counters and remaining count are unchanged.

LateDisposeAfterAbruptTeardownDoesNotCountAgain
  create and settle, ReleaseBridgeRuntimeHandle, capture 0/1/0,
  dispose twice, and assert the Promise counters remain unchanged.
```

Strengthen `CreatePromiseFromManagedTaskReleasesCapabilityOnRuntimeThread` to wait for and assert `ReleasedPromises >= 1`, `ReleasedPromisesOffRuntimeThread == 0`, `LongLivedPromisesReleased == 1`, `LongLivedPromisesAbandoned == 0`, and `LongLivedObjectsRemaining == 0`. Strengthen `DroppedSettlementAbandonsOwnedResult` by releasing the bridge runtime handle after the dropped task and asserting one Promise abandonment, zero Promise releases, and zero remaining entries. Do not edit `JavaScriptPromiseScheduler`.

- [ ] **Step 2: Run tests and verify RED**

Run:

```sh
scripts/test-managed.sh --filter FullyQualifiedName~Expo.JSI.Tests.Runtime.JavaScriptPromiseTests
```

Expected: `OffRuntimeDisposeAlwaysForwardsAndTerminatesTheEntry` FAILS with a remaining entry or missing terminal counter because `countedReleasePromise` returns early after recording the off-runtime observation.

- [ ] **Step 3: Always forward counted Promise releases**

Change `countedReleasePromise` so its catch block increments `released_promises_off_runtime_thread` but does not return. After observation and any explicitly configured Task 3 gate handling, always call:

```cpp
const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
api->release_promise(runtime, promise);
```

Do not require `connector.runtime()` in the underlying bridge release. `releasePromise` only deletes the opaque wrapper; `PromiseHandle::~PromiseHandle()` requests collection removal through `RuntimeState`, whose executor decides runtime release versus abandonment.

- [ ] **Step 4: Run the complete managed suite and commit Task 3**

Run:

```sh
scripts/test-managed.sh
git diff --check
```

Expected: exit 0; all existing Promise/scheduler tests remain unchanged except the explicit counter assertions; all new adversarial tests pass.

Commit:

```sh
git commit -m "fix(jsi): lease and safely release promise handles"
```

---

### Task 4: Merge the accepted delta, archive transient artifacts, and close the plan

**Files:**

- Modify: `docs/specs/promises.md`
- Modify: `docs/plans/README.md` (Plan 015 row and execution note only)
- Move: `docs/changes/2026-07-21-promise-long-lived-state/spec.md` to `docs/archive/changes/2026-07-21-promise-long-lived-state/spec.md`
- Move: `docs/changes/2026-07-21-promise-long-lived-state/plan.md` to `docs/archive/changes/2026-07-21-promise-long-lived-state/plan.md`

**Interfaces:**

- Consumes: accepted requirements in the delta spec and verified behavior from Tasks 1–3.
- Produces: `docs/specs/promises.md` as the sole authoritative current-state Promise specification.
- Produces: Plan 015 status `DONE`; does not alter Plan 016.

- [ ] **Step 1: Merge every accepted requirement into the living spec**

Replace the deferral sentence under `Promise Settlement Can Abandon Owned Results` and merge these requirement groups from the delta without weakening SHALL language:

```text
Runtime-Owned Promise Capability State
Terminal Promise Registration
Promise Settlement State
Managed Promise Handle Leasing
Exactly-Once Promise Entry Lifetime Termination
Off-Runtime Promise Disposal
Promise Entry Accounting
Async Managed Promise Helper (modified scheduling-preservation scenarios)
```

Describe only implemented current behavior. If implementation diverged from the accepted delta, stop for operator approval instead of editing the requirement to match an unapproved design.

- [ ] **Step 2: Archive the completed change and update only Plan 015 status**

Run:

```sh
git mv docs/changes/2026-07-21-promise-long-lived-state docs/archive/changes/2026-07-21-promise-long-lived-state
```

Change the Plan 015 index row to `DONE — Promise capability state is runtime-owned, settlement/teardown races are covered, and managed opaque handles are leased.` Add one execution note naming the four focused commits. Leave Plan 016 and every other row/note unchanged.

- [ ] **Step 3: Run final verification**

Run in this order:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
grep -n "intentionally deferred" docs/specs/promises.md
rg "promise\.release\(\)" packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp
rg "\.AsValue\(\)\.(AsObject|AsArray|AsFunction)\(" packages/expo-modules-dotnet
git status --short
```

Expected:

- managed suite exits 0 with no skipped failures;
- formatting and diff checks exit 0;
- the deferral grep returns no matches;
- no unregistered raw Promise ownership transfer remains;
- no new hidden owned-wrapper conversion chain exists;
- status contains only the intended implementation/spec/archive/index changes.

If formatting fails because files need updates, run `scripts/format.sh`, inspect only formatter-owned changes, then rerun `scripts/format.sh --check --all` and the managed suite.

- [ ] **Step 4: Commit the living-spec merge and archive**

Stage only the living spec, archived change folder, and Plan 015 index update. Run the privacy scan, then commit:

```sh
git commit -m "docs(promises): record runtime-owned capability lifetime"
```

- [ ] **Step 5: Final review gate**

Dispatch a fresh spec reviewer, then a fresh code-quality reviewer. Require both to verify: no entry lock across JavaScript; no JSI destruction while a resolver is active; `PromiseEntry` release/abandon does not re-enter the collection or user JavaScript; collection callback lock discipline and non-Promise callers are unchanged; failed resolver retry occurs only without pending cleanup; terminal registration cannot occur after sweep; registration-race success disposes inside its original `Runtime.Execute` callback; allocation rollback breaks the entry/state cycle; no resolver deletes `RuntimeHandle`; preparation/invalidation resolver tests dispose inside their original `Runtime.Execute` callback and release the bridge handle only afterward; invalidation callbacks allocate their return value before invalidation; every gate targets only its intended runtime/attempt or exact Promise handle and disarms in `finally`/RAII cleanup; blocked-call tests capture pre-resume observations without asserting and always resume, await, and drain before assertions; handle-lease allocation cannot leave a phantom count; managed disposal never releases during a lease; reentrant-dispose terminal counters are checked only after executor drain; off-runtime release always forwards; settlement never changes terminal counters; and late disposal never counts twice.
