# Nullable Reference Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `superpowers:subagent-driven-development` or
> `superpowers:executing-plans` to implement this plan task by task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add strict, annotation-driven nullable reference codecs to generated
`Expo.ModulesCore` bindings.

**Architecture:** Separate nullable wrappers handle nullish values and delegate
non-null conversion to existing strict codecs. Dedicated collection adapters
preserve the current special decode methods. Generator analysis selects these
codecs only for `NullableAnnotation.Annotated` references and stops an excluded
ownership-bearing type before it can fall through to a non-nullable codec, so
the calling analysis reports its existing context-specific diagnostic.

**Tech Stack:** .NET 10, C#, Roslyn source generation, `Expo.JSI`, Hermes, and
xUnit v3.

## Global constraints

- Treat `spec.md` in this directory as the normative delta.
- Keep nullish handling out of existing non-nullable codecs. In particular,
  `StringCodec` must remain strict.
- Preserve nested nullable annotations while removing only the top-level
  annotation during inner codec resolution.
- Keep `List<T>` unsupported.
- Do not add nullable support for `JavaScriptValue`, `ArrayBuffer`,
  `JavaScriptCallback<...>`, `SharedObject`, `SharedRef<T>`, or a concrete
  `[ExpoSharedObject]` class.
- Stop codec fallback for an annotated excluded type and let the calling
  analysis report the diagnostic it already reports for that position. Do not
  add a new diagnostic code.
- Leave `NullableCodec<T, TCodec>` unchanged.
- Do not add runtime reflection, dynamic invocation, JSON conversion, a C ABI
  entry, or a platform-specific dependency.
- Use tests to fix the strict non-nullable behavior before changing generator
  dispatch.

## Files out of scope

These generator files stay untouched. Do not re-add them to a step.

- `Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs` — no new diagnostic
  descriptor is needed, because the exclusions reuse existing codes.
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`
  — nullable shared-object boundaries keep reporting `EXPOJSI023` through the
  current path.
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.ModuleAnalysis.cs`,
  `ExpoModulesGenerator.EventAnalysis.cs`, and
  `ExpoModulesGenerator.SharedObjectModel.cs` — no rejection state has to be
  propagated, because each caller's existing unsupported-type diagnostic already
  fires when codec resolution yields no codec.

## Requirement coverage

| Delta requirement | Implementation step | Verification step | Documentation step |
| --- | --- | --- | --- |
| Nullable Reference Codec Selection Is Annotation-Driven | Step 3 | Steps 3 and 4 | Step 5 |
| Supported Nullable Reference Values Use Separate Codecs | Steps 2 and 3 | Steps 2, 3, and 4 | Step 5 |
| Nullable Collection Containers And Contents Compose Recursively | Steps 2 and 3 | Steps 2, 3, and 4 | Step 5 |
| Ownership-Bearing Nullable References Are Build Diagnostics | Step 3 | Step 3 | Step 5 |
| Optional Nullable Reference Arguments Preserve Authored Defaults | Step 3 | Steps 3 and 4 | Step 5 |
| Nullable Reference Codecs Apply Across Generated Binding Surfaces | Step 3 | Steps 3 and 4 | Step 5 |
| Nullable Value-Type Codec Behavior Remains Unchanged | Step 3 | Steps 3 and 4 | Step 5 |
| Nullable Reference Bindings Remain Generated And Portable | Steps 2 and 3 | Steps 3 and 4 | Step 5 |

---

## Step 2: Add nullable codecs for standard and collection shapes

**Files:**

- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReferenceCodec.cs`.
- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReadOnlyListCodec.cs`.
- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableDictionaryCodec.cs`.
- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableReadOnlyDictionaryCodec.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ByteArrayCodec.cs`.
- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Codecs/NullableReferenceCodecTests.cs`.

### 2.1 Write direct codec tests first

- [ ] Cover `NullableReferenceCodec<T, TCodec>` with scoped
  `JavaScriptValueRef` and owned `JavaScriptValue` decode paths.
- [ ] Prove that JavaScript `null` and `undefined` decode to C# `null` without
  invoking the inner codec.
- [ ] Prove that C# `null` encodes to JavaScript `null`.
- [ ] Prove that non-null decode and encode delegate to the existing inner
  codec.
- [ ] Cover `byte[]?` with null and non-null ArrayBuffer values.
- [ ] Cover null and non-null containers for all three collection adapters.
- [ ] Cover nullable elements and values through an inner nullable reference
  codec.

Run:

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
```

Expected before implementation: compilation fails only because the new codec
types do not exist and `ByteArrayCodec` does not implement
`IJavaScriptCodec<byte[]>`.

### 2.2 Implement the standard nullable wrapper

- [ ] Add `NullableReferenceCodec<T, TCodec>` as a `readonly struct`
  implementing `IJavaScriptCodec<T?>`, with `where T : class` and
  `where TCodec : IJavaScriptCodec<T>`.
