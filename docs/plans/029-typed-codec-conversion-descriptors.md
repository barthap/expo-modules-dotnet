# Plan 029: Replace codec-string inference with typed conversion descriptors

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving on. Touch
> only the files in the In-scope list. If a STOP condition occurs, stop and
> report, do not improvise. This plan is behavior-preserving, so do not create a
> delta spec or a second implementation plan. If implementation requires any
> behavior change, stop; that change must use the living-spec workflow
> separately. Update the status row in `docs/plans/README.md` when done unless a
> reviewer says they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat 512ab46e..HEAD -- docs/plans/README.md packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
> ```
> Plan 028 in `docs/plans/README.md` must be DONE before this plan starts. If it
> is not DONE, or if codec resolution, shared-object boundary classification,
> generator model types, or generated output-contract helpers changed, compare
> the live code with "Current state." A semantic mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: HIGH (behavior-preserving refactor of the generated-binding hot
  path; a wrong descriptor silently changes ownership or context handling)
- **Depends on**: plan 028, annotation-insensitive shared-object boundary
  detection, currently indexed in `docs/plans/README.md`
- **Blocks**: plans 030–033
- **Category**: tech-debt
- **Planned at**: commit `512ab46e`, 2026-07-25

## Why this matters

The generator currently stores codec expressions as strings, then parses those
strings to decide how to decode, whether a runtime context is required, and who
cleans up the decoded value. That is the technical debt behind the repeated
special cases found while implementing plan 026.

This plan changes representation only. Codec selection produces a typed
descriptor, and later analysis and emission consume explicit fields. Generated
bindings, diagnostics, and runtime behavior must remain byte-for-byte
equivalent. Plans 030–033 can then add resource and nullable policies without
adding more `StartsWith` or exact-string checks.

## Current state

### One string carries several unrelated facts

`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`
has three separate protocols built on a codec-expression string:

- `GetCodecExpression` returns a `string?` for the selected codec.
- `GetDecodeExpression` checks whether that string starts with
  `JavaScriptArrayCodec<` or `JavaScriptDictionaryCodec<` to select
  `DecodeToArray` or `DecodeToDictionary` instead of `Decode`.
- Parameter analysis receives a separate `requiresRuntimeContext` Boolean even
  though that fact belongs to conversion policy.

The same pattern appears outside codec resolution:

- `ExpoModulesGenerator.ModuleAnalysis.cs:662-699` derives context and cleanup
  from callback type checks and exact codec strings.
- `ExpoModulesGenerator.ModuleAnalysis.cs:817-825` repeats the exact checks for
  properties.
- `ExpoModulesGenerator.SharedObjectModel.cs:151-156` repeats them for
  constructors.
- `ExpoModulesGenerator.SharedObjectValidation.cs:109` defines
  `IsSharedObjectCodecExpression` with `StartsWith`.
- `ExpoModulesGenerator.Emission.cs:895-1013` selects direct
  `JavaScriptValueCodec` and `ArrayBufferCodec` return handling by exact string.
- `ExpoModulesGenerator.Emission.cs:1158-1167` disposes parameters using the
  Boolean inferred during analysis.

That means renaming a codec can change ownership behavior while still compiling.
It also means a wrapper such as `NullableReferenceCodec<T, TCodec>` hides the
inner codec's policy from every downstream string comparison.

### The generator models preserve the string and derived Booleans

`ExpoModuleModel.cs` stores codec expressions on function parameters, returns,
properties, generated-record fields, and event payloads. It also stores facts
such as `RequiresRuntimeContext` and `OwnsDecodedValue` beside those strings.
`ExpoSharedObjectModel.cs` follows the same shape for shared-object boundaries.

`ExpoModuleEventPayloadKind` is the useful counterexample: event payload
classification already uses an enum instead of reparsing an emitted expression.
Match that approach.

### Output-contract support already exists

`Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs:57-79` can capture and
hash generated output. The tests around plan 026 in
`ExpoModulesGeneratorTests.cs:3920-4487` cover nullable codecs on parameters,
returns, properties, records, collections, events, and shared-object surfaces.
Use those tests as the primary characterization set before changing the model.

### Constraints that must survive

- `IJavaScriptCodec<T>` remains unchanged in this plan.
- `ArrayBufferCodec`, `JavaScriptCallbackCodec`, and `SharedObjectCodec<T>` are
  special helpers and do not all implement `IJavaScriptCodec<T>`.
- Callback and shared-object decoding needs `DotnetRuntimeContext`.
- Direct decoded `JavaScriptValue` and `ArrayBuffer` values are invocation-owned
  today and generated glue disposes them.
- Direct returned `JavaScriptValue` transfers its wrapper; direct returned
  `ArrayBuffer` is encoded and then disposed.
- Event payload classification already has its own typed enum. Do not replace it
  with a second parallel abstraction unless a concrete duplication remains.
- This plan must not alter accepted plan-026 exclusions or diagnostics.

## Target design

Add one internal immutable generator model, named `ExpoCodecDescriptor` unless
the live naming conventions require `ExpoCodecModel`. It must contain the
minimum facts the current generator already uses:

```text
Expression                 emitted C# codec type/expression
DecodeOperation            Decode | DecodeToArray | DecodeToDictionary
ContextRequirement         None | DotnetRuntimeContext
DecodedOwnership           Borrowed | InvocationOwned | ContextRetained
EncodeOperation            Standard | TransferJavaScriptValue |
                           EncodeThenDisposeArrayBuffer
