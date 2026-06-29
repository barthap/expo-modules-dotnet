# Hermes Testhost

## Purpose

Specify the native Hermes-backed test host and managed test workflow used to
verify the portable core.

## Requirements

### Requirement: Canonical Test Runner

The repository SHALL use `scripts/test-jsi.sh` as the canonical Hermes-backed
test runner.

#### Scenario: Test runner executes
- **GIVEN** a developer runs `scripts/test-jsi.sh`
- **WHEN** the script runs
- **THEN** it SHALL build the native Hermes testhost and pass
  `EXPO_JSI_TESTHOST_LIBRARY` to `dotnet test`

### Requirement: Native Testhost

The native testhost SHALL create a Hermes runtime and expose test-only exports
used by `Expo.JSI.Tests`.

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
- **THEN** coverage SHALL live under `managed/packages/Expo.JSI.Tests` unless
  the behavior belongs to the future module layer

### Requirement: Module Tests Are Temporary

Module behavior tests under `Expo.JSI.Tests/Modules` SHALL remain temporary
until `Expo.ModulesCore.Tests` exists.

#### Scenario: Expo.ModulesCore is introduced
- **GIVEN** the repository adds `managed/packages/Expo.ModulesCore`
- **WHEN** equivalent module behavior coverage exists
- **THEN** temporary module dispatch and conversion tests SHALL move out of
  `Expo.JSI.Tests/Modules`
