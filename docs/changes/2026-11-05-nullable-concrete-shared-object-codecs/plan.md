# Nullable Concrete Shared-Object Codecs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A nullable annotation on a valid exact `[ExpoSharedObject]` class, including a sealed `SharedRef<T>` subclass, works on every boundary where the non-null class works, through a new `NullableSharedObjectCodec<T>`, while `SharedObject?`, `SharedRef<T>?`, invalid classes, and nested shapes keep their diagnostics (roadmap item 033).

**Architecture:** One new runtime file holds one static class with the three signatures of `SharedObjectCodec<T>`: it tests `IsNullish` (decode) or `is null` (encode) and otherwise calls the exact codec. The generator drops the nullable-annotation rule in `GetDirectSharedObjectBoundaryIssue` and picks the codec class name by annotation in `TryAnalyzeSharedObjectBoundaryType`. The descriptor row stays `SharedObjectCodec(expr)`, so emission, context passing, and async context capture do not change. The existing pairing hook and the `AsObject` throw prove the null path; no new test seam. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-05-nullable-concrete-shared-object-codecs/spec.md` (binding authority; reconciled against the 032 close `a44051f`). `git diff --stat a44051f..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/033-nullable-concrete-shared-object-codecs.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one null test per operation, then delegate. The exact codec keeps all registry logic.
  2. Performance: null allocates nothing beyond `runtime.CreateNull()` on encode.
  3. Correctness: exact type, owning context, strict identity, released rejection, and teardown behavior are untouched. A shared object is registry-owned, never a leaf holder, so the glue never releases it.
- Exactly one new production file, `Expo.ModulesCore/Codecs/NullableSharedObjectCodec.cs`. The only edited production file is `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`. `SharedObject`, `SharedRef<T>`, `SharedObjectRegistry`, `GeneratedSharedObjectClass`, `SharedObjectCodec<T>`, the emitters, and `ExpoCodecDescriptor` do not change.
- No new diagnostic IDs and no new or changed diagnostic message text. The reason string "which must be used without a nullable annotation" disappears from production code; no other reason text changes.
- No output-contract digest moves. No fixture in `GeneratorOutputContractTests.cs` holds a nullable concrete shared-object class (`rg -nE '(Entry|Other|PolicyEntry|PolicyOther|PolicyLeafEntry|ContractEntry)\?' GeneratorOutputContractTests.cs` prints nothing at plan time; `OptionalSharedObject(SharedObject? value)` at line 176 is the base, which stays rejected). All nine contract tests stay byte-identical.
- 029's typed-policy scan stays empty (it is empty at plan time):
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- The spec's release scan prints nothing (it is empty at plan time; the `\b` keeps `moduleOwnedClasses` out). Any hit is a finding:
  `rg -n 'CodecRelease[^\n]*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
  Do not write a new comment that puts a word starting with `Own` before `SharedObject` or `SharedRef` on one line.
- The spec's test-text scan must print nothing:
  `rg -n 'NullableSharedObjectCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests`
  So a test asserts the absence of the nullable codec with the bare string `"NullableSharedObjectCodec"` only, never with a type argument.
- Pairing hook rule: `SharedObjectRegistry.PairingStepForTesting` (`SharedObjectRegistry.cs:84`, internal; `InternalsVisibleTo("Expo.ModulesCore.Tests")` at `Expo.ModulesCore.csproj:13`) is reached through `context.SharedObjects` (`DotnetRuntimeContext.cs:205`). A first pairing records `InstanceObjectCreated`, `NativeStateAttached`, `WeakObjectCreated` in that order (`SharedObjectRegistry.cs:302-308`). A repeat encode of a live pair records exactly `ExistingWeakObjectLocked` (`:253`). Decode records nothing. So a "no pairing step" check must not run any non-null encode or `new` while the hook is set, and it must end with a control encode that does record steps, so the hook is proven live.
- A count, step list, message, digest, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 a44051f..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                        |
| ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`     |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                         |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                                |
| Full managed suite | `scripts/test-managed.sh` (1176 passed, 0 skipped at 032 close `a44051f`) |

## Review Focus

1. **JavaScript passes a falsy non-null value** (`0`, `false`, `''`) or another primitive (`5`, `'x'`) to a `T?` parameter. Only `null` and `undefined` short-circuit; these fail with `Value is not an object.` like the exact codec, and authored code does not run. Tests: Task 1 `DecodeRejectsNonNullishPrimitivesLikeTheExactCodec`, Task 3 `NullableSharedObjectFailuresMatchTheExactParameter` (`0`, `false`, `''`, `5` rows).
2. **The non-null form stays strict.** `m.echoExact(null)` and `m.echoExact(undefined)` on a non-null `T` parameter still throw a catchable `Error`. Test: Task 3 `NonNullSharedObjectParameterStaysStrict`.
3. **JavaScript omits a required `T?` argument.** The argument-count check rejects the call; it does not become `null`. Test: Task 3 `RequiredNullableSharedObjectStillRequiresItsArgument`.
4. **A `T?` property or shared-object property setter receives a bad value** (plain object, other class, released, foreign, primitive). The setter fails before the authored setter runs, so the stored value stays as it was. Test: Task 3 `NullableSharedObjectFailuresMatchTheExactParameter` (last column).
5. **The same instance goes through `T?` twice.** The second encode returns the strictly equal JavaScript object and the hook records `ExistingWeakObjectLocked`, not a new pairing. Tests: Task 1 `NonNullValueRoundTripsThroughTheExactCodec`, Task 3 `NullableSharedObjectNonNullKeepsIdentity`.

---

### Task 1: Nullable shared-object codec

Model: **cheap**.

This task must land before Task 2. The generator test host compiles generated code against the real `Expo.ModulesCore` assembly (`GeneratorTestHost.cs:109`), so Task 2's acceptance rows need `NullableSharedObjectCodec<T>` to exist.

**Files:**

- Create: `Expo.ModulesCore/Codecs/NullableSharedObjectCodec.cs`
- Test (create): `Expo.ModulesCore.Tests/Codecs/NullableSharedObjectCodecTests.cs`

**Interfaces:**

- Produces (exact bodies from the spec; signatures mirror `SharedObjectCodec.cs:30-34`, `:49-53`, `:70-74`; namespace `Expo.ModulesCore.Codecs`; `using Expo.JSI;`):

```csharp
public static class NullableSharedObjectCodec<T> where T : SharedObject
{
  public static T? Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  public static T? Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value.IsNullish ? null : SharedObjectCodec<T>.Decode(value, runtime, runtimeContext);

