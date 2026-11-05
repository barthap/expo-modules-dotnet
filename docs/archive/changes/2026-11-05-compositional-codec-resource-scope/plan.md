# Compositional Codec Resource Scope Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generated bindings release every `JavaScriptValue` and `ArrayBuffer` leaf exactly once, at any depth inside records, lists, dictionaries, and nullable wrappers, through one `GeneratedConversionScope` per invocation (roadmap item 030).

**Architecture:** Lock today's generated output first, split into a leaf-free digest that never changes and a leaf digest that later tasks re-pin. Then add the runtime pieces in `Expo.ModulesCore/Codecs` (the scope, then the scoped protocol, leaf codecs, composite codecs, and the async transferred result). Then switch the generator in two leaf-shape slices: direct leaves first, composite leaves second. Each slice carries its own generator tests and Hermes integration tests. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-05-compositional-codec-resource-scope/spec.md` (binding authority; reconciled at `5824b07`, and code under `packages/` is unchanged since then).

**Prerequisite:** the spec carries review edits S1 (async ownership handoff at `CreatePromise` return), S2 (narrowed null rule, class-record null guards), and S3 (corrected counter-test citation). The controller commits them as one `docs(modules-core)` edit to `spec.md` before Task 1, so the binding spec and the code agree at every commit.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Each task has a model hint: **cheap** (mechanical, low judgment), **standard** (normal implementation), **most capable** (cross-cutting ownership and emission logic).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/`, `scripts/`, or `.agents/`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one mechanism, one execution order per path, no special cases with hidden data flow. Every new type pays for itself.
  2. Performance: no byte copies. No allocation and no new code on bindings that carry no wrapper leaf. No retain cycles.
  3. Correctness: no leaks. Ownership is explicit at every boundary row. Object lifecycle is boring: create, register, release once.
- New public types are exactly the spec's seven: `GeneratedConversionScope`, `IGeneratedScopedCodec<T>`, `GeneratedTransferredResult<T, TCodec>`, `GeneratedScopedListCodec<T, TCodec>`, `GeneratedScopedDictionaryCodec<T, TCodec>`, `GeneratedScopedNullableCodec<T, TCodec>`, `GeneratedScopedNullableReferenceCodec<T, TCodec>`. All live in `Expo.ModulesCore/Codecs/`, namespace `Expo.ModulesCore.Codecs`. Do not add others.
- `IJavaScriptCodec<T>` does not change. `JavaScriptValueCodec`'s ordinary members do not change. `ArrayBufferCodec`'s existing static members do not change.
- No new diagnostic descriptors and no new message text. Existing IDs only (`EXPOJSI001`, `002`, `007`, `008`, `015`, `019`, `023`, `027`).
- Bindings with no leaf keep byte-identical generated text and allocate no scope. The Task 1 leaf-free digests, and `GeneratorPreservesModuleOutputContract`, `GeneratorPreservesSharedObjectOutputContract`, `GeneratorPreservesNullableCodecOutputContract`, `GeneratorPreservesDiagnosticOutputContract`, and `GeneratorPreservesCodecPolicyDiagnosticContract`, are never edited after Task 1.
- No ambient, thread-static, async-local, reflection, or finalizer ownership. No registration because a value implements `IDisposable`. These scans (verbatim from the spec) print nothing:
  ```sh
  rg -n 'ThreadStatic|AsyncLocal|ThreadLocal|CurrentConversionScope|static\s+(readonly\s+)?GeneratedConversionScope' \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator -g '*.cs'
  rg -n ' is IDisposable|as IDisposable|GetInterfaces|typeof\(IDisposable\)' \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs \
    packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator -g '*.cs'
  ```
- 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Tests never rely on GC or finalizers for an exact-once assertion. Counter assertions use deltas after `ResetCounters()` on the Hermes fixture, calibrated against a leaf-free baseline where `ReleasedValues` is involved (it also counts traversal temporaries). Exact-once `ArrayBuffer` assertions use JS-created buffers (`new ArrayBuffer(n)`) and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`; native-backed leases (`ArrayBuffer.Allocate`) have no counter.
- A counter delta or digest that differs from this plan's expectation is a finding to report, never a number to adjust until green. Several expectations are inferred from reading code (marked "inferred" or derived from calibration).
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Match the surrounding style: 2-space indent, file-scoped namespaces, `readonly struct` codecs, existing naming.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Use repo-relative paths. The privacy scan is the spec's command:
  `git diff --unified=0 5824b07..HEAD -- docs packages .agents | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
  Never write a literal username into a scan command, a doc, or a commit message.
- Commits: conventional subjects (`test(...)`, `feat(...)`, `fix(...)`, `docs(...)`). No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (run `scripts/format.sh`, then check again, if it fails) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose | Command |
| --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` |
| One test | append `--filter "FullyQualifiedName~<Name>"` to either command |
| Full managed suite | `scripts/test-managed.sh` (966 passed, 0 skipped at 029 close per `docs/roadmap.md`; record a fresh baseline before Task 1) |

## Review Focus

1. **A method returns `value.Retain()` of an input leaf.** That is a distinct wrapper over the same JS value. Identity dedup must not merge it with the input: both wrappers release once. Test: Task 4 `ReturnedRetainedCopyOfInputReleasesBothLeases`.
2. **JavaScript passes the same value as two parameters.** Two decodes create two wrappers. Both register and both release once. Test: Task 4 `SameBufferInTwoParametersReleasesBothLeases`.
3. **Nullable-oblivious author code returns a non-nullable leaf list with a `null` element.** `Transfer` skips the null and registers the other leaves, `EncodeBorrowed` throws `ArgumentNullException`, and the scope still releases every registered leaf once. Test: Task 3 `ScopedListNullElementFailsEncodeAndKeepsOtherLeavesOwned`.
4. **JavaScript omits an optional nullable leaf-bearing parameter** (`IReadOnlyList<ArrayBuffer>? buffers = null`). The default path registers nothing and the call succeeds. Test: Task 5 `OmittedOptionalBufferListRegistersNothing` plus the generator compile row.
5. **An async leaf-bearing member fails `RequireArgumentCount`** after the scope is allocated and before any decode. The Promise rejects through the catchable path and the outer `catch` disposes an empty scope. Test: Task 4 `AsyncLeafMemberArgumentCountFailureRejects`.

---

### Task 1: Lock today's generated output

Model: **cheap**.

**Files:**
- Modify: `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs`

**Interfaces:**
- Produces: four digest tests. The two leaf-free ones are never edited again. The two leaf ones are re-pinned only in Tasks 4 and 5, after review of the generated text.

This task changes tests only. No production file changes.

- [ ] **Step 1: Record the baseline**

Run: `scripts/test-managed.sh`
Expected: all pass, 0 skipped. Note the count in the task report.

- [ ] **Step 2: Split the two codec-policy fixtures**

Delete `GeneratorPreservesCodecPolicyModuleOutputContract` and `GeneratorPreservesCodecPolicySharedObjectOutputContract`. Add four `[Fact]`s in the same style (one `GeneratorTestHost.Run`, the `CS` error `DoesNotContain`, `Assert.Equal(new[] { ... }, result.GetOutputContract())`, `Assert.Empty(result.GetDiagnosticContract())`). Every fixture must compile today with no generator diagnostics. Do not add any shape that does not compile today.