- [ ] Make both decode overloads return `null` for `IsNullish`; otherwise call
  `TCodec.Decode`.
- [ ] Make encode return `runtime.CreateNull()` for C# `null`; otherwise call
  `TCodec.Encode`.
- [ ] Change `ByteArrayCodec` from a static class to a `readonly struct`
  implementing `IJavaScriptCodec<byte[]>`. Do not change its three method
  bodies.

### 2.3 Implement nullable collection adapters

- [ ] Add `NullableReadOnlyListCodec<T, TCodec>` implementing
  `IJavaScriptCodec<IReadOnlyList<T>?>`.
- [ ] Add `NullableDictionaryCodec<T, TCodec>` implementing
  `IJavaScriptCodec<Dictionary<string, T>?>`.
- [ ] Add `NullableReadOnlyDictionaryCodec<T, TCodec>` implementing
  `IJavaScriptCodec<IReadOnlyDictionary<string, T>?>`.
- [ ] Return C# `null` from both decode overloads when `IsNullish`.
- [ ] Delegate non-null list decode to
  `JavaScriptArrayCodec<T, TCodec>.DecodeToArray`; use `value.Ref` for the
  owned overload.
- [ ] Delegate non-null dictionary decode to
  `JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary`.
- [ ] Encode null containers as JavaScript `null` and delegate non-null encode
  to the existing collection helper.

Run:

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
```

Expected after implementation: the project and all direct codec tests pass.

---

## Step 3: Teach the generator to classify nullable references

**Files:**

- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`.

See "Files out of scope" above for the generator files this step must leave
alone.

### 3.1 Add failing generator tests

- [ ] Use `#nullable enable` in every positive nullable-reference source.
- [ ] Prove that `string?`, `Uri?`, `byte[]?`, and a nullable reference record
  select `NullableReferenceCodec` around the existing inner codec.
- [ ] Prove that nullable list and both nullable dictionary containers select
  their dedicated adapters.
- [ ] Prove that nullable list elements and dictionary values compose through
  `NullableReferenceCodec`, including one nested nullable container.
- [ ] Prove that nullable record fields use the nullable wrapper.
- [ ] Cover a nullable parameter, sync return, `Task<string?>` result, optional
  argument, read-write property, typed event payload, shared-object
  constructor parameter, and shared-object `[JS]` member.
- [ ] Prove that non-nullable `string` still emits plain `StringCodec`.
- [ ] Prove with `#nullable disable` that an oblivious `string` still emits
  plain `StringCodec`.
- [ ] Prove that `int?` still emits `NullableCodec<int, ...>`.
- [ ] Prove that `JavaScriptValue?` and `ArrayBuffer?` report `EXPOJSI001` in a
  parameter position and `EXPOJSI002` in a return position.
- [ ] Prove that `JavaScriptCallback<...>?` reports `EXPOJSI008`, an excluded
  nullable `[JS]` property reports `EXPOJSI015`, an excluded nullable record
  field reports `EXPOJSI007`, and an excluded nullable event payload reports
  `EXPOJSI019` for a module event or `EXPOJSI027` for a shared-object event.
- [ ] Prove that `SharedObject?`, `SharedRef<T>?`, and a nullable concrete
  `[ExpoSharedObject]` type keep reporting `EXPOJSI023`.
- [ ] Cover an excluded nullable type in a nested record or collection
  position.
- [ ] Assert that a rejected member emits no binding, reports only the existing
  context-specific diagnostics, and never falls back to its non-nullable codec.
- [ ] Prove that an unrelated unsupported annotated reference keeps the
  existing unsupported-type diagnostic.
- [ ] Prove that `List<T>` and `List<T>?` remain unsupported.

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected before implementation: positive nullable references resolve to
non-nullable codecs, nullable containers have no codec, and excluded types fall
through to their non-nullable codecs instead of being rejected.

### 3.2 Add tri-state dispatch

- [ ] Do not add a diagnostic descriptor to `ExpoModulesDiagnostics`.
- [ ] Add nullable reference classification at the top of
  `GetCodecExpression`, before callback, ArrayBuffer, byte-array,
  JavaScriptValue, convertible, record, primitive, and collection matches.
- [ ] Return a distinct not-handled state for references that are not
  `NullableAnnotation.Annotated`.
- [ ] For an annotated reference, strip only the top-level annotation with
  `WithNullableAnnotation(NullableAnnotation.NotAnnotated)`.
- [ ] Resolve nested element, value, and record-field codecs with their
  original nested annotations.
- [ ] Return a handled result even when the annotated type is excluded or its
  inner codec is unavailable. Do not continue through ordinary dispatch.
- [ ] Ensure an excluded nullable type is handled with no codec expression, so
  its caller reports the diagnostic it already reports for that position, emits
  no binding, and cannot reach a concrete non-nullable codec branch.
