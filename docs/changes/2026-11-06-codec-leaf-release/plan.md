# Codec Leaf Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the conversion scope and the scoped codec family with one
codec protocol (`Decode`, `Encode`, `Release`), and open callback and nested
event positions to types that hold `JavaScriptValue` or `ArrayBuffer` leaves.

**Architecture:** `IJavaScriptCodec<T>` gains `static virtual void
Release(T value) { }`. Leaf codecs dispose, composites release each child
through `CodecRelease`, and generated glue releases leaf-bearing arguments and
results in one `finally`. The generator reads `ExpoResourceOwnership.HoldsLeaves`
to decide where to emit `Release`, so leaf-free bindings keep their text.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator
(netstandard2.0), xUnit, Hermes-backed testhost.

**Spec:** `docs/changes/2026-11-06-codec-leaf-release/spec.md`. Read it
first. The design notes it follows are binding, including its "Deviations"
section.

**Execution method:** subagent-driven (already chosen).

## Global Constraints

- Branch `impl/030-compositional-codec-resource-scope`. Add commits on top. Do
  not squash, push, or create branches or worktrees.
- Every commit message ends with a blank line and
  `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`.
- No absolute local paths, usernames, or machine names in committed files.
- No finalizers, GC-driven release, ambient, thread-static, or async-local
  ownership. No reflection, `object?[]`, or JSON on the generated path.
- Leaf-free generated text stays byte for byte:
  `GeneratorPreservesModuleOutputContract`,
  `GeneratorPreservesSharedObjectOutputContract`,
  `GeneratorPreservesLeafFreeCodecPolicyModuleOutputContract`,
  `GeneratorPreservesLeafFreeCodecPolicySharedObjectOutputContract`, and
  `GeneratorPreservesNullableCodecOutputContract` keep their current hashes in
  every task. Never update those hashes.
- Release order in generated glue: result first, then arguments in declaration
  order.
- Before each commit: `scripts/format.sh --check --all` and `git diff --check`
  pass. If the format check fails, run `scripts/format.sh` and check again.
- Paths below are relative to
  `packages/expo-modules-dotnet/managed/packages/` unless they start with
  `docs/`, `scripts/`, or `.agents/`.

## Baseline

`scripts/test-managed.sh` on this branch at `797acda`, before any change:

| Test assembly | Passed | Failed | Skipped |
| --- | --- | --- | --- |
| Expo.ModulesCore.Generator.Tests | 328 | 0 | 0 |
| Expo.JSI.Tests | 220 | 0 | 0 |
| Expo.ModulesCore.Tests | 330 | 0 | 0 |
| ExampleModule.Tests | 3 | 0 | 0 |
| ExpoAssetDotnet.Tests | 43 | 0 | 0 |
| ExpoConstantsDotnet.Tests | 4 | 0 | 0 |
| ExpoFileSystemDotnet.Tests | 134 | 0 | 0 |
| **Total** | **1062** | **0** | **0** |

Targeted runs use
`scripts/test-managed.sh --project <repo-relative csproj> --filter "FullyQualifiedName~<Name>"`.
The two csproj paths are
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
(called `CORE_TESTS` below) and
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
(called `GEN_TESTS`).

## Review Focus

1. An async leaf-bearing method that throws before it returns its `Task`: the
   outer `catch` releases the arguments once and the Promise rejects. Test:
   `AsyncLeafMethodThrowingBeforeTaskReleasesOnce` (Task 3).
2. A nested event payload emitted from an async continuation, where encode
   runs later on the runtime: the author's retained leaf stays alive and
   releases once after the author disposes it. Test:
   `NestedEventPayloadEmittedLaterStaysWithTheAuthor` (Task 5).
3. `Release` through the `IJavaScriptCodec<Dictionary<string, T>>` interface,
   for example a list of dictionaries of buffers: the explicit forwarder must
   exist or every entry leaks. Test:
   `DictionaryReleasesThroughBothInterfaces` (Task 2).
4. A class record that is null inside a returned list: `Release` skips it and
   still releases the other elements. Test:
   `NullRecordInReturnedListReleasesOtherLeaves` (renamed in Task 4, must pass
   after Task 3).
5. A callback argument encode that fails at a later tuple element: the
   author's earlier `JavaScriptValue` stays usable. Test:
   `ValueTupleEncodeFailureKeepsAuthorValue` (Task 1).

---

### Task 1: Codec protocol, leaf codecs, and `CodecRelease`

**Files:**
- Modify: `Expo.ModulesCore/Codecs/IJavaScriptCodec.cs`
- Create: `Expo.ModulesCore/Codecs/CodecRelease.cs`
- Modify: `Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/ArrayBufferCodec.cs`
- Create: `Expo.ModulesCore.Tests/Codecs/CodecReleaseTests.cs`

**Interfaces:**
- Produces:
  - `IJavaScriptCodec<T>`: adds `static virtual void Release(T value) { }`.
  - `public static class CodecRelease` in `Expo.ModulesCore.Codecs`:
    - `public static void Release<T, TCodec>(T value, ref List<Exception>? failures) where TCodec : IJavaScriptCodec<T>`:
      calls `TCodec.Release(value)`; on `AggregateException` adds its inner
      exceptions, on any other exception adds it. Never throws.
    - `public static void ThrowIfFailed(List<Exception>? failures)`: throws
      `new AggregateException(failures)` when the list is non-null.
    - XML remark: "This helper is for codecs and generated bindings, not
      ordinary module author code."
  - `JavaScriptValueCodec.Encode(value, runtime)` returns `value.Retain()`.
    `public static void Release(JavaScriptValue value) => value?.Dispose();`
  - `ArrayBufferCodec : IJavaScriptCodec<ArrayBuffer>` (it still also
    implements `IGeneratedScopedCodec<ArrayBuffer>` until Task 4).
    `public static void Release(ArrayBuffer value) => value?.Dispose();`
  - Test helpers in `CodecReleaseTests` (private, reused by Task 2):
    `static void ReleaseVia<T, TCodec>(T value) where TCodec : IJavaScriptCodec<T> => TCodec.Release(value);`,
    `sealed class RecordingLeaf(Exception? failure = null)` with
    `int DisposeCount`, and
    `readonly struct RecordingLeafCodec : IJavaScriptCodec<RecordingLeaf>`
    whose `Release` calls `value?.Dispose()` and whose `Decode`/`Encode`
    throw `NotSupportedException`.

