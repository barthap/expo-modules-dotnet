# Annotation-Insensitive Shared-Object Boundaries

## Goal

A nullable annotation does not change shared-object type identity in the
generator. `SharedObject?` is the same unsupported polymorphic boundary as
`SharedObject`, so it reports `EXPOJSI023` with the polymorphic-base reason on
every generated boundary, as `SharedRef<T>?` and nullable concrete
`[ExpoSharedObject]` classes already do. This is roadmap item 028 and the
diagnostic baseline that 029 and 033 build on.

This change selects a different diagnostic only. No boundary becomes valid,
and no boundary becomes invalid.

## Scope

### In scope

- `Expo.ModulesCore.Generator`: shared-object identity checks in
  `ExpoModulesGenerator.SharedObjectValidation.cs`.
- `Expo.ModulesCore.Generator.Tests`: the diagnostic matrix for annotated
  shared-object types on every surface that calls shared-object boundary
  analysis.
- `docs/specs/modules-core-boundary.md` and `docs/roadmap.md`.

### Out of scope

- Support for nullable shared-object values (033).
- Codec selection for `SharedObject`, `SharedRef<T>`, or nullable concrete
  classes.
- New diagnostic descriptors or new message text.
- Typed event payload analysis, which has its own checks (`EXPOJSI019`,
  `EXPOJSI027`).
- Other generator refactors, including 029.

## Accepted design

- Shared-object identity compares Roslyn symbols, not display strings.
  `IsSharedObjectRelatedType`, `DerivesFromSharedObject`, and the polymorphic
  base and `SharedRef<T>` checks in `GetDirectSharedObjectBoundaryIssue`
  compare the annotation-erased symbol, for example through
  `WithNullableAnnotation(NullableAnnotation.NotAnnotated)` and the existing
  metadata-name constants, or through `SymbolEqualityComparer.Default` against
  the resolved metadata symbol. No code trims `?` from display text.
- `GetDirectSharedObjectBoundaryIssue` keeps its check order. It still reads
  the nullable annotation of the original symbol for the last check, so a
  nullable concrete class keeps the reason "which must be used without a
  nullable annotation".
- The order of boundary validation and the diagnostic descriptors do not
  change.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED: nullable reference codecs, shared-object paragraph

Replace the paragraph that starts "`SharedObject?`, the polymorphic base, SHALL
report the diagnostic for the position where it appears" with:

> A nullable annotation SHALL NOT change shared-object type identity.
> `SharedObject?`, `SharedRef<T>?`, and a nullable concrete
> `[ExpoSharedObject]` class SHALL report `EXPOJSI023` on every generated
> boundary that runs shared-object boundary analysis, with the same reason as
> the non-null form, except that a valid concrete class annotated as nullable
> reports that it must be used without a nullable annotation. The member is
> rejected at error severity, no binding is emitted, and no shared-object
> codec is selected.

### MODIFIED: Scenario "Nullable polymorphic SharedObject reports the position code"

Replace it with:

#### Scenario: Nullable polymorphic SharedObject reports the shared-object code
- **GIVEN** a generated boundary declares `SharedObject?` as a method
  parameter, a return type, an async result type, a property type, or a
  shared-object `[JS]` constructor parameter, on a module or on a
  shared-object class
- **WHEN** the generator analyzes the boundary
- **THEN** it SHALL report `EXPOJSI023` naming the member and the reason
  "which is the polymorphic SharedObject base"
- **AND** it SHALL NOT report `EXPOJSI001`, `EXPOJSI002`, or `EXPOJSI015` for
  that member
- **AND** it SHALL NOT select a shared-object codec or emit the member

### ADDED: Scenario under the same requirement

#### Scenario: Nullable shared object inside a composed codec
- **GIVEN** a generated method parameter declares a composed type whose
  element is `SharedObject?`, such as `IReadOnlyList<SharedObject?>`
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL report `EXPOJSI023` with the composed-codec reason, as it
  does for `IReadOnlyList<SharedObject>`
- **AND** it SHALL NOT emit the member

#### Scenario: Valid concrete shared object stays accepted
- **GIVEN** a generated method parameter declares a non-null top-level, sealed,
  non-generic `[ExpoSharedObject]` class
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL select `SharedObjectCodec<T>` and emit the binding with no
  diagnostic

## Verification

- Generator tests cover the matrix above: `SharedObject?` and `SharedRef<T>?`
  on each surface, nullable concrete classes, the composed case, a valid
  non-null concrete class, and an unrelated nullable reference type that keeps
  its 026 behavior. Each test asserts the diagnostic ID, the member name, the
  reason text, and that no binding is emitted for a rejected member.
- `scripts/test-managed.sh` passes with no skips.
- `scripts/format.sh --check --all` passes.
- `rg -n 'ToDisplayString\(\) == (SharedObjectMetadataName|SharedRefMetadataName)'`
  over `ExpoModulesGenerator.SharedObjectValidation.cs` finds nothing.
