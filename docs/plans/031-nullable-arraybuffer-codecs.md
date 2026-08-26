# Plan 031: Support nullable ArrayBuffer across scoped generated boundaries

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
> git diff --stat 512ab46e..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectEventEmitter.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests docs/specs/ownership-and-scoped-refs.md docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md
> ```
> Plans 028–030 must be DONE. Rebase the drift check to plan 030's final commit.
> If the scoped ArrayBuffer adapter, nullability dispatch, event payload
> classification, or plan-026 exclusion diagnostics differ from "Current
> state," stop and reconcile before coding.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: HIGH (adds a null state to a resource-bearing codec on every
  generated surface, including async and borrowed event paths)
- **Depends on**: plan 030
- **Blocks**: plans 032 and 033 by the agreed serial execution order
- **Category**: core capability
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

Plan 026 deliberately rejected `ArrayBuffer?` because cleanup did not compose.
Plan 030 supplies the missing mechanism. This plan applies that mechanism:
JavaScript `null`/`undefined` maps to C# `null`, a non-null buffer follows the
same scoped rules as `ArrayBuffer`, and null registers no cleanup.

Support is not limited to direct parameters. Nullable buffers must work inside
records and current collection shapes. Direct nullable typed events reuse the
existing direct ArrayBuffer scheduling-lease path; nested resource-bearing event
payloads remain unsupported. `JavaScriptValue?` remains unsupported because
`JavaScriptValue` already represents and inspects JavaScript null/undefined
explicitly.

## Current state

### Plan 026 rejects ArrayBuffer before fallback

`ExpoModulesGenerator.Codecs.cs:131-225` handles annotated references before
ordinary codec resolution. It wraps regular interface codecs with
`NullableReferenceCodec<T,TCodec>` and uses dedicated nullable collection
adapters, but marks `ArrayBuffer`, `JavaScriptValue`, callbacks, and shared
objects as handled-but-unsupported.

That distinction is required. Returning ordinary "no match" would fall through
to the non-null `ArrayBufferCodec` and emit unsafe glue.

### The merged spec records the temporary exclusion

`docs/specs/modules-core-boundary.md`, under
"Ownership-Bearing Nullable References Are Build Diagnostics," requires:

- `ArrayBuffer?` parameter/return diagnostics `EXPOJSI001`/`EXPOJSI002`;
- record-field diagnostic `EXPOJSI007`;
- property diagnostic `EXPOJSI015`;
- module/shared-object event diagnostics `EXPOJSI019`/`EXPOJSI027`;
- no binding emission and no non-null codec fallback.

`docs/module-authoring-guide.md:151` lists `ArrayBuffer?` among unsupported
annotated references. This plan removes only the ArrayBuffer parts of that
temporary contract.

### Direct typed events bypass ordinary codec resolution

`ExpoModulesGenerator.EventAnalysis.cs` has dedicated payload kinds for direct
`JavaScriptValue` and `ArrayBuffer`. Plan 026 had to reject
`Func<ArrayBuffer?, Task>` inside `GetUnsupportedEventPayload`, because that
shape bypasses `GetCodecExpression`. Plan 031 must update that direct event
classification without removing the nested-resource event diagnostic.

The generated delegate also needs a callable runtime path:

- `ModuleEventEmitter.cs:120-139` has a direct `ArrayBuffer` overload that
  rejects null and synchronously retains an invocation lease.
- `SharedObjectEventEmitter.cs:26-39` has the equivalent shared-object event
  overload.
- Their generic overloads require `IJavaScriptCodec<T>` and cannot accept the
  scoped ArrayBuffer helper.

Change the direct overload parameters to `ArrayBuffer?`. Null must schedule a JS
null payload without retaining a lease. Non-null must run the existing retain
path unchanged. Nullable annotation does not create a distinct C# overload, so
do not add a second overload with the same signature.

### Plan 030 is the required base

The implementation must use plan 030's:

- `JavaScriptConversionScope`;
- scope-aware ArrayBuffer codec;
- recursive scope-aware record/list/dictionary codecs;
- the existing direct event scheduling lease, and return/property-get
  `Transfer` followed by `EncodeBorrowed`;
- reference-identity deduplication and partial-failure cleanup.

Do not add a nullable-specific disposal path.

## Exact support contract

`ArrayBuffer?` is supported anywhere the same shape with non-null
`ArrayBuffer` is supported after plan 030:

- required and optional sync/async function parameters;
- sync return and `Task<ArrayBuffer?>`;
- readable/writable properties;
- generated record fields;
- existing `IReadOnlyList<T>`, dictionary, and nested record/collection shapes;
- direct module and shared-object typed event payloads;
- supported shared-object constructor and member boundaries.

Semantics:

- JS `null` or `undefined` decodes to C# `null`.
- C# `null` encodes as JS `null`.
- Null registers nothing in `JavaScriptConversionScope`.
- Non-null decode is invocation-owned and follows the same scope as
  `ArrayBuffer`.
- Non-null return/property-get encode transfers the source wrapper.
- Non-null direct event dispatch synchronously retains the same scheduling lease
  as non-null `ArrayBuffer`, so the caller may dispose the original after the
  event invocation returns.
- Omitted optional arguments and explicit `undefined` use the authored default,
  matching plan 026.
- Explicit `null` decodes to null even if the authored optional default is
  non-null.

No new diagnostic ID is needed. Former exclusion diagnostics disappear only
where the complete containing shape is now supported.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Runtime tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | all ModulesCore tests pass |
| Full regression | `scripts/test-managed.sh` | all managed tests pass, none skipped |
| Format | `scripts/format.sh --check --all` | exit 0 |
| Exclusion scan | `rg -n 'ArrayBuffer.*(unsupported nullable|nullable annotation)|IsNullable.*ArrayBuffer|JavaScriptValue.*ArrayBuffer' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator docs/module-authoring-guide.md` | no combined JSValue/ArrayBuffer exclusion remains; JavaScriptValue-only exclusion may remain |

## Suggested executor toolkit

- Read `.agents/skills/living-spec-workflow/SKILL.md`.
- Use `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`.
- Start with generator diagnostics and native-counter runtime tests before
  changing resolver behavior.

## Scope

**In scope**:

- `docs/changes/<yyyy-mm-dd>-nullable-arraybuffer-codecs/`
- `docs/archive/changes/<yyyy-mm-dd>-nullable-arraybuffer-codecs/`
- `docs/specs/ownership-and-scoped-refs.md`
- `docs/specs/modules-core-boundary.md`
- `docs/module-authoring-guide.md`
- `docs/plans/README.md`
- The scope-aware nullable/ArrayBuffer codec files under
  `Expo.ModulesCore/Codecs/`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectEventEmitter.cs`
