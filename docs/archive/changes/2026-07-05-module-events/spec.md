# Module Events

## Goal

Add production module event support to `Expo.ModulesCore` by making generated
module JavaScript objects participate in Expo-style JavaScript class and
prototype inheritance. Event-capable modules are `NativeModule` instances
that inherit listener behavior from `EventEmitter`; C# authored modules
emit through runtime-scoped services without owning JavaScript listener lists.

This delta also adds the generic JavaScript class/prototype ABI foundation that
future `SharedObject` and `SharedRef` support can reuse.

## Assumptions

- Event-capable generated modules are constructed as `_expoDotnet.NativeModule`
  instances, whose prototype inherits from `_expoDotnet.EventEmitter.prototype`.
- Plain generated modules MAY continue to use plain JavaScript objects unless
  implementation evidence shows that normalizing all generated modules to
  `NativeModule` is simpler and compatible.
- Event listener storage belongs to the ModulesCore-installed
  `EventEmitter` class state, not to C# module instances and not to low-level
  `Expo.JSI` native bridge code.
- C# generated code MAY keep a runtime-scoped association from an authored
  module instance to its JavaScript event target object so authored module code
  can emit events later.
- The ABI MAY grow, but new entries SHOULD expose reusable class, prototype,
  constructor, and native-state primitives instead of event-only operations.
- `SharedObject` and `SharedRef` are not implemented in this delta, but the
  class/prototype foundation must not make their future object hierarchy
  harder to express.

## Scope

### Included

- Generic ABI support for JavaScript class/prototype operations needed by
  `EventEmitter`, `NativeModule`, and later shared objects.
- Managed `Expo.JSI` wrappers over those generic ABI operations.
- A runtime-scoped `JavaScriptObjectFactory` in `Expo.ModulesCore` that ensures
  Expo base classes exist for a JavaScript runtime and creates class-backed
  objects through named constructors.
- `Expo.ModulesCore` module registry support for defining a generated module as
  an `_expoDotnet.NativeModule` instance.
- Event declaration syntax consumed by the generator, such as an `[Events]`
  attribute.
- Generated provider output that chooses native-module-backed JavaScript
  objects for event-capable modules and still attaches generated `[JS]`
  functions as own properties.
- A runtime-scoped module event service that maps C# module instances to their
  JavaScript module object and emits encoded payloads through that object's
  inherited `emit` method.
- Optional generated observing hooks that expose `startObserving` and
  `stopObserving` functions when authored module syntax declares them.
- Hermes-backed tests that verify JavaScript listener behavior, payload
  conversion, runtime scheduling, teardown failure behavior, and generated
  source shape.

### Excluded

- `SharedObject` and `SharedRef` managed APIs.
- View events.
- React Native adapter-specific event buses.
- JavaScript listener management on authored C# module instances.
- Event-only ABI entries that bypass the general class/prototype model.
- Managed native-state attachment APIs for `SharedObject`; listener state used
  by the installed `EventEmitter` class remains encapsulated in
  `Expo.ModulesCore`.
- Runtime hot-path reflection, dynamic invocation, JSON payload conversion, or
  `object?[]` event payload dispatch.

## Accepted Design

`Expo.ModulesCore` SHALL install Expo-style base classes into each JavaScript
runtime through reusable `Expo.JSI` class/prototype machinery:

```text
globalThis._expoDotnet.EventEmitter
globalThis._expoDotnet.NativeModule extends _expoDotnet.EventEmitter
future: globalThis._expoDotnet.SharedObject extends _expoDotnet.EventEmitter
future: globalThis._expoDotnet.SharedRef extends _expoDotnet.SharedObject
```

The ABI SHALL expose only the underlying reusable primitives for class
creation, subclass creation, object creation with a prototype, and constructor
calls. `Expo.JSI` SHALL expose low-level managed wrappers for those ABI
operations. `Expo.ModulesCore` SHALL provide a runtime-scoped
`JavaScriptObjectFactory` that uses those wrappers to create or retrieve Expo
class constructors, construct class-backed objects, and own the listener
storage behind inherited event methods. The low-level native bridge SHALL NOT
know `_expoDotnet`, module objects, observing hooks, or event listener state.
Each runtime context owns the class functions it installs; a later context in
the same JavaScript runtime replaces disposed-context class functions rather
than reusing listener state from a disposed object factory.

