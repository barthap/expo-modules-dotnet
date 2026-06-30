# Expo.ModulesCore Generated-Building-Blocks Milestone

## Goal

Introduce the first real `Expo.ModulesCore` package boundary by moving
module-layer generated-looking behavior out of `Expo.JSI.Tests` and into a
package that future Roslyn-generated code can target.

This milestone makes `Expo.ModulesCore` real without introducing the public v2
authored API syntax or a source generator yet.

## Scope

### In Scope

- Create `managed/packages/Expo.ModulesCore`.
- Create `managed/packages/Expo.ModulesCore.Tests`.
- Move temporary module behavior tests out of
  `managed/packages/Expo.JSI.Tests/Modules`.
- Copy and adapt the necessary generated-looking module dispatch ideas from
  `experiments/hermes-console-hostfxr`.
- Add generated-binding runtime helpers that hand-written generated-looking
  providers can call.
- Add typed conversion helpers/codecs for primitive values and
  `IReadOnlyList<T>` array conversions.
- Introduce `scripts/test-managed.sh` as the canonical managed test command for
  both `Expo.JSI.Tests` and `Expo.ModulesCore.Tests`.
- Keep `scripts/test-managed.sh` as a compatibility wrapper for the existing
  low-level test command name.
- Keep lower-level JSI behavior covered directly in `Expo.JSI.Tests`.

### Out of Scope

- Roslyn source generator implementation.
- Public v2 authored syntax such as `[ExpoModule]`, `[JS]`, `[Record]`, or
  `[Event]`.
- Dead/inert authored API attributes that are not consumed by a generator.
- Runtime assembly scanning.
- Hot-path reflection, `MethodInfo.Invoke`, `Delegate.DynamicInvoke`,
  `object?[]` argument arrays, or JSON serialization for ordinary JSI values.
- Removing or moving files under `experiments/`.

## Design

### Package Boundary

`Expo.ModulesCore` SHALL sit above `Expo.JSI`.

`Expo.JSI` remains the low-level wrapper package for runtime, value, object,
array, function, promise, error, ownership, scheduler, and ABI behavior.

`Expo.ModulesCore` owns generated-binding runtime building blocks:

- module object registration under `globalThis.expo.modules.<Name>`;
- host-function dispatch helpers used by generated-looking providers;
- arity/type validation helpers where useful;
- primitive value codecs;
- `IReadOnlyList<T>` to/from `JavaScriptArray` conversion helpers.

Generated-looking providers in tests SHALL call authored C# methods directly.
They SHALL NOT use runtime reflection, dynamic invocation, `object?[]` argument
containers, or JSON serialization as the normal dispatch path.

### Authored API Posture

The intended future authored API shape is the v2 syntax described by
`docs/references/expo-modules-v2-api-syntax.md`, with generated output shaped
by `docs/references/expo-modules-v2-macro-expansions.md`.

This milestone SHALL NOT introduce that authored API as production surface.
Without the Roslyn generator, attributes such as `[ExpoModule]` and `[JS]`
would be inert dead code and could make the package appear more complete than
it is.

The current milestone proves the code that a future generator should emit or
call. A later Roslyn milestone may introduce the authored syntax and generate
providers that use these helpers.

### Module Registration

`Expo.ModulesCore` SHOULD provide a small helper surface for creating or
locating the JavaScript module namespace:

```text
globalThis.expo.modules.<ModuleName>
```

Generated-looking code may use that helper to install module objects and host
functions. The helper must be implemented in terms of `Expo.JSI` wrappers and
must preserve owned-wrapper disposal rules.

### Generated Dispatch

Generated-looking dispatch code SHALL follow this runtime flow:

```text
JavaScriptRuntime
  -> generated-looking provider
  -> Expo.ModulesCore registration helper
  -> globalThis.expo.modules.<Name>
  -> JavaScript host function
  -> Expo.ModulesCore dispatch/conversion helpers
  -> authored plain C# method
  -> Expo.ModulesCore return conversion helper
  -> JavaScriptValue returned to JSI
```

For example, `globalThis.expo.modules.Math.add(41.5, true)` should decode a
`double` and `bool`, call a plain C# `MathModule.Add(double, bool)` method
directly, and encode the returned `double`.

### Conversion Helpers

The first conversion helper surface SHOULD include:

- `bool`;
- `double`;
- `string`;
- `IReadOnlyList<T>` backed by `JavaScriptArray`;
- element codecs that generated-looking array helpers can compose.

The authored method surface for array values is `IReadOnlyList<T>`.
Generated-looking code MAY materialize `T[]` internally because it is simple
and satisfies `IReadOnlyList<T>`.

Array conversion helpers SHALL operate above `Expo.JSI` and SHALL NOT require
new native ABI surface.

### Experiments Remain Evidence

Files under `experiments/` are preserved proof evidence. This milestone SHALL
copy and adapt necessary generated-looking ideas from
`experiments/hermes-console-hostfxr`, but SHALL NOT delete, move, or rewrite the
experiment as part of package introduction.

## Test Ownership

### ModulesCore Tests

`Expo.ModulesCore.Tests` SHALL own module-layer behavior tests:

