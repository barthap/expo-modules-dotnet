# Typed Codec Conversion Descriptors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace codec-string policy in the Roslyn generator with a typed `ExpoCodecDescriptor`, make the non-null collection codecs composable, and turn every uncompilable codec shape into valid C# or a generator diagnostic.

**Architecture:** Lock today's generated output with digest tests first. Then change the two runtime collection helpers to `IJavaScriptCodec` structs. Then move the generator to descriptors in two steps (resolution, then models and emission) with no output change. Then add the composition and encode-position rules and the span-return fix. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit, Hermes-backed managed tests.

**Spec:** `docs/changes/2026-10-30-typed-codec-conversion-descriptors/spec.md`

## Global Constraints

- Every shape that compiles today SHALL produce byte-identical generated sources, an identical generator diagnostic list, and an identical compiler diagnostic list (spec "Preservation contract").
- Do not change `IJavaScriptCodec<T>` or any runtime codec other than `JavaScriptArrayCodec<T, TCodec>` and `JavaScriptDictionaryCodec<T, TCodec>` (plus the remarks in the three nullable collection adapters).
- No new diagnostic descriptors and no new message text.
- Production generator code SHALL NOT compare, prefix-match, or parse codec-expression text. The final check is this scan, which must print nothing:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- No runtime hot-path reflection in generated code.
- Do not commit absolute paths, usernames, or machine names. Do not push.
- Match the surrounding code style (2-space indent, file-scoped namespaces, existing naming).
- Before each commit: `scripts/format.sh --check --all` passes (run `scripts/format.sh` if it fails, then check again) and `git diff --check` is clean.

## Commands

| Purpose | Command |
| --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` |
| One test | append `--filter "FullyQualifiedName~<TestName>"` to either command |
| Full managed suite | `scripts/test-managed.sh` (920 passed before this change) |

## Review Focus

1. A shape that compiles today and whose output changes without a digest test noticing (for example an `IReadOnlyDictionary` parameter, a nullable collection inside a record, an async `JavaScriptValue` parameter next to a shared-object receiver). Task 1's fixtures must reach every resolver branch and every emission branch.
2. The owned `Decode(JavaScriptValue)` / `DecodeToArray(JavaScriptValue)` overloads leaking or double-disposing element wrappers when an element decode throws. Task 2 tests a throwing element.
3. `IReadOnlyDictionary<string, T>` used as a generic argument binding to the wrong interface implementation. Task 2 tests both dictionary interfaces through a generic helper.
4. A shared-object class member hitting the new rules: the diagnostic must be translated to `EXPOJSI023`. Task 5 covers one shared-object method row.
5. The span-return context fix running outside the host-function frame. Task 5 checks the emitted text and that the lambda is the synchronous `WithBytes`/`WithReadOnlyBytes` call.

---

### Task 1: Lock today's generated output

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs`

**Interfaces:**
- Produces: digest tests that later tasks must keep green without editing their expected values.

This task changes tests only. No production file may change in this task.

- [ ] **Step 1: Write the characterization tests**

Add these `[Fact]` tests, each in the style of the existing `GeneratorPreservesModuleOutputContract` (one `GeneratorTestHost.Run` source, then `Assert.Equal(new[] { "<hint>:<digest>", ... }, result.GetOutputContract())` and an assertion on `result.GetDiagnosticContract()`). Every fixture must compile today with no compiler errors; assert that with `Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal))`.

- `GeneratorPreservesCodecPolicyModuleOutputContract`: one module with
  - sync parameters: `int`, `string`, an enum, `Guid`, `byte[]`, `Memory<byte>`, `IReadOnlyList<int>`, `Dictionary<string, int>`, `IReadOnlyDictionary<string, string>`, `int?`, a positional record with an `ArrayBuffer` field and a `JavaScriptValue` field, `JavaScriptValue`, `ArrayBuffer`, `JavaScriptCallback<int>`, `JavaScriptCallback<ValueTuple<string>, string>`, an optional `int` with a default;
  - async (`Task`/`Task<T>`) methods with `JavaScriptValue` and `ArrayBuffer` parameters and with `JavaScriptValue`, `ArrayBuffer`, `IReadOnlyList<int>` results;
  - sync returns of `JavaScriptValue`, `ArrayBuffer`, `IReadOnlyList<int>`, a record, `Span<byte>`;
  - a span-parameter function that returns `ArrayBuffer`, one that returns `JavaScriptValue`, and one that returns `string`;
  - read/write properties of `ArrayBuffer`, `JavaScriptValue`, `IReadOnlyList<int>`, `string`;
  - typed events with a record payload, a `JavaScriptValue` payload, and an `ArrayBuffer` payload.
