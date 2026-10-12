# Plan 019: Typed `[Event]` members on shared objects

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat <017-merge-SHA>..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/example-module docs/specs/modules-core-boundary.md`
> where `<017-merge-SHA>` is the commit that closed plan 017 (find it in
> `docs/plans/README.md` / `git log`). This plan is written BEFORE 017
> executes: file names for the public SharedObject surface below are the
> expected shapes from 017's approved spec — verify each against the real
> post-017 code before relying on it; on a mismatch, treat it as a STOP
> condition. Additionally verify plan 021 is DONE and read its merged
> disposer contract in `docs/specs/` before Step 1 — this plan's cleanup
> design consumes it.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (per-object listener lifetime + dispatch on registry-owned
  entries)
- **Depends on**: docs/plans/017-sharedobject-authoring.md (hard — DONE) and
  docs/plans/021-hostfunction-owned-state-disposal.md (hard — the exactly-once
  owned callback-state disposer is what makes JS-owned subscription cleanup
  possible)
- **Category**: direction / dx
- **Planned at**: commit `ea07d69d`, 2026-07-20; reworked at `f2c72f68`,
  2026-07-22 after the first execution was rolled back
- **Execution status**: COMPLETE

### Execution history

- Blocked 2026-07-22 after review of the shared listener lifetime design;
  implementation commits rolled back in `aaf5b6c9`. Root cause: listeners
  stored in managed `EventEmitterRuntimeState` (owned by
  `DotnetRuntimeContext`) rooted every JS listener closure. A listener that
  captures its own shared object (`counter.addListener('onChange', () =>
  console.log(counter.value))`) then forms `managed context → retained
  listener → JS closure → shared object`, so JS GC can never collect the
  shared object and its release callback never fires. The rolled-back
  commits (`63ed1c55`, `2ca76d20`) are historical reference only — do NOT
  restore them.
- Reworked 2026-07-22: this plan now mandates the JS-owned listener design
  (see "Revised design constraints") and depends on plan 021's disposer
  primitive. Two fixable review findings from the first execution must also
  be addressed: per-emitter event-ID isolation (one instance's listeners
  must be keyed so another instance's dispatch can never reach them) and
  teardown locking on the dispatch path (dispatch racing runtime teardown
  must not touch freed state).

## Revised design constraints (2026-07-22, binding)

The delta spec in Step 1 must comply with all of these; deviating is a STOP
condition:

1. **Listener storage lives in the JS heap, per instance** — e.g. an
   internal slot/property on the JS shared-object instance itself. The
   target/listener reference cycle stays entirely inside the JS heap so
   Hermes GC can collect an unreachable shared object together with
   listeners that capture it. Managed code must NEVER retain a strong
   reference to a JS listener function or to the JS instance outside a
   runtime-thread callback frame.
2. **Managed dispatch retains only the registry entry's
   `JavaScriptWeakObject`** and reacquires the live instance on the runtime
   thread; a dead weak ref means the defined loud lifetime failure.
3. **Subscription cleanup uses plan 021's primitive**: a JS-owned `remove()`
   host function whose callback state owns the weak handle passes the
   exactly-once disposer to `CreateHostFunction`, so JS collecting the
   subscription disposes the weak handle under 021's spec'd thread contract
   (creation failure, GC, and teardown paths all covered by 021).
4. `Symbol.dispose` (explicit resource management) may be offered as
   deterministic user-triggered cleanup ONLY as an addition — it does not
   replace GC-driven cleanup, because callers who never invoke it must still
   not leak.

## Why this matters

Upstream Expo Modules shared objects are event emitters: JS code subscribes
to a specific object instance (a download handle's `onProgress`, a camera
session's `onFrame`), not to the whole module. Plan 017 ships the public
SharedObject authoring surface without events; this plan completes the
feature to parity with both the module-level typed `[Event]` members from
plan 014 and the upstream direction where `SharedObject` derives from
`EventEmitter`. After this lands, a C# shared-object class declares
`[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }` and
awaits dispatch to the listeners of that one JS object — same awaitable
failure-surfacing semantics module events already have. Per the AGENTS.md
"Maturity" rule this ships complete: per-object subscription, dispatch,
diagnostics, teardown, example, TS typing, and docs — no partial surface.

## Current state

(Module-level facts at `ea07d69d`; shared-object facts are 017's expected
output — re-verify per the drift check.)

Module-level typed events (the semantics to mirror), normative spec
`docs/specs/modules-core-boundary.md`:

- `:676-698` — `[Event]` valid only on an instance, getter-only partial
  property of exactly `Func<Task>` or `Func<T, Task>` with an event-safe
  codec; JS name lowercases only the first character; `[Event("name")]`
  preserves explicit names.
- `:719` — off-runtime invocation is awaited (scheduled onto the owning
  runtime).
- `:733` — one throwing JS listener does not fail dispatch or later
  listeners.
- `:834-840` — invalid shapes (e.g. `Action<string>`) and unsupported
  payloads are build diagnostics.

Implementation to mirror:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  — existing `[Event]` property scanning, event-member model, generated
  awaitable delegate bodies, diagnostics (continue from the next free
  `EXPOJSI` ID in `ExpoModulesDiagnostics.cs`).
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventEmitterPrototype.cs`
  — the JS listener machinery modules use (`addListener` etc., 5 methods +
  subscription shape).
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs`
  — module-level dispatch path.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs`
  — entries hold only lifetime state, NativeState token, and
  `JavaScriptWeakObject` (spec: no ordinary wrapper retained after
  conversion returns). Dispatch must therefore reacquire the live JS object
  through the weak reference on the runtime thread; a collected/released
  entry means the dispatch target is gone.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs`
  — generated-class prototype machinery (extended by 017).

Expected from 017 (verify): public `SharedObject` base over
`ISharedObjectLifetime`, `[ExpoSharedObject]` attribute, generated JS class
with prototype-bound members, shared-object codecs, example in
`packages/example-module`.

Upstream direction: `SharedObject` derives from `EventEmitter`
(`<expo-repo>/packages/expo-modules-core/common/cpp/SharedObject.cpp` and the
TS `SharedObject extends EventEmitter` declaration) — per-instance listeners
with `startObserving`/`stopObserving`-style hooks scoped to the object.

Repo conventions that apply:

- **Living-spec workflow is mandatory** (AGENTS.md): delta spec at
  `docs/changes/2026-<mm-dd>-sharedobject-events/spec.md` → operator
  approval → `plan.md` → implementation with verified commits → merge into
  `docs/specs/modules-core-boundary.md` → archive. Read
  `.agents/skills/living-spec-workflow/SKILL.md` first.
- AGENTS.md "Maturity": complete polished feature only; no temporary
  shortcuts — STOP and raise instead.
- No runtime hot-path reflection; NativeAOT-compatible generated code only.
- Commit style: conventional-commit-ish
  (`feat(modules-core): shared object typed events`).
- Never commit absolute local paths, usernames, or machine names.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Managed test suite | `scripts/test-managed.sh` | exit 0 |
| Generator tests | `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | exit 0 |
| Mobile facade typecheck | `pnpm --filter mobile-app typecheck` | exit 0 |
| Formatting | `scripts/format.sh --check --all` (run `scripts/format.sh` then re-check if it fails) | exit 0 |