`DotnetRuntimeContext` owns this object factory together with `ModuleRegistry`
and the module event service. Runtime-context construction SHALL ensure the
base Expo classes required by generated modules are installed for that context.
Generated providers SHALL NOT install base classes individually.

For modules that declare events, generated provider code SHALL ask
`ModuleRegistry` for a native-module-backed JavaScript object. The resulting
object SHALL have this effective hierarchy:

```text
moduleObject
  own properties:
    generated functions
    generated constants/properties when supported
    __expo_module_name__

  [[Prototype]] -> _expoDotnet.NativeModule.prototype
    [[Prototype]] -> _expoDotnet.EventEmitter.prototype
      addListener()
      removeListener()
      removeAllListeners()
      emit()
      listenerCount()
      removeSubscription()
```

Generated C# shape SHALL remain direct and readable:

```csharp
using var module_Device =
    context.ModuleRegistry.DefineNativeModule(modules, "Device");

var instance_Device =
    context.ModuleRegistry.GetOrCreateModule(
        "Device",
        () => new global::Example.DeviceModule(context)
    );

context.Events.Attach(instance_Device, module_Device, "Device");

GeneratedFunction.DefineSync(
    context,
    module_Device,
    "getValue",
    0,
    Invoke_Device_getValue,
    instance_Device
);
```

Authored module code SHALL emit through the runtime context rather than
touching listener state:

```csharp
await RuntimeContext.Events.EmitAsync(
    this,
    "onChange",
    new ChangePayload(value)
);
```

The event service SHALL look up the JavaScript module object associated with
the C# module instance, schedule onto the JavaScript runtime path, encode the
payload through generated `IJavaScriptCodec<T>` support, and call the target
object's inherited `emit` function with the module object as `this`.

Observing hooks SHALL integrate with the same inherited ModulesCore event
emitter semantics. When a module declares start or stop observing hooks,
generated registration SHALL define `startObserving(eventName)` and
`stopObserving(eventName)` functions on the module object. The inherited
`EventEmitter` methods call those functions when listener counts transition for
an event.

## Delta Requirements

### ADDED Requirement: ABI Supports JavaScript Class And Prototype Primitives

The ABI SHALL expose reusable JavaScript class, subclass, object-with-prototype,
and constructor-call primitives through opaque handles without exposing raw JSI
layouts to managed code.

#### Scenario: Class is created
- **GIVEN** managed runtime initialization needs a JavaScript constructor
  function for a named class
- **WHEN** it calls the class creation ABI
- **THEN** native SHALL create a JavaScript constructor function for that class
- **AND** native SHALL install native constructor behavior without exposing
  `facebook::jsi::Function` or `facebook::jsi::Object` layouts to C#

#### Scenario: Subclass is created
- **GIVEN** managed runtime initialization has a base JavaScript constructor
  function
- **WHEN** it creates a subclass constructor
- **THEN** native SHALL set the subclass prototype chain to inherit from the
  base class prototype
- **AND** instances constructed from the subclass SHALL observe inherited
  prototype methods

#### Scenario: Object is created with prototype
- **GIVEN** managed code has a JavaScript prototype object
- **WHEN** it creates an object with that prototype
- **THEN** native SHALL return an owned JavaScript object handle whose
  `[[Prototype]]` is the supplied prototype

#### Scenario: EventEmitter listener state remains encapsulated
- **GIVEN** JavaScript uses the inherited `EventEmitter` listener API on a
  generated module object
- **WHEN** listener state is created or released
- **THEN** ModulesCore SHALL manage that state inside the installed event
  emitter implementation
- **AND** low-level `Expo.JSI` SHALL NOT receive event-specific listener
  contracts or expose raw JSI layouts