- `GeneratorPreservesCodecPolicySharedObjectOutputContract`: one `[ExpoSharedObject]` class with a `[JS]` constructor taking `string`, `JavaScriptValue`, `JavaScriptCallback<int>`, and another shared-object class; methods returning the other shared-object class sync and async; a method taking a shared object; a property of the other shared-object type; an event with a record payload; plus a module that owns both classes and has a method returning one.
- `GeneratorPreservesNullableCodecOutputContract`: `#nullable enable` module with `string?`, `Uri?`, `byte[]?`, a nullable record, `IReadOnlyList<string?>?`, `Dictionary<string, int?>?`, `IReadOnlyDictionary<string, string>?`, and `IReadOnlyList<IReadOnlyList<int>?>` on parameters, returns, async results, and properties.
- `GeneratorPreservesCodecPolicyDiagnosticContract`: sources that produce generator diagnostics today without compiler errors in generated code. Include `JavaScriptValue?`, `ArrayBuffer?`, `JavaScriptCallback<int>?` parameters, `SharedObject?` parameter, `IReadOnlyList<SharedObject>` parameter, `List<int>` parameter, a property of type `JavaScriptCallback<int>`, two span parameters, and an async span parameter. Assert the full `GetDiagnosticContract()` list.

Capture each expected digest list by running the test once with an empty expectation and copying the actual values from the failure message. Do this before any production change. Never edit these expected values in later tasks.

- [ ] **Step 2: Run the new tests**

Run: Generator tests with `--filter "FullyQualifiedName~GeneratorPreserves"`
Expected: all pass, including the 3 existing `GeneratorPreserves*` tests.

- [ ] **Step 3: Run the generator suite**

Run: Generator tests
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs
git commit -m "test(generator): lock codec policy output before descriptors"
```

The commit body lists the invariants: generated C# and diagnostics stay the same for compiling shapes; codec expressions become opaque emission data.

### Task 2: Make the non-null collection codecs composable

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptDictionaryCodec.cs`
- Modify (remarks only): `NullableReadOnlyListCodec.cs`, `NullableDictionaryCodec.cs`, `NullableReadOnlyDictionaryCodec.cs` in the same folder
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Codecs/ArrayCodecTests.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` and a new `Modules/NestedCollectionModuleTests.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**
- Produces:
  - `public readonly struct JavaScriptArrayCodec<T, TCodec> : IJavaScriptCodec<IReadOnlyList<T>> where TCodec : IJavaScriptCodec<T>` with existing `T[] DecodeToArray(JavaScriptValueRef, JavaScriptRuntime)` and `JavaScriptValue Encode(IReadOnlyList<T>, JavaScriptRuntime)`, plus new `T[] DecodeToArray(JavaScriptValue, JavaScriptRuntime)`, `IReadOnlyList<T> Decode(JavaScriptValueRef, JavaScriptRuntime)`, `IReadOnlyList<T> Decode(JavaScriptValue, JavaScriptRuntime)`.
  - `public readonly struct JavaScriptDictionaryCodec<T, TCodec> : IJavaScriptCodec<Dictionary<string, T>>, IJavaScriptCodec<IReadOnlyDictionary<string, T>> where TCodec : IJavaScriptCodec<T>` with existing `DecodeToDictionary` overloads and `Encode(IReadOnlyDictionary<string, T>, JavaScriptRuntime)`, plus public `Dictionary<string, T> Decode(JavaScriptValueRef, ...)` and `Decode(JavaScriptValue, ...)`. Explicit implementations: `IJavaScriptCodec<IReadOnlyDictionary<string, T>>.Decode` (both overloads) and `IJavaScriptCodec<Dictionary<string, T>>.Encode`, each delegating to the public method.

- [ ] **Step 1: Write the failing runtime codec tests** in `ArrayCodecTests.cs` (Hermes fixture, as the existing tests there):

