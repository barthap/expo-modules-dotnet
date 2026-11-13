# Modules Core Events

## Purpose

Define typed events on modules and on shared objects: declaration,
dispatch, listener ownership, and build diagnostics.

## Requirements

### Requirement: Event Modules Use NativeModule Objects

`Expo.ModulesCore` SHALL expose legacy `[Events]` and typed `[Event]`
declaration syntax through generated bindings. Modules that declare either kind
of event SHALL be registered as
`_expoDotnet.NativeModule` instances so listener behavior comes from the
JavaScript prototype chain. `JavaScriptObjectFactory` SHALL install
`globalThis._expoDotnet.EventEmitter` and
`globalThis._expoDotnet.NativeModule` through generic `Expo.JSI` class and
prototype primitives, and SHALL own the managed listener storage behind the
inherited event methods. The ModulesCore-owned class hierarchy SHALL live under
`globalThis._expoDotnet` to avoid conflicts with upstream Expo classes under
`globalThis.expo`.

#### Scenario: Event module is registered
- **GIVEN** an authored module declares `[Events]` or `[Event]` with one or
  more event names
- **WHEN** generated registration installs the module
- **THEN** `ModuleRegistry` SHALL define the JavaScript module object as an
  `_expoDotnet.NativeModule` instance
- **AND** generated `[JS]` functions SHALL be attached as own properties
- **AND** inherited `EventEmitter` methods SHALL remain available through the
  prototype chain
- **AND** generated registration SHALL NOT create or mutate `globalThis.expo`

#### Scenario: Event syntax is non-inert
- **GIVEN** an authored module declares `[Events]`
- **WHEN** the event-name list is empty, blank, or contains duplicates
- **THEN** the generator SHALL report a diagnostic
- **AND** generated registration SHALL NOT silently create a plain module for
  inert event syntax

#### Scenario: Typed and legacy names merge for registration and hooks
- **GIVEN** a module declares typed `[Event]` members and legacy `[Events]`
  names
- **WHEN** the generator registers the module
- **THEN** it SHALL merge their distinct names before choosing the
  `NativeModule` prototype, attaching declared events, reserving observing-hook
  names, and validating observing hooks
- **AND** legacy `[Events]` declarations and `SendEventAsync` SHALL retain
  their existing behavior
- **AND** a duplicate between typed members or between typed and legacy
  declarations SHALL fail compilation instead of being resolved by source order

#### Scenario: Module emits an event
- **GIVEN** generated registration attached an authored module instance to its
  JavaScript event target
- **WHEN** authored C# code emits a declared event through
  `DotnetRuntimeContext.Events` or the `Module` base-class convenience helper
- **THEN** the event service SHALL call the target object's inherited `emit`
  function with the module object as `this`
- **AND** payload values SHALL be encoded through generated
  `IJavaScriptCodec<T>` support
- **AND** event emission while already executing on the owning JavaScript
  runtime SHALL dispatch directly without requiring generic synchronous runtime
  execution support from the host scheduler

#### Scenario: Listener identity is internal
- **GIVEN** JavaScript adds an event listener function
- **WHEN** ModulesCore stores listener state or removes listeners
- **THEN** it SHALL retain listener values internally and compare them by
  JavaScript strict equality
- **AND** it SHALL NOT mutate the user-provided listener function object

#### Scenario: Listener throws during emit
- **GIVEN** a module object has multiple listeners for an event
- **WHEN** one listener throws while `emit` dispatches the event
- **THEN** later listeners SHALL still be called
- **AND** the thrown listener error SHALL NOT propagate out of `emit`

#### Scenario: Undeclared event is emitted
- **GIVEN** an authored module declares a finite event-name list
- **WHEN** authored C# code emits an event name outside that list
- **THEN** emission SHALL fail loudly
- **AND** async generated `[JS]` callers SHALL observe a rejected JavaScript
  Promise

#### Scenario: Runtime context is disposed
- **GIVEN** a runtime context owns attached module event targets
- **WHEN** the context is disposed
- **THEN** the event service SHALL release retained JavaScript target handles
- **AND** later event emission SHALL fail instead of touching released runtime
  state
- **AND** inherited `EventEmitter` prototype methods retained in JavaScript
  SHALL fail loudly instead of recreating listener state

