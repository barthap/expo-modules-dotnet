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
> condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (per-object listener lifetime + dispatch on registry-owned
  entries)
- **Depends on**: docs/plans/017-sharedobject-authoring.md (hard — the public
  `SharedObject` base, `[ExpoSharedObject]`, and generated JS classes must
  exist first)
- **Category**: direction / dx
- **Planned at**: commit `ea07d69d`, 2026-07-20
- **Execution status**: TODO — unblocked on 2026-07-22; hard dependency Plan
  017 completed at `353f98d8`.

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

- `Expo.JSI` and native/C++ code — no ABI change. If per-object listeners
  need one, STOP.
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
   `addListener`/`removeListener` etc. — reuse the `EventEmitterPrototype`
   method set, scoped per instance.
3. Dispatch: awaitable; target reacquired via the registry entry's weak
   object on the runtime thread; listener-throw isolation identical to
   `:733`; zero-listener dispatch completes successfully.
4. Lifetime: dispatch on a released/collected/torn-down entry fails loudly
   with a defined error; in-flight dispatch racing release/teardown must not
   crash or leak; observing hooks (if specced) start/stop per instance.
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

Per-instance listener storage on the generated JS class (reusing
`EventEmitterPrototype` internals per the spec), dispatch implementation
with weak reacquisition, listener isolation, and loud lifetime failures.

**Verify**: `scripts/test-managed.sh` → exit 0.

### Step 5: Example + TS + Hermes-backed tests

Add an event to the 017 example shared object and subscribe from its facade.
Hermes-backed tests: emit reaches only the target instance's listeners (two
instances, disjoint listeners); payload decoding; await surfaces encode
error; zero listeners; dispatch after release fails loudly; dispatch after
runtime teardown fails loudly; module-level event tests unmodified and
passing.

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
- Per-instance listeners require new ABI entries or `Expo.JSI` changes.
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