- [ ] Leave module method, property, record, event, shared-object constructor,
  and shared-object member analysis to report their existing unsupported-type
  diagnostics. Do not add rejection state to carry between them.
- [ ] Let typed-event payload analysis keep reporting `EXPOJSI019` or
  `EXPOJSI027`.
- [ ] Leave shared-object boundary analysis unchanged. Nullable `SharedObject`,
  `SharedRef<T>`, concrete shared-object types, and nested nullable
  shared-object types keep reporting `EXPOJSI023`.
- [ ] Preserve current non-nullable shared-object diagnostics and exact-type
  codec selection.

### 3.3 Emit the correct codec expressions

- [ ] Emit `NullableReferenceCodec<innerType, innerCodec>` for supported
  ordinary references.
- [ ] Emit `NullableReadOnlyListCodec<elementType, elementCodec>` for
  `IReadOnlyList<T>?`.
- [ ] Emit `NullableDictionaryCodec<valueType, valueCodec>` for
  `Dictionary<string, T>?`.
- [ ] Emit
  `NullableReadOnlyDictionaryCodec<valueType, valueCodec>` for
  `IReadOnlyDictionary<string, T>?`.
- [ ] Keep dictionary keys restricted to exact `string`.
- [ ] Leave the existing `TryGetNullableCodec` value-type path unchanged.
- [ ] Do not reorder the remaining non-nullable dispatch chain.

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected after implementation: all generator tests pass and generated source
contains no reflection or dynamic fallback.

---

## Step 4: Verify generated bindings through Hermes

**Files:**

- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/ModuleAttributeTests.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/BinaryModuleFixture.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/BinaryModuleTests.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Events/EventModuleTests.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs`.

### 4.1 Add generated-binding fixtures and tests

- [ ] A required `string?` parameter receives C# `null` for JavaScript `null`
  and explicit `undefined`, and preserves a non-null string.
- [ ] A C# null `string?` return becomes JavaScript `null`.
- [ ] JavaScript `null` passed to a non-nullable `string` rejects before
  authored code runs. Record an authored-call flag so the test proves the
  decode boundary stayed strict.
- [ ] An optional `string?` parameter uses its authored default for omission
  and explicit `undefined`, while explicit JavaScript `null` reaches authored
  code as C# `null`.
- [ ] A read-write `string?` property accepts and returns null and non-null
  values.
- [ ] A nullable reference record parameter round-trips null and a value.
- [ ] A record with a `string?` field round-trips null and a non-null field.
- [ ] Nullable list and both dictionary containers round-trip JavaScript null.
- [ ] Nullable list elements and dictionary values preserve null beside
  non-null values.
- [ ] `byte[]?` round-trips JavaScript null and a non-null ArrayBuffer.
- [ ] `Task<string?>` resolves with JavaScript null.
- [ ] A nullable typed event payload encodes C# null as JavaScript null.
- [ ] A shared-object constructor or `[JS]` member proves a safe nullable
  reference parameter or return uses the same codec path.

Run:

```sh
scripts/test-managed.sh
```

Expected: the full managed suite passes, including all new direct,
generator, and Hermes-backed tests.

### 4.2 Check generated hot-path constraints

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected: no new runtime reflection, dynamic invocation, generic object-array,
or JSON conversion appears in generated binding paths.

---

## Step 5: Merge the accepted behavior into durable docs

**Files:**

- Modify `docs/module-authoring-guide.md`.
- Modify `docs/specs/modules-core-boundary.md`.
- Modify `docs/plans/README.md`.
- Archive or remove this transient change package after its accepted
  requirements are merged.

### 5.1 Update authored-module guidance

- [ ] List `string?`, `Uri?`, `byte[]?`, nullable reference records, and the
  three nullable collection containers as supported generated types.
- [ ] Explain recursive nullable element and dictionary value support.
- [ ] State that `List<T>` remains unsupported.
- [ ] Document required and optional nullable reference argument semantics.
- [ ] State that only `NullableAnnotation.Annotated` activates nullable
  reference handling.
- [ ] List the ownership-bearing nullable exclusions and state that annotating
  one reports the existing diagnostic for that position.

### 5.2 Merge the delta into the living spec

- [ ] Merge every `### Requirement:` and scenario from `spec.md` into
  `docs/specs/modules-core-boundary.md` without weakening existing
  ownership, shared-object, event, or nullable value-type requirements.
- [ ] Mark plan 026 complete only after implementation, tests, and the living
  spec agree.
- [ ] Archive or remove the transient `docs/changes` package as required by
  the living-spec workflow.

### 5.3 Run final verification

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
scripts/test-managed.sh
scripts/format.sh --check --all
```

Expected: every command exits successfully, no test is skipped silently, and
the durable documentation matches the implemented behavior.