#### Scenario: New runtime context follows disposed context
- **GIVEN** a runtime context installed `_expoDotnet.EventEmitter` and
  `_expoDotnet.NativeModule` and was then disposed
- **WHEN** a new runtime context initializes in the same JavaScript runtime
- **THEN** `JavaScriptObjectFactory` SHALL install class constructors backed by
  the new context's listener state
- **AND** new module objects SHALL NOT call prototype functions retained from
  the disposed context

#### Scenario: EventEmitter identity is hidden native state
- **GIVEN** JavaScript adds a listener to a ModulesCore event emitter object
- **WHEN** ModulesCore needs to associate that object with managed listener
  storage
- **THEN** it SHALL attach an `EventEmitterNativeState` entry to the emitter
  object through the generic `Expo.JSI` NativeState API
- **AND** the native state entry SHALL contain only the managed emitter id used
  by `EventEmitterRuntimeState`
- **AND** ModulesCore SHALL NOT expose or mutate a
  `__expo_dotnet_emitter_id__` JavaScript property
- **AND** native `Expo.JSI` SHALL remain unaware of event names, listener
  tables, observing hooks, and `_expoDotnet` classes

#### Scenario: EventEmitter listener handles remain runtime-owned
- **GIVEN** ModulesCore stores listener functions and retained emitter handles
- **WHEN** a listener is removed or the owning runtime context is disposed
- **THEN** `EventEmitterRuntimeState` SHALL release the retained JavaScript
  handles through normal runtime-owned teardown
- **AND** NativeState release for `EventEmitterNativeState` SHALL NOT dispose
  JavaScript handles or call into JSI

### Requirement: Typed Event Members Are Awaitable Generated Properties

`[Event]` SHALL be valid only on an instance, getter-only partial property of
exactly `Func<Task>` or `Func<T, Task>`, where `T` has an event-safe
compile-time codec. The containing module SHALL be a top-level, non-generic
partial class. The generated implementation SHALL return one cached delegate
per module instance. `Action`, `Action<T>`, and other delegate shapes SHALL
not be supported because they would discard the dispatch task.

The implicit JavaScript event name SHALL be derived from the C# property name.
When the property name starts with exactly `On` or `on`, has more than two
characters, and its third character is an uppercase letter, the generator SHALL
drop that two-character prefix and lowercase only the first character of the
remainder invariantly. Otherwise it SHALL lowercase only the first character
invariantly, as for generated members. `[Event(name)]` SHALL use its explicit
name verbatim, including names that start with `on`. Each typed event SHALL be
declared under exactly one JavaScript name. The generator SHALL NOT also declare
the C# property name, the lower-camel property name, or the name without the
prefix as an alias. One generator helper SHALL derive the implicit name for
both module and shared-object events.

