# Plan 015: Migrate Promise capability state onto the runtime-owned long-lived collection

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat ea07d69d..HEAD -- packages/expo-modules-dotnet/native/packages/jsi packages/expo-modules-dotnet/managed/packages/Expo.JSI packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests docs/specs/promises.md`
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
- **Execution status**: BLOCKED on 2026-07-21 after two review rounds; partial
  implementation fully rolled back

## Blocked execution findings (2026-07-21)

Retry only after incorporating all of these requirements; do not reuse the
rolled-back partial implementation as an assumed-good baseline:

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

(Line numbers at `ea07d69d`.)

- `packages/expo-modules-dotnet/native/packages/jsi/src/RuntimeState.h`
  - `:116-161` — `PromiseHandle`: static `owned(jsi::Object promise, ...)`
    factory returning `std::unique_ptr<PromiseHandle>`; members
    `std::unique_ptr<jsi::Object> promise_` plus resolve/reject
    `jsi::Function`s. It is NOT a `LongLivedObject`.
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
  - `:613-619` — `makePromiseResult(std::unique_ptr<PromiseHandle> promise)`
    returns `expo_jsi_promise_result{..., promise.release(), ...}` — raw
    ownership crosses the ABI with no runtime-side registration.
  - `:1719+` — `createPromise(expo_jsi_runtime_handle runtime)` constructs the
    JS `Promise`, captures resolve/reject, wraps in `PromiseHandle::owned`.
- The pattern to follow —
  `packages/expo-modules-dotnet/native/packages/jsi/src/WeakObjectHandles.h`:
  - `:13` — `class WeakObjectEntry final : public LongLivedObject`
  - `:93` — `auto entryId = state->longLivedObjects().add(entry);`
  - `:68` — release path calls `state_->releaseLongLivedObject(entryId_)`.
  `ArrayBufferHandles.h:65` (`ArrayBufferEntry`) is the second exemplar.
- `packages/expo-modules-dotnet/native/packages/jsi/src/LongLivedObjectCollection.{h,cpp}`
  — the runtime-owned collection itself.
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridgeTestHooks.h`
  - `:11-25` — `RuntimeLongLivedCounters`, `getRuntimeLongLivedCounters`,
    `resetRuntimeLongLivedCounters`,
    `releaseRuntimeHandleAndGetLongLivedCounters` — test hooks used by managed
    leak-accounting tests; extend these for promise entries.
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

- `packages/expo-modules-dotnet/native/packages/jsi/src/` — `RuntimeState.h`,
  `ExpoJsiBridge.cpp`, `LongLivedObjectCollection.{h,cpp}` (if a new entry
  kind needs it), `ExpoJsiBridgeTestHooks.h` (+ its .cpp if counters grow),
  new `PromiseHandles.h` if you mirror the ArrayBuffer/WeakObject file split
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
- ArrayBuffer and WeakObject entries — pattern sources only.

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

Present to the operator for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–5 to commits.
**Verify**: committed.

### Step 3: Native migration

Make the promise capability a long-lived entry following `WeakObjectEntry`:
an entry class deriving `LongLivedObject` holding the promise object and
resolve/reject functions; `createPromise` adds it via
`state->longLivedObjects().add(...)`; the ABI handle becomes/wraps the entry
id or entry pointer consistent with how ArrayBuffer/WeakObject handles work.
Settle and release paths remove the entry (idempotent). Extend
`RuntimeLongLivedCounters` with promise-entry counters and expose them through
the native testhost and managed fixtures. Make collection registration reject
post-sweep/post-invalidation additions, and roll registration back if handle
allocation fails. Do not hold an entry lock across a resolver call because
thenable resolution can synchronously re-enter managed code.

**Verify**: `scripts/test-managed.sh` → exit 0 (existing promise tests pass
unchanged).

### Step 4: Managed leak-accounting tests

In `Expo.JSI.Tests`, model after the existing weak-object/ArrayBuffer counter
tests (see `Runtime/JavaScriptWeakObjectTests.cs` and
`Runtime/JavaScriptArrayBufferTests.cs`): unresolved promise released at
runtime teardown (counter returns to baseline); settled promise removes its
entry; double dispose is a no-op; async settlement still disposes inside the
scheduled callback (existing scheduler tests keep passing). Add constructor
re-entry coverage that prepares teardown from a user-controlled `Promise`
constructor and asserts failed creation leaves no entry. Add thenable
re-entry coverage whose callback records outcomes for assertions after the
outer resolution, rather than throwing assertions that Promise resolution can
convert into rejection.

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

- The migration requires changing any `expo_jsi.h` ABI signature or adding
  ABI entries.
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
