# Plan 014: Typed `[Event]` members replacing string-based event declaration

> **Execution note (2026-07-19):** The `Action` / `Action<T>` design below is
> superseded by the approved awaitable design in
> `docs/changes/2026-07-19-typed-event-members/spec.md` and its implementation
> plan. The runtime has no failure sink that can make discarded asynchronous
> event tasks reliable, and blocking a void delegate can deadlock an async-only
> scheduler. Execute the change-package plan, which uses `Func<Task>` /
> `Func<T, Task>`, instead of Steps 1-6 in this advisor artifact. This file is
> retained as the original improve-plan rationale and inventory.

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving to the
> next step. If anything in the "STOP conditions" section occurs, stop and
> report — do not improvise. When done, update the status row for this plan
> in `docs/plans/README.md` — unless a reviewer dispatched you and told you
> they maintain the index.
>
> **Drift check (run first)**:
> `git diff --stat b6a702a6..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/example-module docs/module-authoring-guide.md docs/specs/modules-core-boundary.md`
> Plan 013 intentionally changes these files — read `docs/plans/013-*.md`'s
> final state first, then compare remaining drift against the "Current
> state" excerpts; on an unexplained mismatch, treat it as a STOP condition.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED (new generator surface + event dispatch semantics)
- **Depends on**: docs/plans/013-camelcase-js-naming-and-properties.md
- **Category**: dx
- **Planned at**: commit `b6a702a6`, 2026-07-19

## Why this matters

This repo is aligning with the Expo Modules 2.0 authoring direction, where
an event is a single typed callable member instead of a registered string
name plus a send call. The article's Swift form:

```swift
@ExpoModule
public final class DownloadModule {
  @Event
  var onProgress: (ProgressEvent) -> Void

  func tick() { onProgress(ProgressEvent(percent: 50)) }
}
```

Today this repo declares events as strings (`[Events("onStatus")]` on the
class) and emits via `SendEventAsync<StringCodec, string>("onStatus", ...)` —
the event name and its payload type are never connected, typos in the name
are runtime errors, and the codec must be spelled at every call site. The
operator approved this C# mapping (decision recorded 2026-07-19):

```csharp
[ExpoModule]
public sealed partial class DownloadModule : Module
{
  [Event]
  public partial Action<ProgressEvent> OnProgress { get; }

  void Tick() => OnProgress(new ProgressEvent(50));
}

public readonly record struct ProgressEvent(int Percent);
```

The generator implements the partial property getter, returning a cached
delegate that encodes through the compile-time codec and dispatches the
event. The JS event name is the lower-camel-case property name
(`OnProgress` → `onProgress`, matching the existing `"onStatus"` naming
convention; deliberately NOT stripping the `On` prefix the way the article's
Swift comment implies). `[Events]` strings keep working during migration.

## Current state

(Line numbers at `b6a702a6`; plan 013 touches nearby code — re-verify.)

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventsAttribute.cs`
  — `[Events(params string[] names)]`, class-level, stays supported.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs`
  — base class; emission helpers:
  ```csharp
  protected Task SendEventAsync<TCodec, T>(
      string eventName, T payload, CancellationToken cancellationToken = default)
      where TCodec : struct, IJavaScriptCodec<T> =>
      RuntimeContext.Events.EmitAsync<TCodec, T>(this, eventName, payload, cancellationToken);
  ```
  Emission ultimately goes through `RuntimeContext.Events`
  (`ModuleEventEmitter.cs`); emitting an undeclared event name fails loudly.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`:
  - `:99` — `var eventNames = GetEventNames(typeSymbol, moduleName, diagnostics);`
  - `:140-190` — `GetEventNames`: reads `[Events]`, reports empty
    (`EXPOJSI`-series) and duplicate names as diagnostics.
  - Modules with a non-empty event list are registered as
    `_expoDotnet.NativeModule` instances so the `EventEmitterPrototype`
    chain (`addListener` etc.) works; `[OnStartObserving]` /
    `[OnStopObserving]` hooks key off the declared names (`:204+`).
  - `:1805` — `LowerCamel` helper (plan 013 also uses it).
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
  — IDs through `EXPOJSI013` at `b6a702a6`; plan 013 adds more. Continue
  from the next free ID.
- `packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` —
  declares `[Events("onStatus")]` and emits via
  `SendEventAsync<StringCodec, string>("onStatus", ...)`.
- `docs/module-authoring-guide.md` section 5 ("Events") documents the
  string-based flow; `docs/specs/modules-core-boundary.md` is the normative
  spec (GIVEN/WHEN/THEN SHALL scenarios — match the style).
- The module class is already required to be `partial` (generator adds
  registration members), and projects target `net10.0`, so C# partial
  properties are available.

Repo conventions that apply:

- **Living-spec workflow is mandatory** (AGENTS.md): delta spec at
  `docs/changes/2026-<mm-dd>-typed-event-members/spec.md` → approval →
  `plan.md` → implementation with verified commits → merge into
  `docs/specs/modules-core-boundary.md` → archive. Read
  `.agents/skills/living-spec-workflow/SKILL.md` first.
- No runtime reflection; NativeAOT-compatible generated code only.
- Async work scheduled through `DotnetRuntimeContext` is routed onto the
  owning JS runtime by the runtime scheduler (`docs/specs/runtime-scheduling.md`).
- Commit style: conventional-commit-ish, e.g.
  `feat(generator): typed [Event] partial properties`.
- Never commit absolute local paths, usernames, or machine names.

## Commands you will need

| Purpose | Command (repo root) | Expected on success |
|---|---|---|
| Managed test suite | `scripts/test-managed.sh` | exit 0 |
| Generator tests | `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | exit 0 |
| Formatting | `scripts/format.sh --check --all` (run `scripts/format.sh` then re-check if it fails) | exit 0 |
| Mobile facade typecheck | `pnpm --filter mobile-app typecheck` | exit 0 |

