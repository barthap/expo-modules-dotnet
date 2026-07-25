# Plan 032: Support nullable JavaScript callback parameters

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving on. Touch
> only the files in the In-scope list. If a STOP condition occurs, stop and
> report, do not improvise. Follow the repo's living-spec workflow: approved
> delta spec first, then an approved change-local plan, then implementation.
> Update the status row in `docs/plans/README.md` when done unless a reviewer
> says they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat 512ab46e..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptCallback.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests docs/specs/ownership-and-scoped-refs.md docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md
> ```
> Plans 028–031 must be DONE. Rebase the drift check to plan 031's final commit.
> If callback registration, callback codec arity, descriptor capabilities, or
> plan-026 nullable exclusion behavior differs from "Current state," stop and
> reconcile before coding.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: MED-HIGH (null must short-circuit before retaining a JS function,
  while non-null callbacks keep context-owned teardown behavior)
- **Depends on**: plan 031 by the agreed serial execution order
- **Blocks**: plan 033 by the agreed serial execution order
- **Category**: core capability
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

Optional callbacks are a normal API shape: a module can accept a function when
the caller wants notifications and `null`/omission when it does not. Plan 026
rejected nullable callback types because callback conversion retains a JS
function in `DotnetRuntimeContext`.

The correct nullable rule is small once conversion policy is typed: test
nullish first, return C# null without calling `AsFunction` or registering
anything, and delegate every non-null value to the existing callback codec.
Callbacks remain decode-only and context-retained. They do not belong to the
plan-030 invocation resource scope.

## Current state

### Callback codecs are decode-only and context-aware

`Codecs/JavaScriptCallbackCodec.cs` defines two static helper families:

```csharp
JavaScriptCallbackCodec<TResult, TResultCodec>
JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
```

Each `Decode` calls `value.AsFunction()` and then
`JavaScriptCallback<...>.FromOwnedFunction(context, ...)`. There is no `Encode`
method.

`JavaScriptCallback<TArgs,TResult>.FromOwnedFunction` calls
`context.RegisterRetainedCallback(...)`. `DotnetRuntimeContext` retains the
callback until explicit callback disposal or context teardown. Generated
argument glue does not dispose it at function return, because authored module
code may retain and invoke it later.

### Generator policy is now explicit after plan 029

Before plan 029, callback context requirement came from
`IsJavaScriptCallbackType` and callback codec text. Plan 029 requires a
descriptor with:

- `ContextRequirement = DotnetRuntimeContext`;
- decode capability;
- no encode capability;
- callback/context retention distinct from invocation-owned wrapper cleanup.

Plan 030 must not register callbacks in `JavaScriptConversionScope`. Keep that
separation here.

### Plan 026 rejects nullable callbacks on every shape

The merged spec requires `EXPOJSI008` for a nullable callback parameter and no
retained conversion. `TryGetNullableReferenceCodec` handles the type before the
non-null callback branch so it cannot fall through.

Typed events and records containing callbacks are independently rejected
because callbacks are decode-only. Properties and returns cannot use non-null
callbacks either. This plan does not widen those positions.

## Exact support contract

Support:

- `JavaScriptCallback<TResult>?` as a module sync/async method parameter;
- `JavaScriptCallback<TArgs,TResult>?` as a module sync/async method parameter;
- the same nullable callback parameter forms on supported shared-object
  constructors and methods;
- optional parameters with an authored `= null` default.

Do not support:

- callback returns or `Task<JavaScriptCallback<...>?>`;
- callback properties;
- callback record fields, lists, dictionaries, or nested containers;
- callback typed event payloads;
- nullable callback argument/result types inside another callback unless their
  non-null equivalents already have a codec and the approved delta explicitly
  includes them. The default scope of this plan does not.

Semantics:

- JS null or undefined decodes to C# null.
- Omitted optional callback uses the authored default.
- Nullish decode does not call `AsFunction`, retain a function, or register a
  callback.
- Non-null decode uses the existing callback codec unchanged.
- Non-null callback stays registered with `DotnetRuntimeContext`; generated
  glue does not invocation-dispose it.
- Context teardown and explicit callback disposal remain idempotent.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Runtime tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | all ModulesCore tests pass |
| Full regression | `scripts/test-managed.sh` | all managed tests pass, none skipped |
| Format | `scripts/format.sh --check --all` | exit 0 |
| Callback scope scan | `rg -n 'JavaScriptConversionScope.*Callback|\\.Register\\([^\\n]*JavaScriptCallback|Own[^\\n]*JavaScriptCallback' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` | no callback registered with the invocation conversion scope |

## Suggested executor toolkit

- Read `.agents/skills/living-spec-workflow/SKILL.md`.
- Use `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md` to verify the
  context-retained callback owner. Do not generalize callback ownership into the
  invocation scope.

## Scope

**In scope**:

- `docs/changes/<yyyy-mm-dd>-nullable-javascript-callback-parameters/`
- `docs/archive/changes/<yyyy-mm-dd>-nullable-javascript-callback-parameters/`
- `docs/specs/ownership-and-scoped-refs.md`
- `docs/specs/modules-core-boundary.md`
- `docs/module-authoring-guide.md`
- `docs/plans/README.md`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptCallbackCodec.cs`
- A dedicated nullable callback codec file in the same directory, if that keeps
  the two arities clear
