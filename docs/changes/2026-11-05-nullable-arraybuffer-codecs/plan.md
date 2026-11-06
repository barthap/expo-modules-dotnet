# Nullable ArrayBuffer Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `ArrayBuffer?` works on every generated boundary where `ArrayBuffer` works, through the existing `NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>`, and as a direct module or shared-object typed-event payload (roadmap item 031).

**Architecture:** The runtime already has the codec and its `Release` (030). The work is two narrow generator edits that stop rejecting `ArrayBuffer?`, and two emitter overloads that take `ArrayBuffer?` and dispatch an explicit JavaScript `null` for a null payload. The emitters change first, because generated event glue binds to them. Most of the change is tests: generator acceptance and kept rejections, then Hermes-backed lease-counter proofs. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-05-nullable-arraybuffer-codecs/spec.md` (binding authority; reconciled at the 030 tip `e543159`). The brief `docs/plans/031-nullable-arraybuffer-codecs.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: no new codec type when an existing one fits. One execution order per path. No special case for one position.
  2. Performance: null holds no leaf and allocates nothing. Non-null keeps the 030 cost exactly.
  3. Correctness: no leaks. A decoded non-null buffer releases exactly once through `Release`. Null never creates a lease.
- No new runtime types. `NullableReferenceCodec`, `ArrayBufferCodec`, `ArrayBuffer`, `CodecRelease`, and the composite codecs do not change.
- No new diagnostic IDs and no new or changed diagnostic message text.
- Leaf-free digests never move. The only digest that changes is `GeneratorPreservesCodecPolicyDiagnosticContract` (Task 2).
- 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Counter rules: exact-once `ArrayBuffer` assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native **entries**, not leases. Each decode of a JS buffer creates one entry. A `Retain()` copy shares its entry, so a stored copy keeps the count at 0 until its owner disposes it.
- A counter value, digest, or generated fragment that differs from this plan is a finding to report, never a number to adjust until green.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message.
- Commits: conventional subjects. Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR.
- Before each commit: `scripts/format.sh --check --all` passes and `git diff --check` is clean.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1070 passed, 0 skipped at 030 close per `docs/roadmap.md`)                                                                      |

## Review Focus

1. **The non-null form stays strict.** `m.measure(null)` and `m.measure(undefined)` on an `ArrayBuffer` parameter still throw a catchable `Error` before authored code runs, with no lease.
2. **JavaScript omits a nullable record field or passes `undefined` as a list element.** Both decode to C# `null` and release nothing.
3. **JavaScript passes a non-buffer, non-nullish value** (`'x'`, `{}`) to an `ArrayBuffer?` parameter. The call fails with a catchable `Error` and no lease.
4. **A null event payload meets emitter validation** (undeclared event, disposed emitter, cancelled token). It fails exactly like any other payload and never dispatches.
5. **The same buffer fills two `ArrayBuffer?` slots.** Each decode creates its own entry: both release once.

---

### Task 1: Accept null direct ArrayBuffer event payloads

Model: **cheap**. This task lands before Task 2, because the generator test host compiles against the real `Expo.ModulesCore` assembly.

**Files:** `Expo.ModulesCore/ModuleEventEmitter.cs`, `Expo.ModulesCore/SharedObjectEventEmitter.cs`, `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (`CounterEntry`), `Expo.ModulesCore.Tests/Events/ModuleEventEmitterTests.cs`, `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`.

- [ ] **Step 1: Tests first.** The module emitter `onBuffer` listener records arity and handles `null`. Add `NullArrayBufferPayloadDispatchesOneNullArgumentWithoutLease` (expects `"1:null"`, released 0, abandoned 0) and `NullArrayBufferPayloadKeepsEmitterValidation` (undeclared, disposed, cancelled; no dispatch, released 0). Pass `(ArrayBuffer?)null`, because a bare `null` is ambiguous with the `JavaScriptValue` overload. `CounterEntry` gains a non-null `OnBuffer` event; add `SharedObjectArrayBufferEventRetainsLeaseBeforeScheduling` (characterization: `"1:7,8,9"`, released 1) and `SharedObjectNullArrayBufferEventDispatchesOneNullArgument` (`"1:null"`, released 0) through `GeneratedSharedObjectEvents.EmitAsync`.
- [ ] **Step 2: Verify they fail** with `ArgumentNullException` (module) and `NullReferenceException` (shared object).
- [ ] **Step 3: Implement.** Both overloads take `ArrayBuffer?`. Drop the module null guard. Retain with `payload?.Retain()`, and in the dispatch lambda encode `runtime.CreateNull()` when the retained lease is null. Do not pass C# `null` to `SharedObjectEventPrototype.Dispatch`, which treats it as payload-less. Update the `<remarks>`.
- [ ] **Step 4: Run both suites.** Generated text does not change.
- [ ] **Step 5: Commit** `feat(modules-core): dispatch null direct ArrayBuffer event payloads`.

### Task 2: Generator accepts ArrayBuffer?

Model: **standard**.

**Files:** `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (`IsExcludedFromNullableReference`), `Expo.ModulesCore.Generator/ExpoModulesGenerator.EventAnalysis.cs` (`GetUnsupportedEventPayload`), `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`, `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs` (`GeneratorPreservesCodecPolicyDiagnosticContract` only).

