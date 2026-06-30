# Modules Core Boundary

## Purpose

Define the boundary between low-level `Expo.JSI` wrappers and the
`Expo.ModulesCore` generated-binding helper package.

## Requirements

### Requirement: ModulesCore Owns Generated-Binding Helpers

`Expo.ModulesCore` SHALL own module registration helpers, generated dispatch
helpers, and typed conversion helpers above `Expo.JSI`.

#### Scenario: Generated-looking provider registers a module
- **GIVEN** generated-looking provider code has a `JavaScriptRuntime`
- **WHEN** it installs a module under `globalThis.expo.modules`
- **THEN** it SHALL use `Expo.ModulesCore` helpers instead of placing
  module-layer abstractions in `Expo.JSI`

### Requirement: ModulesCore Avoids Inert Authored Syntax

`Expo.ModulesCore` SHALL NOT expose public v2 authored API syntax before the
Roslyn generator milestone.

#### Scenario: Authored syntax is proposed
- **GIVEN** references describe future `[ExpoModule]`, `[JS]`, `[Record]`, or
  `[Event]` syntax
- **WHEN** no Roslyn generator consumes that syntax
- **THEN** the package SHALL keep that syntax out of production API

### Requirement: Generated Bindings Avoid Hot-Path Reflection

Generated v2 runtime bindings SHALL avoid runtime hot-path reflection and
dynamic invocation.

#### Scenario: Module provider invokes a method
- **GIVEN** generated provider code handles a JavaScript call
- **WHEN** it invokes the authored module method
- **THEN** it SHALL NOT use `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]` as the normal argument container, or
  JSON serialization for ordinary JSI values

### Requirement: ModulesCore Owns Module Tests

`Expo.ModulesCore.Tests` SHALL own module dispatch and conversion behavior.

#### Scenario: Module conversion behavior is tested
- **GIVEN** a test proves generated-looking module conversion behavior
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** the test SHALL live in `Expo.ModulesCore.Tests`
