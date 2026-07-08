# Modules Core Boundary

## Purpose

Define the boundary between low-level `Expo.JSI` wrappers and the
`Expo.ModulesCore` generated-binding helper package.

## Requirements

### Requirement: ModulesCore Owns Generated-Binding Helpers

`Expo.ModulesCore` SHALL own module registration helpers, generated dispatch
helpers, typed conversion helpers, and runtime-scoped authored module instance
helpers above `Expo.JSI`. It lives under
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore` as part of the
public Expo adapter package's managed core.

#### Scenario: Generated-looking provider registers a module
- **GIVEN** generated-looking provider code has a `JavaScriptRuntime` and a
  JavaScript modules object
- **WHEN** it installs a module under the supplied modules object
- **THEN** it SHALL use `Expo.ModulesCore` helpers instead of placing
  module-layer abstractions in `Expo.JSI`
- **AND** it SHALL NOT hardcode `globalThis.expo.modules`

#### Scenario: Managed test code uses default dotnet namespace
- **GIVEN** managed test or generated-looking provider code needs a default
  modules object
- **WHEN** it asks `Expo.ModulesCore` for the default dotnet modules object
- **THEN** the helper SHALL create or return `globalThis._expoDotnet.modules`
- **AND** it SHALL NOT create or mutate `globalThis.expo`

### Requirement: ModulesCore Avoids Inert Authored Syntax

`Expo.ModulesCore` SHALL expose authored module attributes only when those
attributes are consumed by the Roslyn generator.

#### Scenario: Authored syntax is proposed
- **GIVEN** references describe future `[ExpoModule]`, `[JS]`, `[Record]`, or
  `[Event]` syntax
- **WHEN** no Roslyn generator consumes a syntax surface
- **THEN** the package SHALL keep that unconsumed syntax out of production API

#### Scenario: Attribute-backed module is compiled
- **GIVEN** a C# project references `Expo.ModulesCore` and has the generator
  configured
- **WHEN** it declares a class with `[ExpoModule]` and a sync method with
  `[JS]`
- **THEN** the generator SHALL emit direct-call registration glue for that
  module

#### Scenario: Void sync function returns undefined
- **GIVEN** a generated sync function wraps an authored method returning `void`
- **WHEN** JavaScript calls the generated function
- **THEN** generated dispatch SHALL call the authored method and return
  JavaScript `undefined`

#### Scenario: Nullable value types preserve nullish values
- **GIVEN** a generated sync function accepts or returns a nullable value type
- **WHEN** JavaScript passes `null` or explicit `undefined` to a required
  nullable argument
- **THEN** generated dispatch SHALL pass C# `null`
- **AND** a C# nullable return value of `null` SHALL become JavaScript `null`

#### Scenario: Optional nullable arguments use defaults for omission
- **GIVEN** a generated sync function has a nullable value-type parameter with a
  C# default value
- **WHEN** JavaScript omits the argument or passes explicit `undefined`
- **THEN** generated dispatch SHALL pass the C# default value
- **AND** explicit JavaScript `null` SHALL still pass C# `null`

#### Scenario: Additional numeric primitives use generic number codecs
- **GIVEN** a generated sync function accepts or returns a supported CLR
  numeric primitive such as signed integer, unsigned integer, single, or double
- **WHEN** JavaScript calls the generated function with a JavaScript number
- **THEN** generated dispatch SHALL decode through a compile-time generic
  number codec
- **AND** encoding SHALL return a JavaScript number
- **AND** nullable numeric primitives SHALL compose through the nullable codec
  over the generated numeric codec
- **AND** fractional JavaScript numbers SHALL be accepted by integer parameters
  according to the managed numeric conversion semantics

#### Scenario: String-backed convertible primitives parse and format strings
- **GIVEN** a generated sync function accepts or returns `Guid`, `Uri`,
  `DateTimeOffset`, or `TimeSpan`
- **WHEN** JavaScript calls the generated function with a string value
- **THEN** generated dispatch SHALL decode through a compile-time codec for that
  CLR type
- **AND** return values SHALL encode back to JavaScript strings

#### Scenario: Invalid convertible input fails through the host function boundary
- **GIVEN** a generated sync function expects a string-backed convertible type
- **WHEN** JavaScript passes a string that the codec cannot parse for that type
- **THEN** the codec SHALL throw a managed conversion exception
- **AND** the host-function boundary SHALL expose it to JavaScript as a
  catchable `Error`

### Requirement: Generated Providers Are Library-Local

The Roslyn generator SHALL emit one deterministic provider for modules in the
current compilation.

#### Scenario: Package-local provider is generated
- **GIVEN** a library project declares module classes
- **WHEN** the project is compiled
- **THEN** generated code SHALL register only modules declared in that library
  project
- **AND** generated code SHALL expose a stable provider that future app-level
  autolinking can call

#### Scenario: Generated provider shape is stable
- **GIVEN** a library project declares at least one `[ExpoModule]`
- **WHEN** the project is compiled
- **THEN** generated code SHALL include a deterministic provider name derived
  from the current compilation
- **AND** the provider SHALL expose `Register(DotnetRuntimeContext context)`
- **AND** the provider SHALL expose
  `Register(DotnetRuntimeContext context, JavaScriptObject modules)`
- **AND** the default overload SHALL register lazy module definitions with the
  context-owned `ModuleRegistry`
- **AND** the explicit overload SHALL eagerly install under the supplied
  modules object as a compatibility path
- **AND** generated registration SHALL use `DotnetRuntimeContext` module
  instances
- **AND** generated registration SHALL NOT require runtime reflection

#### Scenario: Context-backed module is constructed
- **GIVEN** an authored module declares a public or internal constructor
  accepting `DotnetRuntimeContext`
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL pass the current context to the constructor
- **AND** it SHALL use the resulting instance for generated function bindings

#### Scenario: Simple module is constructed
- **GIVEN** an authored module declares a public or internal parameterless
  constructor
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL construct the module without requiring context access

#### Scenario: Module supports both constructors
- **GIVEN** an authored module declares both a supported parameterless
  constructor and a supported constructor accepting `DotnetRuntimeContext`
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL prefer the `DotnetRuntimeContext` constructor

### Requirement: Runtime Context Owns Module Instances

`DotnetRuntimeContext` SHALL own runtime-scoped authored module instances
through a context-owned `ModuleRegistry`.

#### Scenario: Module instance is reused within one runtime context
- **GIVEN** generated registration asks the context-owned registry for an
  authored module instance
- **WHEN** that instance already exists for the current `DotnetRuntimeContext`
- **THEN** the registry SHALL return the existing instance

#### Scenario: Module instance does not cross runtime contexts
- **GIVEN** two JavaScript runtimes have separate `DotnetRuntimeContext`
  instances
- **WHEN** generated registration asks each context for the same authored module
  type
- **THEN** each context SHALL receive its own authored module instance

#### Scenario: Module create hook runs once
- **GIVEN** an authored module declares a valid `[OnCreate]` method
- **WHEN** generated registration creates the module instance for a runtime
  context
- **THEN** generated registration SHALL call the hook directly after the module
  is stored in the context-owned registry
- **AND** later registration in the same runtime context SHALL NOT call the hook
  again
- **AND** generated registration SHALL NOT expose `onCreate` as a JavaScript
  module property

#### Scenario: Module destroy hook runs during teardown
- **GIVEN** an authored module declares a valid `[OnDestroy]` method
- **WHEN** the owning `DotnetRuntimeContext` is disposed
- **THEN** the context-owned registry SHALL call the hook once before
  `IDisposable.Dispose`
- **AND** all module destroy and dispose callbacks SHALL run even if one fails
- **AND** cleanup failures SHALL be reported as one `AggregateException` after
  cleanup finishes
- **AND** generated registration SHALL NOT expose `onDestroy` as a JavaScript
  module property

#### Scenario: Lifecycle hook shape is invalid
- **GIVEN** an authored lifecycle hook is static, generic, private, returns a
  value, has parameters, or duplicates another hook of the same kind
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI011`

