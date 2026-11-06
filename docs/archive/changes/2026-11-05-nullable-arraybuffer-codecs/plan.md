# Nullable ArrayBuffer Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `ArrayBuffer?` works on every generated boundary where `ArrayBuffer` works, through the existing `GeneratedScopedNullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>`, and as a direct module or shared-object typed-event payload (roadmap item 031).

**Architecture:** The runtime already has the codec (030). The work is two narrow generator edits that stop rejecting `ArrayBuffer?`, and two emitter overloads that take `ArrayBuffer?` and dispatch an explicit JavaScript `null` for a null payload. The emitters change first, because generated event glue binds to them. Most of the change is tests: generator acceptance and kept rejections, then Hermes-backed lease-counter proofs. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-05-nullable-arraybuffer-codecs/spec.md` (binding authority; reconciled at `e5fdcfc`). `git diff --stat e5fdcfc..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/031-nullable-arraybuffer-codecs.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: no new codec type when an existing one fits. One execution order per path. No special case with hidden data flow.
  2. Performance: null registers nothing and allocates nothing. Non-null keeps the 030 cost exactly.
  3. Correctness: no leaks. The conversion scope stays the only owner of a decoded buffer. Null never reaches the registry, the scope, or a lease.
- No new runtime types. `GeneratedScopedNullableReferenceCodec`, `ArrayBufferCodec`, `ArrayBuffer`, `GeneratedConversionScope`, and the scoped composite codecs do not change.
- No new diagnostic IDs and no new or changed diagnostic message text.
- Leaf-free digests never move. The only digest that changes is `GeneratorPreservesCodecPolicyDiagnosticContract` (Task 2). `GeneratorPreservesDiagnosticOutputContract`, `GeneratorPreservesModuleOutputContract`, `GeneratorPreservesSharedObjectOutputContract`, `GeneratorPreservesNullableCodecOutputContract`, both `GeneratorPreservesLeafFree*` tests, and both `GeneratorPinsLeaf*` tests stay byte-identical.
- 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Counter rules: exact-once `ArrayBuffer` assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native **entries**, not leases. Each decode of a JS buffer creates one entry. A `Retain()` copy shares its entry, so a stored copy keeps the count at 0 until its owner disposes it. Native-backed buffers (`ArrayBuffer.CopyFrom`, `Allocate`) have no counter.
- A counter value, digest, or generated fragment that differs from this plan is a finding to report, never a number to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 e5fdcfc..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1062 passed, 0 skipped at 030 close per `docs/roadmap.md`)                                                                      |

## Review Focus

1. **The non-null form stays strict.** `m.measure(null)` and `m.measure(undefined)` on an `ArrayBuffer` parameter still throw a catchable `Error` before authored code runs, with no lease. Test: Task 3 `NonNullBufferParameterStaysStrict`.
2. **JavaScript omits a nullable record field or passes `undefined` as a list element.** Both decode to C# `null` and register nothing. Test: Task 3 `NullableBufferRecordFieldsRegisterOnlyPresentLeaves` (`{ second: b2 }`) and `NullableBufferCollectionsRegisterOnlyPresentLeaves` (`[b1, null, undefined, b2]`).
3. **JavaScript passes a non-buffer, non-nullish value** (`'x'`, `{}`) to an `ArrayBuffer?` parameter. The call fails with a catchable `Error` and no lease. Test: Task 3 `NullableBufferParameterRejectsNonBufferValues`.
4. **A null event payload meets emitter validation** (undeclared event, disposed emitter, cancelled token). It fails exactly like any other payload and never dispatches. Test: Task 1 `NullArrayBufferPayloadKeepsEmitterValidation`.
5. **The same buffer fills two `ArrayBuffer?` slots.** Each decode creates its own entry: both release once. Test: Task 3 `NullableBufferCollectionsRegisterOnlyPresentLeaves` (`countPresent([b1, b1])`, count 2).

---

### Task 1: Accept null direct ArrayBuffer event payloads

Model: **cheap**.

This task must land before Task 2. The generator test host compiles against the real `Expo.ModulesCore` assembly, so a `Func<ArrayBuffer?, Task>` acceptance row in Task 2 needs the nullable overloads to compile without a CS8604 warning.

**Files:**

