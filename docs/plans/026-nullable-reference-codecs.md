# Plan 026: Nullable reference-type codecs in `Expo.ModulesCore`

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving on. Touch
> only the files in the In-scope list. If a STOP condition occurs, stop and
> report — do not improvise. Follow the repo's living-spec workflow: delta spec
> first, then plan, then implementation. Update the status row in
> `docs/plans/README.md` when done unless a reviewer says they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat fdf720cf..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator docs/specs/
> ```
> If the codec dispatch chain or `NullableCodec` changed, compare the live code
> against the excerpts in "Current state" before proceeding. A mismatch is a STOP
> condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (touches the generated-binding hot path used by every module)
- **Depends on**: none
- **Blocks**: `docs/plans/022-expo-asset-dotnet.md`, and any authored module
  whose upstream signature has a nullable reference parameter
- **Category**: core capability
- **Planned at**: `fdf720cf`, 2026-07-25

## Why this matters

A `[JS]` method cannot currently accept a nullable reference type. Upstream Expo
signatures routinely do — `expo-asset`'s is
`downloadAsync(url: string, md5Hash: string | null, type: string)` — so plan 022
is blocked outright, and 023–025 will hit the same wall.

The operator's standing rule settles the approach: "these modules need to be
state of the art. No workarounds because core is missing a feature. We'll fix
core instead." So this is fixed in the generator and the codecs, not worked
around per module with a `JavaScriptValue` parameter, a sentinel string, or
hand-rolled argument decoding.

## Current state

### The gap, precisely

`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableCodec.cs`
(verbatim, full file):

```csharp
public readonly struct NullableCodec<T, TCodec> : IJavaScriptCodec<T?>
    where T : struct
    where TCodec : IJavaScriptCodec<T>
{
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.IsNullish ? null : TCodec.Decode(value, runtime);

  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime) =>
      value.HasValue ? TCodec.Encode(value.Value, runtime) : runtime.CreateNull();
}
```

`where T : struct` — value types only. And the generator only ever reaches it for
`Nullable<T>`, from
`Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs:98-107` (verbatim):

```csharp
  private static string? TryGetNullableCodec(
      ITypeSymbol typeSymbol,
      List<ExpoDiagnosticModel> diagnostics,
      List<ExpoGeneratedRecordCodecModel> recordCodecs)
  {
    if (typeSymbol is not INamedTypeSymbol namedType ||
        namedType.ConstructedFrom.SpecialType != SpecialType.System_Nullable_T)
    {
      return null;
    }
```

So a `string?` parameter falls through to `StringCodec`
(`Codecs/StringCodec.cs`, verbatim, full file):

```csharp
public readonly struct StringCodec : IJavaScriptCodec<string>
{
  public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value);
}
```

`AsString()` is strict — `Expo.JSI/JavaScriptValue.cs:105` is
`public string AsString() => Inner.AsString();`, and a separate internal
`CoerceToString()` exists precisely because `AsString` does not coerce. A JS
`null` therefore throws during argument decode. For an async `[JS]` method that
surfaces as a rejected Promise before authored code runs, so the required call
shape simply cannot work.

That `NullableCodec.Decode` guards with `value.IsNullish ? null : ...` is the
proof the inner codecs are not null-tolerant by design. Keep that property.

### Two traps in the dispatch chain

`GetCodecExpression` in the same file is an ordered chain. The tail, verbatim
from `ExpoModulesGenerator.Codecs.cs:325-333`:

```csharp
    if (typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName)
    {
      return "JavaScriptValueCodec";
    }

    if (TryGetNullableCodec(typeSymbol, diagnostics, recordCodecs) is { } nullableCodec)
    {
      return nullableCodec;
    }
