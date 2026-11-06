# Union Arguments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Either<TFirst, TSecond>` works in every generated codec position when the two members have different JavaScript value kinds among `String`, `Bool`, `Number`, and `ArrayBuffer`. The codec reads the kind once and decodes through that member's codec (roadmap item 035).

**Architecture:** The runtime gains one carrier struct, one static-abstract kind interface on seven leaf codecs, and one ordinary union codec, `EitherCodec`, whose `Encode` and `Release` use the member that the union holds. The generator resolves `Either<,>` by `ConstructedFrom`, maps each member type to a kind through a type-keyed table, and builds an ordinary descriptor that holds leaves through the existing `CompositeCodec` rule when a member does. Emission does not change. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-06-union-arguments/spec.md` (binding authority). Base commit is `6fc0e5a` (the 034 close plus the bridge fix); `git diff --stat 6fc0e5a..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/035-union-arguments.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Notes on the spec

- **No new generator gate.** The union is `DecodeAndEncode`, so returns, async results, properties, record fields, callback type arguments, and typed event payloads accept it through the existing checks (spec decision 3). `ExpoModulesGenerator.ModuleAnalysis.cs`, `EventAnalysis.cs`, and the composite branches do not change.
- **No diagnostic can carry "a reason that names the rule".** `EXPOJSI001` is `Parameter '{0}' on '{1}' uses unsupported type '{2}'`. Its shared-object translation and the constructor path end in `uses unsupported type '...'` too. The plan adds no message text. Rejection tests pin the ID, the member name, and the full union type name.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one carrier struct, one codec, one kind table on each side of the boundary, no try-one-then-the-other decoding.
  2. Performance: one `Kind` read, then the member codec. A leaf-free union keeps leaf-free generated text. A union with an `ArrayBuffer` member releases that one lease, exactly as a direct `ArrayBuffer` parameter does.
  3. Correctness: a leaf-bearing member keeps the 030 ownership rules through `IJavaScriptCodec<T>.Release`. The carrier holds no wrapper of its own.
- New production files (exactly three): `Expo.ModulesCore/Either.cs`, `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs`, `Codecs/EitherCodec.cs`. Edited production files: the seven leaf codecs (one interface and one static property each) and `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`. `ExpoCodecDescriptor.cs`, `ExpoModulesGenerator.Emission.cs`, `ModuleAnalysis.cs`, `EventAnalysis.cs`, `SharedObjectModel.cs`, `SharedObjectValidation.cs`, `ExpoModulesDiagnostics.cs`, and every other codec do not change.
- No new diagnostic IDs and no new or changed diagnostic message text.
- The kind message is exact: `Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.`, where each value is the `JavaScriptValueKind` name. The exception type is `InvalidOperationException`.
- No existing test changes and no output-contract digest moves. A moved `GeneratorPreserves*` or `GeneratorPins*` digest is a finding: stop and report.
- The generator never derives a kind or a policy from codec text. 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- The spec's two code scans print nothing:
  - `rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs`.
  - `rg -n 'EitherCodec<[^>]*(SharedObject|JavaScriptValue|JavaScriptCallback)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests`.
