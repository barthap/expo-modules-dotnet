# Shared-object typed events

## Status

Completed on 2026-07-22. The binding listener-lifetime and dispatch-race
constraints were implemented and merged into the living specification.

## Goal

Let generated shared-object classes declare typed, awaitable events while
keeping each instance's listeners wholly owned by the JavaScript heap.

This delta extends `docs/specs/modules-core-boundary.md` and consumes the owned
host-function callback-state contract in
`docs/specs/host-functions-and-errors.md`.

## Scope

This change adds `[Event]` members to `[ExpoSharedObject]` classes, the six
existing event-emitter method names on event-capable shared-object prototypes,
per-instance JavaScript listener storage, weak registry-backed dispatch,
subscription cleanup, TypeScript event maps, an authored example, and build
diagnostics.

It does not change module-level events, observing hooks, the shared-object
identity or terminal-release contract, `Expo.JSI`, the native ABI, or C++.
Shared-object observing hooks and `Symbol.dispose` remain separate changes.

## Accepted design

- A shared-object event uses the same getter-only partial
  `Func<Task>`/`Func<T, Task>` authoring contract, event-safe codecs, default
  first-character lowercase rule, and verbatim explicit-name rule as a module
  event.
- Each event-capable class installation creates one runtime-unique private
  property key and installs `addListener`, `removeListener`,
  `removeAllListeners`, `emit`, `listenerCount`, and `removeSubscription` on
  the shared class prototype. The first listener creates a non-enumerable,
  non-configurable own Array property on that one shared-object instance.
  Array entries are ordinary JavaScript objects containing only the listener
  id, event name, and listener function. Removal replaces the Array with a
  compacted JavaScript Array. No managed listener table, callback state,
  registry entry, or runtime context retains a JavaScript listener function or
  ordinary instance wrapper after its runtime callback returns.
- `addListener` returns a JavaScript-owned subscription with `remove()`. Its
  host-function state owns only a `JavaScriptWeakObject` for the target plus
  scalar/string metadata. It passes an owned-state disposer to the five-
  parameter `CreateHostFunction` overload. Calling `remove()` more than once
  is a no-op; the first call removes the matching entry and atomically disposes
  its weak handle. If JavaScript collects the subscription without calling
  `remove()`, plan 021's creation-failure, GC, or teardown path disposes that
  weak handle exactly once. Collection of a subscription does not itself
  unregister the listener.
- Managed dispatch stores no ordinary wrapper. On the owning runtime thread it
  snapshots and validates the instance's registry entry under the registry
  gate, locks the entry's `JavaScriptWeakObject` outside that gate, then
  revalidates the same active entry under the gate. Release or teardown wins
  if it removes or marks the entry before the second validation; dispatch then
  disposes any locked wrapper and fails loudly. Dispatch wins at the second
  validation; its callback-owned target wrapper keeps the JavaScript target
  available while it reads and invokes that instance's listener Array, and it
  performs no later registry or weak-wrapper access. Terminal release may then
  proceed without waiting for listener code.
- Dispatch runs inline during current runtime access, through synchronous
  scheduling when available, or through the runtime's asynchronous task path.
  Zero listeners succeed. One listener throwing does not fail the task or
  prevent later listeners. Payload encoding, scheduling, released/collected
  targets, and runtime teardown fail or cancel the returned task instead of
  escaping from `Func.Invoke`.
- Event delegates initialize when the exact authored instance first enters its
  registered-class pairing path. Same-context initialization is idempotent;
  another context cannot rebind the instance. A getter used before pairing
  fails clearly.
- `DotnetSharedObject<TEventsMap>` extends the existing typed
  `DotnetEventEmitter<TEventsMap>` facade. Event-capable shared-object facades
  supply their own per-class event map.

## ADDED requirements

### Requirement: Shared objects declare typed awaitable events

`[Event]` SHALL be valid on an `[ExpoSharedObject]` class only when it is an
instance, getter-only partial property of exactly `Func<Task>` or
`Func<T, Task>`, and `T` has an event-safe compile-time codec. The implicit
JavaScript event name SHALL lowercase only the first C# property character.
`[Event(name)]` SHALL preserve a non-empty explicit name verbatim and SHALL NOT
strip an `On` prefix.

Each valid property SHALL return one cached delegate per managed shared-object
instance. That delegate SHALL become available when the instance first enters
its exact registered-class pairing path and SHALL NOT use runtime reflection.

#### Scenario: Payload and payload-less events are generated