- `ArrayCodecImplementsJavaScriptCodecForNestedLists`: through a generic helper `RoundTrip<T, TCodec>(T value, JavaScriptRuntime runtime) where TCodec : IJavaScriptCodec<T>` that encodes then decodes both through the `JavaScriptValueRef` and owned `JavaScriptValue` overloads, round-trip `[[1, 2], [], [3]]` with `TCodec = JavaScriptArrayCodec<IReadOnlyList<int>, JavaScriptArrayCodec<int, NumberCodec<int>>>`. Assert equal sequences.
- `DictionaryCodecImplementsBothDictionaryInterfaces`: round-trip `{ a: 1, b: 2 }` through `RoundTrip<Dictionary<string, int>, JavaScriptDictionaryCodec<int, NumberCodec<int>>>` and through `RoundTrip<IReadOnlyDictionary<string, int>, JavaScriptDictionaryCodec<int, NumberCodec<int>>>`.
- `ArrayCodecOwnedDecodeDisposesElementWrappers`: decode an owned array value of 3 strings with `JavaScriptArrayCodec<string, StringCodec>.Decode(JavaScriptValue, ...)`, then assert the runtime's live owned-handle count (use the counter the existing ownership tests use; find it with `rg -n "LiveHandle|OwnedHandleCount|HandleCount" packages/expo-modules-dotnet/managed/packages`) returns to its value before the decode.
- `ArrayCodecOwnedDecodeReleasesWrappersWhenElementThrows`: decode `[1, "x"]` with `NumberCodec<int>` elements through the owned overload; assert the codec's exception type and that the live owned-handle count returns to its starting value.

If no live-handle counter exists, rule on it in the ledger and assert disposal with the pattern the existing `DecodeToDictionary(JavaScriptValue)` tests use.

- [ ] **Step 2: Write the failing generated-binding tests**

In `ModuleFixtures.cs` add `public record CollectionHolder(IReadOnlyList<int> Values, Dictionary<string, string> Labels);` and `[ExpoModule("GeneratedNestedCollections")]` with:
- `IReadOnlyList<IReadOnlyList<int>> Grid(IReadOnlyList<IReadOnlyList<int>> grid)` returning its input;
- `IReadOnlyDictionary<string, IReadOnlyList<string>> Groups(IReadOnlyDictionary<string, IReadOnlyList<string>> groups)` returning its input;
- `CollectionHolder Holder(CollectionHolder holder)` returning its input;
- `int CountAll(JavaScriptCallback<IReadOnlyList<int>> source)` returning the sum of the callback's result;
- an `[Event] Func<IReadOnlyList<int>, Task> Changed` and a `[JS] Task Raise(IReadOnlyList<int> values)` that awaits `Changed(values)`.

In `NestedCollectionModuleTests.cs`, register the module as the other module tests do and assert from JavaScript:
- `Grid([[1,2],[],[3]])` returns `[[1,2],[],[3]]` (compare with `JSON.stringify`);
- `Groups({a:["x"],b:[]})` returns the same object;
- `Holder({values:[1,2],labels:{k:"v"}})` returns the same object;
- `CountAll(() => [1,2,3])` returns `6`;
- a listener on `Changed` receives `[4,5]` after `Raise([4,5])`.

In `ExpoModulesGeneratorTests.cs` add `GeneratorCompilesNonNullCollectionsInComposedPositions` (theory over: nested list parameter, nullable list of non-null lists, `Dictionary<string, IReadOnlyList<int>>`, `IReadOnlyList<Dictionary<string, int>>` return, record with `IReadOnlyList<int>` field, record with `Dictionary<string, int>` field, callback with list result, callback with list argument, `[Event]` with `IReadOnlyList<int>` payload on a module and on a shared object): assert no generator diagnostics and no compiler errors.

- [ ] **Step 3: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~ArrayCodec|FullyQualifiedName~DictionaryCodec|FullyQualifiedName~NestedCollection"`, then Generator tests with `--filter "FullyQualifiedName~GeneratorCompilesNonNullCollections"`
Expected: the ModulesCore project fails to build (CS0718 or CS0117 from the new codec tests and from the generated `GeneratedNestedCollections` binding); the generator theory fails with the same compiler errors. Put the observed errors in your report.

- [ ] **Step 4: Implement the two structs** with the signatures in Interfaces. The owned overloads use `using` on the array or object wrapper and on each element wrapper, as `DecodeToDictionary(JavaScriptValue)` does today. Update the three nullable-adapter remarks: they no longer say the helpers lack `IJavaScriptCodec<T>`; they say a nullable container needs nullish handling that the non-null codecs do not have.

- [ ] **Step 5: Run the new tests**

