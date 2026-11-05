# Nullable Concrete Shared-Object Codecs

## Goal

A nullable annotation on an otherwise valid exact concrete
`[ExpoSharedObject]` class works on every boundary where the non-null class
works: module parameters, sync returns, `Task<T?>`, properties, and
shared-object constructor parameters, methods, and properties. This includes
a concrete sealed subclass of `SharedRef<T>`. JavaScript `null` and
`undefined` decode to C# `null`; C# `null` encodes as JavaScript `null`.
Null performs no registry work. Non-null delegates to `SharedObjectCodec<T>`
unchanged. This is roadmap item 033.

`SharedObject?` and `SharedRef<T>?` stay `EXPOJSI023`, because their
non-null forms are rejected. Nested shapes stay rejected.

## Process note

The brief `docs/plans/033-nullable-concrete-shared-object-codecs.md`
predates 028–032. Its drift check diffs by design. This spec is reconciled
against the 032 close commit and wins over the brief where they differ.

## Design criteria

1. **Clean code.** One null test per operation, then delegate. The exact
   codec keeps all registry logic.
2. **Performance.** Null allocates nothing beyond `runtime.CreateNull()` on
   encode.
3. **Correctness.** Exact type, owning context, strict identity, released
   rejection, and teardown behavior are untouched. Shared objects never
   enter `GeneratedConversionScope`.

## Scope

### In scope

- `Expo.ModulesCore/Codecs/NullableSharedObjectCodec.cs` (new).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`.
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`,
  `docs/roadmap.md`.

### Out of scope

- `SharedObject?`, `SharedRef<T>?`, and invalid concrete classes.
- Shared objects in records, lists, dictionaries, tuples, callbacks, or
  typed event payloads.
- Any change to `SharedObject`, `SharedRef<T>`, `SharedObjectRegistry`,
  `GeneratedSharedObjectClass`, or `SharedObjectCodec<T>`.

## Accepted design

### Codec

```csharp
public static class NullableSharedObjectCodec<T> where T : SharedObject
{
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value is null ? runtime.CreateNull() : SharedObjectCodec<T>.Encode(value, runtime, runtimeContext);
}
```

Both `JavaScriptValueRef` and `JavaScriptValue` expose `IsNullish`, so each
overload reads `value.IsNullish` directly. Same signatures as
`SharedObjectCodec<T>`, so emission does not change.

### Generator

`GetDirectSharedObjectBoundaryIssue` drops its last rule. A top-level,
non-generic, sealed `[ExpoSharedObject]` class is valid with or without the
annotation. `TryAnalyzeSharedObjectBoundaryType` selects
`NullableSharedObjectCodec<Exact>` when `typeSymbol.NullableAnnotation ==
Annotated`, else `SharedObjectCodec<Exact>`, where `Exact` is the type
printed without the annotation (`SymbolDisplayFormat.FullyQualifiedFormat`
already omits it). The descriptor is the existing `SharedObjectCodec(expr)`
row: `StaticHelper`, `DecodeAndEncode`, `DotnetRuntimeContext`, `Borrowed`.

Shared-object boundary analysis runs before `GetCodec` on every boundary,
and `IsSharedObjectRelatedType` is annotation-insensitive (028), so the
annotated class never reaches `TryGetNullableReferenceCodec`. Nested shapes
reach `TryFindNestedSharedObjectType` as today and stay `EXPOJSI023`.
`SharedObject?` and `SharedRef<T>?` hit the first two rules and stay
`EXPOJSI023` with their current reasons.

The async result path already captures the context before the host frame
exits for every `DotnetRuntimeContext` descriptor; the nullable codec reuses
it.

### Semantics

- JS `null`/`undefined` → C# `null`. No `AsObject`, no `ResolveManaged`,
  no `GetRegistration`, no pairing.
- C# `null` → JS `null`.
- Non-null: `SharedObjectCodec<T>` unchanged (exact type, same context,
  strict identity, released rejection).
- Optional `= null`: omission and `undefined` → default; explicit `null` →
  `null`.
- Not scope-registered.

### Proof of the null path

- Decode: `SharedObjectCodec<T>.Decode` calls `value.AsObject()`, which
  throws on `null`. A null that decodes without error proves the
  short-circuit. No new seam.
- Encode: `SharedObjectRegistry.PairingStepForTesting` (internal, existing)
  observes every pairing step. A null encode records no step. No new seam.

