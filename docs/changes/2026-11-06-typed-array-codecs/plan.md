# Uint8Array Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Uint8Array` crosses the generated boundary in every codec position where `ArrayBuffer` does, and as a union member of kind `Object`. A JavaScript `Uint8Array` decodes as a view over one retained buffer lease with its offset and length and no byte copy; a managed view encodes as a new JavaScript `Uint8Array` over the same bytes. No ABI change (roadmap item 036).

**Architecture:** One managed type, `Uint8Array`, owns one `ArrayBuffer` lease plus an offset and a length and slices every byte access. One ordinary leaf codec, `Uint8ArrayCodec`, has the `ArrayBufferCodec` shape: it proves the kind by prototype-chain membership (`Uint8Array.prototype.isPrototypeOf(value)`, the `instanceof` test), reads `buffer`, `byteOffset`, and `byteLength`, encodes through the global constructor, and disposes the view in `Release`. The generator gains one `HoldsLeaves` row and one union kind-table row; every other position composes through the existing descriptors and emission. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-06-typed-array-codecs/spec.md` (binding authority). Base commit is `b7966f3` (the 035 close); `git diff --stat b7966f3..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/036-typed-array-codecs.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Notes on the spec

- **No new generator gate and no new emission.** The codec is an ordinary `JavaScriptCodec` with `HoldsLeaves`, so returns, async results, properties, records, lists, dictionaries, nullable forms, callback type arguments, and typed-event payloads accept it through the existing checks (spec decision 6). `ExpoCodecDescriptor.cs`, `ExpoModulesGenerator.Emission.cs`, `ModuleAnalysis.cs`, and `EventAnalysis.cs` do not change. A direct `Uint8Array` event payload takes the `Codec` payload kind, because `TryGetEventDelegatePayload` names only `JavaScriptValue` and `ArrayBuffer`.
- **Decode validates the three fields with the same message.** A real `Uint8Array` always passes. An object that passes the kind test but carries its own fields gets `Value is not a Uint8Array.` when a field has the wrong kind or is not an integer in `[0, int.MaxValue]`, and `ArgumentOutOfRangeException` from the view constructor when the numbers do not fit the buffer. Both fail before any byte access, and the lease is disposed.
- **After `Dispose`**, every view member except `Dispose` throws `ObjectDisposedException`, including `Buffer`, `ByteOffset`, and `Length`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one managed view type, one leaf codec, one generator row, one kind-table row. No typed-array kind enum.
  2. Performance: the kind proof is three property reads and one call per decode; no cached handle. Decode then reads `buffer` and two numbers and retains the backing buffer; no copy. Encode encodes the buffer and calls the constructor once. Leaf-free generated text does not change.
  3. Correctness: the view's bytes are a slice of the buffer's bytes under the buffer's captured-length check. The view is released exactly once through `Release`.
- New production files (exactly two): `Expo.ModulesCore/Uint8Array.cs`, `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs`. Edited production files: `Expo.ModulesCore/Codecs/IJavaScriptCodec.cs` (the leaf doc comment) and `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (one constant, one branch, one kind-table row).
- No ABI or native change (spec decision 1). `git diff --stat b7966f3..HEAD -- packages/expo-modules-dotnet/native` and `rg -n 'Uint8Array' packages/expo-modules-dotnet/native` print nothing.
- The kind message is exact: `Value is not a Uint8Array.`, thrown as `InvalidOperationException`. Hermes tests use `Contains`, never equality.
- `byte[]`, `ArrayBuffer`, `Memory<byte>`, and `ReadOnlyMemory<byte>` keep rejecting a `Uint8Array` (spec decision 5).
- No existing test changes and no output-contract digest moves. A moved `GeneratorPreserves*` or `GeneratorPins*` digest is a finding: stop and report.
- 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Owned-wrapper rule (`expo-jsi-managed-handle-lifetime` skill): every owned intermediate is a `using` local.
- Counter rules (from 031): exact-once lease assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native entries; a `Retain()` copy shares its entry. A native-backed buffer is not counted; tests never assert a count for one.
- Hermes caveat: on the pinned Hermes, `jsi::Runtime::tryGetMutableBuffer` returns null, so a native-backed buffer that returns from JavaScript decodes as a JavaScript-backed lease. Tests that round-trip a native view assert no count and say why in a comment. No local mitigation.
- A count, message, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green.
- Privacy scan: `git diff --unified=0 b7966f3..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects. Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts.
- Before each commit: `scripts/format.sh --check --all` passes and `git diff --check` is clean.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1365 passed, 0 skipped at `b7966f3`)                                                                                            |

Test-case arithmetic (each `InlineData` row is one case): Task 1 adds 30, Task 2 adds 39, Task 3 adds 17, nothing is deleted. The full suite reads 1395 after Task 1, 1434 after Task 2, and 1451 after Task 3.

## Review Focus

1. **JavaScript passes a subclass of `Uint8Array`**, such as the React Native `Buffer` polyfill. The prototype-chain test accepts it, and it decodes like a plain `Uint8Array`.
2. **A spoofed or shadowed view.** `Object.create(Uint8Array.prototype)` with own `buffer`, `byteOffset`, and `byteLength` is accepted and bounds-checked. A shadowed field past the buffer, a fractional offset, or a non-buffer `buffer` fails loudly and leaks no lease.
3. **Zero-length views** decode with `Length 0` and encode back; the bounds check is `<=`.
4. **An async method reads the view after `await`.** Async byte access keeps the slice, and the lease releases at settlement.
5. **A nullable view in a callback and an event.** A view or `null` passed to a callback or an event stays with the author; a view returned by a callback belongs to the author.
6. **A union with a view member.** `Either<string, Uint8Array>` decodes by kind; an `ArrayBuffer` gets the kind message, and a plain object gets `Value is not a Uint8Array.`.

