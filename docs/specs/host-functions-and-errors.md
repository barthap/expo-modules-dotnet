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

Each callback context SHALL be rooted in an active-context registry behind a
nonzero opaque token. Terminal release SHALL atomically remove the registry
entry before cleanup, so repeated or concurrent release attempts are safe
no-ops.

#### Scenario: Host function creation fails
- **GIVEN** managed code allocated a callback context
- **WHEN** native fails to create the host function
- **THEN** managed code SHALL invoke the common terminal release operation on
  the failure path

#### Scenario: Host function is released
- **GIVEN** native no longer owns the host function
- **WHEN** it invokes the release callback
- **THEN** the managed callback context SHALL be freed

#### Scenario: Context release is repeated or concurrent
- **GIVEN** one callback context reaches terminal release
- **WHEN** more than one release attempt uses its opaque token
- **THEN** exactly one attempt SHALL remove and clean up the active context
- **AND** every later attempt SHALL be a safe no-op

### Requirement: Owned Callback-State Overload

`CreateHostFunction` SHALL preserve its exact four-parameter method for source
and binary compatibility. It SHALL expose a separate five-parameter overload
that accepts a callback-state disposer without relying on a default parameter.
When a disposer is present, terminal context release SHALL invoke it with the
callback state at most once. The four-parameter overload SHALL keep callback
state externally owned, and `Expo.JSI` SHALL NOT infer disposal from the state
object's type.

The same terminal operation SHALL cover managed creation failure, JavaScript
collection of the function, and runtime teardown of a still-live function.

#### Scenario: One host function owns callback state
- **GIVEN** a caller creates a host function with a callback-state disposer
- **WHEN** the callback context reaches any terminal release path
- **THEN** the context SHALL be removed from the active registry
- **AND** the disposer SHALL run exactly once

#### Scenario: Shared callback state remains externally owned
- **GIVEN** several host functions receive the same callback state
- **WHEN** their creation calls use the four-parameter overload
- **THEN** each function SHALL release only its own callback context
- **AND** ownership of the shared state SHALL remain with the caller

Passing the same state to several host functions with a disposer on each call
is caller error unless the caller supplies its own shared, idempotent ownership
policy.

### Requirement: Owned Callback-State Thread Contract

An owned callback-state disposer MAY run on the managed creation-failure thread
or any native thread that destroys the host function, including a garbage
collection or runtime-teardown thread. It SHALL NOT call into JSI, require a
runtime access frame, or synchronously schedule JavaScript runtime work.

`JavaScriptWeakObject.Dispose` SHALL be safe for this contract because it
atomically detaches its opaque handle, requires no access frame, does not enter
JSI, and delegates release or abandonment to native runtime-lifetime
coordination.

#### Scenario: Disposer runs on a native release thread
- **GIVEN** callback state owns a `JavaScriptWeakObject`
- **WHEN** native host-function destruction invokes its disposer
- **THEN** the wrapper SHALL detach its handle without entering JSI
- **AND** native lifetime coordination SHALL release or abandon the weak entry
  without retaining it beyond terminal cleanup

### Requirement: Owned Callback-State Exception Containment

Callback-state disposer exceptions SHALL be caught and reported on a
best-effort basis. Reporting failure SHALL also be contained. No managed
exception from owned-state disposal or reporting SHALL cross an unmanaged
release callback, and captured-error storage and remaining context cleanup
SHALL still complete.

#### Scenario: Callback-state disposer throws
- **GIVEN** a host-function callback context has a disposer that throws
- **WHEN** the context reaches terminal release
- **THEN** the exception SHALL be contained
- **AND** other callback contexts and internal cleanup SHALL continue

`HostObjectContext` does not yet expose owned callback-state disposal. A future
consumer SHOULD mirror this contract instead of inferring ownership from
`IDisposable`.

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