## Suggested executor toolkit

- `.agents/skills/living-spec-workflow/SKILL.md` — mandatory workflow.
- Skill `expo-jsi-managed-handle-lifetime` (if available) — payload encoding
  happens on the runtime thread inside scheduled work; the skill covers the
  wrapper-ownership pitfalls.

## Scope

**In scope** (the only files you should modify or create):

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/` — new
  `EventAttribute.cs`; possibly small additions to `Module.cs` /
  `ModuleEventEmitter.cs` internals for delegate-based dispatch
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/`
- `packages/example-module/` (module C#, facade TS)
- `docs/module-authoring-guide.md`, `docs/specs/modules-core-boundary.md`
- `docs/changes/2026-<mm-dd>-typed-event-members/` (create)
- `docs/plans/README.md` (status row only)

**Out of scope** (do NOT touch, even though they look related):

- Removing `[Events]` / `SendEventAsync` — they stay as the migration path;
  only the guide's recommendation changes.
- `EventEmitterPrototype.cs` / JS-side listener machinery — listener
  semantics don't change.
- Typed JS facade base classes (`DotnetEventEmitter`) — that is plan 012.
- SharedObject payloads — plan 007 territory; payload support here is
  "any type with an existing codec".
- `Expo.JSI` and native/C++ code — no ABI change.

## Git workflow

- Branch: `advisor/014-typed-event-members` off `main` (after 013 merges).
- Commit per step. Do NOT push or open a PR unless the operator instructed it.

## Steps

### Step 1: Delta spec

Write `docs/changes/2026-<mm-dd>-typed-event-members/spec.md` covering, in
the spec's SHALL-scenario style:

1. `[Event]` SHALL be valid on an instance partial property of delegate type
   `Action` (payload-less) or `Action<T>` where `T` has a compile-time codec.
   The generator SHALL implement the getter, returning a cached delegate.
2. The JS event name SHALL default to the lower-camel-case property name
   (`OnProgress` → `onProgress`); `[Event("name")]` SHALL override.
3. Invoking the delegate SHALL validate and encode the payload and dispatch
   the event to JS listeners through the existing module event emitter,
   scheduling onto the owning runtime when called off the runtime thread —
   same semantics as `SendEventAsync` today. Invocation is fire-and-forget
   from the author's perspective; dispatch failures (encode errors, torn-down
   runtime) SHALL fail loudly (thrown synchronously when detectable at the
   call site, otherwise surfaced through the runtime scheduler's failure
   path) rather than being swallowed.
4. `[Event]` properties SHALL count as declared events: they merge with
   `[Events]` names for NativeModule registration and
   `[OnStartObserving]`/`[OnStopObserving]` hooks; a name declared both ways
   or twice SHALL be a duplicate-name diagnostic.
5. Unsupported shapes SHALL be build diagnostics (next free `EXPOJSI` IDs):
   static, non-partial, setter-having, non-`Action`/`Action<T>` type,
   payload type without a codec, `[Event]` and `[JS]` on the same member.

Present to the operator/reviewer for approval before implementing.
**Verify**: spec committed; operator approved.

### Step 2: Implementation plan artifact

Write `docs/changes/<same-folder>/plan.md` mapping steps 3–6 to commits.
**Verify**: committed.

### Step 3: `EventAttribute` + generator model

Add `EventAttribute.cs` (`AttributeTargets.Property`, parameterless and
`(string name)` constructors, mirroring `JSAttribute.cs`). In the generator,
scan `IPropertySymbol` members for `[Event]`, validate shape per the spec,
build an event-member model (property name, JS name, payload type + codec
expression or none), and merge JS names into the existing `eventNames` flow
(`GetEventNames` result) so registration and observing hooks see them.
Add the diagnostics. Generator tests for model + each diagnostic.

