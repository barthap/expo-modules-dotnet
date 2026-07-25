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
> git diff --stat 4c10f90b..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests docs/module-authoring-guide.md docs/specs/modules-core-boundary.md docs/plans/README.md
> ```
> If the codec dispatch chain, `NullableCodec`, `ByteArrayCodec`, or the
> collection codecs changed, compare the live code against the excerpts and
> constraints in "Current state" before proceeding. A mismatch is a STOP
> condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (touches the generated-binding hot path used by every module)
- **Depends on**: none
- **Blocks**: `docs/plans/022-expo-asset-dotnet.md`, and any authored module
  whose upstream signature has a nullable reference parameter
- **Category**: core capability
- **Planned at**: `4c10f90b`, 2026-07-25

## Why this matters

A `[JS]` method cannot currently accept a nullable reference type. Upstream Expo
signatures routinely do — `expo-asset`'s is
`downloadAsync(url: string, md5Hash: string | null, type: string)` — so plan 022
is blocked outright. Plan 023 is also expected to need nullable metadata fields;
plans 024 and 025 do not yet have a proven dependency on this capability.

The operator's standing rule settles the approach: "these modules need to be
state of the art. No workarounds because core is missing a feature. We'll fix
core instead." So this is fixed in the generator and the codecs, not worked
around per module with a `JavaScriptValue` parameter, a sentinel string, or
hand-rolled argument decoding.

## Current state

### The gap, precisely

`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableCodec.cs`
(relevant type, verbatim):

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
(`Codecs/StringCodec.cs`, relevant type, verbatim):

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

### Four traps in the dispatch chain

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

**Trap 3 — `null` is overloaded.** Existing `TryGet*Codec` helpers use `null` to
mean "this helper did not match." A nullable-reference helper must also express
"this was an annotated reference, but it is deliberately unsupported." If both
states return `null`, `GetCodecExpression` continues through the chain and can
silently select `JavaScriptValueCodec`, `ArrayBufferCodec`, or a callback codec.
Use a handled/not-handled return plus a nullable codec-expression `out`
parameter, and return immediately whenever the helper handled the type.

**Trap 4 — not every current codec implements `IJavaScriptCodec<T>`.**
`ByteArrayCodec`, `JavaScriptArrayCodec`, and `JavaScriptDictionaryCodec` are
static helper classes. The array and dictionary codecs also use
`DecodeToArray` / `DecodeToDictionary`, selected specially by
`GetDecodeExpression`. They cannot be passed to
`NullableReferenceCodec<T, TCodec>` as written, and wrapping their expression
would bypass the special decode method. `List<T>` is not supported at all;
`TryGetReadOnlyListCodec` recognizes only `IReadOnlyList<T>`.

### Conventions

- Codecs live in `Expo.ModulesCore/Codecs/`, one type per file, matching
  `NullableCodec.cs` above. Existing codecs: `ArrayBufferCodec`, `BoolCodec`,
  `ByteArrayCodec`, `DateTimeOffsetCodec`, `DoubleCodec`, `GuidCodec`,
  `JavaScriptArrayCodec`, `JavaScriptCallbackCodec`, `JavaScriptDictionaryCodec`,
  `JavaScriptValueCodec`, `MemoryByteCodec`, `NumberCodec`, `NumberEnumCodec`,
  `ReadOnlyMemoryByteCodec`, `SharedObjectCodec`, `StringCodec`,
  `StringEnumCodec`, `TimeSpanCodec`, `UriCodec`, `ValueTupleCodec`.
- Unsupported boundary types already use context-specific diagnostics:
  `EXPOJSI001`/`002` for method parameters and returns, `EXPOJSI007` for record
  fields, `EXPOJSI008` for callbacks, `EXPOJSI015` for properties,
  `EXPOJSI019`/`027` for event payloads, and `EXPOJSI023` for shared-object
  boundaries. Reuse them instead of adding a context-free diagnostic inside
  `GetCodecExpression`.
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
| ModulesCore runtime tests only | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | no output |
| No reflection or dynamic values on the hot path | `rg "Assembly.GetTypes\|MethodInfo.Invoke\|Delegate.DynamicInvoke\|object\\?\\[\\]\|JsonSerializer" packages/expo-modules-dotnet/managed/packages` | only the pre-existing test assertion that generated output excludes `object?[]` |

`scripts/format.py` discovers C# via `git ls-files`, so **`git add` new files
before running the format check** or it will not see them.

## Scope

**In scope**:

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReferenceCodec.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReadOnlyListCodec.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableDictionaryCodec.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReadOnlyDictionaryCodec.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ByteArrayCodec.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Codecs/NullableReferenceCodecTests.cs` (create)
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ModuleAttributeTests.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/BinaryModuleFixture.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/BinaryModuleTests.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Events/EventModuleTests.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`
- `docs/changes/<yyyy-mm-dd>-nullable-reference-codecs/spec.md` and `plan.md`
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

## Git workflow

- Do not use a worktree. If execution starts on `main` or `master`, create a
  normal `codex/nullable-reference-codecs` branch before writing artifacts.
- Commit the approved delta spec first, then the approved change plan. Continue
  with focused verified implementation commits.
- Before each commit, inspect the staged diff and confirm it contains no local
  absolute paths, usernames, machine names, private hostnames, or
  machine-specific install paths.
- Do not push or open a pull request unless the operator explicitly asks.

## Steps

### Step 1: Delta spec and change plan

Write `docs/changes/<yyyy-mm-dd>-nullable-reference-codecs/spec.md` per
`.agents/skills/living-spec-workflow/SKILL.md`, matching the structure of
`docs/changes/2026-07-24-authored-module-test-core/spec.md`. Obtain operator
approval for the delta before committing it. Then write the change-local
`plan.md`, obtain approval, and commit it separately.

The spec must fix these decisions:

**Which types may be nullable.** Support every current reference-type value
codec that does not carry JSI ownership or runtime-context state: `string`,
`Uri`, `byte[]`, positional `record` / `record class` values,
`IReadOnlyList<T>`, `Dictionary<string, T>`, and
`IReadOnlyDictionary<string, T>`. Collections must support nullable containers
as well as nullable elements/values wherever the nested codec is supported.
`List<T>` remains unsupported because the current generator does not support
its non-nullable form; adding it here would be an unrelated surface expansion.

**Which types may NOT be nullable, and why.** `JavaScriptValue`, `ArrayBuffer`,
`JavaScriptCallback<...>`, and `SharedObject`/`SharedRef<T>` types are excluded
in this slice. Their ownership and lifetime rules are subtle — invocation-owned
argument wrappers, retain-before-store, transfer-on-return, release-exactly-once
— and adding a null axis multiplies those states for no current consumer.
Annotating one of them nullable SHALL produce a clear build diagnostic
from the existing context-specific diagnostic family, not silently generate a
codec and not fall back to the non-nullable codec. Direct nullable shared-object
boundaries SHALL continue to report `EXPOJSI023`; existing tests already fix
that contract. This is the "explicitly decide rather than let the generic rule
include them accidentally" requirement; make it a scenario.

**Null vs undefined.** Decode SHALL treat both JS `null` and `undefined` as C#
`null`, matching `NullableCodec`'s existing `IsNullish` semantics. Encode SHALL
emit JS `null` for C# `null`.

**Strictness is preserved.** A non-nullable reference parameter SHALL keep
rejecting nullish input. This plan must not make `string` tolerant of `null`.

**Optional arguments.** A required nullable-reference parameter SHALL decode
explicit JavaScript `undefined` as C# `null`. For an optional nullable-reference
parameter, omission or explicit `undefined` SHALL use the authored C# default,
while explicit JavaScript `null` SHALL decode as C# `null`, matching existing
nullable-value behavior.

**Nullable-context caveat.** Only
`NullableAnnotation.Annotated` activates the nullable wrapper. An oblivious
reference from a disabled nullable context has `NullableAnnotation.None` and
SHALL keep its current strict codec. Generator test sources must use
`#nullable enable` for positive `T?` cases and `#nullable disable` with an
unannotated reference for the oblivious case.

