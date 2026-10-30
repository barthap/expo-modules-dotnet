# Annotation-Insensitive Shared-Object Boundaries Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `SharedObject?` reports `EXPOJSI023` with the polymorphic-base reason on every generated boundary, because shared-object identity ignores the nullable annotation.

**Architecture:** Replace the display-string identity checks in `ExpoModulesGenerator.SharedObjectValidation.cs` with annotation-erased symbol checks. The test matrix in `ExpoModulesGeneratorTests.cs` pins the diagnostic for every surface that calls `TryAnalyzeSharedObjectBoundaryType`.

**Tech Stack:** C# Roslyn incremental generator (netstandard2.0), xUnit generator tests.

**Spec:** `docs/changes/2026-10-30-annotation-insensitive-shared-object-boundaries/spec.md`

## Global Constraints

- Diagnostic routing changes only. No boundary becomes valid or invalid.
- No new diagnostic descriptors, no new message text, no change to the order
  of boundary validation.
- No string surgery on display text. Compare symbols.
- Touch only the generator file, the generator test file, `docs/specs/modules-core-boundary.md`, `docs/roadmap.md`, and the change and brief locations under `docs/`.
- STOP and report (do not improvise) if: the fix changes whether a boundary is
  emitted; a tested surface has no shared-object-specific route; a valid
  non-null concrete shared object starts to report an error; a new descriptor
  or runtime codec is needed; an out-of-scope file is needed.
- Committed content has no local absolute paths, usernames, or machine names.

## Review Focus

1. `GetDirectSharedObjectBoundaryIssue` for `SharedObject?`: after the identity
   fix, the reason must be "which is the polymorphic SharedObject base", not
   "which is not marked [ExpoSharedObject]".
2. A nullable concrete valid class keeps the reason "which must be used without
   a nullable annotation". The fix must read the original annotation there.
3. `SharedObject?` on a member declared on a shared-object class: today it is
   `EXPOJSI001` remapped to `EXPOJSI023` with an "unsupported type" reason;
   after the fix it is `EXPOJSI023` with the polymorphic-base reason, once.
4. `IReadOnlyList<SharedObject?>`: today `EXPOJSI001`; after the fix
   `EXPOJSI023` with the composed-codec reason.
5. Unrelated nullable references (`string?`, `List<string>?`) keep their 026
   behavior.

---

### Task 1: Diagnostic matrix and annotation-erased identity

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs` (`DerivesFromSharedObject`, `IsSharedObjectRelatedType`, `GetDirectSharedObjectBoundaryIssue`)
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**
- Consumes: `GeneratorTestHost.Run`, `GeneratedText`, `AssertNoCompilerErrors` (existing test helpers).
- Produces: no new API. Private helper signatures stay as they are.

- [ ] **Step 1: Write the failing tests**

Replace `GeneratorRejectsANullablePolymorphicSharedObjectParameter` and its
comment (it records the old `EXPOJSI001` deviation) with the matrix below. Put
it next to `GeneratorKeepsSharedObjectDiagnosticsForNullableSharedObjectBoundaries`.
Use `#nullable enable` sources with the `Entry` shared object from that test.

`GeneratorReportsSharedObjectDiagnosticForNullablePolymorphicBase` — a
`[Theory]` over the module surfaces. For each case, assert exactly one
`EXPOJSI023` whose message contains the member name and
"which is the polymorphic SharedObject base", no `EXPOJSI001`/`EXPOJSI002`/
`EXPOJSI015` for that member, the generated text does not contain the member's
camelCase JS name or `SharedObjectCodec<global::Expo.ModulesCore.SharedObject`,
and `AssertNoCompilerErrors(result)`:

| Case | Declaration on `[ExpoModule]` |
| --- | --- |
| parameter | `[JS] public string TakeIt(SharedObject? value) => "x";` |
| return | `[JS] public SharedObject? GiveIt() => null;` |
| async result | `[JS] public System.Threading.Tasks.Task<SharedObject?> GiveItAsync() => System.Threading.Tasks.Task.FromResult<SharedObject?>(null);` |
| property | `[JS] public SharedObject? Current => null;` |

`GeneratorReportsSharedObjectDiagnosticForNullablePolymorphicBaseOnSharedObjectClass` —
a `[Theory]` over the same three shapes (parameter, return, property) declared
on a second `[ExpoSharedObject]` class registered in the module's `Classes`,
plus a `[JS]` constructor parameter `SharedObject? value`. Same assertions,
and exactly one `EXPOJSI023` per member.

