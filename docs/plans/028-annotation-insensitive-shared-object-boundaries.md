# Plan 028: Make shared-object boundary detection annotation-insensitive

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
> git diff --stat 512ab46e..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs docs/specs/modules-core-boundary.md docs/plans/README.md
> ```
> If shared-object boundary classification or the plan-026 nullable diagnostic
> tests changed, compare the live code with "Current state." A semantic mismatch
> is a STOP condition.

## Status

- **Priority**: P2
- **Effort**: S
- **Risk**: MED (one identity check controls diagnostic selection on every
  shared-object boundary surface)
- **Depends on**: plan 026, complete at `512ab46e`
- **Blocks**: plan 029 and the nullable concrete shared-object work in plan 033
- **Category**: bug
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

`SharedObject?` is the same unsupported polymorphic boundary type as
`SharedObject`, but nullable annotation text prevents it from entering
shared-object validation. It reports generic `EXPOJSI001` instead of the
specific `EXPOJSI023`. Plan 026 documented the shipped mismatch and filed this
follow-up instead of changing diagnostics late in that codec slice.

The fix must normalize symbol identity, then prove that every annotated
shared-object case keeps the right diagnostic. The code change is small; the
regression matrix is the work.

## Current state

`ExpoModulesGenerator.SharedObjectValidation.cs:98-118` contains:

```csharp
private static bool DerivesFromSharedObject(INamedTypeSymbol typeSymbol)
{
  for (var baseType = typeSymbol.BaseType; baseType is not null; baseType = baseType.BaseType)
  {
    if (baseType.ToDisplayString() == SharedObjectMetadataName) return true;
  }
  return false;
}

private static bool IsSharedObjectRelatedType(ITypeSymbol typeSymbol) =>
    typeSymbol.ToDisplayString() == SharedObjectMetadataName ||
    (typeSymbol is INamedTypeSymbol namedType && DerivesFromSharedObject(namedType));
```

The default `ToDisplayString()` includes `?`, so the direct base
`SharedObject?` comparison fails. `GetDirectSharedObjectBoundaryIssue` then
never runs, and ordinary codec analysis reports `EXPOJSI001`.

The same file already uses Roslyn symbols. Fix identity at that level:
`typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated)` or
`SymbolEqualityComparer.Default` against the resolved metadata symbol is valid.
Do not trim a `?` from a display string.

Plan-026 tests around
`ExpoModulesGeneratorTests.cs:4363-4410` already cover nullable
`SharedRef<T>`/concrete types and explicitly record the `SharedObject?`
`EXPOJSI001` deviation. Replace that deviation with the approved behavior and
add the full boundary matrix.

The merged plan-026 spec at `docs/specs/modules-core-boundary.md:1459-1462` and
`:1514-1516` currently records `EXPOJSI001` for `SharedObject?`. This plan must
change that requirement to `EXPOJSI023` after implementation.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Managed regression | `scripts/test-managed.sh` | all discovered managed tests pass |
| Format | `scripts/format.sh --check --all` | exit 0 |
| String-identity scan | `rg -n 'ToDisplayString\\(\\) == (SharedObjectMetadataName|SharedRefMetadataName)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs` | no annotation-sensitive direct-type comparison remains |

## Scope

**In scope**:

- `docs/changes/<yyyy-mm-dd>-annotation-insensitive-shared-object-boundaries/`
- `docs/archive/changes/<yyyy-mm-dd>-annotation-insensitive-shared-object-boundaries/`
- `docs/specs/modules-core-boundary.md`
- `docs/plans/README.md`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Out of scope**:

- Supporting nullable shared-object values
- Selecting a codec for `SharedObject`, `SharedRef<T>`, or nullable concrete
  shared-object classes
- New diagnostics or diagnostic message text
- Other generator refactors, including plan 029
- Runtime codecs, native code, public API, apps, or authored modules

## Git workflow