## Suggested executor toolkit

- `.agents/skills/living-spec-workflow/SKILL.md` — mandatory workflow.
- Skill `expo-jsi-managed-handle-lifetime` (if available) — weak-object
  reacquisition and wrapper ownership on the dispatch path is exactly its
  territory.
- Read plan 017's merged spec sections and archived change package first.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/` —
  shared-object event dispatch internals, prototype listener wiring
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/`
  and `.../Expo.ModulesCore.Generator.Tests/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/`
- `packages/example-module/` (extend the 017 shared-object example with an
  event)
- `packages/expo-modules-dotnet/src/` — TS typing additions only if the spec
  requires them (mirror `DotnetEventEmitter` export style)
- `docs/module-authoring-guide.md`, `docs/specs/modules-core-boundary.md`
- `docs/changes/2026-<mm-dd>-sharedobject-events/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `Expo.JSI` and native/C++ code — CONSUME plan 021's owned callback-state
  disposer as merged into `docs/specs/`; do not modify `Expo.JSI` itself.
  If 021's primitive turns out to be insufficient for this design, STOP.
- Module-level `[Event]` members and `ModuleEventEmitter` behavior — their
  semantics must not change; shared-object dispatch may reuse internals but
  not alter observable module behavior.
- The SharedObject identity/release/teardown contract from 007/017.
- Cross-runtime sharing, `JavaScriptObject` codec.

## Git workflow

- Branch: `advisor/019-sharedobject-events` off `development` (after 017
  merges).