- Counter rules (from 031): exact-once `ArrayBuffer` assertions use JS-created buffers and `LongLivedArrayBuffersReleased` after `WaitUntilIdle()`. The counter counts native entries; `Retain()` copies share their entry.
- JavaScript error text rule: tests use `Contains` on the kind message, never equality.
- A count, message, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Privacy scan: `git diff --unified=0 6fc0e5a..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects. Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts.
- Before each commit: `scripts/format.sh --check --all` passes and `git diff --check` is clean.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1275 passed, 0 skipped at `6fc0e5a`)                                                                                            |

Test-case arithmetic (each `InlineData` row is one case): Task 1 adds 20, Task 2 adds 52, Task 3 adds 18, nothing is deleted. The full suite reads 1295 after Task 1, 1347 after Task 2, and 1365 after Task 3.

## Review Focus

1. **JavaScript passes a `Uint8Array` view to `Either<string, ArrayBuffer>`.** Its kind is `Object`, so the call fails with the kind message before authored code runs, and no lease is created.
2. **An async method takes or returns the union.** The buffer lease survives the `await` and releases once at settlement; the string path holds no lease.
3. **A later parameter fails to decode after the union decoded a buffer.** The union's lease releases once, and authored code never runs.
4. **A bad union argument reaches a `[JS]` constructor.** The constructor fails before authored code runs and leaves no instance in the registry.
5. **A member type carries its own nullability** (`Either<string?, double>`, `Either<double?, bool>`). `null` is not a value kind, so both report `EXPOJSI001`.
6. **A union in an encode position.** A returned input keeps its JavaScript identity and releases once; an event payload or callback argument stays with the author.

---

### Task 1: Union carrier and codec

Model: **standard**. This task lands before Task 2, because the generator test host compiles generated code against the real `Expo.ModulesCore` assembly.

**Files:**

- Create: `Expo.ModulesCore/Either.cs`, `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs`, `Expo.ModulesCore/Codecs/EitherCodec.cs`
- Modify: `Expo.ModulesCore/Codecs/{String,Bool,Number,ArrayBuffer,ByteArray,MemoryByte,ReadOnlyMemoryByte}Codec.cs`
- Create: `Expo.ModulesCore.Tests/Codecs/EitherCodecTests.cs`

- [ ] **Step 1: Carrier.** `Either<TFirst, TSecond>` as in the spec. `First` throws `"The union holds its second member."`, `Second` throws `"The union holds its first member."`, `Match` throws `ArgumentNullException` for a null function.
- [ ] **Step 2: Kind interface** and one `public static JavaScriptValueKind Kind` property on each of the seven leaf codecs.
- [ ] **Step 3: `EitherCodec`.** Decode by kind, `Decode(JavaScriptValue, ...)` forwards to the ref overload, `Encode` and `Release` use the active member.
- [ ] **Step 4: Tests (20 cases).** Leaf-free union (`Either<string, double>`): decode by kind including `''` and `0` (4), reject `true`, `{}`, `null`, `undefined`, an `ArrayBuffer` with the exact message (5), encode both members (1). Buffer union (`Either<string, ArrayBuffer>`): the string path releases nothing (1); the buffer path decodes the bytes and `Release` releases one entry, twice idempotently, and disposes the buffer (1); `5`, `{}`, `null`, `undefined`, `new Uint8Array(2)` fail with the exact message and no lease (5); `Encode` of the buffer member returns the same JavaScript object and leaves the buffer usable, `Release` of a string member and of `default` releases nothing, and `Release` of the buffer member releases one entry (1). Carrier accessors and `Match` (1). The seven `Kind` values (1).
- [ ] **Step 5: Verify and commit** `feat(modules-core): add two-member union codecs`.

### Task 2: Generator resolves unions

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- Modify: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Kind table.** `IsNumberSpecialType` (also used by the numeric arm of `ResolveCodec`) and `GetUnionMemberKind`, which returns `null` for an annotated member and for any type outside the table.
- [ ] **Step 2: `GetEitherCodec`.** Check both kinds first, then resolve both member codecs through `AsCompositeChildCodec`, then build `OrdinaryCodec("EitherCodec<...>")` and apply `CompositeCodec` for each member.
- [ ] **Step 3: Dispatch.** In `ResolveCodec`, after the `JavaScriptValue` branch and before `TryGetNullableCodec`, return `GetEitherCodec` for a type whose `ConstructedFrom` is `global::Expo.ModulesCore.Either<TFirst, TSecond>`, rejected or not.
- [ ] **Step 4: Tests (52 cases).** Leaf-bearing parameters (6: sync, async, second parameter, nullable, list, dictionary; the glue contains `CodecRelease.ThrowIfFailed(`). Leaf-free parameters (7; the glue contains no `CodecRelease`). Shared-object constructor and method parameters (4). Unsupported members on a module (11: `Uri`, two `Number` members, two `String` members, two binary members, `string?`, `double?`, a record, `JavaScriptValue`, a callback, a list, a nested union) and on a shared object (4); each reports one diagnostic naming the union type and emits no `EitherCodec`. Every position (20: sync and async returns, nullable, list, and dictionary returns, properties, record fields, callback type arguments, events, shared-object return, property, and event), each with no diagnostic and a fragment of the accepted glue.
- [ ] **Step 5: Verify** that no `GeneratorPreserves*` or `GeneratorPins*` digest moved, then commit `feat(generator): accept two-member unions`.

### Task 3: Prove unions on Hermes

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (`UnionThingsModule`, `UnionWriter`, `UnionHolder`)
- Modify: `Expo.ModulesCore.Tests/Modules/LeafReleaseModuleTests.cs`

- [ ] **Step 1: Fixtures.** `UnionWriter` (constructor, `Write`, `Pick`) and `UnionThingsModule` with `Write`, `WriteLater` (reads the buffer with `ToArrayAsync` after the await), `WriteOptional`, `WriteWithCount`, `WriteAll`, `Pick`, and the encode members `Echo`, `EchoLater`, `Wrap(UnionHolder)`, `Stored` (stores `Retain()` copies), the `OnContent` event, `Forward` (event and callback argument), and `Take` (callback result).
- [ ] **Step 2: Tests (18 cases).** Parameter decode and release (1), shared-object members (1), async settlement (1), third kinds before authored code on module, shared-object, and constructor paths (6), the nullable form (1), decode by kind not truthiness (1), a later parameter failure (1), a list of unions (1), sync and async return of the input (1), a record field round trip (1), a property that stores copies (1), an event payload and callback argument (1), a callback result (1).
- [ ] **Step 3: Verify and commit** `test(modules-core): prove union arguments on Hermes`.

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Delete: `docs/plans/035-union-arguments.md`, `docs/changes/2026-11-06-union-arguments/`

- [ ] **Step 1: Merge the delta into `docs/specs/modules-core-boundary.md`** as the spec states: the new requirement after "Module-facing ArrayBuffer And Binary Codecs", the union in "Codecs Release Leaves Through One Protocol" with `IJavaScriptValueKindCodec`, and the descriptor table row.
- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`**, the union sentence in "Boundary Leaf Ownership Is Explicit".
- [ ] **Step 3: Update `docs/module-authoring-guide.md`**, the supported-types bullet after binary data.
- [ ] **Step 4: Update `docs/roadmap.md`.** Row 035 `DONE` with brief `—`, a 035 note after the 034 note, and two P3 backlog rows (reference annotations in diagnostic type names; the parameter name in codec failures).
- [ ] **Step 5: Delete the brief and the change folder.** Git history keeps them. `rg -n '035-union-arguments|2026-11-06-union-arguments' docs .agents AGENTS.md` prints nothing.
- [ ] **Step 6: Verify** with the spec's block. Expected: 1365 passed (1275 + 20 + 52 + 18), 0 skipped; format and `git diff --check` clean; all scans print nothing.
- [ ] **Step 7: Commit** `docs(modules-core): close union arguments`.
