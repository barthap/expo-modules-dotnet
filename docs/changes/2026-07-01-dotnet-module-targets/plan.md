# Dotnet Module Targets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make generated C# module providers register into caller-supplied modules objects and move the default managed namespace to `globalThis._expoDotnet.modules`.

**Architecture:** `ModuleRegistry` owns plain-object module target helpers. Generated providers accept a `JavaScriptRuntime` plus a `JavaScriptObject modules` target and use `ModuleRegistry.DefineModule(runtime, modules, name)`. Proof and test callers choose `_expoDotnet.modules` through `ModuleRegistry.GetOrCreateDotnetModulesObject(runtime)`.

**Tech Stack:** C#/.NET 10, Roslyn source generator tests, Hermes-backed managed tests, repo living specs.

---

### Task 1: Add failing coverage for registration target injection

**Files:**
- Modify: `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`
- Modify: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`
- Modify: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedArrayModuleTests.cs`

- [ ] Update generator assertions to expect:
  - `Register(global::Expo.JSI.JavaScriptRuntime runtime, global::Expo.JSI.JavaScriptObject modules)`
  - `ModuleRegistry.DefineModule(runtime, modules, "Math")`
  - no `Register(global::Expo.JSI.JavaScriptRuntime runtime)` signature.
- [ ] Update generated-module tests to create the modules object with `ModuleRegistry.GetOrCreateDotnetModulesObject(runtime)` before calling providers.
- [ ] Update JS assertions to read from `globalThis._expoDotnet.modules`.
- [ ] Run focused tests and confirm failures are caused by the missing API/signature:
  ```sh
  dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
  EXPO_JSI_TESTHOST_LIBRARY=<absolute-testhost-path> dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
  ```

### Task 2: Implement `ModuleRegistry` target helpers

**Files:**
- Modify: `managed/packages/Expo.ModulesCore/ModuleRegistry.cs`

- [ ] Change `DefineModule` to accept `JavaScriptObject modules`.
- [ ] Add `GetOrCreateDotnetModulesObject(JavaScriptRuntime runtime)`.
- [ ] Keep `GetOrCreateObject` private and use it only for `_expoDotnet` and `modules` creation.
- [ ] Ensure the helper never accesses the `expo` property.
- [ ] Run focused `Expo.ModulesCore.Tests` once native testhost is available.

### Task 3: Update Roslyn-generated provider output

**Files:**
- Modify: `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`

- [ ] Emit `Register(runtime, modules)`.
- [ ] Null-check both `runtime` and `modules`.
- [ ] Emit `ModuleRegistry.DefineModule(runtime, modules, "...")`.
- [ ] Run generator tests and confirm they pass.

### Task 4: Update proof code and docs

**Files:**
- Modify: `experiments/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs`
- Modify: `experiments/hermes-console-app/managed/HermesConsoleApp/GeneratedModuleProvider.cs`
- Modify: `experiments/hermes-console-app/native/main.cpp`
- Modify: `experiments/hermes-console-app/README.md`

- [ ] Create/reuse `_expoDotnet.modules` before calling generated providers.
- [ ] Update hand-written provider setup to use `_expoDotnet.modules`.
- [ ] Update native proof JavaScript snippets from `global.expo.modules` to
  `global._expoDotnet.modules`.
- [ ] Update README wording to describe the managed namespace.

### Task 5: Merge accepted behavior into living specs

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`

- [ ] Replace current `globalThis.expo.modules` generated-provider requirements
  with caller-supplied target requirements.
- [ ] Add the default `_expoDotnet.modules` helper requirement.
- [ ] Preserve the existing future autolinking and hot-path reflection
  requirements.

### Task 6: Verify and clean transient workflow artifacts

**Files:**
- Delete or archive: `docs/changes/2026-07-01-dotnet-module-targets/`

- [ ] Run:
  ```sh
  scripts/test-managed.sh
  scripts/format.sh --check --all
  git diff --check
  rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
  ```
- [ ] Remove or archive the transient `docs/changes/2026-07-01-dotnet-module-targets/`
  artifacts after the living spec is updated.
- [ ] Re-run `git diff --check`.
