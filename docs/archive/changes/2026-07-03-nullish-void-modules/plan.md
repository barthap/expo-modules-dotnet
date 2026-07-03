# Nullish and Void Module Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add correct JavaScript `undefined`, `null`, C# `void`, and nullable
double module semantics.

**Architecture:** Extend the JSI ABI with a new primitive value creation entry
at the end of the API table. Existing number and bool creation functions remain
unchanged for this slice, while nullish values use the new primitive entry.
Keep module behavior in `Expo.ModulesCore` by adding a nullable codec and
generator support for void returns, nullable codecs, and optional parameter
defaults.

**Tech Stack:** C++ JSI bridge, C ABI table, C# managed wrappers, Roslyn source
generator, Hermes-backed managed tests.

### Task 1: Lock behavior with tests

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`

- [x] Add low-level nullish creation tests.
- [x] Add generated module fixture methods for void and nullable cases.
- [x] Add Hermes-backed module behavior tests.

### Task 2: Add nullish value creation to JSI

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`

- [x] Append a `create_primitive_value(kind, value)` ABI slot.
- [x] Implement native JSI primitive creation for null and undefined.
- [x] Bump expected ABI version.
- [x] Expose `CreateUndefined` and `CreateNull` on `JavaScriptRuntime`.
- [x] Evaluate whether `JavaScriptValue.Undefined` can fit the owned-handle
  model without changing the host-function return contract.

### Task 3: Add nullable and void module dispatch

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NullableCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`

- [x] Decode nullish nullable values to C# `null`.
- [x] Encode C# nullable `null` as JavaScript `null`.
- [x] Emit `runtime.CreateUndefined()` for void returns.
- [x] Allow optional parameters to be omitted and treat explicit `undefined` as the default.

### Task 4: Verify and merge specs

**Files:**
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/modules-core-boundary.md`

- [x] Run focused managed tests.
- [x] Run `scripts/test-managed.sh`.
- [x] Run `scripts/format.sh --check --all`.
- [ ] Merge accepted behavior into living specs.
