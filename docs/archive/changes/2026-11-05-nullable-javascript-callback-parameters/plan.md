# Nullable JavaScript Callback Parameters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `JavaScriptCallback<TResult>?` and `JavaScriptCallback<TArgs, TResult>?` work as parameters of generated module methods and of shared-object constructors and methods, through a new `NullableJavaScriptCallbackCodec`, while every other position keeps the non-null rejection (roadmap item 032).

**Architecture:** One new runtime file with two static classes tests `IsNullish` and otherwise calls the existing callback codec. The generator resolves an annotated callback through the existing callback resolver with a different class name, so the descriptor stays `StaticHelper`, `DecodeOnly`, `DotnetRuntimeContext`, `ContextRetained`, and the existing position rules keep every non-parameter position rejected. One internal count on `DotnetRuntimeContext` lets tests prove that the null branch registers nothing. The docs close the change last.

**Tech Stack:** C# / .NET 10, Roslyn incremental source generator, xUnit v3, Hermes-backed managed tests (`scripts/test-managed.sh`).

**Spec:** `docs/changes/2026-11-05-nullable-javascript-callback-parameters/spec.md` (binding authority; reconciled at `833a374`). `git diff --stat 833a374..HEAD -- packages` prints nothing at plan time. The brief `docs/plans/032-nullable-javascript-callback-parameters.md` is background only; the spec wins.

**Execution:** subagent-driven (superpowers:subagent-driven-development). Model hints: **cheap** (mechanical), **standard** (normal implementation).

Paths in **Files** blocks are relative to `packages/expo-modules-dotnet/managed/packages/` unless they start with `docs/` or `scripts/`.

## Global Constraints

- **Design criteria (review criteria for every task, from the spec):**
  1. Clean code: one null test in one place. Same call shape as the non-null codec, so emission does not change.
  2. Performance: null allocates nothing and registers nothing.
  3. Correctness: one owner for callbacks, `DotnetRuntimeContext`. Null never reaches `AsFunction` or the context.
- Exactly one new production file, `Expo.ModulesCore/Codecs/NullableJavaScriptCallbackCodec.cs`, with two public static classes, and one new `internal` property, `DotnetRuntimeContext.RetainedCallbackCount`. Nothing else new in production code. `JavaScriptCallback.cs`, `JavaScriptCallbackCodec.cs`, and the emitters do not change.
- Callbacks never enter `GeneratedConversionScope`. Generated glue never disposes a callback. The spec's scope scan prints nothing:
  `rg -n 'GeneratedConversionScope.*Callback|\.Register\([^\n]*JavaScriptCallback|Own[^\n]*JavaScriptCallback' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
  It prints nothing at plan time. Do not write a comment that puts `Own...` before `JavaScriptCallback` on one line.
- No new diagnostic IDs and no new or changed diagnostic message text.
- Only `GeneratorPreservesCodecPolicyDiagnosticContract` may move (Task 2). `GeneratorPreservesModuleOutputContract`, `GeneratorPreservesSharedObjectOutputContract`, `GeneratorPreservesDiagnosticOutputContract`, `GeneratorPreservesNullableCodecOutputContract`, both `GeneratorPreservesLeafFree*` tests, and both `GeneratorPinsLeaf*` tests stay byte-identical. The leaf-free fixtures hold `JavaScriptCallback<int>` and must keep emitting `JavaScriptCallbackCodec<...>`.
- 029's typed-policy scan stays empty:
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- Count rule: `RetainedCallbackCount` only goes up while the context lives. Each non-null decode adds one entry (`DotnetRuntimeContext.cs:241-260`). Explicit `JavaScriptCallback.Dispose()` releases the function but does not remove the entry (`JavaScriptCallback.cs:135-144`), so the count does not drop. Context `Dispose` clears the list (`DotnetRuntimeContext.cs:290-291`), so the count is 0 after teardown. Prove "release once" by exception type, as `RuntimeContextDisposeReleasesRetainedCallback` (`CallbackModuleTests.cs:277-304`) does: `ObjectDisposedException` after explicit `Dispose`, `InvalidOperationException` after context teardown.
- A count, digest, ID list, or generated fragment that differs from this plan is a finding to report, never a value to adjust until green. Values marked "inferred" come from reading code, not from an existing test.
- No runtime hot-path reflection in generated code. Generated code stays NativeAOT-compatible.
- Style: 2-space indent, file-scoped namespaces, existing naming. Use `bunx`, never `npx`.
- Privacy: no local absolute paths, usernames, machine names, or hostnames in any committed file or commit message. Never write a literal username into a scan command; write `/[U]sers/` instead. Privacy scan (from the spec):
  `git diff --unified=0 833a374..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'`
- Commits: conventional subjects (`feat(...)`, `test(...)`, `docs(...)`). Every commit message ends with the line `Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv`. No push, no PR, no GitHub posts. Codex is disabled; do not run it.
- Before each commit: `scripts/format.sh --check --all` passes (if it fails, run `scripts/format.sh`, then check again) and `git diff --check` is clean.
- Finish: `scripts/test-managed.sh` passes with 0 failed and 0 skipped.

## Commands

| Purpose            | Command                                                                                                                                                    |
| ------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Generator tests    | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj` |
| ModulesCore tests  | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`                     |
| One test           | append `--filter "FullyQualifiedName~<Name>"` to either command                                                                                            |
| Full managed suite | `scripts/test-managed.sh` (1120 passed, 0 skipped at 031 close `833a374` per `docs/roadmap.md`)                                                            |

## Review Focus

