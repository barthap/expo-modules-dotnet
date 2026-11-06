# Base-Class Shared-Object Arguments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An authored abstract class that derives from `SharedObject` works as a parameter type of generated module methods and of shared-object constructors and methods, through a new decode-only `SharedObjectBaseCodec<TBase>`, while returns, properties, the nullable form, nested shapes, and the `SharedObject` and `SharedRef<T>` bases keep `EXPOJSI023` (roadmap item 034).

**Architecture:** One new runtime file holds one static class with the two `Decode` overloads of `SharedObjectCodec<T>` and no `Encode`: it resolves the registry entry exactly as the exact codec does, then requires `managed is TBase`. The generator passes an explicit `isParameter` flag to `TryAnalyzeSharedObjectBoundaryType`, splits the attribute rule in `GetDirectSharedObjectBoundaryIssue` for an unattributed abstract class, and selects a new `StaticHelper`/`DecodeOnly` descriptor row. Emission does not change. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-06-base-class-shared-object-arguments/spec.md` (binding authority; committed at `a43800e`). Base commit is the 033 close `482e395`; `git diff --stat 482e395..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/034-base-class-shared-object-arguments.md` is background only; the spec wins (it drops the brief's compile-time subclass count, spec decision 1).

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`. Line numbers are at plan time.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one new decode-only static helper, one explicit `isParameter` flag on the boundary validator, and no assignability near the exact codec or prototype selection.
  2. Performance: decode is the existing registry resolution plus one managed `is` test. No reflection, no scan.
  3. Correctness: the registry holds only instances of generated sealed classes, so `managed is TBase` is a complete check. Foreign-context, released, plain-object, and wrong-class failures stay the registry's and the `is` test's.
- Exactly one new production file, `Expo.ModulesCore/Codecs/SharedObjectBaseCodec.cs`. The only edited production files are `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs`, `ExpoModulesGenerator.ModuleAnalysis.cs`, and `ExpoModulesGenerator.SharedObjectModel.cs`. `SharedObject`, `SharedRef<T>`, `SharedObjectRegistry`, `GeneratedSharedObjectClass`, `SharedObjectCodec<T>`, `NullableSharedObjectCodec<T>`, `ExpoCodecDescriptor.cs`, `ExpoModulesGenerator.Codecs.cs`, and `ExpoModulesGenerator.Emission.cs` do not change.
- `SharedObjectBaseCodec<TBase>` has no `Encode` method and never mentions `GeneratedConversionScope`.
- No new diagnostic IDs. Exactly three new reason strings, copied verbatim from the spec (Task 2 names them `R1`, `R2`, `R3`). No existing reason text changes.
- No existing test is deleted. The three tests that pin "not marked [ExpoSharedObject]" (`GeneratorReportsSharedObjectConstructorParameterBoundaryUse` row at `ExpoModulesGeneratorTests.cs:2710-2712`, `GeneratorReportsDirectSharedObjectBaseBoundaryUse` row at `:2846-2849`, `GeneratorKeepsTheInvalidConcreteClassReasonForNullableParameters` row at `:4390`) all use the sealed, non-abstract `UnattributedEntry` (`:2723`, `:2865`, `:4405`), so they keep that reason unchanged.
- No output-contract digest moves. No fixture in `GeneratorOutputContractTests.cs` declares an abstract class (`rg -n abstract GeneratorOutputContractTests.cs` prints nothing at plan time). All contract tests stay byte-identical.
- 029's typed-policy scan stays empty (it is empty at plan time). The new code tests symbols, never codec text:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- The spec's three scans print nothing (all empty at plan time). Any hit is a finding:
  - `rg -n 'GeneratedConversionScope.*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`. Do not write a line that puts `GeneratedConversionScope` before `SharedObject`, or a word starting with `Own` before `SharedObject` or `SharedRef`.
  - `rg -n 'SharedObjectBaseCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests`. A test asserts the absence of the base codec with the bare string `"SharedObjectBaseCodec"` only.
  - `rg -n 'IsAssignableFrom|IsAssignableTo|\.IsSubclassOf' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs`
- Pairing hook rule: `SharedObjectRegistry.PairingStepForTesting` (`SharedObjectRegistry.cs:84`, internal; reached through `context.SharedObjects`). A first encode records `InstanceObjectCreated`, `NativeStateAttached`, `WeakObjectCreated` in that order (`:302-308`; pinned by `NullableSharedObjectCodecTests.NonNullValueRoundTripsThroughTheExactCodec`). A JavaScript `new` also records `InstanceObjectCreated` once (033's `NullableSharedObjectNonNullKeepsIdentity`). Decode records nothing. So a "no pairing step" check runs only decodes while the hook is set, and ends with a control that does record steps.
- JavaScript error text rule: a sync host-function failure reaches JavaScript as an `Error` whose `message` is the managed exception text with a stack trace. Two surfaces have different frames, so compare only the first line (`Split('\n')[0]`). A rejected Promise carries `exception.Message` only (`GeneratedFunction.cs:91-103`).
- A count, step list, message, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 482e395..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1213 passed, 0 skipped at `482e395`)                                                                                            |

Test-case arithmetic (each `InlineData` row is one case): Task 1 adds 10, Task 2 adds 29, Task 3 adds 11, nothing is deleted. The full suite reads 1223 after Task 1, 1252 after Task 2, and 1263 after Task 3.

## Review Focus