**Coverage surface.** Parameters, return types, `async` (`Task<T?>`) returns,
optional arguments, `[JS]` properties (both accessors), record fields,
collection container/element/value positions, typed event payloads, and
shared-object `[JS]` members.

**Verify**:

- `git diff --check` → no output.
- `rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills`
  → no unintended matches.

### Step 2: Add nullable codecs for standard and collection shapes

Create `Codecs/NullableReferenceCodecTests.cs` first. Cover both
`JavaScriptValueRef` and owned `JavaScriptValue` decode overloads, null encode,
non-null delegation, `byte[]`, and each nullable collection container wrapper.

**Verify red**:
`scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
→ fails to compile only because the new codec types or `ByteArrayCodec`
interface are not implemented yet.

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

`ByteArrayCodec` already has the three static methods required by
`IJavaScriptCodec<byte[]>`. Change it from a static class to a
`readonly struct` implementing that interface, without changing method
behavior. This makes `byte[]?` safe to compose through
`NullableReferenceCodec<byte[], ByteArrayCodec>`.

Do not force the collection helpers through the generic wrapper. Add three
small codecs that implement the exact nullable container interfaces and
delegate non-null values to the existing helpers:

```csharp
NullableReadOnlyListCodec<T, TCodec>
    : IJavaScriptCodec<IReadOnlyList<T>?>

