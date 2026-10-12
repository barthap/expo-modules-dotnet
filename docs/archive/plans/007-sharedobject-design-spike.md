# Plan 007: SharedObject / SharedRef — design spike with identity-round-trip prototype

> **Executor instructions**: This is a DESIGN SPIKE plan. The primary
> deliverable is a delta spec settling the identity/lifetime model, backed by
> a minimal prototype proving the riskiest mechanism (JS↔C# identity
> round-trip and teardown), plus a recorded stop/go decision. It is NOT the
> full SharedObject feature. Follow steps in order; run every verification
> command. On any "STOP conditions" item, stop and report. When done, update
> the status row in `docs/plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat 0f6fc760..HEAD -- packages/expo-modules-dotnet/managed/packages/ packages/expo-modules-dotnet/native/ docs/specs/`
> If in-scope files changed since this plan was written, compare "Current
> state" excerpts against live code; on mismatch, STOP.

## Status

- **Priority**: P3
- **Effort**: M (spike; full feature is L, multi-slice)
- **Risk**: MED — design risk, not code risk; wrong identity model is expensive to unwind
- **Depends on**: none hard. Recommended order: after docs/plans/006 (ArrayBuffer)
  per the roadmap assessment in `docs/plans/README.md`; SharedRef's flagship use
  cases (image refs, file handles) pair with binary data.
- **Category**: direction (design/spike)
- **Planned at**: commit `0f6fc760`, 2026-07-08

## Why this matters