`ArrayBuffer?` resolves through the fall-through at the end of `TryGetNullableReferenceCodec` to `NullableReferenceCodec<global::Expo.ModulesCore.ArrayBuffer, ArrayBufferCodec>` (written `NABC` below; write it out in tests). It is a composite over a `HoldsLeaves` child, so the 030 emission declares and releases it with no generator change.

- [ ] **Step 1: Edit the existing rejection tests.** Drop the `ArrayBuffer` row of `GeneratorReportsMethodDiagnosticsForExcludedNullableParametersAndReturns`. In `GeneratorReportsEventDiagnosticsForExcludedNullablePayloads`, the module event becomes `Func<JavaScriptValue?, Task> OnValue`. `GeneratorRejectsNestedNullableLeafEventPayloads` uses `IReadOnlyList<JavaScriptValue?>`.
- [ ] **Step 2: Write the acceptance tests.** Every row asserts no generator diagnostics, the `NABC` text, a fragment, and no compiler errors.
  - Parameters and returns: `NABC.Decode(arguments.GetValue(0), runtime)`; the `= null` default branch; `CodecRelease.Release<global::Expo.ModulesCore.ArrayBuffer?, NABC>(__expoResult, ref __expoReleaseFailures);` for a sync return; `NABC.Encode(__expoResult, runtime)`; the same release of `__expoArg0` for an async leaf-free result; `StrongBox<global::Expo.ModulesCore.ArrayBuffer?>(__expoResult)` for `Task<ArrayBuffer?>`.
  - Properties: getter and setter fragments.
  - Records and collections: record field decode and release, `NullableCodec` over a record struct, `JavaScriptArrayCodec<global::Expo.ModulesCore.ArrayBuffer?, NABC>`, both dictionary forms, `IReadOnlyList<ArrayBuffer?>?`, nested `Shelf` rows, a list of records.
  - Shared-object constructor, method, and property.
  - Direct module and shared-object `Func<ArrayBuffer?, Task>` events bind to the emitter overload; a nested `IReadOnlyList<ArrayBuffer?>` payload uses `EmitAsync<...NABC...>`.
  - Callbacks: `JavaScriptCallback<ArrayBuffer?>` and `JavaScriptCallback<ValueTuple<ArrayBuffer?>, int>` on a module and on a shared object.
- [ ] **Step 3: Write the rejections that stay.** `GeneratorKeepsJavaScriptValueExclusionNextToNullableArrayBuffer`: each `JavaScriptValue?` member, including a nested `IReadOnlyList<JavaScriptValue?>` event, reports its existing code (`EXPOJSI001`, `002`, `015`, `007`+`001`, `019`, `023`, `027`) and the `ArrayBuffer?` member next to it is generated.
- [ ] **Step 4: Verify** the acceptance rows fail with the plan-026 diagnostics, and the kept rejections pass.
- [ ] **Step 5: Edit `IsExcludedFromNullableReference`.** The final test names `JavaScriptValueMetadataName` only.
- [ ] **Step 6: Edit `GetUnsupportedEventPayload`.** Only an annotated `JavaScriptValue` returns the nullable-annotation reason, at any depth. An `ArrayBuffer` needs no branch: a direct payload uses its payload kind, and a nested one resolves through its codec.
- [ ] **Step 7: Re-pin `GeneratorPreservesCodecPolicyDiagnosticContract`.** Only this digest moves; its `OptionalBuffer` `EXPOJSI001` line goes. Any other digest failure is a stop.
- [ ] **Step 8: Run everything** and the 029 scan.
- [ ] **Step 9: Commit** `feat(generator): support nullable ArrayBuffer codecs`.