1. **The non-null form stays strict.** `m.callNoArgs(null)` and `m.callNoArgs(undefined)` on a `JavaScriptCallback<string>` parameter still throw a catchable `Error`, and nothing registers. Test: Task 3 `NonNullCallbackParameterStaysStrict`.
2. **JavaScript passes a non-function, non-nullish value** (`5`, `'x'`, `{}`). Decode fails before registration with a catchable `Error`. Tests: Task 1 `DecodeRejectsNonFunctionValuesWithoutRegistering`, Task 3 `NullableCallbackParameterRejectsNonFunctionValues`.
3. **JavaScript omits a required nullable callback.** The argument-count check rejects the call; it does not become `null`. Test: Task 3 `RequiredNullableCallbackStillRequiresItsArgument`.
4. **Authored code disposes a callback, then the context tears down.** A later call fails with `ObjectDisposedException`, and teardown does not release the function again. Test: Task 3 `ExplicitDisposeReleasesNullableCallbackOnce`.
5. **The same JavaScript function fills two calls.** Each decode registers its own callback (count +2). Test: Task 3 `NonNullNullableCallbackStaysRetainedAfterTheMethodReturns`.

---

### Task 1: Nullable callback codec and retained-callback count

Model: **cheap**.

This task must land before Task 2. The generator test host compiles generated code against the real `Expo.ModulesCore` assembly (`GeneratorTestHost.cs:109`), so Task 2's acceptance rows need `NullableJavaScriptCallbackCodec` to exist.

**Files:**

- Create: `Expo.ModulesCore/Codecs/NullableJavaScriptCallbackCodec.cs`
- Modify: `Expo.ModulesCore/DotnetRuntimeContext.cs` (insert after `RegisterRetainedCallback`, 241-260)
- Test (create): `Expo.ModulesCore.Tests/Codecs/NullableJavaScriptCallbackCodecTests.cs`

**Interfaces:**

- Produces (exact shape from the spec; it mirrors `JavaScriptCallbackCodec.cs:5-40`, same constraints, same namespace `Expo.ModulesCore.Codecs`):

```csharp
public static class NullableJavaScriptCallbackCodec<TResult, TResultCodec>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TResult>? Decode(
      JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptCallbackCodec<TResult, TResultCodec>.Decode(value, runtime, context);
}

public static class NullableJavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
    where TArgs : struct
    where TArgsCodec : IJavaScriptArgsCodec<TArgs>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TArgs, TResult>? Decode(
      JavaScriptValueRef value, JavaScriptRuntime runtime, DotnetRuntimeContext context) =>
      value.IsNullish ? null : JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>.Decode(value, runtime, context);
}
```

No `Encode`. The repo formatter decides the line breaks.

- Produces: `internal int RetainedCallbackCount { get; }` on `DotnetRuntimeContext`. It reads `retainedCallbacks.Count` (field at line 40) inside `lock (gate)` (line 38). It does not throw after `Dispose`. `InternalsVisibleTo("Expo.ModulesCore.Tests")` already exists (`Expo.ModulesCore.csproj:13`). Doc comment: the number of callbacks registered with this context, for tests; explicit callback disposal does not lower it, and teardown clears it.

- [ ] **Step 1: Write the failing tests** in `NullableJavaScriptCallbackCodecTests.cs` (namespace `Expo.ModulesCore.Tests.Codecs`, pattern of `NullableReferenceCodecTests.cs:12-44`). Each test runs inside `fixture.Runtime.Execute` with `new DotnetRuntimeContext(runtime)`. Below, `C1` is `NullableJavaScriptCallbackCodec<int, NumberCodec<int>>` and `C2` is `NullableJavaScriptCallbackCodec<ValueTuple<string>, ValueTupleCodec<string, StringCodec>, string, StringCodec>`.
  - `DecodeReturnsNullForNullishInputWithoutRegistering`, theory `[InlineData("null")] [InlineData("undefined")]`: `Assert.Null(C1.Decode(value.Ref, runtime, context))`, `Assert.Null(C2.Decode(value.Ref, runtime, context))`, `context.RetainedCallbackCount == 0`.
  - `DecodeRetainsNonNullFunctionThroughTheCallbackCodec`: `() => 5` through `C1` gives a callback whose `Invoke()` is `5`; count 1. `(name) => 'Hi ' + name` through `C2` gives a callback whose `Invoke(ValueTuple.Create("JS"))` is `"Hi JS"`; count 2.
  - `DecodeRejectsNonFunctionValuesWithoutRegistering` (Review Focus 2), theory `[InlineData("5")] [InlineData("'x'")] [InlineData("({})")]`: for `C1` and for `C2`, `Assert.Throws<InvalidOperationException>` with a message that contains `Value is not a function.` (native text at `native/packages/jsi/src/ExpoJsiBridge.cpp:757-763`, wrapped by `JsiContext.ThrowNativeError`); count 0.
  - `RetainedCallbackCountClearsAtContextTeardown`: decode `() => 5` through `C1`; count 1. `callback.Dispose()`; count still 1 (the count rule in Global Constraints). `context.Dispose()`; count 0, and reading it does not throw.

- [ ] **Step 2: Run the tests to verify they fail**

Run: ModulesCore tests with `--filter "FullyQualifiedName~NullableJavaScriptCallbackCodecTests"`
Expected: build fails with CS0246 (`NullableJavaScriptCallbackCodec` not found) and CS1061 (`RetainedCallbackCount`). Put the observed errors in the task report.

- [ ] **Step 3: Create the codec file and add the property** as the Interfaces block states.