NullableDictionaryCodec<T, TCodec>
    : IJavaScriptCodec<Dictionary<string, T>?>

NullableReadOnlyDictionaryCodec<T, TCodec>
    : IJavaScriptCodec<IReadOnlyDictionary<string, T>?>
```

Each codec's two `Decode` overloads return `null` for `IsNullish`; otherwise
they call `JavaScriptArrayCodec<T, TCodec>.DecodeToArray` or
`JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary`. `Encode` returns JS
`null` for a null container and otherwise delegates to the existing collection
codec. `JavaScriptArrayCodec` has no owned-value decode overload, so the
read-only-list codec's `Decode(JavaScriptValue, ...)` must pass `value.Ref`;
the dictionary helper already has both overloads. Keep
`where TCodec : IJavaScriptCodec<T>`.

**Verify green**:
`scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
→ exit 0; all pre-existing project tests and the new direct codec tests pass.

### Step 3: Teach the generator to detect annotated reference types

Before editing the generator, add these cases to
`Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`. Put
`#nullable enable` in every positive nullable-reference source.

1. `string?`, `Uri?`, and `byte[]?` parameters emit the generic nullable
   wrapper around `StringCodec`, `UriCodec`, and `ByteArrayCodec`.
2. A nullable record-class parameter emits the generic nullable wrapper around
   its generated record codec.
3. Nullable `IReadOnlyList<T>`, `Dictionary<string, T>`, and
   `IReadOnlyDictionary<string, T>` containers emit their three
   collection-specific nullable codecs.
4. `IReadOnlyList<string?>`, `Dictionary<string, string?>`, and the read-only
   dictionary equivalent emit `NullableReferenceCodec<string, StringCodec>` in
   the element/value position. Include one nested nullable container to prove
   recursive composition.
5. A record field typed `string?` uses the nullable wrapper.
6. A `string?` return, `Task<string?>` result, optional `string?` parameter,
   read-write `[JS]` property, typed event payload, and shared-object `[JS]`
   member all carry the nullable codec into generated source.
7. Non-nullable `string` still emits plain `StringCodec`, and `int?` still emits
   `NullableCodec<int, ...>`.
8. `JavaScriptValue?` and `ArrayBuffer?` parameters report `EXPOJSI001`;
   `JavaScriptCallback<...>?` reports `EXPOJSI008`; nullable shared-object
   boundaries keep reporting `EXPOJSI023`. None emits a binding for the invalid
   member. Also cover an excluded return or property so the tri-state helper is
   not accidentally correct only for parameters.
9. An annotated reference whose non-annotated inner type has no codec reports
   the existing context-specific unsupported-type diagnostic and emits no
   binding.