#### Scenario: Module inherits the convenience base class
- **GIVEN** an authored module inherits from `Expo.ModulesCore.Module`
- **WHEN** generated registration constructs the module with
  `DotnetRuntimeContext`
- **THEN** the base class SHALL store the context for derived classes

#### Scenario: Module does not inherit the convenience base class
- **GIVEN** an authored module does not inherit from `Expo.ModulesCore.Module`
- **AND** it declares a supported constructor
- **WHEN** generated registration constructs the module
- **THEN** generated registration SHALL NOT require inheritance

### Requirement: Sync Function Generation Uses Direct Calls

Generated sync function glue SHALL decode arguments, call authored methods
directly, and encode return values through typed helpers.

#### Scenario: Generated sync module function is called from JavaScript
- **GIVEN** a generated provider registered a module under a caller-supplied
  modules object
- **WHEN** JavaScript calls a generated sync function with supported arguments
- **THEN** the generated host function SHALL decode arguments through typed
  codecs
- **AND** call the authored method directly
- **AND** return the encoded result through `Expo.JSI`

#### Scenario: Generated sync function accepts JavaScriptValue
- **GIVEN** a generated sync function accepts `JavaScriptValue`
- **WHEN** JavaScript calls that function
- **THEN** generated dispatch SHALL retain the scoped argument into an owned
  wrapper for the invocation