- [ ] **Step 4: Run the Step 2 command.** Expected: PASS.

- [ ] **Step 5: Run both suites.** Run: Generator tests, then ModulesCore tests. Expected: all pass; no generated text changes.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat(modules-core): add nullable JavaScript callback codec" \
  -m "NullableJavaScriptCallbackCodec decodes null and undefined to C# null without AsFunction or context registration, and sends every other value to the existing callback codec. DotnetRuntimeContext gets an internal retained-callback count for tests." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 2: Generator resolves nullable callback parameters

Model: **standard**.

**Files:**

- Modify: `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs:177-188` (`IsExcludedFromNullableReference`), `:219-223` (`TryGetNullableReferenceCodec`), `:512` (the `GetCodec` call), `:698-734` (`TryGetJavaScriptCallbackCodec`)
- Test: `Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`, `Expo.ModulesCore.Generator.Tests/GeneratorOutputContractTests.cs:423-475` (`GeneratorPreservesCodecPolicyDiagnosticContract` only)

**Interfaces:**

- Consumes: Task 1's `NullableJavaScriptCallbackCodec` (both arities).
- Produces: an annotated callback parameter resolves to `CallbackCodec(...)` (`Codecs.cs:736-743`: `StaticHelper`, `DecodeOnly`, `Decode`, `DotnetRuntimeContext`, `ContextRetained`) whose expression starts with `NullableJavaScriptCallbackCodec<` and has the same type arguments as the non-null codec. `GetDecodeExpression` (`Codecs.cs:447-469`) then emits `<expression>.Decode(arguments.GetValue(i), <runtime>, GeneratedFunction.CurrentRuntimeContext)`, where `<runtime>` is `runtime` for sync and `jsRuntime` for async (`Emission.cs:807`). Below, `NC1` stands for `NullableJavaScriptCallbackCodec<int, NumberCodec<int>>` and `NC2` for `NullableJavaScriptCallbackCodec<global::System.ValueTuple<string>, ValueTupleCodec<string, StringCodec>, string, StringCodec>`. Write them out in full in the tests. Both are inferred from `GetDecodeExpression` (`Codecs.cs:447-469`), the callback expressions at `Codecs.cs:717-733`, and `CodecTypeArgumentFormat` (`Codecs.cs:40-50`, fully qualified, so a one-element tuple prints as `global::System.ValueTuple<string>`). The existing assertion `JavaScriptCallbackCodec<global::System.ReadOnlyMemory<byte>, ReadOnlyMemoryByteCodec>` (`ExpoModulesGeneratorTests.cs:475`) confirms the type-argument style.

How the other positions stay rejected (verified mechanisms, used by Step 3):

- Return and async result: the `DecodeOnly` checks at `ModuleAnalysis.cs:510-513` and `:566-569` → `EXPOJSI002`.
- `[JS]` property: `ContainsJavaScriptCallback(member.Type)` at `ModuleAnalysis.cs:776` → `EXPOJSI015`. `IsJavaScriptCallbackType` (`Codecs.cs:633-643`) reads `OriginalDefinition`, so it ignores the annotation.
- Record field: `TryGetRecordCodec` (`Codecs.cs:391-407`) requires `DecodeAndEncode` and no context requirement → `EXPOJSI007`, plus the member's own diagnostic. The spec names `IsJavaScriptCallbackType` for this path; the real mechanism is the descriptor check, with the same outcome.
- List element, dictionary value, callback type argument: `AsCompositeChildCodec` and `AsJavaScriptCodecArgument` (`Codecs.cs:80-87`) reject `StaticHelper` → the member's diagnostic.
- Typed event payload: `GetUnsupportedEventPayload` (`EventAnalysis.cs:382`) → `EXPOJSI019` or `EXPOJSI027`.
- Parameter with an invalid argument or result codec: the selector at `ModuleAnalysis.cs:658-660` uses `IsJavaScriptCallbackType(parameter.Type)` → `EXPOJSI008`.
- Shared-object methods and properties: `TranslateSharedObjectMemberDiagnostic` (`SharedObjectValidation.cs:79-96`) maps `EXPOJSI001`, `002`, `008`, `015` to `EXPOJSI023`. A shared-object constructor parameter with no codec reports `EXPOJSI023` directly (`SharedObjectModel.cs:141-148`), not through that map.

Existing tests that assert a nullable callback is rejected (the complete list):

- `GeneratorReportsCallbackDiagnosticForNullableCallbackParameter` (`ExpoModulesGeneratorTests.cs:4253-4277`): `CallIt(JavaScriptCallback<string>? callback)` → `EXPOJSI008`. It becomes valid. Delete it; the `JavaScriptCallback<decimal>$N` row of the Step 3 theory takes over the `EXPOJSI008` check.
- `GeneratorPreservesCodecPolicyDiagnosticContract` (`GeneratorOutputContractTests.cs:442`, `OptionalCallback(JavaScriptCallback<int>? callback)`). Re-pinned in Step 8.

No other test changes. `GeneratorReportsMethodDiagnosticsForExcludedNullableParametersAndReturns` and `GeneratorReportsEventDiagnosticsForExcludedNullablePayloads` hold only `JavaScriptValue` since 031. `GeneratorRejectsStaticHelperCodecsInGenericPositions` and `GeneratorRejectsStaticHelperCodecsOnSharedObjectMembers` have no annotated callback rows. The `JavaScriptCallback<string>? ... => null` members at `ExpoModulesGeneratorTests.cs:42` and `:1808` are not `[JS]` members or record positional fields, so they stay green.

