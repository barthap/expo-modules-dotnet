# Plan 015: Migrate Promise capability state onto the runtime-owned long-lived collection

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat 6db8167c..HEAD -- packages/expo-modules-dotnet/native/packages/jsi packages/expo-modules-dotnet/managed/packages/Expo.JSI packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests docs/specs/promises.md`
> If any in-scope file changed since this plan was written, compare the
> "Current state" excerpts against the live code before proceeding; on a
> mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (touches teardown ordering in shared native bridge)
- **Depends on**: none
- **Category**: tech-debt
- **Planned at**: commit `ea07d69d`, 2026-07-20
- **Revised at**: commit `6db8167c`, 2026-07-21 — anchors re-verified against
  live code, blocked findings folded into steps 1/3/4, scope corrected
- **Execution status**: READY for retry on 2026-07-21; the prior partial
  implementation was fully rolled back and this revision incorporates all
  four blocking review findings.

## Blocked execution findings (2026-07-21)

These are the review-round requirements that blocked the first attempt. All
four are verified real against live code. Steps 1, 3 and 4 below now
incorporate them — this section is the record, the steps are the instructions.
Do not reuse the rolled-back partial implementation as an assumed-good
baseline:

1. `global.Promise` construction can execute arbitrary JavaScript before the
   capability entry is registered. If that code re-enters runtime teardown,
   the collection can be swept and invalidated before `add`. Registration
   SHALL fail after the collection/runtime becomes terminal, and a failed or
   throwing registration/handle allocation SHALL roll back without retaining
   an entry-to-runtime cycle.
2. Resolver calls SHALL run without holding an entry mutex. A thenable getter
   can synchronously re-enter the same capability; tests SHALL record callback
   outcomes and assert them after the outer resolve returns so Promise
   rejection cannot swallow failed test assertions.
3. Promise-entry release and abandonment counters SHALL be exposed through the
   native testhost and both managed test fixtures. Tests SHALL distinguish
   on-runtime release from abandonment and prove the terminal action occurs
   exactly once, including after late disposal.
4. The retry SHALL include a user-controlled `Promise` constructor that
   re-enters runtime preparation. Creation must fail with zero remaining
   entries.

## Why this matters

Every other retained-across-calls native state in the bridge (ArrayBuffer
entries, weak-object entries) lives in the runtime-owned
`LongLivedObjectCollection`, so runtime teardown can drain it
deterministically and tests can count leaks. Promise capabilities are the
exception: `createPromise` heap-allocates a `PromiseHandle` holding raw
`jsi::Object`/`jsi::Function` wrappers and hands the raw pointer across the
ABI. If managed code never settles or disposes a promise (author bug, torn
runtime, crash between create and settle), that JSI state outlives its
accounting and can be destroyed after the runtime — the same bug family as
the plan-009 Windows teardown crash. `docs/specs/promises.md` explicitly
defers this migration to "a focused follow-up"; this is that follow-up.
Settlement scheduling semantics must NOT change.

## Current state

(Line numbers verified at `6db8167c`.)

- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
  - `:114-166` — `PromiseHandle`: static `owned(jsi::Object promise, ...)`
    factory returning `std::unique_ptr<PromiseHandle>`; members
    `std::unique_ptr<jsi::Object> promise_`, resolve/reject
    `std::optional<jsi::Function>`s, and a `settled_` bool. It is NOT a
    `LongLivedObject`. NOTE: it lives in `ExpoJsiBridge.cpp`, not
    `RuntimeState.h` — an earlier revision of this plan cited the wrong file.
  - `:613-619` — `makePromiseResult(std::unique_ptr<PromiseHandle> promise)`
    returns `expo_jsi_promise_result{..., promise.release(), ...}` — raw
    ownership crosses the ABI with no runtime-side registration.
  - `:1719+` — `createPromise(expo_jsi_runtime_handle runtime)` constructs the
    JS `Promise` via `jsRuntime.global().getPropertyAsFunction(jsRuntime,
    "Promise")` + `callAsConstructor` — this executes user-replaceable JS
    (blocked finding 1) — captures resolve/reject, wraps in
    `PromiseHandle::owned`.
  - `:2602-2604` — `releasePromise(expo_jsi_runtime_handle, ...)` is a raw
    `delete promise;` — synchronous, thread-agnostic, and it ignores the
    runtime handle entirely. After the migration, release routes through the
    collection's `requestRelease` → `executor.executeAsync` path and becomes
    DEFERRED. This is an intended observable change; see steps 3 and 4.
  - `:2761+` — implementations of the test hooks declared in
    `ExpoJsiBridgeTestHooks.h`. There is no `ExpoJsiBridgeTestHooks.cpp`;
    counter implementations go here.
- The pattern to follow —
  `packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectHandles.h`:
  - `:13` — `class WeakObjectEntry final : public LongLivedObject` with
    `std::atomic<bool> terminal_` guarding exactly-once release/abandon.
  - `:93` — `auto entryId = state->longLivedObjects().add(entry);`
  - `:68` — `WeakObjectHandle` destructor calls
    `state_->releaseLongLivedObject(entryId_)`; the ABI handle is a wrapper
    `{shared_ptr<RuntimeState>, shared_ptr<WeakObjectEntry>, entryId}`, NOT
    the entry itself.
  `ArrayBufferHandles.h:65` (`ArrayBufferEntry`) is the second exemplar.
- `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.{h,cpp}`
  — the runtime-owned collection. `add()`
  (`LongLivedObjectCollection.cpp:10-19`) only null-checks; it has NO terminal
  state — after `sweep` or `invalidateWithoutRuntime` erase the entries, a
  later `add()` still succeeds silently and the new entry is never drained.
  This is the gap behind blocked finding 1.
- `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.{h,cpp}`
  — owns the collection; holds the per-kind counters
  (`noteWeakObjectReleased`/`noteWeakObjectAbandoned`,
  `noteArrayBufferReleased`/`noteArrayBufferAbandoned`, matching getters and
  resets). Promise counters follow this exact pattern.
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridgeTestHooks.h`
  - `:11-26` — `RuntimeLongLivedCounters` (fields `arrayBuffersReleased`,
    `arrayBuffersAbandoned`, `weakObjectsReleased`, `weakObjectsAbandoned`,
    `remaining`), `getRuntimeLongLivedCounters`,
    `resetRuntimeLongLivedCounters`,
    `releaseRuntimeHandleAndGetLongLivedCounters` — extend the struct and all
    three functions with promise-entry counters.