---

### Task 1: Uint8Array view and codec

Model: **standard**. This task lands before Task 2, because the generator test host compiles generated code against the real `Expo.ModulesCore` assembly.

**Files:**

- Create: `Expo.ModulesCore/Uint8Array.cs`, `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/IJavaScriptCodec.cs` (the leaf doc comment names `Uint8Array`)
- Create: `Expo.ModulesCore.Tests/Codecs/Uint8ArrayCodecTests.cs`

- [ ] **Step 1: View type** as in the spec, with the async variants of `ArrayBuffer` (`ToArrayAsync`, `WithBytesAsync` ×2, `WithReadOnlyBytesAsync` ×2). The constructor is internal and takes ownership of the buffer only when it succeeds.
- [ ] **Step 2: Codec.** `IJavaScriptCodec<Uint8Array>` and `IJavaScriptValueKindCodec` (`Kind == Object`): `Decode` (ref and owned), `Encode`, `Release` (`value?.Dispose()`), and `EncodeCopy`.
- [ ] **Step 3: Tests (30 cases).** Shared bytes (1), sub-view (1), empty views (1), encode over the same buffer (1), native views allocate, copy, retain, and encode (1), sync and async slices of native storage (1), constructor bounds (6), dispose throws and releases once (1), `Release` disposes once, twice idempotently (1), `Encode` leaves the view usable and `Release(null)` does nothing (1), union dispatch to the view (1), every rejected shape with one message and no lease (8), subclass instance (1), prototype spoof within its buffer (1), shadowed fields (4).
- [ ] **Step 4: Verify and commit** `feat(modules-core): add Uint8Array views and codec`.

### Task 2: Generator resolves Uint8Array

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- Modify: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Row.** In `ResolveCodec`, after the `ArrayBuffer` branch, return `OrdinaryCodec("Uint8ArrayCodec") with { ResourceOwnership = HoldsLeaves }` for `global::Expo.ModulesCore.Uint8Array`.
- [ ] **Step 2: Kind table.** `GetUnionMemberKind` maps `Uint8Array` to `Object`.
- [ ] **Step 3: Tests (39 cases).** The `ArrayBuffer` positions (13; the glue contains `CodecRelease.ThrowIfFailed(`), `Uint8Array?` positions (8), shared-object members (4), callback type arguments and unions (8, including `JavaScriptCallback<Uint8Array?>`, `JavaScriptCallback<ValueTuple<Uint8Array?>, Uint8Array?>`, `Either<string, Uint8Array>`, and `Either<ArrayBuffer, Uint8Array>`), rejected unions without two kinds (2: two views, an annotated view), and event payloads (4: direct on a module and a shared object, `Uint8Array?`, a list).
- [ ] **Step 4: Verify** that no `GeneratorPreserves*` or `GeneratorPins*` digest moved, then commit `feat(generator): resolve Uint8Array as a leaf codec`.

### Task 3: Prove Uint8Array on Hermes

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleFixtures.cs` (`TypedArrayThingsModule`, `TypedArrayEntry`, `ViewHolder`)
- Modify: `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleTests.cs`

- [ ] **Step 1: Fixtures.** `TypedArrayEntry` (constructor, `ReadSeed`, `Describe`) and `TypedArrayThingsModule` with `Fill`, `FillOptional`, `FailAfterRead`, `Hex`, `HexLater` (reads with `ToArrayAsync` after the await), `Make`, `Echo`, `EchoLater`, `Stored` (stores `Retain()` copies), `HexAll`, `EchoList`, `EchoMap`, `EchoHolder`, `Hold`, `ReadHeld`, `ReleaseHeld`, `TakeBuffer`, `TakeBytes`, `Write(Either<string, Uint8Array>)`, the `OnBytes` event (`Func<Uint8Array?, Task>`), `Forward` (event and `JavaScriptCallback<ValueTuple<Uint8Array?>, bool>` argument), and `Take` (`JavaScriptCallback<Uint8Array?>` result).
- [ ] **Step 2: Tests (17 cases).** In-place fill including a subclass (1), sub-view sync and async (1), returns and properties (1), wrong shapes on module, shared-object, constructor, and nullable paths (7), composition (1), retained copy (1), exact binary parameters reject a view (1), authored throw releases once (1), union by kind (1), nullable view in an event and a callback (1), nullable callback result (1).
- [ ] **Step 3: Verify and commit** `test(modules-core): prove Uint8Array codecs on Hermes`.

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`, `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md`
- Delete: `docs/plans/036-typed-array-codecs.md`, `docs/changes/2026-11-06-typed-array-codecs/`

- [ ] **Step 1: Merge the delta into both specs** as the spec states: the new requirement after "Two-Member Unions Decode By Value Kind", and the edits to the other requirements it lists.
- [ ] **Step 2: Update the guide and the skill** (the leaf definition).
- [ ] **Step 3: Update `docs/roadmap.md`.** Row 036 `DONE` with brief `—`, a 036 note after the 035 note, and a P3 backlog row for the Hermes caveat. Its fix is to re-pin Hermes to a `static_h` commit that has upstream `8e45e2f` ("Implement tryGetMutableBuffer"), not a local fallback.
- [ ] **Step 4: Delete the brief and the change folder.** `rg -n '036-typed-array-codecs|2026-11-06-typed-array-codecs' docs .agents AGENTS.md` prints nothing.
- [ ] **Step 5: Verify** with the spec's block. Expected: 1451 passed (1365 + 30 + 39 + 17), 0 skipped; format and `git diff --check` clean; all scans print nothing.
- [ ] **Step 6: Commit** `docs(modules-core): close Uint8Array codecs`.