- Modify: `Expo.ModulesCore/ModuleEventEmitter.cs:112-139`
- Modify: `Expo.ModulesCore/SharedObjectEventEmitter.cs:26-45`
- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs:543-597` (`CounterEntry`)
- Test: `Expo.ModulesCore.Tests/Events/ModuleEventEmitterTests.cs`, `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`

**Interfaces:**

- Produces:

```csharp
// ModuleEventEmitter
public async Task EmitAsync(object module, string eventName, ArrayBuffer? payload, CancellationToken cancellationToken = default);
// GeneratedSharedObjectEvents
public static async Task EmitAsync(DotnetRuntimeContext context, SharedObject instance, string eventName, ArrayBuffer? payload);
```

- `CounterEntry` gains `[Event] public partial Func<ArrayBuffer, Task> OnBuffer { get; }` (non-null; it compiles today) and `public Task EmitBufferAsync(ArrayBuffer value) => OnBuffer(value);`. Task 3 adds `OnOptionalBuffer` beside it.

- [ ] **Step 1: Change the module emitter listener.** In `ModuleEventEmitterTests.CreateEmitter`, replace the `onBuffer` listener with one that records arity and handles `null`:
      `events.addListener('onBuffer', (...args) => { globalThis.__moduleEventBufferArgs = args.length; globalThis.__moduleEventBuffer = args[0] === null ? 'null' : Array.from(new Uint8Array(args[0])).join(','); });`
      `ArrayBufferPayloadRetainsLeaseBeforeScheduling` still expects `"7,8,9"` and `1u`; it stays a characterization test. The arity is what tells one `null` argument (spec decision 3) apart from a payload-less dispatch.

- [ ] **Step 2: Write the failing module emitter tests** in `ModuleEventEmitterTests.cs`. Pass `(ArrayBuffer?)null`: a bare `null` is ambiguous with the `JavaScriptValue` overload (CS0121).
  - `NullArrayBufferPayloadDispatchesOneNullArgumentWithoutLease`: the pause pattern of `ArrayBufferPayloadRetainsLeaseBeforeScheduling` (`ResetCounters`, `DisableSyncExecutionForTesting`, `PauseRuntimeExecutor`, emit to `"onBuffer"`, `ResumeRuntimeExecutor`, `await`, `WaitUntilIdle`, `SetSyncExecutionSupportedForTesting(true)`). Assert `` `${__moduleEventBufferArgs}:${__moduleEventBuffer}` `` is `"1:null"`, `LongLivedArrayBuffersReleased == 0u`, `LongLivedArrayBuffersAbandoned == 0u`.
  - `NullArrayBufferPayloadKeepsEmitterValidation` (Review Focus 4), theory `(string kind, Type expected)`: `"undeclared"` emits to `"missing"` → `InvalidOperationException`; `"disposed"` disposes `emitter.Context` inside `Runtime.Execute` first, as `DisposedEmitterFaultsBeforeRuntimeScheduling` does → `ObjectDisposedException`; `"cancelled"` passes a token from an already cancelled `CancellationTokenSource` → `OperationCanceledException`. Each row: `Assert.IsAssignableFrom(expected, await Record.ExceptionAsync(() => emitted))`, `typeof globalThis.__moduleEventBufferArgs` is `"undefined"`, and `LongLivedArrayBuffersReleased == 0u`.

- [ ] **Step 3: Write the shared-object emitter tests** in `SharedObjectTests.cs`. Set up as `SharedObjectEventDispatchAwaitsOffRuntimeAndEncodesRecordPayload` does: register the provider, create `new m.CounterEntry(1)` in a global, add an `'onBuffer'` listener that records `args.length + ':' + (args[0] === null ? 'null' : Array.from(new Uint8Array(args[0])).join(','))` into `globalThis.__sharedBuffer`, call `m.echoCounter(...)`, and take `SharedThingsModule.LastSeenCounter`. Create the payload as `ModuleEventEmitterTests` does (`Uint8Array.from([7, 8, 9]).buffer`, then `ArrayBufferCodec.Decode(value.Ref, runtime)`). Dispose the context at the end.
  - `SharedObjectArrayBufferEventRetainsLeaseBeforeScheduling` (characterization; passes before and after): the pause pattern above with `instance.EmitBufferAsync(payload)`, then `payload.Dispose()` before `ResumeRuntimeExecutor()`. Expect `"1:7,8,9"`, `LongLivedArrayBuffersReleased == 1u`, `LongLivedArrayBuffersAbandoned == 0u`.
  - `SharedObjectNullArrayBufferEventDispatchesOneNullArgument`: `ResetCounters()`, `await GeneratedSharedObjectEvents.EmitAsync(context!, instance!, "onBuffer", (ArrayBuffer?)null)`, `WaitUntilIdle()`. Expect `"1:null"` and `LongLivedArrayBuffersReleased == 0u`. The call goes to the static API directly, because the generator still rejects a `Func<ArrayBuffer?, Task>` declaration until Task 2.

- [ ] **Step 4: Run the new tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~ModuleEventEmitterTests|FullyQualifiedName~SharedObjectArrayBufferEvent|FullyQualifiedName~SharedObjectNullArrayBufferEvent"`
Expected: `NullArrayBufferPayloadDispatchesOneNullArgumentWithoutLease` and every `NullArrayBufferPayloadKeepsEmitterValidation` row fail with `ArgumentNullException`. `SharedObjectNullArrayBufferEventDispatchesOneNullArgument` fails with `NullReferenceException`. The existing tests and `SharedObjectArrayBufferEventRetainsLeaseBeforeScheduling` pass. Put the observed failures in the task report.

- [ ] **Step 5: Implement the two overloads.**
  - `ModuleEventEmitter.EmitAsync(..., ArrayBuffer? payload, ...)`: delete `ArgumentNullException.ThrowIfNull(payload)`. When `payload is null`, `await ExecuteEventAsync(runtime => { using var nullValue = runtime.CreateNull(); return Emit(runtime, module, eventName, nullValue); }, cancellationToken)` and return. The non-null path keeps its current text exactly: `using var invocationPayload = payload.Retain();` before `ExecuteEventAsync`. Update the `<remarks>`: a null payload dispatches one JavaScript `null` argument and retains no lease.
  - `GeneratedSharedObjectEvents.EmitAsync(..., ArrayBuffer? payload)`: when `payload is null`, go through `ExecuteAsync` with a dispatch lambda that creates `runtime.CreateNull()` and passes it to `SharedObjectEventPrototype.Dispatch`. Do not pass C# `null` as the `payload` argument: `Dispatch` treats C# `null` as payload-less (`SharedObjectEventPrototype.cs:52-54`). The non-null path keeps its current text.
  - Neither branch touches an `ArrayBuffer` member when the payload is null.

- [ ] **Step 6: Run the Step 4 command.** Expected: PASS.