- **AND** generated dispatch SHALL dispose the argument wrapper after the
  authored method returns or throws
- **AND** authored module code SHALL NOT dispose or store the argument wrapper

#### Scenario: Generated sync function returns JavaScriptValue
- **GIVEN** a generated sync function returns `JavaScriptValue`
- **WHEN** JavaScript calls that function
- **THEN** ownership of the returned wrapper SHALL transfer to generated glue
- **AND** generated glue SHALL encode the returned wrapper through
  `JavaScriptValueCodec`
- **AND** generated glue SHALL return the encoded wrapper to the host-function
  bridge without retaining or disposing it again

#### Scenario: Module author returns a retained JavaScriptValue copy
- **GIVEN** authored module code owns a `JavaScriptValue`
- **WHEN** it needs to keep that original wrapper or dispose it locally
- **THEN** it SHALL return an explicit retained copy
- **AND** ownership of the retained copy SHALL transfer to generated glue
- **AND** ownership of the original wrapper SHALL remain with the module author

#### Scenario: Enum values use generated codecs
- **GIVEN** a generated sync function accepts or returns a C# enum
- **WHEN** no explicit enum representation is requested
- **THEN** generated dispatch SHALL decode and encode the enum as JavaScript
  strings
- **AND** integer-backed enum conversion SHALL be available through explicit
  authored metadata

#### Scenario: Simple records use generated codecs
- **GIVEN** a generated sync function accepts or returns a positional C#
  `record`, `record class`, or `record struct`
- **WHEN** JavaScript passes or receives a plain object
- **THEN** generated dispatch SHALL convert known fields through generated
  field codecs
- **AND** construct records through direct constructor calls
- **AND** simple nested records SHALL compose through generated field codecs

#### Scenario: JavaScript callbacks use generated codecs
- **GIVEN** a generated sync or async function accepts
  `JavaScriptCallback<TResult>` or `JavaScriptCallback<TArgs, TResult>`
- **WHEN** JavaScript passes a function value
- **THEN** generated dispatch SHALL decode it as a retained callback owned by
  the current `DotnetRuntimeContext`
- **AND** `JavaScriptCallback<TResult>` SHALL represent zero callback arguments
- **AND** `JavaScriptCallback<TArgs, TResult>` SHALL use `ValueTuple` argument
  codecs for one through eight callback arguments
- **AND** callback argument and result values SHALL use generated
  `Expo.ModulesCore` codecs without runtime reflection or dynamic invocation

#### Scenario: Retained callback is invoked from C#
- **GIVEN** managed module code holds a retained `JavaScriptCallback`
- **WHEN** it calls `Invoke` while already executing on the owning JavaScript
  runtime
- **THEN** the callback SHALL invoke the retained JavaScript function
  synchronously and decode the JavaScript result through the configured result
  codec
