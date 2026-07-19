# Runtime Lifetime Cleanup Delta

## Goal

Make the native runtime-lifetime model explicit, fix the missing Windows early
teardown phase, and keep test-only controls out of production bridge headers.

## Scope

- Add the JSI-safe teardown phase to the Windows installer before connector
  invalidation.
- Separate `RuntimeState` and `LongLivedObjectCollection` declarations and
  definitions into matching native files.
- Move ArrayBuffer test hooks behind a private testhost-only bridge header.
- Move Hermes console queue controls behind a private test-control companion.
- Document ownership, lifetime, and dependencies for the connector, opaque
  runtime handle, `jsi::Runtime`, runtime state, and long-lived collection.
- Add a P3 roadmap item for reusable cross-platform native build composition.

## Explicitly Deferred

- Refactoring `Expo.ModulesCore.ArrayBuffer` to an `IArrayBufferBacking`
  strategy. The existing private two-case representation is behaviorally
  correct and can be isolated later.
- A shared CMake subproject or another reusable build-target mechanism. This
  slice updates the existing platform source lists only as required by the
  native source split.
- Local Windows compilation. CI or a Windows machine remains the required
  platform verification evidence.

## Requirements

### Requirement: Windows uses the JSI-safe teardown phase

The Windows adapter SHALL prepare the opaque runtime handle while its connector
is still valid, then invalidate the connector, tear down the managed runtime
context, and release the opaque handle. It SHALL not invoke runtime work while
holding the installer mutex.

#### Scenario: RNW instance is destroyed

- **GIVEN** a Windows installed runtime owns a React Native connector and an
  opaque runtime handle
- **WHEN** `InstanceDestroyed` triggers teardown
- **THEN** the installer SHALL move both owners into local variables under its
  mutex
- **AND** it SHALL prepare the handle before invalidating the connector
- **AND** it SHALL release the handle before the locally owned connector is
  destroyed

### Requirement: Native lifetime entities have explicit ownership boundaries

The native bridge SHALL document the owner, borrowed dependencies, and valid
lifetime of `JsiRuntimeConnector`, the opaque `RuntimeHandle`, host-owned
`jsi::Runtime`, `RuntimeState`, and `LongLivedObjectCollection`.

#### Scenario: Connector outlives active runtime state access

- **GIVEN** a runtime handle creates a shared runtime state from a connector
- **WHEN** the state is Active or Closing
- **THEN** its connector pointer SHALL be a borrowed dependency protected by
  the host teardown order
- **WHEN** late invalidation begins
- **THEN** the state SHALL clear that borrow before the connector can be
  destroyed

#### Scenario: Retained JSI entries form a temporary cycle

- **GIVEN** a long-lived entry holds runtime state while the collection owns
  the entry
- **WHEN** the entry is released on JSI or abandoned without JSI
- **THEN** the collection SHALL erase the entry and break the cycle exactly
  once

### Requirement: Test-only bridge seams stay private

ArrayBuffer snapshot validation, counter inspection, bridge-handle test
release, and Hermes queue-control helpers SHALL be visible only to native
testhost code. Production connector and bridge public headers SHALL expose
only production lifecycle APIs.

#### Scenario: Testhost controls lifetime ordering

- **GIVEN** Hermes testhost needs to pause, observe, or drop runtime work
- **WHEN** it uses queue controls or bridge test hooks
- **THEN** it SHALL call a private test-only companion/header
- **AND** production consumers of `ExpoJsiBridge.h` and
  `HermesConsoleRuntimeConnector.h` SHALL not receive those test APIs

### Requirement: Testhost distinguishes abrupt and JSI-safe teardown

The Hermes testhost fixture's normal release path SHALL remain abrupt so queued
managed work faults without waiting for an active runtime callback. Tests that
need JSI-safe long-lived-object release SHALL explicitly prepare the runtime for
invalidation before invalidating its connector.

### Requirement: Platform build composition remains a roadmap item

The roadmap SHALL record a future reusable native build-composition mechanism
for the shared JSI bridge source set. This delta SHALL not introduce a CMake
subproject or redesign Apple and MSBuild integration.
