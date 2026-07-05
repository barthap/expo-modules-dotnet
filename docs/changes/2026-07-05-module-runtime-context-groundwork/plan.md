# Module Runtime Context Groundwork Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement context-backed authored module construction, optional module base ergonomics, context-owned module instance registry, advanced runtime accessor documentation, and `JavaScriptValue` codec groundwork without implementing EventEmitter.

**Architecture:** `DotnetRuntimeContext` owns runtime-scoped state and exposes a context-owned `ModuleRegistry`. Generated providers instantiate authored modules through that context-owned registry, preferring constructors that accept `DotnetRuntimeContext` while preserving parameterless modules. Low-level JSI wrapper codecs live in `Expo.ModulesCore.Codecs` and preserve generated-glue-owned argument lifetimes separately from explicit retained ownership.

**Tech Stack:** C#/.NET, Roslyn incremental generator, Expo.JSI managed wrappers, Hermes-backed managed testhost, xUnit.

## Important Semantics

- `JavaScriptValue` as a generated module argument is invocation-scoped and owned by generated glue. Authored module code may use it during the invocation, including until an async method settles, but must not dispose it or store it in module state.
- Returning a `JavaScriptValue` transfers ownership of that returned wrapper to generated glue. Authored code must not dispose a wrapper after returning it.
- If authored code needs to keep a `JavaScriptValue` in module state or dispose a local original, it must return an explicit retained copy such as `value.Retain()`.
- A future explicit retain or ownership-transfer API can let module authors keep a value beyond the generated invocation lifetime; that is not the default codec behavior.
- EventEmitter implementation is out of scope. Keep future event APIs in mind only when naming context-owned services and lifecycle behavior.

## Files

- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
  - Expose documented advanced `Runtime` accessor if current comments are insufficient.
  - Own and expose the context-scoped module registry.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
  - Convert or extend current static-helper shape into a context-owned registry for authored module instances.
  - Preserve clear APIs for `_expoDotnet.modules` JavaScript object creation and module object definition.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs`
  - Optional base class storing `DotnetRuntimeContext`.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`
  - Decode invocation-scoped arguments and encode retained result wrappers.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
  - Add doc comments for generated module argument ownership.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
  - Track constructor strategy instead of only `CanConstruct`.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  - Detect supported constructors and emit context-backed instantiation.
  - Emit cleanup for disposable decoded `JavaScriptValue` arguments after sync return or async settlement.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Cover constructor selection, unsupported constructors, and `JavaScriptValue` codec emission.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/*.cs`
  - Add Hermes-backed behavior tests for context constructor, optional base class, registry reuse, runtime separation, and `JavaScriptValue` ownership.
- Modify: `docs/specs/modules-core-boundary.md`
  - Merge accepted behavior after implementation.
- Modify: `docs/specs/managed-jsi-wrappers.md`
  - Document `JavaScriptValue` argument ownership once implemented.
- Move or remove: `docs/changes/2026-07-05-module-runtime-context-groundwork/`
  - Archive/remove transient change artifacts after living specs are updated.

## Task 1: Generator Constructor Model

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Write failing generator tests for constructor selection**
  - Add a module with only a parameterless constructor and assert generated code still constructs it.
  - Add a module with only `DotnetRuntimeContext` constructor and assert generated code passes `context`.
  - Add a module with both constructors and assert generated code prefers the context constructor.
  - Keep an unsupported constructor test asserting `EXPOJSI003`.

- [ ] **Step 2: Run focused generator tests and verify failure**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests`
  - Expected: new constructor-selection assertions fail.

- [ ] **Step 3: Add constructor strategy to the module model**
  - Replace or supplement `CanConstruct` with a strategy enum or equivalent model state.
  - Preserve existing diagnostic behavior for unsupported constructors.

- [ ] **Step 4: Emit context-backed construction**
  - Generate `new ModuleName(context)` for context-backed modules.
  - Keep `new ModuleName()` for simple modules.
  - Suppress invalid module registration when constructor diagnostics are present.

- [ ] **Step 5: Run focused generator tests**
  - Expected: generator tests pass.

- [ ] **Step 6: Commit**
  - Commit message: `Support context-backed module constructors`

## Task 2: Context-Owned Module Registry and Optional Base

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/DotnetRuntimeContextTests.cs`

- [ ] **Step 1: Write failing registry/runtime tests**
  - Verify a context-owned registry reuses the same authored module instance inside one `DotnetRuntimeContext`.
  - Verify separate contexts get separate instances for the same module type.
  - Verify `ModuleRegistry.GetOrCreateDotnetModulesObject` behavior remains compatible with `_expoDotnet.modules`.
  - Verify a module inheriting `Expo.ModulesCore.Module` receives the same context instance.

- [ ] **Step 2: Run focused managed tests and verify failure**
  - Run: `scripts/test-managed.sh --filter "FullyQualifiedName~ModuleRegistryTests|FullyQualifiedName~DotnetRuntimeContextTests"`
  - Expected: new tests fail.

- [ ] **Step 3: Implement context-owned registry**
  - Store the registry in `DotnetRuntimeContext`.
  - Keep authored module instance lookup distinct from JavaScript module object creation.
  - Keep teardown behavior compatible with existing context disposal tests.

- [ ] **Step 4: Add optional `Module` base class**
  - Store `DotnetRuntimeContext` in a protected property.
  - Do not require generated modules to inherit from it.

- [ ] **Step 5: Run focused managed tests**
  - Expected: registry/runtime tests pass.

- [ ] **Step 6: Commit**
  - Commit message: `Add context-owned module registry`

## Task 3: Advanced Runtime Access Documentation

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`

- [ ] **Step 1: Add XML doc test coverage if existing tooling checks public docs**
  - If no doc-check exists, inspect generated XML or compile warnings only.

- [ ] **Step 2: Add scary runtime accessor documentation**
  - State it does not marshal to the JavaScript runtime thread.
  - State callers must use scheduler APIs when needed.
  - State wrappers must not be used after runtime teardown.
  - State owned wrappers must be disposed by their owner.

- [ ] **Step 3: Build focused projects**
  - Run: `dotnet build packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`
  - Expected: build passes without XML doc regressions.

- [ ] **Step 4: Commit**
  - Commit message: `Document advanced runtime access`

## Task 4: JavaScriptValue Codec and Argument Lifetime

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptValueCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`

- [ ] **Step 1: Write failing generator tests for `JavaScriptValue` codec selection**
  - Assert `JavaScriptValue` parameters use `JavaScriptValueCodec`.
  - Assert `JavaScriptValue` returns use `JavaScriptValueCodec`.
  - Assert generated sync code disposes decoded argument wrappers after invocation.
  - Assert generated async code keeps decoded argument wrappers alive until settlement and disposes after settlement.

- [ ] **Step 2: Write failing Hermes-backed ownership tests**
  - A module method receives a `JavaScriptValue` argument and reads it during the call.
  - An async module method receives a `JavaScriptValue` argument and reads it after an `await`.
  - A module method returning `JavaScriptValue` transfers ownership of the returned wrapper to generated glue.
  - A module method can return `storedValue.Retain()` while keeping responsibility for the stored original.

- [ ] **Step 3: Add `JavaScriptValue` documentation**
  - Document that generated module arguments are owned by generated glue for the invocation lifetime.
  - Document that authored code must not dispose or retain the argument wrapper unless using an explicit retain/ownership-transfer API.

- [ ] **Step 4: Implement `JavaScriptValueCodec`**
  - `Decode(JavaScriptValueRef, runtime)` retains to an owned `JavaScriptValue` for generated glue.
  - `Decode(JavaScriptValue, runtime)` returns an owned wrapper according to current wrapper semantics.
  - `Encode(JavaScriptValue, runtime)` consumes the returned wrapper on behalf of generated glue.
  - Returning a retained copy leaves the original wrapper owned by authored module code.

- [ ] **Step 5: Implement generated cleanup**
  - For sync methods, dispose generated-owned `JavaScriptValue` argument wrappers after authored method return or exception.
  - For async methods, keep generated-owned argument wrappers alive until promise settlement, then dispose them.
  - Do not make module authors responsible for default argument disposal.

- [ ] **Step 6: Run focused generator and managed tests**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests`
  - Run: `scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedAttributeModuleTests"`
  - Expected: tests pass.

- [ ] **Step 7: Commit**
  - Commit message: `Add JavaScriptValue module codec`

## Task 5: Living Specs and Change Closeout

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Move or remove: `docs/changes/2026-07-05-module-runtime-context-groundwork/`

- [ ] **Step 1: Merge accepted behavior into living specs**
  - Add constructor/context registry behavior to `modules-core-boundary.md`.
  - Add `JavaScriptValue` generated argument ownership wording to `managed-jsi-wrappers.md`.
  - Keep EventEmitter implementation wording as future direction only if retained.

- [ ] **Step 2: Archive or remove transient change artifacts**
  - Follow the repo's current convention for completed `docs/changes/<slug>/` artifacts.

- [ ] **Step 3: Run docs hygiene checks**
  - Run: `rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills`
  - Expected: no new unintended matches.

- [ ] **Step 4: Commit**
  - Commit message: `Update specs for module runtime context`

## Task 6: Final Verification

**Files:**
- All changed files.

- [ ] **Step 1: Run managed suite**
  - Run: `scripts/test-managed.sh`
  - Expected: pass.

- [ ] **Step 2: Run formatter check**
  - Run: `scripts/format.sh --check --all`
  - Expected: pass. If it fails because files need formatting, run `scripts/format.sh`, then repeat the check.

- [ ] **Step 3: Run diff whitespace check**
  - Run: `git diff --check`
  - Expected: no output.

- [ ] **Step 4: Check committed content for local machine details**
  - Run a local privacy scan for absolute home paths, usernames, private hostnames, concrete local repo paths, and machine-specific install paths.
  - Expected: no newly introduced committed local paths or usernames. Existing intentional matches, if any, must be explained.

- [ ] **Step 5: Summarize branch state**
  - Include commits made, verification commands, and any residual risks.