- [ ] **Step 1: Delete `GeneratorReportsCallbackDiagnosticForNullableCallbackParameter`.**

- [ ] **Step 2: Write the failing acceptance tests** in `ExpoModulesGeneratorTests.cs`. Every row asserts:
  - `Assert.Empty(GeneratorDiagnosticIds(result))`;
  - `Assert.Contains(fragment, text, StringComparison.Ordinal)`;
  - `Assert.DoesNotContain("GeneratedConversionScope", text, StringComparison.Ordinal)` and `Assert.DoesNotContain("__expoArg0.Dispose", text, StringComparison.Ordinal)` (spec: no scope registration, no disposal at invocation end);
  - `Assert.Equal(CountOccurrences(text, "JavaScriptCallbackCodec<"), CountOccurrences(text, "NullableJavaScriptCallbackCodec<"))`. Every callback codec in the text is the nullable one. A bare `DoesNotContain("JavaScriptCallbackCodec")` would match inside the nullable name.
  - Rows built from `ModuleSource` also assert `AssertNoCompilerDiagnostics(result)`. Rows built from `SharedObjectMemberSource` assert `AssertNoCompilerErrors(result)` and `Assert.DoesNotContain(result.Diagnostics, d => d.Id is "CS8604" or "CS8625")`, as the 031 shared-object rows do.

`GeneratorAcceptsNullableCallbackParameters` (theory `(string member, string fragment)` over `ModuleSource(string.Empty, member)`):

- `[JS] public int M(JavaScriptCallback<int>? callback) => 0;` → `var __expoArg0 = NC1.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`
- `[JS] public string M(JavaScriptCallback<ValueTuple<string>, string>? callback) => "";` → `var __expoArg0 = NC2.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`
- `[JS] public Task<int> M(JavaScriptCallback<int>? callback) => Task.FromResult(0);` → `var __expoArg0 = NC1.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);`
- `[JS] public Task<string> M(JavaScriptCallback<ValueTuple<string>, string>? callback) => Task.FromResult("");` → `var __expoArg0 = NC2.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);`
- `[JS] public int M(JavaScriptCallback<int>? callback = null) => 0;` → `var __expoArg0 = arguments.Count <= 0 || arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : NC1.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` (`Codecs.cs:444`)
- `[JS] public string M(JavaScriptCallback<ValueTuple<string>, string>? callback = null) => "";` → the same text with `NC2`
- `[JS] public Task<int> M(JavaScriptCallback<int>? callback = null) => Task.FromResult(0);` → `? null : NC1.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext)`

`GeneratorAcceptsNullableCallbackParametersOnSharedObjects` (theory `(string member, string fragment)` over `SharedObjectMemberSource(member)`):

- `[JS] public Entry(JavaScriptCallback<int>? callback) { }` → `var __expoArg0 = NC1.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);` (constructor factory, `Emission.cs:300-310`). This row also asserts the text contains `ConstructSharedObject_`.
- `[JS] public Entry(JavaScriptCallback<ValueTuple<string>, string>? callback = null) { }` → `arguments.Length <= 0 || arguments.GetValue(0).Kind == global::Expo.JSI.JavaScriptValueKind.Undefined ? null : NC2.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext)` (`Emission.cs:307`: the factory reads `Length`, not `Count`)
- `[JS] public int Run(JavaScriptCallback<int>? callback) => 0;` → `var __expoArg0 = NC1.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext);`
- `[JS] public Task<string> RunAsync(JavaScriptCallback<ValueTuple<string>, string>? callback) => Task.FromResult("");` → `var __expoArg0 = NC2.Decode(arguments.GetValue(0), jsRuntime, GeneratedFunction.CurrentRuntimeContext);`

- [ ] **Step 3: Write the kept rejections** (spec scenario "Nullable callback outside a parameter keeps its diagnostic"). This test is a characterization test: it passes before and after the production edit.

`GeneratorKeepsCallbackDiagnosticsForNullableCallbacks`, theory `(string declarations, string member, bool onSharedObject, string expectedIds, string absentFragment)`. `declarations` and `member` hold the token `$N`. The test runs the generator twice: once with `$N` replaced by `""` and once by `"?"`. The source is `ModuleSource(declarations, member)` or `SharedObjectMemberSource(member, declarations)`. For each run, assert `AssertNoCompilerErrors(result)`, `Assert.Equal(expectedIds.Split(','), GeneratorDiagnosticIds(result))`, `Assert.DoesNotContain(absentFragment, text, StringComparison.Ordinal)`, and `Assert.DoesNotContain("NullableJavaScriptCallbackCodec", text, StringComparison.Ordinal)`. The non-null run is the oracle: if it disagrees with `expectedIds`, the row is wrong, so report it. The oracle column names the existing non-null test line that pins each expected ID; `inferred` rows follow from the mechanisms above.