1. **JavaScript passes an instance of a JavaScript subclass** (`class SubFile extends m.FileEntry {}`) to a base parameter. Authored code receives the original managed `FileEntry`, and the object keeps `SubFile.prototype`. No Hermes test builds a JavaScript subclass of a generated class today; the expectation rests on the re-parenting described at `GeneratedSharedObjectClass.cs:69-85`. Test: Task 3 `SharedObjectBaseParameterReceivesEverySubclass`.
2. **JavaScript passes a live instance of a shared class outside the base** (`SnapshotEntry`). Decode fails with the base codec's `is not a '...PathBase' instance.` text, and no authored code runs, on sync, async, method, and constructor surfaces. Tests: Task 1 `DecodeRejectsAnInstanceOfAnUnrelatedSharedClass`, Task 3 `SharedObjectBaseParameterFailuresMatchTheExactParameter` (`globalThis.snapshot` row), Task 3 `SharedObjectBaseParameterKeepsIdentityAcrossAwait` (rejection part).
3. **A bad base argument to a `[JS]` constructor** (`new m.DirEntry('x', {})`). The constructor fails before authored code runs and leaves no half-paired `DirEntry` in the registry. Test: Task 3 `SharedObjectBaseParameterFailuresMatchTheExactParameter` (registry count check).
4. **An author marks the abstract base `[ExpoSharedObject]`.** The attributed path keeps its existing rules, so the base reports "must be a top-level, non-generic, sealed" (plus `EXPOJSI021` on the declaration) instead of being accepted. Test: Task 2, new `AbstractEntry` row in `GeneratorKeepsTheInvalidConcreteClassReasonForNullableParameters`.
5. **An abstract base derives from `SharedRef<X>`** instead of `SharedObject` directly. The spec accepts it (its chain reaches `SharedObject`, and it is not `SharedRef<T>` itself), so it selects the base codec. Test: Task 2 `GeneratorAcceptsAbstractBaseOfSharedRefSubclasses`.

---

### Task 1: Shared-object base codec

Model: **cheap**.

This task must land before Task 2. The generator test host compiles generated code against the real `Expo.ModulesCore` assembly, so Task 2's acceptance rows need `SharedObjectBaseCodec<TBase>` to exist.

**Files:**

- Create: `Expo.ModulesCore/Codecs/SharedObjectBaseCodec.cs`
- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (append after line 806, the end of `OptionalTrackedRef`)
- Test (create): `Expo.ModulesCore.Tests/Codecs/SharedObjectBaseCodecTests.cs`

**Interfaces:**

- Produces (namespace `Expo.ModulesCore.Codecs`, `using Expo.JSI;`):

```csharp
public static class SharedObjectBaseCodec<TBase> where TBase : SharedObject
{
  public static TBase Decode(JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext);
  public static TBase Decode(JavaScriptValue value, JavaScriptRuntime runtime, DotnetRuntimeContext runtimeContext);
}
```

The two overloads copy `SharedObjectCodec.cs:30-41` and `:49-60` (argument checks; `using var owned = value.Retain()` in the ref overload) and share one private `DecodeOwned` that copies `:94-105` with one change: the check is `managed is not TBase instance` with no `GetType()` equality. The message stays `$"The shared object receiver is not a '{typeof(TBase)}' instance."`. `ResolveManaged` (`SharedObjectRegistry.cs:364`) is internal and the codec is in the same assembly. Doc comments follow `NullableSharedObjectCodec.cs:5-15` in style: a class `<summary>` (decodes a parameter typed as an authored abstract shared-object base to the original instance of whichever generated sealed subclass JavaScript passed) and a `<remarks>` that says: decode resolves the registry entry as `SharedObjectCodec{T}` does and accepts the managed instance when it is a `TBase`; only generated sealed classes are ever registered, so that test is complete; plain, foreign, and released objects fail in the registry; there is no `Encode`, because a JavaScript prototype belongs to one exact class; the managed instance stays caller-borrowed. Copy the per-method `<summary>` and the ownership `<remarks>` of the exact codec. Obey the scope-scan rule in Global Constraints.

- Produces (fixtures, namespace `Expo.ModulesCore.Tests.Modules`). Task 3 adds members to these; nothing here takes a `PathBase` through `[JS]`, because the generator rejects that until Task 2.

```csharp
// The authored abstract base of FileEntry and DirEntry. It has no [ExpoSharedObject] attribute.
public abstract class PathBase : SharedObject
{
  protected PathBase(string name)   // Name = name;
  public string Name { get; }
  // The concrete class name plus the name, so a test sees which instance authored code received.
  public string Describe()          // => $"{GetType().Name}:{Name}";
}

[ExpoSharedObject]
public sealed partial class FileEntry : PathBase
{
  [JS] public FileEntry(string name) : base(name) { }
}

[ExpoSharedObject]
public sealed partial class DirEntry : PathBase
{
  public DirEntry(string name, PathBase parent) : base(name)   // Parent = parent; (no [JS] yet)
  public PathBase Parent { get; }
}

// Owns FileEntry and DirEntry, so neither reports EXPOJSI024. No other module may list them.
[ExpoModule("BaseThings", Classes = new[] { typeof(FileEntry), typeof(DirEntry) })]
public sealed partial class BaseThingsModule
{
}
```

How the tests install a class: copy the private `Install<T>` helper of `NullableSharedObjectCodecTests.cs:234-239` (`GeneratedSharedObjectClass.Install(context, holder, typeof(T), name, 0, null, null)`). Below, `B` is `SharedObjectBaseCodec<PathBase>` and `X` is `SharedObjectCodec<FileEntry>`, the exact twin. `SnapshotEntry` (`ModuleFixtures.cs:667-672`) is the unrelated class.

- [ ] **Step 1: Add the fixtures** above to `ModuleFixtures.cs`.