- Managed side:
  - `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromise.cs`
    — sealed disposable wrapper; `Dispose()` releases the native capability;
    `ThrowIfDisposed` checks `handle == 0`.
  - `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
    — `CreatePromise` entry point.
  - The async settlement path (`JavaScriptPromiseScheduler`) disposes the
    capability inside the scheduled runtime callback — that ordering was the
    plan-009 crash fix. Do not change it.
- Spec: `docs/specs/promises.md`
  - `:57-63` — "Async promise capability is released": the scheduler SHALL
    release the native promise capability during the scheduled runtime
    callback, NOT from an arbitrary managed thread.
  - `:81-82` — "Promise capability migration onto the generic runtime-owned
    long-lived collection is intentionally deferred to a focused follow-up."

Repo conventions that apply:

- **Living-spec workflow is mandatory** (AGENTS.md): delta spec at
  `docs/changes/2026-<mm-dd>-promise-long-lived-state/spec.md` → operator
  approval → `plan.md` → implementation with verified commits → merge into
  `docs/specs/promises.md` → archive. Read
  `.agents/skills/living-spec-workflow/SKILL.md` first.
- C++ owns JSI mechanics; do not expose raw `jsi::*` layouts to C#.
- Commit style: conventional-commit-ish, e.g.
  `fix(jsi): register promise capabilities as long-lived state`.
- Never commit absolute local paths, usernames, or machine names.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Managed test suite (rebuilds native testhost) | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` (run `scripts/format.sh` then re-check if it fails) | exit 0 |

## Suggested executor toolkit