Keep the scoped members of `JavaScriptValueCodec` and `ArrayBufferCodec` in
this task. Generated code still calls them until Task 3.

- [ ] **Step 1: Write the failing tests** in `CodecReleaseTests`:

```csharp
[Fact] public void CodecReleaseAttemptsEveryValueAndAggregatesFailures()
// three RecordingLeaf, the middle one throws `failure`; call
// CodecRelease.Release<RecordingLeaf, RecordingLeafCodec> on each, then ThrowIfFailed.
// Assert.Same(failure, Assert.Single(Assert.Throws<AggregateException>(...).InnerExceptions));
// Assert.Equal(1, leaf.DisposeCount) for all three.

[Fact] public void CodecReleaseFlattensNestedAggregateFailures()
// one leaf throws new AggregateException(a, b) => ThrowIfFailed throws with InnerExceptions == [a, b].

[Fact] public void ThrowIfFailedWithNoFailuresDoesNothing()
// CodecRelease.ThrowIfFailed(null) does not throw.

[Fact] public void JavaScriptValueEncodeReturnsARetainedCopy()
// source = Evaluate("({})"). encoded = JavaScriptValueCodec.Encode(source, runtime).
// Assert.NotSame(source, encoded); encoded.Dispose(); source.Kind is still Object.
// ResetCounters; ReleaseVia<JavaScriptValue, JavaScriptValueCodec>(source) twice;
// Assert.Equal(1u, fixture.Counters.ReleasedValues); ReleaseVia(null!) does not throw.

[Fact] public void ArrayBufferReleaseFreesTheLeaseOnce()
// using var source = fixture.Evaluate("new ArrayBuffer(4)", ...); decoded = ArrayBufferCodec.Decode(source.Ref, runtime);
// ResetCounters; ReleaseVia<ArrayBuffer, ArrayBufferCodec>(decoded) twice; WaitUntilIdle;
// Assert.Equal(1u, fixture.Counters.LongLivedArrayBuffersReleased); ReleaseVia(null!) does not throw.

[Fact] public void ValueTupleEncodeFailureKeepsAuthorValue()
// value = Evaluate("({})"); ValueTupleCodec<JavaScriptValue, JavaScriptValueCodec, string, ThrowingStringCodec>
//   .Encode((value, "x"), runtime) throws InvalidOperationException;
// value.Kind is still Object (the partial-failure path disposed only the copy).
```

`ThrowingStringCodec` is a private `IJavaScriptCodec<string>` in the test
class whose `Encode` throws `InvalidOperationException`. Use
`HermesRuntimeFixture.Create()` and `fixture.Runtime.Execute` as
`ArrayBufferCodecTests` does.

- [ ] **Step 2: Run them and see them fail to compile**

Run: `scripts/test-managed.sh --project CORE_TESTS --filter "FullyQualifiedName~CodecReleaseTests"`
Expected: build errors for `CodecRelease` and `Release`.

- [ ] **Step 3: Implement the Interfaces block above.**

- [ ] **Step 4: Run the tests**

Run: same command. Expected: all `CodecReleaseTests` pass.

- [ ] **Step 5: Run the full suite**

Run: `scripts/test-managed.sh`. Expected: 1068 passed, 0 failed, 0 skipped
(baseline plus the six new tests).