#### Scenario: Typed payload and payload-less events are generated
- **GIVEN** a module declares `[Event] public partial Func<Task> OnReady { get; }`
  and `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** generated registration creates the module
- **THEN** each property SHALL return a cached delegate
- **AND** awaiting `OnReady()` SHALL dispatch `ready` without a payload
- **AND** awaiting `OnProgress(value)` SHALL dispatch `progress` with its
  generated `ProgressEvent` codec

#### Scenario: Explicit typed event name is preserved
- **GIVEN** a module declares
  `[Event("StatusChanged")] public partial Func<string, Task> OnStatus { get; }`
  and
  `[Event("onDatabaseChange")] public partial Func<string, Task> OnDatabaseChange { get; }`
- **WHEN** generated registration declares the events
- **THEN** JavaScript SHALL observe `StatusChanged` and `onDatabaseChange`
  verbatim
- **AND** it SHALL NOT receive `status`, `onStatus`, or `databaseChange` as
  aliases

#### Scenario: Implicit typed event names drop the On prefix
- **GIVEN** modules declare bare `[Event]` properties named `OnProgress`,
  `OnStatusChange`, `onFoo`, `OnX`, `OnURLChange`, `Online`, `On`, `On1Thing`,
  `On_Foo`, `ONLine`, and `Changed`
- **WHEN** the generator derives their JavaScript names
- **THEN** the names SHALL be `progress`, `statusChange`, `foo`, `x`,
  `uRLChange`, `online`, `on`, `on1Thing`, `on_Foo`, `oNLine`, and `changed`

#### Scenario: Listener on the old prefixed name receives nothing
- **GIVEN** a module declares `[Event] public partial Func<string, Task> OnProgress { get; }`
- **AND** JavaScript adds one listener for `progress` and one for `onProgress`
- **WHEN** C# awaits `OnProgress("50%")`
- **THEN** the `progress` listener SHALL receive `"50%"` once
- **AND** the `onProgress` listener SHALL NOT be called

#### Scenario: Event and member names are separate namespaces
- **GIVEN** a module declares `[Event] public partial Func<string, Task> OnStatus { get; }`
  and `[JS] public string Status => ...`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report no diagnostic
- **AND** JavaScript SHALL read the `status` property and SHALL also receive
  `status` events through `addListener`

### Requirement: Typed Event Tasks Carry Dispatch Outcomes

Generated typed-event delegates SHALL dispatch through `ModuleEventEmitter` and
return its completion task. They SHALL dispatch inline during current runtime
access, use existing synchronous scheduling when available, and otherwise use
the existing asynchronous runtime-task path. They SHALL NOT block waiting for
asynchronous scheduling or discard the task.

The task SHALL complete only after target lookup, payload encoding, listener
iteration, and generated payload cleanup finish. Target lookup, encoding,
scheduling, disposed-context, and teardown failures SHALL fault or cancel that
task instead of being swallowed or escaping directly from `Func.Invoke`.
After initialization, every delegate invocation SHALL return a non-null task.
Listener exceptions retain the existing isolation rule: they SHALL not fault
the dispatch task or prevent later listeners from running.

#### Scenario: Off-runtime typed event is awaited
- **GIVEN** authored managed code invokes an initialized typed-event delegate
  without owning runtime access
- **WHEN** the host requires asynchronous runtime scheduling
- **THEN** the returned task SHALL represent the scheduled operation
- **AND** awaiting it SHALL expose its success, failure, or cancellation

#### Scenario: Immediate typed-event validation fails
- **GIVEN** an initialized typed-event delegate receives a disposed direct
  payload or its runtime context is already disposed
- **WHEN** authored code invokes the delegate without awaiting it yet
- **THEN** invocation SHALL return a non-null faulted or canceled task
- **AND** the failure SHALL NOT escape synchronously from `Func.Invoke`

#### Scenario: Typed event listener throws
- **GIVEN** one JavaScript listener throws while handling a typed event
- **WHEN** the existing `EventEmitter` iterates listeners
- **THEN** later listeners SHALL still run
- **AND** the listener exception SHALL NOT fault the returned dispatch task

#### Scenario: Cached typed delegate survives context teardown
- **GIVEN** authored code retains a generated typed-event delegate
- **WHEN** its `DotnetRuntimeContext` is disposed before a later invocation
- **THEN** awaiting that invocation SHALL fail or cancel loudly
- **AND** it SHALL not access a disposed target or stale JSI handle

### Requirement: Typed Event Members Initialize Before Lifecycle Hooks

Generated registration SHALL initialize typed-event delegates with the owning
`DotnetRuntimeContext` before a newly created module's `OnCreate` hook runs.
Both parameterless and `DotnetRuntimeContext` constructor strategies SHALL use
the same generated initialization, including modules that do not inherit
`Module`. The provider SHALL create and inject the dispatch delegates; the
generated module partial SHALL store and expose them but SHALL NOT reference
provider-private generated record codecs.

Initialization SHALL be idempotent for one module/context pair. Repeated
same-context initialization SHALL preserve delegate identity. Binding the same
module instance to another context SHALL fail.

#### Scenario: Lifecycle hook reads an initialized event member
- **GIVEN** a module declares a typed event and an `OnCreate` hook
- **WHEN** generated registration constructs the module
- **THEN** typed-event initialization SHALL finish before `OnCreate` runs
- **AND** the hook SHALL receive the same cached delegate later returned by the
  property

#### Scenario: Constructor reads an event member too early
- **GIVEN** an authored constructor accesses a generated typed-event property
  before registration can initialize it
- **WHEN** the getter runs
- **THEN** it SHALL throw a clear `InvalidOperationException`
- **AND** it SHALL NOT return `null` or an unbound delegate

### Requirement: Typed Event Payload Ownership Is Explicit

Generated typed-event dispatch SHALL capture no scoped ref. For ordinary
payloads, authors SHALL keep mutable captured state stable until the returned
task completes.

For a direct `ArrayBuffer`, generated glue SHALL synchronously retain an
invocation-owned lease before returning the task. The original remains
caller-owned, and the caller MAY dispose it after invocation returns. The lease
SHALL remain alive until dispatch reaches a terminal state and release exactly
once on success, failure, or cancellation.

For a direct `ArrayBuffer?`, a null payload SHALL dispatch one explicit
JavaScript `null` argument through the same payload path, SHALL retain no
lease, and SHALL NOT use the payload-less dispatch. A non-null payload SHALL
follow the direct `ArrayBuffer` rule unchanged.

For a direct `JavaScriptValue`, generated glue SHALL retain and encode an
invocation copy only while executing on the owning runtime. The caller SHALL
keep the original wrapper alive until the returned task completes. Generated
glue SHALL dispose only its invocation copy and SHALL NOT consume the original.

Records, lists, and dictionaries that hold a `JavaScriptValue`, an
`ArrayBuffer`, an `ArrayBuffer?`, or a `Uint8Array`, and a direct `Uint8Array`
or `Uint8Array?`, SHALL be valid typed event payloads. Generated glue SHALL
encode them on the owning runtime and SHALL dispose only the encoded value. A
null `ArrayBuffer?` or `Uint8Array?` inside such a payload, and a direct null
`Uint8Array?`, SHALL encode as JavaScript `null`. The author keeps every leaf
and SHALL keep it alive until the returned task completes. A payload
containing `JavaScriptCallback` SHALL stay rejected.
Event-safety classification SHALL run before general codec resolution can
mutate generated-record-codec state, and SHALL inspect only encoded inputs
such as selected record-constructor parameters, list elements, dictionary
values, and nullable-value inner types. A rejected payload
SHALL not leave callback `Encode` source or secondary compiler errors behind.
`JavaScriptObject` remains a possible future optional advanced convertible; it
is not a current generated module codec.

#### Scenario: Direct JavaScriptValue ownership is runtime-affine
- **GIVEN** authored code invokes a typed event with an owned `JavaScriptValue`
- **WHEN** dispatch is scheduled for later runtime execution
- **THEN** the caller SHALL keep the original wrapper alive until task
  completion
- **AND** generated glue SHALL retain and encode only an invocation copy during
  runtime access
- **AND** the original SHALL remain usable after successful dispatch

#### Scenario: Direct ArrayBuffer owns a scheduling lease
- **GIVEN** authored code invokes a typed event with an owned `ArrayBuffer`
- **WHEN** dispatch is scheduled for later runtime execution
- **THEN** generated glue SHALL retain an independent lease before returning
- **AND** the caller MAY dispose the original after invocation returns
- **AND** terminal cleanup SHALL release the retained lease exactly once

#### Scenario: Direct nullable ArrayBuffer event delivers null without a lease
- **GIVEN** a module or shared-object typed event declares
  `Func<ArrayBuffer?, Task>`
- **WHEN** authored code invokes it with `null`
- **THEN** the listener SHALL receive exactly one argument that is
  JavaScript `null`
- **AND** `LongLivedArrayBuffersReleased` SHALL NOT change
- **WHEN** authored code invokes it with a buffer and disposes the buffer
  after the call returns
- **THEN** dispatch SHALL still deliver the bytes through its retained lease
- **AND** the lease SHALL release exactly once

#### Scenario: Nested leaf payload stays with the author
- **GIVEN** a module or shared-object event payload is a record holding a
  `JavaScriptValue` and an `ArrayBuffer`
- **WHEN** authored code awaits the event
- **THEN** listeners SHALL receive both values
- **AND** the author's leaves SHALL stay usable after the task completes
- **AND** each encoded copy SHALL release exactly once

### Requirement: Invalid Typed Events Are Build Diagnostics

Typed-event validation SHALL use `EXPOJSI018` for invalid event-property
shapes, `EXPOJSI019` for unsupported event payloads, and `EXPOJSI020` for
typed/typed or typed/legacy duplicate names. `EXPOJSI018` covers null, empty,
or blank explicit names; unsupported static, indexed, non-partial, implemented,
setter, explicit-interface, ref-return, `[JS]`, or modifier shapes; unsupported
delegate types; and file-local, nested, generic, or non-partial containers.
`EXPOJSI019` covers payloads with no encode-capable event codec, a callback
codec, or a nullable annotation on a `JavaScriptValue` at any depth.
Legacy-only invalid or duplicate `[Events]` declarations SHALL continue to use
`EXPOJSI009`.
Duplicate detection SHALL compare derived and explicit JavaScript names, not C#
property names.

When a rejected member's partial-property declaration can be reproduced safely,
the generator SHALL emit an inert matching implementation so the consuming
compilation receives the Expo diagnostic without a secondary generated-C#
error. It SHALL not attempt a declaration for shapes that cannot be reproduced
safely.

#### Scenario: Invalid typed event shape is reported
- **GIVEN** a module declares `[Event] public partial Action<string> OnStatus { get; }`
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI018` and explain that an awaitable
  `Func<T, Task>` is required