- `GeneratorPreservesLeafFreeCodecPolicyModuleOutputContract`, module `PolicyLeafFree`: every member of today's module fixture with no leaf: `Int`, `Text`, `Mode`, `Id`, `Bytes`, `Mem`, `List`, `Dict`, `ReadOnlyDict`, `Nullable`, `Callback`, `TupleCallback`, `Optional`, `ListResultAsync`, `ListReturn`, `SpanReturn`, `SpanToString`, `ListProperty`, `TextProperty`, and the three events `OnRecord`, `OnValue`, `OnBuffer`. The direct `JavaScriptValue`/`ArrayBuffer` events stay here because the spec's ownership matrix keeps typed-event payloads unchanged; a change there must break this frozen digest; put a one-line comment in the test saying so. Add a leaf-free record `PolicyPayloadRecord(string Label, int Count)` used as `int Record(PolicyPayloadRecord value)` and `PolicyPayloadRecord RecordReturn(PolicyPayloadRecord value)`.
- `GeneratorPinsLeafCodecPolicyModuleOutputContract`, module `PolicyLeaf`: `PolicyRecord(ArrayBuffer Buffer, JavaScriptValue Value)` with `Record`/`RecordReturn`, `Value`, `Buffer`, `ValueTask`, `BufferTask`, `ValueResultAsync`, `BufferResultAsync`, `ValueReturn`, `BufferReturn`, `SpanToBuffer`, `SpanToValue`, `BufferProperty`, `ValueProperty`. Add members that compile today, carry a leaf, and have no test: `int Values(IReadOnlyList<JavaScriptValue> value) => 0;`, `int ValueMap(Dictionary<string, JavaScriptValue> value) => 0;`, `IReadOnlyList<JavaScriptValue> ValuesReturn() => Array.Empty<JavaScriptValue>();`.
- `GeneratorPreservesLeafFreeCodecPolicySharedObjectOutputContract`: today's shared-object fixture with the `JavaScriptValue value` constructor parameter removed (`PolicyEntry(string label, JavaScriptCallback<int> callback, PolicyOther other)`), and `MakeEntry` drops the matching argument (`new("", default, new PolicyOther())`); everything else is the same.
- `GeneratorPinsLeafCodecPolicySharedObjectOutputContract`: `[ExpoSharedObject("PolicyLeafEntry")]` with `[JS] PolicyLeafEntry(string label, JavaScriptValue value)`, `[JS] int Measure(ArrayBuffer value) => 0;`, `[JS] JavaScriptValue Echo(JavaScriptValue value) => value;`, `[JS] JavaScriptValue Tag { get; set; }`, and an owning module with `[JS] PolicyLeafEntry Make() => new("", null!);`.

The three `JavaScriptValue` collection members are accepted today by code reading only; no test pins them. If any of them produces a generator diagnostic or a compiler error, drop it from the fixture and say so in the report.

Capture each expected digest list by running once with an empty expectation and copying the actual values from the failure message.

- [ ] **Step 3: Run the digest tests**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorPreserves|FullyQualifiedName~GeneratorPins"`
Expected: PASS, including the five existing `GeneratorPreserves*` tests.

- [ ] **Step 4: Commit**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs
git commit -m "test(generator): split codec policy output contracts by wrapper leaves"
```

The commit body says: leaf-free digests stay identical through 030; leaf digests are re-pinned after review of the generated text.

### Task 2: Add the conversion scope

Model: **standard**.

**Files:**
- Create: `Expo.ModulesCore/Codecs/GeneratedConversionScope.cs`
- Test: `Expo.ModulesCore.Tests/Codecs/GeneratedConversionScopeTests.cs`

**Interfaces:**
- Produces:

```csharp
/// <remarks>This helper is for generated bindings, not ordinary module author code.</remarks>
public sealed class GeneratedConversionScope : IDisposable
{
  public GeneratedConversionScope();
  internal void Register(IDisposable leaf);
  public void Dispose();
}
```

`Expo.ModulesCore.csproj` already declares `InternalsVisibleTo("Expo.ModulesCore.Tests")`; do not add another.

- [ ] **Step 1: Write the failing tests**

Use a private test type `RecordingLeaf : IDisposable` with `int DisposeCount`, an optional exception to throw from `Dispose`, a shared `List<RecordingLeaf>` that records disposal order, and an `Equals` override that returns `true` for any `RecordingLeaf` (for the equal-but-distinct row). All `[Fact]`s, no Hermes fixture:

- `EmptyScopeDisposesWithoutFailure`: `new GeneratedConversionScope().Dispose()` does not throw.
- `OneLeafReleasesOnce`: register one leaf, dispose; `DisposeCount == 1`.
- `AliasedLeafReleasesOnceAcrossTwoDisposals`: register the same leaf twice, dispose twice; `DisposeCount == 1`.
- `EqualButDistinctLeavesBothRelease`: two leaves where `a.Equals(b)`; both `DisposeCount == 1`.
- `LeavesReleaseInRegistrationOrder`: register three; order list is `[first, second, third]`.
- `RegistrationAfterDisposalThrows`: dispose, then `Assert.Throws<ObjectDisposedException>(() => scope.Register(leaf))`; `leaf.DisposeCount == 0` (the scope does not take ownership of a leaf it refuses).
- `ThrowingLeafIsAggregatedAndOthersStillRelease`: three leaves, the middle throws `InvalidOperationException("leaf failed")`; `Dispose` throws `AggregateException` whose single inner exception is that one; the other two have `DisposeCount == 1`; a second `Dispose` does not throw and releases nothing again.
- `NestedAggregateFailureIsFlattened`: one leaf throws `new AggregateException(new InvalidOperationException("a"), new InvalidOperationException("b"))`; the thrown `AggregateException` has two inner exceptions `a`, `b`.

- [ ] **Step 2: Run them to verify they fail**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter "FullyQualifiedName~GeneratedConversionScopeTests"`
Expected: build FAIL with `CS0246: The type or namespace name 'GeneratedConversionScope' could not be found`.

- [ ] **Step 3: Implement `GeneratedConversionScope`**

Fields: `IDisposable? first`, `List<IDisposable>? rest`, `bool disposed`. `Register`: `ObjectDisposedException.ThrowIf(disposed, this)`; return if a linear `ReferenceEquals` scan over `first` and `rest` finds it (not `List.Contains`, which calls `Equals`); store it in `first`, else in `rest` (allocated on the second distinct leaf). `Dispose`: return if already disposed; mark disposed; dispose `first`, then `rest` in order; capture failures as `DotnetRuntimeContext.DisposeAndCapture` does (`DotnetRuntimeContext.cs:336-350`: flatten an `AggregateException`'s inner exceptions); clear `first` and `rest`; throw `new AggregateException(failures)` if any. No locking, no `Interlocked`: the spec makes the scope single-owner, and generated code hands it from the host frame to the Promise lambda at `CreatePromise` return, so no two threads use it at once.

- [ ] **Step 4: Run them to verify they pass**

Run: the Step 2 command.
Expected: PASS (8 tests).

- [ ] **Step 5: Run the ambient-state and disposal-inference scans** from Global Constraints.
Expected: no output.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(modules-core): add generated conversion scope"
```

### Task 3: Add the scoped codec protocol and runtime codecs

Model: **standard**.

**Files:**
- Create in `Expo.ModulesCore/Codecs/`: `IGeneratedScopedCodec.cs`, `GeneratedTransferredResult.cs`, `GeneratedScopedListCodec.cs`, `GeneratedScopedDictionaryCodec.cs`, `GeneratedScopedNullableCodec.cs`, `GeneratedScopedNullableReferenceCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`, `Expo.ModulesCore/Codecs/ArrayBufferCodec.cs`
- Test: `Expo.ModulesCore.Tests/Codecs/ScopedCodecTests.cs`

**Interfaces:**
- Consumes: Task 2 `GeneratedConversionScope` and its internal `Register`.
- Produces:

```csharp
public interface IGeneratedScopedCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, GeneratedConversionScope scope);
  static abstract void Transfer(T value, GeneratedConversionScope scope);
  static abstract JavaScriptValue EncodeBorrowed(T value, JavaScriptRuntime runtime);
}

public readonly struct JavaScriptValueCodec : IJavaScriptCodec<JavaScriptValue>, IGeneratedScopedCodec<JavaScriptValue>
public readonly struct ArrayBufferCodec : IGeneratedScopedCodec<ArrayBuffer>   // was a static class

public sealed class GeneratedTransferredResult<T, TCodec> where TCodec : IGeneratedScopedCodec<T>
{
  public static JavaScriptPromiseResult Resolve(T value, GeneratedConversionScope scope);
}

public readonly struct GeneratedScopedListCodec<T, TCodec> : IGeneratedScopedCodec<IReadOnlyList<T>>
    where TCodec : IGeneratedScopedCodec<T>
public readonly struct GeneratedScopedDictionaryCodec<T, TCodec>
    : IGeneratedScopedCodec<Dictionary<string, T>>, IGeneratedScopedCodec<IReadOnlyDictionary<string, T>>
    where TCodec : IGeneratedScopedCodec<T>
public readonly struct GeneratedScopedNullableCodec<T, TCodec> : IGeneratedScopedCodec<T?>
    where T : struct where TCodec : IGeneratedScopedCodec<T>
public readonly struct GeneratedScopedNullableReferenceCodec<T, TCodec> : IGeneratedScopedCodec<T?>
    where T : class where TCodec : IGeneratedScopedCodec<T>
