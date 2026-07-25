# Plan 033: Support nullable concrete SharedObject and SharedRef-derived classes

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
> git diff --stat 512ab46e..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObject.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedRef.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/SharedObjectRegistry.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests docs/specs/ownership-and-scoped-refs.md docs/specs/modules-core-boundary.md docs/module-authoring-guide.md docs/plans/README.md
> ```
> Plans 028–032 must be DONE. Rebase the drift check to plan 032's final commit.
> If exact-type shared-object validation, registry identity, async context
> capture, or nullable descriptor handling differs from "Current state," stop
> and reconcile before coding.

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: HIGH (null must bypass the context registry while every non-null
  path preserves exact type, context identity, release, and teardown rules)
- **Depends on**: plan 032 by the agreed serial execution order; plans 028 and
  029 are direct technical prerequisites
- **Category**: core capability
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

A module API often has an optional object reference: lookup can return no
object, and a setter can clear a selected object. The bridge already preserves
strict identity for concrete generated shared objects, but plan 026 rejects a
nullable annotation before `SharedObjectCodec<T>` is selected.

Support must stay exact. Nullable concrete sealed `[ExpoSharedObject]` classes
are valid, including concrete subclasses of `SharedRef<T>`. The polymorphic
`SharedObject` and `SharedRef<T>` bases remain invalid because their non-null
forms are invalid generated boundaries. Null performs no registry work;
non-null delegates to the existing exact codec.

## Current state

### SharedObject conversion is exact and context-owned

`Codecs/SharedObjectCodec.cs` is a context-aware static helper:

```csharp
public static class SharedObjectCodec<T> where T : SharedObject
```

- `Decode` resolves through `runtimeContext.SharedObjects.ResolveManaged`.
- It requires `managed is T` and `instance.GetType() == typeof(T)`.
- `Encode` requires `value.GetType() == typeof(T)`.
- It uses `GeneratedSharedObjectClass.GetRegistration` and
  `SharedObjects.GetOrCreateJavaScriptObject`.
- Repeated encode returns the paired strictly equal JS object.
- The managed instance remains caller-owned; this is registry identity, not an
  invocation-owned disposable wrapper.

The codec requires `DotnetRuntimeContext` but does not implement
`IJavaScriptCodec<T>`.

### Only concrete attributed classes are direct boundary types

`ExpoModulesGenerator.SharedObjectValidation.cs:113-136` currently validates:

- `SharedObject` base: rejected as polymorphic;
- `SharedRef<T>` base: rejected as a managed carrier;
- un-attributed, nested, generic, or non-sealed classes: rejected;
- nullable concrete class: rejected with "must be used without a nullable
  annotation";
- valid top-level, non-generic, sealed `[ExpoSharedObject]` class: selects
  `SharedObjectCodec<ExactType>`.

Nested shared-object types in records, collections, tuples, or callbacks report
`EXPOJSI023` because shared-object types are supported only directly.

Plan 028 makes type identity annotation-insensitive. Plan 029 makes
shared-object category and runtime-context requirement explicit instead of
parsing codec text.

### SharedRef is a base, not a boundary codec

`docs/specs/modules-core-boundary.md`, "SharedRef Is A Non-Owning
SharedObject," requires:

- `SharedRef<T>` is a derivable managed carrier base;
- direct `SharedRef<T>` boundary use reports `EXPOJSI023`;
- a concrete sealed non-generic `[ExpoSharedObject]` subclass of
  `SharedRef<T>` uses that concrete class's exact codec and prototype.

Nullability does not change this division.

### Supported non-null positions define nullable positions

The current spec names concrete shared-object parameter, return, constructor
parameter, and property boundaries. Generated shared-object methods use the same
function model. Typed-event payloads and shared objects nested inside composed
codecs are not current shared-object boundary positions. Do not widen them here.

## Exact support contract

Support nullable annotation on an otherwise valid exact concrete class:

```csharp
[ExpoSharedObject]
public sealed partial class CacheEntry : SharedObject { ... }