- **AND** this current-runtime invocation SHALL NOT require generic synchronous
  runtime execution support from the host scheduler
- **AND** `InvokeAsync` SHALL schedule invocation through the owning runtime for
  later event-style use
- **AND** callback invocation after runtime-context teardown SHALL fail loudly
  instead of touching released native state

#### Scenario: String-key dictionaries use JavaScript objects
- **GIVEN** a generated sync function accepts or returns
  `Dictionary<string, T>` or `IReadOnlyDictionary<string, T>`
- **WHEN** `T` has a generated codec
- **THEN** generated dispatch SHALL map the dictionary to a plain JavaScript
  object using own property names

#### Scenario: Generated provider augments an existing native module object
- **GIVEN** a real Expo runtime has already installed a native module object
  under `globalThis.expo.modules`
- **WHEN** a generated provider registers a C# module with the same module name
- **THEN** `Expo.ModulesCore` SHALL reuse the existing JavaScript object instead
  of replacing the `expo.modules` property
- **AND** generated `[JS]` functions SHALL be defined on that existing object

### Requirement: One-Stage Lazy Dotnet Module Registry

`Expo.ModulesCore` SHALL install `globalThis._expoDotnet.modules` as a
HostObject backed by build-time generated module metadata. The registry is
one-stage lazy: reading a registered root module property creates and caches the
real JavaScript module object immediately. Two-stage lazy shells are a future
optimization only if profiling shows the extra complexity is needed.

#### Scenario: Generated default registration is lazy
- **GIVEN** a generated provider has `Register(DotnetRuntimeContext context)`
- **WHEN** the default overload runs
- **THEN** it SHALL register module names and creation callbacks with
  `ModuleRegistry`
- **AND** it SHALL NOT create JavaScript module objects or authored module
  instances until JavaScript reads a registered module property

#### Scenario: Registered module is read
- **GIVEN** `_expoDotnet.modules` is installed for a runtime context
- **AND** the generated module table contains `Camera`
- **WHEN** JavaScript reads `_expoDotnet.modules.Camera`
- **THEN** `Expo.ModulesCore` SHALL create and cache the real `Camera` module
  object
- **AND** it SHALL create or reuse the authored `Camera` module instance through
  the context-owned `ModuleRegistry`
- **AND** later reads of `_expoDotnet.modules.Camera` in the same runtime
  context SHALL return the cached module object

#### Scenario: Unknown module is read
- **GIVEN** `_expoDotnet.modules` is installed for a runtime context
- **AND** the generated module table does not contain `Camera`
- **WHEN** JavaScript reads `_expoDotnet.modules.Camera`
- **THEN** the HostObject SHALL return JavaScript `undefined`
- **AND** it SHALL NOT create a module object or module instance

#### Scenario: Probe property is read
- **GIVEN** `_expoDotnet.modules` is installed for a runtime context
- **WHEN** JavaScript reads a no-initialization probe property such as
  `$$typeof`
- **THEN** the HostObject SHALL return JavaScript `undefined`
- **AND** it SHALL NOT create a module object or module instance

#### Scenario: Module names are enumerated
- **GIVEN** `_expoDotnet.modules` is installed for a runtime context
- **WHEN** JavaScript enumerates own property names
- **THEN** the HostObject SHALL return registered lazy module names
- **AND** it SHALL include explicit modules already present in the backing
  modules object
- **AND** enumeration SHALL NOT create lazy module objects or authored module
  instances

#### Scenario: Explicit module registration mixes with lazy registration
- **GIVEN** `_expoDotnet.modules` is installed as a lazy HostObject
- **WHEN** existing explicit registration writes a module into
  `ModuleRegistry.GetOrCreateDotnetModulesObject()`
- **THEN** JavaScript reads through `_expoDotnet.modules.<name>` SHALL see the
  explicitly registered module
- **AND** explicit modules registered before lazy setup SHALL remain visible
  after lazy setup

#### Scenario: Root module registry is mutated from JavaScript
- **GIVEN** `_expoDotnet.modules` is installed as a HostObject
- **WHEN** JavaScript assigns a property on `_expoDotnet.modules`
- **THEN** assignment SHALL fail with a catchable JavaScript error

