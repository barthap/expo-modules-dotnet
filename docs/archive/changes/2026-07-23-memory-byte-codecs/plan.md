# Memory Byte Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or `superpowers:executing-plans`
> task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add copy-based `Memory<byte>` and `ReadOnlyMemory<byte>` codecs to
generated module bindings and make the existing `ArrayBuffer` API easier to
discover without changing its behavior.

**Architecture:** The codecs remain entirely in `Expo.ModulesCore`. Decode
reuses `ByteArrayCodec` to create managed storage, and encode reuses
`ArrayBufferCodec.EncodeCopy` to copy the current memory slice. The generator
maps the two exact `byte` specializations as normal codecs, leaving span
handling and native code unchanged.

**Tech Stack:** .NET 10, C#, Roslyn incremental generator, xUnit, Hermes
testhost.

## Global Constraints

- Support only `Memory<byte>` and `ReadOnlyMemory<byte>`; non-byte memory stays
  unsupported.
- Copy at each JavaScript/managed boundary. Do not pin, adopt, or alias managed
  memory and do not add `ArrayBuffer.ToMemory()`.
- Preserve the current ArrayBuffer-only input contract; TypedArray views stay
  unsupported.
- Keep the core portable and headless. Do not modify ABI, native bridge,
  scheduler, platform adapters, or package metadata.
- Keep generated bindings reflection-free and direct-call based.
- Do not push, create a pull request, or use a worktree.

---

### Task 1: Add Copying Memory Codecs And Generator Recognition

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/MemoryByteCodec.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ReadOnlyMemoryByteCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ArrayBufferCodecTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**

```csharp
public readonly struct MemoryByteCodec : IJavaScriptCodec<Memory<byte>>
public readonly struct ReadOnlyMemoryByteCodec : IJavaScriptCodec<ReadOnlyMemory<byte>>
```

- [ ] Write a failing codec test that decodes a JavaScript ArrayBuffer into
  each memory type, mutates the JavaScript source, and proves the managed copy
  is unchanged.
- [ ] Run the filtered `ArrayBufferCodecTests` test and confirm it fails because
  the codec types do not exist.
- [ ] Add both readonly struct codecs. Decode through `ByteArrayCodec.Decode`;
  encode with `ArrayBufferCodec.EncodeCopy(value.Span, runtime)`.
- [ ] Extend the test with non-zero-offset encode slices, source mutation after
  encoding, and default/empty values.
- [ ] Add a failing generator test covering synchronous and asynchronous
  functions with both memory types and multiple memory parameters.
- [ ] Map exactly `global::System.Memory<byte>` to `MemoryByteCodec` and
  `global::System.ReadOnlyMemory<byte>` to `ReadOnlyMemoryByteCodec` before
  nullable/collection analysis. Keep them in the ordinary codec passing kind.
- [ ] Assert generated source uses both codecs without span callbacks or span
  diagnostics. Compile an event or callback generic consumer to prove each
  codec satisfies `IJavaScriptCodec<T>`. Assert `Memory<int>` and
  `ReadOnlyMemory<int>` use the existing unsupported-type diagnostic.
- [ ] Run the focused generator and codec tests, then commit the feature and its
  tests as `feat(modules-core): add memory byte codecs`.

### Task 2: Exercise Generated Module Dispatch

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedBinaryModuleTests.cs`

- [ ] Write a failing Hermes-backed test that invokes generated mutable and
  read-only memory methods from JavaScript. Cover a non-zero-offset returned
  slice, independent input/output copies, two memory arguments, and an async
  `ReadOnlyMemory<byte>` result.
- [ ] Run the focused generated binary test and confirm the failure is due to
  unsupported generated memory types.
- [ ] Add minimal authored fixture methods using the two codecs, including one
  asynchronous return and one two-parameter method.
- [ ] Run the focused test and confirm JavaScript receives fresh ArrayBuffers
  containing exactly the returned slice.
- [ ] Commit the generated-dispatch fixture and integration coverage as
  `test(modules-core): cover generated memory byte codecs`.

### Task 3: Reorganize And Document ArrayBuffer

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ArrayBuffer.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ArrayBufferByteAccess.cs`

- [ ] Move existing `ArrayBuffer` members without changing signatures or bodies:
  state/constructors, length, factories, retain, `ToArray` APIs, `Copy` APIs,
  synchronous access, asynchronous access, internal encode, dispose, helpers.
- [ ] Add XML documentation to every public ArrayBuffer member. Describe
  captured length, zero-filled allocation, independent-copy results, retained
  ownership, span callback lifetime, JavaScript runtime scheduling, cancellation,
  and idempotent disposal.
- [ ] Document each public byte-access delegate as receiving a span valid only
  for the synchronous callback.
- [ ] Add concise XML documentation to both new codecs describing independent
  decode storage and exact-slice encode copying.
- [ ] Run the focused ArrayBuffer and generated binary tests to prove this task
  did not change behavior, then commit as
  `docs(modules-core): organize ArrayBuffer API`.

### Task 4: Merge Docs, Archive Artifacts, And Verify

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/ownership-and-scoped-refs.md`
- Modify: `docs/roadmap.md`
- Move: `docs/changes/2026-07-23-memory-byte-codecs/` to
  `docs/archive/changes/2026-07-23-memory-byte-codecs/`

- [ ] Add both memory types to the binary codec requirement and state their
  two-way copy semantics, exact-slice encoding, ordinary-codec status, and
  non-byte rejection.
- [ ] Extend the ownership spec so managed `Memory<byte>` and
  `ReadOnlyMemory<byte>` are copied rather than pinned or retained as JavaScript
  storage.
- [ ] Mark both types complete in the binary codec roadmap entry.
- [ ] Run `scripts/test-managed.sh`, `scripts/format.sh --check --all`,
  `git diff --check`, and the generated-binding reflection/dynamic-invocation
  guard search.
- [ ] Move the accepted delta spec and plan into `docs/archive/changes/` and
  commit the living-spec merge as
  `docs(specs): complete memory byte codec support`.
