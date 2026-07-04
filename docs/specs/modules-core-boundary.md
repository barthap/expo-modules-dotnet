# Modules Core Boundary

## Purpose

Define the boundary between low-level `Expo.JSI` wrappers and the
`Expo.ModulesCore` generated-binding helper package.

## Requirements

### Requirement: ModulesCore Owns Generated-Binding Helpers

`Expo.ModulesCore` SHALL own module registration helpers, generated dispatch
helpers, and typed conversion helpers above `Expo.JSI`. It lives under
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore` as part of the
public Expo adapter package's managed core.

#### Scenario: Generated-looking provider registers a module
- **GIVEN** generated-looking provider code has a `JavaScriptRuntime` and a
  JavaScript modules object
- **WHEN** it installs a module under the supplied modules object
- **THEN** it SHALL use `Expo.ModulesCore` helpers instead of placing
  module-layer abstractions in `Expo.JSI`
- **AND** it SHALL NOT hardcode `globalThis.expo.modules`

#### Scenario: Managed proof uses default dotnet namespace
- **GIVEN** managed proof or test code needs a default modules object
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
- **AND** the default overload SHALL install under the context-owned default
  dotnet modules object
- **AND** the explicit overload SHALL install under the supplied modules object
- **AND** generated registration SHALL use `DotnetRuntimeContext` module
  instances
- **AND** generated registration SHALL NOT require runtime reflection

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

### Requirement: Unsupported Signatures Are Build Diagnostics

Unsupported generated function signatures SHALL fail at build time with
actionable diagnostics. Unsupported shapes SHALL fail the consuming compilation
instead of silently skipping affected modules or emitting invalid generated C#.

#### Scenario: Unsupported parameter type is used
- **GIVEN** a `[JS]` method has an unsupported parameter type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported type
- **AND** generated runtime glue SHALL NOT attempt dynamic invocation

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
- **GIVEN** the React Native macOS or Windows proof app uses the `hostfxr`
  loader
- **WHEN** the dotnet autolinking CLI stages the generated aggregator
- **THEN** it SHALL stage the managed assembly, runtime config, dependency
  file, managed bridge assemblies, and platform `nethost` runtime library into
  the app-owned `Managed` location
- **AND** manual app-local HostFXR staging scripts SHALL NOT be required

#### Scenario: Mobile app stages NativeAOT artifacts through the CLI
- **GIVEN** the React Native iOS or Android proof app uses the `nativeaot`
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