#### Scenario: Cached registry is used after teardown
- **GIVEN** JavaScript still holds a reference to `_expoDotnet.modules`
- **AND** the owning `DotnetRuntimeContext` has been disposed
- **WHEN** JavaScript reads, writes, or enumerates the HostObject
- **THEN** the operation SHALL fail with a catchable JavaScript error
- **AND** it SHALL NOT crash or touch an invalid runtime handle

### Requirement: Required Dotnet Module Lookup Error

Raw HostObject property reads SHALL preserve ordinary JavaScript object
semantics for unknown properties by returning `undefined`. Required module
lookups SHALL be handled above the HostObject layer by JavaScript helpers or
generated facades.

#### Scenario: Required module is missing
- **GIVEN** a JavaScript facade requires the `Camera` module
- **AND** `_expoDotnet.modules.Camera` is `undefined`
- **WHEN** the facade resolves the required module
- **THEN** it SHALL throw a JavaScript `Error`
- **AND** the error message SHALL say that `Camera` is not registered
- **AND** the error message SHALL tell the user to check autolinking

### Requirement: Event Modules Use NativeModule Objects

`Expo.ModulesCore` SHALL expose event declaration syntax through generated
bindings. Modules that declare events SHALL be registered as
`_expoDotnet.NativeModule` instances so listener behavior comes from the
JavaScript prototype chain. `JavaScriptObjectFactory` SHALL install
`globalThis._expoDotnet.EventEmitter` and
`globalThis._expoDotnet.NativeModule` through generic `Expo.JSI` class and
prototype primitives, and SHALL own the managed listener storage behind the
inherited event methods. The ModulesCore-owned class hierarchy SHALL live under
`globalThis._expoDotnet` to avoid conflicts with upstream Expo classes under
`globalThis.expo`.

#### Scenario: Event module is registered
- **GIVEN** an authored module declares `[Events]` with one or more event names
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

#### Scenario: Observing hook shape is invalid
- **GIVEN** an authored observing hook is static, generic, returns a value, uses
  unsupported parameters, names an undeclared event, or appears on a module
  without `[Events]`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report an observing-hook diagnostic

#### Scenario: Generated module uses reserved observing names
- **GIVEN** an event-capable module exports a `[JS]` function named
  `startObserving` or `stopObserving`
- **WHEN** the generator analyzes the module
- **THEN** it SHALL reject the function name because those names are reserved
  for inherited `EventEmitter` callbacks

### Requirement: Async Function Generation Returns Promises

Generated async function glue SHALL expose authored `[JS]` methods returning
`Task` or `Task<T>` as JavaScript functions that return Promises.
`Task<T>` result types SHALL use the same generated return-codec support as
synchronous return values.

#### Scenario: Task async function resolves undefined
- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task`
- **WHEN** JavaScript calls the generated function and the task completes
  successfully
- **THEN** the function SHALL return a JavaScript Promise
- **AND** the Promise SHALL resolve with JavaScript `undefined`

#### Scenario: Task of T async function resolves encoded value
- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task<T>`
- **AND** `T` has a supported generated return codec
- **WHEN** JavaScript calls the generated function and the task completes with a
  result
- **THEN** the function SHALL return a JavaScript Promise
- **AND** the Promise SHALL resolve with the result encoded through the
  generated codec for `T`

#### Scenario: Task of JavaScriptValue resolves encoded value
- **GIVEN** a generated provider registers a module with an authored `[JS]`
  method returning `Task<JavaScriptValue>`
- **WHEN** the task completes with a JavaScript value wrapper
- **THEN** ownership of the returned wrapper SHALL transfer to generated glue
- **AND** generated glue SHALL keep the returned wrapper alive until the Promise
  settlement value is created
- **AND** generated glue SHALL pass the encoded wrapper to the Promise
  scheduler without retaining or disposing it again