#### Scenario: Typed event payload is unsupported
- **GIVEN** a valid-shaped typed event has an unsupported payload, a callback,
  or a nullable `JavaScriptValue` at any depth
- **WHEN** the generator analyzes the event
- **THEN** it SHALL report `EXPOJSI019`
- **AND** it SHALL not emit reflection, dynamic conversion, callback `Encode`
  source, or a secondary compiler error

#### Scenario: Typed and legacy event names collide
- **GIVEN** `[Events("status")]` and `[Event] ... OnStatus` occur on one module
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI020` for the derived name `status`
- **AND** it SHALL not silently merge the duplicate declarations

#### Scenario: Derived typed event names collide
- **GIVEN** a module declares `[Event] public partial Func<Task> OnReady { get; }`
  and `[Event] public partial Func<Task> Ready { get; }`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI020` for the duplicate name `ready`

#### Scenario: Legacy prefixed name and typed event coexist
- **GIVEN** `[Events("onStatus")]` and `[Event] ... OnStatus` occur on one module
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report no duplicate-name diagnostic
- **AND** the module SHALL declare both `onStatus` and `status`

### Requirement: Event Observing Hooks Use Module Object Functions

Generated observing hooks SHALL be ordinary JavaScript functions on the module
object. The inherited ModulesCore `EventEmitter` implementation SHALL call
those functions when listener counts transition for an event.