[ExpoSharedObject]
public sealed partial class ImageRef : SharedRef<NativeImage> { ... }
```

Supported positions:

- module sync/async method parameter;
- module sync return and `Task<T?>`;
- readable/writable module property;
- supported shared-object constructor parameter;
- supported shared-object method parameter, sync/async return, and property.

Still unsupported:

- `SharedObject?`;
- open or constructed `SharedRef<T>?`;
- nullable un-attributed, nested, generic, or non-sealed subclasses;
- concrete shared objects inside records, lists, dictionaries, tuples, callback
  types, or typed event payloads;
- a nullable position whose non-null equivalent is unsupported.

Semantics:

- JS null or undefined decodes to C# null.
- C# null encodes as JS null.
- Null does not call `AsObject`, resolve registry identity, get class
  registration, or create/pair a JS object.
- Non-null decode/encode delegates to `SharedObjectCodec<T>` unchanged.
- Exact runtime type, owning context, strict JS identity, released-object
  rejection, and teardown behavior remain unchanged.
- Optional omission/undefined uses the authored default under plan-026 rules.
- Shared objects do not register with `JavaScriptConversionScope`.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Runtime tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | all ModulesCore tests pass |
| Full regression | `scripts/test-managed.sh` | all managed tests pass, none skipped |
| Format | `scripts/format.sh --check --all` | exit 0 |
| Scope scan | `rg -n 'JavaScriptConversionScope.*SharedObject|Own[^\\n]*Shared(Object|Ref)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` | no shared object registered with the conversion scope |
| Polymorphic codec scan | `rg -n 'NullableSharedObjectCodec<(global::Expo\\.ModulesCore\\.)?(SharedObject|SharedRef)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests` | no generated nullable codec for polymorphic bases |

## Suggested executor toolkit

- Read `.agents/skills/living-spec-workflow/SKILL.md`.
- Use `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md` to audit
  registry/context ownership and teardown.
- Add identity and zero-registry-operation tests before enabling the generator
  branch.

## Scope

**In scope**:

- `docs/changes/<yyyy-mm-dd>-nullable-concrete-shared-object-codecs/`
- `docs/archive/changes/<yyyy-mm-dd>-nullable-concrete-shared-object-codecs/`
- `docs/specs/ownership-and-scoped-refs.md`
- `docs/specs/modules-core-boundary.md`
- `docs/module-authoring-guide.md`
- `docs/plans/README.md`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/SharedObjectCodec.cs`
- A dedicated nullable shared-object codec file in the same directory
- The plan-029 descriptor/model files under `Expo.ModulesCore.Generator/`
- `ExpoModulesGenerator.Codecs.cs`
- `ExpoModulesGenerator.ModuleAnalysis.cs`
- `ExpoModulesGenerator.SharedObjectModel.cs`
- `ExpoModulesGenerator.SharedObjectValidation.cs`
- `ExpoModulesGenerator.Emission.cs`
- Generator tests and shared-object fixtures under
  `Expo.ModulesCore.Generator.Tests/`
- Runtime tests and shared-object fixtures under `Expo.ModulesCore.Tests/`

**Out of scope**:

- Changes to `SharedObject`, `SharedRef<T>`, `SharedObjectRegistry`,
  `GeneratedSharedObjectClass`, or release/teardown implementation unless tests
  expose a pre-existing bug; that is a STOP condition
- Polymorphic base codecs
- Nested/composed shared-object codecs
- Shared-object typed event payload values
- Callback argument/result support
- `JavaScriptConversionScope` ownership of shared objects
- Native ABI, runtime scheduler, platform adapters, or TypeScript facade changes

## Git workflow

- Branch: `advisor/033-nullable-concrete-shared-object-codecs`
- Commit approved delta spec and change-local plan before source.
- Keep codec/generator behavior, registry integration tests, and merged docs as
  logical commits.
- Suggested implementation commit:
  `feat(modules-core): support nullable concrete shared objects`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Specify exact nullable shared-object boundaries

Create the delta spec with the support/unsupported matrix above. Replace the
concrete-class portion of plan 026's exclusion; retain diagnostics for
`SharedObject?`, `SharedRef<T>?`, invalid concrete classes, and every nested
shape.

State directly that a concrete attributed subclass of `SharedRef<T>` is a
supported exact class, while `SharedRef<T>` itself is not.

Get approval and commit the delta, then approve and commit the change-local
plan.

**Verify**: `git log -2 --oneline --name-only` shows only the delta spec and
change-local plan package.

### Step 2: Add generator boundary tests

Add acceptance cases for both a direct `SharedObject` subclass and a concrete
`SharedRef<T>` subclass on every supported position. Assert:

- codec generic argument strips nullable annotation;
- null-aware codec is selected;
- runtime context is passed on decode and encode;
- async result captures the exact context before the host frame exits;
- no conversion scope registration is emitted.

Keep rejection tests for polymorphic bases, invalid concrete classes, nested
records/collections/callbacks, and events. Verify `EXPOJSI023` from plan 028.

**Verify**: acceptance cases fail with `EXPOJSI023` before production changes;
rejection cases pass.

### Step 3: Add the nullable exact codec

Add one generic helper:

```text
NullableSharedObjectCodec<T> where T : SharedObject
```

with ref/owned decode and encode overloads matching `SharedObjectCodec<T>`.
Each method checks nullish/null first and delegates non-null values. Do not
duplicate registry logic.

Update descriptor resolution and shared-object validation:

- annotation-insensitive classification still finds the exact class;
- `GetDirectSharedObjectBoundaryIssue` accepts nullable annotation only after
  every structural/attribute/exact-class rule passes;
- codec type argument uses the non-annotated exact type;
- descriptor retains shared-object category, context requirement, and
  decode/encode capabilities;
- bases and nested shapes remain handled diagnostics, never codec fallback.

**Verify**: generator tests pass; scope and polymorphic-codec scans have no
matches.

### Step 4: Prove null bypass and non-null identity

Add Hermes-backed tests for:

- null and undefined parameter decode to null;
- optional omission uses authored default;
- null return/property getter produces JS null;
- null setter clears authored state;
- null path produces zero resolve/pair/create registry operations;
- repeated non-null encode remains strictly equal in JS;
- non-null decode returns the original managed instance;
- exact-type mismatch, foreign context, plain object, and released object retain
  existing failures;
- async nullable return captures and uses the original runtime context;
- context teardown behavior remains terminal;
- concrete `SharedRef<T>` subclass follows the same identity behavior and does
  not dispose its carried `T` unless its own override does so.

Use existing registry counters/test hooks. If no counter can distinguish the
null path, add an internal test-only observation in the test fixture, not public
runtime API.

**Verify**: ModulesCore tests pass with deterministic identity/counter
assertions.

### Step 5: Regress and merge

Run full managed tests, format, `git diff --check`, and both scans. Merge the
delta into living specs and authoring guide, archive it, and mark plan 033 DONE
with commits/test count.

**Verify**:

```sh
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: clean tree; the privacy scan prints nothing and exits 1.