- Commit per step. Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-sharedobject-events/spec.md` in the
GIVEN/WHEN/THEN SHALL style, deciding at least:

1. Authoring shape: `[Event]` partial properties on `[ExpoSharedObject]`
   classes with the same `Func<Task>` / `Func<T, Task>` contract, naming
   default, and explicit-name override as module events (`:676-698`).
2. JS surface: how a generated shared-object class exposes
   `addListener`/`removeListener` etc. — same method NAMES and observable
   semantics as `EventEmitterPrototype`, but listener storage must comply
   with "Revised design constraints" §1 (JS-heap, per instance) — the
   module-level managed-state storage model must NOT be reused for
   shared-object instances.
3. Dispatch: awaitable; target reacquired via the registry entry's weak
   object on the runtime thread (constraints §2); listeners read from the
   instance's JS-heap storage; listener-throw isolation identical to
   `:733`; zero-listener dispatch completes successfully; per-emitter
   isolation — listeners are keyed to their instance such that another
   instance's dispatch can never invoke them.
4. Lifetime: dispatch on a released/collected/torn-down entry fails loudly
   with a defined error; in-flight dispatch racing release/teardown must not
   crash or leak (define the locking/ordering); subscription cleanup per
   constraints §3 (plan 021 disposer); a self-capturing listener must not
   prevent GC of its shared object (constraints §1); observing hooks (if
   specced) start/stop per instance.
5. Diagnostics: invalid shapes on shared-object classes (same family as
   module-event diagnostics, next free `EXPOJSI` IDs), including `[Event]`
   on a class that is not `[ExpoSharedObject]`/`SharedObject`-derived where
   that is invalid.
6. TS typing for the generated class's event map.

Present to the operator for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3-6 to commits.
**Verify**: committed.

### Step 3: Generator support

Scan `[Event]` properties on `[ExpoSharedObject]` classes, extend the event
member model, emit awaitable partial-property bodies that dispatch through
the per-instance path, add all diagnostics. Generator tests per shape and
diagnostic, modeled after the existing module-event generator tests.

**Verify**: generator test command → exit 0.

### Step 4: Runtime dispatch + listener machinery

Per-instance listener storage in the JS heap on the generated instance
(constraints §1), dispatch implementation with weak reacquisition
(constraints §2), `remove()`/`removeListener` cleanup through plan 021's
exactly-once disposer (constraints §3), listener isolation, per-emitter
isolation, teardown locking on the dispatch path, and loud lifetime
failures. `EventEmitterPrototype` method semantics may be mirrored, but its
managed listener storage must not be reused for instances.

**Verify**: `scripts/test-managed.sh` → exit 0.

### Step 5: Example + TS + Hermes-backed tests

Add an event to the 017 example shared object and subscribe from its facade.
Hermes-backed tests: emit reaches only the target instance's listeners (two
instances, disjoint listeners); payload decoding; await surfaces encode
error; zero listeners; dispatch after release fails loudly; dispatch after
runtime teardown fails loudly; **a listener that captures its own shared
object does not prevent collection — drop all JS references, force GC via
the fixture's `CollectGarbageForTesting()` (see
`Expo.JSI.Tests/Runtime/JavaScriptWeakObjectTests.cs:97` for the pattern),
and assert the registry release callback fires**; subscription weak-handle
state is disposed when JS drops the subscription (021 disposer observed);
module-level event tests unmodified and passing.

**Verify**: `scripts/test-managed.sh` → exit 0;
`pnpm --filter mobile-app typecheck` → exit 0.

### Step 6: Docs merge and archive

Merge the delta into `docs/specs/modules-core-boundary.md`; update the
authoring guide's shared-object section with the event recipe; archive the
change folder; formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0.

## Test plan

- Generator tests: model extraction on shared-object classes, emitted body,
  each new diagnostic.
- Hermes-backed tests as listed in step 5 — instance isolation (the
  two-instance disjoint-listener case) is the load-bearing one; a dispatch
  that broadcasts across instances must fail it.
- All existing module-event and shared-object tests pass unmodified.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0 (new shared-object event tests
      included)
- [ ] Generator test command exits 0 (new tests included)
- [ ] `pnpm --filter mobile-app typecheck` exits 0
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] `packages/example-module`'s shared-object example declares and emits a
      typed event consumed by its facade
- [ ] `docs/specs/modules-core-boundary.md` contains the merged
      shared-object event scenarios
- [ ] Existing module-event tests pass unmodified
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- Plan 017 has not landed, or its shipped surface differs from the "Expected
  from 017" facts above.
- Plan 021 has not landed, or its merged disposer contract differs from what
  "Revised design constraints" §3 assumes (exactly-once across creation
  failure / GC / teardown, with a thread contract safe for
  `JavaScriptWeakObject` payloads).
- Per-instance listeners require new ABI entries or `Expo.JSI` changes
  beyond consuming 021's primitive.
- Any design draft stores strong references to JS listener functions or
  instances in managed state (the exact failure that rolled back the first
  execution — see Execution history).
- The dispatch path cannot satisfy loud lifetime failures without weakening
  the registry's exactly-once release contract.
- Reusing `EventEmitterPrototype` would change observable module-level
  event behavior.
- The operator rejects or wants substantive changes to the delta spec.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- The future generated-TypeScript tool reads these `[Event]` declarations as
  the per-class listener contract; naming stays consistent with module
  events (first-character lowercase, `On` prefix kept).
- Reviewer should scrutinize: instance isolation, no ordinary object wrapper
  retained by the dispatch path outside the runtime-thread callback, and the
  release/teardown races in step 5's tests.
- If profiling later shows per-instance listener storage is hot, optimize
  behind the same spec — semantics are fixed here.
