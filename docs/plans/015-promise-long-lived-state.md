# Plan 015: Migrate Promise capability state onto the runtime-owned long-lived collection

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat 0e8060f8..HEAD -- packages/expo-modules-dotnet/native/packages/jsi packages/expo-modules-dotnet/managed/packages/Expo.JSI packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests docs/specs/promises.md`
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
- **Revised at**: commit `0e8060f8`, 2026-07-21 — anchors re-verified against
  live code, retry-review findings folded into steps 1/3/4, scope corrected
- **Execution status**: READY for retry on 2026-07-21; the prior partial
  implementation was fully rolled back and this revision incorporates the
  complete retry-review lifecycle and race findings.

## Retry-review findings (2026-07-21)

These findings combine the first execution review with the revised, approved
delta spec. They are verified against live code. Steps 1, 3, and 4 below are
the executable instructions; this section records why those instructions are
required. Do not reuse the rolled-back partial implementation as an
assumed-good baseline:

1. `global.Promise` construction can execute arbitrary JavaScript before the
   capability entry is registered. If that code re-enters runtime teardown,
   the collection can be swept and invalidated before `add`. Registration
   SHALL fail after the collection/runtime becomes terminal, and a failed or
   throwing registration/handle allocation SHALL roll back without retaining
   an entry-to-runtime cycle.
2. Settlement does not terminate entry lifetime. Successful settlement SHALL
   clear the resolver functions but retain the Promise object and collection
   entry until explicit disposal or runtime teardown. `AsValue()` SHALL remain
   valid after settlement, and settlement SHALL NOT change terminal counters.
3. Settlement SHALL use Active, Settling, and Settled states independently of
   lifetime-terminal and pending-cleanup state. Resolver calls SHALL run
   without holding an entry mutex. A thenable getter can synchronously re-enter
   the same capability; re-entry while Settling is a no-op and cannot deadlock.
   If a resolver throws without pending cleanup, state returns to Active so a
   later settlement may retry.
4. Release or abandonment that arrives while a resolver is executing SHALL
   become pending. JSI state stays intact until the resolver unwinds, then the
   selected terminal cleanup runs exactly once outside entry locks, whether
   the resolver returned or threw.
5. `JavaScriptPromise` currently reads and clears its opaque native handle
   without protecting in-flight native calls. Internal managed handle leases
   SHALL prevent Dispose/Resolve/Reject/AsValue use-after-free. Reentrant
   disposal from resolver execution SHALL mark release pending and return
   without waiting for its own active lease.
6. The testhost `countedReleasePromise` currently returns early when
   `connector.runtime()` is unavailable. It SHALL record that observation and
   always forward release to the native API so `RuntimeState` can arrange
   release or abandonment.
7. Promise-entry release and abandonment counters SHALL be exposed through the
   native testhost and both managed test fixtures. Counters change only when
   entry lifetime actually terminates, never on settlement or when cleanup is
   merely requested or pending. Tests SHALL prove exactly-once accounting,
   including late disposal.
8. The retry SHALL include a user-controlled `Promise` constructor that
   re-enters runtime preparation. Creation must fail with zero remaining
   entries. Thenable re-entry tests SHALL record callback outcomes and assert
   them after the outer resolve returns so Promise rejection cannot swallow
   failed test assertions.

## Why this matters

Every other retained-across-calls native state in the bridge (ArrayBuffer
entries, weak-object entries) lives in the runtime-owned
`LongLivedObjectCollection`, so runtime teardown can drain it
deterministically and tests can count leaks. Promise capabilities are the
exception: `createPromise` heap-allocates a `PromiseHandle` holding raw
`jsi::Object`/`jsi::Function` wrappers and hands the raw pointer across the
ABI. If managed code never disposes a promise, whether settled or unresolved
(author bug, torn runtime, crash between create and disposal), that JSI state
outlives its accounting and can be destroyed after the runtime — the same bug
family as the plan-009 Windows teardown crash. `docs/specs/promises.md`
explicitly defers this migration to "a focused follow-up"; this is that
follow-up. Settlement scheduling semantics must NOT change.

## Current state

(Line numbers verified at `0e8060f8`.)

- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
  - `:114-166` — `PromiseHandle`: static `owned(jsi::Object promise, ...)`
    factory returning `std::unique_ptr<PromiseHandle>`; members
    `std::unique_ptr<jsi::Object> promise_`, resolve/reject
    `std::optional<jsi::Function>`s, and an unsynchronized `settled_` bool. It
    has neither distinct Active/Settling/Settled state nor lifetime-pending
    cleanup state, and it is NOT a `LongLivedObject`. NOTE: it lives in
    `ExpoJsiBridge.cpp`, not `RuntimeState.h` — an earlier revision of this plan
    cited the wrong file.
  - `:613-619` — `makePromiseResult(std::unique_ptr<PromiseHandle> promise)`
    returns `expo_jsi_promise_result{..., promise.release(), ...}` — raw
    ownership crosses the ABI with no runtime-side registration.
  - `:1719+` — `createPromise(expo_jsi_runtime_handle runtime)` constructs the
    JS `Promise` via `jsRuntime.global().getPropertyAsFunction(jsRuntime,
    "Promise")` + `callAsConstructor` — this executes user-replaceable JS
    (retry-review finding 1) — captures resolve/reject, wraps in
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
  This is the gap behind retry-review finding 1.
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
    — sealed disposable wrapper. `AsValue` and `Settle` check `handle` and then
    read it again for the native call, while `Dispose()` independently clears
    and releases it with `Interlocked.Exchange`. There is no in-flight handle
    lease, so concurrent or resolver-reentrant disposal can release the opaque
    handle while `AsValue`, `Resolve`, or `Reject` is still using it.
  - `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
    — `CreatePromise` entry point.
  - The async settlement path (`JavaScriptPromiseScheduler`) disposes the
    capability inside the scheduled runtime callback — that ordering was the
    plan-009 crash fix. Do not change it.