| declarations                                                    | member                                                                                          | shared  | expectedIds             | absentFragment           | oracle (`ExpoModulesGeneratorTests.cs` line) |
| --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- | ------- | ----------------------- | ------------------------ | -------------------------------------------- |
| `""`                                                            | `[JS] public JavaScriptCallback<int>$N M() => throw new NotImplementedException();`             | `false` | `EXPOJSI002`            | `"m"`                    | 4750                                         |
| `""`                                                            | `[JS] public Task<JavaScriptCallback<int>$N> M() => throw new NotImplementedException();`       | `false` | `EXPOJSI002`            | `"m"`                    | 4751                                         |
| `""`                                                            | `[JS] public JavaScriptCallback<int>$N Callback { get; } = null!;`                              | `false` | `EXPOJSI015`            | `"callback"`             | 9                                            |
| `public sealed record Holder(JavaScriptCallback<int>$N Field);` | `[JS] public int M(Holder holder) => 0;`                                                        | `false` | `EXPOJSI007,EXPOJSI001` | `"m"`                    | 4812                                         |
| `""`                                                            | `[JS] public int M(IReadOnlyList<JavaScriptCallback<int>$N> value) => 0;`                       | `false` | `EXPOJSI001`            | `"m"`                    | 4762                                         |
| `""`                                                            | `[JS] public int M(Dictionary<string, JavaScriptCallback<int>$N> value) => 0;`                  | `false` | `EXPOJSI001`            | `"m"`                    | 4763                                         |
| `""`                                                            | `[JS] public int M(JavaScriptCallback<JavaScriptCallback<int>$N> value) => 0;`                  | `false` | `EXPOJSI008`            | `"m"`                    | 4765                                         |
| `""`                                                            | `[JS] public int M(JavaScriptCallback<ValueTuple<JavaScriptCallback<int>$N>, int> value) => 0;` | `false` | `EXPOJSI008`            | `"m"`                    | 4764                                         |
| `""`                                                            | `[JS] public int M(JavaScriptCallback<decimal>$N callback) => 0;`                               | `false` | `EXPOJSI008`            | `"m"`                    | 748                                          |
| `public sealed record Holder(JavaScriptValue Value);`           | `[JS] public int M(JavaScriptCallback<Holder>$N callback) => 0;`                                | `false` | `EXPOJSI008`            | `"m"`                    | 5191                                         |
| `""`                                                            | `[Event] public partial Func<JavaScriptCallback<int>$N, Task> OnCallback { get; }`              | `false` | `EXPOJSI019`            | `"onCallback"`           | 1739                                         |
| `""`                                                            | `[Event] public partial Func<JavaScriptCallback<int>$N, Task> OnCallback { get; }`              | `true`  | `EXPOJSI027`            | `"onCallback"`           | 3690                                         |
| `""`                                                            | `[JS] public JavaScriptCallback<int>$N Make() => throw new NotImplementedException();`          | `true`  | `EXPOJSI023`            | `"make"`                 | inferred                                     |
| `""`                                                            | `[JS] public JavaScriptCallback<int>$N Callback { get; } = null!;`                              | `true`  | `EXPOJSI023`            | `"callback"`             | inferred                                     |
| `""`                                                            | `[JS] public void Run(JavaScriptCallback<decimal>$N callback) { }`                              | `true`  | `EXPOJSI023`            | `"run"`                  | inferred                                     |
| `""`                                                            | `[JS] public Entry(JavaScriptCallback<decimal>$N callback) { }`                                 | `true`  | `EXPOJSI023`            | `ConstructSharedObject_` | 4779                                         |

In C# string literals the quoted fragments are written `"\"m\""` and so on. The `JavaScriptCallback<Holder>$N` row pins that an invalid annotated callback reports once: the resolver must not fall through and resolve `Holder` a second time.

- [ ] **Step 4: Run the new tests to verify state**

Run: Generator tests with `--filter "FullyQualifiedName~NullableCallback"`
Expected: every `GeneratorAcceptsNullableCallbackParameters` row fails with `EXPOJSI008`. Every `GeneratorAcceptsNullableCallbackParametersOnSharedObjects` row fails with `EXPOJSI023`. Every `GeneratorKeepsCallbackDiagnosticsForNullableCallbacks` row passes. Put the observed failures in the task report. If a characterization row fails, stop and report.

Before any production edit, dump the generated provider text of the `GeneratorPreservesCodecPolicyDiagnosticContract` fixture to a scratch location outside the repo (temporarily assert the text equals `""` so xUnit prints it, then revert that line). Step 8 diffs against this dump.

- [ ] **Step 5: Edit `IsExcludedFromNullableReference`** (`Codecs.cs:177-188`). It keeps `JavaScriptValue` only: `typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == JavaScriptValueMetadataName`. Rewrite the comment above it so it names `JavaScriptValue` only.

- [ ] **Step 6: Edit the callback resolver and its two callers.**
  - Signature: `private static ExpoCodecDescriptor? TryGetJavaScriptCallbackCodec(ITypeSymbol typeSymbol, List<ExpoDiagnosticModel> diagnostics, List<ExpoGeneratedRecordCodecModel> recordCodecs, string codecClassName)`. The parameter is required (no default). The two expressions at `Codecs.cs:717-719` and `:731-733` start with `{codecClassName}<` instead of the literal `JavaScriptCallbackCodec<`. Argument and result resolution do not change.
  - `GetCodec` (`Codecs.cs:512`) passes `"JavaScriptCallbackCodec"`.
  - `TryGetNullableReferenceCodec`: right after the exclusion test (`Codecs.cs:220-223`) and before the `INamedTypeSymbol` branch, add:

```csharp
if (IsJavaScriptCallbackType(valueType))
{
  codec = TryGetJavaScriptCallbackCodec(valueType, diagnostics, recordCodecs, "NullableJavaScriptCallbackCodec");
  return true;
}
```

It returns `true` even when `codec` is `null`, so an annotated callback never reaches the fall-through at `Codecs.cs:280-287`. A short comment says why: the callback descriptor keeps every non-parameter position rejected, and a handled type never falls through. Update the `<returns>` text of `TryGetNullableReferenceCodec` only if it names the callback types.