`docs/roadmap.md` (P2/P3 "Richer Runtime Surface" and the "P2/P3 —
SharedObject references" backlog entry, line ~189) names SharedObject/SharedRef
as future work with an explicit direction already decided: "follow upstream
class/prototype instances with hidden registry-backed native identity rather
than HostObject-first objects." The foundations just landed — NativeState
(generic type-indexed object state, commit `3e016352`), HostObject
(commit `42471b75`), lazy module registry (`c27af310`), and NativeState
already backs EventEmitter identity in ModulesCore. This spike turns the
one-line direction into a concrete, prototyped design before any module
depends on a half-decided model. Shared objects are how upstream Expo modules
hand stateful native resources (images, filesystem handles, camera sessions)
to JS; the .NET bridge needs the same shape for parity.

## Current state

- **Decided direction (do not relitigate)** — `docs/roadmap.md:121-128`:

  ```markdown
  2. **HostObject / NativeState / SharedObject**
      - NativeState is complete as a generic, type-indexed object state primitive
        and backs ModulesCore EventEmitter identity.
      - HostObject is complete as a generic low-level property interceptor
        primitive in `Expo.JSI`.
      - SharedObject and SharedRef remain future work. Direction: follow upstream
        class/prototype instances with hidden registry-backed native identity
        rather than HostObject-first objects.
  ```

- Existing primitives to build on (read these before designing):
  - NativeState: ABI in `packages/expo-modules-dotnet/native/include/expo_jsi.h`
    (~lines 110–130: `expo_jsi_native_state_token`,
    `expo_jsi_release_native_state_fn` — release callback runs during JS
    object destruction, threading caveat documented in the header comment;
    setter entry ~line 245). Managed side:
    `Expo.JSI.Tests/Runtime/JavaScriptNativeStateTests.cs` shows usage.
  - EventEmitter identity precedent:
    `Expo.ModulesCore/EventEmitterRuntimeState.cs` and
    `EventEmitterPrototype.cs` — this is the repo's existing "hidden native
    identity on a JS object" pattern; SharedObject generalizes it.
  - Class/prototype support: `Expo.JSI.Tests/Runtime/JavaScriptClassTests.cs`
    and `ExpoClassInstaller.cs` in ModulesCore — prototype-instance machinery
    exists.
  - Object factory: `Expo.ModulesCore/JavaScriptObjectFactory.cs`.
- Upstream reference semantics (Expo modules, Swift/Kotlin) the design should
  mirror where sensible: `SharedObject` base class with a per-runtime registry
  mapping JS object ↔ native instance both directions; `SharedRef<T>` is a
  thin SharedObject wrapping a native pointer/resource; JS-side objects are
  real class instances (prototype chain, methods from the module definition);
  registry entry released when the JS object is GC'd; native → JS lookup
  returns the SAME JS object for the same native instance (identity, not a
  fresh wrapper each time). If a local checkout of expo/expo exists (see
  `AGENTS.local.md` for machine-local paths; not committed), read
  `packages/expo-modules-core` SharedObject sources for exact semantics —
  otherwise rely on the public Expo modules API docs.
- Relevant living specs to stay consistent with:
  `docs/specs/modules-core-boundary.md` (module object/class scenarios),
  `docs/specs/ownership-and-scoped-refs.md` (wrapper lifetimes),
  `docs/specs/runtime-scheduling.md` (all runtime access scheduled; GC
  callbacks may fire during object destruction).
- Hard constraints (`AGENTS.md`): no raw `jsi::*` layouts to C#; no runtime
  hot-path reflection in generated bindings (registry lookups must be
  dictionary/handle based, not reflective); NativeAOT compatible.
- Spike record requirements (from `AGENTS.md`): hypothesis, commands run,
  expected result, actual result, artifacts, ownership/lifetime findings,
  scheduler findings, stop/go decision.

## Commands you will need

| Purpose | Command | Expected on success |
|---|---|---|
| Managed tests | `scripts/test-managed.sh` | all pass |
| Filtered | `scripts/test-managed.sh --filter FullyQualifiedName~SharedObject` | new tests pass |
| Format | `scripts/format.sh --check --all` | exit 0 |

## Scope

**In scope**:
- `docs/changes/<yyyy-mm-dd>-sharedobject/spec.md` (create — primary deliverable)
- Prototype code in `Expo.ModulesCore` (registry + minimal `SharedObject`
  base) and, ONLY if the existing NativeState ABI proves insufficient,
  minimal additions to `expo_jsi.h` / `ExpoJsiBridge.cpp` (record why)
- `Expo.ModulesCore.Tests/` — prototype proof tests
- `docs/specs/modules-core-boundary.md` (merge accepted delta on GO)
- `docs/plans/README.md` (status row)

**Out of scope** (deferred past the spike):
- Generator support (`[ExpoModule]` methods returning/accepting SharedObject
  subclasses) — design the signature rules in the spec; implement later.
- `SharedRef<T>` beyond a spec section — it's a thin layer once SharedObject
  identity works.
- JS-facing TypeScript API surface in `packages/expo-modules-dotnet/src/`.
- Cross-runtime (multi-runtime) sharing; concurrency beyond the single
  scheduled runtime model.
- Any HostObject-based alternative — direction already decided against it.

## Git workflow

- Branch: `advisor/007-sharedobject-spike`
- Commit style: `docs: add sharedobject delta spec`,
  `feat(modules-core): prototype shared object registry (spike)`
- Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Study pass, written down

Read the files listed in "Current state" (NativeState ABI + tests,
EventEmitterRuntimeState/Prototype, JavaScriptClassTests, ExpoClassInstaller)
and, if available locally, upstream expo-modules-core SharedObject. Produce
the "Prior art" section of the delta spec: 1–2 paragraphs on how upstream does
registry identity, and exactly which existing repo primitives map to which
role (prototype instances ← ExpoClassInstaller; hidden identity ←
NativeState token; release ← native-state release callback).

**Verify**: spec file exists with the Prior art section; `git diff --check` clean.

### Step 2: Design decisions in the delta spec

Answer each with a chosen position and rationale:

1. **Registry shape**: per-runtime `Dictionary<int, SharedObjectEntry>` keyed
   by a monotonically increasing ID stored in the object's NativeState token
   vs keyed by NativeState token pointer identity. Include: what the entry
   holds (strong ref to managed instance? weak?), and both lookup directions
   (JS object → managed via NativeState read; managed → JS object via what —
   this direction is the hard one; upstream keeps a weak JS reference).
2. **Lifetime**: when the JS object is GC'd, the release callback fires — does
   the managed instance die with it (strong ref dropped) or can it outlive and
   be re-wrapped? Define `SharedObject` deallocation hooks (upstream has
   `sharedObjectWillRelease`-style hooks; decide the .NET names, e.g.
   `OnRelease`). Interaction with `runtime-scheduling.md`: the release
   callback's thread context and what managed code may do inside it.
3. **Identity guarantee**: returning the same managed instance twice to JS
   yields the same JS object — requires the managed→JS weak mapping; specify
   behavior when the previous JS object was already GC'd (create fresh, new
   registry generation).
4. **Class integration**: SharedObject JS instances are prototype-based class
   instances installed via the existing `ExpoClassInstaller` path — spell out
   construction flow (native-created vs JS-`new`-created, if the latter is
   allowed at all in v1; upstream allows JS construction only for some types —
   recommended v1: native-created only).