### Task 3: Prove nullable buffer lifetimes on Hermes

Model: **standard**. Tests and fixtures only; a failure is a finding to report.

**Files:** `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleFixtures.cs`, `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleTests.cs`, `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (`GeneratedTypedEventsModule`, `CounterEntry`), `Expo.ModulesCore.Tests/Events/EventModuleTests.cs`, `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`.

- [ ] **Step 1: Extend the fixtures.** Records `OptionalBufferPair(ArrayBuffer? First, ArrayBuffer? Second)` and `OptionalCountedBuffer(ArrayBuffer? Buffer, int Count)`. `LeafReleaseModule` gains `MeasureOptional`, `MeasureOptionalAfterAwaitAsync`, `MeasureDefaulted(ArrayBuffer? value = null)`, `EchoOptional`, `EchoOptionalAsync`, a stored `OptionalBuffer` property that keeps only `Retain()` copies, `MeasureOptionalPair`, `EchoOptionalPair`, `MeasureOptionalCounted`, `CountPresent(IReadOnlyList<ArrayBuffer?>)`, `EchoOptionalList`, `EchoOptionalMap`, two callback members, `ForwardOptionalBuffer(ArrayBuffer?, JavaScriptCallback<ValueTuple<ArrayBuffer?>, int>)` and `TakeOptionalBuffer(JavaScriptCallback<ArrayBuffer?>)`, which disposes the result, and an `OnOptionalBuffers` event (`Func<IReadOnlyList<ArrayBuffer?>, Task>`) raised by `EmitOptionalBuffers(ArrayBuffer?)`. A new `LeafReleaseOptionalEntry` shared object has an `ArrayBuffer?` constructor, method, and property. The typed-events module and `CounterEntry` gain `OnOptionalBuffer` (`Func<ArrayBuffer?, Task>`). Update the initializer-arity and delegate-identity tests for the new event.
- [ ] **Step 2: Module tests**, each with exact counts: nullish parameter (0), non-null sync and async (1 each, null async 0), authored default (0, then 1), return null (0) and the input buffer (1, identity kept) sync and async, property null (0), stored copy (0) and cleared (1), record fields (1, omitted field 1, echo 1), later field failure (1, then 0), collections (`[b1, null, undefined, b2]` 2, list echo 1, map echo 1, `[b1, b1]` 2), non-buffer values rejected (0), non-null strict (0), shared-object members (2, then 1), callback argument null and buffer (author's buffer stays usable), callback result null and buffer (released once by the author).
- [ ] **Step 3: Event tests.** Module and shared-object `OnOptionalBuffer(null)` deliver `"1:null"` with released 0; a buffer disposed after the call returns is delivered through the retained lease, released 1, abandoned 0. A nested `OnOptionalBuffers` payload `[null, b]` delivers `null` and the buffer, and releases only the author's own decode (1).
- [ ] **Step 4: Run everything.** Record the passed count for Task 4.
- [ ] **Step 5: Commit** `test(modules-core): prove nullable ArrayBuffer lifetimes on Hermes`.

### Task 4: Close the change

Model: **cheap**.

- [ ] **Step 1: Merge the deltas** into `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, and `docs/module-authoring-guide.md`, as the spec states.
- [ ] **Step 2: Roadmap.** Row 031 is `DONE` with no brief link. The 028–033 note says 031 is done, with the date and the passed count, and that 032 and 033 still need a rebase on the 030 design.
- [ ] **Step 3: Remove the brief and this change folder from git.** Fix every link to the old paths.
- [ ] **Step 4: Verify** with the spec's block. The exclusion scan and the privacy scan print nothing.
- [ ] **Step 5: Commit** `docs(modules-core): close nullable ArrayBuffer codecs`.
