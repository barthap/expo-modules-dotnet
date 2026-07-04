# Async Module Functions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate promise-returning JavaScript module functions for authored `[JS]` methods that return `Task` or `Task<T>`.

**Architecture:** Keep `Expo.JSI` as the low-level promise/runtime owner and add the async module behavior in `Expo.ModulesCore`. The generator infers async functions from `Task`/`Task<T>` return types, decodes scoped JavaScript arguments during the host-function callback, and returns a Promise that settles through `JavaScriptRuntime.CreatePromise(...)`. Synchronous `[JS]` methods keep the existing direct-call path.

**Tech Stack:** C# 13 / .NET 10, Roslyn incremental source generator, Hermes-backed `Expo.JSI` testhost, xUnit v3, `scripts/test-managed.sh`, `scripts/format.sh`.

## File Structure

- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
  - Add generated function metadata for async functions: async kind, unwrapped `Task<T>` result type, and result codec expression.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  - Detect `System.Threading.Tasks.Task` and `System.Threading.Tasks.Task<T>` return types.
  - Emit `GeneratedFunction.DefineAsync(...)` for async functions.
  - Emit async host-function bodies that decode arguments before creating/settling the Promise.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
  - Add `DefineAsync(...)` as the async registration entry point beside `DefineSync(...)`.
  - Add small helpers only if they reduce duplicated generated code, such as wrapping an exception into a rejected Promise.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Add source-output assertions for `Task`, `Task<T>`, and unsupported `Task<T>` result types.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAsyncModuleTests.cs`
  - Add Hermes-backed module behavior tests for promise resolution and rejection.
- Modify `docs/specs/modules-core-boundary.md`
  - Merge the accepted async module requirements after implementation passes.
- Remove or archive `docs/changes/2026-07-04-async-module-functions/`
  - Do this after the living spec has been updated and verified.

## Task 1: Generator Model And Source Tests

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add failing generator tests for async source shape**
  - Add a test module source that imports `System.Threading.Tasks` and declares:
    - `[JS] public async Task CompleteAsync() { await Task.Yield(); }`
    - `[JS] public async Task<int> GetValueAsync() { await Task.Yield(); return 42; }`
  - Assert generated source contains:
    - `GeneratedFunction.DefineAsync(`
    - a host function for `CompleteAsync`
    - a host function for `GetValueAsync`
    - `JavaScriptPromiseResult.Resolve`
    - `runtime.CreateUndefined()` for `Task`
    - `NumberCodec<int>.Encode` for `Task<int>`
  - Keep assertions structural, not full-file snapshots.

- [ ] **Step 2: Add failing generator diagnostic test for unsupported `Task<T>`**
  - Add a source test with `[JS] public Task<decimal> BadAsync() => Task.FromResult(1m);`.
  - Assert `EXPOJSI002` is reported and the diagnostic message names `BadAsync` plus the unsupported return type.

- [ ] **Step 3: Run focused generator tests and confirm red**
  - Run:
    ```sh
    dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~ExpoModulesGeneratorTests
    ```
  - Expected: new async source-shape assertions fail because the generator still treats `Task` returns as unsupported.

- [ ] **Step 4: Extend function model for async metadata**
  - Add a compact representation to `ExpoFunctionModel`, for example:
    - `bool IsAsync`
    - `bool AsyncReturnsVoid`
    - `string AsyncResultType`
    - `string AsyncResultCodecExpression`
  - Keep sync fields intact so existing generated sync code changes minimally.
  - Avoid runtime reflection or dynamic invocation.

- [ ] **Step 5: Detect `Task` and `Task<T>` return types**
  - In return validation, detect:
    - non-generic `System.Threading.Tasks.Task` as async void-equivalent
    - generic `System.Threading.Tasks.Task<T>` as async result
  - For `Task<T>`, resolve the codec against `T`, not the outer task type.
  - If `T` has no codec, emit `EXPOJSI002`.
  - Do not add support for `ValueTask`, `ValueTask<T>`, arbitrary awaitables, `Task` parameters, or promise parameters.

- [ ] **Step 6: Emit async registration calls**
  - In provider emission, call `GeneratedFunction.DefineAsync(...)` when `function.IsAsync` is true.
  - Keep `GeneratedFunction.DefineSync(...)` for all non-task returns.

- [ ] **Step 7: Emit minimal async host-function code**
  - Generated async host functions must:
    - validate argument count immediately
    - decode all arguments immediately
    - call the authored method after decoding
    - return a JavaScript Promise value
  - Generated code must not capture `JavaScriptArguments`, `JavaScriptValueRef`, or `thisValue` across an `await`.

- [ ] **Step 8: Run focused generator tests and confirm green**
  - Run the same focused `dotnet test` command from Step 3.
  - Expected: generator tests pass.

- [ ] **Step 9: Commit Task 1**
  - Run:
    ```sh
    git diff --check
    git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
      packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
      packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
    git diff --cached
    git commit -m "feat: generate async module functions"
    ```
  - Before committing, inspect the staged diff for environment-specific paths,
    user identifiers, and host identifiers.

## Task 2: Async Generated Function Runtime Helpers

**Files:**

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`

- [ ] **Step 1: Add a failing runtime-helper compile/use path if needed**
  - If Task 1 emitted `GeneratedFunction.DefineAsync(...)`, build should fail until the helper exists.
  - Run:
    ```sh
    dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj
    dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~ExpoModulesGeneratorTests
    ```
  - Expected: fail on the missing helper until implemented.

- [ ] **Step 2: Add `GeneratedFunction.DefineAsync(...)`**
  - Match the `DefineSync(...)` registration shape:
    - validate `runtimeContext`, `module`, `name`, `callback`, and `context`
    - register callback state through `DotnetRuntimeContext.RegisterHostFunction(...)`
    - create the host function with `runtimeContext.Runtime.CreateHostFunction(...)`
    - assign it to the module property
  - Keep the callback signature as `JavaScriptHostFunction`; async generated host functions still return a `JavaScriptValue`, specifically the Promise value.

- [ ] **Step 3: Add rejected-promise helper only if it simplifies generated code**
  - If generated code would repeat exception-to-rejected-promise boilerplate, add an internal/public helper such as:
    - `GeneratedFunction.CreateRejectedPromise(JavaScriptRuntime runtime, Exception exception)`
  - Implement it with `runtime.CreatePromise(_ => Task.FromException<JavaScriptPromiseResult>(exception))`.
  - Return `promiseValue.AsValue()` and dispose the `JavaScriptPromiseValue` owner after cloning the value.

- [ ] **Step 4: Make generated async host functions reject instead of throw**
  - Wrap argument validation, argument decoding, and the authored method call in a generated `try`.
  - On any exception before a task is returned, return a rejected Promise value.
  - For `Task`, create a Promise operation that awaits the task and resolves with `runtime.CreateUndefined()`.
  - For `Task<T>`, create a Promise operation that awaits the task and resolves with `ReturnCodec.Encode(result, runtime)`.
  - Let faulted and canceled tasks flow through the existing `CreatePromise(...)` scheduler catch path so they reject as JS `Error`s.

- [ ] **Step 5: Run focused build/tests**
  - Run:
    ```sh
    dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj
    dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter FullyQualifiedName~ExpoModulesGeneratorTests
    ```
  - Expected: pass.

- [ ] **Step 6: Commit Task 2**
  - Run:
    ```sh
    git diff --check
    git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs \
      packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs
    git diff --cached
    git commit -m "feat: add async generated function helper"
    ```
  - Before committing, inspect the staged diff for environment-specific paths,
    user identifiers, and host identifiers.

## Task 3: Hermes-Backed Async Module Behavior

**Files:**

- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAsyncModuleTests.cs`

- [ ] **Step 1: Write failing Hermes-backed tests**
  - Add tests for:
    - `Task` function returns a Promise and resolves `undefined`
    - `Task<int>` resolves `42`
    - argument-count failure rejects, not synchronous throws
    - codec failure rejects, not synchronous throws
    - authored method throwing before returning a task rejects
    - faulted task rejects
    - canceled task rejects
  - Use the existing `HermesRuntimeFixture` and `fixture.WaitUntilIdle()`/`fixture.DrainTasks()` pattern.
  - Use JavaScript that records Promise outcomes onto `globalThis`, then drain the runtime loop and read the recorded result.

- [ ] **Step 2: Build the manual generated-looking provider**
  - In the test file, define a private module type with methods covering the cases above.
  - Define a private provider that registers the module with `GeneratedFunction.DefineAsync(...)`.
  - Host-function bodies should mirror expected generated output:
    - decode before Promise settlement
    - return rejected Promise for validation/codec failures
    - resolve `undefined` for `Task`
    - resolve encoded number for `Task<int>`

- [ ] **Step 3: Run focused managed runtime tests and confirm red/green**
  - Run the full repo runner first if the helper path needs native testhost setup:
    ```sh
    scripts/test-managed.sh --filter FullyQualifiedName~GeneratedAsyncModuleTests
    ```
  - If the filter is too broad across projects or matches no tests in earlier projects, run:
    ```sh
    scripts/test-managed.sh
    ```
  - Expected before implementation completion: new tests fail.
  - Expected after implementation completion: new tests pass.

- [ ] **Step 4: Confirm generated output and runtime tests agree**
  - Compare the manual provider in `GeneratedAsyncModuleTests.cs` with the emitted source shape asserted in generator tests.
  - If they diverge, update the manual provider or generator assertions so both represent the same contract.

- [ ] **Step 5: Commit Task 3**
  - Run:
    ```sh
    git diff --check
    git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAsyncModuleTests.cs
    git diff --cached
    git commit -m "test: cover async module promise behavior"
    ```
  - Before committing, inspect the staged diff for environment-specific paths,
    user identifiers, and host identifiers.

## Task 4: Living Spec Merge And Transient Artifact Cleanup

**Files:**

- Modify: `docs/specs/modules-core-boundary.md`
- Delete or archive: `docs/changes/2026-07-04-async-module-functions/spec.md`
- Delete or archive: `docs/changes/2026-07-04-async-module-functions/plan.md`

- [ ] **Step 1: Merge accepted async requirements into the living spec**
  - Update `docs/specs/modules-core-boundary.md` with:
    - async `[JS]` `Task`/`Task<T>` promise generation
    - argument decoding before await
    - argument/codec failures rejecting Promises
    - sync functions keeping direct-call behavior
  - Keep this as current-state wording after implementation has passed.

- [ ] **Step 2: Remove or archive transient change artifacts**
  - Once the living spec contains the accepted behavior, remove `docs/changes/2026-07-04-async-module-functions/`.
  - If preserving provenance is preferred in the repo’s current pattern, move it under `docs/archive/changes/` instead.
  - Do not leave the delta spec as the only source of truth.

- [ ] **Step 3: Run docs checks**
  - Run:
    ```sh
    git diff --check
    rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
    ```
  - Expected: `git diff --check` passes; the `rg` command returns no matches unless each match is intentional and explained.

- [ ] **Step 4: Commit Task 4**
  - Run:
    ```sh
    git diff --check
    git add docs/specs/modules-core-boundary.md
    git add -A docs/changes/2026-07-04-async-module-functions
    git diff --cached
    git commit -m "docs: merge async module function spec"
    ```
  - Before committing, inspect the staged diff for environment-specific paths,
    user identifiers, and host identifiers.

## Task 5: Final Verification

**Files:**

- Verify all implementation, test, and docs files touched by Tasks 1-4.

- [ ] **Step 1: Run the canonical managed suite**
  - Run:
    ```sh
    scripts/test-managed.sh
    ```
  - Expected: all managed generator, JSI, and ModulesCore tests pass.

- [ ] **Step 2: Run formatting check**
  - Run:
    ```sh
    scripts/format.sh --check --all
    ```
  - If it fails because files need formatting, run `scripts/format.sh`, then repeat the check.

- [ ] **Step 3: Run hot-path reflection guard**
  - Run:
    ```sh
    rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
    ```
  - Expected: no new generated-binding hot-path reflection, dynamic invocation, `object?[]`, or JSON serialization.

- [ ] **Step 4: Run final diff hygiene**
  - Run:
    ```sh
    git diff --check
    git status --short --branch
    ```
  - Expected: no whitespace errors; branch contains only intended async module function commits and no local-machine paths.

## Self-Review

- Spec coverage:
  - `Task` resolves `undefined`: Task 1 source assertions and Task 3 runtime tests.
  - `Task<T>` resolves encoded value: Task 1 source assertions and Task 3 runtime tests.
  - unsupported `Task<T>` result diagnostics: Task 1.
  - arguments decoded before await: Task 1 generated code, Task 3 manual provider parity check.
  - argument/codec failures reject Promises: Task 2 generated rejection path and Task 3 runtime tests.
  - sync direct-call behavior remains: Task 1 keeps sync path; existing sync tests remain in final suite.
- Unresolved-marker scan: clean.
- Type consistency:
  - `DefineAsync` is the async registration helper.
  - Generated host functions still return `JavaScriptValue`.
  - Promise settlement uses `JavaScriptRuntime.CreatePromise(Func<CancellationToken, Task<JavaScriptPromiseResult>>)` and `JavaScriptPromiseResult.Resolve(...)`.
