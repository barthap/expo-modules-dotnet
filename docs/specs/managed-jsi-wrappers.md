# Managed JSI Wrappers

## Purpose

Specify the low-level `Expo.JSI` package. This package exposes managed wrappers
over the native ABI and intentionally does not own module DSL or source
generator concerns.

## Requirements

### Requirement: Runtime Wrapper

`Expo.JSI` SHALL expose `JavaScriptRuntime` as the managed access point for a
native JavaScript runtime.

#### Scenario: Runtime creates primitive values
- **GIVEN** managed code has a `JavaScriptRuntime`
- **WHEN** it calls `CreateNumber`, `CreateBool`, `CreateString`,
  `CreateUndefined`, or `CreateNull`
- **THEN** the runtime SHALL call the ABI and return an owned
  `JavaScriptValue`

#### Scenario: Runtime creates compound values
- **GIVEN** managed code has a `JavaScriptRuntime`
- **WHEN** it calls `Global`, `CreateObject`, `CreateArray`,
  `CreateHostFunction`, or `CreatePromise`
- **THEN** the runtime SHALL return a typed owned wrapper over a native handle

### Requirement: Typed Owned Wrappers

`Expo.JSI` SHALL expose typed owned wrappers for values, objects, arrays,
functions, promises, promise values, and error objects.

#### Scenario: Object wrapper accesses properties
- **GIVEN** a `JavaScriptObject`
- **WHEN** managed code gets or sets a property by name
- **THEN** the wrapper SHALL call the object property ABI functions using UTF-8
  property names

#### Scenario: Object wrapper enumerates own property names
- **GIVEN** a `JavaScriptObject`
- **WHEN** managed code asks for own property names
- **THEN** the wrapper SHALL call the object property-name ABI using opaque
  handles
- **AND** return managed strings that remain valid after the native call
- **AND** inherited prototype properties SHALL NOT be returned

#### Scenario: Array wrapper accesses indexes
- **GIVEN** a `JavaScriptArray`
- **WHEN** managed code reads length, gets a value, or sets a value at an index
- **THEN** the wrapper SHALL call the array ABI functions and preserve owned
  wrapper disposal rules

### Requirement: Explicit Value Conversions

Owned wrapper conversions SHALL be explicit and SHALL return independently
owned wrappers when they cross from one owned type to another.

#### Scenario: Value converts to object
- **GIVEN** a `JavaScriptValue` containing an object
- **WHEN** managed code calls `AsObject`
- **THEN** the returned `JavaScriptObject` SHALL own a retained handle and must
  be disposed independently

#### Scenario: Object converts to value
- **GIVEN** a `JavaScriptObject`
- **WHEN** managed code calls `AsValue`
- **THEN** the returned `JavaScriptValue` SHALL own a retained handle and must
  be disposed independently

### Requirement: Low-Level Package Boundary

`Expo.JSI` SHALL remain below the module DSL layer.

#### Scenario: Generated-looking module proof exists
- **GIVEN** generated-looking module behavior is covered by
  `Expo.ModulesCore.Tests`
- **WHEN** `Expo.JSI.Tests` changes
- **THEN** `Expo.JSI.Tests` SHALL remain focused on low-level wrapper, ABI,
  runtime, value, ownership, host-function, scheduler, and promise behavior
  instead of owning module-layer architecture