- [ ] **Step 2: Write the failing tests** in `SharedObjectBaseCodecTests.cs` (namespace `Expo.ModulesCore.Tests.Codecs`, same `using`s, fixture pattern, and `steps`/hook pattern as `NullableSharedObjectCodecTests.cs`). Each test runs inside `fixture.Runtime.Execute` with `using var context = new DotnetRuntimeContext(runtime)`.
  - `DecodeReturnsTheOriginalInstanceOfEverySubclass` (spec scenario "Base parameter receives any generated subclass"). Install `FileEntry` (`"FileEntry"`) and `DirEntry` (`"DirEntry"`). `var file = new FileEntry("f"); var dir = new DirEntry("d", file);` Encode both with their exact codecs (`using var fileValue = X.Encode(file, ...)`, `using var dirValue = SharedObjectCodec<DirEntry>.Encode(dir, ...)`) before the hook is set. Set the hook. `Assert.Same(file, B.Decode(fileValue.Ref, runtime, context))`, `Assert.Same(file, B.Decode(fileValue, runtime, context))`, the same two for `dir`, then `Assert.Same(file, B.Decode(fileValue.Ref, runtime, context))` again (repeated decode). `Assert.Empty(steps)`; `Assert.Equal(2, context.SharedObjects.Count)`. Control: `using var paired = X.Encode(new FileEntry("x"), runtime, context)`; `steps` equals `[InstanceObjectCreated, NativeStateAttached, WeakObjectCreated]`.
  - `DecodeRejectsAnInstanceOfAnUnrelatedSharedClass` (Review Focus 2). Install `FileEntry` and `SnapshotEntry` (`"NativeSnapshot"`). `using var other = SharedObjectCodec<SnapshotEntry>.Encode(new SnapshotEntry(), runtime, context)`. Set the hook. Both `B.Decode` overloads throw `InvalidOperationException` whose message `Assert.Equal`s `The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.` (the spec's text). Wording control: `X.Decode(other.Ref, ...)` throws with `The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.FileEntry' instance.`. `Assert.Empty(steps)`.
  - `DecodeFailuresMatchTheExactCodec` (spec scenario "Existing failures stay"). Copy the structure of `NullableSharedObjectCodecTests.NonNullFailuresMatchTheExactCodec` (`:152-211`) with `FileEntry` in place of `SnapshotEntry`: install `FileEntry` in `context` and in `contextB` (same runtime, disposed in a `finally`). A private `AssertDecodeFailuresMatch(value, expected, runtime, context)` compares `B` against `X` for both overloads: first `Assert.Equal(exact.Message, baseCodec.Message)`, then `Assert.Contains(expected, baseCodec.Message)`. Cases:
    - plain object `({})` → `NativeState entry for SharedObjectNativeState is missing.`
    - released (`X.Encode(new FileEntry("r"), ...)`, then `context.SharedObjects.ReleaseFromJavaScript(...)` on its object) → `The JavaScript object is not an active shared object.`
    - another context (`X.Encode(new FileEntry("o"), runtime, contextB)`) → `The JavaScript object is not an active shared object.`
  - `DecodeRejectsNonObjectsLikeTheExactCodec`, theory `[InlineData("null")] [InlineData("undefined")] [InlineData("0")] [InlineData("false")] [InlineData("''")] [InlineData("5")] [InlineData("'x'")]`. Install `FileEntry`, evaluate the input, set the hook. For both overloads: `B` and `X` both throw `InvalidOperationException`; the messages are equal, then each contains `Value is not an object.` (the exact codec's behavior on `null` and `undefined` is pinned by `NullableSharedObjectCodecTests.DecodeReturnsNullForNullishInputWithoutTouchingTheRegistry`). `Assert.Empty(steps)`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~SharedObjectBaseCodecTests"`
Expected: build fails with CS0246 (`SharedObjectBaseCodec<>` not found). Put the observed errors in the task report. If the build fails for any other reason (for example `EXPOJSI021` or `EXPOJSI024` on the new fixtures), stop and report.

- [ ] **Step 4: Create the codec file** as the Interfaces block states.

- [ ] **Step 5: Run the Step 3 command.** Expected: PASS, 10 cases. A message or step list that differs is a finding.

- [ ] **Step 6: Run both suites.** Run: Generator tests, then ModulesCore tests. Expected: all pass; no generated text changes. Full suite count 1223.

- [ ] **Step 7: Commit**

```bash
git commit -m "feat(modules-core): add shared-object base codec" \
  -m "SharedObjectBaseCodec<TBase> decodes a JavaScript shared object to the original managed instance when that instance is a TBase. It resolves the registry entry exactly as SharedObjectCodec<T> does, so plain, foreign-context, and released objects fail with the same messages, and an instance of an unrelated shared class fails with the exact codec's wording. It has no Encode: a prototype belongs to one exact class." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator accepts an authored abstract base as a parameter

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectValidation.cs:113-120` (`GetDirectSharedObjectBoundaryIssue`), `:124-143` (`TryAnalyzeSharedObjectBoundaryType`), `:145` (add a descriptor helper beside `SharedObjectCodec`)
- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.ModuleAnalysis.cs:482`, `:540`, `:633`, `:787`; `Expo.ModulesCore.Generator/ExpoModulesGenerator.SharedObjectModel.cs:122`
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

**Interfaces:**

- Consumes: Task 1's `SharedObjectBaseCodec<TBase>.Decode`.
- Produces:
  - `TryAnalyzeSharedObjectBoundaryType(ITypeSymbol typeSymbol, string memberName, string positionDescription, bool isParameter, Location? location, List<ExpoDiagnosticModel> diagnostics, out ExpoCodecDescriptor? codec)`. Call sites pass it as the named argument `isParameter: true` or `isParameter: false`:
    - `ModuleAnalysis.cs:482` async result: `false`
    - `ModuleAnalysis.cs:540` return: `false`
    - `ModuleAnalysis.cs:633` method parameter: `true` (shared-object methods reach it through `GetFunctions`, `SharedObjectModel.cs:43`)
    - `ModuleAnalysis.cs:787` property: `false` (shared-object properties reach it through `GetProperties`, `SharedObjectModel.cs:45`)
    - `SharedObjectModel.cs:122` `[JS]` constructor parameter: `true`
  - `GetDirectSharedObjectBoundaryIssue(ITypeSymbol typeSymbol, bool isParameter)`. Rule order (load-bearing: `SharedObject` is itself abstract and unattributed):
    1. `IsPolymorphicSharedObjectBase` → existing reason (unchanged).
    2. `SharedRef<T>` original definition → existing reason (unchanged).
    3. Not `HasExpoSharedObjectAttribute` and not `typeSymbol.IsAbstract` → `which is not marked [ExpoSharedObject]` (unchanged).
    4. Not attributed and abstract (an authored base), first match wins:
       - `!isParameter` → `R1` = `which is an abstract shared-object base and is supported only as a parameter`
       - `typeSymbol.NullableAnnotation == NullableAnnotation.Annotated` → `R2` = `which is an abstract shared-object base and must be used without a nullable annotation`
       - `typeSymbol.ContainingType is not null || typeSymbol is INamedTypeSymbol { IsGenericType: true }` → `R3` = `which is an abstract shared-object base and must be a top-level, non-generic class`
       - otherwise `null` (accepted).
    5. Attributed → the existing top-level, non-generic, sealed rule (unchanged).
  - In `TryAnalyzeSharedObjectBoundaryType`, when `issue is null`: if `!HasExpoSharedObjectAttribute(typeSymbol)` (only an accepted authored base reaches this with no attribute), `codec = SharedObjectBaseCodec($"SharedObjectBaseCodec<{exact}>")`; otherwise keep the 033 nullable/exact selection. `exact` stays `typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)`.
  - `private static ExpoCodecDescriptor SharedObjectBaseCodec(string expression)` beside `SharedObjectCodec` (`:145`): `ExpoCodecShape.StaticHelper`, `ExpoCodecCapabilities.DecodeOnly`, `ExpoDecodeOperation.Decode`, `ExpoContextRequirement.DotnetRuntimeContext`, `ExpoResourceOwnership.Borrowed`. One short comment: an authored abstract base is decode-only and accepted only at parameters.

Why no other code changes: a rejected position returns `true` with `codec == null`, and every call site then skips the member (`continue` or `return null`), so the `DecodeOnly` checks at `ModuleAnalysis.cs:510` and `:566` never see the base codec, and no `EXPOJSI001/002/015` follows. Nested shapes still go to `TryFindNestedSharedObjectType` (`SharedObjectValidation.cs:140-141`), which reports the composed-codec reason for any shared-object-related type, abstract or not. A typed event payload never runs shared-object analysis; `GetCodec` returns `null` for the class, so the event reports "no encode-capable codec is available" (`EventAnalysis.cs:221-230`; inferred, as in 033).

Generated text, from `GetDecodeExpression` (`Codecs.cs:450-470`), the method emitter (`Emission.cs:807`, `:813`, `:855`), and the constructor factory (`Emission.cs:303`). `$B` stands for `SharedObjectBaseCodec<global::Expo.TestModules.PathBase>`:

- sync parameter at index `i`: `var __expoArg{i} = $B.Decode(arguments.GetValue({i}), runtime, GeneratedFunction.CurrentRuntimeContext);`
- async parameter: the same with `jsRuntime` in place of `runtime`
- shared-object method receiver (unchanged): `var module = SharedObjectCodec<global::Expo.TestModules.DirEntry>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext);` (`jsRuntime` when async)

- [ ] **Step 1: Add the helpers** next to `NullableConcreteSharedObjectSource` (`ExpoModulesGeneratorTests.cs:5526`):

```csharp
// PathBase is an authored abstract base with no attribute. FileEntry and DirEntry are its
// generated sealed subclasses, and one module owns both, so neither reports EXPOJSI024.
// Neither class has a [JS] constructor unless dirMembers adds one.
private static string SharedObjectBaseSource(string moduleMembers, string dirMembers = "", string declarations = "") =>
    $$"""
    #nullable enable
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Expo.JSI;
    using Expo.ModulesCore;

    namespace Expo.TestModules;

    {{declarations}}

    public abstract class PathBase : SharedObject
    {
    }

    [ExpoSharedObject("FileEntry")]
    public sealed partial class FileEntry : PathBase
    {
    }

    [ExpoSharedObject("DirEntry")]
    public sealed partial class DirEntry : PathBase
    {
      {{dirMembers}}
    }

    [ExpoModule("Paths", Classes = new[] { typeof(FileEntry), typeof(DirEntry) })]
    public sealed partial class PathsModule : Module
    {
      public PathsModule(DotnetRuntimeContext context) : base(context) {}

      {{moduleMembers}}
    }
    """;