#### Scenario: Unsupported Task of T result type is reported
- **GIVEN** an authored `[JS]` method returns `Task<T>`
- **AND** `T` does not have a supported generated return codec
- **WHEN** the generator analyzes the method
- **THEN** it SHALL report the same unsupported-return diagnostic shape used for
  unsupported synchronous return types

### Requirement: Async Function Arguments Are Captured Before Await

Generated async function glue SHALL decode JavaScript arguments before the
host-function callback returns and SHALL NOT capture scoped JavaScript argument
or `this` refs across asynchronous continuations.

#### Scenario: Async function receives supported arguments
- **GIVEN** a generated async function has supported authored parameters
- **WHEN** JavaScript calls the generated function
- **THEN** generated dispatch SHALL validate the argument count during the
  host-function callback
- **AND** decode each argument through the generated parameter codec during the
  host-function callback
- **AND** pass only decoded managed values into the authored async method

### Requirement: Async Function Failures Reject Promises

Generated async function glue SHALL reject the returned Promise for generated
dispatch failures, authored-method failures, faulted tasks, and canceled tasks.

#### Scenario: Argument validation fails
- **GIVEN** JavaScript calls a generated async function with an unsupported
  argument count or value
- **WHEN** generated dispatch validates or decodes the arguments
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`
- **AND** the validation or codec failure SHALL NOT escape as a synchronous
  JavaScript throw

#### Scenario: Authored async method throws before returning a task
- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored method throws before returning its task
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`