#### Scenario: First listener starts observing
- **GIVEN** an authored event module declares a start-observing hook
- **WHEN** JavaScript adds the first listener for a matching event
- **THEN** ModulesCore's `EventEmitter` methods SHALL call the module object's
  `startObserving(eventName)` function
- **AND** generated glue SHALL invoke the authored C# hook

#### Scenario: Last listener stops observing
- **GIVEN** an authored event module declares a stop-observing hook
- **WHEN** JavaScript removes the last listener for a matching event
- **THEN** ModulesCore's `EventEmitter` methods SHALL call the module object's
  `stopObserving(eventName)` function
- **AND** generated glue SHALL invoke the authored C# hook

#### Scenario: Event-specific hook is declared
- **GIVEN** an authored observing hook names a specific declared event
- **WHEN** listener counts change for another event
- **THEN** generated glue SHALL ignore that transition for the event-specific
  hook

#### Scenario: Event-specific hook names the derived event name
- **GIVEN** a module declares `[Event] public partial Func<string, Task> OnChange { get; }`
- **AND** a hook declares `[OnStopObserving("change")]`
- **WHEN** JavaScript removes the last `change` listener
- **THEN** generated glue SHALL invoke the hook

#### Scenario: Event-specific hook names the old prefixed name
- **GIVEN** a module declares `[Event] public partial Func<string, Task> OnChange { get; }`
  and no legacy `onChange` event
