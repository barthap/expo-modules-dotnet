# Host Functions And Errors

## Purpose

Specify managed host-function callbacks and error propagation across the C ABI.

## Requirements

### Requirement: Managed Host Functions

`Expo.JSI` SHALL allow managed callbacks to be exposed as JavaScript host
functions through native JSI.

#### Scenario: Host function is created
- **GIVEN** managed code calls `CreateHostFunction`
- **WHEN** native successfully creates the JavaScript function
- **THEN** managed code SHALL receive a `JavaScriptFunction` owned wrapper and
  native SHALL hold a callback context with an explicit release callback

#### Scenario: Host function is invoked
- **GIVEN** JavaScript calls a managed host function
- **WHEN** native enters the callback
- **THEN** managed code SHALL receive a `JavaScriptRuntime`, scoped `this`
  value, `JavaScriptArguments`, and callback state

### Requirement: Callback Context Lifetime

Managed callback state SHALL be rooted while native owns the host function and
released exactly once when native destroys the host function.

#### Scenario: Host function creation fails
- **GIVEN** managed code allocated a callback context
- **WHEN** native fails to create the host function
- **THEN** managed code SHALL release the callback context on the failure path

#### Scenario: Host function is released
- **GIVEN** native no longer owns the host function
- **WHEN** it invokes the release callback
- **THEN** the managed callback context SHALL be freed

### Requirement: Managed Exceptions Become Structured Errors

Managed exceptions SHALL NOT cross unmanaged callback frames.

#### Scenario: Callback throws
- **GIVEN** a managed host-function callback throws an exception
- **WHEN** control returns to native
- **THEN** managed code SHALL return `ok = 0` with an error captured through
  the callback context
- **AND** the captured error message SHALL include managed exception stack-trace
  context

### Requirement: JavaScript Error Objects

`Expo.JSI` SHALL support creating and recognizing JavaScript Error objects.

#### Scenario: Error object is created
- **GIVEN** managed code calls `CreateErrorObject`
- **WHEN** native creates the JavaScript Error
- **THEN** managed code SHALL receive a `JavaScriptErrorObject` owned wrapper

#### Scenario: Error object fields are inspected
- **GIVEN** a `JavaScriptValue` is a JavaScript Error object
- **WHEN** managed code wraps it as `JavaScriptErrorObject`
- **THEN** accessors SHOULD tolerate JavaScript-visible mutation of ordinary
  error fields