Run: the two Step 3 commands
Expected: PASS.

- [ ] **Step 6: Run both suites**

Run: Generator tests, then ModulesCore tests.
Expected: all pass. Task 1's digest tests pass unchanged.

- [ ] **Step 7: Commit**

```bash
git commit -m "fix(modules-core): make non-null collection codecs composable"
```

### Task 3: Introduce the descriptor and resolve codecs to it

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoCodecDescriptor.cs`
- Modify: `ExpoModulesGenerator.Codecs.cs`, `ExpoModulesGenerator.SharedObjectValidation.cs`, `ExpoModulesGenerator.ModuleAnalysis.cs`, `ExpoModulesGenerator.SharedObjectModel.cs`, `ExpoModulesGenerator.EventAnalysis.cs` (all in the generator project)

**Interfaces:**
- Produces:

```csharp
internal enum ExpoCodecShape { JavaScriptCodec, StaticHelper }
internal enum ExpoCodecCapabilities { DecodeOnly, DecodeAndEncode }
internal enum ExpoDecodeOperation { Decode, DecodeToArray, DecodeToDictionary }
internal enum ExpoContextRequirement { None, DotnetRuntimeContext }
internal enum ExpoDecodedOwnership { Borrowed, InvocationOwned, ContextRetained }
internal enum ExpoEncodeOperation { Standard, TransferJavaScriptValue, EncodeThenDisposeArrayBuffer }

internal sealed record ExpoCodecDescriptor(
    string Expression,
    ExpoCodecShape Shape,
    ExpoCodecCapabilities Capabilities,
    ExpoDecodeOperation DecodeOperation,
    ExpoContextRequirement ContextRequirement,
    ExpoDecodedOwnership DecodedOwnership,
    ExpoEncodeOperation EncodeOperation);
```

- `GetCodecExpression` is renamed `GetCodec` and returns `ExpoCodecDescriptor?`. `TryGetNullableCodec`, `TryGetNullableReferenceCodec` (its `out` becomes `ExpoCodecDescriptor? codec`), `TryGetReadOnlyListCodec`, `TryGetDictionaryCodec`, `TryGetRecordCodec`, and `TryGetJavaScriptCallbackCodec` return descriptors. `TryGetValueTupleArgsCodec` keeps returning `string?` (spec: it is never a boundary codec).
- `TryAnalyzeSharedObjectBoundaryType(..., out ExpoCodecDescriptor? codec)`.
- A private static factory for the common case is allowed only if each caller passes the fields explicitly or it is named for one branch (for example `OrdinaryCodec(string expression)` for the first row of the spec's table). No factory may inspect `expression`.

This task keeps the models unchanged. Call sites in analysis build the existing model fields from the descriptor: `CodecExpression = codec.Expression`, `RequiresRuntimeContext = codec.ContextRequirement == ExpoContextRequirement.DotnetRuntimeContext`, `OwnsDecodedValue = codec.DecodedOwnership == ExpoDecodedOwnership.InvocationOwned`, and the function's `ReturnRequiresRuntimeContext`/`AsyncResultRequiresRuntimeContext` from the return and async-result descriptors. Delete `IsSharedObjectCodecExpression`. The decode-method selection in `GetDecodeExpression` stays string-based until Task 4.

- [ ] **Step 1: Write the descriptor tests**

The descriptor is internal. If the generator project already exposes internals to its test project (`rg -n InternalsVisibleTo packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`), add `ExpoCodecDescriptorTests` asserting one descriptor per row of the spec's "Where each fact is set" table through `GetCodec` on a compiled symbol. If internals are not visible, do not add `InternalsVisibleTo`; Task 1's digests are the gate, and you note this in the report.

- [ ] **Step 2: Implement the descriptor and the resolver changes**, setting each field in its branch exactly as the spec's table says. Record and nullable wrappers are `JavaScriptCodec`/`Borrowed`/`Standard`/`Decode`. The list branch sets `DecodeToArray`; the dictionary branch sets `DecodeToDictionary`; nullable collection adapters set `Decode`.

- [ ] **Step 3: Run**

Run: Generator tests
Expected: all pass, Task 1 digests unchanged.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(generator): resolve codecs to typed descriptors"
```

### Task 4: Store descriptors on models and emit from their fields

