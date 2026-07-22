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

### Requirement: Generated JavaScript Member Names Are Lower Camel

Generated synchronous and asynchronous `[JS]` methods SHALL default to the
lower-camel form of the authored C# method name. The mapping SHALL lowercase
only the first character invariantly. An explicit `[JS(name)]` name SHALL be
exported verbatim.

#### Scenario: Implicit method name is lower camel
- **GIVEN** a module declares `[JS] public double Add(double a, double b)`
- **WHEN** generated registration installs the method
- **THEN** JavaScript SHALL receive an `add` function
- **AND** it SHALL NOT receive an `Add` compatibility alias

#### Scenario: Explicit method name is preserved
- **GIVEN** a module declares `[JS("ExactName")] public void Add()`
- **WHEN** generated registration installs the method
- **THEN** JavaScript SHALL receive `ExactName` verbatim
- **AND** generated registration SHALL NOT transform the explicit name

### Requirement: Generated Record Codecs Use Lower-Camel JavaScript Fields

Generated record codecs SHALL model the authored C# property name separately
from its JavaScript field name. Encoding and decoding SHALL use only the
lower-camel JavaScript name, while generated direct C# access SHALL continue to
use the authored C# name. Decoding SHALL NOT probe or fall back to a PascalCase
JavaScript field. A missing lower-camel field SHALL follow the existing codec's
behavior for JavaScript `undefined`.

#### Scenario: Record is encoded for JavaScript
- **GIVEN** a supported C# record has `Name`, `Age`, and `Summary` properties
- **WHEN** generated glue encodes the record
- **THEN** the JavaScript object SHALL have `name`, `age`, and `summary` own
  properties
- **AND** generated C# access SHALL still use `Name`, `Age`, and `Summary`

#### Scenario: Record is decoded from JavaScript
- **GIVEN** a supported record expects a `Name` property in C#
- **WHEN** generated glue decodes a JavaScript object
- **THEN** it SHALL read only the `name` JavaScript property
- **AND** it SHALL NOT read or fall back to `Name`

#### Scenario: Lower-camel record field is missing
- **GIVEN** a JavaScript object supplies only a stale PascalCase field
- **WHEN** generated glue reads the absent lower-camel field as `undefined`
- **THEN** a required codec that rejects `undefined` SHALL fail through the
  normal catchable JavaScript error path
- **AND** a nullable codec MAY decode `undefined` as `null`

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

### Requirement: `[JS]` Instance Properties Are JavaScript Accessors

`[JS]` SHALL support instance properties that have a public or internal
getter, are not static or indexed, do not have an `init` accessor, and have a
compile-time supported codec. A public or internal ordinary setter SHALL make
the JavaScript property writable. An absent or inaccessible setter SHALL make
it read-only. The JavaScript property name SHALL use the same lower-camel
default and verbatim explicit-name rule as generated methods.

Generated registration SHALL install the member as an own, enumerable,
configurable accessor property. Its getter SHALL have arity zero. A writable
setter SHALL have arity one; a read-only descriptor SHALL omit `set`.

#### Scenario: Read-write property is installed
- **GIVEN** a module declares `[JS] public bool Ready { get; set; }`
- **WHEN** generated registration installs the member
- **THEN** the module object SHALL have an own `ready` accessor property
- **AND** its descriptor SHALL be enumerable and configurable
- **AND** reading and assigning `module.ready` SHALL directly read and update
  the authored property

#### Scenario: Getter-only or inaccessible-setter property is read-only
- **GIVEN** a `[JS]` property has a readable getter and no public or internal
  ordinary setter
- **WHEN** generated registration installs the member
- **THEN** the descriptor SHALL have a zero-argument getter and no setter
- **AND** strict-mode assignment SHALL throw `TypeError`
- **AND** assignment SHALL NOT invoke authored module code

#### Scenario: Explicit property name is preserved
- **GIVEN** a module declares `[JS("isReady")] public bool Ready { get; }`
- **WHEN** generated registration installs the member
- **THEN** JavaScript SHALL expose `isReady` verbatim
- **AND** it SHALL NOT also expose `ready`

#### Scenario: Property access fails
- **GIVEN** an authored property getter throws or a setter codec rejects its
  assigned value
- **WHEN** JavaScript reads or assigns the property
- **THEN** the host-function boundary SHALL expose a catchable JavaScript
  `Error`
- **AND** a failed setter decode SHALL NOT invoke the authored setter

### Requirement: Generated Property Access Uses Direct Typed Glue

Generated property glue SHALL use compile-time codecs and direct C# property
access. It SHALL NOT use runtime reflection, dynamic invocation, thread
scheduling, or an ABI extension. Generated consumer assemblies SHALL install
accessors through the public generated-glue-only `GeneratedProperty` entry
point in `Expo.ModulesCore`; ordinary module author code SHALL NOT need to
construct JSI descriptors or host functions.

#### Scenario: Getter returns a module-convertible value
- **GIVEN** a generated property getter returns a type with a supported codec
- **WHEN** JavaScript reads the accessor
- **THEN** generated glue SHALL encode the result through that codec
- **AND** an owned returned wrapper SHALL transfer to the host-function bridge
  under the same rule as a synchronous `[JS]` method return

#### Scenario: Setter receives JavaScriptValue
- **GIVEN** a generated property setter accepts `JavaScriptValue`
- **WHEN** JavaScript assigns the property
- **THEN** generated glue SHALL retain and own the decoded wrapper for the
  synchronous setter invocation
- **AND** it SHALL dispose that invocation-owned wrapper after the authored
  setter returns or throws
- **AND** authored code SHALL NOT dispose or store the invocation-owned wrapper
- **AND** authored code that needs the value later SHALL store and eventually
  dispose an explicit retained copy