Capabilities               Decode, Encode, or both
```

Use enums or an equivalent closed typed representation. Do not replace string
comparisons with a bag of loosely related Booleans. `Expression` is data for
emission only; no analysis decision may inspect it.

The descriptor may recursively contain an element/value descriptor where that
removes existing duplicate resolution. Do not add speculative fields for plan
030. That plan can extend the model after its resource-scope contract is
approved.

Store the descriptor on generator models. Remove the parallel codec string and
derived ownership/context fields when all consumers have moved. Helper methods
may return `ExpoCodecDescriptor?` during the migration, but the final state must
have one source of truth.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Generator tests | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` | all generator tests pass |
| Managed regression | `scripts/test-managed.sh` | all discovered managed tests pass |
| Format | `scripts/format.sh --check --all` | exit 0, no files need formatting |
| String-policy scan | `rg -n 'CodecExpression.*(StartsWith|==| is )|StartsWith\\(\"(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is \"(JavaScriptValueCodec|ArrayBufferCodec)\"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator` | no production-code matches |
| Changed paths | `git status --short` | only generator, generator-test, and `docs/plans/README.md` paths |

## Suggested executor toolkit

- Use `.agents/skills/expo-jsi-managed-handle-lifetime/SKILL.md` when mapping
  the existing ownership fields. This plan preserves those rules; it does not
  redesign them.
- Use this `improve` plan as the implementation checklist; do not duplicate it
  under `docs/changes/`.

## Scope

**In scope**:

- `docs/plans/README.md`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoSharedObjectModel.cs`
- A new descriptor-model file in that same generator project, if useful
- `ExpoModulesGenerator.Codecs.cs`
- `ExpoModulesGenerator.ModuleAnalysis.cs`
- `ExpoModulesGenerator.SharedObjectModel.cs`
- `ExpoModulesGenerator.SharedObjectValidation.cs`
- `ExpoModulesGenerator.EventAnalysis.cs`
- `ExpoModulesGenerator.Emission.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Out of scope**:

- Any file in `Expo.ModulesCore/Codecs`
- Changes to `IJavaScriptCodec<T>` or public managed API
- New nullable support or resource-scope behavior
- New diagnostics or changed diagnostic selection
- Native ABI, platform adapters, apps, or authored modules
- Reformatting or renaming generated bindings beyond changes forced by the
  descriptor migration

## Git workflow

- Branch: `advisor/029-typed-codec-conversion-descriptors`
- Keep characterization tests, model migration, and final docs as separate
  logical commits when practical.
- Match the repo's conventional commit style, for example
  `refactor(generator): model codec conversion policy`.
- Do not push or open a PR without explicit operator approval.

## Steps

### Step 1: Confirm the behavior-preserving boundary

Read plan 028's final diff and run the drift check. Write down the invariants in
the first characterization-test commit message:

- generated C# source and diagnostics remain unchanged;
- codec expressions are opaque emission data;
- decode operation, context requirement, decoded ownership, encode operation,
  and capabilities become typed descriptor fields;
- analysis and emission do not infer policy from codec-expression text;
- runtime and public API do not change.

Do not create a delta spec for these no-change invariants. If the live code
requires changing one, stop and open a separate living-spec change.

**Verify**: `git status --short` is clean before the first source/test edit.

### Step 2: Lock the current output before refactoring

Add characterization cases to `ExpoModulesGeneratorTests.cs` only where current
coverage is missing. Cover at least:

- scalar, array, and dictionary decode-operation selection;
- direct `JavaScriptValue` and `ArrayBuffer` parameter cleanup;
- direct sync and async return ownership;
- callback and shared-object context requirements;
- nullable regular codecs from plan 026;
- shared-object diagnostics after plan 028.

