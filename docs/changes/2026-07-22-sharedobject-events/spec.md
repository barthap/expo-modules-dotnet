# Shared-object typed events

## Goal

Let each generated shared-object JavaScript instance expose typed, awaitable
events with the existing `EventEmitter` listener API.

## ADDED Requirements

### Requirement: Shared objects declare typed events

`[Event]` SHALL be valid on an `[ExpoSharedObject]` class only when it is an
instance, getter-only partial property of exactly `Func<Task>` or
`Func<T, Task>`. `T` SHALL have an event-safe compile-time codec. The implicit
JavaScript name SHALL lowercase only the first C# property character, and an
explicit `[Event(name)]` name SHALL be used verbatim.

#### Scenario: A shared-object event is generated

- **GIVEN** an `[ExpoSharedObject]` class declares
  `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** the generator creates its class support
- **THEN** the property SHALL return a cached awaitable delegate for that
  managed instance
- **AND** awaiting the delegate SHALL dispatch `onProgress` with the generated
  `ProgressEvent` codec

### Requirement: Shared-object listeners are instance scoped

Generated shared-object JavaScript classes SHALL expose the existing
`EventEmitter` method set, including subscriptions. Listener storage and
observing state SHALL be scoped to each generated shared-object instance.

#### Scenario: Two instances keep their listeners separate

- **GIVEN** JavaScript holds two instances of the same generated shared-object
  class
- **WHEN** it registers an event listener on each instance
- **THEN** dispatching an event from either managed instance SHALL call only
  listeners registered on its paired JavaScript object

### Requirement: Shared-object dispatch is awaitable and lifetime-safe

Generated shared-object event delegates SHALL return a non-null task. They
SHALL dispatch inline during runtime access and otherwise through the existing
runtime scheduling path. Dispatch SHALL reacquire the paired JavaScript object
from the registry's opaque weak object only on the runtime thread. It SHALL
not retain an ordinary JavaScript wrapper outside that callback.

Zero listeners SHALL complete successfully. A throwing listener SHALL not
fault dispatch or prevent later listeners. Dispatch after release, collection,
or runtime teardown SHALL fail loudly. Races with release or teardown SHALL
not crash, leak a bridge handle, or weaken exactly-once release.

#### Scenario: Released target rejects dispatch

- **GIVEN** a generated shared-object event delegate is retained by C#
- **WHEN** its paired JavaScript object is released or its runtime context is
  torn down before invocation
- **THEN** the delegate SHALL return a faulted or canceled task
- **AND** it SHALL not access a released runtime target

### Requirement: Invalid shared-object events are diagnostics

The generator SHALL report dedicated next-free `EXPOJSI` diagnostics for
invalid `[Event]` placement on shared-object classes, invalid event-property
shapes, unsupported event payload codecs, and collisions with generated or
reserved shared-object class members. It SHALL not emit reflection, dynamic
dispatch, or a partial fallback binding for rejected declarations.

### Requirement: Shared-object TypeScript facades type events

The generated shared-object TypeScript class event map SHALL expose its typed
event names and payloads so `addListener` calls are type checked per class.