#### Scenario: Getter returns a retained JavaScriptValue copy
- **GIVEN** authored module state owns a stored `JavaScriptValue`
- **WHEN** a generated property getter exposes that value
- **THEN** the getter SHALL return an explicit retained copy whose ownership
  transfers to generated glue
- **AND** the module SHALL keep ownership of its original stored wrapper

`JavaScriptValue` is the existing advanced module convertible.
`JavaScriptObject` MAY become an optional advanced module convertible in a
separate future change, but this property support does not add its codec.

### Requirement: Generated Accessor Lifetimes Are Context-Owned

The runtime context SHALL own every generated property getter and setter
callback through `GeneratedHostFunctionRegistration`. Accessor installation
SHALL dispose every temporary owned wrapper used to construct and synchronously
pass the descriptor to `Object.defineProperty`, whether installation succeeds
or throws. The installed JavaScript descriptor SHALL retain its host-function
values independently. Callback `this` values and arguments SHALL remain scoped
to the host-function call.

#### Scenario: Descriptor installation completes
- **GIVEN** generated registration constructs a property descriptor and its
  host functions
- **WHEN** `Object.defineProperty` returns synchronously
- **THEN** generated glue SHALL dispose all temporary global, object,
  descriptor, function, and value wrappers
- **AND** the installed accessor SHALL remain callable through the JavaScript
  descriptor

#### Scenario: Descriptor installation fails
- **GIVEN** the runtime context has registered accessor callbacks
- **WHEN** `Object.defineProperty` throws
- **THEN** all temporary wrappers SHALL still be disposed
- **AND** all callback registrations SHALL remain owned and bounded by the
  runtime context until deterministic teardown

#### Scenario: Accessor is replaced
- **GIVEN** an accessor is already installed in an active runtime context
- **WHEN** registration installs the configurable property again
- **THEN** ordinary lookup SHALL use the replacement accessor
- **AND** a previously captured accessor function MAY remain callable while
  the context is active
- **AND** both old and current registrations SHALL remain context-owned

#### Scenario: Runtime context tears down after property installation
- **GIVEN** JavaScript retains a current or replaced accessor function
- **WHEN** the owning runtime context is disposed
- **THEN** teardown SHALL invalidate each accessor registration exactly once
- **AND** later accessor use SHALL fail loudly without touching released state
- **AND** later native release callbacks SHALL NOT double-free managed state

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

The implicit JavaScript event name SHALL lowercase only the first C# property
character. `[Event(name)]` SHALL use its explicit name verbatim, and the
generator SHALL NOT strip an `On` prefix.

#### Scenario: Typed payload and payload-less events are generated
- **GIVEN** a module declares `[Event] public partial Func<Task> OnReady { get; }`
  and `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** generated registration creates the module
- **THEN** each property SHALL return a cached delegate
- **AND** awaiting `OnReady()` SHALL dispatch `onReady` without a payload
- **AND** awaiting `OnProgress(value)` SHALL dispatch `onProgress` with its
  generated `ProgressEvent` codec

#### Scenario: Explicit typed event name is preserved
- **GIVEN** a module declares
  `[Event("StatusChanged")] public partial Func<string, Task> OnStatus { get; }`
- **WHEN** generated registration declares the event
- **THEN** JavaScript SHALL observe `StatusChanged` verbatim
- **AND** it SHALL NOT receive `onStatus` as an alias

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

For a direct `JavaScriptValue`, generated glue SHALL retain and encode an
invocation copy only while executing on the owning runtime. The caller SHALL
keep the original wrapper alive until the returned task completes. Generated
glue SHALL dispose only its invocation copy and SHALL NOT consume the original.

Records, lists, and dictionaries containing nested `JavaScriptValue` or
`ArrayBuffer`, and any payload containing `JavaScriptCallback`, SHALL be
rejected in this slice. Event-safety classification SHALL run before general
codec resolution can mutate generated-record-codec state, and SHALL inspect
only encoded inputs such as selected record-constructor parameters, list
elements, dictionary values, and nullable-value inner types. A rejected payload
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

### Requirement: Invalid Typed Events Are Build Diagnostics

Typed-event validation SHALL use `EXPOJSI018` for invalid event-property
shapes, `EXPOJSI019` for unsupported event payloads, and `EXPOJSI020` for
typed/typed or typed/legacy duplicate names. `EXPOJSI018` covers null, empty,
or blank explicit names; unsupported static, indexed, non-partial, implemented,
setter, explicit-interface, ref-return, `[JS]`, or modifier shapes; unsupported
delegate types; and file-local, nested, generic, or non-partial containers.
`EXPOJSI019` covers payloads with no encode-capable event codec, a callback
codec, or a nested transfer-sensitive wrapper. Legacy-only invalid or duplicate
`[Events]` declarations SHALL continue to use `EXPOJSI009`.

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
  or a nested direct wrapper
- **WHEN** the generator analyzes the event
- **THEN** it SHALL report `EXPOJSI019`
- **AND** it SHALL not emit reflection, dynamic conversion, callback `Encode`
  source, or a secondary compiler error

#### Scenario: Typed and legacy event names collide
- **GIVEN** `[Events("onStatus")]` and `[Event] ... OnStatus` occur on one
  module
- **WHEN** the generator analyzes the module
- **THEN** it SHALL report `EXPOJSI020`
- **AND** it SHALL not silently merge the duplicate declarations

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
  without any typed or legacy event declarations
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
Invalid property shapes SHALL use `EXPOJSI014`; unsupported readable property
types SHALL use `EXPOJSI015`; duplicate JavaScript names involving at least one
property SHALL use `EXPOJSI016`; and property use of a reserved observing-hook
name SHALL use `EXPOJSI017`. Method-only meanings SHALL remain stable:
`EXPOJSI004` covers unsupported or reserved method shapes, and `EXPOJSI005`
covers duplicate method names.

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

#### Scenario: Unsupported property shape is used
- **GIVEN** a `[JS]` property is static, indexed, lacks a public or internal
  getter, is setter-only, or has an `init` accessor
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI014` naming the property and
  unsupported shape