```

- The dictionary codec's public members are `Dictionary<string, T> Decode(...)`, `void Transfer(IReadOnlyDictionary<string, T>, ...)`, and `JavaScriptValue EncodeBorrowed(IReadOnlyDictionary<string, T>, ...)`. The other three interface members are explicit implementations that delegate, as `JavaScriptDictionaryCodec<T, TCodec>` does.
- Null rule (all seven runtime codecs): `Transfer(null)` registers nothing and does not throw. `EncodeBorrowed(null)` returns `runtime.CreateNull()` only in the two nullable codecs; the leaf, list, and dictionary codecs keep the null guard of their ordinary `Encode` (`ArgumentNullException`).
- Each new public type carries the remark "This helper is for generated bindings, not ordinary module author code."

- [ ] **Step 1: Write the failing tests** in `ScopedCodecTests.cs` (Hermes fixture as in `ArrayBufferCodecTests.cs`). `JavaScriptPromiseResult.CreateValue` and `Abandon` are internal to `Expo.JSI`, which already grants `InternalsVisibleTo("Expo.ModulesCore.Tests")`; call them directly. For `Nullable<T>`, declare a test-only `readonly record struct ValueSlot(JavaScriptValue Value)` and a test codec `ValueSlotCodec : IGeneratedScopedCodec<ValueSlot>` that delegates to `JavaScriptValueCodec`.

Leaf codecs:
- `JavaScriptValueScopedDecodeRegistersOneRetainedWrapper`: scoped decode of the ref of an evaluated string; `ResetCounters()`; dispose the scope; `ReleasedValues == 1`; the decoded value's `Kind` throws `ObjectDisposedException`; the source is still readable.
- `JavaScriptValueTransferReleasesSourceOnceAndEncodedResultSurvives`: `Transfer` the same wrapper twice, `EncodeBorrowed` it, `ResetCounters()`, dispose the scope: `ReleasedValues == 1`; the encoded value still reads `"x"`.
- `JavaScriptValueEncodeBorrowedLeavesSourceUsable`: after `EncodeBorrowed` and disposal of its result, the source still reads its string.
- `ArrayBufferScopedDecodeRegistersOneLease`: `new ArrayBuffer(4)` from JS, scoped decode, `ResetCounters()`, dispose, `WaitUntilIdle()`: `LongLivedArrayBuffersReleased == 1`.
- `ArrayBufferEncodeBorrowedKeepsSourceAndIdentity`: store the JS buffer as `globalThis.b`; set `EncodeBorrowed`'s result as `globalThis.r`; `b === r` is `true`; the source `ByteLength` is still `4`.
- `ArrayBufferTransferReleasesLeaseOnce`: transfer twice, dispose, `WaitUntilIdle()`: `LongLivedArrayBuffersReleased == 1`.

Composite codecs (through a generic helper `RoundTrip<T, TCodec>(T value, JavaScriptRuntime runtime, GeneratedConversionScope scope) where TCodec : IGeneratedScopedCodec<T>`):
- `ScopedListDecodesEachBufferIntoTheScope`: `[new ArrayBuffer(1), new ArrayBuffer(2)]` with `GeneratedScopedListCodec<ArrayBuffer, ArrayBufferCodec>`: decoded lengths `1, 2`; after dispose and `WaitUntilIdle()`, `LongLivedArrayBuffersReleased == 2`.
- `ScopedListEncodesOneAliasTwice`: list `[v, v]` of one `JavaScriptValue`; `Transfer`, `EncodeBorrowed`, `ResetCounters()`, dispose: `ReleasedValues == 1`; the encoded array's two elements are strictly equal in JS.
- `ScopedListNullElementFailsEncodeAndKeepsOtherLeavesOwned` (Review Focus 3): list `[v1, null!, v2]`; `Transfer` does not throw; `EncodeBorrowed` throws `ArgumentNullException`; `ResetCounters()`, dispose: `ReleasedValues == 2`.
- `ScopedDictionaryImplementsBothDictionaryInterfaces`: round-trip `{ a: new ArrayBuffer(1) }` through `RoundTrip<Dictionary<string, ArrayBuffer>, GeneratedScopedDictionaryCodec<ArrayBuffer, ArrayBufferCodec>>` and through `RoundTrip<IReadOnlyDictionary<string, ArrayBuffer>, ...>`; after both scopes, `LongLivedArrayBuffersReleased == 2`.
- `ScopedNullableReferenceCodecRegistersNothingForNull`: with `GeneratedScopedNullableReferenceCodec<IReadOnlyList<ArrayBuffer>, GeneratedScopedListCodec<ArrayBuffer, ArrayBufferCodec>>`, decoding JS `null` returns `null`, `Transfer(null)` is a no-op, `EncodeBorrowed(null)` has `Kind == Null`; a non-null list round-trips.
- `ScopedNullableCodecRoundTripsValueStruct`: `GeneratedScopedNullableCodec<ValueSlot, ValueSlotCodec>` decodes `null` to `null` and `{ value: "a" }` to a slot; `ResetCounters()`, dispose: `ReleasedValues == 1`.

Transferred result:
- `TransferredResultFactoryEncodesThenDisposesScope`: register an input leaf by scoped decode; `Resolve(sameLeaf, scope)`; `ResetCounters()`; `using var js = result.CreateValue(runtime)`: `js` reads the value, `ReleasedValues == 1` (the one source); a later `result.Abandon()` releases nothing.
- `TransferredResultFactoryFailureStillDisposesScope`: scope holds one decoded leaf; resolve a separately created wrapper that was already disposed; `CreateValue` throws `ObjectDisposedException`; `ReleasedValues` delta is `1` (the decoded leaf); `Abandon()` releases nothing more.
- `TransferredResultAbandonDisposesScope`: two JS buffers decoded into the scope, resolve one of them; `Abandon()`; `WaitUntilIdle()`: `LongLivedArrayBuffersReleased == 2`; a second `Abandon()` releases nothing.

- [ ] **Step 2: Run them to verify they fail**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter "FullyQualifiedName~ScopedCodecTests"`
Expected: build FAIL with `CS0246` for `IGeneratedScopedCodec<>`, `GeneratedScopedListCodec<,>`, and `GeneratedTransferredResult<,>`, and `CS0117` for `JavaScriptValueCodec.Transfer`.

- [ ] **Step 3: Implement the leaf codecs**

- `JavaScriptValueCodec`: scoped `Decode` = `value.Retain()`, then `scope.Register(result)`; `Transfer` = `scope.Register(value)` unless null; `EncodeBorrowed` = `value.Retain()` after the existing null guard.
- `ArrayBufferCodec`: change `public static class` to `public readonly struct`. Scoped `Decode` = existing `Decode(value, runtime)`, then `scope.Register(result)`; `Transfer` = `scope.Register(value)` unless null; `EncodeBorrowed` = existing `Encode(value, runtime)`.

- [ ] **Step 4: Implement the composites and the transferred result**

- List: `Decode` builds a `T[]` from `value.AsArray()`, element by element through `TCodec.Decode(element, runtime, scope)`; no `try`/`catch`. `Transfer` calls `TCodec.Transfer` per element. `EncodeBorrowed` mirrors `JavaScriptArrayCodec.Encode` with `using var element = TCodec.EncodeBorrowed(...)`.
- Dictionary: the same over `value.AsObject().GetOwnPropertyNames()`, with an ordinal `Dictionary<string, T>`, mirroring `JavaScriptDictionaryCodec`.
- Nullable codecs: `value.IsNullish ? null : TCodec.Decode(value, runtime, scope)`; `Transfer` only for a non-null value; `EncodeBorrowed` returns `runtime.CreateNull()` for null.
- `GeneratedTransferredResult<T, TCodec>` is the `ResolveOwned` state: private constructor, fields `value` and `scope`. `Resolve` calls `TCodec.Transfer(value, scope)` and returns `JavaScriptPromiseResult.ResolveOwned(state, static (runtime, state) => { try { return TCodec.EncodeBorrowed(state.value, runtime); } finally { state.scope.Dispose(); } }, static state => state.scope.Dispose())`. `Resolve` itself never disposes the scope: when `Transfer` throws, the generated lambda's `catch` does.

- [ ] **Step 5: Run the new tests**

Run: the Step 2 command.
Expected: PASS.

- [ ] **Step 6: Run both suites**

Run: Generator tests, then ModulesCore tests.
Expected: all pass. `ArrayBufferCodec` callers compile unchanged. Task 1 digests are unchanged (no generator change).

