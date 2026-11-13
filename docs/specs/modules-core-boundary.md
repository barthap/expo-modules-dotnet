# Modules Core Boundary

## Purpose

Define the boundary between low-level `Expo.JSI` wrappers and the
`Expo.ModulesCore` generated-binding helper package: package ownership, the
module registry, the runtime context, and test ownership.

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

### Requirement: Runtime Context Exposes Host-Supplied App Directories

`DotnetRuntimeContext` SHALL expose a `CacheDirectory` accessor for temporary
files the operating system may remove, and a `PersistentFilesDirectory` accessor
for app files that must survive cache eviction. Both SHALL be non-nullable
`string`. Neither SHALL be exposed to JavaScript.

The directories SHALL arrive at construction, not through a setter. Module
registration runs inside context creation, so a module constructor can already
observe the context, and a setter would leave a window where the directories are
silently absent. The one-argument constructor SHALL remain and SHALL mean both
directories are unconfigured. A second constructor SHALL accept an
`AppDirectories` model and SHALL throw `ArgumentNullException` for a null
argument.

Each accessor SHALL throw `AppDirectoryNotConfiguredException` when the host
supplied no value for it. That exception SHALL be publicly catchable, SHALL derive
from `InvalidOperationException`, and SHALL name the accessor that was
unconfigured so the two are distinguishable. Neither accessor SHALL fall back to a
user-wide or process-wide path. Every path a portable runtime can resolve on its
own is shared by every app on the machine, which is the collision these accessors
exist to remove. Upstream Android behaves the same way and throws when its
`AppDirectoriesService` is not registered.

Each accessor SHALL take the context's existing lock and run the existing
active-state check first, so a disposed context throws `ObjectDisposedException`
before configuration is consulted.

The two directories SHALL be independent. A host MAY supply one and leave the
other unconfigured, and coverage SHALL include both mixed cases.

The context SHALL also expose immutable `HostAppMetadata` through
`AppMetadata`. A three-argument constructor SHALL accept the runtime,
`AppDirectories`, and `HostAppMetadata` before module registration. Existing
one- and two-argument constructors SHALL use
`HostAppMetadata.Unconfigured`. `AppMetadata` SHALL run the context's active
state check before returning its model. The model SHALL independently preserve
nullable native app and build versions, reject empty, whitespace-only, or NUL
values when supplied, and SHALL NOT infer missing versions from process,
assembly, or package metadata.

`HostAppMetadata` SHALL also carry `ResourcesDirectory`: the host's read-only
directory of staged managed artifacts and their resource files, or null when the
host does not supply it. A supplied value SHALL NOT be empty, whitespace-only,
or contain NUL, and SHALL be a fully qualified path. Unlike `CacheDirectory`
and `PersistentFilesDirectory`, an unconfigured value is null, not an exception:
it is optional host information, not app-scoped storage, and hosts without app
files have nothing to put there. Module code SHALL NOT write to the
directory.

#### Scenario: Configured directory is returned verbatim
- **GIVEN** a host supplied a fully qualified path for a directory
- **WHEN** module code reads that accessor
- **THEN** it SHALL return the supplied path unchanged
- **AND** it SHALL NOT canonicalize or rewrite it

#### Scenario: Unconfigured directory throws a specific exception
- **GIVEN** a runtime context whose host supplied no value for a directory
- **WHEN** module code reads that accessor
- **THEN** it SHALL throw `AppDirectoryNotConfiguredException`
- **AND** the exception SHALL identify which accessor was unconfigured
- **AND** it SHALL NOT return a user-wide or process-wide path

#### Scenario: Disposed context throws before checking configuration
- **GIVEN** a runtime context has been disposed
- **WHEN** either directory accessor is read
- **THEN** it SHALL throw `ObjectDisposedException`
- **AND** it SHALL NOT throw `AppDirectoryNotConfiguredException` instead

#### Scenario: Host metadata belongs to the runtime context
- **GIVEN** a host supplies one or both native version values at construction
- **WHEN** a module reads `AppMetadata` before disposal
- **THEN** it SHALL receive the supplied immutable model with missing values
  left null
- **AND** reading `AppMetadata` after disposal SHALL throw
  `ObjectDisposedException`