- **AND** generated source SHALL NOT omit the member silently

#### Scenario: Unsupported property type is used
- **GIVEN** a readable `[JS]` property has no generated codec for its type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI015` naming the property and type
- **AND** generated source SHALL NOT emit reflection or dynamic conversion

#### Scenario: Property export name collides
- **GIVEN** two properties, or one property and one method, resolve to the same
  JavaScript name
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI016` naming the module and
  duplicate JavaScript member name
- **AND** it SHALL NOT resolve the collision by declaration order

#### Scenario: Property uses a reserved observing-hook name
- **GIVEN** an event-capable module has a `[JS]` property that resolves to
  `startObserving` or `stopObserving`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI017` naming the property and
  reserved hook name
- **AND** it SHALL NOT generate the conflicting accessor

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

### Requirement: Typed JavaScript Facades Remain Registry-Owned

`expo-modules-dotnet` SHALL export `EventsMap`, `EventSubscription`,
`DotnetEventEmitter`, and `DotnetModule` for typed JavaScript module facades.
`DotnetModule` SHALL extend `DotnetEventEmitter`; both exports SHALL be real
JavaScript class values only so TypeScript can use them in heritage clauses.

#### Scenario: Author declares a typed module facade
- **GIVEN** an author defines an explicit map from supported event names to
  listener signatures
- **WHEN** it declares an internal `declare class` that extends
  `DotnetModule<Events>` and exports a public type alias for that class
- **THEN** `addListener`, `removeListener`, `removeAllListeners`, `emit`, and
  `listenerCount` SHALL use the selected event-map name and listener or
  argument types
- **AND** `addListener` SHALL return an `EventSubscription` whose `remove()`
  method releases the listener registration
- **AND** the author MAY obtain the facade with the unconstrained
  `requireDotnetModule<T>` generic
- **AND** existing plain-object facade types SHALL remain compatible

#### Scenario: Facade classes are not module constructors
- **GIVEN** JavaScript imports `DotnetEventEmitter` or `DotnetModule`
- **WHEN** it directly constructs either class
- **THEN** the constructor SHALL throw and direct the caller to
  `requireDotnetModule`
- **AND** a native registry module object SHALL NOT be guaranteed to be an
  `instanceof` either facade class

#### Scenario: Modern event facade excludes runtime hooks
- **GIVEN** an author uses the typed JavaScript event facade
- **WHEN** it registers or removes a listener
- **THEN** it SHALL use `addListener` and `EventSubscription.remove()`
- **AND** `removeSubscription`, `startObserving`, and `stopObserving` SHALL
  NOT be members of the modern facade
- **AND** `emit` SHALL remain typed because the installed native prototype
  exposes it, but ordinary JavaScript facades SHALL treat it as primarily
  runtime-internal

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

### Requirement: Module-facing ArrayBuffer And Binary Codecs

`Expo.ModulesCore.ArrayBuffer` SHALL be the universal module-facing binary
abstraction with exactly two private backing forms: JavaScript-owned storage
and native MutableBuffer-owned storage. `byte[]` and span support SHALL be
convenience codecs, not additional backing kinds.

#### Scenario: Module ArrayBuffer owns storage
- **GIVEN** a module receives or creates an `ArrayBuffer`
- **WHEN** it retains, copies, accesses, or disposes the value
- **THEN** ownership SHALL remain explicit, disposal SHALL atomically relinquish
  the backing lease, and duplicate disposal SHALL be idempotent
- **AND** the wrapper SHALL remain single-owner: concurrent use and disposal
  are unsupported, and a concurrent consumer SHALL receive a retained copy
- **AND** `ByteLength` SHALL be available without entering JSI
- **AND** `Copy`/`ToArray` operations SHALL produce independent storage

#### Scenario: JavaScript and native backing are encoded
- **GIVEN** a JavaScript-backed value is encoded into its originating runtime
- **WHEN** the module returns it
- **THEN** the original JavaScript object identity SHALL be preserved
- **WHEN** a native-backed value is encoded into any live runtime
- **THEN** the returned JavaScript object SHALL be distinct while sharing bytes

#### Scenario: Byte arrays and spans cross the module boundary
- **GIVEN** a generated method uses `byte[]`, `Span<byte>`, or
  `ReadOnlySpan<byte>`
- **WHEN** the method is generated
- **THEN** byte arrays SHALL copy in both directions
- **AND** one synchronous scoped span parameter MAY borrow bytes
- **AND** asynchronous or multiple span parameters SHALL produce diagnostics
- **AND** span return values SHALL be copied immediately
- **AND** only rank-one `byte[]` parameters and returns SHALL use the byte-array
  codec; multidimensional byte arrays SHALL produce unsupported-type diagnostics
- **AND** the one-span limit SHALL not restrict `ArrayBuffer` or `byte[]` arity

### Requirement: Internal Shared-Object Identity Registry

Each `DotnetRuntimeContext` SHALL own one internal `SharedObjectRegistry` for
the runtime. The registry SHALL map a managed internal lifetime-contract
instance to one live JavaScript object entry by reference identity, and map a
private entry id back to that same instance. Each entry may retain only its
managed lifetime state, NativeState token, and opaque `JavaScriptWeakObject`;
it SHALL NOT retain an ordinary object, function, value, or prototype wrapper
after conversion returns.

#### Scenario: Internal identity round trip
- **GIVEN** an active entry for one managed lifetime-contract instance
- **WHEN** it is converted to JavaScript twice or decoded through its private
  NativeState token
- **THEN** both JavaScript conversions SHALL be strictly equal
- **AND** decoding SHALL return the original managed instance

#### Scenario: Terminal shared entry
- **GIVEN** JavaScript release, deterministic collection, NativeState cleanup,
  or runtime-context disposal reaches an entry
- **WHEN** the first terminal source removes it from both maps
- **THEN** the registry SHALL dispose the opaque weak wrapper and run the
  managed lifetime action exactly once outside both registry and weak-wrapper
  locks
- **AND** later release sources SHALL be no-ops
- **AND** the same managed instance SHALL not form a replacement pair

#### Scenario: NativeState callback re-enters registry work
- **GIVEN** NativeState cleanup occurs while the registry gate is held
- **WHEN** its callback identifies a terminal entry
- **THEN** it SHALL defer terminal work until the registry gate has been
  released
- **AND** it SHALL not use an access frame, ordinary JSI wrapper operation,
  blocking runtime scheduling, a raw managed pointer, or a JavaScript-visible
  identifier

#### Scenario: Context teardown owns shared entries first
- **GIVEN** a context is being disposed while its runtime is still valid
- **WHEN** it drains owned state
- **THEN** it SHALL drain the shared-object registry before generated host
  registrations, retained callbacks, module registry, event state, and object
  factory
- **AND** it SHALL continue later cleanup after a failure and report aggregate
  failures only after reaching the terminal context state

The registry is also the identity and lifetime mechanism for generated public
`SharedObject` bindings. Its per-context ownership, reference-identity maps,
weak counterpart, private NativeState token, no-repairing rule, exactly-once
terminal release outside locks, re-entry deferral, and teardown-first ordering
apply unchanged to the public surface. Cross-runtime pairing, shared-object
events, and a `JavaScriptObject` codec remain separate future capabilities;
`JavaScriptValue` remains the existing advanced module convertible.

#### Scenario: Public binding reuses the proven registry

- **GIVEN** generated public shared-object glue encodes, decodes, constructs,
  releases, or tears down an instance
- **WHEN** it performs identity or terminal work
- **THEN** it SHALL use the context-owned `SharedObjectRegistry`
- **AND** it SHALL preserve every current registry identity and teardown
  scenario
- **AND** it SHALL not add an ABI entry or modify `Expo.JSI`

#### Scenario: Registry operation can trigger external work

- **GIVEN** constructor installation, class-prototype setup, author code, or a
  JavaScript callback could re-enter registry or context work
- **WHEN** generated or runtime glue performs that work
- **THEN** it SHALL do so outside the registry gate
- **AND** any failed registration SHALL roll back without weakening terminal
  or NativeState re-entry semantics

### Requirement: Public SharedObject Authoring Is Explicit

`Expo.ModulesCore` SHALL expose a public abstract `SharedObject` base class and
an `[ExpoSharedObject]` class attribute. An attributed class SHALL be top-level,
non-generic, sealed, partial, and derived directly or indirectly from
`SharedObject`. It MAY be public or internal. Indirect derivation MAY use a
generic managed carrier base such as `SharedRef<T>`, but only the concrete
sealed attributed class is generated. The attribute MAY accept one non-empty
explicit JavaScript class name; otherwise the class name SHALL be the authored
C# type name verbatim, matching current module-class naming.

`SharedObject` SHALL hide its registry lifetime implementation from authors.
Ordinary authored code SHALL NOT receive registry identifiers, NativeState
tokens, weak wrappers, JSI handles, or explicit runtime-scheduling duties.

#### Scenario: Valid shared-object class is discovered

- **GIVEN** a top-level non-generic sealed partial class derives from
  `SharedObject` and has `[ExpoSharedObject]`
- **WHEN** its compilation runs the ModulesCore generator
- **THEN** the generator SHALL model it as an authored shared-object class
- **AND** it SHALL emit direct generated support only when an owning module
  lists the class

#### Scenario: Implicit JavaScript class name is generated

- **GIVEN**
  `[ExpoSharedObject] public sealed partial class CacheEntry : SharedObject`
- **WHEN** the generator exports the class for an owning module
- **THEN** the JavaScript class name SHALL be `CacheEntry`
- **AND** the generator SHALL NOT transform it to a member-style lower-camel
  name

#### Scenario: Explicit JavaScript class name is preserved

- **GIVEN** a shared-object class declares `[ExpoSharedObject("NativeCache")]`
- **WHEN** the generator exports it
- **THEN** its JavaScript class name SHALL be `NativeCache` verbatim
- **AND** the generator SHALL NOT also export an implicit-name alias

#### Scenario: Shared-object declaration is invalid

- **GIVEN** `[ExpoSharedObject]` appears on a nested, generic, non-sealed,
  non-partial, or non-`SharedObject` class, or its explicit name is null, empty,
  or blank
- **WHEN** the generator analyzes the declaration
- **THEN** it SHALL report `EXPOJSI021`
- **AND** it SHALL NOT silently emit a partial or reflection-based binding

### Requirement: Modules Explicitly Own Shared-Object Classes

`ExpoModuleAttribute` SHALL expose a settable `Type[] Classes` property whose
default is empty. An authored module SHALL list each shared-object class it
owns with `[ExpoModule(Classes = new[] { typeof(CacheEntry) })]`. One authored
shared-object type SHALL have exactly one owning module in a compilation, and a
module SHALL NOT list the same type more than once. Exported class names SHALL
be unique within an owning module.

For a class with an exposed constructor, `EXPOJSI024` validation SHALL compare
its effective JavaScript name against the owning module's complete effective
JavaScript namespace. That namespace includes generated methods and
properties, all exposed class constructors, generated observing hooks, and the
inherited or reserved event-runtime members. A collision SHALL fail generation;
registration SHALL NOT overwrite an existing member or choose by source order.
Native-created-only classes do not add a module property, but their effective
class names SHALL still be unique among every class owned by the module because
the generated prototype and codec identity table uses those names.

Generated default registration SHALL preserve one-stage module laziness.
Shared-object class installation SHALL occur when the owning module object is
created, not when the package provider first registers its module metadata.

#### Scenario: Owning module is materialized

- **GIVEN** a lazy module lists one valid shared-object class
- **WHEN** JavaScript first resolves that module
- **THEN** generated registration SHALL install the class prototype for that
  runtime context
- **AND** it SHALL expose a module property containing the class constructor
  only when the class has a valid `[JS]` constructor
- **AND** later reads of the module SHALL reuse the same module and class
  installation

#### Scenario: Native-created-only class is owned

- **GIVEN** an owning module lists a valid shared-object class with no `[JS]`
  constructor
- **WHEN** the module is materialized
- **THEN** generated registration SHALL install the internal class prototype
  needed for encoded instances
- **AND** it SHALL NOT expose a constructible class property on the module
- **AND** generated methods MAY return managed instances of that class

#### Scenario: Class ownership is invalid or duplicated

- **GIVEN** a `Classes` entry is not an attributed `SharedObject`, one type is
  listed more than once, one type has multiple owning modules, an attributed
  type has no owning module, or two owned classes, including
  native-created-only classes, resolve to the same effective name in one module
- **WHEN** the generator analyzes module ownership
- **THEN** it SHALL report `EXPOJSI024`
- **AND** it SHALL NOT resolve ownership or naming by declaration order

#### Scenario: Exposed class name collides with the module namespace

- **GIVEN** a constructible shared-object class name matches a generated module
  method or property, another exposed class constructor, an observing hook, or
  an inherited or reserved event-runtime member
- **WHEN** the generator computes the complete effective module namespace
- **THEN** it SHALL report `EXPOJSI024` naming both conflicting surfaces
- **AND** generated registration SHALL NOT overwrite either surface

#### Scenario: Native-created-only class names collide

- **GIVEN** two native-created-only classes owned by one module resolve to the
  same effective class name
- **WHEN** the generator builds its prototype and codec identity table
- **THEN** it SHALL report `EXPOJSI024`
- **AND** it SHALL NOT choose a prototype by declaration order

### Requirement: Generated Constructors Create Registry-Paired Instances

An authored shared-object class MAY declare exactly one instance constructor
with `[JS]`. A generated JavaScript constructor SHALL be available only for a
public or internal attributed constructor whose parameters all have
compile-time decode codecs. Constructor arguments SHALL be decoded during the
JavaScript call and the C# constructor SHALL be invoked directly.

The implementation MAY use a generated host function that is valid with
JavaScript `new`, assign its generated prototype explicitly, and return a
registry-paired object created with that prototype. It SHALL NOT depend on a
particular helper being callback-capable. Construction SHALL finish with one
managed instance paired to the returned JavaScript object, carrying the
registry's private NativeState token.

Registry entry creation, prototype selection, and rollback SHALL NOT execute
user-controlled JavaScript or other re-entrant work while the registry gate is
held. Argument-decoding failure or an authored constructor that throws before
returning an instance SHALL release temporary wrappers but has no managed
instance to release. Once an attributed `[JS]` constructor returns a managed
instance, the generated construction path SHALL own that instance until it is
successfully paired and returned to JavaScript. If prototype setup, NativeState
attachment, weak-object creation, map insertion, or later pairing work fails,
generated/runtime glue SHALL dispose every partial registration and owned
wrapper, mark the instance terminal under the no-repairing rule, and invoke its
`OnRelease` exactly once outside registry and weak-wrapper locks. NativeState or
other rollback re-entry SHALL converge on that same terminal action.

This constructor-originated transfer is distinct from encoding an existing
module-owned instance. Ordinary encoding SHALL NOT transfer ownership of that
instance merely by attempting a first pairing.

#### Scenario: JavaScript constructs a shared object

- **GIVEN** an owned class has one valid `[JS]` constructor
- **WHEN** JavaScript evaluates `new module.CacheEntry(arguments)`
- **THEN** generated glue SHALL decode the arguments and call that C#
  constructor directly
- **AND** the returned object SHALL inherit from
  `module.CacheEntry.prototype`
- **AND** it SHALL carry an active private registry token for the new managed
  instance
- **AND** encoding that managed instance later SHALL return the same
  JavaScript object by strict equality

#### Scenario: Shared-object constructor is invalid

- **GIVEN** a shared-object class declares multiple `[JS]` constructors, an
  inaccessible attributed constructor, an explicit `[JS(name)]` constructor
  name, or an otherwise unsupported constructor shape
- **WHEN** the generator analyzes the class
- **THEN** it SHALL report `EXPOJSI022`
- **AND** it SHALL NOT expose a JavaScript constructor for that class

#### Scenario: Constructor parameter has no codec

- **GIVEN** a valid-shaped `[JS]` constructor has a parameter without a
  compile-time decode codec
- **WHEN** the generator analyzes the constructor
- **THEN** it SHALL report `EXPOJSI023` naming the constructor, parameter, and
  unsupported type
- **AND** it SHALL NOT emit dynamic conversion or runtime reflection

#### Scenario: Constructor registration fails partway

- **GIVEN** an attributed `[JS]` constructor has returned a managed instance
  and generated construction has created some but not all pairing state
- **WHEN** a later construction or registration operation throws
- **THEN** all temporary wrappers and partial registry state SHALL be released
- **AND** no entry SHALL resolve for the failed construction
- **AND** the managed instance SHALL be marked terminal and `OnRelease` SHALL
  run exactly once outside registry and weak-wrapper locks
- **AND** any leaked reference to that instance SHALL fail later encoding under
  the no-repairing rule
- **AND** a later valid construction SHALL not encounter stale ownership or
  duplicate-registration state

#### Scenario: Authored constructor fails before returning

- **GIVEN** argument decoding or the attributed C# constructor throws before it
  returns a managed instance
- **WHEN** generated construction reports the failure to JavaScript
- **THEN** it SHALL dispose every temporary wrapper
- **AND** it SHALL NOT fabricate a terminal managed instance or registry entry

### Requirement: Shared-Object Members Are Generated On The Prototype

`[JS]` SHALL support instance methods and instance accessor properties on an
authored shared-object class. Generated methods SHALL support the existing
synchronous and `Task`/`Task<T>` asynchronous forms. Generated properties
SHALL follow the existing readable-getter and optional public/internal ordinary
setter rules. Static, generic, indexed, setter-only, init-only, or inaccessible
members SHALL be rejected.

Implicit method and property names SHALL lowercase only the first authored C#
character invariantly. Explicit `[JS(name)]` member names SHALL be exported
verbatim. Generated members SHALL be installed on the class prototype, not as
per-instance own functions or accessors. `release`, `constructor`, and
`__proto__` SHALL be reserved on generated shared-object prototypes. Authored
members SHALL NOT replace or shadow those lifetime and prototype surfaces.

Each invocation SHALL resolve its JavaScript receiver through the owning
context's registry, validate the expected managed class, call the authored
member directly, and encode its result with compile-time codecs. A foreign,
wrong-class, released, or torn-down receiver SHALL fail with a catchable
JavaScript error before authored code runs.

#### Scenario: Method and property names use current rules

- **GIVEN** a shared object declares `[JS] GetSize`, `[JS("ResetNow")] Reset`,
  and `[JS] IsReady`
- **WHEN** generated registration installs its prototype
- **THEN** JavaScript SHALL receive `getSize`, `ResetNow`, and `isReady`
- **AND** it SHALL NOT receive PascalCase aliases for implicit names

#### Scenario: Shared-object method is invoked

- **GIVEN** JavaScript calls a generated prototype method with an active
  shared-object receiver
- **WHEN** receiver and parameter decoding succeeds
- **THEN** generated glue SHALL call the original managed instance directly
- **AND** it SHALL encode the result through its compile-time codec
- **AND** it SHALL NOT use reflection, dynamic invocation, JSON, or
  `object?[]` as the normal argument path

#### Scenario: Shared-object property is accessed

- **GIVEN** a shared object declares a valid `[JS]` accessor property
- **WHEN** JavaScript reads or writes the generated prototype accessor
- **THEN** the getter or setter SHALL operate directly on the registry-resolved
  managed instance
- **AND** a missing or inaccessible setter SHALL produce a read-only
  descriptor
- **AND** codec failure SHALL occur before the authored setter runs

#### Scenario: Shared-object member is invalid

- **GIVEN** a `[JS]` member has an unsupported shape or parameter, return, or
  property type, or `[Event]` is declared on a shared object in this change
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report `EXPOJSI023` with the member and reason
- **AND** it SHALL NOT silently skip the member or emit a dynamic fallback

#### Scenario: Shared-object JavaScript member name conflicts

- **GIVEN** generated members resolve to the same JavaScript name or a member
  resolves to the reserved name `release`, `constructor`, or `__proto__`
- **WHEN** the generator analyzes the class
- **THEN** it SHALL report `EXPOJSI025`
- **AND** it SHALL NOT resolve the conflict by declaration order

#### Scenario: Prototype infrastructure name is reserved

- **GIVEN** a `[JS]` method or property explicitly or implicitly resolves to
  `release`, `constructor`, or `__proto__`
- **WHEN** the generator builds the shared-object prototype model
- **THEN** it SHALL report `EXPOJSI025` naming the reserved surface
- **AND** generated prototype installation SHALL retain its lifetime and
  prototype infrastructure unchanged

### Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity

The generator SHALL provide compile-time codecs for each valid, owned, sealed
authored shared-object type used directly as a method parameter, method return,
constructor parameter, or property type. The codec SHALL be bound to the
current `DotnetRuntimeContext` and exact managed class. `SharedObject`,
`SharedRef<T>`, an unannotated shared-object base, and other polymorphic base
types SHALL NOT be generated-boundary codec types.

Decoding SHALL read the private NativeState token through the existing
registry, require an active entry owned by the current context, validate the
exact expected managed runtime type, and return the original managed instance.
Assignable base/derived compatibility SHALL NOT select a codec or prototype.
Decoding SHALL NOT construct, clone, or substitute a managed object.

Encoding SHALL ask the existing registry for the live JavaScript counterpart.
For an unpaired, unreleased managed instance, it SHALL create one JavaScript
object with the generated class prototype, attach one private registry token,
and add one entry only after verifying that the instance's runtime type exactly
matches the codec's sealed authored type. For an active paired instance, it
SHALL return a newly owned wrapper for the same JavaScript object. A released
instance SHALL never be paired again. Cross-context, cross-runtime, or
base/derived pairing SHALL fail loudly.

If first pairing fails while encoding a pre-existing module-owned instance,
generated/runtime glue SHALL dispose every partial registration and owned
wrapper but SHALL NOT mark that instance terminal or invoke `OnRelease`.
Ownership remains with the module author, and a later pairing attempt MAY be
made. This rollback SHALL not leave an entry, NativeState association, or
released-instance marker.

The registry entry SHALL continue to retain only managed lifetime state, its
NativeState state, and an opaque `JavaScriptWeakObject`. Generated conversion
SHALL dispose or transfer every ordinary object, function, value, prototype,
and scoped wrapper before returning.

#### Scenario: Managed instance is encoded twice

- **GIVEN** an unreleased managed shared-object instance is encoded twice in
  one runtime context while its JavaScript counterpart remains live
- **WHEN** JavaScript compares both results
- **THEN** they SHALL be strictly equal
- **AND** the registry SHALL contain one entry for that managed instance

#### Scenario: JavaScript object is decoded

- **GIVEN** JavaScript passes an active paired object to a generated parameter
  expecting its authored shared-object type
- **WHEN** the generated codec decodes it
- **THEN** it SHALL return the exact original managed instance
- **AND** it SHALL NOT allocate another instance or registry entry

#### Scenario: Shared-object type does not match

- **GIVEN** JavaScript passes a foreign object or a paired object of another
  authored shared-object class, including a base/derived mismatch
- **WHEN** a generated shared-object codec decodes it
- **THEN** decoding SHALL fail with a catchable JavaScript error
- **AND** authored method or property code SHALL NOT run

#### Scenario: Encoded runtime type does not match exactly

- **GIVEN** generated code attempts to encode a value whose runtime type is not
  exactly the sealed attributed type selected by the codec
- **WHEN** the codec validates the value
- **THEN** encoding SHALL fail before creating or looking up a registry pair
- **AND** it SHALL NOT select a base or derived prototype
- **AND** it SHALL NOT terminally release the caller-owned value

#### Scenario: First pairing of a pre-existing instance fails

- **GIVEN** a module-owned, unreleased shared-object instance existed before an
  ordinary generated return or property encoding began
- **WHEN** its first pairing fails after creating partial state
- **THEN** generated/runtime glue SHALL remove and dispose all partial pairing
  state and surface the conversion failure
- **AND** ownership SHALL remain with the module author
- **AND** the instance SHALL remain unreleased, `OnRelease` SHALL not run, and a
  later pairing attempt MAY proceed

#### Scenario: Shared-object conversion follows terminal release

- **GIVEN** a shared-object entry has reached terminal release
- **WHEN** generated code encodes its managed instance or decodes its stale
  JavaScript object
- **THEN** conversion SHALL fail loudly
- **AND** it SHALL NOT create a replacement pair or invoke `OnRelease` again

### Requirement: Public SharedObject Release Is Exactly Once

`SharedObject` SHALL expose a protected virtual `OnRelease()` hook. All
terminal sources, including JavaScript `release()`, deterministic JavaScript
collection, and `DotnetRuntimeContext` teardown, SHALL converge on the existing
registry terminal path. The first source SHALL make the instance terminal and
invoke `OnRelease` exactly once outside registry and weak-wrapper locks. Later
terminal sources and repeated JavaScript `release()` calls SHALL be no-ops.

`OnRelease` SHALL run synchronously on whichever thread wins terminal release.
Authors SHALL NOT assume JavaScript, UI, or scheduler thread affinity. The hook
MAY release thread-safe managed or native resources. It SHALL NOT access JSI,
use bridge wrappers, enter or schedule JavaScript runtime work, block waiting
for runtime work, or resurrect/re-pair the instance.

An `OnRelease` failure SHALL NOT undo terminal state or prevent later context
owners from being cleaned up. Explicit JavaScript release SHALL surface a
catchable JavaScript error; context teardown SHALL include the failure in its
aggregate-and-continue result. Collection-triggered cleanup SHALL not allow an
exception to escape through a native release callback.

#### Scenario: JavaScript explicitly releases an object

- **GIVEN** JavaScript holds an active generated shared-object instance
- **WHEN** it calls `release()` one or more times
- **THEN** the first call SHALL terminally detach the managed pairing and call
  `OnRelease` once
- **AND** later calls SHALL be no-ops
- **AND** later generated method or property access SHALL fail before authored
  member code runs

#### Scenario: JavaScript collection releases an object

- **GIVEN** an active generated shared object has no strong JavaScript owner
- **WHEN** deterministic collection releases its private NativeState
- **THEN** the registry SHALL reach the same terminal path and call
  `OnRelease` once
- **AND** cleanup SHALL use no JSI wrapper, access frame, blocking runtime
  operation, or raw managed pointer

#### Scenario: Runtime teardown releases live shared objects

- **GIVEN** a `DotnetRuntimeContext` still owns active public shared-object
  entries
- **WHEN** context teardown drains its shared-object registry first
- **THEN** every entry SHALL become terminal and attempt `OnRelease` once
- **AND** cleanup SHALL continue after any hook failure
- **AND** later use SHALL fail without touching invalid runtime state

### Requirement: SharedRef Is A Non-Owning SharedObject

`Expo.ModulesCore` SHALL expose a public derivable `SharedRef<T>` that extends
`SharedObject`, receives a `T` in its constructor, and exposes that same value
through a read-only `Ref` property. It SHALL hold a strong managed
reference to `T` for the lifetime of the `SharedRef<T>` instance.

`SharedRef<T>` SHALL NOT infer ownership from `T`, test whether `T` implements
`IDisposable` or `IAsyncDisposable`, or dispose `T` automatically. Its default
release behavior SHALL be a no-op for `T`. A subclass that owns the resource
MAY override `OnRelease` and perform allowed cleanup explicitly.

`SharedRef<T>` itself SHALL be a managed carrier base, not a generated codec
surface. A generated parameter, return, constructor parameter, or property
SHALL use a concrete, sealed, non-generic `[ExpoSharedObject]` subclass of
`SharedRef<T>`. Direct generated-boundary use of `SharedRef<T>`, whether open or
constructed, SHALL report `EXPOJSI023` instead of selecting a polymorphic
prototype.

#### Scenario: Non-owning SharedRef is released

- **GIVEN** a `SharedRef<T>` carries a disposable value supplied by another
  owner
- **WHEN** JavaScript release, collection, or runtime teardown releases the
  shared ref
- **THEN** its inherited lifetime SHALL become terminal exactly once
- **AND** `SharedRef<T>` SHALL NOT call `Dispose` or `DisposeAsync` on `T`

#### Scenario: Owning subclass releases its resource

- **GIVEN** a concrete sealed attributed subclass of `SharedRef<T>` explicitly
  owns its carried resource
- **WHEN** terminal release invokes the subclass's `OnRelease`
- **THEN** the subclass MAY release that resource once without JSI or runtime
  work
- **AND** repeated JavaScript release and later teardown SHALL not repeat the
  hook

#### Scenario: Concrete SharedRef subclass crosses the boundary

- **GIVEN** a sealed non-generic `[ExpoSharedObject]` class derives from
  `SharedRef<NativeImage>`
- **WHEN** a generated member uses that concrete class as a parameter, return,
  constructor parameter, or property type
- **THEN** the generator SHALL use that class's exact generated codec and
  prototype
- **AND** `SharedRef<NativeImage>` SHALL remain only its managed carrier base

#### Scenario: SharedRef base crosses the boundary directly

- **GIVEN** a generated member directly declares `SharedRef<T>` or a constructed
  `SharedRef<NativeImage>` as a parameter, return, constructor parameter, or
  property type
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report `EXPOJSI023`
- **AND** it SHALL NOT emit a base-type codec, reflection fallback, or
  assignability-based prototype selection

### Requirement: TypeScript Facades Expose Explicit Release

`expo-modules-dotnet` SHALL export a real JavaScript class value named
`DotnetSharedObject` for TypeScript facade heritage clauses. Its public type
surface SHALL contain `release(): void`. Direct construction of
`DotnetSharedObject` SHALL throw and explain that usable instances come from a
generated module class or a generated module return value. Native generated
instances SHALL NOT be guaranteed to satisfy `instanceof DotnetSharedObject`.

An authored TypeScript facade for a constructible class SHALL declare a class
extending `DotnetSharedObject` and type the owning module's class property as
that class constructor. A native-created-only class SHALL be represented as an
instance type returned by module methods, without a constructible module
property.

The package and module authoring guide SHALL document `release()` as the only
deterministic JavaScript cleanup API in this change. Examples SHALL use
`try/finally` when deterministic cleanup is required. They SHALL state that
release is idempotent and that any later native member access fails.

#### Scenario: TypeScript facade declares a constructible class

- **GIVEN** an example module exposes a valid generated shared-object
  constructor
- **WHEN** its TypeScript facade models that module
- **THEN** the facade SHALL provide a constructible class type extending
  `DotnetSharedObject`
- **AND** instances SHALL expose the generated members and `release()`

#### Scenario: Deterministic JavaScript cleanup is documented

- **GIVEN** an example acquires a resource-owning shared object
- **WHEN** documentation demonstrates bounded lifetime
- **THEN** it SHALL call `release()` from `finally`
- **AND** it SHALL NOT require `Symbol.dispose` or JavaScript `using` syntax

#### Scenario: Symbol disposal is considered later

- **GIVEN** a future change proposes `Symbol.dispose` support
- **WHEN** that change is designed
- **THEN** it SHALL separately review the package's minimum TypeScript version,
  emitted library types, and supported JavaScript runtimes
- **AND** this change SHALL remain compatible with explicit `release()`

### Requirement: SharedObject Bindings Are Fully Generated And Portable

Shared-object discovery, validation, construction, member dispatch, and codec
selection SHALL be build-time generated and NativeAOT-compatible. The runtime
path SHALL use direct calls, typed codecs, context-owned generated host-function
registrations, and the existing managed registry and `Expo.JSI` primitives.
It SHALL NOT use runtime reflection, dynamic invocation, JSON conversion, raw
JSI layouts, a new C ABI entry, or a platform-specific dependency.

#### Scenario: Generated provider registers shared objects

- **GIVEN** a library compilation contains owned authored shared-object classes
- **WHEN** its generated provider registers with a `DotnetRuntimeContext`
- **THEN** generated code SHALL use context-owned host-function registrations
  and direct typed glue
- **AND** the same generated source SHALL remain valid for HostFXR and
  NativeAOT loading

#### Scenario: Generator diagnostics are verified

- **GIVEN** invalid declarations exercise `EXPOJSI021` through `EXPOJSI025`
- **WHEN** generator tests compile each invalid source shape
- **THEN** each diagnostic SHALL be asserted independently with its relevant
  source location and message arguments
- **AND** rejected declarations SHALL not produce secondary generated-C#
  errors

#### Scenario: Public identity and lifetime behavior is verified

- **GIVEN** the public shared-object surface is implemented
- **WHEN** the Hermes-backed ModulesCore suite runs
- **THEN** it SHALL prove JavaScript construction and prototype identity,
  managed-to-JavaScript strict identity, JavaScript-to-managed original
  instance lookup, explicit release, deterministic collection, context
  teardown, use after release, exact-type encode/decode rejection, and
  `SharedRef<T>` non-ownership
- **AND** it SHALL prove that post-constructor pairing failure terminally
  releases the returned instance exactly once while failed pairing of a
  pre-existing module-owned instance does not release it
- **AND** generator tests SHALL cover a non-sealed attributed class, direct
  `SharedRef<T>` boundary use, a valid sealed concrete `SharedRef<T>` subclass,
  collisions with each effective module namespace category, duplicate
  native-created-only class names, and each of the `release`, `constructor`,
  and `__proto__` prototype reservations
- **AND** existing internal `SharedObjectRegistryTests` SHALL remain unchanged
  and pass