- [ ] **Step 7: Run the three scans** from Global Constraints. Expected: no output.

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(modules-core): add scoped codec protocol and composite codecs"
```

### Task 4: Release direct leaves through one conversion scope

Model: **most capable**.

This task swaps the descriptor facts and moves every direct `JavaScriptValue` and `ArrayBuffer` parameter, return, async result, and property onto the scope. Composite branches (records, lists, dictionaries, nullable wrappers) keep their current `Borrowed` behavior and text in this task; Task 5 changes them. `ArrayBufferCodec`'s static `Decode`/`Encode` stay, so record codecs that call them still compile.

**Files:**
- Modify: `Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs`, `ExpoModulesGenerator.Codecs.cs`, `ExpoModulesGenerator.SharedObjectValidation.cs`, `ExpoModulesGenerator.ModuleAnalysis.cs`, `ExpoModulesGenerator.Emission.cs` (generator project)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`, `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs` (leaf digests only)
- Create: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleFixtures.cs`, `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs`
- Modify: `Expo.ModulesCore.Tests/Modules/ModuleAttributeTests.cs:95-125`

**Interfaces:**
- Consumes: Task 3 protocol and `GeneratedTransferredResult<T, TCodec>.Resolve`.
- Produces:

```csharp
internal enum ExpoCodecShape { JavaScriptCodec, ScopedOnly, StaticHelper }
internal enum ExpoResourceOwnership { Borrowed, ScopedResource, ContextRetained }

internal sealed record ExpoCodecDescriptor(
    string Expression,
    ExpoCodecShape Shape,
    ExpoCodecCapabilities Capabilities,
    ExpoDecodeOperation DecodeOperation,
    ExpoContextRequirement ContextRequirement,
    ExpoResourceOwnership ResourceOwnership);