#### Scenario: Authored async task fails
- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored task faults or is canceled
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error`

#### Scenario: Sync function does not use async promise dispatch
- **GIVEN** a generated provider registers a module with a non-`Task` `[JS]`
  method
- **WHEN** JavaScript calls the generated function
- **THEN** the function SHALL keep the existing synchronous direct-call behavior
- **AND** generated dispatch SHALL NOT wrap the result in a JavaScript Promise

### Requirement: Unsupported Signatures Are Build Diagnostics

Unsupported generated function signatures SHALL fail at build time with
actionable diagnostics. Unsupported shapes SHALL fail the consuming compilation
instead of silently skipping affected modules or emitting invalid generated C#.

#### Scenario: Unsupported parameter type is used
- **GIVEN** a `[JS]` method has an unsupported parameter type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported type
- **AND** generated runtime glue SHALL NOT attempt dynamic invocation

#### Scenario: Unsupported callback codec type is used
- **GIVEN** a `[JS]` method has a `JavaScriptCallback` parameter whose argument
  or result type lacks an `Expo.ModulesCore` codec
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a callback-specific diagnostic naming the
  unsupported callback type
- **AND** generated runtime glue SHALL NOT emit a dynamic callback fallback

#### Scenario: Unsupported return type is used
- **GIVEN** a `[JS]` method has an unsupported return type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported type
- **AND** generated runtime glue SHALL NOT attempt dynamic invocation

#### Scenario: Unsupported module constructor is used
- **GIVEN** a module cannot be constructed by a public or internal
  parameterless constructor
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the exported module
- **AND** generated code SHALL NOT rely on runtime reflection to create the
  module

#### Scenario: Unsupported method shape is used
- **GIVEN** a `[JS]` method is static or generic
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the method and
  unsupported shape
- **AND** generated source SHALL NOT emit a fallback dispatch path

#### Scenario: Duplicate exported names are used
- **GIVEN** two generated modules have the same exported module name, or two
  generated functions in one module have the same exported JavaScript name
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the duplicate export
- **AND** duplicate names SHALL NOT be resolved by source order

### Requirement: App Aggregation Is Owned By Dotnet Autolinking

The generator SHALL keep module discovery library-local. The dotnet
autolinking CLI SHALL own app-level aggregation by generating one stable
`ExpoDotnetHost` project that calls each library-local generated provider.
Manual app-owned HostFXR staging is superseded by CLI staging for platforms
migrated to the tool. Mobile NativeAOT apps SHALL use the autolinking CLI for
aggregation and staging instead of legacy per-module adapter-owned staging.

#### Scenario: Multiple libraries are linked into an app
- **GIVEN** dotnet autolinking resolves several dotnet Expo libraries
- **WHEN** an app-level provider is generated
- **THEN** it SHALL call each library-local generated provider
- **AND** module class discovery SHALL remain owned by each library's Roslyn
  generation step

#### Scenario: Public adapter looks up a staged module
- **GIVEN** an app depends on `expo-modules-dotnet`
- **WHEN** JavaScript calls `requireDotnetModule<T>(name)`
- **THEN** the adapter SHALL first touch its TurboModule installer
- **AND** it SHALL return `globalThis._expoDotnet.modules[name]` when present
- **AND** it SHALL throw a plain JavaScript `Error` when the module is missing

#### Scenario: Desktop app stages HostFXR artifacts through the CLI
- **GIVEN** the React Native macOS or Windows example app uses the `hostfxr`
  loader
- **WHEN** the dotnet autolinking CLI stages the generated aggregator
- **THEN** it SHALL stage the managed assembly, runtime config, dependency
  file, managed bridge assemblies, and platform `nethost` runtime library into
  the app-owned `Managed` location
- **AND** manual app-local HostFXR staging scripts SHALL NOT be required

#### Scenario: Mobile app stages NativeAOT artifacts through the CLI
- **GIVEN** the React Native iOS or Android example app uses the `nativeaot`
  loader
- **WHEN** the dotnet autolinking CLI stages the generated aggregator
- **THEN** it SHALL stage the single `ExpoDotnetHost` native library into the
  app-owned mobile loader location
- **AND** legacy per-module NativeAOT staging SHALL NOT be required

### Requirement: Generated Bindings Avoid Hot-Path Reflection

Generated v2 runtime bindings SHALL avoid runtime hot-path reflection and
dynamic invocation.

#### Scenario: Module provider invokes a method
- **GIVEN** generated provider code handles a JavaScript call
- **WHEN** it invokes the authored module method
- **THEN** it SHALL NOT use `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]` as the normal argument container, or
  JSON serialization for ordinary JSI values

### Requirement: Generator Authoring Documentation Is Durable

The repo SHALL document how library authors configure generation today and how
dotnet autolinking aggregates generated providers.

#### Scenario: Developer wants manual repo-local generator wiring
- **GIVEN** a test or development project cannot consume packaged analyzer
  assets yet
- **WHEN** it needs generator output
- **THEN** documentation SHALL show the manual analyzer `ProjectReference`
  configuration

#### Scenario: Dotnet package config is documented
- **GIVEN** dotnet autolinking resolves module packages
- **WHEN** package discovery is documented
- **THEN** documentation SHALL include the parsed dotnet
  `expo-module.config.json` shape
- **AND** state that module class discovery remains owned by each library's
  Roslyn generation step

### Requirement: ModulesCore Owns Module Tests

`Expo.ModulesCore.Tests` SHALL own module dispatch and conversion behavior.

#### Scenario: Module conversion behavior is tested
- **GIVEN** a test proves generated-looking module conversion behavior
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** the test SHALL live in `Expo.ModulesCore.Tests`

### Requirement: Runtime-Scoped Dotnet Runtime Contexts

Generated module registration SHALL create or receive a runtime-scoped
`DotnetRuntimeContext` that owns module instances and generated host-function
registrations for one JavaScript runtime.

Static registration helpers MAY remain as compatibility wrappers, but the
production owner is the runtime context. Managed teardown SHALL be deterministic
and idempotent; it SHALL NOT depend on finalizers or ordinary GC timing.

#### Scenario: Provider registers through a runtime context
- **GIVEN** a host adapter creates a managed runtime context for a JavaScript
  runtime
- **WHEN** a generated provider registers module functions
- **THEN** generated module instances SHALL be owned by that runtime context
- **AND** generated host-function registrations SHALL be owned by that runtime
  context

#### Scenario: Dotnet runtime context is torn down
- **GIVEN** a runtime context owns module instances and generated host-function
  registrations
- **WHEN** the host invokes the managed teardown callback
- **THEN** the context SHALL release managed module state exactly once
- **AND** future use of that context SHALL fail loudly
- **AND** later native JSI host-function release callbacks SHALL NOT double-free
  managed state