- **AND** a hook declares `[OnStopObserving("onChange")]`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI010` because `onChange` is not a declared
  event

#### Scenario: Observing hook shape is invalid
- **GIVEN** an authored observing hook is static, generic, returns a value, uses
  unsupported parameters, names an undeclared event, or appears on a module
  without any typed or legacy event declarations
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report an observing-hook diagnostic

#### Scenario: Generated module uses reserved observing names
- **GIVEN** an event-capable module exports a `[JS]` function named
  `startObserving` or `stopObserving`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL reject the function name because those names are reserved
  for inherited `EventEmitter` callbacks

### Requirement: Shared Objects Declare Typed Awaitable Events

`[Event]` SHALL be valid on an `[ExpoSharedObject]` class only when it is an
instance, getter-only partial property of exactly `Func<Task>` or
`Func<T, Task>`, and `T` has an event-safe compile-time codec. Implicit names
SHALL follow the module rule in "Typed Event Members Are Awaitable Generated
Properties", which drops an `On` or `on` prefix that an uppercase letter
follows. Explicit non-empty names SHALL be preserved verbatim. The generator
SHALL cache one delegate per managed instance and initialize it during the
first exact registered-class pairing. Reading it before pairing SHALL fail
clearly.

#### Scenario: Shared-object event is generated

- **GIVEN** a shared object declares
  `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** its instance first pairs with its generated JavaScript class
- **THEN** the property SHALL return a cached awaitable delegate
- **AND** awaiting it SHALL dispatch `progress` with the generated payload
  codec
- **AND** a listener that the instance registers for `onProgress` SHALL NOT be
  called

### Requirement: Shared-Object Listeners Are JavaScript-Owned And Instance Scoped

Every event-capable shared-object prototype SHALL expose `addListener`,
`removeListener`, `removeAllListeners`, `emit`, `listenerCount`, and
`removeSubscription` with the existing ModulesCore event-emitter argument,
ordering, matching, and subscription semantics. These names SHALL be reserved
against authored `[JS]` members.

Listener entries SHALL live only in a runtime-private, non-enumerable own
JavaScript Array on the target instance. Managed state SHALL NOT retain a
listener function or ordinary shared-object wrapper outside a runtime callback.
Removal SHALL replace the storage with a compacted Array. Dispatch SHALL use a
snapshot of the starting Array so listeners added or removed during dispatch do
not alter that dispatch's listener set. `addListener` SHALL commit its replacement
Array only after the subscription object, remove host function, callback state,
and return wrapper have been created successfully. A failure before that commit
SHALL leave the listener count unchanged and release any temporary weak state.

#### Scenario: Two instances keep listeners separate

- **GIVEN** two instances of one generated shared-object class have disjoint
  listeners for the same event
- **WHEN** either managed instance dispatches
- **THEN** only that JavaScript instance's listeners SHALL run

#### Scenario: Self-capturing listener is collected

- **GIVEN** a listener captures its own shared-object instance
- **WHEN** JavaScript drops every external reference and collects the cycle
- **THEN** the instance and listener SHALL be collectible together
- **AND** the registry's existing release callback SHALL run

### Requirement: Shared-Object Subscriptions Release Weak State Exactly Once

The subscription returned by `addListener` SHALL own a `remove()` host
function whose callback state owns one target `JavaScriptWeakObject` plus only
scalar/string metadata. That host function SHALL use the owned-state disposer
contract. Explicit repeated `remove()` calls SHALL be no-ops after the first
removal. Collection of a subscription without explicit removal SHALL dispose
the weak handle exactly once, but SHALL NOT itself unregister the listener. The
callback state SHALL NOT retain the runtime context, shared-object registry,
class registration, or prototype graph. A retained JavaScript subscription
SHALL therefore not keep a disposed runtime context alive, and its `remove()`
method SHALL remain safe after context teardown.

#### Scenario: Subscription construction fails

- **GIVEN** `addListener` starts constructing a subscription for a live target
- **WHEN** remove-host-function creation or later subscription setup fails
- **THEN** the target's listener count SHALL remain unchanged
- **AND** any created target weak handle SHALL be disposed exactly once

#### Scenario: Subscription outlives its runtime context

- **GIVEN** JavaScript retains a subscription after its runtime context is
  disposed and all other context roots are dropped
- **WHEN** managed garbage collection runs and JavaScript later calls `remove()`
- **THEN** the disposed runtime context SHALL remain collectible
- **AND** `remove()` SHALL complete safely without consulting that context

#### Scenario: Subscription is explicitly removed

- **GIVEN** one live listener subscription
- **WHEN** JavaScript calls `remove()` more than once
- **THEN** only the first call SHALL remove that listener
- **AND** the weak handle SHALL be disposed exactly once