  public static JavaScriptValue Encode(T? value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext) =>
      value is null ? runtime.CreateNull() : SharedObjectCodec<T>.Encode(value, runtime, runtimeContext);
}
```

`IsNullish` exists on both `JavaScriptValueRef` (`JavaScriptValueRef.cs:44`) and `JavaScriptValue` (`JavaScriptValue.cs:80`). Doc comments follow `SharedObjectCodec.cs:5-23` in style: a class `<summary>` (nullable form of the exact shared-object codec for an annotated `[ExpoSharedObject]` class) and a `<remarks>` that says JavaScript `null` and `undefined` decode to C# `null`, C# `null` encodes as JavaScript `null`, neither touches the registry, the class registration, or pairing, and every other value goes to `SharedObjectCodec<T>` unchanged. One `<summary>` per method. The repo formatter decides line breaks.

How the tests install a class: `SharedObjectCodec<T>.Encode` looks the class up with `GeneratedSharedObjectClass.GetRegistration` (`GeneratedSharedObjectClass.cs:235-254`), which sees only classes installed through `GeneratedSharedObjectClass.Install` (`:133-151`, public). A bare `SharedObjectClassRegistration.Create` (the `PublicSharedObjectRegistryTests` pattern) does not register there. The test class has one private helper:

```csharp
private static void Install<T>(JavaScriptRuntime runtime, DotnetRuntimeContext context, string name) where T : SharedObject
{
  using var holder = runtime.CreateObject();
  GeneratedSharedObjectClass.Install(context, holder, typeof(T), name, 0, null, null);
}
```

No existing test calls `Install` with `null` installers, but the generated provider does exactly that for a class with no constructor, members, or events: it emits `null` for the factory, the member installer, `eventNames`, and `eventInitializer` (`Emission.cs:476-485`), and `CreateInstallation` skips each `null` (`GeneratedSharedObjectClass.cs:267-298`). `SnapshotEntry` and `PolicyOther` are paired through that path today. Do not fall back to `SharedObjectClassRegistration.Create`.

It uses existing fixtures from `Expo.ModulesCore.Tests.Modules` (`ModuleFixtures.cs`): `SnapshotEntry` (667-672, no events, no `[JS]` constructor, implicit public C# constructor) as `T`, and `SiblingEntry` (696-712) as the other class. Below, `N` is `NullableSharedObjectCodec<SnapshotEntry>` and `X` is `SharedObjectCodec<SnapshotEntry>`.

- [ ] **Step 1: Write the failing tests** in `NullableSharedObjectCodecTests.cs` (namespace `Expo.ModulesCore.Tests.Codecs`, pattern of `NullableJavaScriptCallbackCodecTests.cs:9-118`). Each test runs inside `fixture.Runtime.Execute` with `new DotnetRuntimeContext(runtime)`. `steps` is a `List<SharedObjectPairingStep>` and `context.SharedObjects.PairingStepForTesting = steps.Add` is set as the test says.
  - `DecodeReturnsNullForNullishInputWithoutTouchingTheRegistry`, theory `[InlineData("null")] [InlineData("undefined")]`. No class is installed. Set the hook. `Assert.Null(N.Decode(value.Ref, runtime, context))` and `Assert.Null(N.Decode(value, runtime, context))`. Control, same input: `Assert.Throws<InvalidOperationException>(() => X.Decode(value.Ref, runtime, context))` with a message that contains `Value is not an object.` (`ExpoJsiBridge.cpp:732`; pinned by `JavaScriptValueTests.cs:33`). This is the spec's proof: the exact codec reaches `AsObject` and throws, the nullable one returns first. `Assert.Empty(steps)`, `context.SharedObjects.Count == 0`.
  - `EncodeNullReturnsJavaScriptNullWithoutClassRegistration`. No class is installed. Set the hook. `using var encoded = N.Encode(null, runtime, context)`; `Assert.Equal(JavaScriptValueKind.Null, encoded.Kind)`. Control: `Assert.Throws<InvalidOperationException>(() => X.Encode(new SnapshotEntry(), runtime, context))` with a message that contains `is not installed for this runtime context` (`GeneratedSharedObjectClass.cs:251-253`). That proves the null branch skipped `GetRegistration`. `Assert.Empty(steps)`, `context.SharedObjects.Count == 0`.
  - `DecodeRejectsNonNullishPrimitivesLikeTheExactCodec` (Review Focus 1), theory `[InlineData("0")] [InlineData("false")] [InlineData("''")] [InlineData("5")] [InlineData("'x'")]`. Install `SnapshotEntry` as `"NativeSnapshot"`. For both `N.Decode` overloads: `Assert.Throws<InvalidOperationException>` with a message that contains `Value is not an object.`; the `X.Decode(value.Ref, ...)` message is equal to the `N.Decode(value.Ref, ...)` message. `Assert.Empty(steps)`.
  - `NonNullValueRoundTripsThroughTheExactCodec` (Review Focus 5). Install `SnapshotEntry`. `var instance = new SnapshotEntry()`. Set the hook. `using var first = N.Encode(instance, runtime, context)`: `steps` equals `[InstanceObjectCreated, NativeStateAttached, WeakObjectCreated]`. `steps.Clear()`. `using var second = N.Encode(instance, runtime, context)`: `steps` equals `[ExistingWeakObjectLocked]`. `Assert.True(runtime.StrictEquals(first, second))` (`JavaScriptRuntime.cs:308`). `Assert.Same(instance, N.Decode(first.Ref, runtime, context))`, `Assert.Same(instance, N.Decode(second, runtime, context))`, `Assert.Same(instance, X.Decode(first.Ref, runtime, context))`. `context.SharedObjects.Count == 1`. Step lists are inferred from the pairing hook rule.
  - `NonNullFailuresMatchTheExactCodec`. Install `SnapshotEntry` and `SiblingEntry` (`"Sibling"`) in `context`. Create `contextB = new DotnetRuntimeContext(runtime)` in the same runtime and install `SnapshotEntry` there; dispose `contextB` in a `finally`. For each case, `var nullable = Assert.Throws<InvalidOperationException>(() => N.Decode(v.Ref, runtime, context))`, `var exact = Assert.Throws<InvalidOperationException>(() => X.Decode(v.Ref, runtime, context))`, `Assert.Equal(exact.Message, nullable.Message)`, and `Assert.Contains(expected, nullable.Message)`:
    - plain object `({})` → `NativeState entry for SharedObjectNativeState is missing.` (`JavaScriptObject.cs:83-85`; inferred, the existing `StaleAndForeignObjectsFailWithoutAllocatingAPair` pins only the type);
    - other class, `SharedObjectCodec<SiblingEntry>.Encode(new SiblingEntry(), runtime, context)` → `The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.SnapshotEntry' instance.` (`SharedObjectCodec.cs:100-102`);
    - released, `X.Encode(new SnapshotEntry(), runtime, context)`, then `using var o = released.AsObject(); context.SharedObjects.ReleaseFromJavaScript(o)` (`SharedObjectRegistry.cs:424`, internal) → `The JavaScript object is not an active shared object.` (`SharedObjectRegistry.cs:374-379`);
    - another context, `X.Encode(new SnapshotEntry(), runtime, contextB)` → `The JavaScript object is not an active shared object.` (inferred: the owner check at `SharedObjectRegistry.cs:374-376`).

    Encode side: keep the managed instance of the released case; `N.Encode(releasedInstance, ...)` and `X.Encode(releasedInstance, ...)` both throw `InvalidOperationException` with the same message, which contains `already been released` (inferred, `SharedObjectRegistry.cs:196-199`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~NullableSharedObjectCodecTests"`
Expected: build fails with CS0246 (`NullableSharedObjectCodec<>` not found). Put the observed errors in the task report.

- [ ] **Step 3: Create the codec file** as the Interfaces block states.

- [ ] **Step 4: Run the Step 2 command.** Expected: PASS. A message or step list that differs is a finding.

- [ ] **Step 5: Run both suites.** Run: Generator tests, then ModulesCore tests. Expected: all pass; no generated text changes.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(modules-core): add nullable shared-object codec" \
  -m "NullableSharedObjectCodec<T> decodes null and undefined to C# null and encodes C# null as JavaScript null without registry lookup, class registration, or pairing. Every other value goes to SharedObjectCodec<T> unchanged, so exact type, owning context, strict identity, and released rejection stay as they are." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator selects the nullable shared-object codec

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs:119` (`GetDirectSharedObjectBoundaryIssue`), `:130` (`TryAnalyzeSharedObjectBoundaryType`)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**

- Consumes: Task 1's `NullableSharedObjectCodec<T>`.
- Produces: an annotated valid class resolves to `SharedObjectCodec($"NullableSharedObjectCodec<{exact}>")` (`SharedObjectValidation.cs:139`: `StaticHelper`, `DecodeAndEncode`, `Decode`, `DotnetRuntimeContext`, `Borrowed`), where `exact` is `typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`. Plain `FullyQualifiedFormat` omits the `?`: the comment at `Codecs.cs:41-47` explains that `CodecTypeArgumentFormat` must add `IncludeNullableReferenceTypeModifier` back to keep it. A non-annotated class keeps `SharedObjectCodec<{exact}>`.

Why the annotated class never reaches `TryGetNullableReferenceCodec` (`Codecs.cs:195-226`): every boundary runs shared-object analysis before `GetCodec`, and `IsSharedObjectRelatedType` (`SharedObjectValidation.cs:111`) strips the annotation through `IsPolymorphicSharedObjectBase` (`:98`) and walks base types. Call sites: async result `ModuleAnalysis.cs:482`, sync return `:540`, parameter `:633`, property `:787`, shared-object constructor parameter `SharedObjectModel.cs:122`. Shared-object methods and properties reach the same module call sites through `GetFunctions` and `GetProperties` (`SharedObjectModel.cs:40-43`). `SharedObject?` and `SharedRef<T>?` stop at the first two rules (`SharedObjectValidation.cs:115-116`). Invalid classes stop at `:117-118`. Nested shapes go to `TryFindNestedSharedObjectType` (`:134-136`). A typed event payload never runs shared-object analysis: `GetUnsupportedEventPayload` (`EventAnalysis.cs:368-415`) has no shared-object branch, and `GetCodec` returns `null` for the class, so the event reports "no encode-capable codec is available" (`EventAnalysis.cs:221-230`) in both annotation forms (inferred).

Generated text the acceptance rows assert (from `GetDecodeExpression` `Codecs.cs:450-470`, `GetParameterExpression` `:433-448`, and `Emission.cs`). `$C` stands for `NullableSharedObjectCodec<global::Expo.TestModules.$T>`:

- parameter: `var __expoArg0 = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` (`jsRuntime` in an async method, `Emission.cs:807`)
- optional `= null` module parameter: `var __expoArg0 = arguments.Count <= 0 || arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` (`Codecs.cs:447`; default text `null` from `Codecs.cs:10-13`)
- optional constructor parameter: the same with `arguments.Length` instead of `arguments.Count` (`Emission.cs:307`)
- sync return: `return $C.Encode(module.M(__expoArg0), runtime, GeneratedFunction.CurrentRuntimeContext);` (`Emission.cs:953-956`)
- `Task<T?>`: `var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;` (`Emission.cs:833-838`) and `return global::Expo.JSI.JavaScriptPromiseResult.Resolve(runtime => $C.Encode(__expoResult, runtime, __expoRuntimeContext));` (`:892-895`)
- property getter: `return $C.Encode(module.Value, runtime, GeneratedFunction.CurrentRuntimeContext);` (`Emission.cs:1016`); setter: `var __expoValue = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` (`:1046`)

A shared-object prototype host function always starts with a non-null receiver decode, `var module = SharedObjectCodec<global::Expo.TestModules.Entry>.Decode(thisValue, ...)` (`Emission.cs:813`, `:995`, `:1031`, `:1062`). So the 032 trick of counting codec names does not work here; the helper below counts only argument decodes and encodes.

Existing tests that pin the old rule (the complete list; `rg -n 'without a nullable annotation' Expo.ModulesCore.Generator.Tests` finds lines 2929, 2964, 4352):

- `GeneratorReportsNullableAnnotatedSharedObjectBoundaryUse` (`ExpoModulesGeneratorTests.cs:2884-2933`, four rows: parameter, return, async result, property of `CacheEntry?`). All four become valid. Delete the test; `GeneratorAcceptsNullableConcreteSharedObjects` covers the positions.
- `GeneratorReportsNullableAnnotatedSharedObjectConstructorParameter` (`:2935-2967`, `CacheEntry(CacheEntry? source)`). Becomes valid. Delete it; the first row of `GeneratorAcceptsNullableConcreteSharedObjectsOnSharedObjects` covers it.
- `GeneratorKeepsSharedObjectDiagnosticsForNullableSharedObjectBoundaries` (`:4350-4381`): delete only the row `[InlineData("Entry? value", "which must be used without a nullable annotation")]` (`:4352`). Keep the theory with its `SharedRef<string>? value` row.

No other existing test changes, except the two added rows in Step 3. `GeneratorKeepsTheInvalidConcreteClassReasonForNullableParameters` (`:4474-4515`) asserts `DoesNotContain("SharedObjectCodec<", source)`, which also matches inside `NullableSharedObjectCodec<`, so it stays a valid check for invalid classes. `AssertSingleSharedObjectDiagnostic` (`:4581-4593`) checks `SharedObjectCodec<global::Expo.ModulesCore.SharedObject`; the base stays rejected, so those tests stay green.

- [ ] **Step 1: Delete the two tests and the one row** listed above.

- [ ] **Step 2: Add the source helper and the assertion helper** next to `SharedObjectMemberSource` (`ExpoModulesGeneratorTests.cs:5467`). `ModuleSource` (`:5515`) has no `Classes`, and `SharedObjectMemberSource` owns only `Entry`, so a `SharedRef<T>` subclass declared through `declarations` would be unowned and report `EXPOJSI024`. One module owns both classes here:

```csharp
// Entry derives from SharedObject and Handle from SharedRef<Resource>; one module owns both, so
// neither reports EXPOJSI024. Neither class has a [JS] constructor unless entryMembers adds one.
private static string NullableConcreteSharedObjectSource(string moduleMembers, string entryMembers = "", string declarations = "") =>
    $$"""
    #nullable enable
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Expo.JSI;
    using Expo.ModulesCore;

    namespace Expo.TestModules;

    {{declarations}}

    public sealed class Resource
    {
    }

    [ExpoSharedObject("Entry")]
    public sealed partial class Entry : SharedObject
    {
      {{entryMembers}}
    }

    [ExpoSharedObject("Handle")]
    public sealed partial class Handle : SharedRef<Resource>
    {
      public Handle(Resource resource) : base(resource) { }
    }

    [ExpoModule("Shared", Classes = new[] { typeof(Entry), typeof(Handle) })]
    public sealed partial class SharedModule : Module
    {
      public SharedModule(DotnetRuntimeContext context) : base(context) {}

      {{moduleMembers}}
    }
    """;
```

`private static void AssertNullableSharedObjectGlue(GeneratorRunResult result, string typeName)` where `typeName` is `global::Expo.TestModules.Entry` or `...Handle`. It asserts: `Assert.Empty(GeneratorDiagnosticIds(result))`; `AssertNoCompilerErrors(result)`; `Assert.DoesNotContain(result.Diagnostics, item => item.Id is "CS8604" or "CS8625" or "CS8634")` (CS8604: a `T?` passed to the exact codec's `T value`; CS8634: an annotated type argument against `where T : SharedObject`; inferred IDs); on the generated text, `Assert.DoesNotContain("CodecRelease", ...)` (a shared object is never a leaf holder), `Assert.DoesNotContain($"Codec<{typeName}?>", ...)`, `Assert.Equal(0, CountOccurrences(text, $" SharedObjectCodec<{typeName}>.Decode(arguments"))`, and `Assert.Equal(0, CountOccurrences(text, $" SharedObjectCodec<{typeName}>.Encode("))`. The leading space keeps the last two from matching inside `NullableSharedObjectCodec<`; the receiver decode reads `thisValue`, so it does not match.

- [ ] **Step 3: Write the failing acceptance tests** in `ExpoModulesGeneratorTests.cs`. Both theories take `(string member, string[] fragments)`. In `member` and `fragments`, replace `$C` with `NullableSharedObjectCodec<global::Expo.TestModules.$T>` first, then `$T`. Each test loops `foreach (var type in new[] { "Entry", "Handle" })`, so every row runs for the direct `SharedObject` subclass and for the `SharedRef<T>` subclass. Per run: `AssertNullableSharedObjectGlue(result, $"global::Expo.TestModules.{type}")` and `Assert.Contains(fragment, text, StringComparison.Ordinal)` for every fragment.

`GeneratorAcceptsNullableConcreteSharedObjects` over `NullableConcreteSharedObjectSource(member)` (module members):

| member                                                          | fragments                                                                                                                                                                                                                                                                                                      |
| --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[JS] public int M($T? value) => 0;`                            | `var __expoArg0 = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                                                                                         |
| `[JS] public int M($T? value = null) => 0;`                     | `var __expoArg0 = arguments.Count <= 0 \|\| arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                         |
| `[JS] public $T? M($T? value) => value;`                        | the parameter fragment; `return $C.Encode(module.M(__expoArg0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                                                                            |
| `[JS] public Task<$T?> M($T? value) => Task.FromResult(value);` | `var __expoArg0 = $C.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);`; `var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;`; `return global::Expo.JSI.JavaScriptPromiseResult.Resolve(runtime => $C.Encode(__expoResult, runtime, __expoRuntimeContext));` |
| `[JS] public $T? Value { get; set; }`                           | `return $C.Encode(module.Value, runtime, GeneratedFunction.CurrentRuntimeContext);`; `var __expoValue = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                   |
| `[JS] public $T? Current => null;`                              | `return $C.Encode(module.Current, runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                                                                                                          |

(`\|` is the table escape for `||`.)

`GeneratorAcceptsNullableConcreteSharedObjectsOnSharedObjects` over `NullableConcreteSharedObjectSource(string.Empty, member)` (members of `Entry`):

| member                                                             | fragments                                                                                                                                                                                                               |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[JS] public Entry($T? source) { }`                                | `var __expoArg0 = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`; `ConstructSharedObject_`                                                                                        |
| `[JS] public Entry($T? source = null) { }`                         | `var __expoArg0 = arguments.Length <= 0 \|\| arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` |
| `[JS] public int Take($T? value) => 0;`                            | `var __expoArg0 = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                  |
| `[JS] public $T? Give() => null;`                                  | `return $C.Encode(module.Give(), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                    |
| `[JS] public Task<$T?> GiveAsync() => Task.FromResult<$T?>(null);` | `var __expoRuntimeContext = GeneratedFunction.CurrentRuntimeContext;`; `return global::Expo.JSI.JavaScriptPromiseResult.Resolve(runtime => $C.Encode(__expoResult, runtime, __expoRuntimeContext));`                    |
| `[JS] public $T? Related { get; set; }`                            | `return $C.Encode(module.Related, runtime, GeneratedFunction.CurrentRuntimeContext);`; `var __expoValue = $C.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                          |

- [ ] **Step 4: Write the kept rejections** (spec scenario "Bases and nested shapes stay rejected"). These are characterization tests: they pass before and after the production edit.

`GeneratorKeepsSharedObjectDiagnosticsForNullableBasesAndNestedShapes`, theory `(string declarations, string moduleMembers, string entryMembers, string expectedId, string memberName, string expectedReason, string absentFragment)`. `declarations`, `moduleMembers`, and `entryMembers` hold the token `$N`. Run the generator twice over `NullableConcreteSharedObjectSource(moduleMembers, entryMembers, declarations)`, with `$N` replaced by `""` and by `"?"`. Per run: `AssertNoCompilerErrors(result)`; `Assert.Equal(new[] { expectedId }, GeneratorDiagnosticIds(result))`; the single diagnostic's message contains `$"'{memberName}'"` and `expectedReason`; `Assert.DoesNotContain(absentFragment, text, StringComparison.Ordinal)`; `Assert.DoesNotContain("NullableSharedObjectCodec", text, StringComparison.Ordinal)`. The non-null run is the oracle: if it disagrees with the row, the row is wrong, so stop and report. The oracle column names the existing non-null test; `inferred` rows follow from the mechanisms in the Interfaces block. A row whose column is empty uses `""`.

| declarations                                  | moduleMembers                                                                     | entryMembers                                                    | id           | memberName | expectedReason                                   | absentFragment           | oracle                                                          |
| --------------------------------------------- | --------------------------------------------------------------------------------- | --------------------------------------------------------------- | ------------ | ---------- | ------------------------------------------------ | ------------------------ | --------------------------------------------------------------- |
|                                               | `[JS] public int M(SharedObject$N value) => 0;`                                   |                                                                 | `EXPOJSI023` | `M`        | `which is the polymorphic SharedObject base`     | `"m"`                    | `GeneratorReportsDirectSharedObjectBaseBoundaryUse` (2828-2880) |
|                                               | `[JS] public SharedRef<Resource>$N M() => null!;`                                 |                                                                 | `EXPOJSI023` | `M`        | `which is the SharedRef<T> managed carrier base` | `"m"`                    | same                                                            |
|                                               | `[JS] public int M(IReadOnlyList<Entry$N> values) => 0;`                          |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | `GeneratorReportsNestedSharedObjectComposition` (3072-3112)     |
|                                               | `[JS] public IReadOnlyList<Handle$N> M() => null!;`                               |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | same                                                            |
|                                               | `[JS] public int M(Dictionary<string, Handle$N> values) => 0;`                    |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | same                                                            |
| `public sealed record Holder(Entry$N Field);` | `[JS] public int M(Holder holder) => 0;`                                          |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | same                                                            |
|                                               | `[JS] public int M(JavaScriptCallback<Entry$N> callback) => 0;`                   |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | same                                                            |
|                                               | `[JS] public int M(JavaScriptCallback<ValueTuple<Handle$N>, int> callback) => 0;` |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | `"m"`                    | inferred                                                        |
|                                               | `[Event] public partial Func<Entry$N, Task> OnEntry { get; }`                     |                                                                 | `EXPOJSI019` | `OnEntry`  | `no encode-capable codec is available`           | `"onEntry"`              | inferred                                                        |
|                                               |                                                                                   | `[Event] public partial Func<Handle$N, Task> OnHandle { get; }` | `EXPOJSI027` | `OnHandle` | `no encode-capable codec is available`           | `"onHandle"`             | inferred                                                        |
|                                               |                                                                                   | `[JS] public int Take(IReadOnlyList<Handle$N> values) => 0;`    | `EXPOJSI023` | `Take`     | `inside a composed codec`                        | `"take"`                 | inferred                                                        |
|                                               |                                                                                   | `[JS] public Entry(SharedRef<Resource>$N source) { }`           | `EXPOJSI023` | `Entry`    | `which is the SharedRef<T> managed carrier base` | `ConstructSharedObject_` | inferred                                                        |

In C# string literals the quoted fragments are written `"\"m\""` and so on.

Invalid concrete classes: extend `GeneratorKeepsTheInvalidConcreteClassReasonForNullableParameters` (`:4474-4515`), which already runs `""` and `"?"`. Do not put them in the table above: a nested or generic `[ExpoSharedObject]` class also reports its own `EXPOJSI021` from `CreateSharedObjectModel` (`SharedObjectModel.cs:27-29`), and that test asserts only the single `EXPOJSI023`. Add to its source, after `OpenEntry`:

```csharp
public static class Outer
{
  [ExpoSharedObject]
  public sealed partial class Inner : SharedObject
  {
  }
}

[ExpoSharedObject]
public sealed partial class Box<T> : SharedObject
{
}
```

and two rows: `[InlineData("Outer.Inner", "which must be a top-level, non-generic, sealed [ExpoSharedObject] class")]` and `[InlineData("Box<int>", "which must be a top-level, non-generic, sealed [ExpoSharedObject] class")]`. The `Box<int>` reason is inferred: it assumes `GetAttributes()` on the constructed `Box<int>` returns the definition's attribute, so `:117` passes and `:118` fires. If both runs agree on another reason, report it; do not edit the row.

- [ ] **Step 5: Run the new tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~NullableConcreteSharedObject|FullyQualifiedName~NullableBasesAndNestedShapes|FullyQualifiedName~InvalidConcreteClassReason"`
Expected: every row of both acceptance theories fails with one `EXPOJSI023` whose message contains "which must be used without a nullable annotation". Every kept-rejection row and both new invalid-class rows pass. Put the observed failures in the task report. If a characterization row fails, stop and report. `NullableConcreteSharedObjectSource` gives `Entry` and `Handle` no `[JS]` members for module rows, and no existing zero-diagnostic test uses a fully empty owned class (`GeneratorAcceptsNonNullConcreteSharedObjectParameter` gives `Entry` a `[JS]` constructor). If a non-null run reports a diagnostic that names `Entry` or `Handle` itself, report it; do not add members to the helper on your own.

- [ ] **Step 6: Edit the validator.**
  - `GetDirectSharedObjectBoundaryIssue` (`SharedObjectValidation.cs:119`): the last statement becomes `return null;`. A top-level, non-generic, sealed `[ExpoSharedObject]` class is valid with or without the annotation.
  - `TryAnalyzeSharedObjectBoundaryType` (`:130`): when `issue is null`, build `exact` from `typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)` and select `SharedObjectCodec($"NullableSharedObjectCodec<{exact}>")` when `typeSymbol.NullableAnnotation == NullableAnnotation.Annotated`, else `SharedObjectCodec($"SharedObjectCodec<{exact}>")`. Add one short comment: the annotation selects the nullable codec; the type argument is the exact class without `?`. No new descriptor field and no new helper method.

- [ ] **Step 7: Run the Step 5 command.** Expected: PASS.

- [ ] **Step 8: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped. Every `GeneratorPreserves*` and `GeneratorPins*` contract test passes unchanged. If any contract test fails, stop: unrelated text changed.

- [ ] **Step 9: Run the 029 typed-policy scan, the spec's release scan, and the spec's test-text scan** from Global Constraints. Expected: all three scans print nothing.

- [ ] **Step 10: Commit**

```bash
git commit -m "feat(generator): support nullable concrete shared objects" \
  -m "A valid sealed [ExpoSharedObject] class annotated as nullable, including a concrete SharedRef<T> subclass, now selects NullableSharedObjectCodec<T> on every boundary that accepts the non-null class. The descriptor stays the shared-object row, so emission, runtime-context passing, and async context capture are unchanged." \
  -m "SharedObject?, SharedRef<T>?, invalid classes, nested shapes, and typed event payloads keep their diagnostics; a paired-row theory runs each in its non-null and nullable form. Three old tests that pinned the nullable-annotation rejection are removed. No output-contract digest moves." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove nullable shared objects on Hermes

Model: **standard**.

This task adds fixtures and tests only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (append after `SiblingEntry`, which ends at line 712)
- Test: `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs` (class `GeneratedSharedObjectTests`; insert after `AsyncSharedResultCompletedOnAnotherThreadPreservesIdentity`, which ends at line 1139, so the private `WaitForConditionAsync` helper at `:1176-1200` is reused)

**Interfaces:**

- Consumes: Task 1's codec and Task 2's generator support.
- Produces: a separate module and two separate classes, so the `SharedThings` fixtures and their tests stay untouched. The new module must not list a class that `SharedThingsModule` already lists (`ModuleFixtures.cs:556-562`), or both report `EXPOJSI024`. `TrackedResource` (`:674-679`) is reused as the carried value.

```csharp
[ExpoModule("NullableSharedThings", Classes = new[] { typeof(OptionalEntry), typeof(OptionalTrackedRef) })]
public sealed partial class NullableSharedThingsModule
{
  // The last value Echo or Describe received, so tests can check identity and null from C#.
  public static OptionalEntry? LastEntry;

  [JS] public OptionalEntry MakeEntry(string label)                         // new(label)
  [JS] public OptionalEntry? Echo(OptionalEntry? entry)                      // LastEntry = entry; return entry;
  [JS] public OptionalEntry EchoExact(OptionalEntry entry)                   // entry (the non-null twin, the oracle for failures)
  [JS] public string Describe(OptionalEntry? entry = null)                  // LastEntry = entry; return entry?.Label ?? "null";
  [JS] public async Task<OptionalEntry?> EchoLater(OptionalEntry? entry)     // await Task.Yield(); return entry;
  [JS] public OptionalEntry? Stored { get; set; }
  [JS] public OptionalTrackedRef MakeRef()                                   // new(new TrackedResource())
  [JS] public OptionalTrackedRef? EchoRef(OptionalTrackedRef? value)         // value
  [JS] public async Task<OptionalTrackedRef?> EchoRefLater(OptionalTrackedRef? value) // await Task.Yield(); return value;
  [JS] public OptionalTrackedRef? StoredRef { get; set; }
}

[ExpoSharedObject]
public sealed partial class OptionalEntry : SharedObject
{
  [JS] public OptionalEntry(string label, OptionalEntry? parent = null)      // Label = label; Parent = parent;
  [JS] public string Label { get; }
  [JS] public OptionalEntry? Parent { get; set; }
  [JS] public OptionalEntry? EchoParent(OptionalEntry? value)                // value
  [JS] public OptionalTrackedRef? Tracked { get; set; }
}

[ExpoSharedObject]
public sealed partial class OptionalTrackedRef : SharedRef<TrackedResource>
{
  public static OptionalTrackedRef? LastCreated;
  public OptionalTrackedRef(TrackedResource reference) : base(reference)    // LastCreated = this;
  [JS] public bool IsDisposed => Ref.Disposed;
}
```

The `SharedRef<T>` subclass has no `[JS]` constructor (its carried value has no codec), so it is native-created through `makeRef()`, as `TrackedRef` is (`:681-694`); constructor coverage lives on `OptionalEntry`. JavaScript names follow lower-camel defaults (`m.echo`, `m.echoLater`, `m.stored`, `e.echoParent`, `r.isDisposed`); the constructible class is `m.OptionalEntry`.

Common setup (from `NativeCreatedReturnAndRepeatEncodeKeepStrictIdentity`, `SharedObjectTests.cs:179-202`): `new DotnetRuntimeContext(runtime)`, `context.ModuleRegistry.GetOrCreateDotnetModulesObject()`, `ExpoModulesProvider_Expo_ModulesCore_Tests.Register(context, modules)`, `globalThis.m = globalThis._expoDotnet.modules.NullableSharedThings`. Reset `NullableSharedThingsModule.LastEntry = null` and `OptionalTrackedRef.LastCreated = null` at the start of each test. Async tests keep `context` outside `Execute` and dispose it in the last `Execute`, as `AsyncSharedResultCompletedOnAnotherThreadPreservesIdentity` (`:1090-1139`) does. A JavaScript failure check is `const err = f => { try { f(); return 'no error'; } catch (error) { return error instanceof Error ? error.message : typeof error; } };`. Hook and step rules are in Global Constraints.

- [ ] **Step 1: Add the fixtures** above to `ModuleFixtures.cs`.

- [ ] **Step 2: Write the tests** in `GeneratedSharedObjectTests`:
  - `NullableSharedObjectNullCrossesEveryBoundaryWithoutPairing` (async; spec scenario "Null parameter, return, and property"). Setup also runs `globalThis.e = new m.OptionalEntry('e'); 'ready'`. Then set the hook and read `var before = context.SharedObjects.Count`. Evaluate:
    `[m.echo(null) === null, m.echo(undefined) === null, (m.stored = null, m.stored === null), (m.stored = undefined, m.stored === null), m.echoRef(null) === null, m.echoRef(undefined) === null, (m.storedRef = null, m.storedRef === null), e.parent === null, (e.parent = undefined, e.parent === null), e.echoParent(null) === null, e.echoParent(undefined) === null, (e.tracked = null, e.tracked === null)].join(':')`
    is twelve `true` joined by `:`. `NullableSharedThingsModule.LastEntry` is `null`. Then evaluate `m.echoLater(null).then(v => { globalThis.__later = String(v === null); }, error => { globalThis.__later = 'error:' + error.message; }); m.echoRefLater(undefined).then(v => { globalThis.__refLater = String(v === null); }, error => { globalThis.__refLater = 'error:' + error.message; }); 'pending'`, and `await WaitForConditionAsync(fixture, "typeof globalThis.__later !== 'undefined' && typeof globalThis.__refLater !== 'undefined'")`. `[__later, __refLater].join(':')` is `"true:true"`. `Assert.Empty(steps)` and `context.SharedObjects.Count == before`. Control: `m.makeEntry('x'); 'made'` records exactly `[InstanceObjectCreated, NativeStateAttached, WeakObjectCreated]`.
  - `NullableSharedObjectNonNullKeepsIdentity` (async; scenario "Non-null keeps identity"; Review Focus 5). Setup: `globalThis.a = m.makeEntry('a'); globalThis.b = new m.OptionalEntry('b'); globalThis.r = m.makeRef(); 'ready'`. Evaluate `m.echo(globalThis.a); 'once'` and keep `var first = NullableSharedThingsModule.LastEntry`; `Assert.Equal("a", first!.Label)`. Evaluate it again; `Assert.Same(first, NullableSharedThingsModule.LastEntry)`. Do the same with `globalThis.b` (`Label` is `"b"`). Then set the hook, read `before = context.SharedObjects.Count`, and evaluate:
    `[m.echo(a) === a, m.echo(a) === a, m.echo(b) === b, (m.stored = a, m.stored === a), m.stored === a, m.describe(b), m.echoRef(r) === r, m.echoRef(r) === r, (m.storedRef = r, m.storedRef === r), b.echoParent(a) === a, (b.parent = a, b.parent === a), new m.OptionalEntry('c', a).parent === a, (b.tracked = r, b.tracked === r)].join(':')`
    is `"true:true:true:true:true:b:true:true:true:true:true:true:true"`. `steps.Count(s => s == SharedObjectPairingStep.InstanceObjectCreated)` is 1 (only `'c'` is new) and `context.SharedObjects.Count == before + 1` (inferred). Async: `m.echoLater(a).then(v => { globalThis.__later = String(v === globalThis.a); }, ...)` and `m.echoRefLater(r).then(v => { globalThis.__refLater = String(v === globalThis.r); }, ...)`; after the wait, `"true:true"`.
  - `NullableSharedObjectFailuresMatchTheExactParameter` (scenario "Existing failures stay"; Review Focus 1, 4), theory `(string input, string expected)`:
    - `"({})"`, `NativeState entry for SharedObjectNativeState is missing.` (inferred)
    - `"m.makeRef()"`, `is not a 'Expo.ModulesCore.Tests.Modules.OptionalEntry' instance.`
    - `"globalThis.released"`, `not an active shared object` (as `UseAfterReleaseFailsLoudly`, `:1035-1060`)
    - `"globalThis.foreign"`, `not an active shared object` (inferred)
    - `"0"`, `"false"`, `"''"`, `"5"`, each `Value is not an object.` (inferred)

    Setup: `globalThis.kept = m.makeEntry('kept'); m.stored = kept; globalThis.e = new m.OptionalEntry('e', kept); globalThis.released = m.makeEntry('released'); released.release(); 'ready'`. Another context in the same runtime: `var contextB = new DotnetRuntimeContext(runtime)`; `using var holder = runtime.CreateObject(); GeneratedSharedObjectClass.Install(contextB, holder, typeof(OptionalEntry), "OptionalEntry", 0, null, null);` `using var foreign = SharedObjectCodec<OptionalEntry>.Encode(new OptionalEntry("foreign"), runtime, contextB); using var global = runtime.Global(); global.SetProperty("foreign", foreign);`. Dispose `contextB` in a `finally` inside the same `Execute`. Evaluate, with `const v = () => (<input>);`:
    `[err(() => m.echo(v())), err(() => m.echoExact(v())), err(() => { m.stored = v(); }), err(() => e.echoParent(v())), err(() => { e.parent = v(); }), String(m.stored === kept && e.parent === kept)].join('|')`.
    Split on `|`: parts 0 to 4 each contain `expected`; part 1 equals part 0 (the nullable parameter fails exactly as the non-null one); part 5 is `"true"` (a failed setter did not change the stored value).

  - `NullableSharedRefSubclassDoesNotDisposeItsValue` (scenario "Concrete SharedRef subclass"). Keep `context` outside `using`. Evaluate:
    `const r = m.makeRef(); const results = [m.echoRef(r) === r, m.echoRef(null) === null, (m.storedRef = r, m.storedRef === r), (m.storedRef = null, m.storedRef === null), r.isDisposed]; r.release(); results.join(':')`
    is `"true:true:true:true:false"`; keep `var released = OptionalTrackedRef.LastCreated` and `Assert.False(released!.Ref.Disposed)` (pattern `SharedRefReleaseDoesNotDisposeTheReference`, `:1062-1088`). Then `globalThis.k = m.makeRef(); m.storedRef = k; m.storedRef = null; String(m.echoRef(k) === k)` is `"true"` (wrap in `String(...)`: `AsString()` on a boolean throws `Value is not a string.`, `JavaScriptValueTests.cs:29-33`); keep `var kept = OptionalTrackedRef.LastCreated`. `context.Dispose()`. `Assert.False(released.Ref.Disposed)` and `Assert.False(kept!.Ref.Disposed)`.
  - `OptionalNullableSharedObjectDefaultsToNull` (spec semantics "Optional `= null`"). `globalThis.a = m.makeEntry('a'); [m.describe(), m.describe(undefined), m.describe(null), m.describe(a), new m.OptionalEntry('c').parent === null, new m.OptionalEntry('d', undefined).parent === null, new m.OptionalEntry('f', null).parent === null, new m.OptionalEntry('g', a).parent === a].join(':')` is `"null:null:null:a:true:true:true:true"`. The only legal default of a shared-object reference is `null`, so omission, `undefined`, and explicit `null` cannot be told apart at runtime; Task 2's optional-parameter fragments prove that the default branch exists.
  - `RequiredNullableSharedObjectStillRequiresItsArgument` (Review Focus 3). `[err(() => m.echo()), err(() => m.echoRef()), err(() => globalThis.e.echoParent())].join('|')` with `globalThis.e = new m.OptionalEntry('e')` set first: each part contains `got 0` (`GeneratedFunction.cs:146-148`; inferred). `NullableSharedThingsModule.LastEntry` stays `null`.
  - `NonNullSharedObjectParameterStaysStrict` (Review Focus 2). `[err(() => m.echoExact(null)), err(() => m.echoExact(undefined))].join('|')`: both parts contain `Value is not an object.` (inferred).

- [ ] **Step 3: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~GeneratedSharedObjectTests"`
Expected: PASS. A failing string, count, or step list is a finding to report with the observed value.

- [ ] **Step 4: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped. Record the passed count for Task 4.

- [ ] **Step 5: Commit**

```bash
git commit -m "test(modules-core): prove nullable shared objects on Hermes" \
  -m "A dedicated fixture module, a sealed shared object, and a sealed SharedRef subclass prove that null and undefined cross parameters, returns, Task results, properties, and shared-object constructor, method, and property members as null with no pairing step; that a non-null value keeps strict identity; that plain, other-class, released, foreign-context, and primitive inputs fail as the non-null parameter does; and that the SharedRef subclass never disposes its carried value." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Delete: `docs/plans/033-nullable-concrete-shared-object-codecs.md`
- Delete: `docs/changes/2026-11-05-nullable-concrete-shared-object-codecs/`

- [ ] **Step 1: Merge the deltas into `docs/specs/modules-core-boundary.md`**, as the spec's "Delta against `docs/specs/modules-core-boundary.md`" section states. Line numbers are at plan time.
  - `### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics` (1885-2003), first paragraph (1887-1899): the rejected list becomes `JavaScriptValue`, `SharedObject`, and `SharedRef<T>`.
  - The shared-object identity paragraph (1917-1923) becomes the spec's text: `SharedObject?` and `SharedRef<T>?` SHALL report `EXPOJSI023` with the same reason as the non-null form; a valid concrete class annotated as nullable SHALL select `NullableSharedObjectCodec<T>` (see "Nullable Concrete Shared Objects Bypass The Registry On Null"). Keep its unchanged opening sentence ("A nullable annotation SHALL NOT change shared-object type identity.") and its closing sentence, narrowed to the rejected forms ("A rejected member is rejected at error severity, no binding is emitted, and no shared-object codec is selected."). Report both kept sentences as consistency edits.
  - Rename `#### Scenario: Nullable SharedRef or concrete class reports the shared-object code` (1963-1968) to `#### Scenario: Nullable SharedRef reports the shared-object code`, and drop "or an annotated concrete `[ExpoSharedObject]` class" from its GIVEN.
  - `#### Scenario: Valid concrete shared object stays accepted` (1991-1995): add `- **AND** the same class annotated as nullable SHALL select \`NullableSharedObjectCodec<T>\``.
  - Insert `### Requirement: Nullable Concrete Shared Objects Bypass The Registry On Null` with its five scenarios, verbatim from the spec, right before `### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults` (2125), after 032's callback requirement.
  - Consistency edits the spec does not list but its behavior requires (no new requirement; report them):
    - Codec protocol paragraph: name `NullableSharedObjectCodec<T>` next to `SharedObjectCodec<T>` among the static classes with no `Release`.
    - Descriptor table, row ``Direct `[ExpoSharedObject]` class`` (1533): name it "Direct `[ExpoSharedObject]` class `T` and `T?`"; shape and ownership stay `StaticHelper` and `Borrowed`.
    - `### Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity`, first paragraph (2551-2556): after "or property type", add ", with or without a nullable annotation".
    - `### Requirement: Nullable Reference Codecs Apply Across Generated Binding Surfaces` (2151-2191): "It does not make a shared-object instance type nullable." (2184-2185) is now false. Replace it with "A nullable shared-object instance type follows \"Nullable Concrete Shared Objects Bypass The Registry On Null\" instead and stays rejected in records, collections, callbacks, and typed event payloads."

- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`.** The file has no list of unsupported nullable types (`rg -n 'SharedObject\?|SharedRef<T>\?' docs/specs/ownership-and-scoped-refs.md` prints nothing at plan time), so the spec's "keeps `SharedObject?` and `SharedRef<T>?`" edit has nothing to keep. Under `### Requirement: Boundary Leaf Ownership Is Explicit` (202-211), append to the first paragraph after "...never a leaf holder.": "A nullable concrete `[ExpoSharedObject]` value that is `null` holds nothing; a non-null one stays a borrowed shared object owned by the registry, never a leaf holder." Say in the task report that there was no list.

- [ ] **Step 3: Update `docs/module-authoring-guide.md`.**
  - Unsupported annotated references paragraph (184-187): drop the concrete class. Result: "`JavaScriptValue?`, `SharedObject?`, and `SharedRef<T>?` are build diagnostics. `JavaScriptValue` already represents JavaScript `null` and `undefined`. The others carry shared-object identity, and a null axis multiplies those states. A nullable concrete `[ExpoSharedObject]` class is supported; see [Shared objects](#8-shared-objects)."
  - Section 8 boundary rule (515-519): "Shared-object parameters, returns, and properties must use the exact sealed authored type directly at the boundary, optionally annotated as nullable (`ExampleCounter?`). JavaScript `null` and `undefined` arrive as C# `null`, and C# `null` returns JavaScript `null`. The `SharedObject`/`SharedRef<T>` bases, with or without `?`, and nested composition (records, lists, dictionaries, callbacks) are rejected."

- [ ] **Step 4: Update `docs/roadmap.md`.** Row 033 (line 58): `DONE`, brief `—`; keep the column widths aligned. In the `**028–033**` note (115-145), replace "033 then applies its own null policy; its brief needs a rebase on this design first." with: "033 is done: it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). A valid concrete `[ExpoSharedObject]` class, including a sealed `SharedRef<T>` subclass, now works as `T?` on every boundary that accepts `T`; `null` and `undefined` decode to C# `null` with no registry work." Keep the following sentence about `JavaScriptValue?` and the bases. Take `<N>` from Step 6.

- [ ] **Step 5: Delete the brief and the change folder** with `git rm -r`; git history keeps them. Fix every link to the old paths: `rg -n '033-nullable-concrete-shared-object-codecs|2026-11-05-nullable-concrete-shared-object-codecs' docs .agents AGENTS.md` prints nothing.

- [ ] **Step 6: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'CodecRelease[^\n]*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
rg -n 'NullableSharedObjectCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --unified=0 a44051f..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped; the passed count is 1176 plus the test cases added in Tasks 1-3, minus the six deleted cases (four rows of `GeneratorReportsNullableAnnotatedSharedObjectBoundaryUse`, `GeneratorReportsNullableAnnotatedSharedObjectConstructorParameter`, and the `Entry? value` row); put the arithmetic in the report. Format is OK and `git diff --check` is clean. All three scans print nothing.

- [ ] **Step 7: Commit**

```bash
git commit -m "docs(modules-core): close nullable concrete shared-object codecs" \
  -m "Merge the accepted delta into the living specs and the authoring guide, remove the brief and the change folder, and mark roadmap item 033 DONE." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
