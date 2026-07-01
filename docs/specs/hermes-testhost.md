# Hermes Testhost

## Purpose

Specify the native Hermes-backed test host and managed test workflow used to
verify the portable core.

## Requirements

### Requirement: Canonical Test Runner

The repository SHALL use `scripts/test-managed.sh` as the canonical
Hermes-backed managed test runner.

#### Scenario: Test runner executes
- **GIVEN** a developer runs `scripts/test-managed.sh`
- **WHEN** the script runs
- **THEN** it SHALL build the native Hermes testhost, pass
  `EXPO_JSI_TESTHOST_LIBRARY` to managed tests, and run both `Expo.JSI.Tests`
  and `Expo.ModulesCore.Tests`

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