- [ ] **Step 7: Run the Step 4 command.** Expected: PASS.

- [ ] **Step 8: Re-pin the one moved contract.**

Run: Generator tests. Expected: only `GeneratorPreservesCodecPolicyDiagnosticContract` fails. If any other digest test fails, stop: unrelated text changed.
Dump the fixture's generated text again, diff it against the Step 4 dump, and confirm the diff only adds the `OptionalCallback` binding (an `optionalCallback` host function with `NC1.Decode(arguments.GetValue(0), runtime, GeneratedFunction.CurrentRuntimeContext)` and its registration). Put the diff summary in the task report. Then copy the new output digest from the failure message into line 457, and delete exactly this line (466) from the expected diagnostic list:
`"EXPOJSI008|Error|Callback parameter 'callback' on 'OptionalCallback' uses unsupported callback type 'global::Expo.ModulesCore.JavaScriptCallback<int>'. Callback argument and result type codecs are required, and callback arguments must use ValueTuple shapes.|14|60|14|68",`
Every other diagnostic line, including its line and column numbers, stays the same: the fixture source does not change.

- [ ] **Step 9: Run everything.** Run: Generator tests, ModulesCore tests, then `scripts/test-managed.sh`. Expected: all pass, 0 skipped.

- [ ] **Step 10: Run the 029 typed-policy scan and the spec's scope scan** from Global Constraints. Expected: no output from either.

- [ ] **Step 11: Commit**

```bash
git commit -m "feat(generator): support nullable callback parameters" \
  -m "An annotated JavaScriptCallback resolves through the callback resolver with NullableJavaScriptCallbackCodec. The descriptor stays StaticHelper, DecodeOnly, context-retained, so returns, properties, record fields, collections, and typed events keep the non-null diagnostics." \
  -m "Re-pin GeneratorPreservesCodecPolicyDiagnosticContract: its OptionalCallback(JavaScriptCallback<int>?) member now compiles, so its EXPOJSI008 line goes and the provider digest gains that binding. No other digest moves." \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 3: Prove nullable callback lifetimes on Hermes

Model: **standard**.

This task adds tests and fixtures only. Production code is complete after Task 2, so these tests are expected to pass on the first run. A failure is a finding: stop and report it with the observed values. Do not change production code or adjust an expectation.

**Files:**

- Modify: `Expo.ModulesCore.Tests/Modules/ModuleFixtures.cs` (insert after `GeneratedCallbacksModule`, 249-282)
- Test: `Expo.ModulesCore.Tests/Modules/CallbackModuleTests.cs`

**Interfaces:**

- Consumes: Task 1's `RetainedCallbackCount`; Task 2's generator support.
- Produces: a separate fixture module, so the existing `store`/`callStored` tests stay untouched. A new module does not disturb other tests: the `Object.keys(globalThis._expoDotnet.modules)` assertions in `ModuleRegistryTests.cs` use their own lazy modules, not the generated provider.

```csharp
[ExpoModule("GeneratedNullableCallbacks", Classes = new[] { typeof(NullableCallbackEntry) })]
public sealed partial class GeneratedNullableCallbacksModule
{
  // The last callback CallOptional received, so tests can reach it after the call returns.
  public static JavaScriptCallback<int>? LastOptional { get; private set; }

  [JS("callOptional")] public int CallOptional(JavaScriptCallback<int>? callback)      // LastOptional = callback; return callback?.Invoke() ?? -1;
  [JS("callStoredOptional")] public int CallStoredOptional()                            // LastOptional?.Invoke() ?? -1
  [JS("disposeStoredOptional")] public void DisposeStoredOptional()                     // LastOptional?.Dispose()
  [JS("callOptionalAfterAwait")] public async Task<int> CallOptionalAfterAwaitAsync(JavaScriptCallback<int>? callback)
                                                                                        // await Task.Yield(); null → -1, else await callback.InvokeAsync()
  [JS("callDefaulted")] public string CallDefaulted(JavaScriptCallback<ValueTuple<string>, string>? callback = null)
                                                                                        // callback?.Invoke(ValueTuple.Create("JS")) ?? "null"
}