```

- `ExpoDecodedOwnership`, `ExpoEncodeOperation`, both fields, `IsInvocationOwned`, and `EmitDisposeDecodedValues` are deleted.
- `private static bool IsScopedResource(ExpoCodecDescriptor? codec)` replaces `IsInvocationOwned`.
- Rows set in this task: `OrdinaryCodec` → `Borrowed`; `JavaScriptValue` → `OrdinaryCodec("JavaScriptValueCodec") with { ResourceOwnership = ScopedResource }`; `ArrayBuffer` → `ScopedCodec("ArrayBufferCodec")`, where `private static ExpoCodecDescriptor ScopedCodec(string expression)` = `ScopedOnly`, `DecodeAndEncode`, `Decode`, `None`, `ScopedResource` (Task 5 reuses it for the composite and record rows); `CallbackCodec` → `ContextRetained`; `SharedObjectCodec` (`SharedObjectValidation.cs:139`) → `Borrowed`. Every list, dictionary, nullable, and record branch keeps `OrdinaryCodec` (`Borrowed`), which is today's effective behavior.
- `AsGenericArgumentCodec` is unchanged in this task: `ArrayBuffer` (now `ScopedOnly`) stays rejected in every generic position with today's IDs.
- `GetDecodeExpression` emits `{Expression}.Decode(arguments.GetValue(i), {runtime}, __expoScope)` for a `ScopedResource` descriptor and today's text otherwise.
- `asyncResultTypeName` (`ModuleAnalysis.cs:477`) uses `GetCodecTypeArgumentName(asyncResultType)`, so the `T` of `GeneratedTransferredResult<T, TCodec>` carries nullable annotations (no CS8631). Nothing emits `AsyncResultType` today, so no digest moves.
- A member "holds a leaf" when any codec parameter, the return, or the async result is `ScopedResource`. Only such members change text.

Generated shapes (the scope local is always `__expoScope`; sync members declare it on the line after the receiver declaration):

```csharp
// sync function, shared-object member, getter, setter, constructor factory, span host function
using var __expoScope = new GeneratedConversionScope();
var __expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime, __expoScope);
var __expoArg1 = StringCodec.Decode(arguments.GetValue(1), runtime);
// leaf-bearing result (also inside the span lambda, and the getter with module.Prop):
var __expoResult = module.M(__expoArg0, __expoArg1);
ArrayBufferCodec.Transfer(__expoResult, __expoScope);
return ArrayBufferCodec.EncodeBorrowed(__expoResult, runtime);
// leaf-free result: today's `return Codec.Encode(module.M(...), runtime[, context]);`
```

```csharp
// async, any leaf: the outer try covers only code that runs before CreatePromise returns
var __expoScope = new GeneratedConversionScope();
global::Expo.JSI.JavaScriptPromiseValue __expoPromiseValue;
try
{
  // RequireArgumentCount, receiver, context capture, decodes as above (scope argument, plain var)
  var __expoTask = module.M(__expoArg0);
  __expoPromiseValue = jsRuntime.CreatePromise(
      async _ =>
      {
        // leaf-free result (void, ordinary codec):
        try { await __expoTask.ConfigureAwait(false); return /* today's Resolve(...) */; }
        finally { __expoScope.Dispose(); }
        // leaf-bearing result:
        try
        {
          var __expoResult = await __expoTask.ConfigureAwait(false);
          return GeneratedTransferredResult<{AsyncResultType}, {AsyncResultCodec.Expression}>.Resolve(__expoResult, __expoScope);
        }
        catch
        {
          __expoScope.Dispose();
          throw;
        }
      }
  );
}
catch (global::System.Exception exception)
{
  __expoScope.Dispose();
  return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);
}
// From here the lambda is the sole owner of the scope; the host frame never touches it again.
using (__expoPromiseValue)
{
  return __expoPromiseValue.AsValue();
}
```

If `AsValue()` throws, the host function throws, the bridge turns it into a JavaScript error, and the orphaned Promise still settles through the lambda, whose own paths dispose the scope. This removes the cross-thread overlap between the outer `catch` and a lambda continuation (spec S1). Async members with no leaf keep today's text exactly. The span lease keeps its own `using var __expoSpanBuffer{n} = ArrayBufferCodec.Decode(...)` after the scope declaration. Put the three-line transfer return in one emitter helper used by the sync, getter, and span sites.

- [ ] **Step 1: Write the failing generator tests** in `ExpoModulesGeneratorTests.cs`. Each asserts `Assert.Empty(GeneratorDiagnosticIds(result))` and `AssertNoCompilerErrors(result)`, and counts occurrences of `new GeneratedConversionScope()` in `GeneratedText(result)`.

- `GeneratorEmitsOneConversionScopePerDirectLeafMember` (theory: member, expected scope count, expected line):
  - `[JS] public int M(JavaScriptValue value) => 0;`, 1, `var __expoArg0 = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
  - `[JS] public int M(ArrayBuffer value) => 0;`, 1, `var __expoArg0 = ArrayBufferCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
  - `[JS] public JavaScriptValue M() => throw new NotImplementedException();`, 1, `return JavaScriptValueCodec.EncodeBorrowed(__expoResult, runtime);`
  - `[JS] public ArrayBuffer M() => throw new NotImplementedException();`, 1, `ArrayBufferCodec.Transfer(__expoResult, __expoScope);`
  - `[JS] public JavaScriptValue Value { get; set; } = null!;`, 2, `var __expoValue = JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime, __expoScope);`
  - `[JS] public JavaScriptValue M(ReadOnlySpan<byte> data, JavaScriptValue label) => label;`, 1, `var __expoArg1 = JavaScriptValueCodec.Decode(arguments.GetValue(1), runtime, __expoScope);`
  Every row also asserts the text does not contain `?.Dispose();`, `using var __expoArg`, or `using var __expoResult`.
- `GeneratorEmitsConversionScopeInSharedObjectFactoryAndMembers`: `[ExpoSharedObject] Entry` with `[JS] public Entry(JavaScriptValue value) { }` and `[JS] public int Read(ArrayBuffer value) => 0;` plus an owning module: two scopes; the factory contains `JavaScriptValueCodec.Decode(arguments.GetValue(0), runtime, __expoScope)`.
- `GeneratorEmitsAsyncConversionScopeShapes` (theory: member, expected fragment, forbidden fragment):
  - `[JS] public Task M(JavaScriptValue value) => Task.CompletedTask;`, `__expoScope.Dispose();`, `GeneratedTransferredResult`
  - `[JS] public Task<int> M(ArrayBuffer value) => Task.FromResult(0);`, `__expoScope.Dispose();`, `GeneratedTransferredResult`
  - `[JS] public Task<JavaScriptValue> M() => throw new NotImplementedException();`, `return GeneratedTransferredResult<global::Expo.JSI.JavaScriptValue, JavaScriptValueCodec>.Resolve(__expoResult, __expoScope);`, `ResolveOwned`
  - `[JS] public Task<ArrayBuffer> M(JavaScriptValue value) => throw new NotImplementedException();`, `GeneratedTransferredResult<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.Resolve(`, `ResolveOwned`
  Every row asserts that `var __expoScope = new GeneratedConversionScope();` occurs before the first `try`, that the last `__expoScope.Dispose();` occurs before `return GeneratedFunction.CreateRejectedPromise(jsRuntime, exception);`, and that `return __expoPromiseValue.AsValue();` occurs after `catch (global::System.Exception exception)`.
- `GeneratorEmitsNoConversionScopeForLeafFreeMembers`: a module with `int`, `string`, `IReadOnlyList<int>`, a `(string, int)` record, `JavaScriptCallback<int>`, an `[ExpoSharedObject]` parameter (declare the class and list it in `Classes`), an async `Task<int>`, a `ReadOnlySpan<byte>` function returning `string`, and a `JavaScriptValue` typed event: the text does not contain `GeneratedConversionScope`.

- [ ] **Step 2: Write the failing Hermes tests**

In `ResourceScopeModuleFixtures.cs`, add `[ExpoModule("ResourceScope", Classes = new[] { typeof(ResourceScopeEntry) })] public sealed partial class ResourceScopeModule : Module` with a constructor taking `DotnetRuntimeContext` and these members (Task 5 adds more to the same class):

```csharp
[JS] public int Measure(ArrayBuffer value) => value.ByteLength;
[JS] public int MeasureOrThrow(ArrayBuffer value) => throw new InvalidOperationException("measure failed");
[JS] public int MeasureTwo(ArrayBuffer first, ArrayBuffer second) => first.ByteLength + second.ByteLength;
[JS] public ArrayBuffer Same(ArrayBuffer value) => value;
[JS] public ArrayBuffer Retained(ArrayBuffer value) => value.Retain();
[JS] public string Text(string value) => value;                         // leaf-free calibration twin
[JS] public JavaScriptValue SameValue(JavaScriptValue value) => value;
[JS] public JavaScriptValue DisposedValue() { var v = RuntimeContext.Runtime.CreateString("gone"); v.Dispose(); return v; }
[JS] public async Task<JavaScriptValue> SameValueAsync(JavaScriptValue value) { await Task.Yield(); return value; }
[JS] public async Task<int> LengthAfterAwaitAsync(ArrayBuffer value) { await Task.Yield(); return value.ByteLength; }
[JS] public async Task<int> FailAfterAwaitAsync(ArrayBuffer value) { await Task.Yield(); throw new InvalidOperationException("async failed"); }
[JS] public async Task<ArrayBuffer> HoldAsync(ArrayBuffer value) { await Gate.Task; return value; }
[JS] public async Task<JavaScriptValue> HoldValueAsync(JavaScriptValue value) { await Gate.Task; return value; }
[JS] public async Task<string> HoldTextAsync(string value) { await Gate.Task; return value; }   // leaf-free twin
[JS] public async Task<ArrayBuffer> HoldRetainedAsync(ArrayBuffer value) { await Gate.Task; return value.Retain(); }
[JS] public ArrayBuffer Pick(ReadOnlySpan<byte> bytes, ArrayBuffer other) => other;
public TaskCompletionSource Gate { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
```

and `[ExpoSharedObject("ResourceScopeEntry")] public sealed partial class ResourceScopeEntry : SharedObject` with `[JS] public ResourceScopeEntry(ArrayBuffer seed, bool fail)` (throws `InvalidOperationException("entry failed")` when `fail`, else stores `seed.ByteLength`) and `[JS] public int Measure(ArrayBuffer value) => value.ByteLength;`.

In `ResourceScopeModuleTests.cs`, register through `ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules)` as `BinaryModuleTests.cs` does, and get the module instance for `Gate` with `context.ModuleRegistry.GetOrCreateModule<ResourceScopeModule>(...)` as `ModuleAttributeTests.cs:104-107` does. Every buffer is `new ArrayBuffer(n)` created in JS. Each test calls `ResetCounters()` right before the measured call and `WaitUntilIdle()` before reading `LongLivedArrayBuffersReleased`.

- `SyncLeafParameterReleasesOnceOnSuccessAndThrow` (characterization): `measure(b)` returns `4`; `measureOrThrow(b)` is caught in JS as an `Error` with message containing `measure failed`; lease delta `2`.
- `SyncMethodReturningItsInputBufferKeepsIdentityAndReleasesOnce`: `same(b) === b`; lease delta `1`.
- `SyncMethodReturningItsInputValueResolvesIt` (RED today; inferred: `using var __expoArg0` disposes the returned wrapper): `sameValue(o) === o` for a JS object `o`; `ReleasedValues` delta equals the delta of `text('x')` plus `1`.
- `ReturnedRetainedCopyOfInputReleasesBothLeases` (Review Focus 1): `retained(b) === b`; lease delta `2`.
- `SameBufferInTwoParametersReleasesBothLeases` (Review Focus 2): `measureTwo(b, b)` returns `8`; lease delta `2`.
- `ReturnedDisposedValueFailsThroughCatchableError`: `disposedValue()` is caught in JS as an `Error`; a following `measure(b)` still returns `4`.
- `AsyncMethodReturningItsInputValueResolvesWithIt` (RED today; inferred rejection with `ObjectDisposedException`): the Promise of `sameValueAsync(o)` fulfils with a value strictly equal to `o`. Use the outcome pattern in `AsyncModuleTests.cs`.
- `AsyncLeafBufferIsReadableAfterAwait` (characterization): `lengthAfterAwaitAsync(b)` fulfils with `4`; lease delta `1`.
- `AsyncLeafFaultRejectsAndReleasesInputOnce` (characterization): `failAfterAwaitAsync(b)` rejects with an `Error` whose message contains `async failed`; lease delta `1`.
- `AsyncInputLeafLivesUntilSettlement` (RED today: the input `finally` releases before settlement). Use a `JavaScriptValue`: its release deletes the `jsi::Value` on the calling thread with no executor hop, so `ReleasedValues` sees an early release even while the executor is paused (a JS-backed `ArrayBuffer` lease release is executor-hopped and would not). Call `holdValueAsync(o)` and keep the Promise in a global; then `DisableSyncExecutionForTesting()`, `PauseRuntimeExecutor()`, `ResetCounters()`, `module.Gate.SetResult()`, `WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate)`: `ReleasedValues` delta `0`. Then `ResumeRuntimeExecutor()`, `WaitUntilIdle()`, `SetSyncExecutionSupportedForTesting(true)`: the Promise fulfilled with a value strictly equal to `o`, and the delta equals the delta of the same sequence with `holdTextAsync('x')` plus `1`. Run the twin sequence first in the same fixture (after its own `ResetCounters()`), then the leaf sequence; complete `Gate` for the twin and give the leaf call a fresh gate (make `Gate` a settable property and replace it between the two sequences).
- `AbandonedAsyncLeafResultReleasesInputAndResultOnce` (characterization): as above with `holdRetainedAsync(b)`, plus `DropNextRuntimeTask(JavaScriptTaskPriority.Immediate)` before `Gate.SetResult()` (pattern: `EventModuleTests.cs:378-402`); after `WaitUntilIdle()` the lease delta is `2` and the Promise stays pending.
- `AsyncLeafMemberArgumentCountFailureRejects` (Review Focus 5): `holdAsync()` returns a rejected Promise with an `Error` whose message contains `expects 1 arguments`; lease delta `0`.
- `PropertyBufferSetAndGetReleaseEachLeaseOnce`: on the existing `GeneratedProperties` module, `module.buffer = b; [module.buffer === b, module.buffer === b]` is `[true, true]`; lease delta `3` (one setter decode, two getter copies).
- `SharedObjectConstructorAndMemberReleaseLeaves`: construct `ResourceScopeEntry(b, false)` from JS (as `SharedObjects/SharedObjectTests.cs` constructs exported classes), call `entry.measure(b)`, then construct with `fail = true` and catch the `Error`; lease delta `3`.
- `SpanFunctionReleasesSpanLeaseAndLeafArgumentOnce`: `pick(b1, b2) === b2`; lease delta `2` (span lease and scoped argument).

In `ModuleAttributeTests.cs:95-125` change `1u` to `3u` and `2u` to `4u`. Replace the comment: each getter read transfers the authored `Retain()` copy into the conversion scope and returns an independent retained wrapper, so each of the two reads adds one release. This is the shift the spec's Verification section names (S3). `Properties/ModulePropertyTests.cs:52-93` and `Testing/ExpoModuleTestHostTests.cs:152-171` do not run generated glue (they use hand-written `GeneratedProperty.Define` delegates and `Promise.resolve`), so expect them unchanged.

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorEmitsOneConversionScope|FullyQualifiedName~GeneratorEmitsConversionScopeInSharedObject|FullyQualifiedName~GeneratorEmitsAsyncConversionScope|FullyQualifiedName~GeneratorEmitsNoConversionScope"`
Expected: FAIL on every scope-count and fragment assertion (`Assert.Equal() Failure` / `Assert.Contains() Failure`); `GeneratorEmitsNoConversionScopeForLeafFreeMembers` passes (characterization).

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter "FullyQualifiedName~ResourceScopeModuleTests|FullyQualifiedName~GeneratedProviderPropertyJavaScriptValueSetter"`
Expected: FAIL for `SyncMethodReturningItsInputValueResolvesIt`, `AsyncMethodReturningItsInputValueResolvesWithIt`, `AsyncInputLeafLivesUntilSettlement`, and the `ModuleAttributeTests` count; the characterization tests pass. Put the observed failures in the task report. If a test marked RED passes, or a characterization test fails, stop and report.

Before any production edit, dump the generated provider text of both leaf fixtures to a scratch location outside the repo (for example, temporarily assert the text equals `""` so xUnit prints it, then revert that line). Step 7 diffs against this dump.

- [ ] **Step 4: Implement the descriptor change and resolution rows** listed in Interfaces. Let the compiler find every use of the deleted members.

- [ ] **Step 5: Implement the emission** for `EmitHostFunction` (sync and async), `EmitPropertyGetter`, `EmitPropertySetter`, `EmitSharedObjectFactory`, and `EmitSpanHostFunction`, using the generated shapes above. Leaf-free branches keep their exact text.

- [ ] **Step 6: Run the Step 3 commands**
Expected: PASS.

- [ ] **Step 7: Review and re-pin the leaf digests**

Run Generator tests. Expected: only `GeneratorPinsLeafCodecPolicyModuleOutputContract` and `GeneratorPinsLeafCodecPolicySharedObjectOutputContract` fail. Every leaf-free and `GeneratorPreserves*` digest passes unchanged; if one fails, stop: leaf-free text changed.
Dump the generated provider text of both leaf fixtures again the same way, diff it against the Step 3 dump, and confirm the diff touches only leaf-bearing members and replaces per-leaf cleanup with the scope. Put the diff summary in the task report. Then update the two expected digest lists.

- [ ] **Step 8: Run everything**

Run: Generator tests, then ModulesCore tests, then `scripts/test-managed.sh`.
Expected: all pass, 0 skipped.

- [ ] **Step 9: Run the three scans** from Global Constraints. Expected: no output.

- [ ] **Step 10: Commit**

```bash
git commit -m "fix(generator): release direct wrapper leaves through one conversion scope"
```

### Task 5: Compose leaves in records, collections, and nullable wrappers

Model: **most capable**.

**Files:**
- Modify: `Expo.ModulesCore.Generator/ExpoModuleModel.cs`, `ExpoModulesGenerator.Codecs.cs`, `ExpoModulesGenerator.EventAnalysis.cs`, `ExpoModulesGenerator.Emission.cs`
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`, `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs` (leaf digests only)
- Modify: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleFixtures.cs`, `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs`

**Interfaces:**
- Consumes: Task 4 descriptor, `IsScopedResource`, scoped `GetDecodeExpression`, and the transfer emitter helper.
- Produces:
  - `ExpoGeneratedRecordCodecModel(string CodecTypeName, string RecordTypeName, EquatableArray<ExpoGeneratedRecordFieldModel> Fields, ExpoResourceOwnership ResourceOwnership, bool IsValueType, Location? Location)`. `ResourceOwnership` is `ScopedResource` when any field descriptor is `ScopedResource`, else `Borrowed`. `IsValueType` is the record symbol's `IsValueType` (record struct versus class record); only scoped record emission reads it.
  - Task 4's `ScopedCodec(expression)` serves the "scoped composite" and "record with a leaf" rows of the spec's table.
  - `private static ExpoCodecDescriptor RecordCodecDescriptor(ExpoGeneratedRecordCodecModel codec)`: `ScopedCodec(codec.CodecTypeName)` when the model is `ScopedResource`, else `OrdinaryCodec(codec.CodecTypeName)`. Both the new-record path and the cache-hit path (`Codecs.cs:321-325`) call it.
  - `AsGenericArgumentCodec` is replaced by two helpers:
    - `AsCompositeChildCodec(codec)`: accepts `JavaScriptCodec` or `ScopedOnly`. Used for the list element, dictionary value, `Nullable<T>` inner, and the three nullable-reference inners.
    - `AsJavaScriptCodecArgument(codec)`: accepts `JavaScriptCodec` only. Used for the two callback-result sites, the callback-argument tuple element, and the event payload (`EventAnalysis.cs:221`, behavior unchanged).
  - Selection on a `ScopedResource` child (else today's ordinary codec and text):
    - list → `ScopedCodec($"GeneratedScopedListCodec<{T}, {child}>")`
    - dictionary (both interfaces) → `ScopedCodec($"GeneratedScopedDictionaryCodec<{T}, {child}>")`
    - `Nullable<T>` → `ScopedCodec($"GeneratedScopedNullableCodec<{T}, {child}>")`
    - nullable reference → `ScopedCodec($"GeneratedScopedNullableReferenceCodec<{valueTypeName}, {inner}>")`, where `inner` is the child itself, or the scoped list or dictionary codec built over the element.
  - `IsExcludedFromNullableReference` is unchanged, so `ArrayBuffer?` and `JavaScriptValue?` stay rejected before resolution.

Record codec text for a `ScopedResource` record (leaf-free records keep today's text exactly):

```csharp
private readonly struct {CodecTypeName} : global::Expo.ModulesCore.Codecs.IGeneratedScopedCodec<{Record}>
{
  public static {Record} Decode(global::Expo.JSI.JavaScriptValueRef value, global::Expo.JSI.JavaScriptRuntime runtime, global::Expo.ModulesCore.Codecs.GeneratedConversionScope __expoScope)
  {
    var obj = value.AsObject();
    var buffer = ArrayBufferCodec.Decode(obj.GetProperty("buffer"), runtime, __expoScope);   // ScopedResource field
    var label = StringCodec.Decode(obj.GetProperty("label"), runtime);                     // other field
    return new {Record}(buffer, label);
  }

  public static void Transfer({Record} value, global::Expo.ModulesCore.Codecs.GeneratedConversionScope __expoScope)
  {
    if (value is null) return;                                                              // class record only
    ArrayBufferCodec.Transfer(value.Buffer, __expoScope);                                   // ScopedResource fields only
  }

  public static global::Expo.JSI.JavaScriptValue EncodeBorrowed({Record} value, global::Expo.JSI.JavaScriptRuntime runtime)
  {
    global::System.ArgumentNullException.ThrowIfNull(value);                                // class record only
    using var obj = runtime.CreateObject();
    using var buffer = ArrayBufferCodec.EncodeBorrowed(value.Buffer, runtime);
    obj.SetProperty("buffer", buffer);
    using var label = StringCodec.Encode(value.Label, runtime);
    obj.SetProperty("label", label);
    return obj.AsValue();
  }
}
```

The two null-guard lines are emitted only when `IsValueType` is false (spec S2): `Transfer` must be total so a null record inside a list cannot stop later leaves from registering, and `EncodeBorrowed` fails with `ArgumentNullException` as the ordinary codecs do. The scope parameter is `__expoScope`, so a record field named `scope` cannot collide with it. Field locals keep `GetRecordFieldLocalName`.

- [ ] **Step 1: Write the failing generator tests** in `ExpoModulesGeneratorTests.cs`.

- Edit `GeneratorRejectsStaticHelperCodecsInGenericPositions`: remove the five `ArrayBuffer` list, nullable list, dictionary, dictionary-return, and property rows. Keep every callback row, including both `ArrayBuffer` callback rows (`EXPOJSI008`).
- Edit `GeneratorRejectsStaticHelperCodecsOnSharedObjectMembers`: remove the `Store(IReadOnlyList<ArrayBuffer>)` row.
- Edit `GeneratorRejectsRecordFieldsThatNeedARuntimeContext`: remove the `IReadOnlyList<ArrayBuffer>` field row.
- `GeneratorCompilesScopedCompositesAtBoundaryPositions` (theory: declarations, member, expected fragment). Each row is `[JS] public <signature> => <trivial body>;` (or the property) inside `ModuleSource`. Each row: no generator diagnostics, `AssertNoCompilerDiagnostics(result)` (errors and warnings, to catch CS8631), and `Assert.Contains(fragment, GeneratedText(result))`:
  - `int M(IReadOnlyList<ArrayBuffer> value)`: `GeneratedScopedListCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.Decode(arguments.GetValue(0), runtime, __expoScope)`
  - `int M(IReadOnlyList<ArrayBuffer>? value)`: `GeneratedScopedNullableReferenceCodec<global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.ArrayBuffer>, GeneratedScopedListCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>>`
  - `int M(IReadOnlyList<ArrayBuffer>? buffers = null)` (Review Focus 4): `arguments.Count <= 0 ||`
  - `int M(Dictionary<string, ArrayBuffer> value)`: `GeneratedScopedDictionaryCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.Decode(`
  - `IReadOnlyDictionary<string, ArrayBuffer> M()`: `GeneratedScopedDictionaryCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.Transfer(__expoResult, __expoScope);`
  - `IReadOnlyList<ArrayBuffer> Buffers { get; } = null!;`: `GeneratedScopedListCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>.EncodeBorrowed(__expoResult, runtime)`
  - `Task<IReadOnlyList<ArrayBuffer>?> M()`: `GeneratedTransferredResult<global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.ArrayBuffer>?, GeneratedScopedNullableReferenceCodec<`
  - `int M(IReadOnlyList<JavaScriptValue> value)`: `GeneratedScopedListCodec<global::Expo.JSI.JavaScriptValue, JavaScriptValueCodec>`
  - `int M(Dictionary<string, JavaScriptValue> value)`: `GeneratedScopedDictionaryCodec<global::Expo.JSI.JavaScriptValue, JavaScriptValueCodec>`
  - record `Holder(IReadOnlyList<ArrayBuffer> Field)`, `int M(Holder holder)`: `IGeneratedScopedCodec<global::Expo.TestModules.Holder>`
  - record `Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer>> Rows)`, `Shelf M(Shelf shelf)`: `.Transfer(value.Rows, __expoScope);`
  - `readonly record struct Slot(JavaScriptValue Value)`, `int M(Slot? slot)`: `GeneratedScopedNullableCodec<global::Expo.TestModules.Slot, `
- `GeneratorCompilesScopedCompositesOnSharedObjectMembers`: an `[ExpoSharedObject] Entry` with `[JS] public void Store(IReadOnlyList<ArrayBuffer> data) { }` and `[JS] public Entry(Dictionary<string, ArrayBuffer> seed) { }`: no diagnostics, no compiler errors, two scopes.
- `GeneratorRecordCodecUsesScopeOnlyForLeafFields`: record `Mixed(ArrayBuffer Buffer, string Label)` as parameter and return. Contains `ArrayBufferCodec.Decode(obj.GetProperty("buffer"), runtime, __expoScope)`, `StringCodec.Decode(obj.GetProperty("label"), runtime)`, `ArrayBufferCodec.Transfer(value.Buffer, __expoScope);`, `using var buffer = ArrayBufferCodec.EncodeBorrowed(value.Buffer, runtime);`, and `using var label = StringCodec.Encode(value.Label, runtime);`. Does not contain `StringCodec.Transfer`.
- `GeneratorRecordNullGuardsOnlyForClassRecords` (theory): class record `Boxed(ArrayBuffer Buffer)` → the record codec contains `if (value is null) return;` and `global::System.ArgumentNullException.ThrowIfNull(value);`; `readonly record struct BoxedSlot(ArrayBuffer Buffer)` → contains neither. Both rows: `AssertNoCompilerDiagnostics(result)`.
- `GeneratorRecordCacheHitKeepsScopedResource`: one leaf record used as a parameter of a first method, then as the return of a second method and a shared-object property: the second method contains `.Transfer(__expoResult, __expoScope);` for the record codec, and the provider declares the record codec once.
- `GeneratorRejectsLeafBearingCompositeCallbackTypeArguments` (theory, module rows `EXPOJSI008`, one shared-object row `EXPOJSI023`): `JavaScriptCallback<Holder>` with `record Holder(JavaScriptValue Value)`; `JavaScriptCallback<IReadOnlyList<JavaScriptValue>>`; `JavaScriptCallback<ValueTuple<Dictionary<string, JavaScriptValue>>, int>`; `JavaScriptCallback<IReadOnlyList<JavaScriptValue>?>`; and `JavaScriptCallback<IReadOnlyList<JavaScriptValue>>` as a shared-object method parameter.
- `GeneratorKeepsDirectJavaScriptValueCallbackTypeArguments` (characterization; passes before and after): `JavaScriptCallback<JavaScriptValue>` and `JavaScriptCallback<ValueTuple<JavaScriptValue>, int>` parameters: no diagnostics, no compiler errors.
- `GeneratorRejectsNestedBufferEventPayloads` (characterization): `[Event] Func<IReadOnlyList<ArrayBuffer>, Task>` on a module → `EXPOJSI019`; on a shared object → `EXPOJSI027`; the text does not contain `GeneratedConversionScope`.

- [ ] **Step 2: Write the failing Hermes tests**

Add to `ResourceScopeModuleFixtures.cs`:

```csharp
public sealed record BufferPair(ArrayBuffer First, ArrayBuffer Second);
public sealed record CountedBuffer(ArrayBuffer Buffer, int Count);
public sealed record ValuePair(JavaScriptValue First, JavaScriptValue Second);
public sealed record Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer>> Rows);
public readonly record struct Slot(JavaScriptValue Value);
public sealed record TextPair(string First, string Second);        // leaf-free calibration twins
public readonly record struct TextSlot(string Value);
public sealed record Mixed(ArrayBuffer Buffer, JavaScriptValue Value);
public sealed record TextMixed(string Buffer, string Value);    // leaf-free twin of Mixed
```

and to `ResourceScopeModule`:

```csharp
[JS] public int MeasurePair(BufferPair pair) => pair.First.ByteLength + pair.Second.ByteLength;
[JS] public BufferPair EchoPair(BufferPair pair) => pair;
[JS] public int MeasureCounted(CountedBuffer value) => value.Count;
[JS] public ValuePair Twin(JavaScriptValue value) { var copy = value.Retain(); return new ValuePair(copy, copy); }
[JS] public ValuePair EchoValues(ValuePair pair) => pair;
[JS] public BufferPair BrokenPair(ArrayBuffer value) => new(value.Retain(), null!);
[JS] public IReadOnlyList<ArrayBuffer> EchoList(IReadOnlyList<ArrayBuffer> values) => values;
[JS] public Dictionary<string, ArrayBuffer> EchoMap(Dictionary<string, ArrayBuffer> values) => values;
[JS] public Shelf EchoShelf(Shelf shelf) => shelf;
[JS] public async Task<BufferPair> EchoPairAsync(BufferPair pair) { await Task.Yield(); return pair; }
[JS] public async Task<BufferPair> HoldPairAsync(BufferPair pair) { await Gate.Task; return pair; }
[JS] public string? SlotKind(Slot? slot) => slot?.Value.Kind.ToString();
[JS] public Slot? EchoSlot(Slot? slot) => slot;
[JS] public int CountOptional(IReadOnlyList<ArrayBuffer>? buffers = null) => buffers?.Count ?? -1;
[JS] public TextPair TextTwin(string value) => new(value, value);
[JS] public TextSlot? EchoTextSlot(TextSlot? slot) => slot;
[JS] public Mixed EchoMixed(Mixed value) => value;
[JS] public TextMixed EchoTextMixed(TextMixed value) => value;
[JS] public IReadOnlyList<JavaScriptValue> EchoValueList(IReadOnlyList<JavaScriptValue> values) => values;
[JS] public IReadOnlyList<string> EchoTextList(IReadOnlyList<string> values) => values;
[JS] public int MeasurePairs(IReadOnlyList<BufferPair> pairs) => pairs.Count;
[JS] public IReadOnlyList<BufferPair> PairsWithNull(BufferPair pair) => new[] { null!, pair };
[JS] public BufferPair StoredPair
{
  get => new(stored!.First.Retain(), stored.Second.Retain());
  set { var copy = new BufferPair(value.First.Retain(), value.Second.Retain()); Release(stored); stored = copy; }
}
```

`stored` is a private `BufferPair?` field; `Release` disposes both leaves of a non-null pair; the module's `Dispose` releases it.

Tests in `ResourceScopeModuleTests.cs` (same counter rules as Task 4):
- `RecordWithBufferFieldsReleasesEachLeaseOnce` (RED: leaks today): `measurePair({ first: b1, second: b2 })` returns the sum; lease delta `2`.
- `RecordReturnTransfersEveryLeaf`: `echoPair(p)` returns an object whose `first === b1` and `second === b2`; after the call, `new Uint8Array(r.first)[0]` still reads the written byte; lease delta `2`.
- `LaterFieldDecodeFailureReleasesEarlierLease` (RED: leaks today): `measureCounted({ buffer: b, count: 'x' })` is caught in JS as an `Error`; lease delta `1`.
- `AliasInTwoReturnedLeavesReleasesSourceOnce` (RED: `ObjectDisposedException` today): `twin(o)` returns `r` with `r.first === o && r.second === o`; `ReleasedValues` delta equals the delta of `textTwin('x')` plus `2` (the decoded input and the one retained copy). `textTwin` has the same arity and result shape, so its delta covers the object and field temporaries.
- `SyncAndAsyncRecordReturningInputLeavesReleaseOnce`: `echoValues({ first: o1, second: o2 })` returns both values strictly equal; `echoPairAsync(p)` fulfils with both buffers strictly equal; lease delta for the async call `2`.
- `EncodeFailureReleasesTransferredLeaves`: `brokenPair(b)` is caught in JS as an `Error`; lease delta `2` (the decoded input and the transferred retained copy).
- `ListAndDictionaryOfBuffersRoundTrip` (RED: does not compile today): `echoList([b1, b2])` returns `[b1, b2]` by identity; `echoMap({ a: b1 })` returns `{ a: b1 }`; lease delta `3`; the JS results stay readable after the call.
- `NestedRecordListDictionaryBufferRoundTrips`: `echoShelf({ rows: [{ a: b }] })` returns `rows[0].a === b`; lease delta `1`.
- `AbandonedAsyncRecordResultReleasesLeavesOnce`: `holdPairAsync(p)` with the Task 4 drop pattern; lease delta `2`; the Promise stays pending.
- `NullableRecordStructWithLeafRoundTrips`: `slotKind(null)` is `null`; `slotKind({ value: 'a' })` is `"String"`; `echoSlot({ value: o }).value === o`; `echoSlot(null)` is `null`; for the `echoSlot({ value: o })` call, `ReleasedValues` delta equals the delta of `echoTextSlot({ value: 'x' })` plus `1`.
- `MixedRecordRoundTripsBothLeafKinds` (spec scenario "Record with wrapper fields crosses the boundary"; RED: the value field is disposed on encode today): `echoMixed({ buffer: b, value: o })` returns `buffer === b` and `value === o`; lease delta `1`; `ReleasedValues` delta equals the delta of `echoTextMixed({ buffer: 'x', value: 'y' })` plus `1`.
- `ValueListRoundTripsWithoutLeakOrConsumption` (spec: `JavaScriptValue` elements no longer leak on decode or dispose the author's wrapper on encode): `echoValueList([o1, o2])` returns both elements strictly equal; `ReleasedValues` delta equals the delta of `echoTextList(['x', 'y'])` plus `2`.
- `NullRecordInListDecodeFailureReleasesEarlierLeases`: `measurePairs([p, null])` is caught in JS as an `Error`; lease delta `2` (both leaves of `p`).
- `NullRecordInReturnedListKeepsTransferTotal` (spec S2): `pairsWithNull(p)` is caught in JS as an `Error` (`ArgumentNullException` from the record `EncodeBorrowed`); lease delta `2`: the null element registered nothing and the later `p` leaves still registered and released.
- `StoredRecordPropertyReturnsRetainedCopies` (spec scenario "Stored state is returned"): `module.storedPair = { first: b1, second: b2 }`, then read `module.storedPair` twice; both reads return `first === b1` and `second === b2`; a third read still works (the stored record stays usable); lease delta over the set and two reads is `6` (two setter decodes, two transferred copies per read).
- `OmittedOptionalBufferListRegistersNothing` (Review Focus 4): `countOptional()` returns `-1` with lease delta `0`; `countOptional([b])` returns `1` with lease delta `1`.

- [ ] **Step 3: Run the new tests to verify they fail**

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorCompilesScopedComposites|FullyQualifiedName~GeneratorRecordCodecUsesScope|FullyQualifiedName~GeneratorRecordCacheHit|FullyQualifiedName~GeneratorRejectsLeafBearingComposite|FullyQualifiedName~GeneratorKeepsDirectJavaScriptValueCallback|FullyQualifiedName~GeneratorRejectsNestedBufferEventPayloads"`
Expected: FAIL. The `ArrayBuffer` composite rows report `EXPOJSI001`/`002`/`015`/`023`; the `JavaScriptValue` composite rows and records miss the scoped fragments; the callback rows have no diagnostics. The `GeneratorKeepsDirect...` and `GeneratorRejectsNestedBufferEventPayloads` tests pass.