## Test plan

- Generator acceptance/rejection covers both concrete inheritance forms and all
  listed positions.
- Runtime tests prove zero registry work for null and exact identity for
  non-null.
- Existing cross-context, released-object, teardown, and SharedRef non-ownership
  tests remain enabled.
- Include async return context capture; source snapshots alone cannot prove it.

## Done criteria

- [ ] Plans 028–032 are DONE.
- [ ] Approved delta and change plan were committed first.
- [ ] Nullable valid concrete SharedObject classes work on listed boundaries.
- [ ] Nullable concrete `[ExpoSharedObject]` subclasses of `SharedRef<T>` work.
- [ ] `SharedObject?` and direct `SharedRef<T>?` remain `EXPOJSI023`.
- [ ] Invalid and nested concrete shapes remain unsupported.
- [ ] Null/undefined decode to null and null encodes as JS null.
- [ ] Null performs no shared-object registry or class-registration work.
- [ ] Non-null exact identity, context, release, and teardown behavior is
  unchanged.
- [ ] No shared object enters `JavaScriptConversionScope`.
- [ ] Generator, runtime, full managed tests, and format pass without skips.
- [ ] Specs/guide are merged, package archived, and plan 033 marked DONE.

## STOP conditions

Stop and report if:

- Supporting nullable concrete types requires a polymorphic base codec.
- Null touches `AsObject`, class registration, registry pairing/resolution, or
  a disposed runtime context.
- Non-null repeated encode loses strict JS identity.
- A nested shared-object shape becomes valid through generic nullable handling.
- Shared objects must enter `JavaScriptConversionScope`.
- Registry, SharedObject, SharedRef, release, or teardown internals need changes.
- A verification fails twice or an out-of-scope file is needed.

## Maintenance notes

The rule after this plan is simple: nullable support follows the exact concrete
class, never the base. If a future non-null shared-object position becomes
supported, its nullable form can use this helper. Do not infer prototype or
codec selection from assignability.