- [ ] **Step 7: Run both suites.** Run: Generator tests, then ModulesCore tests. Expected: all pass; no generated text changes (generated event glue still reads `onBufferValue => emitter.EmitAsync(module, "onBuffer", onBufferValue)`).

- [ ] **Step 8: Commit**

```bash
git commit -m "feat(modules-core): dispatch null direct ArrayBuffer event payloads" \
  -m "A null payload dispatches one JavaScript null argument through the payload path and retains no lease. Non-null payloads keep the retained invocation lease." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator accepts ArrayBuffer?

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs:177-189` (`IsExcludedFromNullableReference`)
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.EventAnalysis.cs:370-416` (`GetUnsupportedEventPayload`)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`, `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs:423-469` (`GeneratorPreservesCodecPolicyDiagnosticContract` only)

**Interfaces:**

- Consumes: Task 1's `ArrayBuffer?` emitter overloads.
- Produces: `ArrayBuffer?` resolves through the fall-through at the end of `TryGetNullableReferenceCodec` (`Codecs.cs:281-288`) to `ScopedNullableReferenceCodec(valueType, "ArrayBufferCodec")`. Its expression is exactly
  `GeneratedScopedNullableReferenceCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>`
  (first type argument from the de-annotated symbol, codec names unqualified, as in the existing scoped rows at `ExpoModulesGeneratorTests.cs:5033-5047`). Below, `NABC` stands for that exact text; write it out in full in the tests.

Existing tests that assert `ArrayBuffer?` is rejected (the complete list):

- `GeneratorReportsMethodDiagnosticsForExcludedNullableParametersAndReturns` (`ExpoModulesGeneratorTests.cs:4218-4249`), row `[InlineData("ArrayBuffer", ...)]`.
- `GeneratorReportsEventDiagnosticsForExcludedNullablePayloads` (`ExpoModulesGeneratorTests.cs:4339-4373`), module event `Func<ArrayBuffer?, Task> OnBuffer`.
- `GeneratorPreservesCodecPolicyDiagnosticContract` (`GeneratorOutputContractTests.cs:441`), member `OptionalBuffer(ArrayBuffer? value)`.

`GeneratorPreservesDiagnosticOutputContract` has no `ArrayBuffer?` member (its fixture is one static method), so it does not move.

- [ ] **Step 1: Edit the existing rejection tests.**
  - Delete the `ArrayBuffer` row of `GeneratorReportsMethodDiagnosticsForExcludedNullableParametersAndReturns`. The `JavaScriptValue` row stays.
  - In `GeneratorReportsEventDiagnosticsForExcludedNullablePayloads`, change the module event to `[Event] public partial Func<JavaScriptValue?, Task> OnValue { get; }`. Assert the `EXPOJSI019` message contains `OnValue` and `nullable annotation`, and the text does not contain `"onValue"`.

- [ ] **Step 2: Write the failing acceptance tests** in `ExpoModulesGeneratorTests.cs`. Every row asserts `Assert.Empty(GeneratorDiagnosticIds(result))`, `Assert.Contains(NABC, text)`, and `Assert.Contains(fragment, text, StringComparison.Ordinal)`. Rows built from `ModuleSource` also assert `AssertNoCompilerDiagnostics(result)` (errors and warnings; 030 proved those fixtures compile clean). Rows built from `SharedObjectMemberSource` and every event row assert `AssertNoCompilerErrors(result)` plus `Assert.DoesNotContain(result.Diagnostics, d => d.Id is "CS8604" or "CS8625")`, which proves the glue binds to the nullable overloads without depending on warnings outside this change. Fragments are inferred from `Codecs.cs:445`, `Emission.cs:343` and `:696`, and the 030 rows.

`GeneratorAcceptsNullableArrayBufferParametersAndReturns` (theory over `ModuleSource(string.Empty, member)`; member, fragment):