5. **SharedRef<T>**: one spec section defining it as
   `SharedObject` + `T Reference` payload + release disposing the payload;
   no prototype methods of its own in v1.
6. **NativeAOT check**: every mechanism above expressed without reflection —
   name the concrete data structures.

**Verify**: spec contains all six sections, each with a position (not open
questions); remaining genuine unknowns collected in an "Open questions"
section at the end.

### Step 3: Prototype the riskiest slice

Implement the minimum to prove decisions 1–3: a `SharedObjectRegistry` in
`Expo.ModulesCore`, a test-only `SharedObject` subclass, wiring through
NativeState + release callback. No generator changes — construct and return
objects through the existing test harness / `JavaScriptObjectFactory` path the
way `EventEmitterRuntimeState` does.

**Verify**: `scripts/test-managed.sh` → full suite passes (prototype compiles,
nothing regressed).

### Step 4: Prototype proof tests

In `Expo.ModulesCore.Tests` (model on existing tests there — check how
EventEmitter identity is tested): (a) round-trip — managed instance → JS →
passed back to a managed function → resolves to the SAME managed instance;
(b) identity — same managed instance returned twice → JS `===` is true;
(c) release — dropping all JS references and forcing GC (use whatever the
existing NativeState release tests do to trigger collection) fires the
release hook exactly once and empties the registry entry; (d) teardown —
runtime disposal with live shared objects neither leaks (registry drained)
nor crashes.

**Verify**: `scripts/test-managed.sh --filter FullyQualifiedName~SharedObject`
→ all four pass; full suite passes.

### Step 5: Spike record + stop/go

Append the spike record (hypothesis / commands / expected / actual /
artifacts / ownership-lifetime findings / scheduler findings / stop-go) to the
delta spec. On GO: merge the accepted design into
`docs/specs/modules-core-boundary.md` (new SharedObject section) and list the
follow-up slices (generator support, SharedRef, JS API). On NO-GO: record why
and what would change the answer; revert or clearly mark the prototype.

**Verify**: `scripts/format.sh --check --all` → exit 0; on GO,
`grep -n "SharedObject" docs/specs/modules-core-boundary.md` shows the section.

## Test plan

Step 4 is the test plan. Exemplars: NativeState release tests in
`Expo.JSI.Tests/Runtime/JavaScriptNativeStateTests.cs`, EventEmitter identity
coverage in `Expo.ModulesCore.Tests`.

## Done criteria

- [ ] Delta spec exists: Prior art + six decided design sections + Open
      questions + spike record.
- [ ] Registry prototype compiles; four proof tests pass.
- [ ] `scripts/test-managed.sh` exits 0; `scripts/format.sh --check --all` exits 0.
- [ ] On GO: `docs/specs/modules-core-boundary.md` updated with the
      SharedObject section and follow-up slice list.
- [ ] No files outside in-scope list modified (`git status`).
- [ ] `docs/plans/README.md` status row updated.

## STOP conditions

Stop and report back (do not improvise) if:

- The managed→JS direction (identity guarantee) cannot be built on existing
  primitives — i.e. there is no way to hold a weak reference to a JS object
  through the current ABI — and would need a new weak-handle ABI capability.
  That is a real finding: record it in the spec and stop; the weak-handle ABI
  design is its own slice.
- The NativeState release callback's threading context makes it unsafe to
  touch the registry from it (check the header caveat against
  `runtime-scheduling.md`) and no safe deferral pattern exists in the repo.
- Test (c) cannot deterministically trigger JS GC in the Hermes testhost —
  check how existing NativeState tests handle this; if they can't either,
  record the verification gap instead of writing a flaky test.
- Prototype scope starts pulling in generator changes — that's the follow-up
  slice, not the spike.

## Maintenance notes

- Follow-up slices on GO (in order): weak-handle ABI if flagged, generator
  support for SharedObject-typed parameters/returns, `SharedRef<T>`,
  TypeScript-side API. Each is its own delta spec per repo workflow.
- Reviewer: scrutinize the release-hook reentrancy story (release fires during
  JS GC — anything it schedules must not deadlock the sync-execution path
  fixed in commit `4098c24e`).
- Per the roadmap assessment in `docs/plans/README.md`: NativeAOT end-to-end proof
  recommended before modules depend on SharedObject in production.