10. A `#nullable disable` source with an unannotated `string` parameter emits
    plain `StringCodec`; `NullableAnnotation.None` must not activate the wrapper.

**Verify red**:
`dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
→ the positive cases fail because nullable reference annotations still resolve
to the non-nullable codec; excluded-type cases expose the current fallback.

In `ExpoModulesGenerator.Codecs.cs`, add `TryGetNullableReferenceCodec` and call
it at the **top** of `GetCodecExpression`, before every concrete type match (see
Traps 1 and 2). Its contract must distinguish "not handled" from "handled but
unsupported":

```csharp
private static bool TryGetNullableReferenceCodec(
    ITypeSymbol typeSymbol,
    List<ExpoDiagnosticModel> diagnostics,
    List<ExpoGeneratedRecordCodecModel> recordCodecs,
    out string? codecExpression)
```

At the top of `GetCodecExpression`, use:

```csharp
if (TryGetNullableReferenceCodec(
        typeSymbol, diagnostics, recordCodecs, out var nullableReferenceCodec))
{
  return nullableReferenceCodec;
}
```

The helper must:

1. Set the output to `null` and return `false` unless
   `typeSymbol.NullableAnnotation == NullableAnnotation.Annotated` and
   `typeSymbol.IsReferenceType`.
2. Strip only the top-level annotation with
   `WithNullableAnnotation(NullableAnnotation.NotAnnotated)`. Preserve nested
   annotations on type arguments and record fields, and use this non-annotated
   symbol for all category checks and emitted type names.
3. Return `true` with a null output for `JavaScriptValue`, `ArrayBuffer`, and
   `JavaScriptCallback<...>`. This stops dispatch so the caller emits its
   existing context-specific unsupported-type diagnostic. Direct and nested
   shared-object types remain governed by
   `TryAnalyzeSharedObjectBoundaryType`; do not replace its existing
   `EXPOJSI023` contract.
4. Return the matching collection-specific nullable codec for
   `IReadOnlyList<T>`, `Dictionary<string, T>`, or
   `IReadOnlyDictionary<string, T>`, resolving the element/value codec
   recursively. The exact expressions are
   `NullableReadOnlyListCodec<elementTypeName, elementCodec>`,
   `NullableDictionaryCodec<valueTypeName, valueCodec>`, and
   `NullableReadOnlyDictionaryCodec<valueTypeName, valueCodec>`. Preserve the
   existing dictionary requirement that the key type is exactly `string`; an
   invalid key type is handled with a null output.
5. For other annotated references, resolve the non-annotated inner codec
   recursively. Return `true` with a null output if no inner codec exists, so
   the caller emits its existing unsupported-type diagnostic. Otherwise return
   `NullableReferenceCodec<innerTypeName, innerCodec>`.

Do not add a new diagnostic descriptor. `GetCodecExpression` does not know the
member context, and the current callers already provide the correct member,
property, record, event, callback, or shared-object diagnostic.

**Verify**: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
→ exit 0, existing 220 tests still pass.

### Step 4: Tests

Runtime tests in `Expo.ModulesCore.Tests` must drive generated bindings through
Hermes, following `Modules/ModuleAttributeTests.cs` and the existing event and
shared-object test shapes:

11. A required `string?` parameter receives C# `null` for JS `null` and
    explicit `undefined`, and receives the original non-null string.
12. A C# `null` `string?` return becomes JS `null`, never `undefined`.
13. JS `null` passed to a non-nullable `string` still rejects before authored
    code runs.
14. An optional `string?` parameter uses its authored default for omission and
    explicit `undefined`, while explicit JS `null` reaches authored code as C#
    `null`.
15. A read-write `string?` property accepts and returns null and non-null values.
16. A nullable record-class parameter round-trips null and a value; a separate
    record with a `string?` field round-trips both null and a string field.
17. Nullable list and both dictionary containers round-trip JS null, and
    nullable list elements/dictionary values preserve null alongside non-null
    values.
18. `byte[]?` round-trips JS null and an ArrayBuffer value.
19. `Task<string?>` resolves JS null.
20. A nullable typed-event payload encodes C# null as JS null, and one
    shared-object `[JS]` member proves the same codec path accepts or returns
    null.

**Verify**: `scripts/test-managed.sh` → exit 0, with the pre-existing 650 tests
still passing.

### Step 5: Docs and merge

Update `docs/module-authoring-guide.md` section 3's supported-types list to state
which reference categories support nullable containers or values, that
`List<T>` remains unsupported, which ownership-bearing categories are excluded
and why, the optional-argument semantics, and the nullable-context caveat.
Merge the accepted delta into `docs/specs/modules-core-boundary.md`. Archive or
remove the transient change package according to
`.agents/skills/living-spec-workflow/SKILL.md`. Mark 026 DONE in
`docs/plans/README.md`.

**Verify**: the full Done criteria below.

## Done criteria

- [ ] `scripts/test-managed.sh` exits 0; the 650 pre-existing tests still pass.
- [ ] `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` exits 0.
- [ ] `scripts/format.sh --check --all` exits 0.
- [ ] `git diff --check` produces no output.
- [ ] `rg -n "where T : class" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReferenceCodec.cs` matches.
- [ ] `rg -n "IJavaScriptCodec<(IReadOnlyList|Dictionary|IReadOnlyDictionary)" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/Nullable*Codec.cs` finds all three nullable collection interfaces.
- [ ] `rg -n "public readonly struct ByteArrayCodec : IJavaScriptCodec<byte\\[\\]>" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ByteArrayCodec.cs` matches.
- [ ] `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages` remains limited to the pre-existing test assertion that generated output does not contain `object?[]`.
- [ ] A test asserts JS `null` into a non-nullable `string` parameter still
      rejects (case 13). Without it, this plan could silently weaken every
      existing signature.
- [ ] Tests prove nullable collection containers and nullable nested
      elements/values; element-only coverage is incomplete.
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
- A nullable collection requires changing the public non-nullable collection
  signature or adding `List<T>` support. The dedicated nullable collection
  codecs avoid both; stop if that design does not compile instead of widening
  the public surface.
- Any pre-existing test in the 650 starts failing and the cause is not obviously
  a test that asserted the old (broken) nullable behavior.
- The tri-state helper cannot stop excluded annotated references before the
  existing concrete codec branches, or doing so requires a fake codec string.
- Shared-object nullable diagnostics would need to change from the existing
  `EXPOJSI023` contract.

## Maintenance notes

- **The strictness guard (case 13) is the most important test here.** The easy
  wrong implementation makes *all* reference codecs null-tolerant, which would
  silently turn every existing `string` parameter into `string?` and defer
  failures from decode time to deep inside authored code. A reviewer should check
  that test exists and genuinely asserts rejection.
- **The exclusion list is a deliberate scope boundary, not an oversight.** If a
  future module genuinely needs a nullable `ArrayBuffer` or shared object, that
  is its own delta with its own ownership analysis. Do not relax the exclusion
  by making an ownership-bearing codec null-tolerant.
- **Plan 022 consumes this immediately** for
  `downloadAsync(url, md5Hash: string | null, type)`. When this lands, amend plan
  022 to drop its dependency on 026 and confirm the module declares
  `string? md5Hash` rather than any workaround.
- **Interaction with 027.** Independent; no shared files. Either order works.
- **`NullableCodec` and `NullableReferenceCodec` are near-duplicates by
  necessity** — the C# generic constraints `struct` and `class` cannot be
  unified in one type. Do not try to merge them.
- **The collection-specific wrappers are also deliberate.** The current array
  and dictionary helpers have special decode shapes and do not implement
  `IJavaScriptCodec<T>`. Do not hide that mismatch with a fake generic
  constraint or reflection.
- **Future reference codecs need an explicit nullable decision.** A normal
  `IJavaScriptCodec<T>` can compose through `NullableReferenceCodec`; a
  context-owning or special-decode codec must either stay excluded or add a
  typed nullable adapter with matching lifetime tests.
