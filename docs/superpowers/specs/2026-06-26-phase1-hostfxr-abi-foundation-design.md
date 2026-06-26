# Phase 1 HostFXR And ABI Foundation Design

Date: 2026-06-26
Repo: `<repo>`

## Context

This repository is the clean research repository for the portable C# / JSI
bridge. The first implementation step should prove the loader and ABI
foundation without pulling standalone smoke-test code into the long-term package
layout.

The governing architecture remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## User Decisions

- Standalone loader smoke tests live entirely under `experiments/`.
- Real package code should start in maintainable monorepo-shaped locations.
- The repo does not need its entire final structure designed before coding.
- Future work should account for expo-desktop on both macOS and Windows.
- Future app fixtures should be maintainable examples, not one-off clutter.
- The bridge layer and module layer are separate:
  - `Expo.CSharpJsi` is the C++ -> C ABI -> C# bridge package.
  - `Expo.ModulesCore` is the higher-level C# modules package.
- Autolinking should be an authored package focused on Windows and macOS
  behavior, not a vendored copy of `expo-modules-autolinking`.
- The repo will likely need tools or scripts later.

## Initial Repository Shape

The first implementation should create only the directories needed for the
current proof, while using names that fit the future monorepo.

```text
experiments/
  hostfxr-smoke/
    native/
    managed/

docs/
  spike-results/

managed/
  packages/
    Expo.CSharpJsi/
    Expo.CSharpJsi.Tests/

native/
  include/
  packages/
    bridge/
      src/
      tests/
```

Expected future locations, not necessarily created in the first edit:

```text
examples/
  expo-desktop-app/

managed/
  packages/
    Expo.ModulesCore/

packages/
  autolinking/

tools/
  scripts/
```

`examples/` is the intended home for expo-desktop-based runnable apps. It is
plural because the repository may eventually need multiple runnable fixtures,
for example a minimal bridge app and a modules-core app.

`native/` is the general home for C and C++ code that belongs to the bridge or
future platform/package integrations. Standalone loader experiments stay under
`experiments/` and should not be imported by production package code.

## First Implementation Slice

Implement the first slice as:

```text
HostFXR loader proof + ABI handle foundation
```

Build only:

- a standalone HostFXR smoke proof under `experiments/hostfxr-smoke/`;
- the initial C ABI header under `native/include/`;
- a minimal fake or stub native bridge implementation under
  `native/packages/bridge/`;
- C# ABI declarations under `managed/packages/Expo.CSharpJsi/`;
- ABI layout tests under `managed/packages/Expo.CSharpJsi.Tests/`;
- result notes under `docs/spike-results/`.

Do not build yet:

- expo-desktop example app;
- RNW or React Native macOS adapter;
- `Expo.ModulesCore`;
- autolinking package;
- npm packaging;
- source generator;
- views.

## Component Boundaries

### `experiments/hostfxr-smoke`

Purpose: prove native macOS code can load a framework-dependent .NET assembly
with HostFXR, invoke a managed entry point, receive a known value, and release
owned memory explicitly.

This code is standalone. It may share naming conventions with the future bridge,
but it must not become a dependency of real package code.

### `native/include` And `native/packages/bridge`

Purpose: define and compile the bridge ABI foundation.

The ABI should expose C-compatible handles, enums, structs, function pointer
shapes, scheduler callback shape, retain/release operations, and structured
error results. It must not expose raw `jsi::Runtime`, `jsi::Value`,
`jsi::Object`, C++ STL types, C++ exceptions, or React Native scheduler types.

### `managed/packages/Expo.CSharpJsi`

Purpose: define the managed bridge layer that understands the C ABI.

This package should contain ABI declarations and, later, wrapper types such as
`JavaScriptRuntime`, `JavaScriptValue`, `JavaScriptObject`, and
`JavaScriptFunction`. It should not contain module DSL logic.

### `managed/packages/Expo.ModulesCore`

Purpose: future higher-level C# modules package.

This package should start only after the lower-level bridge semantics are clear
enough to support module APIs.

### `examples`

Purpose: future expo-desktop app fixtures for macOS and Windows.

Do not create the example app in the first slice. When it is created, it should
be a maintainable example app that exercises package consumption rather than a
standalone smoke test.

### `packages/autolinking`

Purpose: future authored autolinking package for Windows and macOS.

Do not create it in the first slice.

## Verification

The first implementation slice is complete only when the following evidence is
recorded:

- `dotnet --info` was run and captured in a result note.
- Managed projects build.
- Managed ABI layout tests pass.
- Native bridge skeleton builds.
- HostFXR smoke executable builds and runs.
- The smoke executable proves a managed entry point was invoked.
- Returned buffer or string ownership is documented and released explicitly.
- Result notes explain ownership and lifetime findings.
- Result notes explain scheduler findings, including which operations do not
  need scheduling yet.
- Result notes explain reflection and NativeAOT findings.

## Stop Gates

Stop and ask for review if:

- ownership of any handle, string, buffer, callback, promise, or error result
  cannot be described;
- the ABI needs raw C++ JSI layouts in C#;
- the ABI needs React Native scheduler types in C#;
- HostFXR convenience pushes the design toward runtime hot-path reflection;
- standalone experiment code starts becoming a dependency of real packages;
- the first slice starts pulling in expo-desktop, RNW, React Native macOS,
  views, npm packaging, autolinking, or module DSL work.

## Self-Review

- No placeholders remain.
- The first slice is narrower than the future monorepo.
- Standalone smoke code is isolated under `experiments/`.
- Real package code starts under package-shaped locations.
- The expo-desktop app location is identified as future `examples/` work.
- The design preserves the C++ -> C ABI -> C# boundary.