- The plan-029 descriptor/model files under `Expo.ModulesCore.Generator/`
- `ExpoModulesGenerator.Codecs.cs`
- `ExpoModulesGenerator.ModuleAnalysis.cs`
- `ExpoModulesGenerator.SharedObjectModel.cs`
- `ExpoModulesGenerator.Emission.cs`
- Generator tests and fixtures under `Expo.ModulesCore.Generator.Tests/`
- Runtime callback/module fixtures and tests under `Expo.ModulesCore.Tests/`

**Out of scope**:

- Changes to `JavaScriptCallback.cs` or `DotnetRuntimeContext.cs` unless a test
  exposes a real bug in the existing non-null path; such a bug is a STOP
  condition for this plan
- Callback encoding or any callback return/property/event support
- Nested callback records or collections
- Registering callbacks in `JavaScriptConversionScope`
- ArrayBuffer, JavaScriptValue, or shared-object nullable behavior
- Native code, ABI, scheduler, or platform adapters

## Git workflow

- Branch: `advisor/032-nullable-javascript-callback-parameters`
- Commit approved delta spec and change-local plan before source.
- Keep codec/generator work, runtime tests, and merged docs as logical commits.
- Suggested implementation commit:
  `feat(modules-core): support nullable callback parameters`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Specify parameter-only support

Create the delta spec with the exact supported and unsupported positions above.
Replace the callback part of plan 026's blanket exclusion with a parameter-only
requirement. State that callback ownership is `DotnetRuntimeContext`, not
`JavaScriptConversionScope`.

Get approval and commit the delta, then approve and commit the change-local
plan.

**Verify**: `git log -2 --oneline --name-only` shows docs-only delta-spec and
change-local-plan commits.

### Step 2: Add generator acceptance and rejection tests

Add acceptance tests for both callback arities on:

- sync module method;
- async module method;
- optional `= null`;
- supported shared-object constructor;
- supported shared-object method.

For each, assert generated code:

- checks nullish before callback decode;
- passes the exact runtime context only for non-null decode;
- invokes authored code with a nullable C# local;
- emits no conversion-scope registration or callback disposal.

Keep rejection tests for return, property, record, collection, module event, and
shared-object event positions. Assert their existing context-specific diagnostic
IDs.

**Verify**: acceptance cases fail with plan-026 diagnostics before production
changes; rejection cases pass.

### Step 3: Add null-aware callback decode

Add one null-aware codec family with two generic arities mirroring
`JavaScriptCallbackCodec`. Each exposes decode only and returns nullable
callback. It must check `IsNullish` before `AsFunction` and before touching
`DotnetRuntimeContext`.

Update descriptor resolution:

- nullable callback parameter descriptor is handled and valid;
- it keeps `ContextRequirement = DotnetRuntimeContext`;
- it remains decode-only;
- it is marked context-retained, not invocation-owned;
- unsupported positions reject it from capabilities instead of falling through
  to a non-null codec.

Do not place a generic nullable-reference `IJavaScriptCodec<T>` wrapper around
the callback helper; the callback helper does not implement that interface and
requires context.

**Verify**: generator tests pass and the callback scope scan has no match.

### Step 4: Prove no registration on null

Add Hermes-backed runtime tests with callback/context counters or observable
context behavior:

- omitted optional callback reaches authored code as null;
- explicit null and undefined reach authored code as null;
- nullish calls create no retained callback registration;
- non-null callback invokes successfully;
- non-null callback remains usable after the module method returns;
- explicit callback disposal releases once;
- context teardown releases an undisposed callback once;
- nullable callback on an async method remains usable after `await`.

If the harness lacks a callback registration counter, add a test-only
observation seam inside existing test fixtures. Do not change public runtime API
solely for a counter.

**Verify**: ModulesCore tests pass with deterministic assertions.

### Step 5: Regress and merge

Run full managed tests, format, `git diff --check`, and the scope scan. Merge the
delta into the living specs and authoring guide, archive it, and mark plan 032
DONE with commits/test count.

**Verify**:

```sh
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: clean tree; the privacy scan prints nothing and exits 1.

## Test plan

- Generator tests prove capability-based position acceptance and rejection.
- Runtime tests prove the null branch has zero registration and the non-null
  branch keeps existing context lifetime.
- Cover both callback generic forms and sync/async authored methods.
- Keep existing callback invocation-after-teardown tests enabled.

## Done criteria

- [ ] Plan 031 is DONE.
- [ ] Approved delta and change plan were committed first.
- [ ] Both nullable callback arities work as method parameters.
- [ ] Supported shared-object constructor/method parameters work.
- [ ] Optional omission, null, and undefined reach authored code as null.
- [ ] Nullish decode performs no `AsFunction` or context registration.
- [ ] Non-null callbacks keep context-retained behavior.
- [ ] Generated glue never registers/disposes callbacks through the conversion
  scope.
- [ ] Returns, properties, records, collections, and events remain unsupported.
- [ ] Generator, runtime, full managed tests, and format pass without skips.
- [ ] Specs/guide are merged, package archived, and plan 032 marked DONE.

## STOP conditions

Stop and report if:

- Nullish decode reaches `AsFunction` or registers with the context.
- Supporting nullable callbacks requires callback encode.
- Generated glue must dispose callbacks at invocation end.
- The implementation needs changes to callback/context lifetime internals.
- A generic nullable adapter accidentally makes callback returns or containers
  valid.
- Diagnostic capability checks cannot distinguish decode-only positions.
- A verification fails twice or an out-of-scope file is needed.

## Maintenance notes

Callbacks have one owner: `DotnetRuntimeContext`, until explicit disposal or
context teardown. The conversion scope owns invocation-scoped wrapper values,
not retained callbacks. Keep that division even if both types implement
`IDisposable`.
