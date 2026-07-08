# Hermes Testhost

## Purpose

Specify the native Hermes-backed test host and managed test workflow used to
verify the portable core.

## Requirements

### Requirement: Platform-Aware Hermes Prebuilt

The repo-local Hermes prebuilt root SHALL support platform-specific native link
layouts while preserving a common header root. macOS SHALL use
`hermesvm.framework`. Windows SHALL use the official Hermes `hermesvm` shared
library built from `scripts/hermes-ref.txt` and staged with `jsi.lib`,
`hermesvm.lib`, and `hermesvm.dll`.

#### Scenario: Windows Hermes prebuilt is staged
- **GIVEN** a Windows developer has VS 2026 C++ tooling and CMake available
- **WHEN** the developer runs `scripts/build-hermes-windows.ps1`
- **THEN** the script SHALL build the official Hermes `hermesvm` target
- **AND** it SHALL stage `include/hermes/hermes.h`, `include/jsi/jsi.h`,
  `lib/win32/<arch>/jsi.lib`, `lib/win32/<arch>/hermesvm.lib`, and
  `bin/win32/<arch>/hermesvm.dll` under the configured prebuilt root

#### Scenario: Upstream Intl build is unavailable
- **GIVEN** the supported Windows Hermes lane cannot build upstream Intl
  artifacts with the current Hermes revision
- **WHEN** `scripts/build-hermes-windows.ps1` runs without `-EnableIntl`
- **THEN** it SHALL build the official shared Hermes runtime with Intl disabled
- **AND** this choice SHALL remain isolated to the headless Hermes proof and
  testhost setup

### Requirement: Canonical Test Runners

The repository SHALL use platform-paired Hermes-backed managed test runners. On
macOS, `scripts/test-managed.sh` SHALL build the native Hermes testhost and run
managed test projects. On Windows, `scripts/test-managed.ps1` SHALL build the
Windows native Hermes testhost and run the same managed test projects. Both
runners SHALL pass `EXPO_JSI_TESTHOST_LIBRARY` to managed tests.

#### Scenario: macOS test runner executes
- **GIVEN** a developer runs `scripts/test-managed.sh`
- **WHEN** the script runs
- **THEN** it SHALL build the native Hermes testhost, pass
  `EXPO_JSI_TESTHOST_LIBRARY` to managed tests, and run both `Expo.JSI.Tests`
  and `Expo.ModulesCore.Tests`

#### Scenario: Windows test runner executes
- **GIVEN** a Windows Hermes prebuilt exists under the configured
  `HERMES_PREBUILT_ROOT`
- **WHEN** a developer runs `scripts/test-managed.ps1`
- **THEN** it SHALL build the native Windows Hermes testhost
- **AND** it SHALL run `Expo.ModulesCore.Generator.Tests`, `Expo.JSI.Tests`,
  and `Expo.ModulesCore.Tests`
- **AND** it SHALL pass the built testhost DLL through
  `EXPO_JSI_TESTHOST_LIBRARY`

### Requirement: Headless Hermes Console Runners

The headless Hermes console proof SHALL have platform-paired runners. The
macOS runner SHALL remain `scripts/run-hermes-console-app.sh`. The Windows
HostFXR runner SHALL be `scripts/run-hermes-console-app.ps1`.

#### Scenario: Windows HostFXR console proof runs
- **GIVEN** a Windows Hermes prebuilt exists under the configured
  `HERMES_PREBUILT_ROOT`
- **WHEN** a developer runs `scripts/run-hermes-console-app.ps1`
- **THEN** it SHALL build the managed console app project
- **AND** it SHALL build the native console executable through CMake
- **AND** it SHALL run the executable unless `-NoRun` is provided
- **AND** the proof SHALL exercise the same opaque C ABI and generated module
  registration behavior as the macOS console proof

### Requirement: Native Testhost

The native testhost SHALL create a Hermes runtime and expose test-only exports
used by managed tests. It lives under
`packages/expo-modules-dotnet/native/testhost` so native and managed test
linking can reference adapter-owned bridge code directly.

#### Scenario: Managed fixture creates runtime
- **GIVEN** a managed test calls `HermesRuntimeFixture.Create`
- **WHEN** the native testhost successfully creates a runtime
- **THEN** the fixture SHALL expose a managed `JavaScriptRuntime` backed by the
  native API table and runtime handle

### Requirement: Low-Level Test Ownership

`Expo.JSI.Tests` SHALL focus on low-level runtime, value, ABI, ownership,
host-function, scheduling, promise, and testhost behavior.

#### Scenario: Runtime behavior changes
- **GIVEN** a change modifies low-level wrapper or native ABI behavior
- **WHEN** tests are added or updated
- **THEN** coverage SHALL live under
  `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests` unless the
  behavior belongs to the future module layer

### Requirement: Module Test Ownership

Module behavior tests SHALL live under `Expo.ModulesCore.Tests`.

#### Scenario: Module behavior is tested
- **GIVEN** generated-looking module dispatch or conversion behavior is tested
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** coverage SHALL live under
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests`