`GeneratorReportsSharedObjectDiagnosticForNullableSharedRefOnEveryBoundary` —
the module-surface theory with `SharedRef<string>?` in place of
`SharedObject?`, reason "which is the SharedRef<T> managed carrier base". This
already passes before the fix; it pins the sibling behavior.

`GeneratorReportsComposedSharedObjectDiagnosticForNullableElement` —
`[JS] public string TakeAll(System.Collections.Generic.IReadOnlyList<SharedObject?> values) => "x";`
reports one `EXPOJSI023` containing `TakeAll` and
"inside a composed codec", and no `EXPOJSI001`.

`GeneratorAcceptsNonNullConcreteSharedObjectParameter` — `Entry value`
parameter: no diagnostics, generated text contains
`SharedObjectCodec<global::Expo.TestModules.Entry>` and `"takeIt"`. Skip it if
an existing test already asserts exactly this; name that test in the commit
body instead.

`GeneratorKeepsNullableReferenceBehaviorForUnrelatedTypes` — `string? value`
parameter emits a binding with no diagnostic. Skip it if a 026 test already
covers this; name that test instead.

- [ ] **Step 2: Run the generator tests and check they fail for the right reason**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
Expected: FAIL only in the `SharedObject?` cases (polymorphic base theories and
the composed case), with `EXPOJSI001`/`EXPOJSI002`/`EXPOJSI015` found where
`EXPOJSI023` is expected. The `SharedRef<T>?`, concrete, and unrelated cases
pass.

- [ ] **Step 3: Implement annotation-erased identity**

In `ExpoModulesGenerator.SharedObjectValidation.cs`, compare the
annotation-erased symbol (`WithNullableAnnotation(NullableAnnotation.NotAnnotated)`)
in `DerivesFromSharedObject`, `IsSharedObjectRelatedType`, and the first two
checks of `GetDirectSharedObjectBoundaryIssue`. Keep the last check of
`GetDirectSharedObjectBoundaryIssue` on the original symbol's
`NullableAnnotation`.

- [ ] **Step 4: Run the tests and the identity scan**

Run: the generator test command above.
Expected: all generator tests pass.
Run: `rg -n 'ToDisplayString\(\) == (SharedObjectMetadataName|SharedRefMetadataName)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`
Expected: no output, exit 1.

- [ ] **Step 5: Full suite and format**

Run: `scripts/test-managed.sh`
Expected: every assembly passes, 0 skipped.
Run: `scripts/format.sh --check --all`
Expected: exit 0 (run `scripts/format.sh` and check again if it fails).

- [ ] **Step 6: Commit**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git commit -m "fix(generator): normalize nullable shared-object boundaries"
```

### Task 2: Merge the delta and close 028

**Files:**
- Modify: `docs/specs/modules-core-boundary.md` (the nullable shared-object paragraph and scenarios near "`SharedObject?`, the polymorphic base")
- Modify: `docs/roadmap.md` (row 028, notes)
- Move: `docs/changes/2026-10-30-annotation-insensitive-shared-object-boundaries/` → `docs/archive/changes/`
- Move: `docs/plans/028-annotation-insensitive-shared-object-boundaries.md` → `docs/archive/plans/`

**Interfaces:**
- Consumes: Task 1 commit hash and the managed suite totals from Task 1 Step 5.

- [ ] **Step 1: Apply the spec delta**

Apply the MODIFIED and ADDED blocks of the change spec to
`docs/specs/modules-core-boundary.md`. Remove the sentence that explains the
display-string cause of the old behavior.

- [ ] **Step 2: Archive and close**

`git mv` the change folder and the brief. Set row 028 to `DONE` and point its
Brief link at `archive/plans/028-annotation-insensitive-shared-object-boundaries.md`.
Update the 028 mention in the "028–033" note to say 028 is done, with the
Task 1 commit and the managed test total.

- [ ] **Step 3: Verify**

Run: `git diff --check && scripts/format.sh --check --all`
Expected: exit 0.
Run: `grep -rn 'docs/changes/2026-10-30\|plans/028-' docs | grep -v '^docs/archive/'`
Expected: no link to the old locations.

- [ ] **Step 4: Commit**

```bash
git add -A docs
git commit -m "docs(generator): close annotation-insensitive shared-object boundaries"
```