Run: `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter "FullyQualifiedName~ResourceScopeModuleTests"`
Expected: build FAIL with generator errors: `EXPOJSI001` and `EXPOJSI002` for `EchoList` and `EchoMap`, `EXPOJSI001` for `CountOptional`, and `EXPOJSI007` plus `EXPOJSI001`/`EXPOJSI002` for `Shelf` in `EchoShelf`. Put the observed errors in the task report. Before any production edit, dump the leaf fixtures' generated text as in Task 4 Step 3.

- [ ] **Step 4: Implement the model, helpers, and selection rules** listed in Interfaces.

- [ ] **Step 5: Implement the scoped record codec emission** in `EmitRecordCodec`, branching on `codec.ResourceOwnership`.

- [ ] **Step 6: Run the Step 3 commands**
Expected: PASS.

- [ ] **Step 7: Review and re-pin the leaf digests** as in Task 4 Step 7, diffing against a dump taken in Step 3 before any production edit. Expected diff: `PolicyRecord`'s codec becomes scoped, `Record`/`RecordReturn` use the scope, and `Values`/`ValueMap`/`ValuesReturn` use `GeneratedScopedListCodec`/`GeneratedScopedDictionaryCodec`. No leaf-free or `GeneratorPreserves*` digest changes.

- [ ] **Step 8: Run everything**

