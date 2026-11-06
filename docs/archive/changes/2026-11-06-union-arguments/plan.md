# Union Arguments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Either<TFirst, TSecond>` works as a parameter type of generated module methods and of shared-object constructors and methods when the two members have different JavaScript value kinds among `String`, `Bool`, `Number`, and `ArrayBuffer`. The codec reads the kind once and decodes through that member's codec. Unions stay decode-only everywhere else (roadmap item 035).

**Architecture:** The runtime gains one carrier struct, one static-abstract kind interface on seven leaf codecs, an ordinary union codec, a scoped union codec, and an adapter that lifts an ordinary leaf codec into the scoped protocol. The generator resolves `Either<,>` by `ConstructedFrom`, maps each member type to a kind through a type-keyed table, and selects the ordinary or scoped codec with `DecodeOnly` capability. Three small guards keep that capability from leaking through composites, callback and event type arguments, and properties (see Deviations). Emission does not change. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-06-union-arguments/spec.md` (binding authority; committed at `5441fd7`). Base commit is `fbb3459` (the 034 close plus the bridge fix); `git diff --stat fbb3459..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/035-union-arguments.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`. Line numbers are at plan time.

## Deviations from the spec (controller decision)

The spec states four facts that the code does not have. The plan resolves them as below. The controller can strike D1-D3; each is a separate step in Task 2.

- **D1. Properties have no capability check.** The spec says `DecodeOnly` keeps properties rejected "through the existing checks". Only the async result (`ModuleAnalysis.cs:511`) and the return (`:568`) test `DecodeOnly`. `GetProperties` (`:750-841`) has no such test, so `[JS] public Either<string, double> Value { get; set; }` would be accepted. Fix: one check in `ExpoModulesGenerator.ModuleAnalysis.cs`, a file the spec's scope list does not name.
- **D2. Callback and event type arguments test shape only.** `AsJavaScriptCodecArgument` (`Codecs.cs:86-87`) accepts any `JavaScriptCodec` shape. The ordinary `EitherCodec` has that shape, so `Func<Either<string, double>, Task>` events and `JavaScriptCallback<Either<string, double>>` would be accepted. Fix: also require `DecodeAndEncode`.
- **D3. Composites always report `DecodeAndEncode`.** Every composite branch in `Codecs.cs` builds its descriptor with `OrdinaryCodec`/`ScopedCodec` (`:54-72`), whatever the child. So `Either<string, double>?` and `IReadOnlyList<Either<string, ArrayBuffer>>` would be accepted as returns, properties, and record fields, against the spec's "Unions SHALL be decode-only". Fix: a composite takes its child's `Capabilities`.
- **D4. No diagnostic can carry "a reason that names the rule".** `EXPOJSI001` is `Parameter '{0}' on '{1}' uses unsupported type '{2}'`. Its shared-object translation (`SharedObjectValidation.cs:84`) and the constructor path (`SharedObjectModel.cs:142-148`) end in `uses unsupported type '...'` too. A reason needs new message text, which no earlier item allowed. The plan adds no message text. Rejection tests pin the ID, the member name, and the full union type name.

D1-D3 change no existing output. Before 035, every `DecodeOnly` descriptor is a `StaticHelper` (callbacks, `SharedObjectBaseCodec`). `AsCompositeChildCodec` and `AsJavaScriptCodecArgument` already exclude that shape, and the property path rejects both earlier (`ContainsJavaScriptCallback`, `isParameter: false`).

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one carrier struct, one kind table on each side of the boundary (the generator's for diagnostics, the leaf codecs' for dispatch), no try-one-then-the-other decoding.
  2. Performance: one `Kind` read, then the member codec. An ordinary union allocates no scope. A union with an `ArrayBuffer` member registers that one lease, exactly as a direct `ArrayBuffer` parameter does.
  3. Correctness: a scoped member keeps the 030 ownership rules through the same `IGeneratedScopedCodec<T>` protocol. The carrier holds no wrapper of its own.
- New production files (exactly five): `Expo.ModulesCore/Either.cs`, `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs`, `Codecs/EitherCodec.cs`, `Codecs/GeneratedScopedEitherCodec.cs`, `Codecs/GeneratedScopedOrdinaryCodec.cs`. Edited production files: the seven leaf codecs (one interface and one static property each), `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`, and (D1) `ExpoModulesGenerator.ModuleAnalysis.cs`. `ExpoCodecDescriptor.cs`, `ExpoModulesGenerator.Emission.cs`, `EventAnalysis.cs`, `SharedObjectModel.cs`, `SharedObjectValidation.cs`, `ExpoModulesDiagnostics.cs`, and every other codec do not change.
- No new diagnostic IDs and no new or changed diagnostic message text (D4).
- The kind message is exact: `Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.`, where each value is the `JavaScriptValueKind` name (`String`, `Bool`, `Number`, `ArrayBuffer`, `Null`, `Undefined`, `Object`, `Function`). The exception type is `InvalidOperationException`.
- No existing test changes and no output-contract digest moves. No fixture or test in `Expo.ModulesCore.Generator.Tests` mentions `Either` (`rg -n Either packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests` prints nothing at plan time). A moved `GeneratorPreserves*` or `GeneratorPins*` digest is a finding: stop and report.
- The generator never derives a kind or a policy from codec text. 029's typed-policy scan stays empty (it is empty at plan time):
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- The spec's two code scans print nothing (both empty at plan time). Any hit is a finding:
  - `rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs`. No union codec catches a member failure; that is what "no try-one-then-the-other" means.
  - `rg -n 'EitherCodec<[^>]*(SharedObject|JavaScriptValue|JavaScriptCallback)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests`. A rejection test asserts absence with the bare string `"EitherCodec"` only (it is also a substring of `GeneratedScopedEitherCodec`).
- Counter rules (from 031): exact-once `ArrayBuffer` assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native **entries**: each decode of a JS buffer creates one entry. `ReleasedValues` also counts traversal temporaries, so read it only right after `ResetCounters()` with no JavaScript evaluation in between.
- JavaScript error text rule: a sync host-function failure reaches JavaScript as an `Error` whose `message` holds the managed exception text plus more (type name, stack). Tests use `Contains` on the kind message, never equality.
- A count, message, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming and doc-comment style. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 fbb3459..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1264 passed, 0 skipped at `fbb3459`)                                                                                            |

Test-case arithmetic (each `InlineData` row is one case): Task 1 adds 21, Task 2 adds 51, Task 3 adds 13, nothing is deleted. The full suite reads 1285 after Task 1, 1336 after Task 2, and 1349 after Task 3.

## Review Focus