```

**Trap 1 — placement.** `string?` and `string` are the *same* symbol; they differ
only by `NullableAnnotation`. The concrete matches (`string`, `Uri`, byte
buffers, `JavaScriptValue`) come *earlier* in the chain, so a nullable-reference
check appended near `TryGetNullableCodec` never runs — `string?` returns
`StringCodec` first. The new check must sit at the **top** of
`GetCodecExpression`, before any concrete type match.

**Trap 2 — recursion.** Resolving the inner codec must strip the annotation
(`typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated)`), or the
new check matches its own input forever.

### Conventions

- Codecs live in `Expo.ModulesCore/Codecs/`, one type per file, matching
  `NullableCodec.cs` above. Existing codecs: `ArrayBufferCodec`, `BoolCodec`,
  `ByteArrayCodec`, `DateTimeOffsetCodec`, `DoubleCodec`, `GuidCodec`,
  `JavaScriptArrayCodec`, `JavaScriptCallbackCodec`, `JavaScriptDictionaryCodec`,
  `JavaScriptValueCodec`, `MemoryByteCodec`, `NumberCodec`, `NumberEnumCodec`,
  `ReadOnlyMemoryByteCodec`, `SharedObjectCodec`, `StringCodec`,
  `StringEnumCodec`, `TimeSpanCodec`, `UriCodec`, `ValueTupleCodec`.
- Generator diagnostics are `EXPOJSI001`–`EXPOJSI028` in
  `Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`. A new one is
  **`EXPOJSI029`**.
- `.editorconfig` sets 2-space indent for `*.cs` repo-wide.
- Generator behavior is tested in `Expo.ModulesCore.Generator.Tests` (source
  output and diagnostics); runtime codec behavior in `Expo.ModulesCore.Tests`
  (see `Codecs/ArrayBufferCodecTests.cs` and `Codecs/CodecExpansionTests.cs` for
  the two established shapes).
- Test conventions: xUnit v3 `3.2.0`, file-scoped namespace,
  `public sealed class <Subject>Tests`, `[Fact]` named
  `MethodUnderTest_ExpectedBehavior`, `TestContext.Current.CancellationToken` for
  async waits.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests only | `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | exit 0 |
| Full managed regression | `scripts/test-managed.sh` | exit 0; 650 pre-existing tests still pass, plus the new ones |
| One project | `scripts/test-managed.sh --project <repo-relative path to a .Tests.csproj>` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | no output |
| No reflection on the hot path | `rg "Assembly.GetTypes\|MethodInfo.Invoke\|Delegate.DynamicInvoke\|JsonSerializer" packages/expo-modules-dotnet/managed/packages` | no new matches |

`scripts/format.py` discovers C# via `git ls-files`, so **`git add` new files
before running the format check** or it will not see them.

## Scope