- `[JS] public int M(ArrayBuffer? value) => 0;` → `var __expoArg0 = NABC.Decode(arguments.GetValue(0), runtime, __expoScope);`
- `[JS] public int M(ArrayBuffer? value = null) => 0;` → `arguments.Count <= 0 || arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : NABC.Decode(arguments.GetValue(0), runtime, __expoScope)`
- `[JS] public ArrayBuffer? M() => null;` → `NABC.Transfer(__expoResult, __expoScope);`
- `[JS] public ArrayBuffer? M(ArrayBuffer? value) => value;` → `return NABC.EncodeBorrowed(__expoResult, runtime);`
- `[JS] public Task<int> M(ArrayBuffer? value) => Task.FromResult(0);` → `return global::Expo.JSI.JavaScriptPromiseResult.Resolve(`
- `[JS] public Task<ArrayBuffer?> M() => Task.FromResult<ArrayBuffer?>(null);` → `GeneratedTransferredResult<global::Expo.ModulesCore.ArrayBuffer?, NABC>.Resolve(__expoResult, __expoScope);` (the result type keeps `?`; the codec's first type argument does not)

`GeneratorAcceptsNullableArrayBufferProperties` (theory, same source):

- `[JS] public ArrayBuffer? Buffer { get; set; }` → `var __expoValue = NABC.Decode(arguments.GetValue(0), runtime, __expoScope);`
- `[JS] public ArrayBuffer? Buffer => null;` → `NABC.Transfer(__expoResult, __expoScope);`

`GeneratorAcceptsNullableArrayBufferInRecordsAndCollections` (theory over `ModuleSource(declarations, member)`; declarations, member, fragment):

- `public sealed record Holder(ArrayBuffer? Buffer);`, `[JS] public Holder M(Holder holder) => holder;` → `NABC.Decode(obj.GetProperty("buffer"), runtime, __expoScope)`
- same declarations and member → `NABC.Transfer(value.Buffer, __expoScope);`
- `public readonly record struct Slot(ArrayBuffer? Buffer);`, `[JS] public int M(Slot? slot) => 0;` → `GeneratedScopedNullableCodec<global::Expo.TestModules.Slot, `
- `""`, `[JS] public int M(IReadOnlyList<ArrayBuffer?> values) => 0;` → `GeneratedScopedListCodec<global::Expo.ModulesCore.ArrayBuffer?, NABC>.Decode(arguments.GetValue(0), runtime, __expoScope)`
- `""`, `[JS] public Dictionary<string, ArrayBuffer?> M() => new();` → `GeneratedScopedDictionaryCodec<global::Expo.ModulesCore.ArrayBuffer?, NABC>.Transfer(__expoResult, __expoScope);`
- `""`, `[JS] public int M(IReadOnlyDictionary<string, ArrayBuffer?> values) => 0;` → `GeneratedScopedDictionaryCodec<global::Expo.ModulesCore.ArrayBuffer?, NABC>.Decode(`
- `""`, `[JS] public int M(IReadOnlyList<ArrayBuffer?>? values) => 0;` → `GeneratedScopedNullableReferenceCodec<global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.ArrayBuffer?>, GeneratedScopedListCodec<global::Expo.ModulesCore.ArrayBuffer?, NABC>>`
- `public sealed record Shelf(IReadOnlyList<Dictionary<string, ArrayBuffer?>> Rows);`, `[JS] public Shelf M(Shelf shelf) => shelf;` → `.Transfer(value.Rows, __expoScope);`
- `public sealed record Holder(ArrayBuffer? Buffer);`, `[JS] public int M(IReadOnlyList<Holder> holders) => 0;` → `GeneratedScopedListCodec<global::Expo.TestModules.Holder, HolderCodec_`

`GeneratorAcceptsNullableArrayBufferOnSharedObjectMembers` (theory over `SharedObjectMemberSource(member)`):

- `[JS] public Entry(ArrayBuffer? seed) { }` → `NABC.Decode(arguments.GetValue(0), runtime, __expoScope)`
- `[JS] public int Measure(ArrayBuffer? value) => 0;` → `NABC.Decode(arguments.GetValue(0), runtime, __expoScope)`
- `[JS] public ArrayBuffer? Buffer { get; set; }` → `NABC.Transfer(__expoResult, __expoScope);`

`GeneratorAcceptsDirectNullableArrayBufferEvents` (theory `(bool onSharedObject, string fragment)`, member `[Event] public partial Func<ArrayBuffer?, Task> OnBuffer { get; }` through `ModuleSource` or `SharedObjectMemberSource`). This family asserts the fragment only, not `NABC`, because events do not use codecs:

- `false` → `onBufferValue => emitter.EmitAsync(module, "onBuffer", onBufferValue)`
- `true` → `onBufferValue => global::Expo.ModulesCore.GeneratedSharedObjectEvents.EmitAsync(context, sharedObject, "onBuffer", onBufferValue)`
  Both rows also assert the text does not contain `GeneratedConversionScope`.

- [ ] **Step 3: Write the rejections that stay.**
  - `GeneratorKeepsJavaScriptValueExclusionNextToNullableArrayBuffer` (spec scenario "JavaScriptValue stays excluded"), theory `(string declarations, string valueMember, string bufferMember, bool onSharedObject, string expectedIds, string acceptedJsName)`. The source is `ModuleSource(declarations, valueMember + "\n" + bufferMember)` or `SharedObjectMemberSource(valueMember + "\n" + bufferMember)`. Assert `Assert.Equal(expectedIds.Split(','), GeneratorDiagnosticIds(result))`, no generator diagnostic message contains `global::Expo.ModulesCore.ArrayBuffer`, the text contains `"{acceptedJsName}"` (with quotes), and `AssertNoCompilerErrors(result)`. Rows (expected IDs inferred from the existing exclusion tests):
    - `""`, `[JS] public int TakeValue(JavaScriptValue? value) => 0;`, `[JS] public int TakeBuffer(ArrayBuffer? value) => 0;`, `false`, `EXPOJSI001`, `takeBuffer`
    - `""`, `[JS] public JavaScriptValue? GiveValue() => null;`, `[JS] public ArrayBuffer? GiveBuffer() => null;`, `false`, `EXPOJSI002`, `giveBuffer`
    - `""`, `[JS] public JavaScriptValue? ValueProperty { get; set; }`, `[JS] public ArrayBuffer? BufferProperty { get; set; }`, `false`, `EXPOJSI015`, `bufferProperty`
    - `public sealed record ValueHolder(JavaScriptValue? Value); public sealed record BufferHolder(ArrayBuffer? Buffer);`, `[JS] public int TakeValueHolder(ValueHolder holder) => 0;`, `[JS] public int TakeBufferHolder(BufferHolder holder) => 0;`, `false`, `EXPOJSI007,EXPOJSI001`, `takeBufferHolder`
    - `""`, `[Event] public partial Func<JavaScriptValue?, Task> OnValue { get; }`, `[Event] public partial Func<ArrayBuffer?, Task> OnBuffer { get; }`, `false`, `EXPOJSI019`, `onBuffer`
    - `""`, the `TakeValue`/`TakeBuffer` pair, `true`, `EXPOJSI023`, `takeBuffer`
    - `""`, the `OnValue`/`OnBuffer` event pair, `true`, `EXPOJSI027`, `onBuffer`
  - `GeneratorRejectsNestedNullableBufferEventPayloads` (spec scenario "Nested nullable buffer in a typed event stays rejected"; characterization), theory `(string payload, bool onSharedObject, string expectedId)`. Add an optional `string declarations = ""` parameter to `SharedObjectMemberSource`, emitted after the `namespace` line as `ModuleSource` does. Declarations for every row: `public sealed record BufferHolder(ArrayBuffer? Buffer); public readonly record struct BufferSlot(ArrayBuffer? Buffer);`. Member: `[Event] public partial Func<{payload}, Task> OnPayload { get; }`. Rows: `IReadOnlyList<ArrayBuffer?>` × module `EXPOJSI019` and shared `EXPOJSI027`; `BufferHolder` × module `EXPOJSI019` and shared `EXPOJSI027`; `Dictionary<string, ArrayBuffer?>`, module, `EXPOJSI019`; `BufferSlot?`, module, `EXPOJSI019`. Assert one diagnostic with `expectedId`, its message contains `nested transfer-sensitive wrapper`, the text does not contain `"onPayload"`, and `AssertNoCompilerErrors(result)`.
  - Add rows to `GeneratorRejectsStaticHelperCodecsInGenericPositions` (characterization): `[JS] public int M(JavaScriptCallback<ArrayBuffer?> value) => 0;` → `EXPOJSI008`, and `[JS] public int M(JavaScriptCallback<ValueTuple<ArrayBuffer?>, int> value) => 0;` → `EXPOJSI008`.
  - Add a row to `GeneratorRejectsStaticHelperCodecsOnSharedObjectMembers` (characterization): `[JS] public void Run(JavaScriptCallback<ArrayBuffer?> callback) { }` (its assertion is `EXPOJSI023`).

- [ ] **Step 4: Run the new and edited tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~NullableArrayBuffer|FullyQualifiedName~NullableBufferEventPayloads|FullyQualifiedName~ExcludedNullable|FullyQualifiedName~StaticHelperCodecs"`
Expected: every `GeneratorAccepts*NullableArrayBuffer*` row fails with a generator diagnostic (`EXPOJSI001`, `002`, `007`, `015`, `019`, `023`, or `027`). Every `GeneratorKeepsJavaScriptValueExclusionNextToNullableArrayBuffer` row fails on the ID list (the `ArrayBuffer?` member also reports). `GeneratorRejectsNestedNullableBufferEventPayloads`, the new callback rows, and both edited exclusion tests pass. Put the observed failures in the task report. If a characterization test fails, stop and report.

Before any production edit, dump the generated provider text of the `GeneratorPreservesCodecPolicyDiagnosticContract` fixture to a scratch location outside the repo (temporarily assert the text equals `""` so xUnit prints it, then revert that line). Step 8 diffs against this dump.

- [ ] **Step 5: Edit `IsExcludedFromNullableReference`.** The final test becomes `typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName`. Rewrite the comment above it so it names `JavaScriptValue` and the callback types only. Do not put `ArrayBuffer` and `nullable annotation` on one line anywhere in the generator: the spec's exclusion scan matches that.

- [ ] **Step 6: Edit `GetUnsupportedEventPayload`.** In the branch for `JavaScriptValueMetadataName or ArrayBufferMetadataName`: a nested occurrence returns `"it contains a nested transfer-sensitive wrapper"` as today. At the top level, an annotated `JavaScriptValue` returns `"it applies a nullable annotation to a transfer-sensitive wrapper"` as today, and every other case (non-annotated, or annotated `ArrayBuffer`) returns `null`. Update the comment: an annotated `ArrayBuffer` is the direct `ArrayBuffer` payload kind plus nullability. `TryGetEventDelegatePayload` (`EventAnalysis.cs:328-364`, which the spec calls `ClassifyEventPayload`) and the emission switches do not change.

- [ ] **Step 7: Run the Step 4 command.** Expected: PASS.

- [ ] **Step 8: Re-pin the one moved contract.**

Run: Generator tests. Expected: only `GeneratorPreservesCodecPolicyDiagnosticContract` fails. If any other digest test fails, stop: leaf-free or unrelated text changed.
Dump the fixture's generated text again, diff it against the Step 4 dump, and confirm the diff only adds the `OptionalBuffer` binding (an `optionalBuffer` host function with `NABC.Decode(...)` and its registration). Put the diff summary in the task report. Then copy the new output digest from the failure message, and delete exactly this line from the expected diagnostic list:
`"EXPOJSI001|Error|Parameter 'value' on 'OptionalBuffer' uses unsupported type 'global::Expo.ModulesCore.ArrayBuffer'|13|46|13|51",`
Every other diagnostic line, including its line and column numbers, stays the same: the fixture source does not change.

- [ ] **Step 9: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped.

- [ ] **Step 10: Run the 029 typed-policy scan** from Global Constraints. Expected: no output.

- [ ] **Step 11: Commit**

```bash
git commit -m "feat(generator): support nullable ArrayBuffer codecs" \
  -m "ArrayBuffer? resolves to GeneratedScopedNullableReferenceCodec<ArrayBuffer, ArrayBufferCodec> and is a direct typed-event payload. JavaScriptValue? stays excluded." \
  -m "Re-pin GeneratorPreservesCodecPolicyDiagnosticContract: its OptionalBuffer(ArrayBuffer?) member now compiles, so its EXPOJSI001 line goes and the provider digest gains that binding. No other digest moves." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove nullable buffer lifetimes on Hermes

Model: **standard**.

This task adds tests and fixtures only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleFixtures.cs`
- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (`GeneratedTypedEventsModule` at 444-490, `CounterEntry`)
- Test: `Expo.ModulesCore.Tests/Modules/ResourceScopeModuleTests.cs`, `Expo.ModulesCore.Tests/Events/EventModuleTests.cs`, `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`

**Interfaces:**

- Consumes: Task 1's overloads and `CounterEntry.OnBuffer`; Task 2's generator support.

- [ ] **Step 1: Extend the fixtures.**

In `ResourceScopeModuleFixtures.cs`:

```csharp
public sealed record OptionalBufferPair(ArrayBuffer? First, ArrayBuffer? Second);
public sealed record OptionalCountedBuffer(ArrayBuffer? Buffer, int Count);
```

Add `typeof(ResourceScopeOptionalEntry)` to the `ResourceScopeModule` `Classes` array, a private `ArrayBuffer? storedOptional` field (released in `Dispose` next to `stored`), and these members:

```csharp
[JS] public int MeasureOptional(ArrayBuffer? value) => value?.ByteLength ?? -1;
[JS] public async Task<int> MeasureOptionalAfterAwaitAsync(ArrayBuffer? value) { await Task.Yield(); return value?.ByteLength ?? -1; }
[JS] public int MeasureDefaulted(ArrayBuffer? value = null) => value?.ByteLength ?? -1;
[JS] public ArrayBuffer? EchoOptional(ArrayBuffer? value) => value;
[JS] public async Task<ArrayBuffer?> EchoOptionalAsync(ArrayBuffer? value) { await Task.Yield(); return value; }
// Stores and returns only Retain() copies; the module owns the stored copy until Dispose.
[JS] public ArrayBuffer? OptionalBuffer
{
  get => storedOptional?.Retain();
  set { var copy = value?.Retain(); storedOptional?.Dispose(); storedOptional = copy; }
}
[JS] public int MeasureOptionalPair(OptionalBufferPair pair) => (pair.First?.ByteLength ?? 0) + (pair.Second?.ByteLength ?? 0);
[JS] public OptionalBufferPair EchoOptionalPair(OptionalBufferPair pair) => pair;
[JS] public int MeasureOptionalCounted(OptionalCountedBuffer value) => value.Count;
[JS] public int CountPresent(IReadOnlyList<ArrayBuffer?> values) => values.Count(value => value is not null);
[JS] public IReadOnlyList<ArrayBuffer?> EchoOptionalList(IReadOnlyList<ArrayBuffer?> values) => values;
[JS] public Dictionary<string, ArrayBuffer?> EchoOptionalMap(Dictionary<string, ArrayBuffer?> values) => values;
```

and a shared object (a new class, because a shared object exports one `[JS]` constructor):

```csharp
[ExpoSharedObject("ResourceScopeOptionalEntry")]
public sealed partial class ResourceScopeOptionalEntry : SharedObject
{
  // fields: int seedLength; ArrayBuffer? stored
  [JS] public ResourceScopeOptionalEntry(ArrayBuffer? seed)   // seedLength = seed?.ByteLength ?? -1
  [JS] public int ReadSeed() => seedLength;
  [JS] public int Measure(ArrayBuffer? value) => value?.ByteLength ?? -1;
  [JS] public ArrayBuffer? Buffer { get; set; }               // same Retain()-copy body as OptionalBuffer, over `stored`
  protected override void OnRelease()                         // disposes `stored`, sets it to null
}
```

In `ModuleFixtures.cs`: add `[Event] public partial Func<ArrayBuffer?, Task> OnOptionalBuffer { get; }` to `GeneratedTypedEventsModule` right after `OnBuffer` (keep `OnBuffer` non-null). Add `[Event] public partial Func<ArrayBuffer?, Task> OnOptionalBuffer { get; }` and `public Task EmitOptionalBufferAsync(ArrayBuffer? value) => OnOptionalBuffer(value);` to `CounterEntry`.

The new typed event changes the generated `__ExpoModulesCoreInitializeEvents` arity. In `EventModuleTests.cs`, `GeneratedInitializerRejectsDifferentRuntimeContext` (276-284) passes one lambda per event: add a seventh `static _ => Task.CompletedTask`. In `RegisteringProviderTwicePreservesEveryTypedDelegateIdentity` (249-266), capture `module.OnOptionalBuffer` and add `Assert.Same(optionalBuffer, module.OnOptionalBuffer)`.

- [ ] **Step 2: Write the module tests** in `ResourceScopeModuleTests.cs`. Follow the file's pattern: create `globalThis.b` (`new ArrayBuffer(4)` with `new Uint8Array(b)[0] = 7`) and `b1`/`b2` (4 and 2 bytes) in `Run`. Before each measured call, run `ResetCounters()`. Run `WaitUntilIdle()` before reading `LongLivedArrayBuffersReleased`. Use `StartOutcome`/`WaitForOutcomeAsync` for Promises. All counts are inferred from the entry rule and the 030 tests they mirror.
  - `NullishBufferParameterRegistersNothing` (scenario "Nullish parameter registers nothing"): `[m.measureOptional(null), m.measureOptional(undefined)].join(':')` is `"-1:-1"`; count 0.
  - `NonNullNullableBufferParameterReleasesOnceSyncAndAsync` (scenario "Non-null parameter releases once"): `m.measureOptional(b)` is `"4"`, count 1. `measureOptionalAfterAwaitAsync(b)` gives `"fulfilled:4"`, count 1. `measureOptionalAfterAwaitAsync(null)` gives `"fulfilled:-1"`, count 0.
  - `OptionalNullableBufferUsesAuthoredDefault` (scenario "Optional nullable buffer uses the authored default"): `[m.measureDefaulted(), m.measureDefaulted(undefined), m.measureDefaulted(null)].join(':')` is `"-1:-1:-1"`, count 0. Then `m.measureDefaulted(b)` is `"4"`, count 1.
  - `NullableBufferReturnEncodesNullOrTransfersSyncAndAsync` (scenario "Nullable return and property encode null or transfer"): `[m.echoOptional(null) === null, m.echoOptional(undefined) === null].join(':')` is `"true:true"`, count 0. `globalThis.r = m.echoOptional(b); [r === b, new Uint8Array(r)[0]].join(':')` is `"true:7"`, count 1. `echoOptionalAsync(null)` gives `` `${status}:${value === null}` `` = `"fulfilled:true"`, count 0. `echoOptionalAsync(b)` gives `` `${status}:${value === b}` `` = `"fulfilled:true"`, count 1.
  - `NullableBufferPropertyStoresNullOrRetainedCopy` (same scenario, property half): `String(m.optionalBuffer)` is `"null"`, count 0. `m.optionalBuffer = b; [m.optionalBuffer === b, m.optionalBuffer === b].join(':')` is `"true:true"`, count 0: the stored copy pins the entry, as in `PropertyBufferLeasesReleaseWhenStoredCopyDrops`. `m.optionalBuffer = null; String(m.optionalBuffer)` is `"null"`, count 1: the setter disposed the stored copy.
  - `NullableBufferRecordFieldsRegisterOnlyPresentLeaves` (scenario "Nullable buffer inside a record and a collection"; Review Focus 2): `m.measureOptionalPair({ first: b1, second: null })` is `"4"`, count 1. `m.measureOptionalPair({ second: b2 })` is `"2"`, count 1. `globalThis.r = m.echoOptionalPair({ first: null, second: b2 }); [r.first === null, r.second === b2].join(':')` is `"true:true"`, count 1.
  - `LaterFieldDecodeFailureReleasesEarlierNullableLease` (same scenario, failure half): `m.measureOptionalCounted({ buffer: b, count: 'x' })` is caught in JS as an `Error`, count 1. `{ buffer: null, count: 'x' }` is caught as an `Error`, count 0.
  - `NullableBufferCollectionsRegisterOnlyPresentLeaves` (same scenario; Review Focus 2 and 5): `m.countPresent([b1, null, undefined, b2])` is `"2"`, count 2. `globalThis.r = m.echoOptionalList([null, b1]); [r[0] === null, r[1] === b1].join(':')` is `"true:true"`, count 1. `globalThis.r = m.echoOptionalMap({ a: null, b: b2 }); [r.a === null, r.b === b2].join(':')` is `"true:true"`, count 1. `m.countPresent([b1, b1])` is `"2"`, count 2.
  - `NullableBufferParameterRejectsNonBufferValues` (Review Focus 3): `m.measureOptional('x')` and `m.measureOptional({})` are each caught in JS as an `Error`; count 0.
  - `NonNullBufferParameterStaysStrict` (Review Focus 1): `m.measure(null)` and `m.measure(undefined)` are each caught in JS as an `Error`; count 0.
  - `SharedObjectNullableBufferMembersRegisterOnlyPresentLeaves` (scenario "Shared-object members accept nullable buffers"): `globalThis.e0 = new m.ResourceScopeOptionalEntry(null); globalThis.e1 = new m.ResourceScopeOptionalEntry(b); [e0.readSeed(), e1.readSeed(), e1.measure(null), e1.measure(b)].join(':')` is `"-1:4:-1:4"`, count 2 (the constructor decode and the method decode are two entries). Then `e1.buffer = b; const same = e1.buffer === b; e1.buffer = null; [same, e1.buffer === null].join(':')` is `"true:true"`, count 1.

- [ ] **Step 3: Write the event tests.** Every listener records `args.length + ':' + (args[0] === null ? 'null' : Array.from(new Uint8Array(args[0])).join(','))`. The arity is the assertion that rules out a payload-less dispatch. Payloads come from `CreateJavaScriptBackedArrayBuffer` in `EventModuleTests.cs` (`"7,8,9"`), or the same inline decode in `SharedObjectTests.cs`. These cover spec scenario "Direct nullable ArrayBuffer event delivers null without a lease".
  - `EventModuleTests.TypedNullableArrayBufferEventDeliversOneNullArgumentWithoutLease`: listener on `'onOptionalBuffer'`; `ResetCounters()`; `await typed.Module.OnOptionalBuffer(null)`; `WaitUntilIdle()`. Expect `"1:null"`, `LongLivedArrayBuffersReleased == 0u`, `LongLivedArrayBuffersAbandoned == 0u`.
  - `EventModuleTests.TypedNullableArrayBufferEventReleasesItsScheduledInvocationLeaseExactlyOnce`: the exact sequence of `TypedArrayBufferReleasesItsScheduledInvocationLeaseExactlyOnce` (521-551) with `OnOptionalBuffer`, including `payload.Dispose()` right after the invocation returns and `WaitUntilRuntimeTaskQueued(JavaScriptTaskPriority.Immediate)`. Expect `"1:7,8,9"`, released 1, abandoned 0.
  - `SharedObjectTests.GeneratedSharedObjectNullableBufferEventDeliversOneNullArgument`: the Task 1 setup with the listener on `'onOptionalBuffer'`; `await instance.EmitOptionalBufferAsync(null)`. Expect `"1:null"`, released 0.
  - `SharedObjectTests.GeneratedSharedObjectNullableBufferEventKeepsItsLeaseAfterCallerDisposes`: the pause sequence of `SharedObjectArrayBufferEventRetainsLeaseBeforeScheduling` with `instance.EmitOptionalBufferAsync(payload)` and `payload.Dispose()` before resume. Expect `"1:7,8,9"`, released 1, abandoned 0.

- [ ] **Step 4: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~ResourceScopeModuleTests|FullyQualifiedName~EventModuleTests|FullyQualifiedName~SharedObjectTests"`
Expected: PASS. A failing count or string is a finding to report with the observed value.

- [ ] **Step 5: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped. Record the passed count for Task 4.

- [ ] **Step 6: Commit**

```bash
git commit -m "test(modules-core): prove nullable ArrayBuffer lifetimes on Hermes" \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/031-nullable-arraybuffer-codecs.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-05-nullable-arraybuffer-codecs/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the deltas into `docs/specs/modules-core-boundary.md`**, as the spec's "Delta against `docs/specs/modules-core-boundary.md`" section states. Line numbers are at plan time:
  - `### Requirement: Typed Event Payload Ownership Is Explicit` (911-952): add the nullable paragraph after the direct `ArrayBuffer` paragraph (917-921). Add `#### Scenario: Direct nullable ArrayBuffer event delivers null without a lease` after `#### Scenario: Direct ArrayBuffer owns a scheduling lease` (946-952).
  - `### Requirement: Unsupported Signatures Are Build Diagnostics`, `#### Scenario: Static helper codec is used as a generic codec argument` (1225-1233): the annotated-type clause names `JavaScriptValue?` only.
  - `### Requirement: Generated Bindings Compose Leaves Through One Scope`, table row `` `Nullable<T>` inner, nullable-reference inner `` (1494): the spec's replacement text.
  - `### Requirement: Module-facing ArrayBuffer And Binary Codecs`, table row `` `ArrayBuffer` `` (1635): the spec's constraints text.
  - `### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics` (1868-1984): drop `ArrayBuffer` from the first-paragraph list (1870-1872) and add the pointer sentence; rewrite the typed-event paragraph (1892-1895); in `#### Scenario: Excluded nullable parameter or return reports the method codes` (1905-1911), name `JavaScriptValue?` only and drop `ArrayBufferCodec`.
  - Insert `### Requirement: Nullable ArrayBuffer Is A Scoped Nullable Reference` with its seven scenarios, verbatim from the spec, right before `### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults` (1985).

- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`.** Under `### Requirement: Boundary Leaf Ownership Is Explicit` (202-208), append to the first paragraph: "A decoded `ArrayBuffer?` that is `null` holds no leaf and registers nothing." That requirement has no list of unsupported nullable types (`rg -n 'ArrayBuffer\?' docs/specs/ownership-and-scoped-refs.md` prints nothing at plan time), so the spec's "drops `ArrayBuffer?`" edit has nothing to remove. Say so in the task report.

- [ ] **Step 3: Update `docs/module-authoring-guide.md`.**
  - Binary data bullet (143-150): "`ArrayBuffer` and `ArrayBuffer?` work directly, as a record field, a list element, a dictionary value, and a direct typed-event payload; neither works as a callback type argument or a nested typed-event payload."
  - Unsupported annotated references paragraph (177-180): drop `ArrayBuffer?`.
  - `### Event payload ownership` (298-313): add a bullet: a `Func<ArrayBuffer?, Task>` event delivers C# `null` as one JavaScript `null` argument and retains no lease; a non-null buffer follows the `ArrayBuffer` rule.
    Do not put `ArrayBuffer` and `nullable annotation` on one line.

- [ ] **Step 4: Update `docs/roadmap.md`.** Row 031 (line 56): `DONE`, link `[031](archive/plans/031-nullable-arraybuffer-codecs.md)`; keep the column widths aligned. In the `**028–033**` note (115-134), replace "031–033 then apply their own null policies." with: "031 is done ([change](archive/changes/2026-11-05-nullable-arraybuffer-codecs/spec.md)): it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). `ArrayBuffer?` now works wherever `ArrayBuffer` does, through the existing scoped nullable-reference codec, and as a direct typed-event payload. 032 and 033 then apply their own null policies." Take `<N>` from Step 6.

- [ ] **Step 5: Move the brief and the change folder** with `git mv`. Fix every link to the old paths: `rg -n '031-nullable-arraybuffer-codecs|2026-11-05-nullable-arraybuffer-codecs' docs .agents AGENTS.md`. The only hits left point to `archive/`.

- [ ] **Step 6: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'ArrayBuffer.*(unsupported nullable|nullable annotation)|IsNullable.*ArrayBuffer|JavaScriptValueMetadataName or ArrayBufferMetadataName' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator docs/module-authoring-guide.md
git diff --unified=0 e5fdcfc..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped; the passed count is 1062 plus the test cases added in Tasks 1-3, minus the one deleted `ArrayBuffer` row (put the arithmetic in the report). Format is OK and `git diff --check` is clean. The exclusion scan prints exactly one line, in `ExpoModulesGenerator.EventAnalysis.cs`: the `typeName is JavaScriptValueMetadataName or ArrayBufferMetadataName` check that the nested-wrapper branch still needs. It prints nothing from `ExpoModulesGenerator.Codecs.cs` or the guide. The privacy scan prints nothing.

- [ ] **Step 7: Commit**

```bash
git commit -m "docs(modules-core): close nullable ArrayBuffer codecs" \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
