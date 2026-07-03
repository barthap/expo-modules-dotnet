# Generator Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden `Expo.ModulesCore.Generator` so generated provider shape and unsupported authored module shapes are deterministic, diagnostic-driven, and ready for a slightly larger codec surface.

**Architecture:** Keep Roslyn symbol analysis, generator diagnostics, and generated-source emission as separate concerns inside the existing generator package. Add diagnostics through the existing descriptor/model path, then replace only the most awkward provider emission blocks with small raw-string emitter helpers if that directly reduces risk. Treat `void` and nullable primitive codecs as a checkpointed extension after generator hardening is verified.

**Tech Stack:** C# incremental source generator, Roslyn `Microsoft.CodeAnalysis`, xUnit generator tests, `Expo.ModulesCore` codecs, Hermes-backed managed tests.

## File Structure

- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
  - Own diagnostic descriptors and stable diagnostic IDs.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
  - Extend generator models only if diagnostics or emission need additional source locations or classification.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  - Collect unsupported authored shapes.
  - Detect duplicate exported names.
  - Emit provider source through direct-call generated functions.
  - Optionally introduce small raw-string emitter helpers.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Add failing tests for diagnostics and provider contract behavior.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`
  - Add helper assertions or diagnostic filtering only if tests become repetitive.
- Optional modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/*.cs`
  - Add `void` and nullable primitive codec support only after the checkpoint.
