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

### Requirement: Function Wrapper Invocation

`JavaScriptFunction` SHALL expose managed function invocation over the native
function-call ABI.

#### Scenario: Function is called
- **GIVEN** managed code owns a `JavaScriptFunction`
- **WHEN** it calls `Call` with zero or more representable JavaScript values
- **THEN** the wrapper SHALL call the ABI function-call entry
- **AND** return the JavaScript result as an owned `JavaScriptValue`

#### Scenario: Function is called with explicit this
- **GIVEN** managed code owns a `JavaScriptFunction`
- **AND** managed code owns a `JavaScriptObject` to use as `this`
- **WHEN** it calls `CallWithThis`
- **THEN** the wrapper SHALL call the ABI function-call-with-this entry
- **AND** return the JavaScript result as an owned `JavaScriptValue`

#### Scenario: Function is called as constructor
- **GIVEN** managed code owns a `JavaScriptFunction`
- **WHEN** it calls `CallAsConstructor`
- **THEN** the wrapper SHALL call the ABI constructor-call entry
- **AND** return the constructed object as an owned `JavaScriptObject`

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

#### Scenario: Value converts to retained value
- **GIVEN** a `JavaScriptValue`
- **WHEN** managed code calls `Retain` or `AsValue`
- **THEN** the returned `JavaScriptValue` SHALL own a retained handle and must
  be disposed independently

#### Scenario: Value converts to function
- **GIVEN** a `JavaScriptValue` or scoped `JavaScriptValueRef` containing a
  JavaScript function
- **WHEN** managed code calls `AsFunction`
- **THEN** the returned `JavaScriptFunction` SHALL own a retained handle and
  must be disposed independently

### Requirement: Low-Level Package Boundary

`Expo.JSI` SHALL remain below the module DSL layer.

#### Scenario: Generated-looking module coverage lives in ModulesCore.Tests
- **GIVEN** generated-looking module behavior is covered by
  `Expo.ModulesCore.Tests`
- **WHEN** `Expo.JSI.Tests` changes
- **THEN** `Expo.JSI.Tests` SHALL remain focused on low-level wrapper, ABI,
  runtime, value, ownership, host-function, scheduler, and promise behavior
  instead of owning module-layer architecture

### Requirement: Generated Module Value Ownership

`JavaScriptValue` SHALL document the ownership conventions used when generated
module glue passes or receives owned value wrappers.

#### Scenario: Generated module receives JavaScriptValue argument
- **GIVEN** generated module glue passes a `JavaScriptValue` argument to
  authored module code
- **WHEN** authored module code receives that wrapper
- **THEN** generated glue SHALL own the wrapper for the invocation lifetime
- **AND** authored module code SHALL NOT dispose it
- **AND** authored module code SHALL NOT store it beyond the invocation unless
  it first retains an explicit owned copy

#### Scenario: Authored module returns JavaScriptValue
- **GIVEN** authored module code returns a `JavaScriptValue`
- **WHEN** generated module glue receives that wrapper
- **THEN** ownership of the returned wrapper SHALL transfer to generated glue
- **AND** authored module code SHALL NOT dispose the wrapper after returning it
- **AND** authored module code that needs to keep an original wrapper SHALL
  return a retained copy instead
