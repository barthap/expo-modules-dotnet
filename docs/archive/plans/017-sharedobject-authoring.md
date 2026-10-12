# Plan 017: SharedObject public authoring surface over the proven identity registry

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat ea07d69d..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/example-module docs/specs/modules-core-boundary.md`
> If plan 015 landed first its changes live under `Expo.JSI`/native — that
> drift is expected and unrelated. For any other in-scope drift, compare the
> "Current state" excerpts against the live code; on a mismatch, treat it as
> a STOP condition.

## Status

- **Priority**: P2
- **Effort**: L
- **Risk**: MED-HIGH (new generator surface, ownership contract, public API)
- **Depends on**: none hard; 015 recommended first (shared teardown ordering
  churn), 016 recommended first (NativeAOT lane guards this plan's
  generated code)
- **Category**: direction / dx
- **Planned at**: commit `ea07d69d`, 2026-07-20
- **Execution status**: IN PROGRESS on 2026-07-22. Task 1 is restored
  byte-for-byte from `b318681d`, and the production `Expo.ModulesCore` build
  passes. The prior SDK 10.0.201 consumer compile stall did not reproduce. The
  focused test now reaches execution: 1 of 5 tests passes and 4 fail because
  the restored helper assumes the host-function `Proxy` exposes an object
  `prototype`. Task 1 is not verified or currently approved. Tasks 2–7 remain
  pending.

## Why this matters

The SharedObject design spike (plan 007) closed GO: the opaque weak-object
ABI, deterministic Hermes GC evidence, and the internal per-context
`SharedObjectRegistry` with reference-identity round trips and exactly-once
terminal release are implemented and specced. What's missing is everything a
module author touches: a public `SharedObject` base class, the
`[ExpoSharedObject]` attribute, generated constructor/method/property
bindings on a JS class prototype, `SharedRef<T>`, and the TypeScript facade.
This is the flagship capability unlock — image/file/crypto handles and every
"expensive native resource held by JS" module shape depends on it, and it is
the largest remaining gap against the Expo Modules 2.0 authoring direction.

## Current state

(At `ea07d69d`.)

The archived design-phase spec defines this work precisely —
`docs/archive/changes/2026-07-19-sharedobject/spec.md` "Deferred slices"
(lines 505-519; "slice" is that document's historical vocabulary). This plan
implements items 1, 2, and 4 of that list:

1. `[ExpoSharedObject]`, `[ExpoModule(Classes = ...)]` validation, and
   generated constructor, method, and property bindings.
2. SharedObject-typed generated codecs and public `SharedRef<T>` with its
   explicit ownership contract.
4. TypeScript constructors, facades, normal authoring guidance, and the
   resource-cleanup recipe.

Item 3 (typed `[Event]` members on shared objects) is a separate feature
with its own plan — `docs/plans/019-sharedobject-events.md` — because it
carries its own per-object listener/emitter machinery. Items 5
(cross-runtime sharing) and 6 (`JavaScriptObject` codec) are separate
features with no plan yet. Everything IN this plan ships complete and
polished — the AGENTS.md "Maturity" rule applies: no partial surface, no
temporary shortcuts; if one seems needed, STOP and report.

What exists today:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs`
  — `internal interface ISharedObjectLifetime` (line 6),
  `internal sealed class SharedObjectEntry` (line 11) holding `Id`,
  `Instance` (`ISharedObjectLifetime`), and `WeakObject`
  (`JavaScriptWeakObject`). All internal — the public base class wraps this.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectPrototype.cs`
  — prototype machinery from the spike.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjectRegistryTests.cs`
  — existing identity/release/teardown tests; they must keep passing.
- Normative spec: `docs/specs/modules-core-boundary.md`, "Internal
  Shared-Object Identity Registry" section (around line 1254): identity round
  trip, exactly-once terminal release outside locks, NativeState-callback
  re-entry deferral, teardown drains shared entries first. Closing note
  (around line 1298): "This is an internal identity proof only. It SHALL NOT
  be read as implementing a public `SharedObject` ... a `JavaScriptObject`
  codec is a separate future slice." This plan replaces that closing note
  with the public-surface requirements.