- **GIVEN** an `[ExpoSharedObject]` class declares
  `[Event] public partial Func<Task> OnReady { get; }` and
  `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** generated registration pairs one managed instance
- **THEN** each property SHALL return a cached awaitable delegate for that
  instance
- **AND** awaiting `OnReady()` SHALL dispatch `onReady` without a payload
- **AND** awaiting `OnProgress(value)` SHALL dispatch `onProgress` with the
  generated `ProgressEvent` codec

#### Scenario: Explicit event name is preserved

- **GIVEN** a shared object declares
  `[Event("StatusChanged")] public partial Func<string, Task> OnStatus { get; }`
- **WHEN** its generated delegate dispatches
- **THEN** JavaScript SHALL observe `StatusChanged` verbatim
- **AND** it SHALL NOT receive `onStatus` as an alias

#### Scenario: Event member is read before pairing

- **GIVEN** authored code reads a valid shared-object event property before the
  instance enters a registered-class pairing path
- **WHEN** the generated getter runs
- **THEN** it SHALL throw a clear `InvalidOperationException`
- **AND** it SHALL NOT return `null` or a delegate bound to an unknown runtime

### Requirement: Shared-object listeners are JavaScript-owned and instance scoped

Every event-capable generated shared-object prototype SHALL expose
`addListener`, `removeListener`, `removeAllListeners`, `emit`,
`listenerCount`, and `removeSubscription` with the same observable argument,
listener-order, matching, and subscription semantics as the existing
ModulesCore event-emitter methods. These names SHALL be reserved against
authored `[JS]` shared-object members.

Listener entries SHALL live only in a private own JavaScript Array on the
target instance. Managed state SHALL NOT retain a strong reference to a
listener function or to the ordinary shared-object JavaScript instance outside
a runtime-thread callback frame.

#### Scenario: Two instances keep listeners separate

- **GIVEN** JavaScript holds two instances of one event-capable generated class
- **WHEN** it registers disjoint listeners for the same event name
- **THEN** dispatch from either managed instance SHALL invoke only listeners in
  that instance's own JavaScript Array
- **AND** no listener id or event name from the other instance SHALL select it

#### Scenario: Listener captures its own shared object

- **GIVEN** an instance listener closes over that same shared-object instance
- **WHEN** JavaScript drops all external references to the instance,
  subscription, and listener and Hermes collects the cycle
- **THEN** the private instance/listener cycle SHALL be collectible as one
  JavaScript-heap cycle
- **AND** the registry's existing collection release callback SHALL run
- **AND** no managed listener state SHALL keep the cycle reachable

#### Scenario: Listener methods survive context teardown in JavaScript

- **GIVEN** JavaScript retains an event-capable prototype method after its
  runtime context is disposed
- **WHEN** JavaScript invokes that method while the runtime is otherwise still
  callable
- **THEN** the method SHALL fail loudly through its disposed generated callback
  registration
- **AND** it SHALL NOT recreate listener state or access the disposed registry

### Requirement: Shared-object subscriptions release weak state exactly once

The JavaScript subscription returned from `addListener` SHALL own a `remove()`
host function. Its callback state SHALL own one target `JavaScriptWeakObject`
and SHALL pass an exactly-once disposer to `CreateHostFunction`. Calling
`remove()` repeatedly SHALL be a no-op after the first removal. Explicit
removal MAY dispose the weak handle early; the callback-state disposer SHALL
remain a safe exactly-once terminal cleanup.

#### Scenario: Subscription is explicitly removed

- **GIVEN** a live subscription for one listener entry
- **WHEN** JavaScript calls `remove()` one or more times
- **THEN** the first call SHALL remove only that entry from its target instance
- **AND** later calls SHALL be no-ops
- **AND** the target weak handle SHALL be disposed exactly once

#### Scenario: JavaScript collects a subscription

- **GIVEN** JavaScript drops a subscription without calling `remove()`
- **WHEN** host-function creation failure, JavaScript collection, or runtime
  teardown releases its callback context
- **THEN** plan 021's owned-state disposer SHALL dispose the target weak handle
  exactly once under its documented thread contract
- **AND** the disposer SHALL NOT enter JSI or require a runtime access frame

### Requirement: Shared-object event dispatch is awaitable and lifetime safe

Generated delegates SHALL return a non-null task and dispatch only on the
owning runtime thread. The dispatch target SHALL be reacquired only from the
registry entry's opaque `JavaScriptWeakObject`. Dispatch SHALL read listeners
from that target's private JavaScript Array and SHALL retain no target or
listener wrapper beyond the runtime callback.

Target acquisition SHALL use the gate/snapshot, out-of-gate weak lock, and
same-entry gate revalidation order in the accepted design. Release or teardown
that wins before revalidation SHALL make dispatch fail loudly. Dispatch that
wins revalidation MAY finish listener iteration from its callback-owned target
wrapper while terminal release proceeds. Neither ordering SHALL crash, leak a
bridge handle, invoke `OnRelease` twice, or repair a terminal entry.

#### Scenario: Dispatch has no listeners

- **GIVEN** an active paired shared object has no listener for a declared event
- **WHEN** authored code awaits its generated event delegate
- **THEN** dispatch SHALL complete successfully

#### Scenario: Listener throws

- **GIVEN** one instance has several listeners and one throws
- **WHEN** its generated event dispatch iterates the instance Array
- **THEN** later listeners SHALL still run in registration order
- **AND** the listener exception SHALL NOT fault the returned task

#### Scenario: Payload encoding fails

- **GIVEN** a generated shared-object event receives a payload that its codec
  cannot encode
- **WHEN** authored code invokes the delegate
- **THEN** the returned task SHALL fault with that encoding failure
- **AND** no listener SHALL run

#### Scenario: Release wins target acquisition

- **GIVEN** a retained generated event delegate and an active pairing
- **WHEN** explicit release, collection, or registry teardown removes the entry
  before dispatch's same-entry revalidation
- **THEN** the task SHALL fail or cancel loudly
- **AND** dispatch SHALL dispose any temporary locked target and SHALL NOT read
  listener storage

#### Scenario: Dispatch wins target acquisition

- **GIVEN** dispatch locks and revalidates one active entry before terminal
  release removes it
- **WHEN** terminal release begins while listener iteration is in progress
- **THEN** dispatch MAY complete from its callback-owned target wrapper
- **AND** terminal release SHALL preserve its existing exactly-once contract
- **AND** dispatch SHALL perform no later registry or weak-wrapper access

#### Scenario: Runtime context is already torn down

- **GIVEN** C# retains a generated shared-object event delegate after context
  teardown
- **WHEN** authored code invokes it
- **THEN** it SHALL return a faulted or canceled task
- **AND** it SHALL not touch a disposed runtime, registry entry, or weak handle

### Requirement: Invalid shared-object events are build diagnostics

Shared-object event validation SHALL use `EXPOJSI026` for invalid placement or
property shapes, `EXPOJSI027` for unsupported payloads, and `EXPOJSI028` for
duplicate effective event names. `EXPOJSI026` SHALL cover `[Event]` on a class
that is neither an `[ExpoModule]` nor a valid `[ExpoSharedObject]`, plus the
same unsupported static, indexed, non-partial, implemented, setter,
explicit-interface, ref-return, `[JS]`, modifier, delegate, and container
shapes rejected for module events. `EXPOJSI027` SHALL apply the same
event-safety rules as module events. Rejected reproducible partial properties
SHALL receive inert matching implementations so Expo diagnostics do not cause
secondary generated-C# errors.

#### Scenario: Event is placed on an unrelated class

- **GIVEN** a partial property with `[Event]` appears on a class that is not an
  `[ExpoModule]` and is not a valid `[ExpoSharedObject]`
- **WHEN** the generator analyzes the compilation
- **THEN** it SHALL report `EXPOJSI026` at the event property
- **AND** it SHALL not treat the class as an event dispatch target

#### Scenario: Event shape is invalid

- **GIVEN** an `[ExpoSharedObject]` class declares
  `[Event] public partial Action<string> OnStatus { get; }`
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI026` explaining that an awaitable
  `Func<T, Task>` is required