- The plan-029 descriptor/model files under `Expo.ModulesCore.Generator/`
- `ExpoModulesGenerator.Codecs.cs`
- `ExpoModulesGenerator.ModuleAnalysis.cs`
- `ExpoModulesGenerator.SharedObjectModel.cs`
- `ExpoModulesGenerator.EventAnalysis.cs`
- `ExpoModulesGenerator.Emission.cs`
- Generator tests and fixtures under `Expo.ModulesCore.Generator.Tests/`
- Runtime tests and fixtures under `Expo.ModulesCore.Tests/`

**Out of scope**:

- `JavaScriptValue?`
- Nullable callbacks or shared objects
- New collection types
- Callback argument/result positions that reject non-null ArrayBuffer
- Copying buffer bytes to avoid ownership rules
- Changes to `ArrayBuffer.Dispose`, `ArrayBuffer.Retain`, native backing-store
  ownership, ABI, scheduler, or platform code
- Any per-surface nullable disposal mechanism outside the plan-030 scope

## Git workflow

- Branch: `advisor/031-nullable-arraybuffer-codecs`
- Commit approved delta spec and change-local plan before source changes.
- Keep codec/generator behavior, runtime coverage, and merged docs as logical
  commits.
- Suggested implementation commit:
  `feat(modules-core): support nullable ArrayBuffer codecs`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Replace the temporary exclusion in a delta spec

Specify the exact support contract above. Amend the plan-026 exclusion
requirement so it lists `JavaScriptValue`, callbacks, and shared-object families,
not `ArrayBuffer`. Add direct, record/collection, optional-default, property,
direct-event, and shared-object scenarios. Preserve nested-resource event
rejection.

Get approval and commit the delta, then approve and commit its implementation
plan.

**Verify**: `git log -2 --oneline --name-only` shows only the delta spec and
change-local plan package.

### Step 2: Turn existing rejection tests into acceptance tests

Keep at least one explicit `JavaScriptValue?` rejection beside the new
ArrayBuffer acceptance cases so the change cannot accidentally enable every
excluded type.

Update/add generator tests for:

- parameter, sync return, async return;
- optional omitted, explicit undefined, explicit null;
- property getter/setter;
- record field and record inside list/dictionary;
- list/dictionary of nullable buffers;
- module and shared-object direct typed events;
- nested event record/collection rejection;
- supported shared-object constructor and member;
- unsupported containing shapes, which must retain their existing diagnostic.

Assert generated null short-circuit, scoped non-null delegation,
returns/getters calling `Transfer` first, and direct events retaining a non-null
scheduling lease before returning. Do not assert only compilation.

**Verify**: new acceptance tests fail before production changes for the expected
plan-026 diagnostics.

### Step 3: Add nullable scoped ArrayBuffer conversion

Prefer the generic nullable-reference scoped adapter introduced by plan 030 if
its constraints fit ArrayBuffer. Otherwise add one
`NullableArrayBufferScopedCodec`; do not add separate parameter, property,
event, or return helpers.

Its operations must short-circuit before touching the non-null codec:

```text
Decode(nullish)      -> null, no registration
Decode(non-nullish)  -> scoped ArrayBuffer decode
Transfer(null)       -> no registration
Transfer(non-null)   -> delegate to scoped ArrayBuffer transfer
EncodeBorrowed(null) -> runtime.CreateNull()
EncodeBorrowed(value)-> delegate to scoped ArrayBuffer borrowed encode
```

Update `ExpoCodecDescriptor` resolution so the nullable descriptor preserves
the inner ArrayBuffer resource policy recursively. Remove ArrayBuffer from the
handled-but-unsupported nullable-reference set.

**Verify**: generator tests pass for direct and recursive generated source.

### Step 4: Update direct event classification

First change the two direct emitter method signatures from `ArrayBuffer` to
`ArrayBuffer?`.

- Null schedules dispatch with one explicit JS null payload. It is not the
  payload-less event overload.
- Null creates no retained ArrayBuffer lease.
- Non-null retains the invocation lease synchronously before returning the
  dispatch Task, exactly as today.
- Validation, cancellation, scheduling, and exception behavior stay unchanged.

Teach direct typed event analysis to recognize annotated ArrayBuffer as the same
payload family plus nullability. Remove the plan-026 event exclusion only for
ArrayBuffer. Emit JS null for null. For non-null, reuse the current direct
ArrayBuffer path that synchronously retains an independent lease before the
dispatch Task is returned. Do not route this path through transfer cleanup.
Keep nested resource-bearing event payloads rejected.

The event property itself remains async `Func<T, Task>` and keeps all existing
scheduling/error behavior.

**Verify**: module and shared-object event generator tests pass; direct
`JavaScriptValue?` event tests still report `EXPOJSI019`/`EXPOJSI027`.
Hermes-backed emitter tests prove explicit null payload versus payload-less
dispatch and zero retained lease for null.

### Step 5: Prove runtime lifetime behavior

Add Hermes-backed fixtures and counter assertions:

- null/undefined decode creates no backing-store lease;
- non-null direct parameter releases once after sync call;
- async parameter remains usable after an `await`, then releases once;
- null return encodes as JS null;
- non-null sync/async return disposes source but leaves JS result readable;
- nullable property setter null/non-null and retain-before-store behavior;
- module and shared-object null events deliver one explicit JS null payload and
  create no retained lease;
- direct event non-null source may be disposed after invocation returns while
  dispatch still succeeds through its retained lease;
- nested list/dictionary/record null leaves create no registration;
- duplicate non-null leaf references dispose once in transfer mode;
- failure after a non-null earlier field cleans it once.

**Verify**: ModulesCore tests pass; new assertions use deterministic counters.

### Step 6: Regress and merge

Run full managed tests, format, `git diff --check`, and the exclusion scan.
Merge the delta into living specs and the authoring guide, archive it, and mark
plan 031 DONE with commits and test count.

**Verify**:

```sh
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: clean tree; the privacy scan prints nothing and exits 1.

## Test plan

- Generator tests cover every supported boundary family, both null branches,
  and nested-event rejection.
- Runtime tests distinguish null, undefined, non-null, omitted optional default,
  borrow, transfer, alias, failure, and async lifetime.
- Use ArrayBuffer/native lease counters, not only decoded byte equality.
- Existing non-null ArrayBuffer tests and every plan-026 regular nullable test
  remain enabled.

## Done criteria

- [ ] Plan 030 is DONE.
- [ ] Approved delta and change plan were committed before source.
- [ ] `ArrayBuffer?` is supported on every listed direct boundary.
- [ ] Nullable buffers compose through current records/lists/dictionaries.
- [ ] Direct typed events reuse the existing scheduling lease; nested
  resource-bearing typed events remain unsupported.
- [ ] Module and shared-object null events dispatch one explicit JS null payload
  with zero retained lease.
- [ ] Null and undefined decode to null and register no resource.
- [ ] Null encodes as JS null.
- [ ] Optional authored defaults keep plan-026 semantics.
- [ ] Non-null values reuse the plan-030 scope with no new disposal path.
- [ ] `JavaScriptValue?` remains rejected on every boundary.
- [ ] Unsupported containing shapes remain unsupported.
- [ ] Generator, runtime, full managed tests, and format pass without skips.
- [ ] Specs/guide are merged, package archived, and plan 031 marked DONE.

## STOP conditions

Stop and report if:

- Null creates a scope registration or native backing-store lease.
- Supporting nested ArrayBuffer requires copying bytes.
- Direct events must consume or dispose authored payload wrappers.
- A separate cleanup mechanism is needed outside plan 030.
- `JavaScriptValue?` becomes accepted through a generic wrapper.
- A non-null ArrayBuffer surface regresses.
- Callback support or a new collection family becomes necessary.
- A verification fails twice or an out-of-scope file is needed.

## Maintenance notes

ArrayBuffer nullability is now ordinary policy layered over the scoped
resource codec. Future resource codecs should follow this pattern: null
short-circuits, non-null delegates, and the conversion scope remains the only
owner.