- Upstream direction (from the archived spec "Prior art" section, lines
  30-40): annotated class deriving `SharedObject`; owning module lists the
  class; annotated members bound on a JS class prototype; registry-backed
  native identity with a native-state releaser; native-to-JS conversion
  returns the existing JS object via the weak counterpart. Inspiration files
  named there: `<expo-repo>/packages/expo-modules-core/common/cpp/SharedObject.cpp`
  and `.../ios/Core/SharedObjects/SharedObjectRegistry.swift`.
- Generator: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  — existing `[JS]` method/property scanning, typed `[Event]` members,
  camelCase naming defaults (`LowerCamel`), diagnostics in
  `ExpoModulesDiagnostics.cs` (continue from the next free `EXPOJSI` ID).
- Authoring patterns to mirror: `packages/example-module` (module C# +
  TypeScript facade extending `DotnetModule<...>` from `expo-modules-dotnet`),
  `docs/module-authoring-guide.md` structure.

Repo conventions that apply:

- **Living-spec workflow is mandatory** (AGENTS.md): delta spec at
  `docs/changes/2026-<mm-dd>-sharedobject-authoring/spec.md` → operator
  approval → `plan.md` → implementation with verified commits → merge into
  `docs/specs/modules-core-boundary.md` → archive. Read
  `.agents/skills/living-spec-workflow/SKILL.md` first.
- No runtime hot-path reflection; NativeAOT-compatible generated code only.
- Do not expose raw `jsi::Runtime`/`jsi::Value`/`jsi::Object` layouts to C#.
- Commit style: conventional-commit-ish
  (`feat(modules-core): public SharedObject base`).
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
- Skill `expo-jsi-managed-handle-lifetime` (if available) — wrapper-ownership
  pitfalls; conversion paths here must not retain ordinary object wrappers
  (registry entries keep only lifetime state, NativeState token, and the
  opaque weak object — spec requirement).
- Read the archived spike package fully before the delta spec:
  `docs/archive/changes/2026-07-19-sharedobject/{spec,plan}.md`.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/` — public
  `SharedObject` base (with protected `OnRelease`), `SharedRef<T>`,
  `[ExpoSharedObject]` attribute, codec additions, registry/prototype
  internals as needed
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/`
  and `.../Expo.ModulesCore.Generator.Tests/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/`
- `packages/example-module/` (a real shared-object example: C# class +
  facade + usage)
- `packages/expo-modules-dotnet/src/` — TypeScript facade additions only if
  the design requires a base class/type export (mirror how
  `DotnetModule`/`DotnetEventEmitter` are exported from
  `src/ts-declarations/`)
- `docs/module-authoring-guide.md`, `docs/specs/modules-core-boundary.md`
- `docs/changes/2026-<mm-dd>-sharedobject-authoring/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- `Expo.JSI` and native/C++ code — plan 007 put the weak-object ABI in
  place; this plan is managed + generator + TS only. If an ABI gap
  appears, STOP.
- Typed `[Event]` members on shared objects — separate feature, plan 019.
- Cross-runtime sharing — separate future feature.
- `JavaScriptObject` codec — separate future feature.
- `JavaScriptValue` advanced-module codec — unchanged.

## Git workflow

- Branch: `advisor/017-sharedobject-authoring` off `development`.
- Commit per plan.md task. Do NOT push or open a PR unless the operator
  instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-sharedobject-authoring/spec.md` in the
GIVEN/WHEN/THEN SHALL style of `docs/specs/modules-core-boundary.md`. It must
decide and specify at least:

1. Public `SharedObject` base: how an authored class opts in
   (`[ExpoSharedObject]` on a class deriving `SharedObject`), the protected
   `OnRelease` contract (called exactly once, off which thread, what the
   author may do inside), and how instances register with the internal
   registry.
2. Module ownership: how a module declares its classes (the archived spec
   names `[ExpoModule(Classes = ...)]` — validate that shape against the
   current attribute design and pick the final form in the spec).
3. Generated bindings: JS class with constructor (when the C# class has a
   usable public constructor), `[JS]` methods/properties on the prototype,
   camelCase naming defaults consistent with existing rules.
4. Codecs: shared-object-typed parameters and returns — decode = registry
   lookup via NativeState token (must return the original managed instance),
   encode = existing-entry weak lookup or new JS object creation.
5. `SharedRef<T>`: explicit ownership contract (who releases, double-release
   no-op, behavior after runtime teardown).
6. Diagnostics for invalid shapes (non-partial class, missing base, codec-less
   member types, duplicate class registration — next free `EXPOJSI` IDs).