- generated-looking provider registration;
- `globalThis.expo.modules.<Name>` installation;
- generated dispatch calling authored C# methods directly;
- module arity/type failures through the host-function error boundary;
- module-level primitive conversions;
- module-level `IReadOnlyList<T>` array conversions.

The existing temporary tests under `managed/packages/Expo.JSI.Tests/Modules`
SHALL move to `Expo.ModulesCore.Tests` once equivalent coverage exists.

### JSI Tests

`Expo.JSI.Tests` SHALL keep or gain direct low-level tests for behavior that
module tests happen to exercise indirectly:

- `JavaScriptHostFunction` invocation mechanics;
- argument count and scoped argument refs;
- string UTF-8 and embedded-NUL conversion;
- bool, double, and string value conversion;
- JS-visible error propagation from host functions;
- array wrapper length/index/get/set/as-value behavior;
- ownership and disposal counters.

When moving a module test exposes that it was also guarding low-level JSI
behavior, the low-level assertion SHALL stay in `Expo.JSI.Tests` or be
refactored into a more direct `Expo.JSI.Tests` case.

## Requirements

### ADDED Requirement: ModulesCore Package Exists

The repository SHALL include `managed/packages/Expo.ModulesCore` as the first
package above `Expo.JSI`.

#### Scenario: Generated code needs module helpers
- **GIVEN** hand-written generated-looking provider code
- **WHEN** it registers a module object or dispatches a host function
- **THEN** it SHALL use `Expo.ModulesCore` helpers instead of placing
  module-layer abstractions in `Expo.JSI`

### ADDED Requirement: ModulesCore Tests Own Module Behavior

The repository SHALL include `managed/packages/Expo.ModulesCore.Tests` for
module-layer behavior.

#### Scenario: Array conversion proof is migrated
- **GIVEN** `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`
  currently proves generated-looking array conversion behavior
- **WHEN** `Expo.ModulesCore.Tests` has equivalent coverage
- **THEN** the temporary `Expo.JSI.Tests/Modules` test SHALL be moved out of
  `Expo.JSI.Tests`

### ADDED Requirement: Generated Dispatch Uses Direct Calls

Generated-looking dispatch SHALL call authored C# methods directly.

#### Scenario: Math module function is called from JavaScript
- **GIVEN** JavaScript evaluates
  `globalThis.expo.modules.Math.add(41.5, true)`
- **WHEN** the generated-looking provider dispatches the call
- **THEN** it SHALL decode arguments through typed helpers, call the authored
  C# method directly, and encode the return value without reflection, dynamic
  invocation, `object?[]`, or JSON serialization

### ADDED Requirement: Array Codecs Support IReadOnlyList

`Expo.ModulesCore` SHALL provide generated-binding helpers for
`IReadOnlyList<T>` array conversion.

#### Scenario: JavaScript array is passed to authored C#
- **GIVEN** JavaScript calls `globalThis.expo.modules.Array.sum([1, 2, 3.5])`
- **WHEN** generated-looking code decodes the argument
- **THEN** the authored C# method SHALL receive an `IReadOnlyList<double>` and
  return `6.5`

#### Scenario: Authored C# returns a read-only list
- **GIVEN** an authored C# method returns `IReadOnlyList<string>`
- **WHEN** generated-looking code encodes the result
- **THEN** JavaScript SHALL receive a real array whose joined value is
  `"one,two"`

### MODIFIED Requirement: Canonical Managed Test Runner

The repository SHALL provide one canonical managed test command that builds the
Hermes testhost and runs both low-level JSI tests and module-core tests.

#### Scenario: Developer runs the managed test suite
- **GIVEN** `Expo.ModulesCore.Tests` exists
- **WHEN** a developer runs the canonical managed test script
- **THEN** it SHALL build the native Hermes testhost and run both
  `Expo.JSI.Tests` and `Expo.ModulesCore.Tests`

#### Scenario: Existing test-managed command is used
- **GIVEN** existing docs and workflow may still reference `scripts/test-managed.sh`
- **WHEN** the script is retained for compatibility
- **THEN** it SHOULD delegate to the broader managed test command until docs
  and agents no longer depend on the old name

## Verification

Implementation SHALL be verified with:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
```

If `scripts/test-managed.sh` is introduced while `scripts/test-managed.sh` remains
as a compatibility wrapper, both commands SHOULD be exercised during the
implementation milestone.

If formatting fails because touched files need updates, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

## Acceptance Criteria

- `managed/packages/Expo.ModulesCore` exists and depends on `Expo.JSI`.
- `managed/packages/Expo.ModulesCore.Tests` exists and runs against the Hermes
  testhost.
- Temporary module tests are removed from `Expo.JSI.Tests/Modules` after
  equivalent `Expo.ModulesCore.Tests` coverage lands.
- Generated-looking module dispatch tests prove JS-visible calls through
  `globalThis.expo.modules.*`.
- `IReadOnlyList<T>` array conversion behavior is covered in
  `Expo.ModulesCore.Tests`.
- Lower-level JSI behavior that module tests previously covered indirectly
  remains directly covered in `Expo.JSI.Tests`.
- `experiments/hermes-console-hostfxr` remains present as proof evidence.
- No public v2 authored API attributes are introduced.
- No Roslyn generator is introduced.
- Forbidden hot-path reflection/dynamic/JSON patterns are absent from
  `managed/packages`.