#### Scenario: Event payload is unsupported

- **GIVEN** a valid-shaped shared-object event has an unsupported payload, a
  callback, or a nested transfer-sensitive wrapper
- **WHEN** the generator analyzes the payload
- **THEN** it SHALL report `EXPOJSI027`
- **AND** it SHALL not emit reflection, dynamic conversion, callback `Encode`
  source, or a secondary compiler error

#### Scenario: Effective event name is duplicated

- **GIVEN** two valid shared-object event properties resolve to the same
  JavaScript event name
- **WHEN** the generator links the shared-object class
- **THEN** it SHALL report `EXPOJSI028`
- **AND** it SHALL not choose one declaration by source order

### Requirement: Shared-object TypeScript facades type event maps

`DotnetSharedObject<TEventsMap>` SHALL extend
`DotnetEventEmitter<TEventsMap>`, defaulting to an empty event map so existing
facades remain source compatible. An event-capable authored facade SHALL pass
its per-class listener map as `TEventsMap`.

#### Scenario: Shared-object listener is type checked

- **GIVEN** a facade extends `DotnetSharedObject<{ onChange(value: number): void }>`
- **WHEN** TypeScript checks `addListener` calls on that facade
- **THEN** `onChange` SHALL require a numeric listener payload
- **AND** undeclared event names or incompatible listener payloads SHALL fail
  type checking