Use `GeneratorTestHost` output-contract hashes or exact generated-source
assertions. Capture the pre-refactor output; do not update expected output after
the migration unless whitespace comes from an approved formatter.

**Verify**: run the generator-test command. Expected: all tests pass before the
production refactor.

### Step 3: Introduce the descriptor and migrate resolution

Add the descriptor and its closed enums. Change `GetCodecExpression` and its
`TryGet*` helpers to return descriptors. Each resolver branch must set policy
where the codec is selected:

- array/dictionary branches set their decode operation;
- callback/shared-object branches set the context requirement;
- direct `JavaScriptValue`/`ArrayBuffer` branches set decoded ownership and
  encode operation;
- decode-only callback branches declare context-retained ownership and no
  encode capability;
- ordinary codecs use `Decode`, no context, borrowed decoded ownership, and
  standard encoding.

Do not infer the descriptor from the final expression in a shared factory. That
would preserve the same bug in a different location.

**Verify**: build through the generator-test command. Expected: compile success
and no changed expected output.

### Step 4: Migrate models and consumers

Replace codec-string plus policy-Boolean combinations in `ExpoModuleModel.cs`
and `ExpoSharedObjectModel.cs` with the descriptor. Update module analysis,
shared-object analysis, event analysis, and emission to switch on descriptor
fields. Keep event payload kind where it expresses event scheduling policy.

Emission may read `descriptor.Expression` only to write generated C#. It may
switch on `DecodeOperation`, `DecodedOwnership`, or `EncodeOperation`, but must
not compare or parse `Expression`.

Delete `IsSharedObjectCodecExpression` after its consumers use
`ContextRequirement` or an explicit shared-object category. Delete redundant
ownership/context fields after all construction sites compile.

**Verify**:

1. Run the string-policy scan. Expected: no production matches.
2. Run generator tests. Expected: all pass with the pre-refactor contracts.

### Step 5: Prove no generated behavior changed

Run the full managed suite and format check. Review the generated-source test
diff. No digest, diagnostic ID, generated statement order, cleanup statement,
or context argument may change.

If a source difference is unavoidable because a descriptor removes unstable
ordering, stop and request approval. Do not fold a behavior change into this
plan.

**Verify**: full managed tests and format both pass.

### Step 6: Close the plan

Mark plan 029 DONE in `docs/plans/README.md`. Record the test count and
implementation commits in the status row. Do not edit the living spec for a
behavior-preserving representation change.

**Verify**:

```sh
git status --short
git diff --unified=0 512ab46e..HEAD -- docs packages/expo-modules-dotnet/managed/packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\[A-Za-z0-9._-]+\\\\'
```

Expected: clean tree after commits; privacy scan has no newly introduced
machine-specific path or username, so `rg` prints nothing and exits 1.

## Test plan

- Characterization tests must fail if any existing generated ownership,
  context, decode-method, or return path changes.
- Add direct descriptor-model assertions only when generated-source assertions
  cannot expose a wrong field. Do not test private implementation names for
  their own sake.
- Run the generator project first, then the full managed suite.
- The test count may increase through characterization tests; no existing test
  may be removed or weakened.

## Done criteria

- [ ] Plan 028 is DONE before implementation starts.
- [ ] Codec resolution returns a typed descriptor.
- [ ] Generator models have one source of truth for conversion policy.
- [ ] Production analysis and emission never parse or compare codec-expression
  text.
- [ ] Generated source and diagnostics remain unchanged.
- [ ] Generator tests and the full managed suite pass.
- [ ] `scripts/format.sh --check --all` passes.
- [ ] No runtime codec, public API, native, app, or authored-module file changed.
- [ ] Changed docs contain no machine-specific paths.
- [ ] Plan 029 is marked DONE with evidence.

## STOP conditions

Stop and report if:

- Plan 028 is not complete or its final behavior conflicts with this plan.
- The live generator added another policy derived from codec-expression text
  that the target descriptor does not cover.
- Any externally visible behavior, diagnostic, generated statement, or public
  contract must change; use a separate living-spec change instead.
- Preserving output requires keeping both typed and string-derived policy.
- Any generated source or diagnostic changes.
- Migration requires changing `IJavaScriptCodec<T>` or runtime codecs.
- A verification command fails twice after a reasonable correction.
- An out-of-scope file is required.

## Maintenance notes

Plans 030–033 should extend `ExpoCodecDescriptor` instead of introducing a
second conversion-policy object. Reviewers should reject future codec-name
parsing even when it appears to be a one-line shortcut. The expression remains
an emitted implementation detail; policy belongs in typed fields.