1. **JavaScript passes a `Uint8Array` view to `Either<string, ArrayBuffer>`** (what a 038 `write` caller will try). Its kind is `Object`, so the call fails with the kind message before authored code runs, and no lease is created (inferred from the bridge's `getValueKind`, per the brief). Tests: Task 1 `ScopedUnionRejectsOtherKindsWithoutRegistering` (`new Uint8Array(2)` row), Task 3 `UnionParameterRejectsOtherKindsBeforeAuthoredCode` (`new Uint8Array([1, 2, 3])` row).
2. **An async method takes the union.** The buffer lease survives the `await` and releases once at settlement; the string path holds no lease. The spec's scenario is sync-only. Test: Task 3 `UnionParameterReleasesTheBufferAfterAsyncSettlement`.
3. **A later parameter fails to decode after the union decoded a buffer.** The union's lease releases once, and authored code never runs. Test: Task 3 `LaterParameterFailureReleasesTheEarlierUnionLease`.
4. **A bad union argument reaches a `[JS]` constructor.** The constructor fails before authored code runs and leaves no instance in the registry. Test: Task 3 `UnionParameterRejectsOtherKindsBeforeAuthoredCode` (constructor part and `SharedObjects.Count` check).
5. **A member type carries its own nullability** (`Either<string?, double>`, `Either<double?, bool>`). `null` is not a value kind, so both report `EXPOJSI001`. Without the check, `string?` would pass the `SpecialType` test and produce a codec that does not implement `IJavaScriptValueKindCodec` (CS0315 in generated code). Test: Task 2 `GeneratorRejectsUnsupportedUnionMembers` (two rows).

---

### Task 1: Union carrier and codecs

Model: **standard**.

This task must land before Task 2. The generator test host compiles generated code against the real `Expo.ModulesCore` assembly, so Task 2's acceptance rows need these types.

**Files:**

- Create: `Expo.ModulesCore/Either.cs`, `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs`, `Expo.ModulesCore/Codecs/EitherCodec.cs`, `Expo.ModulesCore/Codecs/GeneratedScopedEitherCodec.cs`, `Expo.ModulesCore/Codecs/GeneratedScopedOrdinaryCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/StringCodec.cs:5`, `BoolCodec.cs:5`, `NumberCodec.cs:6-7`, `ArrayBufferCodec.cs:5`, `ByteArrayCodec.cs:5`, `MemoryByteCodec.cs:10`, `ReadOnlyMemoryByteCodec.cs:10`
- Test (create): `Expo.ModulesCore.Tests/Codecs/EitherCodecTests.cs`

**Interfaces:**

- Produces (namespace `Expo.ModulesCore`, author-facing; doc comments on the type and every member):

```csharp
public readonly struct Either<TFirst, TSecond>
{
  public static Either<TFirst, TSecond> FromFirst(TFirst value);
  public static Either<TFirst, TSecond> FromSecond(TSecond value);
  public bool IsFirst { get; }
  public TFirst First { get; }    // InvalidOperationException "The union holds its second member." when !IsFirst
  public TSecond Second { get; }  // InvalidOperationException "The union holds its first member." when IsFirst
  public TResult Match<TResult>(Func<TFirst, TResult> first, Func<TSecond, TResult> second); // ArgumentNullException.ThrowIfNull on both
}
```

Two private fields plus `IsFirst`; the inactive field holds `default!`. `default(Either<,>)` is `IsFirst == false` with a default `Second` (spec). No implicit conversions, no equality members. The two exception messages are this plan's choice (the spec pins only the type).

- Produces (namespace `Expo.ModulesCore.Codecs`, `using Expo.JSI;`):

```csharp
public interface IJavaScriptValueKindCodec
{
  static abstract JavaScriptValueKind Kind { get; }
}

public readonly struct EitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IJavaScriptCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IJavaScriptCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IJavaScriptCodec<TSecond>, IJavaScriptValueKindCodec
// Decode(JavaScriptValueRef, runtime); Decode(JavaScriptValue, runtime) => Decode(value.Ref, runtime);
// Encode(Either value, runtime) => value.IsFirst ? TFirstCodec.Encode(value.First, runtime) : TSecondCodec.Encode(value.Second, runtime)

public readonly struct GeneratedScopedEitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IGeneratedScopedCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IGeneratedScopedCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IGeneratedScopedCodec<TSecond>, IJavaScriptValueKindCodec
// Decode(ref, runtime, scope); Transfer and EncodeBorrowed delegate to the active member.

public readonly struct GeneratedScopedOrdinaryCodec<T, TCodec>
    : IGeneratedScopedCodec<T>, IJavaScriptValueKindCodec
    where TCodec : IJavaScriptCodec<T>, IJavaScriptValueKindCodec
// Decode(ref, runtime, scope) => TCodec.Decode(value, runtime)  (never touches scope)
// Transfer(value, scope) { }  (no-op: an ordinary value holds no leaf)
// EncodeBorrowed(value, runtime) => TCodec.Encode(value, runtime)
// Kind => TCodec.Kind
```

Decode in both union codecs (the one algorithm the signatures leave open; the ordinary one omits `scope`):

```csharp
var kind = value.Kind;
if (kind == TFirstCodec.Kind) return Either<TFirst, TSecond>.FromFirst(TFirstCodec.Decode(value, runtime, scope));
if (kind == TSecondCodec.Kind) return Either<TFirst, TSecond>.FromSecond(TSecondCodec.Decode(value, runtime, scope));
throw new InvalidOperationException($"Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.");
```

No `try`/`catch` anywhere in the union codecs. Doc comments follow `GeneratedScopedNullableCodec.cs:5-12`: the three codecs say "This helper is for generated bindings, not ordinary module author code."; the union codecs' remarks say decode reads the kind once and never falls back to the other member; the adapter's remarks say it lifts an ordinary leaf codec into the scoped protocol so a union can pair it with a scoped member.

- Each leaf codec adds `IJavaScriptValueKindCodec` to its interface list and one line `public static JavaScriptValueKind Kind => JavaScriptValueKind.<Value>;`: `StringCodec` → `String`; `BoolCodec` → `Bool`; `NumberCodec<T>` → `Number`; `ArrayBufferCodec`, `ByteArrayCodec`, `MemoryByteCodec`, `ReadOnlyMemoryByteCodec` → `ArrayBuffer`. Nothing else implements it (`DoubleCodec`, enum, and convertible codecs stay as they are).

Below, `O` is `EitherCodec<string, StringCodec, double, NumberCodec<double>>`, `S` is `GeneratedScopedEitherCodec<string, GeneratedScopedOrdinaryCodec<string, StringCodec>, ArrayBuffer, ArrayBufferCodec>`, and `A` is `GeneratedScopedOrdinaryCodec<string, StringCodec>`. Declare them as `using` aliases with fully qualified names, as `ScopedCodecTests.cs:5-7` does.

- [ ] **Step 1: Write the failing tests** in `EitherCodecTests.cs` (namespace `Expo.ModulesCore.Tests.Codecs`; `HermesRuntimeFixture` pattern of `ScopedCodecTests.cs`). Every JavaScript input comes from `fixture.Evaluate(...)` inside `fixture.Runtime.Execute`.
  - `OrdinaryUnionDecodesEachMemberByKind`, theory `(string input, string expected)`: `("'x'", "first:x")`, `("''", "first:")`, `("2.5", "second:2.5")`, `("0", "second:0")`. Decode with both `O.Decode` overloads; describe each result as `IsFirst ? $"first:{First}" : $"second:{Second.ToString(CultureInfo.InvariantCulture)}"`; both equal `expected`. `''` proves the kind test is not a truthiness test.
  - `OrdinaryUnionRejectsOtherKinds`, theory `(string input, string kind)`: `("true", "Bool")`, `("({})", "Object")`, `("null", "Null")`, `("undefined", "Undefined")`, `("new ArrayBuffer(1)", "ArrayBuffer")`. Both overloads throw `InvalidOperationException` whose message `Assert.Equal`s `$"Value kind '{kind}' is not one of 'String' or 'Number'."`.
  - `OrdinaryUnionEncodesTheActiveMember`: `O.Encode(FromFirst("a"), runtime).AsString()` is `"a"`; `O.Encode(FromSecond(2.5), runtime).AsDouble()` is `2.5` (dispose both values).
  - `ScopedUnionDecodesStringWithoutRegistering`: copy `ScopedCodecTests.ArrayBufferScopedDecodeRegistersOneLease` (`:96-115`). Inside `Execute`: `using var source = Evaluate("'x'")`; `var decoded = S.Decode(source.Ref, runtime, scope)`; `Assert.True(decoded.IsFirst)`; `Assert.Equal("x", decoded.First)`. After `Execute`: `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`; `LongLivedArrayBuffersReleased == 0u`. Then repeat the decode of `'x'` into a second scope that was disposed before the call: it returns `IsFirst` with `"x"` and does not throw, which proves the string path never calls `Register` (`GeneratedConversionScope.cs:26` throws `ObjectDisposedException` on a disposed scope).
  - `ScopedUnionDecodesArrayBufferIntoOneLease`: the same with `new Uint8Array([1, 2, 3]).buffer`; inside `Execute`, `Assert.False(decoded.IsFirst)` and `Assert.Equal(new byte[] { 1, 2, 3 }, decoded.Second.ToArray())`. After dispose: `LongLivedArrayBuffersReleased == 1u` and `Assert.Throws<ObjectDisposedException>(() => _ = decoded.Second.ByteLength)`.
  - `ScopedUnionRejectsOtherKindsWithoutRegistering`, theory `(string input, string kind)`: `("5", "Number")`, `("({})", "Object")`, `("null", "Null")`, `("undefined", "Undefined")`, `("new Uint8Array(2)", "Object")` (the last inferred; Review Focus 1). `S.Decode` throws `InvalidOperationException` with message `$"Value kind '{kind}' is not one of 'String' or 'ArrayBuffer'."`. After `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`: `LongLivedArrayBuffersReleased == 0u`.
  - `ScopedUnionTransferAndEncodeFollowTheActiveMember`: inside `Execute`, `using var source = Evaluate("new ArrayBuffer(4)")`, set it as global `b`; `var buffer = ArrayBufferCodec.Decode(source.Ref, runtime)`. `S.Transfer(FromFirst("x"), scope)`; `S.Transfer(FromSecond(buffer), scope)`. `using var text = S.EncodeBorrowed(FromFirst("x"), runtime)` reads `"x"`; `using var encoded = S.EncodeBorrowed(FromSecond(buffer), runtime)`, set as global `r`; `b === r` is `true` (pattern of `ArrayBufferEncodeBorrowedKeepsSourceAndIdentity`, `:117-138`). After `Execute`: `ResetCounters()`, `scope.Dispose()`, `WaitUntilIdle()`; `LongLivedArrayBuffersReleased == 1u` (only the second transfer registered).
  - `OrdinaryAdapterNeverTouchesTheScope`: create a scope and dispose it first (`GeneratedConversionScope.Register` throws `ObjectDisposedException` on a disposed scope, `GeneratedConversionScope.cs:26`). `A.Decode(Evaluate("'x'").Ref, runtime, disposedScope)` returns `"x"`; `A.Transfer("x", disposedScope)` does not throw; `A.EncodeBorrowed("x", runtime).AsString()` is `"x"`; `A.Kind == JavaScriptValueKind.String`.
  - `CarrierExposesOnlyTheActiveMember` (no runtime): `var first = Either<string, int>.FromFirst("a")`: `IsFirst`, `First == "a"`, `Second` throws `InvalidOperationException` with `The union holds its first member.`, `first.Match(s => s.Length, n => -n) == 1`. `var second = Either<string, int>.FromSecond(7)`: `!IsFirst`, `Second == 7`, `First` throws with `The union holds its second member.`, `Match` gives `-7`. `default(Either<string, int>)`: `!IsFirst`, `Second == 0`. `Match(null!, n => n)` throws `ArgumentNullException`.
  - `LeafCodecsReportTheirValueKind` (no runtime): `StringCodec.Kind == String`, `BoolCodec.Kind == Bool`, `NumberCodec<int>.Kind == Number`, `NumberCodec<double>.Kind == Number`, `ArrayBufferCodec.Kind`, `ByteArrayCodec.Kind`, `MemoryByteCodec.Kind`, `ReadOnlyMemoryByteCodec.Kind` all `== ArrayBuffer`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~EitherCodecTests"`
Expected: build fails with CS0246 or CS0117 (`Either<,>`, the codecs, or `Kind` not found). Put the observed errors in the task report. If the build fails for another reason, stop and report.

- [ ] **Step 3: Create the five files and edit the seven leaf codecs** as the Interfaces block states.

- [ ] **Step 4: Run the Step 2 command.** Expected: PASS, 21 cases. A message or count that differs is a finding.

- [ ] **Step 5: Run both suites and the scan.** Run: Generator tests, then ModulesCore tests; then `rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs`. Expected: all pass, no generated text changes, the scan prints nothing. Full suite count 1285.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(modules-core): add two-member union codecs" \
  -m "Either<TFirst, TSecond> carries one of two typed values. Seven leaf codecs report their JavaScript value kind through the static IJavaScriptValueKindCodec.Kind. EitherCodec and GeneratedScopedEitherCodec read the kind once and decode through the matching member codec, or throw one kind message; they never try a member and fall back. GeneratedScopedOrdinaryCodec lifts an ordinary leaf codec into the scoped protocol, so a string member can pair with a scoped ArrayBuffer member." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator resolves union parameters

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (`GetCodec` at `:500-591`; new resolver beside `TryGetReadOnlyListCodec` at `:298`; composite branches at `:163-174`, `:236-244`, `:259-276`, `:283-288`, `:318-326`, `:359-368`; `AsJavaScriptCodecArgument` and the comments at `:77-87`)
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.ModuleAnalysis.cs:808-809` (D1)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs` (new tests after `GeneratorAcceptsNullableArrayBufferOnSharedObjectMembers`, which ends at `:5225`)

**Interfaces:**

- Consumes: Task 1's types.
- Produces (all `private static`, in `ExpoModulesGenerator.Codecs.cs`):
  - `const string EitherMetadataName = "global::Expo.ModulesCore.Either<TFirst, TSecond>";` (compared with `namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`, as `IReadOnlyList<T>` is at `:303-305`).
  - `string? GetUnionMemberKind(ITypeSymbol typeSymbol)`. The generator-side kind table. It returns `"String"`, `"Bool"`, `"Number"`, `"ArrayBuffer"`, or `null`. The names mirror `JavaScriptValueKind` and the leaf codecs' `Kind`; say so in its comment. Rules, in order:
    1. `typeSymbol.NullableAnnotation == NullableAnnotation.Annotated` → `null` (Review Focus 5; `null` is not a kind).
    2. `SpecialType.System_String` → `"String"`; `System_Boolean` → `"Bool"`; the ten numeric `SpecialType`s of the `GetCodec` switch (`:578-587`, `SByte` through `Double`) → `"Number"`.
    3. `IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: System_Byte }` → `"ArrayBuffer"`; fully qualified display `ArrayBufferMetadataName`, `"global::System.Memory<byte>"`, or `"global::System.ReadOnlyMemory<byte>"` → `"ArrayBuffer"`.
    4. Anything else → `null` (records, lists, dictionaries, shared objects, callbacks, `JavaScriptValue`, convertibles such as `Uri`, enums, `decimal`, `Nullable<T>`, a nested `Either<,>`).
  - `ExpoCodecDescriptor? GetEitherCodec(INamedTypeSymbol typeSymbol, List<ExpoDiagnosticModel> diagnostics, List<ExpoGeneratedRecordCodecModel> recordCodecs)`:
    1. Look up both member kinds first. Return `null` if either is `null` or both are equal. This runs before any member `GetCodec`, so a rejected union never adds a record codec or an `EXPOJSI007` as a side effect.
    2. `AsCompositeChildCodec(GetCodec(member, ...))` for each member; return `null` if either is `null` (defensive; every table type resolves).
    3. Type-argument text is `GetCodecTypeArgumentName(member)`. Both members ordinary → `OrdinaryCodec($"EitherCodec<{f}, {fc}, {s}, {sc}>") with { Capabilities = ExpoCodecCapabilities.DecodeOnly }`. Any member `IsScopedResource` → `ScopedCodec($"GeneratedScopedEitherCodec<{f}, {lift(f, fc)}, {s}, {lift(s, sc)}>") with { Capabilities = ExpoCodecCapabilities.DecodeOnly }`, where `lift` keeps a scoped member's expression and wraps an ordinary one as `GeneratedScopedOrdinaryCodec<{name}, {expression}>`. The branch decides from `IsScopedResource`, never from text.
       One comment above it: a union is decode-only and accepted only at parameters, and its members must be of different JavaScript kinds.
  - In `GetCodec`, right after the `JavaScriptValue` branch (`:542-548`) and before `TryGetNullableCodec`: when `typeSymbol is INamedTypeSymbol` and its `ConstructedFrom` display equals `EitherMetadataName`, `return GetEitherCodec(...)` (also when it is `null`, so `Either<,>` never reaches later branches). `Either<,>?` keeps reaching it through `TryGetNullableCodec`.
  - **D3:** every composite descriptor built over a child codec ends with `with { Capabilities = <child>.Capabilities }`, merged into the existing `with { DecodeOperation = ... }` where one exists: `TryGetNullableCodec` (`:172-174`, child `valueCodec`), the nullable-reference list (`:239-244`), dictionary (`:262-276`), and fall-through (`:284-287`) branches, `TryGetReadOnlyListCodec` (`:318-326`), and `TryGetDictionaryCodec` (`:359-368`). Add one sentence to the composition comment at `:77-79`: "A composite has its child's capabilities, so it is decode-only over a decode-only child."
  - **D2:** `AsJavaScriptCodecArgument` becomes `codec is { Shape: ExpoCodecShape.JavaScriptCodec, Capabilities: ExpoCodecCapabilities.DecodeAndEncode } ? codec : null;`. Extend its comment: a decode-only `JavaScriptCodec` (an ordinary union) is rejected there too, because a union is a parameter type only.
  - **D1:** in `GetProperties`, between the end of the `else` block (`ModuleAnalysis.cs:808`) and `if (codec is null)` (`:809`): `if (codec is { Capabilities: ExpoCodecCapabilities.DecodeOnly }) { codec = null; }` with the comment `// Encode position: a property getter is encoded, so a decode-only codec has no valid output.` (wording of `:567`). Shared-object properties reach it through `SharedObjectModel.cs:45`.

Why nothing else changes: emission reads only `Expression`, `IsScopedResource`, `DecodeOperation`, and `ContextRequirement` (`Codecs.cs:450-470`, `Emission.cs:963-971`), so a scoped union parameter allocates `__expoScope` and an ordinary one does not. A rejected parameter returns `null` from `GetCodec`, and the existing parameter paths report `EXPOJSI001` (`ModuleAnalysis.cs:659-676`), translated to `EXPOJSI023` on shared-object methods (`SharedObjectValidation.cs:84`), and `EXPOJSI023` on a constructor (`SharedObjectModel.cs:142-148`). `TryFindNestedSharedObjectType` does not descend into `Either<,>` (`SharedObjectValidation.cs:162-170`), so `Either<string, Entry>` takes the `GetCodec` path and reports `uses unsupported type`, not the "inside a composed codec" text.

Generated text. `$S` stands for `GeneratedScopedEitherCodec<string, GeneratedScopedOrdinaryCodec<string, StringCodec>, global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>`, `$O` for `EitherCodec<double, NumberCodec<double>, bool, BoolCodec>`, and `$U` for the union type argument `global::Expo.ModulesCore.Either<string, global::Expo.ModulesCore.ArrayBuffer>`. In the tests, declare them as `private const string` fields (`UnionScopedCodec`, `UnionOrdinaryCodec`, `UnionScopedType`) beside `Nabc` (`:5152`) and build `InlineData` arguments by `const` concatenation.

- [ ] **Step 1: Write the acceptance tests.** Each theory asserts `Assert.Empty(GeneratorDiagnosticIds(result))` and `Assert.Contains(fragment, GeneratedText(result), StringComparison.Ordinal)`. `ModuleSource` rows also assert `AssertNoCompilerDiagnostics(result)`; `SharedObjectMemberSource` rows assert `AssertNoCompilerErrors(result)` (as 031's shared-object family does).
  - `GeneratorAcceptsScopedUnionParameters`, theory `(string member, string fragment)` over `ModuleSource(string.Empty, member)`; each row also contains `__expoScope = new GeneratedConversionScope();` (the sync and async forms share it):
    - `[JS] public string M(Either<string, ArrayBuffer> content) => "";` → `var __expoArg0 = $S.Decode(arguments.GetValue(0), runtime, __expoScope);`
    - `[JS] public Task<string> M(Either<string, ArrayBuffer> content) => Task.FromResult("");` → `var __expoArg0 = $S.Decode(arguments.GetValue(0), jsRuntime, __expoScope);`
    - `[JS] public string M(string path, Either<string, ArrayBuffer> content) => path;` → `var __expoArg1 = $S.Decode(arguments.GetValue(1), runtime, __expoScope);`
    - `[JS] public string M(Either<string, ArrayBuffer>? content) => "";` → `var __expoArg0 = GeneratedScopedNullableCodec<$U, $S>.Decode(arguments.GetValue(0), runtime, __expoScope);`
    - `[JS] public int M(IReadOnlyList<Either<string, ArrayBuffer>> parts) => 0;` → `GeneratedScopedListCodec<$U, $S>.Decode(arguments.GetValue(0), runtime, __expoScope)`
    - `[JS] public int M(IReadOnlyDictionary<string, Either<string, ArrayBuffer>> parts) => 0;` → `GeneratedScopedDictionaryCodec<$U, $S>.Decode(arguments.GetValue(0), runtime, __expoScope)`
  - `GeneratorAcceptsOrdinaryUnionParametersWithoutAScope` (spec scenario "Ordinary union keeps the binding scope-free"), theory `(string member, string fragment)` over `ModuleSource`; every row also asserts `Assert.DoesNotContain("GeneratedConversionScope", text, StringComparison.Ordinal)`:
    - `[JS] public int M(Either<double, bool> value) => 0;` → `var __expoArg0 = $O.Decode(arguments.GetValue(0), runtime);`
    - `[JS] public Task<int> M(Either<double, bool> value) => Task.FromResult(0);` → `var __expoArg0 = $O.Decode(arguments.GetValue(0), jsRuntime);`
    - `[JS] public int M(Either<int, string> value) => 0;` → `EitherCodec<int, NumberCodec<int>, string, StringCodec>.Decode(arguments.GetValue(0), runtime)`
    - `[JS] public int M(Either<string, byte[]> value) => 0;` → `EitherCodec<string, StringCodec, byte[], ByteArrayCodec>.Decode(arguments.GetValue(0), runtime)`
    - `[JS] public int M(Either<bool, ReadOnlyMemory<byte>> value) => 0;` → `EitherCodec<bool, BoolCodec, global::System.ReadOnlyMemory<byte>, ReadOnlyMemoryByteCodec>.Decode(arguments.GetValue(0), runtime)`
    - `[JS] public int M(Either<double, bool>? value) => 0;` → `NullableCodec<global::Expo.ModulesCore.Either<double, bool>, $O>.Decode(arguments.GetValue(0), runtime)`
    - `[JS] public int M(IReadOnlyList<Either<double, bool>> values) => 0;` → `JavaScriptArrayCodec<global::Expo.ModulesCore.Either<double, bool>, $O>.DecodeToArray(arguments.GetValue(0), runtime)`
  - `GeneratorAcceptsUnionParametersOnSharedObjects`, theory `(string member, string fragment)` over `SharedObjectMemberSource(member)`:
    - `[JS] public Entry(Either<string, ArrayBuffer> seed) { }` → `var __expoArg0 = $S.Decode(arguments.GetValue(0), runtime, __expoScope);`
    - `[JS] public string Write(Either<string, ArrayBuffer> content) => "";` → the same fragment
    - `[JS] public Task<string> WriteLater(Either<string, ArrayBuffer> content) => Task.FromResult("");` → `var __expoArg0 = $S.Decode(arguments.GetValue(0), jsRuntime, __expoScope);`
    - `[JS] public int Pick(Either<double, bool> value) => 0;` → `var __expoArg0 = $O.Decode(arguments.GetValue(0), runtime);`

- [ ] **Step 2: Write the rejection tests.** Each row asserts `AssertNoCompilerErrors(result)`, the exact ID list, and that the generated text contains neither `"EitherCodec"` nor the member's quoted JavaScript name.
  - `GeneratorRejectsUnsupportedUnionMembers` (spec scenario "Unsupported members report a diagnostic"; Review Focus 5), theory `(string declarations, string typeArguments, string diagnosticType)`. Source `ModuleSource(declarations, $"[JS] public int M(Either<{typeArguments}> value) => 0;")`. IDs equal `["EXPOJSI001"]`; the message contains `"on 'M'"` and `$"'{diagnosticType}'"`; the text does not contain `"\"m\""`. `diagnosticType` is `global::Expo.ModulesCore.Either<` + the column + `>`:

| declarations                                | typeArguments                     | diagnosticType inner text                                          | rule                 |
| ------------------------------------------- | --------------------------------- | ------------------------------------------------------------------ | -------------------- |
|                                             | `string, Uri`                     | `string, global::System.Uri`                                       | outside the table    |
|                                             | `double, int`                     | `double, int`                                                      | same kind            |
|                                             | `string, string`                  | `string, string`                                                   | same kind            |
|                                             | `ArrayBuffer, byte[]`             | `global::Expo.ModulesCore.ArrayBuffer, byte[]`                     | same kind            |
|                                             | `string?, double`                 | `string, double` (inferred: the format drops `?`)                  | annotated member     |
|                                             | `double?, bool`                   | `double?, bool`                                                    | `Nullable<T>` member |
| `public sealed record Holder(string Name);` | `string, Holder`                  | `string, global::Expo.TestModules.Holder`                          | `Object` kind        |
|                                             | `string, JavaScriptValue`         | `string, global::Expo.JSI.JavaScriptValue`                         | `Object` kind        |
|                                             | `string, JavaScriptCallback<int>` | `string, global::Expo.ModulesCore.JavaScriptCallback<int>`         | `Function` kind      |
|                                             | `string, IReadOnlyList<string>`   | `string, global::System.Collections.Generic.IReadOnlyList<string>` | `Object` kind        |
|                                             | `string, Either<double, bool>`    | `string, global::Expo.ModulesCore.Either<double, bool>`            | nested union         |

    The `Holder` row also asserts the text does not contain `HolderCodec_` (the kind check runs before member resolution).

- `GeneratorRejectsUnsupportedUnionsOnSharedObjectMembers` (spec scenario row `Either<string, SharedObjectClass>`), theory `(string member, string memberName, string reason)` over `SharedObjectMemberSource(member)`. IDs equal `["EXPOJSI023"]`; the message contains `$"'{memberName}'"` and `reason`:
  - `[JS] public string Write(Either<string, Entry> content) => "";`, `Write`, `parameter 'content' uses unsupported type 'global::Expo.ModulesCore.Either<string, global::Expo.TestModules.Entry>'`
  - `[JS] public Entry(Either<string, Entry> other) { }`, `Entry`, `its [JS] constructor parameter 'other' uses unsupported type 'global::Expo.ModulesCore.Either<string, global::Expo.TestModules.Entry>'`
  - `[JS] public int Pick(Either<double, int> value) => 0;`, `Pick`, `parameter 'value' uses unsupported type 'global::Expo.ModulesCore.Either<double, int>'`
  - `[JS] public Entry(Either<double, int> value) { }`, `Entry`, `its [JS] constructor parameter 'value' uses unsupported type 'global::Expo.ModulesCore.Either<double, int>'`
- `GeneratorKeepsUnionsDecodeOnly` (spec scenario row "`Either<,>` as a return, async result, property, or record field"; D1-D3), theory `(bool onSharedObject, string declarations, string member, string expectedIds, string jsName)` over `SharedObjectMemberSource(member, declarations)` or `ModuleSource(declarations, member)`. IDs equal `expectedIds.Split(',')`; the text does not contain `$"\"{jsName}\""`. The last column says which guard keeps the row green once the resolver exists (Step 6):

| #   | shared | declarations                                                                    | member                                                                                                      | ids                     | jsName      | guard           |
| --- | ------ | ------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ----------------------- | ----------- | --------------- |
| 1   | false  |                                                                                 | `[JS] public Either<string, double> M() => default;`                                                        | `EXPOJSI002`            | `m`         | existing `:568` |
| 2   | false  |                                                                                 | `[JS] public Either<string, ArrayBuffer> M() => default;`                                                   | `EXPOJSI002`            | `m`         | existing `:568` |
| 3   | false  |                                                                                 | `[JS] public Task<Either<string, double>> M() => Task.FromResult(default(Either<string, double>));`         | `EXPOJSI002`            | `m`         | existing `:511` |
| 4   | false  |                                                                                 | `[JS] public Either<string, double>? M() => null;`                                                          | `EXPOJSI002`            | `m`         | D3              |
| 5   | false  |                                                                                 | `[JS] public IReadOnlyList<Either<string, ArrayBuffer>> M() => Array.Empty<Either<string, ArrayBuffer>>();` | `EXPOJSI002`            | `m`         | D3              |
| 6   | false  |                                                                                 | `[JS] public Dictionary<string, Either<double, bool>> M() => new();`                                        | `EXPOJSI002`            | `m`         | D3              |
| 7   | false  |                                                                                 | `[JS] public Either<string, double> Value { get; set; }`                                                    | `EXPOJSI015`            | `value`     | D1              |
| 8   | false  |                                                                                 | `[JS] public Either<string, ArrayBuffer> Content => default;`                                               | `EXPOJSI015`            | `content`   | D1              |
| 9   | false  |                                                                                 | `[JS] public Either<double, bool>? Value { get; set; }`                                                     | `EXPOJSI015`            | `value`     | D1, D3          |
| 10  | false  | `public sealed record Holder(Either<string, double> Content);`                  | `[JS] public int M(Holder holder) => 0;`                                                                    | `EXPOJSI007,EXPOJSI001` | `m`         | existing `:400` |
| 11  | false  | `public sealed record Holder(Either<string, double> Content);`                  | `[JS] public Holder M() => null!;`                                                                          | `EXPOJSI007,EXPOJSI002` | `m`         | existing `:400` |
| 12  | false  | `public sealed record Shelf(IReadOnlyList<Either<string, ArrayBuffer>> Parts);` | `[JS] public int M(Shelf shelf) => 0;`                                                                      | `EXPOJSI007,EXPOJSI001` | `m`         | D3              |
| 13  | false  |                                                                                 | `[JS] public int M(JavaScriptCallback<Either<string, double>> callback) => 0;`                              | `EXPOJSI008`            | `m`         | D2              |
| 14  | false  |                                                                                 | `[JS] public int M(JavaScriptCallback<ValueTuple<Either<string, double>>, int> callback) => 0;`             | `EXPOJSI008`            | `m`         | D2              |
| 15  | false  |                                                                                 | `[Event] public partial Func<Either<string, double>, Task> OnValue { get; }`                                | `EXPOJSI019`            | `onValue`   | D2              |
| 16  | false  |                                                                                 | `[Event] public partial Func<Either<string, ArrayBuffer>, Task> OnContent { get; }`                         | `EXPOJSI019`            | `onContent` | existing shape  |
| 17  | true   |                                                                                 | `[JS] public Either<string, double> Give() => default;`                                                     | `EXPOJSI023`            | `give`      | existing `:568` |
| 18  | true   |                                                                                 | `[JS] public Either<string, double> Value { get; set; }`                                                    | `EXPOJSI023`            | `value`     | D1              |
| 19  | true   |                                                                                 | `[Event] public partial Func<Either<string, double>, Task> OnValue { get; }`                                | `EXPOJSI027`            | `onValue`   | D2              |

    Rows 15, 16, and 19 also assert the event diagnostic's message contains `no encode-capable codec is available` (`EventAnalysis.cs:221-230`). The event and callback rows are inferred.

- [ ] **Step 3: Run the new tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~Union"`
Expected: every row of the three `GeneratorAccepts*Union*` theories fails with one `EXPOJSI001` (`EXPOJSI023` on shared objects): `Either<,>` has no codec yet. Every rejection row passes: an unresolved union already reports the same IDs. Put the observed failures in the task report. If a rejection row fails, stop and report.

- [ ] **Step 4: Add the kind table, `GetEitherCodec`, and the `GetCodec` branch** as the Interfaces block states. Do not touch the composite branches, `AsJavaScriptCodecArgument`, or `GetProperties` yet.

- [ ] **Step 5: Run the Step 3 command.** Expected: every acceptance row passes. In `GeneratorKeepsUnionsDecodeOnly`, exactly rows 4, 5, 6, 7, 8, 9, 12, 13, 14, 15, 18, and 19 fail, each because the member is now accepted (inferred). Every other row passes. Put the failing row numbers in the task report; a different set is a finding. This step is the evidence for D1-D3.

- [ ] **Step 6: Add the three guards** (D3 composite capabilities, D2 `AsJavaScriptCodecArgument`, D1 property check) as the Interfaces block states.

- [ ] **Step 7: Run the Step 3 command.** Expected: PASS, 51 cases.

- [ ] **Step 8: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped, full suite 1336. Every `GeneratorPreserves*` and `GeneratorPins*` contract test passes unchanged. If any existing test fails, stop: unrelated text or policy changed.

- [ ] **Step 9: Run the 029 typed-policy scan and the spec's two code scans** from Global Constraints. Expected: all print nothing.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(generator): accept two-member union parameters" \
  -m "Either<TFirst, TSecond> resolves to the decode-only EitherCodec when both members are ordinary, and to GeneratedScopedEitherCodec when one is an ArrayBuffer, with the ordinary member lifted by GeneratedScopedOrdinaryCodec. A type-keyed table maps each member to String, Bool, Number, or ArrayBuffer before any member codec resolves; a member outside the table, an annotated member, or two members of one kind report the parameter's existing diagnostic. Emission is unchanged." \
  -m "Keep unions decode-only: a composite now takes its child's capabilities, callback and event type arguments require an encode-capable codec, and properties reject decode-only codecs as returns already did. No earlier descriptor was affected, so no existing test or digest moves." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove union arguments on Hermes

Model: **standard**.

This task adds fixtures and tests only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (append after `BaseThingsModule`, which ends at line 884)
- Test: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs` (insert before the private `ResourceScopeFixture` class at line 915)

**Interfaces:**

- Consumes: Task 1's types, Task 2's generator support.
- Produces (fixtures, namespace `Expo.ModulesCore.Tests.Modules`; JavaScript names are the lower-camel defaults):

```csharp
[ExpoSharedObject]
public sealed partial class UnionWriter : SharedObject
{
  private readonly string seed;
  [JS] public UnionWriter(Either<string, ArrayBuffer> seed)   // this.seed = UnionThingsModule.Describe(seed);
  [JS] public string ReadSeed() => seed;
  [JS] public string Write(Either<string, ArrayBuffer> content) => UnionThingsModule.Describe(content);
  [JS] public int Pick(Either<double, bool> value) => UnionThingsModule.Score(value);
}

// Owns UnionWriter, so it does not report EXPOJSI024. No other module may list it.
[ExpoModule("UnionThings", Classes = new[] { typeof(UnionWriter) })]
public sealed partial class UnionThingsModule
{
  // Counts every authored read of a union, so a test proves a failed decode never reached authored code.
  public static int Calls;

  // Reads the active member through IsFirst, First, and Second, as authored code is expected to.
  public static string Describe(Either<string, ArrayBuffer> content)
  // Calls++; return content.IsFirst ? $"first:{content.First}" : $"second:{string.Join(",", content.Second.ToArray())}";

  public static int Score(Either<double, bool> value) => value.Match(number => 100 + (int)(number * 10), flag => flag ? 1 : 0);

  [JS] public string Write(Either<string, ArrayBuffer> content) => Describe(content);
  [JS] public async Task<string> WriteLater(Either<string, ArrayBuffer> content)   // await Task.Yield(); return Describe(content);
  [JS] public string WriteOptional(Either<string, ArrayBuffer>? content) => content is { } value ? Describe(value) : "null";
  [JS] public string WriteWithCount(Either<string, ArrayBuffer> content, int count) => $"{Describe(content)}:{count}";
  [JS] public string WriteAll(IReadOnlyList<Either<string, ArrayBuffer>> parts) => string.Join("|", parts.Select(Describe));
  [JS] public int Pick(Either<double, bool> value) => Score(value);
}
```

Common setup: the private `ResourceScopeFixture` (`ResourceScopeModuleTests.cs:915-1053`) registers the whole generated provider, so `UnionThings` is already installed. Each test starts with `using var scope = ResourceScopeFixture.Create();` and `scope.Run("globalThis.u = globalThis._expoDotnet.modules.UnionThings; globalThis.b = new Uint8Array([1, 2, 3]).buffer; true")`, then sets `UnionThingsModule.Calls = 0`. Before each measured call: `scope.Fixture.ResetCounters()`; before reading `LongLivedArrayBuffersReleased`: `scope.Fixture.WaitUntilIdle()`. `Calls` is static; tests in one class run one at a time, as `BaseThingsModule.LastPath` relies on. All counts are inferred from the entry rule and the 030/031 tests they mirror. `K(kind)` below means `$"Value kind '{kind}' is not one of 'String' or 'ArrayBuffer'."`.

- [ ] **Step 1: Add the fixtures** above.

- [ ] **Step 2: Write the tests** in `ResourceScopeModuleTests`:
  - `UnionParameterDecodesEachMemberAndReleasesTheBufferOnce` (spec scenario "String or ArrayBuffer parameter", module half): `u.write('hello')` is `"first:hello"`, count 0. `u.write(b)` is `"second:1,2,3"`, count 1. `u.write('')` is `"first:"`, count 0. `Calls == 3`.
  - `SharedObjectUnionMembersDecodeEachMemberAndReleaseOnce` (same scenario, shared-object half): `globalThis.w = new u.UnionWriter('seed'); [w.readSeed(), w.write('x')].join('|')` is `"first:seed|first:x"`, count 0. `globalThis.w2 = new u.UnionWriter(b); [w2.readSeed(), w2.write(b)].join('|')` is `"second:1,2,3|second:1,2,3"`, count 2 (the constructor decode and the method decode are two entries).
  - `UnionParameterReleasesTheBufferAfterAsyncSettlement` (Review Focus 2): `scope.StartOutcome("u.writeLater(b)")`, `await scope.WaitForOutcomeAsync()`, idle; `` `${__outcome.status}:${__outcome.value}` `` is `"fulfilled:second:1,2,3"`, count 1. Repeat with `u.writeLater('x')`: `"fulfilled:first:x"`, count 0.
  - `UnionParameterRejectsOtherKindsBeforeAuthoredCode` (spec scenario "Third kind and nullish are rejected", non-nullable half; Review Focus 1, 4), theory `(string input, string kind)`: `("5", "Number")`, `("true", "Bool")`, `("({})", "Object")`, `("null", "Null")`, `("undefined", "Undefined")`, `("new Uint8Array([1, 2, 3])", "Object")` (the last inferred). Setup also evaluates `globalThis.w = new u.UnionWriter('seed'); true`; then set `Calls = 0` and read `var before = scope.Fixture.Runtime.Execute(_ => scope.Context.SharedObjects.Count)`. Evaluate `"const v = () => (" + input + "); const err = f => { try { f(); return 'no error'; } catch (error) { return error instanceof Error ? error.message : 'not an Error'; } }; [err(() => u.write(v())), err(() => w.write(v())), err(() => new u.UnionWriter(v()))].join('|')"`. Split on `|`: 3 parts, each contains `K(kind)`. `Calls == 0`; `SharedObjects.Count == before` (the failed constructor left no instance; inferred); count 0.
  - `NullableUnionParameterPassesNullAndRejectsOtherKinds` (same scenario, nullable half): `[u.writeOptional(null), u.writeOptional(undefined), u.writeOptional('x'), u.writeOptional(b)].join('|')` is `"null|null|first:x|second:1,2,3"`, count 1. Then `u.writeOptional(5)` and `u.writeOptional({})`, each in its own `try`/`catch` returning `error.message`, contain `K("Number")` and `K("Object")`; `Calls` did not change across these two calls.
  - `OrdinaryUnionDecodesByKindNotTruthiness` (spec scenario "Ordinary union keeps the binding scope-free", runtime half): `globalThis.w = new u.UnionWriter('seed'); [u.pick(0), u.pick(1.5), u.pick(false), u.pick(true), w.pick(0), w.pick(true)].join(':')` is `"100:115:0:1:100:1"`. `u.pick('')` and `w.pick('')` each throw an `Error` whose message contains `Value kind 'String' is not one of 'Number' or 'Bool'.`; `u.pick(null)` contains `Value kind 'Null' is not one of 'Number' or 'Bool'.`.
  - `LaterParameterFailureReleasesTheEarlierUnionLease` (Review Focus 3; pattern of `LaterDecodeFailureReleasesEarlierLeafSyncAndAsync`, `:37-59`): `let failed; try { u.writeWithCount(b, 'x'); failed = 'no error'; } catch (error) { failed = error instanceof Error; } String(failed)` is `"true"`, count 1, `Calls == 0`. Control: `u.writeWithCount(b, 2)` is `"second:1,2,3:2"`, count 1.
  - `UnionListReleasesOneLeasePerBuffer` (spec "a union inside a list ... covered by one test"): `globalThis.b2 = new ArrayBuffer(2); u.writeAll(['a', b, b2])` is `"first:a|second:1,2,3|second:0,0"`, count 2.

- [ ] **Step 3: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~ResourceScopeModuleTests"`
Expected: PASS; the new tests add 13 cases. A failing string, count, or message is a finding to report with the observed value.

- [ ] **Step 4: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped, 1349 passed. Record the count for Task 4.

- [ ] **Step 5: Commit**

```bash
git commit -m "test(modules-core): prove union arguments on Hermes" \
  -m "A fixture module and shared object take Either<string, ArrayBuffer> in sync, async, optional-nullable, list, and constructor positions, and Either<double, bool> in module and shared-object methods. Tests prove that each JavaScript kind reaches the matching member, that a buffer member releases its lease once at invocation end or at async settlement while a string member holds none, that third kinds, nullish values, and typed-array views fail before authored code runs and leave no instance behind, that the nullable form maps nullish input to null, and that a later parameter failure still releases the union's lease." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/035-union-arguments.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-06-union-arguments/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the delta into `docs/specs/modules-core-boundary.md`**, as the spec's "Delta against `docs/specs/modules-core-boundary.md`" section states:
  - Insert `### Requirement: Two-Member Unions Decode By Value Kind` with its four scenarios, verbatim from the spec, right before `### Requirement: Nullable Reference Codecs Are Annotation-Driven` (line 1701), so it follows the binary-codec requirement. In the scenario "Unsupported members report a diagnostic", replace "with a reason naming the rule" by "naming the member and the union type" (D4), and say so in the task report.
  - Position table (1509-1514). Row `Top-level boundary (...)` (1509): append "A two-member union `Either<TFirst, TSecond>` resolves to the decode-only `EitherCodec` or `GeneratedScopedEitherCodec` and is accepted only as a module method, shared-object method, or shared-object constructor parameter." Rows `List element, dictionary value` (1510) and `` `Nullable<T>` inner, nullable-reference inner `` (1511): append "The composite has its child's capabilities, so it is decode-only over a decode-only child." (D3). Row `Callback result, callback-argument tuple element` (1513): "`JavaScriptCodec` with `DecodeAndEncode` only, ..." and keep the rest (D2). Row `Typed event payload` (1514): append "The payload codec must then be a `JavaScriptCodec` with `DecodeAndEncode`." (D2).
  - Descriptor table (1525-1534): after the `Authored abstract base` row, add ``| Two-member union `Either<TFirst, TSecond>` with two ordinary members (parameter only; decode-only `EitherCodec`) | `JavaScriptCodec` | `Borrowed` |`` and ``| Two-member union with an `ArrayBuffer` member (parameter only; decode-only `GeneratedScopedEitherCodec`, the ordinary member lifted by `GeneratedScopedOrdinaryCodec`) | `ScopedOnly` | `ScopedResource` |``.
    If the controller struck any of D1-D3, drop the matching sentence.

- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`.** In `### Requirement: Boundary Leaf Ownership Is Explicit` (202-213), after "A decoded `ArrayBuffer?` that is `null` holds no leaf and registers nothing." (209), insert: "A union member that is an `ArrayBuffer` is a scoped leaf registered once; the other member holds no leaf."

- [ ] **Step 3: Update `docs/module-authoring-guide.md`**, section 3, the supported-types list. After the binary-data bullet (143-152), add: "- `Either<TFirst, TSecond>` for an argument that JavaScript passes as one of two kinds, such as `string | ArrayBuffer`. Each member is `string`, `bool`, a numeric type, or a binary type (`ArrayBuffer`, `byte[]`, `Memory<byte>`, `ReadOnlyMemory<byte>`), and the two members must have different JavaScript kinds: `Either<double, bool>` works, `Either<double, int>` does not. Read `IsFirst`, then `First` or `Second`, or call `Match`. A union is a parameter type only (module methods, shared-object methods and constructors, and inside a list or dictionary parameter); it cannot be a return, property, record field, callback type argument, or event payload. JavaScript `null`, `undefined`, and any other kind fail before your code runs; `Either<TFirst, TSecond>?` turns `null` and `undefined` into C# `null`. An `ArrayBuffer` member is released at the end of the call, like a direct `ArrayBuffer` parameter."

- [ ] **Step 4: Update `docs/roadmap.md`.** Row 035 (line 60): `DONE`, link `[035](archive/plans/035-union-arguments.md)`; keep the column widths aligned. After the `**034**` note (108-115), add: "- **035**: done ([change](archive/changes/2026-11-06-union-arguments/spec.md)): it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). `Either<TFirst, TSecond>` now works as a parameter of module methods and shared-object constructors and methods when its two members have different JavaScript kinds among string, bool, number, and binary, so 038 can port `write(content: string | ArrayBuffer)`. Unions stay decode-only, and a decode-only child now makes its list, dictionary, or nullable composite decode-only too." Take `<N>` from Step 6 and `<date>` from the commit date.

- [ ] **Step 5: Move the brief and the change folder** with `git mv`. Fix every Markdown link to the old paths: `rg -n '035-union-arguments|2026-11-06-union-arguments' docs .agents AGENTS.md`. Every link left points into `archive/`. Plain-text path mentions inside the archived brief, spec, and plan are history and stay.

- [ ] **Step 6: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs
rg -n 'EitherCodec<[^>]*(SharedObject|JavaScriptValue|JavaScriptCallback)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --unified=0 fbb3459..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped and 1349 passed (1264 + 21 + 51 + 13; put the arithmetic in the report). Format is OK and `git diff --check` is clean. All three scans print nothing.

- [ ] **Step 7: Commit**

```bash
git commit -m "docs(modules-core): close union arguments" \
  -m "Merge the accepted delta into the living specs and the authoring guide, archive the brief and the change folder, and mark roadmap item 035 DONE." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