- [ ] **Step 6: Commit**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Codecs/CodecReleaseTests.cs
git commit  # feat(modules-core): add Release to the codec protocol
```

---

### Task 2: Composite `Release` and decode cleanup

**Files:**
- Modify: `Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/JavaScriptDictionaryCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/NullableCodec.cs`,
  `NullableReferenceCodec.cs`, `NullableReadOnlyListCodec.cs`,
  `NullableDictionaryCodec.cs`, `NullableReadOnlyDictionaryCodec.cs`
- Test: `Expo.ModulesCore.Tests/Codecs/CodecReleaseTests.cs`

**Interfaces:**
- Consumes: `CodecRelease`, `ReleaseVia`, `RecordingLeaf`,
  `RecordingLeafCodec` (Task 1).
- Produces:
  - `JavaScriptArrayCodec<T, TCodec>.Release(IReadOnlyList<T> values)`:
    null releases nothing; otherwise each element through
    `CodecRelease.Release<T, TCodec>`, then `ThrowIfFailed`.
  - `JavaScriptDictionaryCodec<T, TCodec>.Release(IReadOnlyDictionary<string, T> values)`
    plus the explicit forwarder
    `static void IJavaScriptCodec<Dictionary<string, T>>.Release(Dictionary<string, T> value) => Release(value);`.
  - `NullableCodec<T, TCodec>.Release(T? value)`: `TCodec.Release(value.Value)`
    when `HasValue`.
  - `NullableReferenceCodec<T, TCodec>.Release(T? value)`: `TCodec.Release`
    when not null.
  - `NullableReadOnlyListCodec<T, TCodec>.Release(IReadOnlyList<T>? value)`:
    `if (value is not null) JavaScriptArrayCodec<T, TCodec>.Release(value);`.
    `NullableDictionaryCodec` and `NullableReadOnlyDictionaryCodec` do the
    same with `JavaScriptDictionaryCodec<T, TCodec>.Release`. The guard sits
    in the nullable adapter, so no call passes `T?` where `T` is expected
    (no CS8604).
  - Both `DecodeToArray` overloads and both `DecodeToDictionary` overloads:
    wrap the loop in `try`/`catch`; the `catch` releases the elements or
    values decoded so far through `CodecRelease`, calls `ThrowIfFailed`, then
    `throw;`. Signatures stay unchanged.

- [ ] **Step 1: Write the failing tests.** Each release test decodes
  JS-created buffers with the codec's own `Decode(ref)`, calls
  `fixture.ResetCounters()`, releases through `ReleaseVia<T, TCodec>` (so a
  missing override falls back to the empty default and the test fails), calls
  `fixture.WaitUntilIdle()`, and asserts `LongLivedArrayBuffersReleased`.
  Each also asserts `ReleaseVia<T, TCodec>(default!)` does not throw.

```csharp
[Fact] public void ListReleasesEveryBuffer()                  // "[new ArrayBuffer(1), new ArrayBuffer(2)]" => 2u
[Fact] public void DictionaryReleasesThroughBothInterfaces()  // "({ a: new ArrayBuffer(1), b: new ArrayBuffer(2) })"
// release once through ReleaseVia<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>> => 2u,
// and a second decoded copy through ReleaseVia<IReadOnlyDictionary<string, ArrayBuffer>, ...> => 2u.
[Fact] public void ListOfDictionariesReleasesEveryBuffer()     // JavaScriptArrayCodec<Dictionary<string, ArrayBuffer>, JavaScriptDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>, "[{ a: new ArrayBuffer(1) }, { b: new ArrayBuffer(2) }]" => 2u
[Fact] public void NullableValueReleasesItsInnerValue()       // NullableCodec<BufferSlot, BufferSlotCodec>, "({ buffer: new ArrayBuffer(1) })" => 1u
[Fact] public void NullableReferenceReleasesItsInnerValue()   // NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>, "new ArrayBuffer(1)" => 1u
[Fact] public void NullableListReleasesEveryBuffer()          // NullableReadOnlyListCodec<ArrayBuffer, ArrayBufferCodec> => 2u
[Fact] public void NullableDictionaryReleasesEveryBuffer()    // NullableDictionaryCodec<ArrayBuffer, ArrayBufferCodec> => 2u
[Fact] public void NullableReadOnlyDictionaryReleasesEveryBuffer() // NullableReadOnlyDictionaryCodec<ArrayBuffer, ArrayBufferCodec> => 2u
[Fact] public void ListReleaseAggregatesAndContinues()        // [ok, throwing, ok] RecordingLeaf list; AggregateException with the one failure; each DisposeCount == 1
[Fact] public void DictionaryReleaseAggregatesAndContinues()  // same through JavaScriptDictionaryCodec<RecordingLeaf, RecordingLeafCodec>
[Fact] public void ListDecodeFailureReleasesDecodedElements()  // "[new ArrayBuffer(4), 1]": DecodeToArray(ref) throws; WaitUntilIdle; 1u.
//   Repeat with DecodeToArray(JavaScriptValue owned) => 1u.
[Fact] public void DictionaryDecodeFailureReleasesDecodedValues() // "({ a: new ArrayBuffer(4), b: 1 })", both overloads => 1u each
[Fact] public void ListEncodeLeavesAuthorValuesUsable()       // owned [o1, o2] from Evaluate; Encode list; dispose result;
//   both sources still have Kind Object; the encoded array's elements are strictly equal to the sources (check with Evaluate on a global).
[Fact] public void ListEncodesOneAliasTwice()                 // list [b, b] of one decoded ArrayBuffer encodes an array whose two elements are === ; release => 1u
```

`BufferSlot` is a private `readonly record struct BufferSlot(ArrayBuffer Buffer)`
with a private `BufferSlotCodec : IJavaScriptCodec<BufferSlot>` that reads and
writes property `buffer` through `ArrayBufferCodec` and releases
`value.Buffer`.

- [ ] **Step 2: Run them and see them fail**

Run: `scripts/test-managed.sh --project CORE_TESTS --filter "FullyQualifiedName~CodecReleaseTests"`
Expected: the release tests fail with `0u` released, the decode-failure tests
fail with `0u`, and the dictionary-interface test fails for the
`Dictionary<string, T>` path.

- [ ] **Step 3: Implement the Interfaces block above.**

- [ ] **Step 4: Run the tests.** Same command. Expected: all pass.

- [ ] **Step 5: Run the full suite.** `scripts/test-managed.sh`. Expected:
  0 failed, 0 skipped.

- [ ] **Step 6: Commit** — `feat(modules-core): release leaves through composite codecs`

---

### Task 3: Generator emits `Release` through the one protocol

This task switches the resolver, record codecs, and every glue site at once,
because the scoped composites and the glue that calls them cannot change
separately.

**Files:**
- Modify: `Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs`
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Emission.cs`
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.EventAnalysis.cs` (one call site)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`,
  `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs`