- Branch: `advisor/028-annotation-insensitive-shared-object-boundaries`
- Commit the approved delta spec and change-local plan before source changes.
- Keep implementation/tests and merged docs as separate logical commits.
- Suggested implementation commit:
  `fix(generator): normalize nullable shared-object boundaries`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Approve the diagnostic delta

Create the living-spec change package. Specify that nullable annotations do not
change shared-object type identity and that unsupported `SharedObject?`,
`SharedRef<T>?`, and invalid nullable concrete shared-object boundaries report
the same `EXPOJSI023` family as their non-null counterparts. This plan changes
diagnostic routing only; it does not make any boundary valid.

Get approval and commit the delta spec, then create, approve, and commit the
change-local plan.

**Verify**: `git log -2 --oneline` shows separate spec and plan commits.

### Step 2: Add the diagnostic regression matrix

Before changing production code, add or update generator tests for:

- `SharedObject?` method parameter and return;
- `SharedObject?` property;
- `SharedObject?` shared-object constructor parameter and member;
- `SharedRef<T>?` on the same boundary families;
- nullable invalid concrete classes;
- a valid non-null concrete sealed `[ExpoSharedObject]` class, which must remain
  accepted;
- unrelated nullable reference types, which must keep their plan-026 behavior.

Assert the diagnostic ID, member/position in the message, and absence of emitted
binding for invalid members. Do not assert only that "some error" exists.

**Verify**: generator tests fail only on the new `SharedObject?` expectations
before the production fix.

### Step 3: Normalize type identity

Change `IsSharedObjectRelatedType` and any direct base identity check exposed by
the matrix to compare annotation-erased symbols or metadata identity. Preserve
the nullable annotation for `GetDirectSharedObjectBoundaryIssue`, because that
method still needs to explain why a concrete nullable class is unsupported.

Do not change the order of boundary validation or diagnostic descriptors.

**Verify**: generator tests pass, and the string-identity scan has no match.

### Step 4: Run regressions and merge the delta

Run the full managed suite and format check. Update the living spec so
`SharedObject?` now requires `EXPOJSI023`, archive the change package, and mark
plan 028 DONE with the commit and test count.

**Verify**:

```sh
git diff --check
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: no whitespace errors; after commits, the tree is clean; the privacy
scan prints nothing and exits 1.

## Test plan

- The test matrix must cover all generator surfaces that call shared-object
  boundary analysis, not only ordinary method parameters.
- Keep a valid concrete-class acceptance case beside invalid cases so a broad
  "reject every annotated symbol" fix cannot pass.
- Run generator tests before and after the one-line production change, then the
  full managed suite.

## Done criteria

- [ ] Approved delta spec and change-local plan were committed first.
- [ ] Shared-object identity ignores nullable annotation.
- [ ] `SharedObject?` reports `EXPOJSI023` on every applicable boundary.
- [ ] Existing `SharedRef<T>?` and concrete-class diagnostics remain specific.
- [ ] No previously valid shared-object boundary becomes invalid.
- [ ] No nullable shared-object boundary becomes supported.
- [ ] Generator tests, full managed tests, and format pass.
- [ ] Living spec is merged, change package archived, and plan 028 marked DONE.

## STOP conditions

Stop and report if:

- Fixing identity changes whether a boundary is emitted, instead of only which
  unsupported diagnostic is selected.
- A tested surface has no shared-object-specific diagnostic route.
- A valid concrete non-null shared object starts reporting an error.
- The fix requires string surgery on display text.
- A new diagnostic descriptor or runtime codec is required.
- An out-of-scope file is required.

## Maintenance notes

Plan 029 removes the remaining policy inference from codec-expression strings,
but it must start after this diagnostic baseline is stable. Plan 033 later
changes nullable concrete classes from unsupported to supported. It must keep
`SharedObject?` and direct `SharedRef<T>?` bases unsupported because their
non-null forms are unsupported too.