**In scope**:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReferenceCodec.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/**` (new tests)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/**` (new tests + fixture members)
- `docs/changes/2026-<mm-dd>-nullable-reference-codecs/spec.md` and `plan.md`
- `docs/specs/modules-core-boundary.md` (merge step)
- `docs/module-authoring-guide.md` (the supported-types list in section 3)
- `docs/plans/README.md` (status row)

**Out of scope**:

- `packages/expo-asset-dotnet/**` — does not exist yet; plan 022 consumes this.
- `packages/example-module/**` — do not add nullable members to the example
  module in this plan. Fixture coverage belongs in the test projects.
- `Expo.JSI` and the native bridge — `IsNullish` already exists and is
  sufficient. Do not touch the C ABI.
- Making owned wrappers, callbacks, or shared objects nullable — see the
  exclusion decision below.
- Nullable *value* types. `NullableCodec` already handles those; leave it alone.

## Steps

### Step 1: Delta spec and change plan

Write `docs/changes/2026-<mm-dd>-nullable-reference-codecs/spec.md` per
`.claude/skills/living-spec-workflow/SKILL.md`, matching the structure of
`docs/changes/2026-07-24-authored-module-test-core/spec.md`. Commit `spec.md`,
then `plan.md`, separately.

The spec must fix these decisions:

**Which types may be nullable.** In scope: `string`, `Uri`, positional records
(`record` / `record class`), and the collection codecs
(`IReadOnlyList<T>`/`List<T>`, `Dictionary<string, T>` /
`IReadOnlyDictionary<string, T>`) — plus, for collections, nullable *elements*
and nullable *values* where the element/value codec itself is supported.

**Which types may NOT be nullable, and why.** `JavaScriptValue`, `ArrayBuffer`,
`JavaScriptCallback<...>`, and `SharedObject`/`SharedRef<T>` types are excluded
in this slice. Their ownership and lifetime rules are subtle — invocation-owned
argument wrappers, retain-before-store, transfer-on-return, release-exactly-once
— and adding a null axis multiplies those states for no current consumer.
Annotating one of them nullable SHALL produce a clear build diagnostic
(`EXPOJSI029`), not silently generate a codec and not fall back to the
non-nullable codec. This is the "explicitly decide rather than let the generic
rule include them accidentally" requirement; make it a scenario.

**Null vs undefined.** Decode SHALL treat both JS `null` and `undefined` as C#
`null`, matching `NullableCodec`'s existing `IsNullish` semantics. Encode SHALL
emit JS `null` for C# `null`.

**Strictness is preserved.** A non-nullable reference parameter SHALL keep
rejecting nullish input. This plan must not make `string` tolerant of `null`.

**Nullable-context caveat.** Annotations only exist when the consuming project
has `<Nullable>enable</Nullable>`. In a disabled context the annotation is
`NullableAnnotation.None`, and the type SHALL be treated as non-nullable. State
this so it is a documented limitation rather than a surprise.

**Coverage surface.** Parameters, return types, `async` (`Task<T?>`) returns,
`[JS]` properties (both accessors), record fields, and collection
element/value positions.

### Step 2: Add `NullableReferenceCodec<T, TCodec>`

Create `Codecs/NullableReferenceCodec.cs` mirroring `NullableCodec.cs` but
constrained to reference types:

```csharp
public readonly struct NullableReferenceCodec<T, TCodec> : IJavaScriptCodec<T?>
    where T : class
    where TCodec : IJavaScriptCodec<T>
```

`Decode` returns `value.IsNullish ? null : TCodec.Decode(value, runtime)` for
both the `JavaScriptValueRef` and `JavaScriptValue` overloads. `Encode` returns
`value is null ? runtime.CreateNull() : TCodec.Encode(value, runtime)`.

Note the asymmetry with `NullableCodec`: it tests `value.HasValue` on a
`Nullable<T>`; this tests `value is null` on a reference. Do not copy
`HasValue`.

**Verify**: `dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`
→ exit 0.

### Step 3: Teach the generator to detect annotated reference types

In `ExpoModulesGenerator.Codecs.cs`, add `TryGetNullableReferenceCodec` and call
it at the **top** of `GetCodecExpression`, before every concrete type match (see
Trap 1). It must:

1. Return `null` unless `typeSymbol.NullableAnnotation == NullableAnnotation.Annotated`
   and the type is a reference type (`typeSymbol.IsReferenceType`).
2. Return `null` for the excluded categories, after reporting `EXPOJSI029` —
   `JavaScriptValue`, `ArrayBuffer`, `JavaScriptCallback<...>`, and any type
   deriving from `SharedObject`. Reuse whatever helper already identifies shared
   object types for `SharedObjectCodec`; do not re-derive that test.
3. Resolve the inner codec from
   `typeSymbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated)` (see
   Trap 2), returning `null` if the inner codec is unsupported so the existing
   unsupported-type diagnostic still fires.
4. Return `$"NullableReferenceCodec<{innerTypeName}, {innerCodec}>"`, where
   `innerTypeName` is the **non-annotated** fully-qualified display string.

Add `EXPOJSI029` to `ExpoModulesDiagnostics.cs` following the existing entries'
shape, with a message naming the member and the offending type.

**Verify**: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
→ exit 0, existing 220 tests still pass.

### Step 4: Tests

Generator tests in `Expo.ModulesCore.Generator.Tests` — assert the emitted codec
expression, following the existing source-output test pattern:

1. `string?` parameter emits `NullableReferenceCodec<string, StringCodec>`.
2. `Uri?` emits `NullableReferenceCodec<..., UriCodec>`.
3. A nullable record parameter emits the nullable wrapper around its generated
   record codec.
4. `IReadOnlyList<string?>` and `Dictionary<string, string?>` emit the nullable
   wrapper in the element/value position.
5. `string?` **return** type, and `Task<string?>`.
6. A `[JS]` property typed `string?` — both accessors.
7. Non-nullable `string` still emits plain `StringCodec` (no regression).
8. `int?` still emits `NullableCodec<int, ...>` (no regression).
9. `JavaScriptValue?`, `ArrayBuffer?`, `JavaScriptCallback<...>?`, and a nullable
   shared-object type each report `EXPOJSI029` and do **not** emit a codec.
10. A nullable type whose inner codec is unsupported still reports the existing
    unsupported-type diagnostic, not `EXPOJSI029`.
11. In a `<Nullable>disable</Nullable>` compilation, `string?` is treated as
    non-nullable.

Runtime tests in `Expo.ModulesCore.Tests` — add nullable members to the existing
module fixture (`Modules/ModuleFixtures.cs`) and drive them through Hermes:

12. Passing JS `null` for a `string?` parameter yields C# `null`.
13. Passing JS `undefined` for the same parameter yields C# `null`.
14. Passing a string yields that string.
15. Returning C# `null` from a `string?` method yields JS `null`
    (`result === null`, not `undefined`).
16. Passing JS `null` to a **non-nullable** `string` parameter still rejects —
    this is the strictness regression guard.
17. A nullable record parameter round-trips `null` and a value.
18. An `async` method returning `Task<string?>` resolves `null`.

**Verify**: `scripts/test-managed.sh` → exit 0, with the pre-existing 650 tests
still passing.

### Step 5: Docs and merge

Update `docs/module-authoring-guide.md` section 3's supported-types list to state
that nullable reference types are supported, which categories are excluded and
why, and the `<Nullable>enable</Nullable>` caveat. Merge the accepted delta into
`docs/specs/modules-core-boundary.md`. Archive the change folder's `plan.md`.
Mark 026 DONE in `docs/plans/README.md`.

**Verify**: the full Done criteria below.

## Done criteria

- [ ] `scripts/test-managed.sh` exits 0; the 650 pre-existing tests still pass.
- [ ] `dotnet test .../Expo.ModulesCore.Generator.Tests/...` exits 0.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] `git diff --check` produces no output.
- [ ] `grep -n "where T : class" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReferenceCodec.cs` matches.
- [ ] `grep -c "EXPOJSI029" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs` is at least 1.
- [ ] `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|JsonSerializer" packages/expo-modules-dotnet/managed/packages` shows no new matches — no reflection entered the hot path.
- [ ] A test asserts JS `null` into a non-nullable `string` parameter still
      rejects (case 16). Without it, this plan could silently weaken every
      existing signature.
- [ ] No file outside the In-scope list is modified (`git status`).
- [ ] `docs/plans/README.md` row for 026 says DONE.

## STOP conditions

- The generator cannot observe `NullableAnnotation.Annotated` for the test
  inputs — that would mean annotations are being erased before the generator
  runs, and the whole approach needs rethinking rather than patching.
- Making the new check work requires reordering existing entries in
  `GetCodecExpression` in a way that changes which codec an *existing*
  non-nullable type resolves to. Adding at the top is safe; reshuffling the rest
  is not, and is a STOP.
- Any pre-existing test in the 650 starts failing and the cause is not obviously
  a test that asserted the old (broken) nullable behavior.
- The excluded categories cannot be identified without duplicating type-matching
  logic that already exists elsewhere in the generator.

## Maintenance notes

- **The strictness guard (case 16) is the most important test here.** The easy
  wrong implementation makes *all* reference codecs null-tolerant, which would
  silently turn every existing `string` parameter into `string?` and defer
  failures from decode time to deep inside authored code. A reviewer should check
  that test exists and genuinely asserts rejection.
- **The exclusion list is a deliberate scope boundary, not an oversight.** If a
  future module genuinely needs a nullable `ArrayBuffer` or shared object, that
  is its own delta with its own ownership analysis — do not relax `EXPOJSI029`
  casually.
- **Plan 022 consumes this immediately** for
  `downloadAsync(url, md5Hash: string | null, type)`. When this lands, amend plan
  022 to drop its dependency on 026 and confirm the module declares
  `string? md5Hash` rather than any workaround.
- **Interaction with 027.** Independent; no shared files. Either order works.
- **`NullableCodec` and `NullableReferenceCodec` are near-duplicates by
  necessity** — the C# generic constraints `struct` and `class` cannot be
  unified in one type. Do not try to merge them.