**Files:**
- Modify: `ExpoModuleModel.cs`, `ExpoSharedObjectModel.cs` (if needed), `ExpoModulesGenerator.Codecs.cs`, `ExpoModulesGenerator.ModuleAnalysis.cs`, `ExpoModulesGenerator.SharedObjectModel.cs`, `ExpoModulesGenerator.EventAnalysis.cs`, `ExpoModulesGenerator.Emission.cs` (generator project)

**Interfaces:**
- Consumes: Task 3's descriptor and resolvers.
- Produces (model fields, spec "Model changes"):
  - `ExpoParameterModel(string Name, string TypeName, ExpoCodecDescriptor? Codec, bool HasDefaultValue, string DefaultValueExpression, ExpoParameterPassingKind PassingKind = ExpoParameterPassingKind.Codec)`; `Codec` is null only for span parameters.
  - `ExpoFunctionModel`: `ReturnCodec` and `AsyncResultCodec` of type `ExpoCodecDescriptor?` replace `ReturnCodecExpression`, `AsyncResultCodecExpression`, `ReturnRequiresRuntimeContext`, `AsyncResultRequiresRuntimeContext`. Move the existing comment about async-result context capture to the new field.
  - `ExpoPropertyModel(..., string TypeName, ExpoCodecDescriptor Codec, bool HasSetter)`.
  - `ExpoGeneratedRecordFieldModel(..., string TypeName, ExpoCodecDescriptor Codec, Location? Location)`.
  - `ExpoEventModel`: `ExpoCodecDescriptor? Codec` replaces `CodecExpression`; null when `PayloadKind != Codec`.
  - `GetDecodeExpression(ExpoCodecDescriptor codec, int index, string runtimeParameterName)` picks the method from `DecodeOperation` and the context argument from `ContextRequirement`.

- [ ] **Step 1: Migrate the models and every construction site.** Delete the replaced fields; let the compiler find every use.

- [ ] **Step 2: Migrate emission.** Return, async-result, getter, and span-return branches switch on `EncodeOperation`. `using var` and dispose lists read `DecodedOwnership == InvocationOwned`. Context arguments read `ContextRequirement`. The span-return branch must keep emitting no context argument in this task (the fix is Task 5), so output stays identical. Record emission keeps `.Decode`.

- [ ] **Step 3: Run the string-policy scan** from Global Constraints.
Expected: no output.

- [ ] **Step 4: Run**

Run: Generator tests, then the full managed suite.
Expected: all pass; Task 1 digests unchanged; the full suite count is 920 plus the tests added in Tasks 1–3.

- [ ] **Step 5: Commit**

```bash
git commit -m "refactor(generator): model codec conversion policy"
```

### Task 5: Enforce composition and encode rules; fix span returns

**Files:**
- Modify: `ExpoModulesGenerator.Codecs.cs`, `ExpoModulesGenerator.ModuleAnalysis.cs`, `ExpoModulesGenerator.Emission.cs` (generator project)
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**
- Consumes: Task 4 models.

- [ ] **Step 1: Write the failing tests.** For each test, assert the exact generator diagnostic IDs (in order, from `GetDiagnosticContract()` or the generator diagnostics list) and `Assert.DoesNotContain(result.Diagnostics, d => d.Id.StartsWith("CS", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error)`.