### ADDED Requirement: Runtime Context Installs Expo Base Classes

`DotnetRuntimeContext` SHALL ensure the base JavaScript classes required by
generated module objects exist for its JavaScript runtime.

#### Scenario: Runtime context is created
- **GIVEN** managed code creates a `DotnetRuntimeContext`
- **WHEN** the context initializes runtime-scoped services
- **THEN** it SHALL ensure `globalThis._expoDotnet.EventEmitter` exists
- **AND** it SHALL ensure `globalThis._expoDotnet.NativeModule` exists
- **AND** `_expoDotnet.NativeModule.prototype` SHALL inherit from
  `_expoDotnet.EventEmitter.prototype`

#### Scenario: Base classes are already installed
- **GIVEN** the host or an earlier context initialization already installed
  compatible Expo base classes in the runtime
- **WHEN** `DotnetRuntimeContext` initializes
- **THEN** initialization SHALL be idempotent
- **AND** it SHALL NOT replace compatible existing constructors or prototypes

#### Scenario: Base class installation fails
- **GIVEN** native cannot install or verify required base classes
- **WHEN** `DotnetRuntimeContext` initializes
- **THEN** initialization SHALL fail loudly before generated modules are
  registered

### ADDED Requirement: Modules Can Be NativeModule Instances

`ModuleRegistry` SHALL support defining generated module JavaScript objects as
`_expoDotnet.NativeModule` instances.

#### Scenario: Event-capable module is defined
- **GIVEN** generated provider code registers a module that declares events
- **WHEN** it asks the registry to define that module
- **THEN** the registry SHALL create the JavaScript module object through the
  runtime object factory as an `_expoDotnet.NativeModule` instance
- **AND** it SHALL install the object under the supplied modules object
- **AND** generated functions SHALL be attached as own properties of that
  module object

#### Scenario: Existing module object is reused
- **GIVEN** the supplied modules object already contains an object for a module
  name
- **WHEN** generated registration defines that module
- **THEN** the registry SHALL return the existing object
- **AND** it SHALL preserve existing compatible properties

#### Scenario: Module object exposes inherited listener API
- **GIVEN** generated registration defines an event-capable module
- **WHEN** JavaScript reads the module object
- **THEN** `addListener`, `removeListener`, `removeAllListeners`, `emit`, and
  `listenerCount` SHALL be available through the prototype chain
- **AND** listener storage SHALL be associated with the JavaScript module
  object rather than the C# module instance

### ADDED Requirement: Event Syntax Is Generated And Non-Inert

`Expo.ModulesCore` SHALL expose event declaration syntax only when the Roslyn
generator consumes it and emits event-capable module registration.

#### Scenario: Module declares events
- **GIVEN** an authored module uses the supported event declaration syntax
- **WHEN** the generator builds the module model
- **THEN** it SHALL record the declared event names
- **AND** generated registration SHALL define the JavaScript module object as a
  native-module-backed object

#### Scenario: Event names are invalid
- **GIVEN** an authored module declares an empty, whitespace-only, duplicate,
  or otherwise unsupported event name
- **WHEN** the generator builds the module model
- **THEN** it SHALL report a generator diagnostic
- **AND** it SHALL suppress invalid event registration for that module

#### Scenario: Module does not declare events
- **GIVEN** an authored module declares no events
- **WHEN** the generator emits registration
- **THEN** it MAY continue using the existing plain-object module definition
  path
- **AND** it SHALL NOT expose inert event metadata

### ADDED Requirement: C# Emits Through Module Event Service

`DotnetRuntimeContext` SHALL expose a runtime-scoped event service that lets C#
module logic emit to its associated JavaScript module object.

#### Scenario: Module emits event payload
- **GIVEN** a generated provider registered an event-capable module
- **AND** C# module code emits a declared event with payload type `T`
- **WHEN** the event service handles the emission
- **THEN** it SHALL schedule event dispatch onto the JavaScript runtime path
- **AND** it SHALL encode the payload through generated
  `IJavaScriptCodec<T>` support