#### Scenario: Subscription is collected

- **GIVEN** JavaScript drops a subscription without calling `remove()`
- **WHEN** creation failure, JavaScript collection, or teardown releases its
  host-function callback context
- **THEN** the owned-state disposer SHALL release the weak handle exactly once
- **AND** it SHALL not enter JSI or require a runtime access frame

### Requirement: Shared-Object Event Dispatch Is Awaitable And Lifetime Safe

Generated event delegates SHALL always return a non-null task. They SHALL
dispatch inline during current runtime access, through synchronous scheduling
when available, or through the asynchronous runtime task path. Target lookup,
payload encoding, scheduling, release, collection, and teardown failures SHALL
fault or cancel that task. Zero listeners SHALL succeed, and a throwing
listener SHALL neither fault dispatch nor prevent later listeners.

On the runtime thread, dispatch SHALL snapshot and validate the registry entry
under the registry gate, lock its `JavaScriptWeakObject` outside the gate, then
revalidate the same active entry under the gate. Release or teardown wins if
the entry is terminal before that second validation; dispatch SHALL dispose
any locked wrapper and fail. Dispatch wins at successful revalidation and may
finish using only its callback-owned target wrapper while terminal release
proceeds. It SHALL perform no later registry or weak-wrapper access.

#### Scenario: Release wins dispatch acquisition

- **GIVEN** dispatch and terminal release race for one active entry
- **WHEN** release removes the entry before dispatch revalidates it
- **THEN** dispatch SHALL fail loudly without reading listeners
- **AND** it SHALL release any temporary target wrapper

#### Scenario: Dispatch wins acquisition

- **GIVEN** dispatch revalidates the active entry before terminal release
- **WHEN** release begins during listener iteration
- **THEN** dispatch MAY finish from its callback-owned target wrapper
- **AND** release SHALL retain the registry's exactly-once terminal contract

#### Scenario: Context is torn down

- **GIVEN** C# retains an event delegate after its runtime context is disposed
- **WHEN** it invokes that delegate
- **THEN** the returned task SHALL fail or cancel loudly
- **AND** it SHALL not touch a disposed runtime, registry entry, or weak handle

### Requirement: Invalid Shared-Object Events Are Build Diagnostics

Shared-object event validation SHALL use `EXPOJSI026` for invalid placement or
property shapes, `EXPOJSI027` for unsupported event payloads, and `EXPOJSI028`
for duplicate effective names. `[Event]` on a class that is neither an
`[ExpoModule]` nor a valid `[ExpoSharedObject]` SHALL report `EXPOJSI026`.
Rejected reproducible partial properties SHALL receive inert matching
implementations so the Expo diagnostic does not cause a secondary compiler
error. Every declaration in a duplicate effective-name group SHALL be invalid;
source order SHALL NOT select one declaration for dispatch glue.

#### Scenario: Unsupported payload is diagnosed

- **GIVEN** a valid-shaped shared-object event uses a callback payload or a
  nullable `JavaScriptValue` at any depth
- **WHEN** the generator analyzes it
- **THEN** it SHALL report `EXPOJSI027`
- **AND** it SHALL not emit reflection, dynamic conversion, or callback encode
  source

#### Scenario: Derived shared-object event names collide

- **GIVEN** a shared object declares
  `[Event] public partial Func<Task> OnReady { get; }` and
  `[Event] public partial Func<Task> Ready { get; }`
- **WHEN** the generator analyzes it
- **THEN** it SHALL report `EXPOJSI028` on both declarations for the duplicate
  name `ready`
- **AND** neither declaration SHALL receive dispatch glue

### Requirement: Shared-Object TypeScript Facades Type Event Maps

`DotnetSharedObject<TEventsMap>` SHALL extend
`DotnetEventEmitter<TEventsMap>` and default to an empty event map for source
compatibility. Event-capable facades SHALL supply their per-class event map.

#### Scenario: Listener payload is type checked

- **GIVEN** a facade extends
  `DotnetSharedObject<{ onChange(value: number): void }>`
- **WHEN** TypeScript checks `addListener`
- **THEN** `onChange` SHALL require a numeric listener payload
- **AND** undeclared event names SHALL be rejected