```

`private static void AssertSharedObjectBaseGlue(GeneratorRunResult result, string baseTypeName)`, where `baseTypeName` is for example `global::Expo.TestModules.PathBase`: `Assert.Empty(GeneratorDiagnosticIds(result))`; `AssertNoCompilerErrors(result)`; on `GeneratedText(result)`: `DoesNotContain("GeneratedConversionScope")`, `DoesNotContain($"SharedObjectCodec<{baseTypeName}>")` (this also covers `NullableSharedObjectCodec<...>`, which contains it), and `DoesNotContain($"SharedObjectBaseCodec<{baseTypeName}>.Encode")`. Also add the constants `R1`, `R2`, `R3` as `private const string` fields with the Interfaces text, named `AbstractBaseParameterOnlyReason`, `AbstractBaseNullableReason`, `AbstractBaseShapeReason`.

- [ ] **Step 2: Write the failing acceptance tests.**

`GeneratorAcceptsSharedObjectBaseParameters`, theory `(string moduleMembers, string dirMembers, string[] fragments)` over `SharedObjectBaseSource(moduleMembers, dirMembers)`. Replace `$B` in fragments first. Per row: `AssertSharedObjectBaseGlue(result, "global::Expo.TestModules.PathBase")` and `Assert.Contains(fragment, text, StringComparison.Ordinal)` for every fragment.

| moduleMembers                                                   | dirMembers                                                                  | fragments                                                                                                                                                                                                                                            |
| --------------------------------------------------------------- | --------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[JS] public int M(PathBase path) => 0;`                        |                                                                             | `var __expoArg0 = $B.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                               |
| `[JS] public string M(string name, PathBase path) => name;`     |                                                                             | `var __expoArg1 = $B.Decode(arguments.GetValue(1), runtime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                               |
| `[JS] public Task<int> M(PathBase path) => Task.FromResult(0);` |                                                                             | `var __expoArg0 = $B.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);`                                                                                                                                             |
|                                                                 | `[JS] public DirEntry(string name, PathBase parent) { }`                    | `var __expoArg1 = $B.Decode(arguments.GetValue(1), runtime, GeneratedFunction.CurrentRuntimeContext);`; `ConstructSharedObject_`                                                                                                                     |
|                                                                 | `[JS] public int Compare(PathBase other) => 0;`                             | `var module = SharedObjectCodec<global::Expo.TestModules.DirEntry>.Decode(thisValue, runtime, GeneratedFunction.CurrentRuntimeContext);`; `var __expoArg0 = $B.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`     |
|                                                                 | `[JS] public Task<int> CompareLater(PathBase other) => Task.FromResult(0);` | `var module = SharedObjectCodec<global::Expo.TestModules.DirEntry>.Decode(thisValue, jsRuntime, GeneratedFunction.CurrentRuntimeContext);`; `var __expoArg0 = $B.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);` |

`GeneratorAcceptsAbstractBaseOfSharedRefSubclasses` (Review Focus 5), a fact with its own source: `#nullable enable`, `using Expo.ModulesCore;`, namespace `Expo.TestModules`, `public sealed class Resource {}`, `public abstract class HandleBase : SharedRef<Resource> { protected HandleBase(Resource resource) : base(resource) { } }`, `[ExpoSharedObject("Handle")] public sealed partial class Handle : HandleBase { public Handle(Resource resource) : base(resource) { } }`, and `[ExpoModule("Handles", Classes = new[] { typeof(Handle) })] public sealed partial class HandlesModule { [JS] public int M(HandleBase handle) => 0; }`. Assert `AssertSharedObjectBaseGlue(result, "global::Expo.TestModules.HandleBase")` and that the text contains `var __expoArg0 = SharedObjectBaseCodec<global::Expo.TestModules.HandleBase>.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`.

- [ ] **Step 3: Write the rejection tests.**

`GeneratorKeepsSharedObjectDiagnosticsForBasesOutsideParameters` (spec scenario "Non-parameter positions and the nullable form stay rejected"), theory `(string declarations, string moduleMembers, string dirMembers, string expectedId, string memberName, string reason, string annotatedReason, string absentFragment)`. The three source columns hold the token `$N`. Run the generator twice over `SharedObjectBaseSource(moduleMembers, dirMembers, declarations)`, with `$N` replaced by `""` (expect `reason`) and by `"?"` (expect `annotatedReason`). Per run: `AssertNoCompilerErrors(result)`; `Assert.Equal(new[] { expectedId }, GeneratorDiagnosticIds(result))`; the single generator diagnostic's message contains `$"'{memberName}'"` and the expected reason; `Assert.DoesNotContain(result.Diagnostics, item => item.Id is "EXPOJSI001" or "EXPOJSI002" or "EXPOJSI015")`; the generated text does not contain `absentFragment` and does not contain `"SharedObjectBaseCodec"`. The two reason columns differ only where rule order makes the `?` run stop at `R2` first. Rows marked `inferred` follow from the mechanisms in the Interfaces block; the others mirror an existing 033 row for a concrete class.

| declarations                                                                        | moduleMembers                                                                       | dirMembers                                                      | id           | memberName | reason                                           | annotatedReason | absentFragment |
| ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- | --------------------------------------------------------------- | ------------ | ---------- | ------------------------------------------------ | --------------- | -------------- |
|                                                                                     | `[JS] public PathBase$N M() => null!;`                                              |                                                                 | `EXPOJSI023` | `M`        | `R1`                                             | `R1`            | `"m"`          |
|                                                                                     | `[JS] public Task<PathBase$N> M() => null!;`                                        |                                                                 | `EXPOJSI023` | `M`        | `R1`                                             | `R1`            | `"m"`          |
|                                                                                     | `[JS] public PathBase$N Stored { get; set; } = null!;`                              |                                                                 | `EXPOJSI023` | `Stored`   | `R1`                                             | `R1`            | `"stored"`     |
|                                                                                     |                                                                                     | `[JS] public PathBase$N Give() => null!;`                       | `EXPOJSI023` | `Give`     | `R1`                                             | `R1`            | `"give"`       |
|                                                                                     |                                                                                     | `[JS] public PathBase$N Link { get; set; } = null!;`            | `EXPOJSI023` | `Link`     | `R1`                                             | `R1`            | `"link"`       |
|                                                                                     | `[JS] public int M(IReadOnlyList<PathBase$N> paths) => 0;`                          |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | same            | `"m"`          |
|                                                                                     | `[JS] public int M(Dictionary<string, PathBase$N> paths) => 0;`                     |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | same            | `"m"`          |
| `public sealed record Holder(PathBase$N Field);`                                    | `[JS] public int M(Holder holder) => 0;`                                            |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | same            | `"m"`          |
|                                                                                     | `[JS] public int M(JavaScriptCallback<PathBase$N> callback) => 0;`                  |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | same            | `"m"`          |
|                                                                                     | `[JS] public int M(JavaScriptCallback<ValueTuple<PathBase$N>, int> callback) => 0;` |                                                                 | `EXPOJSI023` | `M`        | `inside a composed codec`                        | same            | `"m"`          |
|                                                                                     | `[Event] public partial Func<PathBase$N, Task> OnPath { get; }`                     |                                                                 | `EXPOJSI019` | `OnPath`   | `no encode-capable codec is available`           | same            | `"onPath"`     |
|                                                                                     |                                                                                     | `[Event] public partial Func<PathBase$N, Task> OnPath { get; }` | `EXPOJSI027` | `OnPath`   | `no encode-capable codec is available`           | same            | `"onPath"`     |
| `public abstract class GenericBase<T> : SharedObject { }`                           | `[JS] public int M(GenericBase<int>$N path) => 0;`                                  |                                                                 | `EXPOJSI023` | `M`        | `R3`                                             | `R2`            | `"m"`          |
| `public static class Outer { public abstract class NestedBase : SharedObject { } }` | `[JS] public int M(Outer.NestedBase$N path) => 0;`                                  |                                                                 | `EXPOJSI023` | `M`        | `R3`                                             | `R2`            | `"m"`          |
|                                                                                     | `[JS] public int M(SharedObject$N value) => 0;`                                     |                                                                 | `EXPOJSI023` | `M`        | `which is the polymorphic SharedObject base`     | same            | `"m"`          |
|                                                                                     | `[JS] public int M(SharedRef<string>$N value) => 0;`                                |                                                                 | `EXPOJSI023` | `M`        | `which is the SharedRef<T> managed carrier base` | same            | `"m"`          |

Write `R1`, `R2`, `R3` as the constants from Step 1 (an `InlineData` argument can reference a `const`), "same" as the `reason` value again, and quoted fragments as `"\"m\""`. Rows 1-5 and 13-14 are inferred; the event rows are inferred; the rest mirror `GeneratorKeepsSharedObjectDiagnosticsForNullableBasesAndNestedShapes` (`:5382-5394`).

`GeneratorRejectsNullableSharedObjectBaseParameters` (the nullable form at a parameter; its non-null form is accepted, so it cannot use `$N`), theory `(string moduleMembers, string dirMembers, string memberName, string absentFragment)` over `SharedObjectBaseSource`. Per row: `AssertNoCompilerErrors`; IDs equal `[EXPOJSI023]`; the message contains `$"'{memberName}'"` and `R2`; the text contains neither `absentFragment`, `"SharedObjectBaseCodec"`, nor `"NullableSharedObjectCodec"`.

| moduleMembers                                                    | dirMembers                                       | memberName | absentFragment           |
| ---------------------------------------------------------------- | ------------------------------------------------ | ---------- | ------------------------ |
| `[JS] public int M(PathBase? path) => 0;`                        |                                                  | `M`        | `"m"`                    |
| `[JS] public Task<int> M(PathBase? path) => Task.FromResult(0);` |                                                  | `M`        | `"m"`                    |
|                                                                  | `[JS] public DirEntry(PathBase? parent) { }`     | `DirEntry` | `ConstructSharedObject_` |
|                                                                  | `[JS] public int Compare(PathBase? other) => 0;` | `Compare`  | `"compare"`              |

Concrete non-sealed classes (spec scenario "Concrete non-sealed classes keep their diagnostic") and Review Focus 4: extend `GeneratorKeepsTheInvalidConcreteClassReasonForNullableParameters` (`:4389-4444`), which already runs `""` and `"?"` and filters to the single `EXPOJSI023`, so the extra `EXPOJSI021` of an attributed invalid class does not break it. `OpenEntry` (`:4410`) already covers the attributed non-sealed class. Add to its source, after `OpenEntry`:

```csharp
public class OpenUnattributed : SharedObject
{
}

[ExpoSharedObject("AbstractEntry")]
public abstract partial class AbstractEntry : SharedObject
{
}
```

and two rows: `[InlineData("OpenUnattributed", "which is not marked [ExpoSharedObject]")]` and `[InlineData("AbstractEntry", "which must be a top-level, non-generic, sealed [ExpoSharedObject] class")]`. Add `Assert.DoesNotContain("SharedObjectBaseCodec", source)` after the existing `DoesNotContain("SharedObjectCodec<", source)` (`:4441`); the existing substring does not match inside `SharedObjectBaseCodec<`.

- [ ] **Step 4: Run the new tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~SharedObjectBase|FullyQualifiedName~AbstractBaseOfSharedRef|FullyQualifiedName~BasesOutsideParameters|FullyQualifiedName~InvalidConcreteClassReason"`
Expected: every acceptance row and the `HandleBase` fact fail with one `EXPOJSI023` whose message contains `which is not marked [ExpoSharedObject]`. In the rejection theory, every row that expects `R1`, `R2`, or `R3` fails with that same old reason, and every other row passes. All `GeneratorRejectsNullableSharedObjectBaseParameters` rows fail the same way. Both new invalid-concrete rows pass. Put the observed failures in the task report. If a row that should pass fails, stop and report.

- [ ] **Step 5: Edit the validator and the five call sites** as the Interfaces block states. No new descriptor field, no change to emission or `Codecs.cs`.

- [ ] **Step 6: Run the Step 4 command.** Expected: PASS.

- [ ] **Step 7: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped, full suite 1252. Every `GeneratorPreserves*` and `GeneratorPins*` contract test passes unchanged. If any contract test fails, stop: unrelated text changed.

- [ ] **Step 8: Run the 029 typed-policy scan and the spec's three scans** from Global Constraints. Expected: all print nothing.

- [ ] **Step 9: Commit**

```bash
git commit -m "feat(generator): accept abstract shared-object base parameters" \
  -m "An unattributed, top-level, non-generic abstract class that derives from SharedObject is now accepted as a parameter of module methods and of shared-object constructors and methods. It selects the decode-only SharedObjectBaseCodec<TBase>; emission is unchanged. TryAnalyzeSharedObjectBoundaryType takes an explicit isParameter flag, set by the two parameter call sites." \
  -m "The same base as a return, async result, or property, with a nullable annotation, nested or generic, or inside a composed codec or event payload reports EXPOJSI023 (or the event code) with a reason that names the rule. SharedObject, SharedRef<T>, and concrete non-sealed classes keep their diagnostics. No existing test is removed and no output-contract digest moves." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove base-class parameters on Hermes

Model: **standard**.

This task adds fixture members and tests only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (the Task 1 fixtures)
- Test: `Expo.ModulesCore.Tests/SharedObjects/SharedObjectTests.cs` (class `GeneratedSharedObjectTests`; insert after `NonNullSharedObjectParameterStaysStrict`, which ends at line 1532, so `JsErrHelper` (`:1143-1145`) and `WaitForConditionAsync` (`:1569`) are reused)

**Interfaces:**

- Consumes: Task 1's fixtures and codec, Task 2's generator support.
- Produces (fixture changes; JavaScript names follow lower-camel defaults, `m.describe`, `m.describeLater`, `m.describeFile`, `d.compare`, and the constructible classes `m.FileEntry` and `m.DirEntry`):

```csharp
public sealed partial class BaseThingsModule
{
  // The last PathBase that authored code received through any member, so tests can check
  // identity from C# and prove that a failed call never reached authored code.
  public static PathBase? LastPath;

  [JS] public string Describe(PathBase path)                    // LastPath = path; return path.Describe();
  [JS] public async Task<string> DescribeLater(PathBase path)   // await Task.Yield(); LastPath = path; return path.Describe();
  [JS] public string DescribeFile(FileEntry file)               // LastPath = file; return file.Describe(); (the exact twin, the oracle for failures)
}

public sealed partial class DirEntry : PathBase
{
  [JS] public DirEntry(string name, PathBase parent) : base(name)   // add [JS]; Parent = parent; BaseThingsModule.LastPath = parent;
  [JS] public string Compare(PathBase other)                      // BaseThingsModule.LastPath = other; return ReferenceEquals(other, Parent) ? "parent" : other.Describe();
}
```

Common setup, a private helper `CreateBaseThingsContext(fixture, runtime)` that copies `CreateNullableThingsContext` (`:1147-1162`): reset `BaseThingsModule.LastPath = null`, register the provider, and evaluate `globalThis.m = globalThis._expoDotnet.modules.BaseThings; 'ready'`. Async tests keep `context` outside `Execute` and dispose it in the last `Execute`, as `NullableSharedObjectNonNullKeepsIdentity` (`:1246-1320`) does. The unrelated class comes from the `SharedThings` module, registered by the same provider: `globalThis._expoDotnet.modules.SharedThings.makeSnapshot()`.

- [ ] **Step 1: Add the fixture members** above.

- [ ] **Step 2: Write the tests** in `GeneratedSharedObjectTests`:
  - `SharedObjectBaseParameterReceivesEverySubclass` (spec scenario "Base parameter receives any generated subclass"; Review Focus 1). Setup evaluates `globalThis.f = new m.FileEntry('f'); globalThis.SubFile = class extends m.FileEntry {}; globalThis.s = new SubFile('s'); globalThis.d = new m.DirEntry('d', f); 'ready'`. Then, from C#, one call per evaluation:
    - after setup, `var file = Assert.IsType<FileEntry>(BaseThingsModule.LastPath)` with `Name == "f"` (the constructor decoded `f`);
    - `m.describe(d)`: `var dir = Assert.IsType<DirEntry>(LastPath)`, `Assert.Same(file, dir.Parent)`;
    - `m.describe(s)`: `var sub = Assert.IsType<FileEntry>(LastPath)`, `Name == "s"`;
    - `new m.DirEntry('d2', s); 'made'`: `Assert.Same(sub, LastPath)` (a JavaScript subclass instance as a constructor argument);
    - `m.describe(f)` twice: `Assert.Same(file, LastPath)` after each (repeated calls receive the same instance).

    Then set the hook, read `var before = context.SharedObjects.Count`, and evaluate
    `[m.describe(f), m.describe(d), m.describe(s), d.compare(f), d.compare(d), d.compare(s), Object.getPrototypeOf(s) === SubFile.prototype, s instanceof m.FileEntry].join('|')`.
    It equals `"FileEntry:f|DirEntry:d|FileEntry:s|parent|DirEntry:d|FileEntry:s|true|true"`; the last two values are inferred (Review Focus 1). `Assert.Same(sub, LastPath)` (the last call was `d.compare(s)`). `Assert.Empty(steps)` and `context.SharedObjects.Count == before`. Control: `new m.FileEntry('x'); 'made'` records exactly one `InstanceObjectCreated`.

  - `SharedObjectBaseParameterKeepsIdentityAcrossAwait` (async; spec "async method" member and Review Focus 2). Setup: `globalThis.f = new m.FileEntry('f'); globalThis.d = new m.DirEntry('d', f); 'ready'`, then `m.describe(d)` and keep `dir = (DirEntry)LastPath`; `file = dir.Parent`. Reset `LastPath = null`, evaluate `m.describeLater(f).then(v => { globalThis.__f = v; }, error => { globalThis.__f = 'error:' + error.message; }); 'pending'`, wait for `typeof globalThis.__f !== 'undefined'`, then `__f` is `"FileEntry:f"` and `Assert.Same(file, LastPath)`. Repeat with `d` into `__d`: `"DirEntry:d"`, `Assert.Same(dir, LastPath)`. Rejection part: `m.describe(f)` sets the sentinel `file`; evaluate `m.describeLater(globalThis._expoDotnet.modules.SharedThings.makeSnapshot()).then(v => { globalThis.__bad = 'resolved:' + v; }, error => { globalThis.__bad = error.message; }); 'pending'`, wait, then `__bad` contains `The shared object receiver is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.` (inferred: `GeneratedFunction.cs:91-103` rejects with `exception.Message`), and `Assert.Same(file, LastPath)`, because `DescribeLater` sets `LastPath` after its `await` and never ran.
  - `SharedObjectBaseParameterFailuresMatchTheExactParameter` (spec scenario "Existing failures stay"; Review Focus 2, 3), theory `(string input, string expected, string exactExpected)`:

| input                 | expected                                                       | exactExpected                                                   |
| --------------------- | -------------------------------------------------------------- | --------------------------------------------------------------- |
| `({})`                | `NativeState entry for SharedObjectNativeState is missing.`    | same                                                            |
| `null`                | `Value is not an object.`                                      | same                                                            |
| `undefined`           | `Value is not an object.`                                      | same                                                            |
| `0`                   | `Value is not an object.`                                      | same                                                            |
| `'x'`                 | `Value is not an object.`                                      | same                                                            |
| `globalThis.released` | `not an active shared object`                                  | same                                                            |
| `globalThis.foreign`  | `not an active shared object`                                  | same                                                            |
| `globalThis.snapshot` | `is not a 'Expo.ModulesCore.Tests.Modules.PathBase' instance.` | `is not a 'Expo.ModulesCore.Tests.Modules.FileEntry' instance.` |

    Setup: `globalThis.kept = new m.FileEntry('kept'); globalThis.d = new m.DirEntry('d', kept); globalThis.released = new m.FileEntry('released'); released.release(); globalThis.snapshot = globalThis._expoDotnet.modules.SharedThings.makeSnapshot(); 'ready'`; keep `var sentinel = BaseThingsModule.LastPath` (the managed `kept`). The foreign instance copies the 033 setup (`:1343-1351`) with `FileEntry`: `contextB`, `GeneratedSharedObjectClass.Install(contextB, holder, typeof(FileEntry), "FileEntry", 0, null, null)`, `SharedObjectCodec<FileEntry>.Encode(new FileEntry("foreign"), runtime, contextB)`, set as global `foreign`, `contextB` disposed in a `finally`. Read `var before = context.SharedObjects.Count`, then evaluate `JsErrHelper + "const v = () => (<input>); [err(() => m.describe(v())), err(() => m.describeFile(v())), err(() => d.compare(v())), err(() => new m.DirEntry('x', v()))].join('|')"`. Split on `|`: 4 parts; parts 0, 2, and 3 contain `expected`; part 1 contains `exactExpected`; when the two columns are equal, the first line of part 0 equals the first line of part 1. `Assert.Same(sentinel, BaseThingsModule.LastPath)` (no authored code ran). `Assert.Equal(before, context.SharedObjects.Count)` (the failed constructor left no `DirEntry`; inferred). The constructor and `null`/`undefined` rows are inferred; the others follow 033's `NullableSharedObjectFailuresMatchTheExactParameter`.

- `SharedObjectBaseParameterStillRequiresItsArgument` (spec semantics "Omitting a required base argument"). Setup `globalThis.f = new m.FileEntry('f'); globalThis.d = new m.DirEntry('d', f); 'ready'` and keep the sentinel. `JsErrHelper + "[err(() => m.describe()), err(() => d.compare()), err(() => new m.DirEntry('x'))].join('|')"`: part 0 and part 1 contain `got 0` (`GeneratedFunction.cs:127`; inferred for the shared-object method); part 2 contains `new DirEntry expects 2 arguments, got 1.` (`GeneratedSharedObjectClass.cs:226`; inferred). `Assert.Same(sentinel, BaseThingsModule.LastPath)`.

- [ ] **Step 3: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~GeneratedSharedObjectTests.SharedObjectBase"`
Expected: PASS, 11 cases. A failing string, count, or step list is a finding to report with the observed value.

- [ ] **Step 4: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped, 1263 passed. Record the count for Task 4.

- [ ] **Step 5: Commit**

```bash
git commit -m "test(modules-core): prove base-class shared-object parameters on Hermes" \
  -m "A fixture module takes an abstract PathBase in a sync method, an async method, a shared-object method, and a shared-object constructor. Tests prove that FileEntry, DirEntry, and a JavaScript subclass instance reach authored code as the original managed instance with no pairing step, that identity holds across await, that plain, primitive, null, undefined, released, foreign-context, and unrelated-class inputs fail before authored code runs, and that an omitted base argument fails the argument-count check." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/034-base-class-shared-object-arguments.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-06-base-class-shared-object-arguments/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the delta into `docs/specs/modules-core-boundary.md`**, as the spec's "Delta against `docs/specs/modules-core-boundary.md`" section states.
  - `### Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity` (2597-2691): lines 2603-2604 become "`SharedObject`, `SharedRef<T>`, and other polymorphic base types SHALL NOT be generated-boundary codec types. An authored abstract base SHALL NOT be an encode-side codec type." Line 2609 becomes "Assignable base/derived compatibility SHALL NOT select an exact codec or a prototype. An authored abstract base is decoded only through `SharedObjectBaseCodec<TBase>` (see \"Authored Abstract Bases Decode As Parameters\")." Keep every other sentence.
  - Insert `### Requirement: Authored Abstract Bases Decode As Parameters` with its four scenarios, verbatim from the spec, right before `### Requirement: Public SharedObject Release Is Exactly Once` (2692).
  - Descriptor table (1525-1533): after the row ``Direct `[ExpoSharedObject]` class `T` and `T?` ``, add ``| Authored abstract base `TBase` (parameter only; decode-only `SharedObjectBaseCodec<TBase>`) | `StaticHelper` | `Borrowed` |``.
  - Position table, row `Top-level boundary (...)` (1509): append "An authored abstract shared-object base resolves to the decode-only `SharedObjectBaseCodec<TBase>` and is accepted only as a module method, shared-object method, or shared-object constructor parameter." The table has no per-type rows, so this sentence is the spec's position-table row; say so in the task report.

- [ ] **Step 2: Update `docs/module-authoring-guide.md`**, section 8, `### Module ownership`, the boundary paragraph (517-522, starting "Shared-object parameters, returns, and properties"). Result: "Shared-object parameters, returns, and properties must use the exact sealed authored type directly at the boundary, optionally annotated as nullable (`ExampleCounter?`). JavaScript `null` and `undefined` arrive as C# `null`, and C# `null` returns JavaScript `null`. A parameter may instead use an authored abstract base of several shared classes: a top-level, non-generic `abstract class` that derives from `SharedObject` and has no `[ExpoSharedObject]` attribute. Authored code receives the concrete instance JavaScript passed. A base parameter cannot be nullable, and returns and properties still use the exact sealed class. The `SharedObject`/`SharedRef<T>` bases, with or without `?`, and nested composition (records, lists, dictionaries, callbacks) are rejected."

- [ ] **Step 3: Update `docs/roadmap.md`.** Row 034 (line 59): `DONE`, link `[034](archive/plans/034-base-class-shared-object-arguments.md)`; keep the column widths aligned. Replace the `**034**` note (108-110) with: "- **034**: done ([change](archive/changes/2026-11-06-base-class-shared-object-arguments/spec.md)): it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). An authored abstract shared-object base such as `FileSystemPath` now works as a parameter of module methods and shared-object constructors and methods, through the decode-only `SharedObjectBaseCodec<TBase>`. Returns, properties, `Base?`, and nested shapes stay rejected, so 038 can port `copy` and `move` but not a base-typed return." Take `<N>` from Step 5 and `<date>` from the commit date.

- [ ] **Step 4: Move the brief and the change folder** with `git mv`. Fix every Markdown link to the old paths: `rg -n '034-base-class-shared-object-arguments|2026-11-06-base-class-shared-object-arguments' docs .agents AGENTS.md`. Every link left points into `archive/`. Plain-text path mentions inside the archived brief, spec, and plan are history and stay.

- [ ] **Step 5: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'GeneratedConversionScope.*SharedObject|\bOwn[^\n]*Shared(Object|Ref)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
rg -n 'SharedObjectBaseCodec<(global::Expo\.ModulesCore\.)?(SharedObject|SharedRef)' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
rg -n 'IsAssignableFrom|IsAssignableTo|\.IsSubclassOf' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs
git diff --unified=0 482e395..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped and 1263 passed (1213 + 10 + 29 + 11; put the arithmetic in the report). Format is OK and `git diff --check` is clean. All four scans print nothing.

- [ ] **Step 6: Commit**

```bash
git commit -m "docs(modules-core): close base-class shared-object arguments" \
  -m "Merge the accepted delta into the living spec and the authoring guide, archive the brief and the change folder, and mark roadmap item 034 DONE." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
