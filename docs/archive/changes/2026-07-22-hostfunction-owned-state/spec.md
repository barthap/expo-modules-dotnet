# Host-Function Owned Callback State Delta Spec

## Goal

Allow a managed host function to opt into owning its callback state and to
release that state exactly once when the callback context reaches any terminal
path.

This delta extends `docs/specs/host-functions-and-errors.md`.

## Scope

This change covers `Expo.JSI` host-function callback contexts, their managed
creation-failure and native release paths, and the disposal contract for state
owned by one host function. It does not change the native ABI, host objects,
module event emitters, or shared-object events.

`HostObjectContext` has the same ownership gap, but adding owned state to host
objects is deferred until a consumer requires it.

## Accepted Design

- `JavaScriptRuntime.CreateHostFunction` preserves its exact four-parameter
  overload for source and binary compatibility. A separate five-parameter
  overload accepts `Action<object>? disposeCallbackState` without a default
  value. The four-parameter overload delegates with no disposer and retains
  the existing externally-owned-state behavior.
- Each callback context stores its configured disposer and uses an interlocked
  terminal guard. An active-context registry roots the context behind a
  nonzero opaque token. Terminal release atomically removes that registry
  entry before cleanup, so repeated or concurrent release attempts are safe
  no-ops.
- The same release operation handles native host-function destruction and
  managed creation failure. Native destruction covers JavaScript collection
  and runtime teardown.
- The disposer may run on the thread that destroys the native host function or
  on the managed creation-failure thread. It SHALL NOT enter JSI, use a runtime
  access frame, or synchronously schedule JavaScript runtime work.
- `JavaScriptWeakObject.Dispose` is valid under this thread contract: it is
  idempotent, synchronizes detachment of its opaque handle, requires no access
  frame, does not enter JSI, and delegates release or abandonment to native
  runtime-lifetime coordination.
- Disposer exceptions are caught and reported to standard error. They never
  cross an unmanaged callback boundary and do not prevent captured-error
  storage or the callback context from being released.
- Passing no disposer means the state remains externally owned. Passing a
  disposer means that one host-function context owns one terminal call for
  that state. Sharing one state among several host functions while giving each
  an owning disposer is caller error unless the caller's disposer and state
  implement their own shared, idempotent ownership policy.

## ADDED Requirements

### Requirement: Owned Callback-State Overload

`Expo.JSI` SHALL preserve the exact four-parameter `CreateHostFunction` method
and SHALL expose a separate five-parameter overload for opting into
exactly-once terminal disposal of callback state. The four-parameter overload
SHALL preserve the existing externally-owned callback-state behavior.

#### Scenario: Externally owned callback state is preserved

- **GIVEN** a caller creates a host function through the four-parameter overload
- **WHEN** the callback context reaches a terminal release path
- **THEN** `Expo.JSI` SHALL free its internal callback context
- **AND** it SHALL NOT infer disposal from the state object's type
- **AND** it SHALL NOT dispose the caller's state

#### Scenario: One function owns its callback state

- **GIVEN** a caller creates a host function with a callback-state disposer
- **WHEN** its callback context reaches a terminal release path
- **THEN** `Expo.JSI` SHALL invoke that disposer with the callback state at most
  once
- **AND** it SHALL remove the callback context's active registry entry before
  invoking the disposer

#### Scenario: State is shared across host functions

- **GIVEN** several host functions receive the same externally-owned state
- **WHEN** their creation calls use the four-parameter overload
- **THEN** each function SHALL release only its own internal callback context
- **AND** ownership of the shared state SHALL remain with the caller

### Requirement: Callback-State Terminal Paths

An opted-in callback-state disposer SHALL run exactly once when host-function
creation fails, when JavaScript collection destroys the host function, or when
runtime teardown destroys a still-live host function. A repeated terminal
release attempt SHALL be a safe no-op for callback-state disposal.

#### Scenario: Host-function creation fails

- **GIVEN** managed code allocated a callback context with an owned-state
  disposer
- **WHEN** native host-function creation fails
- **THEN** the managed failure path SHALL invoke the common terminal release
  operation
- **AND** the disposer SHALL run exactly once

#### Scenario: JavaScript collects a host function

- **GIVEN** JavaScript no longer references a host function whose context owns
  callback state
- **WHEN** garbage collection destroys the native host function
- **THEN** the native release callback SHALL invoke the disposer exactly once

#### Scenario: Runtime teardown destroys a host function

- **GIVEN** JavaScript still references a host function whose context owns
  callback state
- **WHEN** runtime teardown destroys the native host function
- **THEN** the native release callback SHALL invoke the disposer exactly once

### Requirement: Callback-State Disposer Thread Safety

A callback-state disposer MAY run on any thread that destroys the native host
function, including a garbage-collection or runtime-teardown thread, or on the
managed thread handling creation failure. It SHALL NOT call into JSI, require a
runtime access frame, or synchronously schedule JavaScript runtime work.

#### Scenario: Owned state is a weak-object wrapper

- **GIVEN** callback state owns a `JavaScriptWeakObject`
- **WHEN** the host-function disposer calls `JavaScriptWeakObject.Dispose` on a
  native release thread
- **THEN** the wrapper SHALL atomically detach its opaque handle without
  entering JSI
- **AND** native lifetime coordination SHALL release or abandon the handle on
  its safe terminal path

### Requirement: Callback-State Disposer Exceptions

Managed callback-state disposer exceptions SHALL NOT cross unmanaged callback
frames or interrupt internal callback-context cleanup.

#### Scenario: Owned-state disposer throws

- **GIVEN** a host-function callback context has a disposer that throws
- **WHEN** the context reaches terminal release
- **THEN** `Expo.JSI` SHALL catch and report the exception
- **AND** it SHALL release captured-error storage and the callback context
- **AND** an unmanaged release callback SHALL return without a managed
  exception