7. TypeScript surface: how a facade exposes the generated class and the
   resource-cleanup recipe (explicit `release()`/`using`-style guidance).

Present to the operator for approval before implementing. This spec has real
design decisions — if any of items 1-5 cannot be settled without changing
the internal registry's specced semantics, STOP and report instead of
respeccing the registry.

**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping the work to commits
(suggested slicing: attribute + generator model/diagnostics → public base +
registry wiring → generated prototype bindings → codecs + `SharedRef<T>` →
example + TS facade → docs merge).

**Verify**: committed.

### Step 3: Attribute, generator model, diagnostics

`[ExpoSharedObject]` attribute; generator scans and validates per the spec;
all new diagnostics with generator tests (one test per diagnostic, model
after the existing diagnostics tests in `Expo.ModulesCore.Generator.Tests`).

**Verify**: generator test command → exit 0.

### Step 4: Public base + generated bindings

Public `SharedObject` (over `ISharedObjectLifetime`/`SharedObjectEntry`),
generated JS class prototype with constructor/method/property bindings,
identity round trip preserved (two conversions strictly equal — the existing
`SharedObjectRegistryTests` assertions extend to the public path).

**Verify**: `scripts/test-managed.sh` → exit 0.

### Step 5: Codecs + `SharedRef<T>`

Generated codecs for shared-object parameters/returns; `SharedRef<T>` with
its ownership contract and Hermes-backed lifetime tests (release exactly
once, teardown drains, post-teardown use fails loudly).

**Verify**: `scripts/test-managed.sh` → exit 0.

### Step 6: Example, TS facade, docs merge, archive

Shared-object example in `packages/example-module` (a small handle-style
class exercised from the facade); TS exports if specced; rewrite the
guide's shared-object section; merge the delta into
`docs/specs/modules-core-boundary.md` (replacing the "internal identity
proof only" closing note); archive the change folder; formatting.

**Verify**: `pnpm --filter mobile-app typecheck` → exit 0;
`scripts/format.sh --check --all` → exit 0.

## Test plan

- Generator tests: model extraction, each new diagnostic, emitted binding
  shapes (constructor/method/property), codec selection.
- Hermes-backed tests (`Expo.ModulesCore.Tests`, model after
  `SharedObjectRegistryTests.cs` and the generated-module test fixtures):
  construct from JS and call methods; pass a shared object back to C# and
  get the original instance; identity round trip via the public path;
  `OnRelease` exactly once (JS release, GC, teardown); `SharedRef<T>`
  lifetime cases; existing registry tests unmodified and passing.
- Verification: all commands in the table green.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0 (new shared-object tests included)
- [ ] Generator test command exits 0 (new diagnostics + binding tests included)
- [ ] `pnpm --filter mobile-app typecheck` exits 0
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] `grep -n "internal identity proof only" docs/specs/modules-core-boundary.md`
      returns no matches (closing note replaced by public-surface spec)
- [ ] `packages/example-module` contains a working `[ExpoSharedObject]`
      example used by its facade
- [ ] Existing `SharedObjectRegistryTests` pass unmodified
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row updated

## STOP conditions

Stop and report back (do not improvise) if:

- The public surface requires new ABI entries or `Expo.JSI` changes.
- Any specced registry semantic (identity round trip, exactly-once terminal
  release outside locks, teardown drain order, NativeState re-entry
  deferral) would need to change.
- The operator rejects or wants substantive changes to the delta spec.
- Generated bindings would need runtime reflection to satisfy a spec item.
- The `[ExpoModule(Classes = ...)]` shape conflicts with the existing
  `[ExpoModule]` attribute design in a way the spec discussion didn't
  anticipate.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Plan 019 (typed `[Event]` members on shared objects) is the natural next
  feature once this lands — the module-level event surface exists since
  plan 014.
- The future generated-TypeScript tool will read `[ExpoSharedObject]`
  classes as class declarations; naming decisions here become load-bearing.
- Reviewer should scrutinize: no ordinary object/function wrapper retained
  by registry entries after conversion returns (spec requirement), thread of
  `OnRelease` invocation, and codec behavior for a released/torn-down entry.
- If plan 015 has not landed, coordinate: both touch teardown-adjacent code
  paths in tests; land 015 first to avoid churn.