#### Scenario: Resources directory is optional
- **GIVEN** a runtime context whose host supplied no resources directory
- **WHEN** module code reads `AppMetadata.ResourcesDirectory`
- **THEN** it SHALL be null and SHALL NOT throw

#### Scenario: Existing single-argument construction still compiles
- **GIVEN** an existing caller constructs a runtime context with only a runtime
- **WHEN** the code compiles and runs
- **THEN** construction SHALL succeed
- **AND** both directories SHALL report the unconfigured state

#### Scenario: One directory configured, the other not
- **GIVEN** a host supplies only one of the two directories
- **WHEN** the runtime context is created
- **THEN** the supplied accessor SHALL return that path
- **AND** the other accessor SHALL throw `AppDirectoryNotConfiguredException`

### Requirement: App Directory Validation Never Touches The Filesystem

`Expo.ModulesCore` SHALL NOT resolve, create, canonicalize, or probe a directory.
It SHALL NOT call `Environment.GetFolderPath`, `Path.GetTempPath`,
`Path.GetFullPath`, `Directory.*`, or `File.*`. Creating a subdirectory before
writing belongs to the consuming module, matching upstream iOS, which creates the
directory at use time rather than at configuration time.

`AppDirectories` SHALL reject a supplied path that is empty, whitespace-only,
contains a NUL character, or is not fully qualified, and SHALL name the offending
parameter. Validation SHALL be a pure string check, so
`Path.IsPathFullyQualified` is used and no disk is touched. Both an ABI-decoded
value and a direct managed caller's value SHALL flow through the same validation.

`AppDirectories` SHALL be immutable after construction, so no caller can change a
context's directory policy after module registration. It SHALL expose a shared
`Unconfigured` instance for hosts that supply neither directory. A `null` value for
either member means the host supplied nothing for that directory.

#### Scenario: Invalid supplied path is rejected at construction
- **GIVEN** a supplied path is empty, whitespace-only, contains NUL, or is
  relative
- **WHEN** the directory model is constructed
- **THEN** construction SHALL throw and SHALL name the offending parameter
- **AND** no runtime context SHALL be created with that value

#### Scenario: Validation performs no disk access
- **GIVEN** a supplied path is fully qualified but does not exist on disk
- **WHEN** the directory model is constructed
- **THEN** construction SHALL succeed
- **AND** validation SHALL NOT create, stat, or probe the path

#### Scenario: The core holds no filesystem API
- **GIVEN** `Expo.ModulesCore` sources are scanned for filesystem and
  special-folder APIs
- **WHEN** the scan runs
- **THEN** it SHALL report no matches
- **AND** a new filesystem call in the core SHALL be treated as a defect

#### Scenario: The model cannot be mutated after registration
- **GIVEN** a runtime context was created with a directory model
- **WHEN** module registration has run
- **THEN** no public member SHALL change either configured value
- **AND** the context SHALL keep serving the values it was constructed with

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

### Requirement: ModulesCore Owns Framework Module Tests

`Expo.ModulesCore.Tests` SHALL own generated binding, codec, registry,
lifecycle, event, callback, and shared-object behavior that is independent of
one authored package.

#### Scenario: Framework module behavior is tested
- **GIVEN** a test proves module-layer behavior that is independent of one
  authored package
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** the test SHALL live in `Expo.ModulesCore.Tests`

### Requirement: Authored Packages Own Their Behavior Tests

Each repo-local authored module package SHALL own its module-specific tests in
a `.Tests` project and MAY combine pure C# tests with Hermes-backed
`Expo.ModulesCore.Testing` tests.

#### Scenario: ExampleModule behavior is tested
- **GIVEN** a test proves behavior specific to `ExampleModule`
- **WHEN** the test is added
- **THEN** it SHALL live in
  `packages/example-module/dotnet/ExampleModule.Tests`
- **AND** `Expo.ModulesCore.Tests` SHALL NOT reference `ExampleModule` or own
  its package-specific tests

#### Scenario: Authored package registers its generated provider
- **GIVEN** an authored package test creates an `ExpoModuleTestHost`
- **WHEN** it needs its generated module surface in Hermes
- **THEN** it SHALL pass the package's generated provider `Register` method
  explicitly to the host
- **AND** the host SHALL register the provider against its normal
  `_expoDotnet.modules` object
- **AND** `Expo.ModulesCore.Testing` SHALL NOT discover providers through
  runtime reflection

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