- **AND** it SHALL call the JavaScript module object's inherited `emit`
  function with the module object as `this`

#### Scenario: Module emits event without payload
- **GIVEN** C# module code emits a declared event without a payload
- **WHEN** the event service dispatches the event
- **THEN** it SHALL call the JavaScript module object's inherited `emit`
  function with only the event name

#### Scenario: Module emits undeclared event
- **GIVEN** C# module code emits an event name not declared by that module
- **WHEN** the event service validates the emission
- **THEN** it SHALL fail loudly
- **AND** it SHALL NOT dispatch to JavaScript listeners

#### Scenario: Module emits after runtime teardown
- **GIVEN** the owning `DotnetRuntimeContext` has been disposed
- **WHEN** C# module code attempts to emit an event
- **THEN** the event service SHALL fail loudly
- **AND** it SHALL NOT touch stale JavaScript runtime state

#### Scenario: Module emits before event target is attached
- **GIVEN** a C# module instance is not associated with a JavaScript module
  object
- **WHEN** it attempts to emit an event
- **THEN** the event service SHALL fail loudly
- **AND** it SHALL NOT create an implicit JavaScript target

### ADDED Requirement: JavaScript Listener Semantics Follow EventEmitter

Event-capable generated modules SHALL use the inherited `EventEmitter`
listener behavior instead of a C# listener registry.

#### Scenario: JavaScript listener receives event
- **GIVEN** JavaScript adds a listener to a generated event-capable module
- **WHEN** C# module code emits the matching event
- **THEN** the listener SHALL be called with the encoded payload

#### Scenario: Listener is removed
- **GIVEN** JavaScript adds and then removes a listener from a generated module
- **WHEN** C# emits that event
- **THEN** the removed listener SHALL NOT be called

#### Scenario: Multiple listeners are registered
- **GIVEN** JavaScript registers multiple listeners for one event
- **WHEN** C# emits that event
- **THEN** each registered listener SHALL be called according to the inherited
  `EventEmitter` semantics

#### Scenario: Listener throws
- **GIVEN** a JavaScript listener throws while handling an emitted event
- **WHEN** dispatch continues
- **THEN** listener exception behavior SHALL match the installed
  `EventEmitter` implementation
- **AND** C# SHALL NOT own listener exception routing

### ADDED Requirement: Event Observing Hooks Use Module Object Functions

Generated observing hooks SHALL be ordinary JavaScript functions on the module
object that the inherited `EventEmitter` calls when listener counts change.

#### Scenario: First listener starts observing
- **GIVEN** an authored module declares a start-observing hook
- **WHEN** JavaScript adds the first listener for a matching event
- **THEN** the inherited event emitter SHALL call the module object's
  `startObserving(eventName)` function
- **AND** generated glue SHALL invoke the authored C# hook

#### Scenario: Last listener stops observing
- **GIVEN** an authored module declares a stop-observing hook
- **WHEN** JavaScript removes the last listener for a matching event
- **THEN** the inherited event emitter SHALL call the module object's
  `stopObserving(eventName)` function
- **AND** generated glue SHALL invoke the authored C# hook

#### Scenario: No observing hooks are declared
- **GIVEN** an event-capable module declares no observing hooks
- **WHEN** generated registration installs the module object
- **THEN** it SHALL NOT define unused observing hook functions

## Verification Requirements

- Generator tests SHALL cover event syntax, invalid event diagnostics,
  native-module-backed registration, and observing hook source shape.
- Hermes-backed `Expo.ModulesCore.Tests` SHALL cover prototype inheritance,
  listener add/remove behavior, payload delivery, undeclared-event failure,
  runtime teardown failure, and observing hooks.
- ABI and wrapper tests SHALL cover class creation, subclass prototype
  inheritance, constructor calls, and object-with-prototype behavior without
  exposing raw JSI layouts to C#.
- Final implementation verification SHALL run `scripts/test-managed.sh`,
  `scripts/format.sh --check --all`, and `git diff --check`.