- `.agents/skills/living-spec-workflow/SKILL.md` — mandatory workflow.
- Skill `expo-jsi-managed-handle-lifetime` (if available) — ownership and
  teardown pitfalls for exactly this code.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/native/packages/jsi/src/` —
  `RuntimeState.{h,cpp}` (promise counters), `ExpoJsiBridge.cpp`,
  `LongLivedObjectCollection.{h,cpp}` (terminal flag + `tryAdd`, step 3),
  `ExpoJsiBridgeTestHooks.h` (counter struct; implementations live in
  `ExpoJsiBridge.cpp` — there is no separate test-hooks .cpp), new
  `PromiseHandles.h` if you mirror the ArrayBuffer/WeakObject file split
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/` — only if the
  managed wrapper needs an internal change; the public `JavaScriptPromise`
  API surface must not change
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/`
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
- `packages/expo-modules-dotnet/native/testhost/include/expo_jsi_testhost.h`
- `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- `docs/specs/promises.md`
- `docs/changes/2026-<mm-dd>-promise-long-lived-state/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `expo_jsi.h` ABI signatures — this is internal storage migration; the
  existing promise ABI entry points keep their shapes. If a signature must
  change, that is a STOP condition.
- `JavaScriptPromiseScheduler` settlement scheduling / disposal ordering —
  the plan-009 fix. Behavior stays identical.
- `Expo.ModulesCore` generated promise bindings — they sit above this layer.
- ArrayBuffer and WeakObject entries — pattern sources only. In particular,
  `WeakObjectHandles.h:93` and `ArrayBufferHandles.h` call the existing
  `add()` and do not handle registration failure; do NOT change `add()`'s
  contract for them (step 3 adds a separate failable `tryAdd` instead).

## Git workflow

- Branch: `advisor/015-promise-long-lived-state` off `development`.
- Commit per step. Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-promise-long-lived-state/spec.md` in the
GIVEN/WHEN/THEN SHALL style of `docs/specs/promises.md`, covering:

1. A created promise capability SHALL be registered in the runtime-owned
   long-lived collection until settled, disposed, or runtime teardown.
2. Settle/dispose SHALL remove the entry exactly once; later release sources
   SHALL be no-ops (idempotent late disposal).
3. Runtime teardown SHALL drain unresolved promise capabilities with the
   other long-lived entries; a capability released concurrently with teardown
   SHALL not double-free (settlement/teardown race).
4. Settlement scheduling and the scheduled-callback disposal ordering SHALL
   be unchanged.
5. Registration SHALL fail once the collection is terminal (post-sweep or
   post-invalidation); `createPromise` SHALL then fail with zero remaining
   entries, including when a user-replaced `Promise` constructor re-enters
   runtime teardown during creation (blocked findings 1 and 4).
6. A failed or throwing registration/handle allocation SHALL roll back
   without retaining an entry-to-runtime reference cycle (blocked finding 1).
7. Resolver calls SHALL run without holding an entry lock; synchronous
   re-entry from a thenable getter SHALL not deadlock or corrupt entry state
   (blocked finding 2).
8. Promise-entry release and abandonment SHALL be observable through
   dedicated counters, distinguishing on-runtime release from abandonment,
   with the terminal action occurring exactly once (blocked finding 3).

Present to the operator for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–5 to commits.
**Verify**: committed.

### Step 3: Native migration

Target shape, mirroring the weak-object split:

1. **Entry**: a `PromiseEntry final : public LongLivedObject` holding the
   promise `jsi::Object` and resolve/reject `jsi::Function`s, with an
   `std::atomic<bool> terminal_` guarding exactly-once release/abandon like
   `WeakObjectEntry` (`WeakObjectHandles.h:13`). `release(runtime)` destroys
   the JSI state on the runtime path and calls the new
   `RuntimeState::notePromiseReleased()`; `abandon()` drops it without
   touching the runtime and calls `notePromiseAbandoned()`.
2. **ABI handle**: `expo_jsi_promise_t` becomes a wrapper
   `{shared_ptr<RuntimeState>, shared_ptr<PromiseEntry>, entryId}` like
   `WeakObjectHandle` (`WeakObjectHandles.h:55-85`). The opaque pointer
   typedef in `expo_jsi.h` (`native/include/expo_jsi.h:28`) is unchanged —
   no ABI signature changes are needed. `releasePromise` still deletes the
   wrapper; the wrapper destructor calls
   `state_->releaseLongLivedObject(entryId_)`, which is a no-op if the entry
   was already removed by settle or teardown.
3. **Failable registration**: add a terminal flag to
   `LongLivedObjectCollection`, set inside `sweep` and
   `invalidateWithoutRuntime` (under `mutex_`), and a new failable
   `tryAdd(std::shared_ptr<LongLivedObject>)` that returns 0 (or
   `std::nullopt`) once terminal. Only the promise path uses `tryAdd`. Do NOT
   change the existing `add()` contract — `WeakObjectHandles.h:93` and
   `ArrayBufferHandles.h` call it unchecked and are out of scope.
4. **Registration ordering in `createPromise`** (`ExpoJsiBridge.cpp:1719+`):
   the `global.Promise` lookup + `callAsConstructor` run user-replaceable JS
   that can re-enter teardown, so register the entry AFTER the constructor
   returns, via `tryAdd`; if `tryAdd` fails or wrapper allocation throws,
   destroy the entry immediately (it holds a `shared_ptr<RuntimeState>` — a
   leaked entry is a permanent cycle) and return a promise error result.