- `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
  - `countedReleasePromise` increments the wrapper-release observation, then
    returns early when `connector.runtime()` throws. The early return prevents
    the underlying `release_promise` call and leaks the opaque handle on the
    exact off-runtime path this migration must support.
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
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptPromise.cs`
  — add internal opaque-handle leasing; the public `JavaScriptPromise` API
  surface must not change
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

- Work on the operator's current branch. Do not create a worktree or switch
  branches for this retry.
- Commit per step. Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-promise-long-lived-state/spec.md` in the
GIVEN/WHEN/THEN SHALL style of `docs/specs/promises.md`, covering:

1. A created promise capability SHALL be registered in the runtime-owned
   long-lived collection until explicit disposal or runtime teardown.
   Settlement SHALL NOT remove it.
2. Successful settlement SHALL clear both resolver functions, retain the
   Promise object and collection entry, leave `AsValue()` usable, and change no
   lifetime-terminal counters. A settled entry remains in the collection's
   remaining count until disposal or teardown.
3. Settlement SHALL have Active, Settling, and Settled states separate from
   lifetime-terminal/pending-cleanup state. Re-entry during Settling is a
   no-op. Resolver failure restores Active when no cleanup is pending, so a
   later attempt may retry.
4. Release or abandonment during Settling SHALL defer JSI cleanup until the
   resolver unwinds. The selected terminal action then runs exactly once
   outside entry locks, including when the resolver throws; late disposal is
   a no-op.
5. Runtime teardown SHALL drain settled and unresolved Promise entries with
   the other long-lived entries. Settlement, disposal, and teardown races
   SHALL not double-free, destroy state in use, or leave an entry registered.
6. Settlement scheduling and the scheduled-callback disposal ordering SHALL
   be unchanged.
7. Registration SHALL fail once the collection is terminal (post-sweep or
   post-invalidation); `createPromise` SHALL then fail with zero remaining
   entries, including when a user-replaced `Promise` constructor re-enters
   runtime teardown during creation.
8. A failed or throwing registration/handle allocation SHALL roll back
   without retaining an entry-to-runtime reference cycle.
9. Resolver calls SHALL run without holding an entry lock; synchronous
   re-entry from a thenable getter SHALL not deadlock or corrupt entry state.
10. `JavaScriptPromise` SHALL lease its opaque native handle internally for
    `AsValue`, `Resolve`, and `Reject`. Disposal rejects new leases and
    forwards release once after existing leases exit; reentrant disposal does
    not block on its own lease.
11. Off-runtime managed disposal SHALL always forward the native release
    request. Promise-entry release and abandonment SHALL be observable through
    dedicated counters in the native testhost and both managed fixtures.
    Counters change only when lifetime cleanup actually terminates the entry,
    not on settlement, release request, or pending cleanup.

Present to the operator for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–5 to commits.
**Verify**: committed.

### Step 3: Native migration and managed handle safety

Target shape, mirroring the weak-object split:

1. **Entry**: a `PromiseEntry final : public LongLivedObject` holding the
   Promise `jsi::Object` and optional resolve/reject `jsi::Function`s. Keep
   settlement state (`Active`, `Settling`, `Settled`) separate from lifetime
   cleanup state (no request, release/abandon pending, terminal). Protect
   compound transitions with an entry mutex, but never hold that mutex while
   invoking JavaScript or while destroying JSI state.
2. **Settlement protocol**: under the entry mutex, only Active may claim a
   resolver and transition to Settling. A concurrent or reentrant settlement
   that observes Settling or Settled is a no-op. Invoke the selected resolver
   with no entry lock held. On success, clear both resolvers and transition to
   Settled while retaining the Promise object. On failure, restore Active and
   retain both resolvers if no cleanup is pending, then rethrow with existing
   error semantics so a later settlement may retry. Settlement never removes
   the collection entry and never increments a lifetime counter.
3. **Lifetime cleanup protocol**: release and abandonment compete for one
   lifetime-terminal transition independently of settlement. If cleanup
   reaches an Active or Settled entry, claim and clear its JSI state once. If
   it reaches a Settling entry, record the selected cleanup as pending and
   return without clearing state used by the resolver. After the resolver
   unwinds, complete pending cleanup exactly once outside the entry mutex,
   whether the resolver returned or threw. Count release or abandonment only
   when that terminal cleanup actually completes. Every operation that may
   outlive collection ownership SHALL retain a strong `PromiseEntry`
   reference until it exits.
4. **ABI handle**: `expo_jsi_promise_t` becomes a wrapper
   `{shared_ptr<RuntimeState>, shared_ptr<PromiseEntry>, entryId}` like
   `WeakObjectHandle` (`WeakObjectHandles.h:55-85`). The opaque pointer
   typedef in `expo_jsi.h` (`native/include/expo_jsi.h:28`) is unchanged —
   no ABI signature changes are needed. `releasePromise` still deletes the
   wrapper; the wrapper destructor calls
   `state_->releaseLongLivedObject(entryId_)`, which is a no-op if teardown
   already removed the entry. `promiseAsValue` reads the retained Promise
   object from the entry and therefore remains valid after settlement.
5. **Managed opaque-handle leases**: change only the internals of
   `JavaScriptPromise`. Acquire a lease under one synchronization gate before
   `AsValue`, `Resolve`, or `Reject` reads the native handle; release it in a
   `finally` after the native call. `Dispose` atomically rejects new leases and
   forwards `ReleasePromiseHandle` once, immediately if no operation is in
   flight or when the last lease exits. Make the native call outside the
   managed synchronization gate. Disposal during its own resolver call SHALL
   mark release pending and return without waiting, preventing both deadlock
   and handle use-after-free. Preserve the public API and existing disposed
   exception behavior.
6. **Failable registration**: add a terminal flag to
   `LongLivedObjectCollection`, set inside `sweep` and
   `invalidateWithoutRuntime` (under `mutex_`), and a new failable
   `tryAdd(std::shared_ptr<LongLivedObject>)` that returns 0 (or
   `std::nullopt`) once terminal. Only the promise path uses `tryAdd`. Do NOT
   change the existing `add()` contract — `WeakObjectHandles.h:93` and
   `ArrayBufferHandles.h` call it unchecked and are out of scope.
7. **Registration ordering in `createPromise`** (`ExpoJsiBridge.cpp:1719+`):
   the `global.Promise` lookup + `callAsConstructor` run user-replaceable JS
   that can re-enter teardown, so register the entry AFTER the constructor
   returns, via `tryAdd`; if `tryAdd` fails or wrapper allocation throws,
   destroy the entry immediately (it holds a `shared_ptr<RuntimeState>` — a
   leaked entry is a permanent cycle) and return a promise error result.
8. **Off-runtime release forwarding**: update native testhost
   `countedReleasePromise` to record both its wrapper-release call and an
   unavailable `connector.runtime()` observation, but never return before
   calling the underlying `release_promise`. Native release only deletes the
   ABI wrapper and requests collection removal through `RuntimeState` and its
   executor; it SHALL NOT synchronously destroy JSI state on the disposing
   thread.
9. **Counters**: extend `RuntimeLongLivedCounters`
   (`ExpoJsiBridgeTestHooks.h:11-26`) with `promisesReleased` /
   `promisesAbandoned`, backed by `RuntimeState` counters following the
   weak-object pattern, and wire them through `getRuntimeLongLivedCounters`,
   `resetRuntimeLongLivedCounters`,
   `releaseRuntimeHandleAndGetLongLivedCounters` (`ExpoJsiBridge.cpp:2761+`),
   the native testhost, and both managed `NativeTestHost.cs` fixtures. Do not
   increment these entry counters on settlement, wrapper deletion, release
   request, or pending cleanup; only the actual lifetime-terminal release or
   abandonment changes them.

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
`Runtime/JavaScriptPromiseTests.cs`). Add deterministic coverage for all of the
following:

1. An unresolved Promise reaches one release at runtime teardown; invalidation
   without runtime access reaches one abandonment.
2. Successful resolve and reject leave the entry registered, leave both
   terminal counters unchanged, and keep `AsValue()` valid. Disposal or
   teardown afterward removes the entry and increments exactly one terminal
   counter.
3. Double disposal and disposal after teardown do not change counters twice.
   Existing async settlement still disposes inside the scheduled callback.
4. A custom Promise resolver that throws restores Active state when no cleanup
   is pending; a later settlement attempt invokes it again and can succeed.
5. A thenable getter synchronously re-entering the same capability observes a
   no-op instead of deadlocking or invoking a resolver twice. The callback
   records invocation counts and outcomes for assertions after the outer
   resolve returns; do not throw test assertions inside Promise callbacks,
   because Promise machinery may convert them into rejection.
6. Coordinated teardown entered while a resolver is in Settling state leaves
   the resolver's JSI state usable until it returns, then completes one pending
   release or abandonment. Cover the variant where the resolver throws after
   cleanup becomes pending. Assert no deadlock, no crash, no remaining entry,
   and exactly one terminal counter change.
7. A user-controlled `Promise` constructor re-enters coordinated runtime
   preparation before registration. Creation fails, no entry remains, and no
   terminal counter is fabricated for an entry that was never registered.
8. Managed `Dispose` racing a deterministically gated in-flight `AsValue`,
   `Resolve`, or `Reject` cannot release the opaque handle early. A resolver
   that synchronously calls `Dispose` on its own `JavaScriptPromise` returns
   without blocking; the last lease exit forwards native release exactly once.
   Add only the minimum testhost synchronization hook needed to make the race
   repeatable; do not use sleeps as correctness assertions.
9. Off-runtime `Dispose` increments the existing wrapper-release and
   off-runtime observations, still forwards `release_promise`, and eventually
   produces exactly one Promise-entry release or abandonment with zero
   remaining entries.

Counter assertions: entry release after `Dispose()` is deferred to the
runtime executor (see step 3), so assert final counts via
`releaseRuntimeHandleAndGetLongLivedCounters` (or after a drain), never
immediately after dispose. Treat the existing wrapper-release counter as a
call observation only; assert entry release/abandonment with the new dedicated
long-lived counters. Settlement, release request, and pending cleanup must not
change those entry counters.

**Verify**: `scripts/test-managed.sh` → exit 0 including the new tests.

### Step 5: Spec merge and archive

Merge the delta into `docs/specs/promises.md` (replace the `:81-82` deferral
note with the new requirement). Archive the change folder per the
living-spec skill. Run formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0;
`grep -n "intentionally deferred" docs/specs/promises.md` → no matches.

## Test plan

- New `Expo.JSI.Tests` cases (step 4): settled-entry retention and post-settle
  `AsValue`; terminal-only release/abandon accounting; unresolved teardown;
  idempotent late disposal; resolver-throw retry; thenable re-entry; teardown
  during Settling, including resolver failure; terminal constructor re-entry;
  managed handle lease races and reentrant disposal; off-runtime forwarding.
- Race tests use deterministic testhost gates or synchronous re-entry, not
  timing sleeps. Assertions made from Promise callbacks are recorded and
  checked after the outer call returns.
- All existing promise and scheduler tests pass unmodified — they are the
  guard that settlement semantics did not change.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0; new promise long-lived tests exist
      and prove settled entries remain registered and convertible with
      `AsValue()` until disposal/teardown
- [ ] Tests prove Active/Settling/Settled behavior, resolver-throw retry,
      no-op reentrant settlement, deferred cleanup during Settling, and one
      lifetime-terminal counter change
- [ ] Tests prove managed opaque-handle leases prevent early native release,
      reentrant `Dispose()` does not block, and off-runtime
      `countedReleasePromise` forwards release
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
- Terminal Promise registration appears to require changing the existing `add()`
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
- Keep settlement and lifetime termination as separate concepts in future
  edits. Adding another post-settlement operation must preserve the retained
  Promise object; adding another cleanup source must join the existing
  lifetime-terminal arbitration instead of treating Settled as terminal.
- Reviewer should scrutinize: no entry mutex held across JavaScript calls; no
  JSI state cleared while a resolver is active; resolver failure restores
  Active only when cleanup is not pending; no managed native-handle release
  while a lease is active; `countedReleasePromise` never drops forwarding; and
  counters change only when release or abandonment actually completes.
- Deferred deliberately: any promise API surface change; scheduler priority
  work (P3).