**Verify**: `dotnet test .../Expo.ModulesCore.Generator.Tests.csproj` → exit 0.

### Step 4: Emit partial property implementation

Generate, in the module's partial class: a backing field and a getter
implementation returning a cached `Action<T>` (or `Action`) that calls the
event emitter with the compile-time codec — the generated body can reuse
whatever `Module.SendEventAsync`/`RuntimeContext.Events` path the spec
settled on, observing the failure semantics from Step 1 item 3. Modules not
inheriting `Module` (context-constructor strategy) must work too — dispatch
through the stored `DotnetRuntimeContext`, matching how generated function
glue reaches the context today.

**Verify**: generator tests for emitted body shape → exit 0.

### Step 5: Example migration + Hermes-backed tests

Convert `ExampleMathModule`'s `onStatus` to a typed
`[Event] public partial Action<string> OnStatus { get; }` (keep the JS name
`onStatus` — camelCase default gives exactly that) and emit through it; keep
one `[Events]`-based fixture module in `Expo.ModulesCore.Tests` so the
legacy path stays covered. Add Hermes-backed tests: typed emit reaches a JS
listener with the decoded payload; payload-less `Action` event; record
payload; duplicate declaration diagnostic; observing hooks fire for
`[Event]`-declared names; emit after runtime teardown fails loudly.

**Verify**: `scripts/test-managed.sh` → exit 0;
`pnpm --filter mobile-app typecheck` → exit 0.

### Step 6: Docs merge and archive

Merge the delta into `docs/specs/modules-core-boundary.md`. Rewrite guide
section 5 around typed `[Event]` members with the string-based `[Events]`
form kept as a documented migration/interop path. Archive the
`docs/changes/` folder per the living-spec skill. Run formatting.

**Verify**: `scripts/format.sh --check --all` → exit 0; guide section 5
shows the typed form first.

## Test plan

- Generator tests: event model extraction, JS-name defaulting and override,
  emitted partial-property body, all new diagnostics (static, non-partial,
  has setter, wrong delegate type, codec-less payload, `[Event]`+`[JS]`
  clash, duplicate with `[Events]`).
- Hermes-backed tests (`Expo.ModulesCore.Tests`, model after the existing
  event fixtures): typed emit → JS listener receives payload; `Action`
  payload-less; record-struct payload; observing hooks; post-teardown emit
  fails loudly; legacy `[Events]` module still works alongside.
- Verification: both test commands → all pass including new tests.

## Done criteria

Machine-checkable. ALL must hold:

- [ ] `scripts/test-managed.sh` exits 0
- [ ] `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` exits 0
- [ ] `pnpm --filter mobile-app typecheck` exits 0
- [ ] `scripts/format.sh --check --all` exits 0
- [ ] `grep -n 'SendEventAsync' packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs` returns no matches (example fully migrated)
- [ ] A `[Events]`-based fixture module still exists and passes in `Expo.ModulesCore.Tests`
- [ ] `docs/specs/modules-core-boundary.md` contains the merged typed-event scenarios
- [ ] No files outside the in-scope list modified (`git status`)
- [ ] `docs/plans/README.md` status row for 014 updated

## STOP conditions

Stop and report back (do not improvise) if:

- Plan 013 has not landed (this plan assumes its camelCase defaults and
  property-scanning machinery).
- The operator rejects or wants changes to the delta spec.
- C# partial properties can't be generated for a module's declared shape in
  some project configuration (LangVersion below C# 13 anywhere in the
  matrix) — report instead of downgrading the syntax.
- The fire-and-forget failure semantics (Step 1 item 3) can't be satisfied
  with the existing scheduler/emitter — do not silently swallow dispatch
  failures to make tests pass.
- Supporting non-`Module`-derived modules requires changing the constructor
  strategy contract.
- A step's verification fails twice after a reasonable fix attempt.

## Maintenance notes

- Plan 012 (typed JS facade base classes) should, when implemented, derive
  its typed events map from these `[Event]` declarations — coordinate names.
- The future generated-TypeScript tool will read `[Event]` payload types as
  the listener contract; the name mapping chosen here (full camelCase
  property name, `On` prefix kept) becomes load-bearing.
- Reviewer should scrutinize: delegate caching per module instance (one
  runtime context = one instance = one delegate), no payload encoding off
  the runtime thread, and that failure paths are loud.
- Deferred deliberately: removing `[Events]`/`SendEventAsync` (migration
  path), event payloads that are shared objects (plan 007), stripping the
  `On` prefix for JS names (rejected — would diverge from the existing
  `onStatus` listener convention).