5. **Settle**: resolve/reject paths remove the entry on the runtime path
   after the resolver call returns. Do not hold any entry lock across the
   `resolve_->call(...)` — a thenable getter can synchronously re-enter the
   same capability; use the `terminal_` atomic exchange pattern for
   exactly-once, not a mutex around the call.
6. **Counters**: extend `RuntimeLongLivedCounters`
   (`ExpoJsiBridgeTestHooks.h:11-26`) with `promisesReleased` /
   `promisesAbandoned`, backed by `RuntimeState` counters following the
   weak-object pattern, and wire them through `getRuntimeLongLivedCounters`,
   `resetRuntimeLongLivedCounters`,
   `releaseRuntimeHandleAndGetLongLivedCounters` (`ExpoJsiBridge.cpp:2761+`),
   the native testhost, and both managed `NativeTestHost.cs` fixtures.

Known observable change: entry release triggered by `Dispose()` is now
deferred (`requestRelease` → `executor.executeAsync`), where the old
`releasePromise` freed synchronously. Do NOT "fix" this by destroying JSI
state synchronously off the runtime path — that reintroduces the bug family
this plan removes. Tests observe final counts via
`releaseRuntimeHandleAndGetLongLivedCounters` or after a drain, not
immediately after dispose.

**Verify**: `scripts/test-managed.sh` → exit 0 (existing promise tests pass
unchanged).

### Step 4: Managed leak-accounting tests

In `Expo.JSI.Tests`, model after the existing weak-object/ArrayBuffer counter
tests (see `Runtime/JavaScriptWeakObjectTests.cs` and
`Runtime/JavaScriptArrayBufferTests.cs`; existing promise coverage lives in
`Runtime/JavaScriptPromiseTests.cs`): unresolved promise released at
runtime teardown (counter returns to baseline); settled promise removes its
entry; double dispose is a no-op; async settlement still disposes inside the
scheduled callback (existing scheduler tests keep passing). Add constructor
re-entry coverage that prepares teardown from a user-controlled `Promise`
constructor and asserts failed creation leaves no entry. Add thenable
re-entry coverage whose callback records outcomes for assertions after the
outer resolution, rather than throwing assertions that Promise resolution can
convert into rejection.

Counter assertions: entry release after `Dispose()` is deferred to the
runtime executor (see step 3), so assert final counts via
`releaseRuntimeHandleAndGetLongLivedCounters` (or after a drain), never
immediately after dispose.

**Verify**: `scripts/test-managed.sh` → exit 0 including the new tests.

### Step 5: Spec merge and archive

Merge the delta into `docs/specs/promises.md` (replace the `:81-82` deferral
note with the new requirement). Archive the change folder per the
living-spec skill. Run formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0;
`grep -n "intentionally deferred" docs/specs/promises.md` → no matches.

## Test plan

- New `Expo.JSI.Tests` cases (step 4): unresolved-promise teardown drain,
  settle-removes-entry, idempotent late disposal, teardown/settlement race
  (settle scheduled, runtime torn down before it runs — no crash, no leak).
- All existing promise and scheduler tests pass unmodified — they are the
  guard that settlement semantics did not change.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0; new promise long-lived tests exist
      and pass
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] `grep -n "promise.release()" packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
      returns no unregistered-ownership transfer (the raw-release pattern is
      gone or now backed by a collection entry)
- [ ] `docs/specs/promises.md` no longer contains the deferral note and
      contains the merged long-lived requirement
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The migration requires changing any `expo_jsi.h`
  (`packages/expo-modules-dotnet/native/include/expo_jsi.h`) ABI signature or
  adding ABI entries. (Wrapping the opaque `expo_jsi_promise_t` contents is
  not an ABI change — see step 3.)
- Meeting blocked finding 1 appears to require changing the existing `add()`
  semantics for the weak-object/ArrayBuffer callers instead of adding the
  separate `tryAdd` from step 3.
- Preserving the scheduled-callback disposal ordering (plan-009 fix) is
  impossible with collection-owned entries.
- Existing promise or scheduler tests fail after the migration and the fix
  would change settlement scheduling semantics.
- The operator rejects the delta spec.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Plan 017 (SharedObject public authoring surface) and any future retained-state
  feature should use the same long-lived pattern; after this plan, promises
  stop being the documented exception.
- Reviewer should scrutinize: exactly-once entry removal under the
  settle/teardown race, and that counters cannot go negative.
- Deferred deliberately: any promise API surface change; scheduler priority
  work (P3).