## Decisions made by the controller

1. **One static helper with the same three signatures** as
   `SharedObjectCodec<T>`, so emission is untouched. Cost if wrong: none.
2. **The boundary validator selects the codec by annotation**; no
   descriptor field for nullability. Cost if wrong: a flag to add later.
3. **No new test seam.** The `AsObject` throw and the existing pairing hook
   prove the null path. Cost if wrong: one internal counter later.
4. **Positions follow the non-null form exactly.** No widening.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

Remove "a concrete `[ExpoSharedObject]` class" from the rejected list and
from the shared-object identity paragraph. The paragraph becomes:
`SharedObject?` and `SharedRef<T>?` SHALL report `EXPOJSI023` with the
same reason as the non-null form. A valid concrete class annotated as
nullable SHALL select `NullableSharedObjectCodec<T>` (see "Nullable Concrete
Shared Objects Bypass The Registry On Null").

Scenario "Nullable SharedRef or concrete class reports the shared-object
code" becomes "Nullable SharedRef reports the shared-object code" with the
concrete class removed. Scenario "Valid concrete shared object stays
accepted" adds: and the same class annotated as nullable SHALL select
`NullableSharedObjectCodec<T>`.

### ADDED Requirement: Nullable Concrete Shared Objects Bypass The Registry On Null

A top-level, non-generic, sealed `[ExpoSharedObject]` class annotated as
nullable, including a concrete subclass of `SharedRef<T>`, SHALL be accepted
on every boundary that accepts the non-null class and SHALL select
`NullableSharedObjectCodec<T>` with the non-annotated exact type. JavaScript
`null` and `undefined` SHALL decode to C# `null` without resolving registry
identity. C# `null` SHALL encode as JavaScript `null` without class
registration or pairing. A non-null value SHALL use `SharedObjectCodec<T>`
unchanged. Shared objects SHALL NOT register with `GeneratedConversionScope`.

#### Scenario: Null parameter, return, and property

- **GIVEN** a module with a `T?` parameter, a `T?` return, a `Task<T?>`
  method, and a readable and writable `T?` property
- **WHEN** JavaScript passes `null` or `undefined`, and authored code
  returns or stores `null`
- **THEN** authored code SHALL receive `null`, and JavaScript SHALL receive
  `null`
- **AND** no pairing step SHALL be observed

#### Scenario: Non-null keeps identity

- **GIVEN** the same members
- **WHEN** JavaScript passes a live instance and authored code returns the
  same instance twice
- **THEN** authored code SHALL receive the original managed instance
- **AND** both JavaScript results SHALL be strictly equal to the input

#### Scenario: Existing failures stay

- **GIVEN** the `T?` parameter
- **WHEN** JavaScript passes a plain object, an instance of another shared
  class, an instance from another context, or a released instance
- **THEN** the call SHALL fail as it does for `T`

#### Scenario: Concrete SharedRef subclass

- **GIVEN** a sealed `[ExpoSharedObject]` class that derives from
  `SharedRef<X>` used as `T?`
- **WHEN** it crosses the boundary as `null` and as an instance
- **THEN** it SHALL behave as the scenarios above and SHALL NOT dispose its
  carried value

#### Scenario: Bases and nested shapes stay rejected

- **GIVEN** `SharedObject?`, `SharedRef<X>?`, `IReadOnlyList<T?>`, a record
  with a `T?` field, and `Func<T?, Task>`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the
  existing reasons

## Delta against `docs/specs/ownership-and-scoped-refs.md`

The file has no unsupported-nullable list, so nothing is removed. Add one
sentence: a nullable concrete `[ExpoSharedObject]` value that is `null`
holds nothing and is not scope-owned; a non-null one stays a borrowed
shared object owned by the registry.

## Module authoring guide

- Section 8 boundary rule: parameters, returns, and properties use the exact
  sealed authored type, optionally annotated as nullable; bases and nested
  composition stay rejected.
- Unsupported annotated references list: drop the concrete class; keep
  `SharedObject?` and `SharedRef<T>?`.

## Roadmap

Row 033 `IN PROGRESS` then `DONE` with the test count in the 028–033 note.
Brief and change folder archive.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'GeneratedConversionScope.*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
rg -n 'NullableSharedObjectCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --unified=0 d1060bd..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

All scans print nothing.