- Optional modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/*.cs`
  - Add Hermes-backed behavior tests for runtime-visible `void` or nullable primitives.
- Modify `docs/specs/modules-core-boundary.md`
  - Merge accepted generator hardening behavior after implementation.
- Remove or archive `docs/changes/2026-07-03-generator-hardening/` after living spec merge, following repo workflow.

## Task 1: Lock Current Provider Contract With Tests

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add provider contract assertions**
  - Strengthen the existing provider test to assert:
    - deterministic provider class name
    - both `Register(DotnetRuntimeContext context)` and `Register(DotnetRuntimeContext context, JavaScriptObject modules)` overloads
    - default overload calls `context.GetOrCreateDotnetModulesObject()`
    - explicit overload checks both arguments
    - generated registration uses `context.GetOrCreateModule(...)`
    - no `JavaScriptRuntime` provider overload is emitted

- [ ] **Step 2: Run generator tests**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
  - Expected: PASS, unless new assertions expose an existing provider contract gap.

- [ ] **Step 3: Fix only exposed provider contract gaps**
  - If the strengthened test fails, update `ExpoModulesGenerator.cs` to preserve the spec-approved provider shape.
  - Do not refactor emission style yet unless the failing contract is difficult to express safely with the current code.

- [ ] **Step 4: Re-run generator tests**
  - Expected: PASS.

## Task 2: Add Diagnostic Tests For Unsupported Method Shapes

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Optional modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`

- [ ] **Step 1: Add static `[JS]` method test**
  - Test source: module with `[JS] public static double Bad() => 1.0;`
  - Expected: one generator diagnostic with a new stable ID and message naming the method.

- [ ] **Step 2: Add generic `[JS]` method test**
  - Test source: module with `[JS] public T Bad<T>(T value) => value;`
  - Expected: one generator diagnostic with a new stable ID and message naming the method.

- [ ] **Step 3: Add duplicate JS function name test**
  - Test source: one module with `[JS("same")]` on two instance methods.
  - Expected: one duplicate-function diagnostic naming the module and exported JS name.

- [ ] **Step 4: Run generator tests to verify failures**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
  - Expected: FAIL because diagnostics are not implemented yet.

## Task 3: Implement Unsupported Method Diagnostics

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Optional modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`

- [ ] **Step 1: Add diagnostic descriptors**
  - Add stable IDs after `EXPOJSI003` for unsupported method shape and duplicate exported function name.
  - Keep category `Expo.ModulesCore` and severity `Error`.

- [ ] **Step 2: Collect `[JS]` attribute before filtering method shape**
  - Change `GetFunctions(...)` so `[JS]` methods that are static or generic are diagnosed instead of silently skipped.
  - Continue ignoring ordinary methods that do not have `[JS]`.

- [ ] **Step 3: Diagnose static and generic methods**
  - Use the method location when available.
  - Do not yield an `ExpoFunctionModel` for unsupported method shapes.

- [ ] **Step 4: Diagnose duplicate exported JS names within a module**
  - Detect duplicates after explicit `[JS("name")]` names are resolved.
  - Report diagnostics instead of source-order resolution.
  - Avoid yielding duplicate function models that would generate colliding host functions.

- [ ] **Step 5: Update diagnostic dispatch**
  - Update `ToDiagnostic(...)` to map all new descriptor IDs.

- [ ] **Step 6: Run generator tests**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
  - Expected: PASS for Task 2 tests and existing unsupported parameter/return tests.

- [ ] **Step 7: Commit diagnostic method-shape work**
  - Run: `git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests`
  - Run: `git diff --cached --check`
  - Scan staged content for local absolute paths, usernames, machine names,
    private hostnames, and machine-specific install paths.
  - Commit message: `test: harden generator method diagnostics`

## Task 4: Add Module Constructor And Duplicate Module Diagnostics

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`

- [ ] **Step 1: Add unsupported constructor test**
  - Test source: module with only a parameterized constructor.
  - Expected: existing `EXPOJSI003` diagnostic.
  - Verify generated source does not contain `new global::...BadModule()`.

- [ ] **Step 2: Implement constructor validation**
  - Accept public or internal parameterless constructors.
  - Reject modules that cannot be instantiated by generated direct call.
  - Do not introduce runtime reflection fallback.

- [ ] **Step 3: Add duplicate module name test**
  - Test source: two modules with the same explicit `[ExpoModule("Name")]`.
  - Expected: new duplicate-module diagnostic naming the exported module name.

- [ ] **Step 4: Implement duplicate module diagnostics**
  - Detect duplicates after all module models are collected.
  - Report diagnostics before emitting registration for duplicate modules.
  - Ensure duplicate module names are not resolved by source order.

- [ ] **Step 5: Run generator tests**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
  - Expected: PASS.

- [ ] **Step 6: Commit module-shape diagnostics**
  - Run staged whitespace and local-path scans.
  - Commit message: `feat: diagnose unsupported generated modules`

## Task 5: Emitter Cleanup Checkpoint

**Files:**
- Modify if needed: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify if needed: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Assess emission complexity**
  - If Tasks 1-4 remain readable with the existing emitter, skip raw-string cleanup.
  - If new diagnostics made emission harder to audit, extract small helpers:
    - `EmitProviderSource(...)`
    - `EmitRegisterOverloads(...)`
    - `EmitModuleRegistration(...)`
    - `EmitHostFunction(...)`

- [ ] **Step 2: Use raw interpolated strings only for stable blocks**
  - Keep loops and conditional logic outside templates.
  - Avoid T4, custom template engines, and `SyntaxFactory` construction.

- [ ] **Step 3: Preserve generated contract tests**
  - Run: `dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
  - Expected: PASS.

- [ ] **Step 4: Commit emitter cleanup if performed**
  - Commit message: `refactor: clarify generator source emission`
  - If skipped, record the skip in the final implementation summary.

## Task 6: Void And Nullable Primitive Checkpoint

**Files:**
- Modify if included: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify if included: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/*.cs`
- Modify if included: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify if included: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/*.cs`

- [ ] **Step 1: Decide whether to include this task**
  - Include only if Tasks 1-5 changed local generator/test code without broad model restructuring.
  - Skip if diagnostics required deeper generator architecture changes or if null handling exposes low-level `Expo.JSI` wrapper gaps.

- [ ] **Step 2: Add failing generator tests if included**
  - Test `void` return emits a host function that calls the authored method and returns JavaScript `undefined` or the repo's existing no-value representation.
  - Test nullable primitive parameters and returns use explicit nullable codec expressions.

- [ ] **Step 3: Add minimal runtime codec support if included**
  - Support only nullable `bool`, `double`, and `string`.
  - Preserve existing non-nullable codec behavior.
  - Do not add integer, enum, record, dictionary, async, or promise support.

- [ ] **Step 4: Add Hermes-backed behavior tests if runtime-visible behavior changed**
  - Put module behavior tests under `Expo.ModulesCore.Tests`.
  - Verify `null` and `undefined` behavior explicitly.

- [ ] **Step 5: Run focused tests**
  - Run generator tests.
  - If runtime-visible behavior changed, run the relevant `Expo.ModulesCore.Tests` tests or `scripts/test-managed.sh`.

- [ ] **Step 6: Commit optional codec work if included**
  - Commit message: `feat: add void and nullable primitive generated codecs`
  - If skipped, record the skip and reason in the final implementation summary.

## Task 7: Merge Accepted Delta Into Living Specs

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Remove or archive: `docs/changes/2026-07-03-generator-hardening/`

- [ ] **Step 1: Update living spec**
  - Merge implemented generator diagnostics and provider-contract requirements into `docs/specs/modules-core-boundary.md`.
  - Include `void` and nullable primitive requirements only if Task 6 was implemented.

- [ ] **Step 2: Remove or archive transient change artifacts**
  - Follow the repo's existing pattern for completed `docs/changes/*` artifacts.
  - Do not leave the delta spec as the only source of truth.

- [ ] **Step 3: Run docs checks**
  - Run: `git diff --check`
  - Run the stale-planning-pattern scan from `docs/README.md` if touched docs contain planning language.

- [ ] **Step 4: Commit living spec sync**
  - Commit message: `docs: merge generator hardening spec`

## Task 8: Final Verification

**Files:**
- Verify all touched files.

- [ ] **Step 1: Run managed test suite**
  - Run: `scripts/test-managed.sh`
  - Expected: PASS.

- [ ] **Step 2: Run formatting check**
  - Run: `scripts/format.sh --check --all`
  - Expected: PASS.
  - If it fails only because files need formatting, run `scripts/format.sh`, then repeat the check.

- [ ] **Step 3: Run final diff checks**
  - Run: `git diff --check`
  - Run: `git status --short`
  - Scan staged content before any final commit for local absolute paths, usernames, machine names, private hostnames, and machine-specific install paths.

- [ ] **Step 4: Summarize outcome**
  - Report implemented diagnostics.
  - Report whether emitter cleanup was performed or skipped.
  - Report whether `void` and nullable primitives were included or skipped, with reason.
  - Report exact verification commands and results.