[ExpoSharedObject]
public sealed partial class NullableCallbackEntry : SharedObject
{
  [JS] public NullableCallbackEntry(JavaScriptCallback<int>? seed)                       // Seed = seed?.Invoke() ?? -1
  [JS] public int Seed { get; }
  [JS] public string Call(JavaScriptCallback<ValueTuple<string>, string>? callback = null) // callback?.Invoke(ValueTuple.Create("entry")) ?? "null"
}
```

The static `LastOptional` follows `SharedThingsModule.LastSeenCounter` (`ModuleFixtures.cs:512`). Tests in one class run one at a time, and only `GeneratedCallbackModuleTests` uses it.

- [ ] **Step 1: Add the fixtures** above to `ModuleFixtures.cs`.

- [ ] **Step 2: Write the module tests** in `CallbackModuleTests.cs` (class `GeneratedCallbackModuleTests`). Register the provider as `GeneratedModuleInvokesCallbackParameter` does (12-30), with `m = globalThis._expoDotnet.modules.GeneratedNullableCallbacks`. Read `context.RetainedCallbackCount` from C# after each JS evaluation; it is lock-protected, so any thread can read it. Wrap a single numeric result in `String(...)` so `AsString()` can read it. A JS failure check is `(() => { try { <call>; return 'no error'; } catch (e) { return e instanceof Error ? 'Error' : typeof e; } })()`, which must return `"Error"`. All counts are inferred from the count rule in Global Constraints.
  - `NullishCallbackReachesAuthoredCodeAsNull` (spec scenario "Nullish callback reaches authored code as null"): `[m.callOptional(null), m.callOptional(undefined), m.callDefaulted(), m.callDefaulted(undefined), m.callDefaulted(null)].join(':')` is `"-1:-1:null:null:null"`. Count 0 and `GeneratedNullableCallbacksModule.LastOptional` is `null`.
  - `NullableCallbackParameterRejectsNonFunctionValues` (scenario "Non-function non-null value is rejected"; Review Focus 2), theory `[InlineData("5")] [InlineData("'x'")] [InlineData("({})")]`: `m.callOptional(<value>)` is caught as an `Error` whose `message` contains `not a function` (inferred: the host-function error path keeps the managed message); count 0.
  - `NonNullNullableCallbackStaysRetainedAfterTheMethodReturns` (scenario "Non-null callback keeps context-retained lifetime", sync half; Review Focus 5): `m.callOptional(() => 5)` is `"5"`, count 1. `m.callStoredOptional()` is `"5"` (a later host call, after the method returned), count 1. `m.callDefaulted(name => 'Hi ' + name)` is `"Hi JS"`, count 2. `globalThis.f = () => 9; [m.callOptional(f), m.callOptional(f)].join(':')` is `"9:9"`, count 4.
  - `NonNullNullableCallbackInvokesAfterAwait` (same scenario, async half): follow `GeneratedModuleInvokesRetainedCallbackLater` (128-168): context created in `Runtime.Execute`, disposed in `finally`. `m.callOptionalAfterAwait(() => 7).then(v => { globalThis.__optionalAfterAwait = String(v); }, e => { globalThis.__optionalAfterAwait = 'error:' + e.message; })`; `await WaitForGlobalStringAsync(fixture, "__optionalAfterAwait")` is `"7"`; count 1. Then the same with `null` into `__optionalAfterAwaitNull`: `"-1"`; count still 1.
  - `ExplicitDisposeReleasesNullableCallbackOnce` (same scenario, dispose half; Review Focus 4): context kept outside `Execute`, as `RuntimeContextDisposeReleasesRetainedCallback` (277-304) keeps it. `m.callOptional(() => 5); m.disposeStoredOptional(); m.disposeStoredOptional();` then `m.callStoredOptional()` is caught as an `Error`; count 1. Dispose the context inside `Runtime.Execute`; count 0. `Assert.Throws<ObjectDisposedException>(() => GeneratedNullableCallbacksModule.LastOptional!.Invoke())`: the exact type proves teardown skipped the already-disposed callback (`DisposeFromRuntimeContext` returns early, `JavaScriptCallback.cs:146-156`).
  - `ContextTeardownReleasesUndisposedNullableCallbackOnce` (same scenario, teardown half): `m.callOptional(() => 5)` is `"5"`; count 1. Dispose the context inside `Runtime.Execute`; count 0. `Assert.Throws<InvalidOperationException>(() => GeneratedNullableCallbacksModule.LastOptional!.Invoke())`. Then `Assert.Null(Record.Exception(() => GeneratedNullableCallbacksModule.LastOptional!.Dispose()))`: the later explicit `Dispose` releases nothing again.
  - `NonNullCallbackParameterStaysStrict` (Review Focus 1): on the existing `GeneratedCallbacks` module, `m.callNoArgs(null)` and `m.callNoArgs(undefined)` are each caught as an `Error`; count 0.
  - `RequiredNullableCallbackStillRequiresItsArgument` (Review Focus 3): `m.callOptional()` is caught as an `Error` (the argument-count check, `GeneratedFunction.cs:118-145`); count 0.

- [ ] **Step 3: Write the shared-object test** (spec scenario "Shared-object parameters accept nullable callbacks").
  - `SharedObjectNullableCallbackParametersDecodeLikeModuleParameters`: `globalThis.a = new m.NullableCallbackEntry(null); globalThis.u = new m.NullableCallbackEntry(undefined); [a.seed, u.seed, a.call(null), a.call(undefined), a.call()].join(':')` is `"-1:-1:null:null:null"`, count 0. Then `globalThis.b = new m.NullableCallbackEntry(() => 3); [b.seed, b.call(name => 'Hi ' + name)].join(':')` is `"3:Hi entry"`, count 2 (one constructor decode and one method decode). Then `new m.NullableCallbackEntry(5)` is caught as an `Error`; count still 2. The constructor reads the context through `GeneratedFunction.CurrentRuntimeContext`, which the construct host function sets (`GeneratedSharedObjectClass.cs:334`, `GeneratedHostFunctionRegistration.cs:46`; inferred).

- [ ] **Step 4: Run the new tests**

Run: ModulesCore tests with `--filter "FullyQualifiedName~GeneratedCallbackModuleTests"`
Expected: PASS. A failing count or string is a finding to report with the observed value.

- [ ] **Step 5: Run everything.** Run: `scripts/test-managed.sh`. Expected: all pass, 0 skipped. Record the passed count for Task 4.

- [ ] **Step 6: Commit**

```bash
git commit -m "test(modules-core): prove nullable callback lifetimes on Hermes" \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```

### Task 4: Close the change

Model: **cheap**.

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/module-authoring-guide.md`, `docs/roadmap.md`
- Move: `docs/plans/032-nullable-javascript-callback-parameters.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-11-05-nullable-javascript-callback-parameters/` → `docs/archive/changes/`

