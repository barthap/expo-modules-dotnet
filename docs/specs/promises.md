# Promises

## Purpose

Specify the low-level promise capability and promise value wrappers exposed by
`Expo.JSI`.

## Requirements

### Requirement: Promise Capability

`JavaScriptRuntime.CreatePromise` SHALL create a native promise capability and
return a managed `JavaScriptPromise` wrapper.

#### Scenario: Promise is converted to value
- **GIVEN** managed code has a `JavaScriptPromise`
- **WHEN** it calls `AsValue`
- **THEN** it SHALL receive a `JavaScriptPromiseValue` owning the JavaScript
  promise value

#### Scenario: Promise capability is disposed
- **GIVEN** managed code owns a `JavaScriptPromise`
- **WHEN** it disposes the wrapper before settlement
- **THEN** native SHALL release the promise capability handle

### Requirement: Promise Settlement

Promise settlement SHALL happen through explicit resolve or reject operations
using owned JavaScript values.

#### Scenario: Promise resolves
- **GIVEN** managed code has a promise capability and a settlement value
- **WHEN** it calls resolve
- **THEN** native SHALL resolve the JavaScript promise with that value

#### Scenario: Promise rejects
- **GIVEN** managed code has a promise capability and an error value
- **WHEN** it calls reject
- **THEN** native SHALL reject the JavaScript promise with that value

### Requirement: Async Managed Promise Helper

`JavaScriptRuntime.CreatePromise(Func<CancellationToken,
Task<JavaScriptPromiseResult>>)` SHALL create a JavaScript promise value backed
by an asynchronous managed operation.

#### Scenario: Async operation resolves
- **GIVEN** the managed operation returns a resolve result
- **WHEN** the operation completes
- **THEN** the scheduler SHALL settle the native promise on the runtime path

#### Scenario: Async operation throws
- **GIVEN** the managed operation throws
- **WHEN** the scheduler observes the failure
- **THEN** the JavaScript promise SHALL reject with an error value

### Requirement: Promise Detection

`JavaScriptValue` SHALL expose promise detection before wrapping a value as a
promise value.

#### Scenario: Non-promise is wrapped
- **GIVEN** a `JavaScriptValue` is not a JavaScript Promise
- **WHEN** managed code calls `AsPromiseValue`
- **THEN** managed code SHALL throw `InvalidOperationException`