Run: Generator tests, then ModulesCore tests, then `scripts/test-managed.sh`.
Expected: all pass, 0 skipped.

- [ ] **Step 9: Run the three scans** from Global Constraints. Expected: no output.

- [ ] **Step 10: Commit**

```bash
git commit -m "fix(generator): compose wrapper leaves in records and collections"
```

### Task 6: Close the change

Model: **standard** (spec merge needs judgment; the file moves are mechanical).

**Files:**
- Modify: `docs/specs/ownership-and-scoped-refs.md`, `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`, `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`, `docs/roadmap.md`
- Move: `docs/plans/030-compositional-codec-resource-scope.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-05-compositional-codec-resource-scope/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the spec deltas.** Add the two ADDED requirements to `docs/specs/ownership-and-scoped-refs.md` and the ADDED and MODIFIED requirements to `docs/specs/modules-core-boundary.md`, exactly as the spec's two "Delta against" sections state. Include the ownership matrix and the execution order (sync and the async shapes) in the boundary spec where the ADDED requirement points to "this change's design", because the change folder is archived.

- [ ] **Step 2: Update the authoring guide and the skill.** In `docs/module-authoring-guide.md:143-145`, state that `ArrayBuffer` works directly, as a record field, a list element, and a dictionary value, but not as a nullable inner type, a callback type argument, or a nested typed-event payload. Update the callback type rule: a callback result or argument that holds a leaf inside a record, list, or dictionary is rejected; a direct `JavaScriptValue` stays supported. Update the ownership note: authors store and expose only `Retain()` copies, and a returned value transfers all its leaves. In the skill's "Generated Module Ownership", keep that `JavaScriptValueCodec.Encode` returns the same wrapper, and add that generated glue no longer uses it for boundary returns: it transfers into the conversion scope and returns an independent retained wrapper.

- [ ] **Step 3: Update the roadmap.** Set row 030 to `DONE` with the link `archive/plans/030-compositional-codec-resource-scope.md`. Add a note with the implementation commits and the full-suite count from Step 5. Under "Unplanned backlog / ABI and codecs", add:
  `- **P2 — Callback argument ownership**: invoking a \`JavaScriptCallback\` disposes \`JavaScriptValue\` arguments that the author still owns.`

- [ ] **Step 4: Move the brief and the change folder** with `git mv`. Fix every link that pointed to the old paths (`rg -n '030-compositional-codec-resource-scope|2026-11-05-compositional-codec-resource-scope' docs .agents AGENTS.md`).

- [ ] **Step 5: Verify**

Run: `scripts/test-managed.sh`, `scripts/format.sh --check --all`, `git diff --check`, the three scans in Global Constraints, and the privacy scan in Global Constraints.
Expected: all tests pass with 0 skipped; format OK; no scan output.

- [ ] **Step 6: Commit**

```bash
git commit -m "docs(modules-core): close compositional codec resource scope"
```