- [ ] **Step 1: Merge the deltas into `docs/specs/modules-core-boundary.md`**, as the spec's "Delta against `docs/specs/modules-core-boundary.md`" section states. Line numbers are at plan time. 031 inserted "Nullable ArrayBuffer Is A Scoped Nullable Reference" (2004-2079), so the anchors below already include it.
  - `### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics` (1885-2003): in the first paragraph (1887-1899), drop `JavaScriptCallback<TResult>` and `JavaScriptCallback<TArgs, TResult>` from the list and "retained callback state" from the reasons. After the `ArrayBuffer?` sentence (1897-1899), add: a nullable callback is supported as a method or constructor parameter (see "Nullable Callback Parameters Decode Without Retention") and keeps the non-null rejection on every other position. The codes paragraph (1901-1910) stays: `EXPOJSI008` still covers an invalid callback codec.
  - Replace `#### Scenario: Nullable callback reports the callback code` (1941-1946) with `#### Scenario: Nullable callback outside a parameter keeps its diagnostic`, verbatim from the spec.
  - Insert `### Requirement: Nullable Callback Parameters Decode Without Retention` with its four scenarios, verbatim from the spec, right before `### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults` (2080). One correction: the spec's scenario text writes `JavaScriptCallback<(string), string>? cb = null`, and `(string)` is not a C# tuple type. Write `JavaScriptCallback<ValueTuple<string>, string>? cb = null`, which is what the fixtures use.
  - Consistency edits the spec does not list but its behavior requires (no new requirement; report them):
    - `#### Scenario: Static helper codec is used as a generic codec argument` (1242-1245): "a `Nullable<T>` or nullable-reference inner type is a `JavaScriptCallback` or a type that holds one" becomes "a `Nullable<T>` or nullable-reference inner type holds a `JavaScriptCallback` inside a list, dictionary, or record". A direct `JavaScriptCallback<...>?` now resolves to `NullableJavaScriptCallbackCodec`.
    - Position table, row `` `Nullable<T>` inner, nullable-reference inner `` (1511): add "A direct `JavaScriptCallback<...>?` resolves to the callback descriptor with `NullableJavaScriptCallbackCodec` before this rule."
    - Descriptor table, row `` `JavaScriptCallback<...>` `` (1532): name `JavaScriptCallback<...>` and `JavaScriptCallback<...>?`; the shape and ownership stay `StaticHelper` and `ContextRetained`.

- [ ] **Step 2: Merge into `docs/specs/ownership-and-scoped-refs.md`.** Under `### Requirement: Boundary Leaf Ownership Is Explicit` (202-208), append to the first paragraph after "A decoded `ArrayBuffer?` that is `null` holds no leaf and registers nothing.": "A nullable callback parameter that decodes to `null` holds nothing. A non-null one is context-retained by `DotnetRuntimeContext`, not scope-owned." The file has no list of unsupported nullable types (`rg -n 'JavaScriptCallback' docs/specs/ownership-and-scoped-refs.md` prints nothing at plan time), so the spec's "drops `JavaScriptCallback<...>?`" edit has nothing to remove. Say so in the task report.

- [ ] **Step 3: Update `docs/module-authoring-guide.md`.**
  - Callbacks bullet (163-168): after "A callback is a parameter type only: ...", add: "`JavaScriptCallback<...>?` is accepted as a method or shared-object constructor parameter, including with `= null`. JavaScript `null` and `undefined` arrive as C# `null`, and a function is retained like a non-null callback."
  - Unsupported annotated references paragraph (178-181): drop `JavaScriptCallback<...>?` and "retained callback state". The result names `JavaScriptValue?`, `SharedObject?`, `SharedRef<T>?`, and a nullable concrete `[ExpoSharedObject]` class, whose conversion carries JSI ownership or shared-object identity.

- [ ] **Step 4: Update `docs/roadmap.md`.** Row 032 (line 57): `DONE`, link `[032](archive/plans/032-nullable-javascript-callback-parameters.md)`; keep the column widths aligned. In the `**028–033**` note (115-139), replace "032 and 033 then apply their own null policies." with: "032 is done ([change](archive/changes/2026-11-05-nullable-javascript-callback-parameters/spec.md)): it landed on <date> with a local `scripts/test-managed.sh` run (<N> passed, 0 failed, 0 skipped). `JavaScriptCallback<...>?` now works as a module or shared-object method or constructor parameter; `null` and `undefined` decode to C# `null` and register nothing. 033 then applies its own null policy." Take `<N>` from Step 6.

- [ ] **Step 5: Move the brief and the change folder** with `git mv`. Fix every link to the old paths: `rg -n '032-nullable-javascript-callback-parameters|2026-11-05-nullable-javascript-callback-parameters' docs .agents AGENTS.md`. The only hits left point to `archive/` (the archived brief's own branch-name line is not a link and stays).

- [ ] **Step 6: Verify** with the spec's block:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'GeneratedConversionScope.*Callback|\.Register\([^\n]*JavaScriptCallback|Own[^\n]*JavaScriptCallback' \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator
git diff --unified=0 833a374..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

Expected: all tests pass with 0 skipped; the passed count is 1120 plus the test cases added in Tasks 1-3, minus the one deleted `GeneratorReportsCallbackDiagnosticForNullableCallbackParameter` (put the arithmetic in the report). Format is OK and `git diff --check` is clean. Both scans print nothing.

- [ ] **Step 7: Commit**

```bash
git commit -m "docs(modules-core): close nullable callback parameters" \
  -m "Claude-Session: https://claude.ai/code/session_01KE6iXYcK754xrC54k9x2Fv"
```