- Test: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs` and
  `ResourceScopeModuleFixtures.cs` (two new fixture methods and tests; the
  rename is Task 4)

**Interfaces:**
- Consumes: Task 1 and Task 2 runtime members.
- Produces:
  - `enum ExpoCodecShape { JavaScriptCodec, StaticHelper }`.
  - `enum ExpoResourceOwnership { Borrowed, HoldsLeaves, ContextRetained }`.
  - `ExpoCodecDescriptor` gains `public string ValueTypeName { get; init; } = string.Empty;`
    (not positional). Rename the body of `GetCodec` to `ResolveCodec`;
    `GetCodec` returns `ResolveCodec(...) is { } codec ? codec with { ValueTypeName = GetCodecTypeArgumentName(typeSymbol) } : null`.
  - `ExpoGeneratedRecordCodecModel.ResourceOwnership` keeps its name and holds
    `HoldsLeaves` or `Borrowed`.
  - Resolver: `ArrayBuffer` is `OrdinaryCodec("ArrayBufferCodec") with { ResourceOwnership = HoldsLeaves }`.
    Lists, dictionaries, the five nullable codecs, and records use the
    ordinary codec type and copy `HoldsLeaves` from a leaf-bearing child.
    Delete `ScopedCodec`, `ScopedNullableReferenceCodec`, and every
    `GeneratedScoped*` expression. `IsScopedResource` becomes `HoldsLeaves(ExpoCodecDescriptor?)`.
  - Delete `AsJavaScriptCodecArgument`; the callback result and the
    callback-argument tuple elements use `AsCompositeChildCodec`. Once
    `ScopedOnly` is gone the two had the same body, so leaf-bearing callback
    types resolve from this task on. The typed event payload call site in
    `ExpoModulesGenerator.EventAnalysis.cs` switches to `AsCompositeChildCodec`
    too. Nested event leaves stay rejected by `GetUnsupportedEventPayload`
    until Task 5.
  - Emission: one record emitter. A `HoldsLeaves` record adds `Release` and
    decode cleanup; a leaf-free record keeps its exact current text.

**Emission rules** (the spec's "Generated glue" sketch shows the shape):

- Locals: for each `HoldsLeaves` codec parameter emit
  `{codec.ValueTypeName} __expoArgN = default!;` before the `try`, and assign
  inside it. Other parameters keep `var __expoArgN = ...;` inside the `try`.
  A `HoldsLeaves` result is `{codec.ValueTypeName} __expoResult = default!;`.
- `finally`: `global::System.Collections.Generic.List<global::System.Exception>? __expoReleaseFailures = null;`,
  then `CodecRelease.Release<{ValueTypeName}, {Expression}>(local, ref __expoReleaseFailures);`
  for the result and then each leaf-bearing argument in declaration order,
  then `CodecRelease.ThrowIfFailed(__expoReleaseFailures);`.
- Sync functions, getters (`__expoResult` only), setters (`__expoValue`
  only), shared-object members, and the shared-object constructor factory use
  this shape. The factory's `return new T(...)` sits inside the `try`.
- Span functions: the `try` wraps the span decode and `WithBytes`; the lambda
  assigns the captured `__expoResult` and returns `Codec.Encode(__expoResult, runtime)`.
  The span lease keeps `using var`.
- Async: leaf-bearing argument locals are declared before the outer `try`;
  the outer `catch` releases them and returns the rejected Promise; keep
  `using (__expoPromiseValue) { return __expoPromiseValue.AsValue(); }`
  after the `catch`. In the lambda:
  - leaf-free or void result: `try { ...existing body... } finally { release arguments }`;
  - `HoldsLeaves` result: declare `{function.AsyncResultType} __expoResult = default!;`
    (`AsyncResultType` already carries the nullable annotation; the emitter
    does not read `AsyncResultCodec.ValueTypeName`),
    then `try { __expoResult = await __expoTask.ConfigureAwait(false); return global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned(new global::System.Runtime.CompilerServices.StrongBox<S>(V), static (runtime, __expoState) => { try { return Codec.Encode(<result>, runtime); } finally { release all } }, static __expoState => { release all }); } catch { release result and arguments; throw; }`.
    `S` is `{AsyncResultType}` and `V` is `__expoResult` when no argument
    holds leaves; otherwise `S` is `({AsyncResultType}, {Arg0}, ...)` and `V` is
    `(__expoResult, __expoArg0, ...)`. `<result>` is `__expoState.Value!` in
    the scalar form (`StrongBox<T>.Value` is maybe-null, and a bare `.Value`
    warns CS8604) or `__expoState.Value.Item1` in the tuple form; arguments
    are `.Item2` onward.
- Record codec (`HoldsLeaves` only), class records start `Release` with
  `if (value is null) return;`:
  `public static void Release({Record} value)` releases each `HoldsLeaves`
  field in declaration order through `CodecRelease`, then `ThrowIfFailed`.
  Both `Decode` overloads declare leaf-bearing field locals as
  `{field.Codec.ValueTypeName} x = default!;` before a `try`, and the `catch`
  releases them through `CodecRelease`, calls `ThrowIfFailed`, and rethrows.
  `Encode` keeps the leaf-free text: `using var` on each field's `Encode`.
- Emit nothing new for a member, record, or field that is not `HoldsLeaves`.

- [ ] **Step 1: Update the generator tests to the new shape (they fail first).**
  Rename and rewrite these tests in `ExpoModulesGeneratorTests.cs`:

| Old name | New name | New assertions |
| --- | --- | --- |
| `GeneratorEmitsOneConversionScopePerDirectLeafMember` | `GeneratorReleasesDirectLeafMembersInFinally` | per row: no `__expoScope`; the expected line, e.g. `__expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime);`, `global::Expo.JSI.JavaScriptValue __expoArg0 = default!;`, `CodecRelease.Release<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>(__expoResult, ref __expoReleaseFailures);`, `__expoValue = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime);`; the existing count column counts `CodecRelease.ThrowIfFailed(` |
| `GeneratorEmitsConversionScopeInSharedObjectFactoryAndMembers` | `GeneratorReleasesLeavesInSharedObjectFactoryAndMembers` | factory and member each contain one `CodecRelease.ThrowIfFailed(`; factory contains `return new global::Expo.TestModules.` inside the `try` |
| `GeneratorEmitsAsyncConversionScopeShapes` | `GeneratorEmitsAsyncLeafReleaseShapes` | switch to `AssertNoCompilerDiagnostics`, which catches CS8604; `Task M(JavaScriptValue)` and `Task<int> M(ArrayBuffer)`: `JavaScriptPromiseResult.Resolve(` and no `StrongBox`; `Task<JavaScriptValue> M()`: `new global::System.Runtime.CompilerServices.StrongBox<global::Expo.JSI.JavaScriptValue>(__expoResult)` and `JavaScriptValueCodec.Encode(__expoState.Value!, runtime)`; `Task<ArrayBuffer> M(JavaScriptValue)`: `StrongBox<(global::Expo.ModulesCore.ArrayBuffer, global::Expo.JSI.JavaScriptValue)>((__expoResult, __expoArg0))` and `ArrayBufferCodec.Encode(__expoState.Value.Item1, runtime)`; every row: no `GeneratedTransferredResult` |
| `GeneratorEmitsNoConversionScopeForLeafFreeMembers` | `GeneratorEmitsNoReleaseForLeafFreeMembers` | no `CodecRelease`, no `default!` |
| `GeneratorCompilesScopedCompositesAtBoundaryPositions` | `GeneratorCompilesLeafBearingCompositesAtBoundaryPositions` | same rows; each expected snippet names the ordinary codec, e.g. `JavaScriptArrayCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.DecodeToArray(arguments.GetValue(0), runtime)`, `NullableReadOnlyListCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>`, `JavaScriptDictionaryCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.DecodeToDictionary(`, `NullableCodec<global::Expo.TestModules.Slot, `, `NullableReferenceCodec<global::Expo.TestModules.Holder, HolderCodec_`; the `Holder` row expects `IJavaScriptCodec<global::Expo.TestModules.Holder>`; the `Shelf` row expects `CodecRelease.Release<global::System.Collections.Generic.IReadOnlyList<global::System.Collections.Generic.Dictionary<string, global::Expo.ModulesCore.ArrayBuffer>>, ` |
| `GeneratorCompilesScopedCompositesOnSharedObjectMembers` | `GeneratorCompilesLeafBearingCompositesOnSharedObjectMembers` | compiles, no diagnostics, contains `CodecRelease.ThrowIfFailed(` |
| `GeneratorRecordCodecUsesScopeOnlyForLeafFields` | `GeneratorRecordCodecReleasesOnlyLeafFields` | the record's `Release` contains a `CodecRelease.Release<` call for the leaf field and none for the `int` field |
| `GeneratorRecordNullGuardsOnlyForClassRecords` | `GeneratorRecordReleaseNullGuardsOnlyForClassRecords` | the class record's `Release` starts with `if (value is null) return;`; the record struct's does not; delete the `ArgumentNullException.ThrowIfNull(value)` assertion, because `Encode` keeps the leaf-free text with no null guard |
| `GeneratorRecordCacheHitKeepsScopedResource` | `GeneratorRecordCacheHitKeepsHoldsLeaves` | the second use of the record still emits `CodecRelease` in glue |

  Rewrite these three tests outside the table:

  - `GeneratorEmitsBinaryCodecOwnershipAndSingleSpanCallbacks`: replace the
    scope assertions with `Contains` for
    `__expoArg0 = ArrayBufferCodec.Decode(arguments.GetValue(0), runtime);`,
    `__expoResult = module.Echo(__expoArg0);`,
    `__expoResult = module.Transform(__expoArg0);`,
    `return ArrayBufferCodec.Encode(__expoResult, runtime);`, and
    `CodecRelease.Release<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>(__expoResult, ref __expoReleaseFailures);`.
    Keep the four `DoesNotContain` lines.
  - `GeneratorUsesClaimOrAbandonForAsyncArrayBufferResults`: use
    `AssertNoCompilerDiagnostics`; assert `Contains`
    `global::Expo.JSI.JavaScriptPromiseResult.ResolveOwned(` and
    `new global::System.Runtime.CompilerServices.StrongBox<(global::Expo.ModulesCore.ArrayBuffer, global::Expo.ModulesCore.ArrayBuffer)>((__expoResult, __expoArg0))`,
    and `DoesNotContain` `GeneratedTransferredResult`. Drop the
    `DoesNotContain("JavaScriptPromiseResult.ResolveOwned(")` assertion.
  - `GeneratorSupportsJavaScriptValueArgumentsAndReturns`: assert `Contains`
    `__expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime);`,
    `__expoResult = module.Echo(__expoArg0);`,
    `return JavaScriptValueCodec.Encode(__expoResult, runtime);`,
    `__expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), jsRuntime);`,
    `var __expoTask = module.EchoAsync(__expoArg0);`, and
    `StrongBox<(global::Expo.JSI.JavaScriptValue, global::Expo.JSI.JavaScriptValue)>((__expoResult, __expoArg0))`.
    Keep the `DoesNotContain` lines.

  Flip the callback-position tests here, because the resolver change opens
  those positions in this task:

  - `GeneratorRejectsStaticHelperCodecsInGenericPositions`: delete the
    `JavaScriptCallback<ValueTuple<ArrayBuffer>, int>` and
    `JavaScriptCallback<ArrayBuffer>` rows.
  - Replace `GeneratorRejectsLeafBearingCompositeCallbackTypeArguments` with
    `GeneratorAcceptsLeafBearingCallbackTypeArguments(string declarations, string member, bool onSharedObject)`:
    the same five rows without the diagnostic column (the fifth keeps
    `onSharedObject: true` and uses `SharedObjectMemberSource(member)`), plus
    `("", "[JS] public int M(JavaScriptCallback<ArrayBuffer> callback) => 0;", false)`
    and `("", "[JS] public int M(JavaScriptCallback<ValueTuple<ArrayBuffer>, int> callback) => 0;", false)`.
    Assert `AssertNoCompilerDiagnostics` and an empty `GeneratorDiagnosticIds`.

  Every test in the project that asserts the old shape must assert the new
  one. Check with
  `rg -n 'expoScope|GeneratedScoped|IGeneratedScopedCodec|EncodeBorrowed|GeneratedTransferredResult|\.Transfer\(|DoesNotContain\("JavaScriptPromiseResult\.ResolveOwned|AsJavaScriptCodecArgument|ScopedOnly|ScopedResource' Expo.ModulesCore.Generator.Tests Expo.ModulesCore.Generator`,
  which must print nothing at the end of this task.

- [ ] **Step 2: Add two async Hermes tests.** In
  `ResourceScopeModuleFixtures.cs` add

```csharp
[JS] public Task<int> ThrowBeforeTaskAsync(ArrayBuffer value) => throw new InvalidOperationException("before task");
[JS] public async Task<BufferPair> BrokenPairAsync(ArrayBuffer value)
{
  await Task.Yield();
  return new BufferPair(value.Retain(), null!);
}
```

  In `ResourceScopeModuleTests.cs` add, each with `ResetCounters` before the
  call and `StartOutcome`, `WaitForOutcomeAsync`, `WaitUntilIdle` after it:
  - `AsyncLeafMethodThrowingBeforeTaskReleasesOnce`:
    `m.throwBeforeTaskAsync(b)`; assert `"rejected:Error:true"` from
    `` `${__outcome.status}:${__outcome.name}:${__outcome.message.includes('before task')}` ``
    and `1u` `LongLivedArrayBuffersReleased`.
  - `AsyncResultEncodeFailureRejectsAndReleasesOnce`: `m.brokenPairAsync(b)`;
    the factory's `Encode` throws on the null field. Assert
    `"rejected:Error"` from `` `${__outcome.status}:${__outcome.name}` `` and,
    after `WaitForArrayBufferReleases(1u)`, `1u`
    `LongLivedArrayBuffersReleased` (the decoded lease and the `Retain()`
    copy share one entry).

- [ ] **Step 3: Run the generator tests and see them fail**

Run: `scripts/test-managed.sh --project GEN_TESTS`
Expected: the rewritten tests fail on the old scoped text.

- [ ] **Step 4: Implement the descriptor, resolver, and emission rules above.**

- [ ] **Step 5: Update the two pinned leaf contract hashes.** Run
  `GeneratorPinsLeafCodecPolicyModuleOutputContract` and
  `GeneratorPinsLeafCodecPolicySharedObjectOutputContract`, read the new
  hashes from the failure, and paste them. Read the generated source once
  before pasting and confirm it matches the emission rules. Do not touch any
  `GeneratorPreserves*` hash; if one changes, the emission is wrong.

- [ ] **Step 6: Run the full suite.** `scripts/test-managed.sh`. Expected:
  0 failed, 0 skipped. Every existing `ResourceScopeModuleTests` test passes
  without changing its assertions; they are the exactly-once proof for the new
  glue.

- [ ] **Step 7: Run the codec-text check** from "Generator Conversion Policy
  Is Typed" in `docs/specs/modules-core-boundary.md`. Expected: no matches.

- [ ] **Step 8: Commit** — `fix(generator): release leaves through the one codec protocol`

---

### Task 4: Delete the scoped family and rename the leaf tests

**Files:**
- Delete: `Expo.ModulesCore/Codecs/GeneratedConversionScope.cs`,
  `IGeneratedScopedCodec.cs`, `GeneratedTransferredResult.cs`,
  `GeneratedScopedListCodec.cs`, `GeneratedScopedDictionaryCodec.cs`,
  `GeneratedScopedNullableCodec.cs`, `GeneratedScopedNullableReferenceCodec.cs`
- Delete: `Expo.ModulesCore.Tests/Codecs/ScopedCodecTests.cs`,
  `Expo.ModulesCore.Tests/Codecs/GeneratedConversionScopeTests.cs`
- Modify: `Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`,
  `ArrayBufferCodec.cs` (remove the scoped `Decode`, `Transfer`,
  `EncodeBorrowed`, and the `IGeneratedScopedCodec<T>` interface)
- Rename (`git mv`): `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs`
  → `LeafReleaseModuleTests.cs`, `ResourceScopeModuleFixtures.cs` →
  `LeafReleaseModuleFixtures.cs`
- Modify: `Expo.ModulesCore.Tests/Modules/ModuleAttributeTests.cs` if it
  names the renamed module

**Interfaces:**
- Produces: module `LeafReleaseModule` registered as `"LeafRelease"`,
  shared object `LeafReleaseEntry` (`[ExpoSharedObject("LeafReleaseEntry")]`),
  test class `LeafReleaseModuleTests`, private helper `LeafReleaseFixture`
  (was `ResourceScopeFixture`), JS global `m` unchanged. Task 5 adds to these.

The deleted test files cover only the removed types, or scenarios that Task 2
now covers for the ordinary composites (alias encode, element temporaries,
both dictionary interfaces, nullable null, nullable struct). Do not port
anything else.

- [ ] **Step 1: Delete and rename** as listed. In the renamed files, rename
  these tests and update comments that mention the scope, `Transfer`, or
  registration so they describe `Release`:
  `RecordReturnTransfersEveryLeaf` → `RecordReturnReleasesEveryLeafOnce`,
  `EncodeFailureReleasesTransferredLeaves` → `EncodeFailureReleasesReturnedLeaves`,
  `NullRecordInReturnedListKeepsTransferTotal` → `NullRecordInReturnedListReleasesOtherLeaves`,
  `OmittedOptionalBufferListRegistersNothing` → `OmittedOptionalBufferListReleasesNothing`.
  Assertions stay as they are.

- [ ] **Step 2: Run the full suite.** `scripts/test-managed.sh`. Expected:
  0 failed, 0 skipped. The `Expo.ModulesCore.Tests` total drops by exactly
  the number of test cases the two deleted files ran; read that number from
  a filtered run of the two classes before deleting them.

- [ ] **Step 3: Check nothing references the removed design**

Run: `rg -n 'GeneratedConversionScope|IGeneratedScopedCodec|GeneratedTransferredResult|GeneratedScoped|EncodeBorrowed|ScopedOnly|ScopedResource|ResourceScope' packages`
Expected: no matches.

- [ ] **Step 4: Commit** — `refactor(modules-core): remove the scoped codec family`

---

### Task 5: Leaf-bearing event payloads and callback ownership tests

Task 3 already opened the callback positions. This task opens nested event
payloads and adds the Hermes proof for both.

**Files:**
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.EventAnalysis.cs`
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Test: `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleFixtures.cs`,
  `LeafReleaseModuleTests.cs`

**Interfaces:**
- Consumes: Task 4 names (`LeafReleaseModule`, `LeafReleaseEntry`,
  `LeafReleaseFixture`), `BufferPair` and `ValuePair` records already in the
  fixtures file.
- Produces: in `GetUnsupportedEventPayload`, a
  `JavaScriptValue` or `ArrayBuffer` is rejected only when it carries a
  nullable annotation, at any depth, with the existing reason text
  "it applies a nullable annotation to a transfer-sensitive wrapper". The
  "nested transfer-sensitive wrapper" branch goes away.

- [ ] **Step 1: Flip the event generator tests.**
  - `TypedEventPayloadFailuresDoNotGenerateCallbackCodecs`: delete the
    `IReadOnlyList<JavaScriptValue>` row.
  - Replace `GeneratorRejectsNestedBufferEventPayloads` with
    `GeneratorAcceptsNestedLeafEventPayloads(bool onSharedObject)`: no
    diagnostics, compiles, and the source contains
    `EmitAsync<JavaScriptArrayCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>, global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.ArrayBuffer>>`.
  - Add `GeneratorRejectsNestedNullableLeafEventPayloads(bool onSharedObject, string expectedId)`
    with `Func<IReadOnlyList<ArrayBuffer?>, Task>`: exactly one `EXPOJSI019`
    (module) or `EXPOJSI027` (shared object) whose message contains
    "nullable annotation".

- [ ] **Step 2: Add the Hermes fixtures** to `LeafReleaseModule`:

```csharp
[JS] public int ForwardBuffer(ArrayBuffer buffer, JavaScriptCallback<ValueTuple<BufferPair>, int> callback)
    => callback.Invoke(ValueTuple.Create(new BufferPair(buffer, buffer))) + buffer.ByteLength;
[JS] public string ForwardValue(JavaScriptValue value, JavaScriptCallback<ValueTuple<ValuePair>, bool> callback)
    => $"{(callback.Invoke(ValueTuple.Create(new ValuePair(value, value))) ? "true" : "false")}:{value.Kind}";
[JS] public int TakeBuffers(JavaScriptCallback<IReadOnlyList<ArrayBuffer>> callback)
// invokes, sums ByteLength, disposes every returned buffer in finally (rule 4)
[Event] public partial Func<BufferPair, Task> OnPair { get; }
[JS] public int EmitPair(ArrayBuffer buffer)
// OnPair(new BufferPair(buffer, buffer)).GetAwaiter().GetResult(); returns buffer.ByteLength
[JS] public async Task EmitPairLater(ArrayBuffer buffer)
// using var copy = buffer.Retain(); await Task.Yield(); await OnPair(new BufferPair(copy, copy));
```

  and to `LeafReleaseEntry`:
  `[Event] public partial Func<IReadOnlyList<ArrayBuffer>, Task> OnBuffers { get; }`
  and `[JS] public int EmitBuffers(ArrayBuffer buffer)` that awaits
  `OnBuffers(new[] { buffer })` synchronously and returns `buffer.ByteLength`.

- [ ] **Step 3: Write the Hermes tests** in `LeafReleaseModuleTests`, each
  with `ResetCounters` before the call and `WaitUntilIdle` before asserting:

```csharp
[Fact] public void CallbackArgumentStaysWithTheAuthor()
// "String(m.forwardBuffer(b, p => p.first === b && p.second === b ? p.first.byteLength : -1))" == "8"; 1u buffer released
[Fact] public void CallbackValueArgumentStaysUsable()
// "m.forwardValue(o, p => p.first === o && p.second === o)" == "true:Object"
[Fact] public void CallbackResultBelongsToTheAuthor()
// "String(m.takeBuffers(() => [b1, b2]))" == "6"; 2u released
[Fact] public void NestedEventPayloadStaysWithTheAuthor()
// "let seen; m.addListener('onPair', p => { seen = p.first === b && p.second === b; }); `${m.emitPair(b)}:${seen}`" == "4:true"; 1u released
[Fact] public async Task NestedEventPayloadEmittedLaterStaysWithTheAuthor()
// listener as above; StartOutcome("m.emitPairLater(b)"); WaitForOutcomeAsync; status "fulfilled" and seen true; WaitForArrayBufferReleases(1u); 1u released
[Fact] public void SharedObjectNestedEventPayloadStaysWithTheAuthor()
// "const e = new m.LeafReleaseEntry(b, false); let seen; e.addListener('onBuffers', l => { seen = l.length === 1 && l[0] === b; }); `${e.emitBuffers(b)}:${seen}`" == "4:true"; 2u released (constructor lease and member lease)
```

  The released counts come from JS-created buffers: each decoded lease and
  every `Retain()` copy share one native entry, which releases once when the
  last lease drops.

- [ ] **Step 4: Run and see them fail.**
  `scripts/test-managed.sh --project GEN_TESTS` fails on the flipped event
  rows; `CORE_TESTS` fails to build with `EXPOJSI019` and `EXPOJSI027` on the
  new event fixtures. The callback fixtures already compile after Task 3;
  their tests prove the ownership rules once the project builds.

- [ ] **Step 5: Implement the Interfaces block above.**

- [ ] **Step 6: Run the full suite.** `scripts/test-managed.sh`. Expected:
  0 failed, 0 skipped.

- [ ] **Step 7: Commit** — `feat(generator): accept leaves in callback and event payload types`

---

### Task 6: Merge the delta into the living docs and close 030

**Files:**
- Modify: `docs/specs/ownership-and-scoped-refs.md`,
  `docs/specs/modules-core-boundary.md`
- Modify: `docs/module-authoring-guide.md`
- Modify: `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`
- Modify: `docs/roadmap.md`
- Modify: `docs/plans/031-nullable-arraybuffer-codecs.md`,
  `docs/plans/032-nullable-javascript-callback-parameters.md`,
  `docs/plans/033-nullable-concrete-shared-object-codecs.md`
- Move out of git: `docs/changes/2026-11-06-codec-leaf-release/spec.md` and
  `plan.md` → `docs/archive/changes/2026-11-06-codec-leaf-release/`

- [ ] **Step 1: Apply every REMOVED, MODIFIED, and ADDED heading** in the
  spec's two "Delta against" sections to the matching living spec. Put the
  two ADDED requirements where "Generated Bindings Compose Leaves Through One
  Scope" was. Keep the ownership text as short as the spec has it.

- [ ] **Step 2: Update the authoring guide.** Section 3: one block with the
  four rules for authors, copied from the spec, and drop the leaf
  restrictions on lists, dictionaries, `ArrayBuffer`, and callback types.
  Section 5 ("Event payload ownership"): nested leaves are allowed and stay
  the author's until the task completes. Section 7: rule 3 for arguments and
  rule 4 for results, with the note that a leaf passed to `InvokeAsync` must
  stay alive until its task completes.

- [ ] **Step 3: Update the skill.** Replace the "Generated Module Ownership"
  bullets with the three-line codec contract and the four rules.

- [ ] **Step 4: Update the roadmap.** Row 030: title "Composable codec leaf
  release", status `DONE`, brief `—`. In "Notes on open items", replace the
  030 sentences with one or two lines: the conversion scope was replaced by
  `Release` on the one codec protocol, leaves now compose in every position,
  and the landing run's `scripts/test-managed.sh` counts. Change "the
  conversion scope (030)" in the 028–030 note to "codec leaf release (030)".
  Remove the backlog item "P2 — Callback argument ownership" (fixed here) and
  add "P2 — Callback lifetime: the runtime context keeps one retained entry
  per decoded callback until teardown." and "P2 — Async start order: the
  authored async method starts before `CreatePromise` returns, so a failure
  in between releases arguments that the running Task still uses (from 030)."

  Leave the event diagnostic reason text "transfer-sensitive wrapper" as it
  is; the spec does not change diagnostic text.

- [ ] **Step 5: Update briefs 031–033.** Replace mentions of
  `GeneratedConversionScope`, registration, `Transfer`, and `EncodeBorrowed`
  with the `Release` protocol wording. Do not change their designs.

- [ ] **Step 6: Archive the change folder.**

`docs/archive/` is git-ignored, so the move keeps a local copy and removes
the files from git:

```bash
mkdir -p docs/archive/changes/2026-11-06-codec-leaf-release
mv docs/changes/2026-11-06-codec-leaf-release/spec.md docs/changes/2026-11-06-codec-leaf-release/plan.md docs/archive/changes/2026-11-06-codec-leaf-release/
git rm --cached -q docs/changes/2026-11-06-codec-leaf-release/spec.md docs/changes/2026-11-06-codec-leaf-release/plan.md
```

Expected: `git status --short` shows the two deletions and no archive
files.

- [ ] **Step 7: Verify.**
  - `scripts/test-managed.sh`: 0 failed, 0 skipped; record the counts in the
    roadmap note from Step 4.
  - `scripts/format.sh --check --all` and `git diff --check` pass.
  - `rg -n 'GeneratedConversionScope|IGeneratedScopedCodec|GeneratedTransferredResult|GeneratedScoped|EncodeBorrowed|ScopedOnly|ScopedResource|conversion scope' packages docs/specs docs/module-authoring-guide.md docs/roadmap.md docs/plans .agents`
    prints nothing.
  - `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages`
    prints nothing new compared with `797acda`.
  - `git diff 797acda..HEAD | grep -n '/[U]sers/'` prints nothing.

- [ ] **Step 8: Commit** — `docs(modules-core): close codec leaf release`