- `GeneratorRejectsCallbackAtEncodePositions` (theory): `JavaScriptCallback<int> M()` → `EXPOJSI002`; `Task<JavaScriptCallback<int>> M()` → `EXPOJSI002`; `JavaScriptCallback<int> M(ReadOnlySpan<byte> data)` → `EXPOJSI002`.
- `GeneratorRejectsStaticHelperCodecsInGenericPositions` (theory, module members): `IReadOnlyList<JavaScriptCallback<int>>` parameter → `EXPOJSI001`; `Dictionary<string, JavaScriptCallback<int>>` parameter → `EXPOJSI001`; `JavaScriptCallback<ValueTuple<JavaScriptCallback<int>>, int>` parameter → `EXPOJSI008`; `JavaScriptCallback<JavaScriptCallback<int>>` parameter → `EXPOJSI008`; `IReadOnlyList<ArrayBuffer>` parameter → `EXPOJSI001`; `IReadOnlyList<ArrayBuffer>?` parameter (`#nullable enable`) → `EXPOJSI001`; `Dictionary<string, ArrayBuffer>` parameter → `EXPOJSI001`; `IReadOnlyDictionary<string, ArrayBuffer>` return → `EXPOJSI002`; `IReadOnlyList<ArrayBuffer>` property → `EXPOJSI015`; `JavaScriptCallback<ValueTuple<ArrayBuffer>, int>` parameter → `EXPOJSI008`; `JavaScriptCallback<ArrayBuffer>` parameter → `EXPOJSI008`.
- `GeneratorRejectsStaticHelperCodecsOnSharedObjectMembers` (theory): an `[ExpoSharedObject]` method parameter `IReadOnlyList<ArrayBuffer>` and a `[JS]` constructor parameter `IReadOnlyList<JavaScriptCallback<int>>` → `EXPOJSI023` each.
- `GeneratorRejectsRecordFieldsThatNeedARuntimeContext` (theory): record with a `JavaScriptCallback<int>` field as a parameter → `EXPOJSI007`, `EXPOJSI001`; same record as a return → `EXPOJSI007`, `EXPOJSI002`; record with an `IReadOnlyList<ArrayBuffer>` field as a parameter → `EXPOJSI007`, `EXPOJSI001`; record with a callback field as a property type → `EXPOJSI015` only.
- `GeneratorPassesRuntimeContextToSpanFunctionSharedObjectReturns`: a module that owns `[ExpoSharedObject] sealed partial class Entry : SharedObject` and has `[JS] Entry Make(ReadOnlySpan<byte> data)`. Assert no compiler errors and that the generated provider contains `SharedObjectCodec<global::Expo.TestModules.Entry>.Encode(module.Make(__expoArg0), runtime, GeneratedFunction.CurrentRuntimeContext)` inside a `WithReadOnlyBytes(` call. Read the exact local names from the current span emission (`EmitSpanHostFunction`) and keep them.

- [ ] **Step 2: Run them**

Run: Generator tests with `--filter "FullyQualifiedName~GeneratorRejectsCallbackAtEncodePositions|FullyQualifiedName~GeneratorRejectsStaticHelper|FullyQualifiedName~GeneratorRejectsRecordFields|FullyQualifiedName~GeneratorPassesRuntimeContextToSpan"`
Expected: FAIL. The failure messages show today's CS0117, CS0718, or CS7036 errors (the property row of the record theory may already pass; that row pins existing behavior). Put the observed errors in your report.

- [ ] **Step 3: Implement the spec's composition rules.**
  - Generic position: in the list, dictionary, `Nullable<T>`, nullable-reference (value, list element, dictionary value), callback result, and callback-argument-tuple resolvers, return no codec when the inner descriptor's `Shape != ExpoCodecShape.JavaScriptCodec`.
  - Record field: in `TryGetRecordCodec`, treat a field descriptor with `Capabilities != DecodeAndEncode` or `ContextRequirement != None` as unsupported (the existing `EXPOJSI007` branch).
  - Encode position: in module analysis, a sync return, async result, or span return descriptor with `Capabilities == DecodeOnly` reports `EXPOJSI002` through the existing unsupported-return branch.
  - Span fix: the span-return `Standard` branch passes `, GeneratedFunction.CurrentRuntimeContext` when `ContextRequirement == DotnetRuntimeContext`.

- [ ] **Step 4: Run**

Run: the Step 2 filter, then Generator tests, then ModulesCore tests.
Expected: all pass; Task 1 digests unchanged; Task 2 collection tests still pass.

- [ ] **Step 5: Run the string-policy scan.** Expected: no output.

- [ ] **Step 6: Commit**

```bash
git commit -m "fix(generator): reject uncompilable codec compositions"
```

### Task 6: Close the change (controller)

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/029-typed-codec-conversion-descriptors.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-10-30-typed-codec-conversion-descriptors/` → `docs/archive/changes/`

- [ ] **Step 1:** Merge every delta in the spec's "Delta against `docs/specs/modules-core-boundary.md`" section into the living spec, and the two "Module authoring guide" notes into the guide.
- [ ] **Step 2:** Set row 029 to `DONE`, link the archived brief, and add a note with the implementation commits and the full-suite count. Add to the 030 note that nested non-null collections now compose, so 030 builds on them.
- [ ] **Step 3:** Run `scripts/test-managed.sh`, `scripts/format.sh --check --all`, `git diff --check`, the string-policy scan, and the privacy scan `git diff main...HEAD | rg -n '/Users/|~/dev|<username>'`.
Expected: all tests pass with 0 skipped; format OK; no scan output.
- [ ] **Step 4:** Commit `docs(generator): close typed codec conversion descriptors`.
