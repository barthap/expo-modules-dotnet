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
JavaScript field. A missing lower-camel field SHALL read as JavaScript
`undefined`. Generated decode SHALL then apply "Record Fields With Defaults Are
Optional In JavaScript" and "Required Record Fields Name The Missing Field".

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
- **THEN** a required field SHALL fail with the required-field error that names
  the lower-camel field, through the normal catchable JavaScript error path
- **AND** a nullable field without a default SHALL decode as `null`
- **AND** a field with a C# default SHALL decode as that default

### Requirement: Record Fields With Defaults Are Optional In JavaScript

A generated record codec SHALL treat a positional record constructor parameter
that has a C# default value as an optional JavaScript field. When the field
reads as JavaScript `undefined`, because the key is absent or holds explicit
`undefined`, generated decode SHALL use the authored default and SHALL NOT call
the field codec. Any other value, including JavaScript `null`, SHALL decode
through the field codec. The generator SHALL inline the default as a
compile-time C# expression, without reflection, boxing or dynamic invocation.
Both generated `Decode` overloads SHALL behave the same. Encoding SHALL write
every field, including a field whose value equals its default. A record field
that has a default and whose type is `JavaScriptValue`, or a `record struct`
that holds a `JavaScriptValue`, SHALL be a build error: the generator SHALL
report `EXPOJSI007` with a message that says the field cannot have a default
value, and SHALL NOT emit a codec for the record. A method parameter of type
`JavaScriptValue` with a default SHALL keep its current behavior.

#### Scenario: Omitted or undefined defaulted field uses the default
- **GIVEN** a record `Options(string Name, int Count = 5)` is a `[JS]` method
  parameter
- **WHEN** JavaScript passes `{ name: 'a' }` or `{ name: 'a', count: undefined }`
- **THEN** authored code SHALL receive `Count == 5`

#### Scenario: Supplied value wins over the default
- **GIVEN** the same record
- **WHEN** JavaScript passes `{ name: 'a', count: 41 }`
- **THEN** authored code SHALL receive `Count == 41`

#### Scenario: Explicit null on a nullable defaulted field stays null
- **GIVEN** a record field `string? Label = "x"`
- **WHEN** JavaScript passes `{ label: null }`, and then an object without
  `label`
- **THEN** authored code SHALL receive `null` for the first call
- **AND** it SHALL receive `"x"` for the second call

#### Scenario: Explicit null on a non-nullable defaulted field fails
- **GIVEN** a record field `int Count = 5`
- **WHEN** JavaScript passes `{ count: null }`
- **THEN** the call SHALL fail through the field codec with a catchable
  JavaScript `Error`
- **AND** authored code SHALL NOT run

#### Scenario: Enum and value-type defaults
- **GIVEN** a `readonly record struct Settings(Mode Mode = Mode.Fast, TimeSpan
  Delay = default, Mode? Fallback = Mode.Slow)`
- **WHEN** JavaScript passes `{}`
- **THEN** authored code SHALL receive `Mode.Fast`, `TimeSpan.Zero` and
  `Mode.Slow`

#### Scenario: Owned decode path applies the default
- **GIVEN** a `[JS]` method takes `JavaScriptCallback<Options>`
- **WHEN** the JavaScript callback returns `{ name: 'a' }` and authored code
  calls `Invoke()`
- **THEN** `Invoke()` SHALL return a record with `Count == 5`

#### Scenario: Encoding writes every field
- **GIVEN** an authored method returns `new Options("a")`
- **WHEN** JavaScript reads the result
- **THEN** the object SHALL have own properties `name` and `count`, with
  `count === 5`

#### Scenario: Defaulted leaf field is released exactly once
- **GIVEN** a record `Blob(string Name, ArrayBuffer? Data = null)` is a `[JS]`
  method parameter
- **WHEN** JavaScript passes `{ name: 'a' }`, and then
  `{ name: 'a', data: buffer }`
- **THEN** authored code SHALL receive `null` for the first call, and no leaf
  SHALL be released
- **AND** for the second call the decoded buffer SHALL be released exactly once
  after the call

#### Scenario: Default on a JavaScriptValue record field is a build error
- **GIVEN** a record `Holder(string Name, JavaScriptValue V = null)`, and a
  record `Wrapper(Slot Slot = default)` where `Slot` is a `record struct` that
  holds a `JavaScriptValue`
- **WHEN** the generator runs on a module with a `[JS]` method that takes each
  record
- **THEN** it SHALL report `EXPOJSI007` for `V` with the message
  `Record 'Holder' field 'V' of type 'global::Expo.JSI.JavaScriptValue' cannot have a default value`
- **AND** it SHALL report `EXPOJSI007` for `Slot` with the message
  `Record 'Wrapper' field 'Slot' of type '<Slot type>' cannot have a default value because the type holds a JavaScriptValue`,
  where `<Slot type>` is the `global::`-qualified name of `Slot`
- **AND** it SHALL NOT emit a record codec for either record

#### Scenario: Unsupported field type message does not change
- **GIVEN** a record `Bad(decimal Value)` used by a `[JS]` method
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI007` with the message
  `Record 'Bad' field 'Value' uses unsupported type 'System.Decimal'`

#### Scenario: JavaScriptValue field without a default and method parameter default stay accepted
- **GIVEN** a record `Holder(string Name, JavaScriptValue V)` and a `[JS]`
  method `Run(JavaScriptValue value = null)`
- **WHEN** the generator runs
- **THEN** it SHALL report no diagnostic
- **AND** an omitted `v` field SHALL hold a JavaScript `undefined` value

#### Scenario: Generated decode inlines the default
- **GIVEN** a record field `int Count = 5`
- **WHEN** the generator emits the record codec
- **THEN** both `Decode` overloads SHALL contain a `JavaScriptValueKind.Undefined`
  check that yields the literal `5`
- **AND** the generated source SHALL NOT use reflection to read the default

### Requirement: Required Record Fields Name The Missing Field

A generated record field SHALL be required when it has no C# default and its C#
type is not a nullable value type, not an annotated nullable reference type,
and not `JavaScriptValue`. When a required field reads as JavaScript
`undefined`, generated decode SHALL throw before it calls the field codec. The
message SHALL be `Value for property '<field>' is required, got undefined`,
where `<field>` is the lower-camel JavaScript name. The failure SHALL reach
JavaScript through the normal path for its position: a sync `Error`, an async
rejection, or a failed callback `Invoke()`. Leaves that earlier fields already
decoded SHALL be released once. Explicit `null` on a required field SHALL decode
through the field codec. A field that is not required and has no default SHALL
keep its codec's behavior for `undefined`.

#### Scenario: Missing required field is named
- **GIVEN** a record `CodecUser(string Name, int Age)` is a `[JS]` method
  parameter
- **WHEN** JavaScript passes `{ age: 37 }`
- **THEN** JavaScript SHALL catch an `Error` whose message contains
  `Value for property 'name' is required, got undefined`
- **AND** authored code SHALL NOT run

#### Scenario: Async method rejects on a missing required field
- **GIVEN** an async `[JS]` method takes the same record
- **WHEN** JavaScript passes `{ age: 37 }`
- **THEN** the call SHALL return a promise that rejects with an `Error` whose
  message contains `Value for property 'name' is required`
- **AND** the call SHALL NOT throw synchronously

#### Scenario: Nullable and JavaScriptValue fields stay optional
- **GIVEN** records with an `int? LuckyNumber` field and a `JavaScriptValue
  Value` field, neither with a default
- **WHEN** JavaScript omits each field
- **THEN** `LuckyNumber` SHALL decode as `null`
- **AND** `Value` SHALL hold a JavaScript `undefined` value

#### Scenario: Earlier leaf is released when a later required field is missing
- **GIVEN** a record `CountedBuffer(ArrayBuffer Buffer, int Count)` is a `[JS]`
  method parameter
- **WHEN** JavaScript passes `{ buffer: b }`
- **THEN** the call SHALL fail with the required-field error naming `count`
- **AND** the decoded buffer SHALL be released exactly once

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
- **AND** generated dispatch SHALL release it in `finally`, after the result is
  encoded or after a failure
- **AND** authored module code SHALL NOT dispose or store the argument wrapper

#### Scenario: Generated sync function returns JavaScriptValue
- **GIVEN** a generated sync function returns `JavaScriptValue`
- **WHEN** JavaScript calls that function
- **THEN** ownership of the returned wrapper SHALL pass to generated glue
- **AND** generated glue SHALL return a retained copy to the host-function
  bridge and release the returned wrapper once

#### Scenario: Module author returns a retained JavaScriptValue copy
- **GIVEN** authored module code owns a `JavaScriptValue`
- **WHEN** it needs to keep that original wrapper or dispose it locally
- **THEN** it SHALL return an explicit retained copy
- **AND** ownership of the retained copy SHALL transfer to generated glue
- **AND** ownership of the original wrapper SHALL remain with the module author

#### Scenario: Enum values use generated codecs
- **GIVEN** a generated function, property, record, collection, callback,
  event, or union uses a C# enum
- **WHEN** the enum's effective representation is `String`
- **THEN** generated dispatch SHALL decode and encode it through a generated
  enum codec, as defined in "String Enums Cross As Declared JavaScript Values"
- **AND** when the effective representation is `Number`, it SHALL decode and
  encode the underlying value through `NumberEnumCodec<T>`

#### Scenario: Usage-level enum representation wins
- **GIVEN** `[JSEnum(EnumRepresentation.Number)] enum Mode` and a parameter
  `[JSEnum(EnumRepresentation.String)] Mode mode`
- **WHEN** JavaScript calls the method with `'Fast'`
- **THEN** the parameter SHALL decode as a string
- **AND** a record field of type `Mode` SHALL still cross as a number, because
  a field has no usage-level attribute

#### Scenario: Usage-level representation reaches a nullable enum
- **GIVEN** `enum Mode { Slow, Fast }` and a parameter
  `[JSEnum(EnumRepresentation.Number)] Mode? mode`
- **WHEN** JavaScript passes `1` and `null`
- **THEN** authored code SHALL receive `Mode.Fast` and `null`

#### Scenario: Simple records use generated codecs
- **GIVEN** a generated sync function accepts or returns a positional C#
  `record`, `record class`, or `record struct`
- **WHEN** JavaScript passes or receives a plain object
- **THEN** generated dispatch SHALL convert known fields through generated
  field codecs
- **AND** construct records through direct constructor calls
- **AND** simple nested records SHALL compose through generated field codecs

#### Scenario: Record with wrapper fields crosses the boundary
- **GIVEN** a record with an `ArrayBuffer` and a `JavaScriptValue` field is a
  parameter or a return
- **WHEN** JavaScript calls the method
- **THEN** each decoded or returned leaf SHALL release exactly once
- **AND** no returned leaf SHALL be disposed before it is encoded

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
- **AND** a callback argument or result type MAY hold leaves at any depth

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

### Requirement: String Enums Cross As Declared JavaScript Values

The effective representation of an enum at a codec position SHALL be the
usage-level `[JSEnum]` on the parameter or return value when present, else the
enum type's `[JSEnum]` when present, else `String`. The usage-level attribute
SHALL apply to the enum inside `Nullable<TEnum>` at that position, and a
return attribute SHALL apply to the result of a `Task<T>` return value, with or
without `Nullable<TEnum>` inside it. It SHALL NOT reach into a collection,
record, callback, event, or union.

Each member of a string enum SHALL have one JavaScript string: the `Value` of
its `System.Runtime.Serialization.EnumMemberAttribute` when the attribute sets
`Value`, otherwise its C# member name verbatim. The generator SHALL read these
strings at build time, from source or from metadata, and SHALL emit one private
codec per string enum in each provider. The codec SHALL use constant `switch`
expressions only, with no runtime reflection, `Enum.TryParse`,
`Enum.IsDefined`, or `Enum.ToString`.

Decode SHALL accept exactly the declared strings, compared by ordinal and
case-sensitively. Any other string SHALL throw `ArgumentException` with the
message below, where the received and declared strings appear verbatim, the
declared strings are in declaration order, and `<Enum>` is the enum's full C#
name without `global::`, with `.` before a nested enum:

`'<value>' is not a valid <Enum> value. It must be one of: '<s1>', '<s2>', ...`

A value that is not a string SHALL fail as it does for `string`. Encode SHALL
produce the member's declared string. A value that names no member, including a
`[Flags]` combination, SHALL throw `InvalidOperationException` with the message
`<number> is not a member of <Enum>, so it has no JavaScript value.`, where the
number is the underlying value in the invariant culture. These errors SHALL have
no code: JavaScript SHALL receive them as catchable `Error`s with no `code`
property from sync members, and as rejected promises from async members.

The generated enum codec SHALL compose in every codec position where an enum is
accepted, SHALL hold no leaves, and SHALL need no runtime context. As a union
payload, a string enum SHALL have the kind `String`; a string it does not
declare SHALL fail that case's attempt, so decoding moves to the next case.

#### Scenario: Declared strings cross both ways
- **GIVEN** `enum DigestAlgorithm { MD5, [EnumMember(Value = "SHA-1")] SHA1 }`
  and `[JS] DigestAlgorithm Echo(DigestAlgorithm algorithm)`
- **WHEN** JavaScript calls `echo('SHA-1')` and `echo('MD5')`
- **THEN** authored code SHALL receive `SHA1` and `MD5`
- **AND** JavaScript SHALL receive `'SHA-1'` and `'MD5'`

#### Scenario: Only declared strings decode
- **GIVEN** the same enum
- **WHEN** JavaScript passes `'SHA1'`, `'sha-1'`, `' SHA-1'`, `'1'`, or `1`
- **THEN** the call SHALL throw a catchable `Error` with no `code` before
  authored code runs
- **AND** for each string, the message SHALL contain the received string,
  `DigestAlgorithm`, `'MD5'`, and `'SHA-1'`

#### Scenario: Number strings no longer decode plain enums
- **GIVEN** `enum Mode { Slow, Fast }` with no attributes
- **WHEN** JavaScript passes `'Fast'` and then `'0'`
- **THEN** `'Fast'` SHALL decode to `Mode.Fast`
- **AND** `'0'` SHALL throw a catchable `Error`

#### Scenario: A value with no member does not encode
- **GIVEN** a method that returns `(DigestAlgorithm)42`, and a method that
  returns the `[Flags]` combination `Upper | Spaced` of a string enum
- **WHEN** JavaScript calls them
- **THEN** each call SHALL throw a catchable `Error` whose message contains the
  number (`42`, `3`) and the enum's name

#### Scenario: Async functions reject for a bad string
- **GIVEN** `[JS] Task<string> HashAsync(DigestAlgorithm algorithm)`
- **WHEN** JavaScript calls it with `'MD2'`
- **THEN** it SHALL return a rejected promise and SHALL NOT throw synchronously

#### Scenario: Declared strings in composite positions
- **GIVEN** echo methods for `record Options(DigestAlgorithm Algorithm)`,
  `IReadOnlyList<DigestAlgorithm>`, `DigestAlgorithm?`, and
  `Dictionary<string, DigestAlgorithm>`
- **WHEN** JavaScript passes `{ algorithm: 'SHA-1' }`, `['SHA-1', 'MD5']`,
  `'SHA-1'`, `null`, and `{ a: 'SHA-1' }`
- **THEN** each call SHALL return the same JavaScript value it received, with
  the declared strings
- **AND** `null` SHALL reach authored code as `null`

#### Scenario: Declared strings in other positions
- **GIVEN** a `DigestAlgorithm` property, a getter-only constant, an async
  method returning `Task<DigestAlgorithm>`, a callback that receives a
  `(DigestAlgorithm, int)` argument tuple and returns a `DigestAlgorithm`, a
  typed event whose payload is a `DigestAlgorithm` and one whose payload record
  has a `DigestAlgorithm` field, and a shared object whose constructor, method,
  and property take or return `DigestAlgorithm`
- **WHEN** each crosses with the member `SHA1`
- **THEN** JavaScript SHALL see `'SHA-1'` in each, and `'SHA-1'` passed from
  JavaScript SHALL reach authored code as `SHA1`

#### Scenario: Defaults are members
- **GIVEN** `[JS] DigestAlgorithm Pick(DigestAlgorithm algorithm = DigestAlgorithm.SHA1)`
  and `record Settings(string Label, DigestAlgorithm Algorithm = DigestAlgorithm.SHA1)`
- **WHEN** JavaScript omits the argument and the field
- **THEN** authored code SHALL receive `SHA1`, rendered as a cast of its
  underlying value and never decoded from a string
- **AND** JavaScript SHALL receive `'SHA-1'` back

#### Scenario: String enums as union payloads
- **GIVEN** a union with cases `Algorithm(DigestAlgorithm)`,
  `Output(DigestEncoding)` where `DigestEncoding` declares `'hex'` and
  `'base64'`, and `Text(string)`
- **WHEN** JavaScript passes `'SHA-1'`, `'hex'`, and `'SHA1'`
- **THEN** they SHALL decode as `Algorithm(SHA1)`, `Output(Hex)`, and
  `Text("SHA1")`
- **AND** a union with only the two enum cases SHALL reject `'nope'` with
  `UnionCaseMismatchException` whose cause is the `DigestEncoding` decode error

#### Scenario: Number enums do not change
- **GIVEN** `[JSEnum(EnumRepresentation.Number)] enum Level { [EnumMember] Low = 1, High = 2 }`
- **WHEN** JavaScript passes `2`
- **THEN** authored code SHALL receive `Level.High` and JavaScript SHALL receive
  `2` back

#### Scenario: One codec per enum per provider
- **GIVEN** two modules and a shared object in one assembly that all use
  `DigestAlgorithm` in several positions
- **WHEN** the project is compiled
- **THEN** the provider SHALL contain exactly one generated codec for
  `DigestAlgorithm`
- **AND** it SHALL contain no `Enum.TryParse` or `Enum.IsDefined`

#### Scenario: An enum from a referenced assembly
- **GIVEN** a string enum with `[EnumMember(Value = "SHA-1")]` declared in a
  referenced assembly
- **WHEN** a module in the consuming project uses it
- **THEN** the generated codec SHALL use `'SHA-1'`, read from metadata

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
- **AND** a returned value that holds a leaf SHALL follow the same rule as a
  synchronous `[JS]` method return

#### Scenario: Setter receives JavaScriptValue
- **GIVEN** a generated property setter accepts `JavaScriptValue`
- **WHEN** JavaScript assigns the property
- **THEN** generated glue SHALL retain and own the decoded wrapper for the
  synchronous setter invocation
- **AND** it SHALL release that wrapper after the authored setter returns or
  throws
- **AND** authored code SHALL NOT dispose or store the invocation-owned wrapper
- **AND** authored code that needs the value later SHALL store and eventually
  dispose an explicit retained copy

#### Scenario: Getter returns a retained JavaScriptValue copy
- **GIVEN** authored module state owns a stored `JavaScriptValue`
- **WHEN** a generated property getter exposes that value
- **THEN** the getter SHALL return an explicit retained copy whose ownership
  passes to generated glue
- **AND** generated glue SHALL return its own retained copy and release the
  getter's copy once
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
- **THEN** ownership of the returned wrapper SHALL pass to generated glue
- **AND** the runtime factory SHALL encode a retained copy and then release
  the returned wrapper and the leaf-bearing arguments
- **AND** abandoning the result SHALL release them without encoding

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

### Requirement: Coded Exceptions

`Expo.ModulesCore` SHALL expose a public, non-sealed
`CodedException : Exception` with constructors
`(string message, Exception? innerException = null)` and
`(string? code, string message, Exception? innerException = null)`, and a
virtual `string Code`. `Code` SHALL return a non-null explicit code as given,
and otherwise the code derived from `GetType().Name`: remove a generic arity
suffix, remove one trailing `Error` or `Exception`, insert `_` per the
`(.)([A-Z])` to `$1_$2` replacement, uppercase invariantly, and prefix `ERR_`.
Derivation SHALL run only when `Code` is read and SHALL NOT use reflection
beyond `GetType().Name`. `Message` SHALL NOT include the cause chain.

#### Scenario: Code is derived from the class name
- **GIVEN** `class InvalidPathException : CodedException` constructed with a
  message only
- **WHEN** `Code` is read
- **THEN** it SHALL be `ERR_INVALID_PATH`

#### Scenario: Derivation keeps upstream edge cases
- **GIVEN** coded exception types named `NotFoundError`, `HTTPException`,
  `PathException<T>`, a nested `Outer.InvalidPathException`, and
  `CodedException` itself
- **WHEN** `Code` is read on each
- **THEN** the codes SHALL be `ERR_NOT_FOUND`, `ERR_H_TT_P`, `ERR_PATH`,
  `ERR_INVALID_PATH`, and `ERR_CODED`

#### Scenario: Explicit code
- **GIVEN** `new CodedException("ERR_CUSTOM", "m")`
- **WHEN** `Code` is read
- **THEN** it SHALL be `ERR_CUSTOM`

#### Scenario: Null explicit code derives
- **GIVEN** a subclass passes a null code to the three-argument constructor
- **WHEN** `Code` is read
- **THEN** it SHALL be the derived code

#### Scenario: Same code under both loaders
- **GIVEN** the console showcase throws a coded exception
- **WHEN** it runs under the hostfxr loader and under the NativeAOT loader
- **THEN** JavaScript SHALL read the same `code` in both runs

### Requirement: Module Failures Become JavaScript Errors

Every JavaScript-callable function that `Expo.ModulesCore` creates SHALL convert
a managed exception through `GeneratedFunction.CreateJavaScriptError`. The
result SHALL be a JavaScript `Error`. Its `message` SHALL be the thrown
exception's `Message`, followed, for each exception in its `InnerException`
chain from the direct inner exception to the root, by `\n→ Caused by: ` and that
exception's `Message`. When the thrown exception is a `CodedException` whose
`Code` is not null or empty, the `Error` SHALL have a string `code` property
with that value; otherwise it SHALL have no `code` property. The converter SHALL
NOT add the .NET type name, the stack trace, or the code of a cause to the
message, and SHALL NOT filter, shorten, or limit the chain. A module that must
keep data out of JavaScript SHALL keep it out of every exception in the chain it
throws.

#### Scenario: Sync member throws a coded exception
- **GIVEN** a sync module method, property getter, property setter,
  shared-object method, or shared-object constructor throws
  `InvalidPathException("bad path")`
- **WHEN** JavaScript calls it
- **THEN** JavaScript SHALL catch an `Error` with `e instanceof Error`,
  `e.code === 'ERR_INVALID_PATH'`, and `e.message === 'bad path'`

#### Scenario: Plain exception has no code
- **GIVEN** a module member throws `InvalidOperationException("plain")`
- **WHEN** JavaScript calls it
- **THEN** `'code' in e` SHALL be `false`
- **AND** `e.message` SHALL be `plain`

#### Scenario: Empty or overridden code
- **GIVEN** a coded exception whose `Code` returns an empty string, and another
  whose overridden `Code` and `Message` return `ERR_REPHRASED` and `rephrased`
- **WHEN** JavaScript calls members that throw them
- **THEN** the first `Error` SHALL have no `code` property
- **AND** the second SHALL have `code === 'ERR_REPHRASED'` and
  `message === 'rephrased'`

#### Scenario: Cause chain is appended
- **GIVEN** a member throws
  `new CodedException("outer", new IOException("inner"))`
- **WHEN** JavaScript calls it
- **THEN** `e.message` SHALL be `'outer\n→ Caused by: inner'`
- **AND** `e.code` SHALL be `ERR_CODED`

#### Scenario: Chain walks to the root through coded and plain causes
- **GIVEN** a member throws
  `new InvalidOperationException("outer", new InvalidPathException("middle", new IOException("root")))`
- **WHEN** JavaScript calls it
- **THEN** `e.message` SHALL be
  `'outer\n→ Caused by: middle\n→ Caused by: root'`
- **AND** `'code' in e` SHALL be `false`, because only the thrown exception
  gives the code

#### Scenario: Overridden message keeps the chain
- **GIVEN** a coded exception whose overridden `Message` returns `rephrased` and
  whose inner exception has the message `inner`
- **WHEN** JavaScript calls a member that throws it
- **THEN** `e.message` SHALL be `'rephrased\n→ Caused by: inner'`

#### Scenario: Sync and async give the same error
- **GIVEN** the same exception type, message, and inner exception thrown from a
  sync member and from an async member's task
- **WHEN** JavaScript catches the throw and the rejection
- **THEN** both errors SHALL have equal `code` presence, `code`, and `message`,
  cause chain included

#### Scenario: Core functions follow the same contract
- **GIVEN** an `EventEmitterPrototype` function, a subscription `remove`
  function, or a shared-object `release` function throws
- **WHEN** JavaScript calls it
- **THEN** the function SHALL convert the exception through
  `GeneratedFunction.CreateJavaScriptError`
- **AND** JavaScript SHALL catch an `Error` whose message holds no .NET type
  name or stack trace

#### Scenario: Converter failure falls back
- **GIVEN** `CreateJavaScriptError` throws while converting a sync member
  failure
- **WHEN** control returns to native
- **THEN** JavaScript SHALL still catch an `Error`, built from the original
  exception's message result, which is its `ToString()` text and so holds the
  .NET type name and the stack trace

### Requirement: Async Function Failures Reject Promises

Generated async function glue SHALL reject the returned Promise for generated
dispatch failures, authored-method failures, faulted tasks, canceled tasks, and
result-encoding failures. Every rejection reason SHALL come from
`GeneratedFunction.CreateJavaScriptError`, so it follows "Module Failures Become
JavaScript Errors", cause chain included.

#### Scenario: Argument validation fails
- **GIVEN** JavaScript calls a generated async function with an unsupported
  argument count or value
- **WHEN** generated dispatch validates or decodes the arguments
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error` without a `code`
  property
- **AND** the validation or codec failure SHALL NOT escape as a synchronous
  JavaScript throw

#### Scenario: Authored async method throws before returning a task
- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored method throws a `CodedException` before returning its
  task
- **THEN** the generated function SHALL return a JavaScript Promise
- **AND** the Promise SHALL reject with a JavaScript `Error` that carries the
  exception's `code` and its message with the cause chain

#### Scenario: Authored async task faults
- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored task faults after an `await` with a `CodedException`
- **THEN** the Promise SHALL reject with a JavaScript `Error` that carries the
  exception's `code` and its message with the cause chain

#### Scenario: Authored async task is canceled
- **GIVEN** JavaScript calls a generated async function
- **WHEN** the authored task is canceled
- **THEN** the Promise SHALL reject with a JavaScript `Error` without a `code`
  property

#### Scenario: Result encoding fails
- **GIVEN** a generated async function's task completes
- **WHEN** encoding its result throws on the runtime path
- **THEN** the Promise SHALL reject with the converted `Error`

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

#### Scenario: Decode-only codec is used where a value is encoded
- **GIVEN** a `[JS]` method returns `JavaScriptCallback<...>` directly, as a
  `Task` result, or from a span function
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI002` naming the method and type,
  or `EXPOJSI023` on a shared-object class
- **AND** generated source SHALL NOT call a missing `Encode` method

#### Scenario: Static helper codec is used as a generic codec argument
- **GIVEN** a list element, dictionary value, `Nullable<T>` inner type,
  callback result, or callback-argument tuple element is a
  `JavaScriptCallback` or a type that holds one, a nullable-reference inner
  type holds a `JavaScriptCallback` inside a list, dictionary, or record, or
  the annotated type is `JavaScriptValue?`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report the using member's unsupported-type
  diagnostic: `EXPOJSI001`, `EXPOJSI002`, `EXPOJSI008`, `EXPOJSI015`, or
  `EXPOJSI023`
- **AND** generated source SHALL NOT pass a static codec class as a type
  argument
- **AND** a type that holds a leaf is not in this scenario in any of these
  positions

#### Scenario: Record field needs a runtime context
- **GIVEN** a generated record has a `JavaScriptCallback` field, or a field
  whose codec the generic-argument rule rejects
- **WHEN** a `[JS]` method parameter, return, async result, or shared-object
  constructor parameter uses the record
- **THEN** the generator SHALL report `EXPOJSI007` naming the record and field
- **AND** the using member SHALL report its unsupported-type diagnostic
- **AND** a `[JS]` property whose type contains a callback SHALL keep
  reporting only `EXPOJSI015`

#### Scenario: Async method returns a byte span
- **GIVEN** an async `[JS]` method returns `Task<Span<byte>>` or
  `Task<ReadOnlySpan<byte>>`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI002` naming the method and type
- **AND** the generator SHALL NOT fail or drop its other output

### Requirement: Invalid String Enum Declarations Are Build Diagnostics

The generator SHALL report, at error severity, for each enum that reaches a
codec position, including a union payload and a typed event payload:
`EXPOJSI033` for an `EnumMember` `Value` that is `null`, empty, or holds a lone
surrogate, for an `EnumMember` `Value` on an enum whose own `[JSEnum]` is
`Number`, and, at the parameter or return value, for a usage-level
`[JSEnum(EnumRepresentation.Number)]` on an enum where a member sets `Value`;
`EXPOJSI034` for two members of a string enum that cross as the same string by
ordinal comparison; `EXPOJSI035` for two members of a string enum with the same
underlying value. `[EnumMember]` without `Value` SHALL NOT be reported. The
string checks (an invalid `Value`, `EXPOJSI034`, and `EXPOJSI035`) SHALL run
only where the enum crosses as a string, so a number enum MAY alias members.

A diagnostic about a member of an enum declared in the compiling assembly SHALL
be reported at the member. For an enum from a referenced assembly it SHALL be
reported at the using parameter, return, property, record field, or event, and
SHALL name the enum's assembly. A pair diagnostic SHALL be reported at the later
member and name the earlier one, and a pair with the same underlying value SHALL
report only `EXPOJSI035`. The provider SHALL report equal diagnostics with these
IDs (same ID, location, and arguments) once, so each is reported once per
declaration however many positions use the enum, and once per using position
for an enum from a referenced assembly. The `[Union]` declaration pipeline SHALL
NOT report them; a union case whose payload is such an enum SHALL report
`EXPOJSI030` with a reason that names the enum and these IDs. An enum with any
of these errors SHALL resolve to no codec where the error applies, so the using
position SHALL also report its existing unsupported-type diagnostic, and no
binding SHALL be emitted for it.

#### Scenario: Invalid values report EXPOJSI033
- **GIVEN** string enum members with `[EnumMember(Value = "")]`,
  `[EnumMember(Value = null)]`, and `[EnumMember(Value = "\uD800")]`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI033` once for each member, naming
  the enum, the member, and the reason

#### Scenario: Values on a number enum report EXPOJSI033
- **GIVEN** `[JSEnum(EnumRepresentation.Number)] enum Level { [EnumMember(Value = "low")] Low }`
  used by a method
- **AND** a string enum with `[EnumMember(Value = "SHA-1")]` used by a parameter
  with `[JSEnum(EnumRepresentation.Number)]`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI033` at `Low` and at the parameter
- **AND** a parameter of `Level` with `[JSEnum(EnumRepresentation.String)]`
  SHALL still have no codec, and a string parameter of the string enum SHALL
  still bind

#### Scenario: Duplicate strings report EXPOJSI034
- **GIVEN** `enum E { [EnumMember(Value = "B")] A, B }`
- **WHEN** a method uses `E`
- **THEN** the generator SHALL report `EXPOJSI034` once, naming `A`, `B`, and
  `"B"`

#### Scenario: Aliased members report EXPOJSI035
- **GIVEN** `enum Mode { Slow, Fast, Default = Fast }` as a string enum
- **WHEN** a method uses `Mode`
- **THEN** the generator SHALL report `EXPOJSI035` once, naming `Fast`,
  `Default`, and the value `1`
- **AND** the same enum with `[JSEnum(EnumRepresentation.Number)]` SHALL report
  nothing, unless a parameter uses it with
  `[JSEnum(EnumRepresentation.String)]`

#### Scenario: One report for many uses
- **GIVEN** an enum with a duplicate string, used by two modules, a record
  field, a list, a union payload, and a shared-object method
- **WHEN** the project is compiled
- **THEN** `EXPOJSI034` SHALL be reported exactly once

#### Scenario: A bad enum from a referenced assembly
- **GIVEN** a referenced assembly declares a string enum with two members that
  share one string
- **WHEN** a consuming module uses it as a parameter
- **THEN** the generator SHALL report `EXPOJSI034` at the parameter, naming the
  enum and its assembly
- **AND** the parameter SHALL report `EXPOJSI001`, and no binding SHALL be
  emitted for the method

#### Scenario: A bad enum as a union payload
- **GIVEN** a union case `Pick(Bad Value)` where `Bad` has a duplicate string
- **WHEN** a method uses the union
- **THEN** the case SHALL report `EXPOJSI030` naming `Bad` and `EXPOJSI034`,
  `EXPOJSI034` SHALL be reported once, and the method SHALL report `EXPOJSI001`
- **AND** when no member uses the union, only `EXPOJSI030` SHALL be reported

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

### Requirement: Generator Conversion Policy Is Typed

Generator analysis and emission SHALL read decode operation, runtime-context
requirement, resource ownership, capabilities, and codec shape from typed
`ExpoCodecDescriptor` fields. Production generator code SHALL
NOT compare, prefix-match, or parse emitted codec-expression text to make a
conversion decision.

#### Scenario: Codec text is opaque
- **GIVEN** the generator source under `Expo.ModulesCore.Generator`
- **WHEN** a reviewer runs
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- **THEN** it SHALL print no matches

### Requirement: Codecs Release Leaves Through One Protocol

The leaf and the codec contract are defined in `### Requirement: Boundary Leaf
Ownership Is Explicit` in `ownership-and-scoped-refs.md`.

`IJavaScriptCodec<T>` SHALL declare `static virtual void Release(T value)`
with an empty default body. There SHALL be no second codec protocol.
`JavaScriptValueCodec`, `ArrayBufferCodec`, and `Uint8ArrayCodec` SHALL
dispose the leaf.
`JavaScriptValueCodec.Encode` SHALL return `value.Retain()`.

Every list, dictionary, nullable, generated union, and generated record codec
whose content holds a leaf SHALL release each child through the child codec. A
generated union codec SHALL release only the payload of the case it holds. A
dictionary codec SHALL do this through both dictionary interfaces. A composite
with more than one child SHALL attempt every child. It SHALL throw one
`AggregateException` with every failure, through the public `CodecRelease`
helper.

A list, dictionary, or record decode that fails at a child SHALL release the
children it already decoded and rethrow. If that cleanup throws, its
`AggregateException` SHALL replace the decode error. A generated union decode
SHALL rely on this rule for each failed attempt, SHALL propagate an
`AggregateException` from an attempt, and SHALL release a decoded payload whose
case construction throws.

No codec SHALL use finalizers, reflection, or ambient state to release.

Every generated record codec SHALL use one `Encode` text with no null guard.
Encoding a null class record SHALL fail with a catchable JavaScript error.
`Release` SHALL skip a null record. `JavaScriptCallbackCodec`,
`NullableJavaScriptCallbackCodec`, `SharedObjectCodec<T>`,
`NullableSharedObjectCodec<T>`, and `SharedObjectBaseCodec<TBase>` are static
classes with no `Release`: the runtime context retains callbacks and the
registry owns shared objects.

#### Scenario: A missing override leaks
- **GIVEN** a composite codec over `ArrayBufferCodec`, holding JS-created
  buffers
- **WHEN** a test calls `Release` through the generic `TCodec.Release`
- **THEN** each buffer entry SHALL release exactly once
- **AND** the test SHALL fail if that composite used the empty default

#### Scenario: Release failure is aggregated
- **GIVEN** a list whose second element's release throws
- **WHEN** the list codec releases it
- **THEN** the first and third elements SHALL still release
- **AND** the codec SHALL throw `AggregateException` with that failure

#### Scenario: Later element decode fails
- **GIVEN** a JavaScript array whose first element is an `ArrayBuffer` and
  whose second element is not
- **WHEN** `JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>` decodes it
- **THEN** the first lease SHALL release exactly once
- **AND** the decode error SHALL propagate

#### Scenario: Encode leaves the source usable
- **GIVEN** an author-owned `JavaScriptValue` inside a list
- **WHEN** `JavaScriptArrayCodec<JavaScriptValue, JavaScriptValueCodec>`
  encodes the list
- **THEN** the author's wrapper SHALL stay usable
- **AND** the encoder SHALL release only its own copy

#### Scenario: Null record in a returned list
- **GIVEN** a returned list holds a null class record and records with leaves
- **WHEN** generated glue encodes and releases it
- **THEN** the call SHALL fail with a catchable JavaScript error
- **AND** the leaves of the other records SHALL release exactly once

### Requirement: Generated Bindings Release Leaves After Each Call

Generated bindings with no `HoldsLeaves` argument or result SHALL keep their
generated text. The generator SHALL emit `Release` only for a `HoldsLeaves`
descriptor.

Sync: a leaf-bearing sync function, property getter or setter, shared-object
member, shared-object constructor factory, or span function SHALL declare its
leaf-bearing arguments and result before one `try`. Its `finally` SHALL release
each of them once. An argument that never decoded is still default and
releases nothing. The span lease SHALL keep its own `using`.

Async: the outer `catch` SHALL release leaf-bearing arguments until
`CreatePromise` returns. After that, only the Promise lambda SHALL release
them. With a leaf-free result, the lambda SHALL release them when the `Task`
completes. A leaf-bearing result SHALL travel to the factory in a `StrongBox`
with the leaf-bearing arguments. The factory SHALL release them after it
encodes, and abandon SHALL release them without encoding. A fault or cancel
SHALL release them in the lambda's `catch`.

A `Release` failure SHALL replace the original outcome. In a sync `finally` it
replaces the result or the authored exception. In the async outer `catch` the
host function throws it instead of returning a rejected Promise.

`ExpoResourceOwnership` is `Borrowed`, `HoldsLeaves`, or `ContextRetained`.
`ExpoCodecShape` is `JavaScriptCodec` or `StaticHelper`. `ExpoCodecDescriptor`
SHALL carry `ValueTypeName`, the codec's `T` with its nullable annotation, for
local declarations and helper type arguments.

| Branch | Shape | ResourceOwnership |
| --- | --- | --- |
| Leaf-free types | as before | `Borrowed` |
| `JavaScriptValue`, `ArrayBuffer`, `Uint8Array` | `JavaScriptCodec` | `HoldsLeaves` |
| List, dictionary, or nullable over a `HoldsLeaves` child | `JavaScriptCodec` | `HoldsLeaves` |
| `[Union]` type with any `HoldsLeaves` payload | `JavaScriptCodec` | `HoldsLeaves` |
| Record with any `HoldsLeaves` field | `JavaScriptCodec` | `HoldsLeaves` |
| `JavaScriptCallback<...>` and `JavaScriptCallback<...>?` | `StaticHelper` | `ContextRetained` |
| Direct `[ExpoSharedObject]` class `T` and `T?` | `StaticHelper` | `Borrowed` |
| Authored abstract base `TBase` (parameter only; decode-only `SharedObjectBaseCodec<TBase>`) | `StaticHelper` | `Borrowed` |

A record codec model SHALL store whether the record holds leaves and whether it
is a value type, and a union codec model SHALL store whether the union holds
leaves, so a cache hit rebuilds the same descriptor.

#### Scenario: Leaf-free member text is unchanged
- **GIVEN** the leaf-free output contract sources
- **WHEN** the generator runs
- **THEN** the output hashes SHALL equal the hashes from before leaf release

#### Scenario: Later field decode fails
- **GIVEN** a record parameter whose first field is an `ArrayBuffer` and whose
  second field fails to decode
- **WHEN** JavaScript calls the method
- **THEN** the first field's lease SHALL release exactly once
- **AND** the call SHALL fail through the catchable JavaScript error path

#### Scenario: Alias in two returned leaves
- **GIVEN** a method returns a record whose two fields hold the same
  `JavaScriptValue`
- **WHEN** generated glue encodes it
- **THEN** both JavaScript properties SHALL hold the value
- **AND** the source's native handle SHALL release exactly once

#### Scenario: Encode failure releases returned leaves
- **GIVEN** a returned value whose encode throws
- **WHEN** generated glue handles the failure
- **THEN** every leaf of the returned value SHALL release exactly once

#### Scenario: Abandoned async result
- **GIVEN** an async result with leaves whose settlement never reaches the
  runtime
- **WHEN** the scheduler abandons it
- **THEN** every input and result leaf SHALL release exactly once

#### Scenario: Async method throws before returning its task
- **GIVEN** an async method with leaf-bearing arguments throws before it
  returns its `Task`
- **WHEN** JavaScript calls it
- **THEN** the outer `catch` SHALL release each argument exactly once
- **AND** the Promise SHALL reject

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

### Requirement: Module-facing ArrayBuffer And Binary Codecs

`Expo.ModulesCore.ArrayBuffer` SHALL be the universal module-facing binary
abstraction with exactly two private backing forms: JavaScript-owned storage
and native MutableBuffer-owned storage. `byte[]`, `Memory<byte>`,
`ReadOnlyMemory<byte>`, and span support SHALL be convenience codecs, not
additional backing kinds.

| CLR type | Boundary behavior | Generated-binding constraints |
| --- | --- | --- |
| `ArrayBuffer` | Preserves wrapper ownership semantics | Every codec position, including list elements, dictionary values, callback types, and nested typed-event payloads, each also as `ArrayBuffer?` |
| `Uint8Array` | A view over one retained buffer lease with its offset and length; no byte copy in either direction | Every `ArrayBuffer` position, each also as `Uint8Array?`, and a union payload |
| `byte[]` | Copies in both directions | Rank-one arrays only |
| `Memory<byte>` / `ReadOnlyMemory<byte>` | Copies in both directions, encoding exactly the current logical slice | Ordinary codecs |
| `Span<byte>` / `ReadOnlySpan<byte>` | Borrows an input slice or copies a return value | One synchronous parameter only |

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

#### Scenario: Byte arrays, memory, and spans cross the module boundary
- **GIVEN** a generated method uses `byte[]`, `Memory<byte>`,
  `ReadOnlyMemory<byte>`, `Span<byte>`, or `ReadOnlySpan<byte>`
- **WHEN** the method is generated
- **THEN** byte arrays SHALL copy in both directions
- **AND** `Memory<byte>` and `ReadOnlyMemory<byte>` SHALL be ordinary codecs
  that copy in both directions
- **AND** memory codecs SHALL be available in ordinary synchronous and
  asynchronous generated-binding positions
- **AND** memory encoding SHALL copy exactly the current logical slice into a
  distinct JavaScript `ArrayBuffer`
- **AND** memory codecs SHALL NOT pin, retain, or alias managed storage as
  JavaScript storage
- **AND** one synchronous scoped span parameter MAY borrow bytes
- **AND** asynchronous or multiple span parameters, and asynchronous span
  results, SHALL produce diagnostics
- **AND** span return values SHALL be copied immediately
- **AND** only rank-one `byte[]` parameters and returns SHALL use the byte-array
  codec; multidimensional byte arrays SHALL produce unsupported-type diagnostics
- **AND** only exact `Memory<byte>` and `ReadOnlyMemory<byte>` specializations
  SHALL use the memory codecs; other `Memory<T>` and `ReadOnlyMemory<T>` types
  SHALL produce unsupported-type diagnostics
- **AND** the one-span limit SHALL not restrict `ArrayBuffer`, `byte[]`,
  `Memory<byte>`, or `ReadOnlyMemory<byte>` arity

### Requirement: Named Unions Decode Cases In Declaration Order

A type with `[Union]` SHALL be a non-generic `abstract record` class with no
parameter list, accessible to generated code, with no generic containing type.
Its cases SHALL be the nested `sealed record` types that derive from it
directly, each with exactly one positional parameter, the payload, which SHALL
have no default value and SHALL NOT be nullable. Case order SHALL be the source
order of the case declarations, which SHALL all be in one partial declaration.
A payload SHALL be a type whose codec is an ordinary `JavaScriptCodec` with
`DecodeAndEncode` and no runtime context, or another `[Union]` declared in the
same assembly. A payload type MAY be declared in a referenced assembly, and
then SHALL decode and encode through the same codec as when that type is a
method parameter: a positional record, an enum, or a list or dictionary of
them. A union SHALL be accepted in every codec position: parameters, returns,
async results, properties, record fields, list elements, dictionary values,
nullable references, callback arguments and results, typed event payloads, and
shared-object constructors and members.

The generator SHALL emit one private codec per used union in each provider. Its
`Decode` SHALL read the value kind once and visit the cases in declaration
order. It SHALL skip a case whose payload kinds do not contain the kind. The
payload kinds are: `String` for `string`, `Guid`, `Uri`, `DateTimeOffset`,
`TimeSpan`, and string-represented enums; `Bool` for `bool`; `Number` for the
`NumberCodec<T>` types and number-represented enums; `ArrayBuffer` for
`ArrayBuffer`, `byte[]`, `Memory<byte>`, and `ReadOnlyMemory<byte>`; `Object`
for records, lists, dictionaries, and `Uint8Array`; every kind for
`JavaScriptValue`; and the kinds of all cases for a nested union. An integer
case that is not the last case whose kinds contain `Number` SHALL take only a
whole number that its type holds exactly. Any other case SHALL be attempted
through its payload codec. The first successful attempt SHALL win. An
`AggregateException` from an attempt SHALL propagate. Any other exception SHALL
fail the attempt and move to the next case. If constructing the case throws,
the codec SHALL release the decoded payload and rethrow. A typed event payload
is only encoded, so it attempts no case.

When no case takes the value, `Decode` SHALL throw
`UnionCaseMismatchException` with the message
`'<Union>' expected <T1> or <T2> or ..., but received <kind>`: the union's
simple name, the payload types in declaration order in C# minimal form, and one
of `undefined`, `null`, `boolean`, `number`, `string`, `object`, `function`, or
`ArrayBuffer`. `UnionCaseMismatchException` SHALL derive from
`CodedException`, and its `Code` SHALL be the derived `ERR_UNION_CASE_MISMATCH`,
the code of upstream iOS `UnionCaseMismatch`. JavaScript SHALL receive it as an
`Error` whose `message` is that text followed by the cause chain of
`### Requirement: Module Failures Become JavaScript Errors`. It SHALL expose
`UnionName`, `Received`, and `Expected`, and its `InnerException` SHALL be the
failure of the last attempted case, or `null` when no case was attempted.

`Encode` SHALL encode the payload of the case it holds through the payload
codec. A `null` union SHALL throw `ArgumentNullException`, and a type that is
not a case SHALL throw `InvalidOperationException` with the message
`Type '<type>' is not a case of union '<Union>'.`, built by
`UnionCodecErrors.NotACase`, so generated union text uses no `GetType` or
`typeof`. A union SHALL hold leaves when a payload does. Its `Release` SHALL
release only the payload of the case it holds and SHALL skip `null`. A
leaf-free union SHALL keep leaf-free generated text.

The generator SHALL report, once per declaration and at error severity:
`EXPOJSI029` for an invalid union declaration, a recursive union, or a union
declared in a referenced assembly (at the using parameter, member, or record
field); `EXPOJSI030` for an invalid case, a type that derives from a union and
is not a valid case, a nullable or defaulted payload, a payload that contains
itself through records, lists, dictionaries, callbacks, or unions, or a payload
with no union codec; `EXPOJSI031` for two cases with the same payload type; and
`EXPOJSI032` for a case that an earlier case always takes first. A case type
used directly, and every invalid union, SHALL make the using position report
its existing unsupported-type diagnostic. No Roslyn dependency on source file
order, no reflection, and no ABI surface SHALL be added.

A later case is unreachable when:

1. An earlier case has a `JavaScriptValue` payload.
2. Its kinds are only `Number`, and an earlier case has a `double` or `float`
   payload.
3. It is an integer case that is not the last case whose kinds contain
   `Number`, and an earlier integer case's type range contains its type range.
4. Its kinds are only `String`, and an earlier case has a `string` payload.
5. Its kinds are only `ArrayBuffer`, and an earlier case has an `ArrayBuffer`,
   `byte[]`, `Memory<byte>`, or `ReadOnlyMemory<byte>` payload.
6. Its kinds are only `Object`, and an earlier case has a
   `Dictionary<string, JavaScriptValue>` or
   `IReadOnlyDictionary<string, JavaScriptValue>` payload, or a record payload
   whose every field is `JavaScriptValue`.
7. It has an `IReadOnlyList<T>` payload, and an earlier case has an
   `IReadOnlyList<JavaScriptValue>` payload.
8. It has a record payload, an earlier case has a record payload, and every
   field of the earlier record that is not `JavaScriptValue` has a field in the
   later record with the same JavaScript name and either the same C# type or
   the C# type of which the earlier field's type is the nullable form, and,
   when the earlier field is required, the later field is required too.

#### Scenario: Three cases decode by kind
- **GIVEN** `[Union] abstract record NonceInput` with cases `Base64(string)`,
  `Bytes(byte[])`, and `Length(int)`, and a method
  `string Describe(NonceInput nonce)`
- **WHEN** JavaScript passes `'AAEC'`, an `ArrayBuffer` of 3 bytes, and `12`
- **THEN** authored code SHALL receive `Base64("AAEC")`, `Bytes` with the 3
  bytes, and `Length(12)`

#### Scenario: Integer before double
- **GIVEN** a union with cases `Whole(int)` and then `Fraction(double)`
- **WHEN** JavaScript passes `3`, `2.5`, and `3e9`
- **THEN** `3` SHALL decode as `Whole(3)`, and `2.5` and `3e9` SHALL decode as
  `Fraction`

#### Scenario: A lone integer case decodes like its codec
- **GIVEN** a union with cases `Text(string)` and `Count(int)`
- **WHEN** JavaScript passes `2.5`
- **THEN** it SHALL decode as `Count(2)`, as `NumberCodec<int>` decodes `2.5`
  outside a union

#### Scenario: A failed attempt moves to the next case
- **GIVEN** a union with cases `Address(Uri)` and then `Text(string)`
- **WHEN** JavaScript passes `'https://expo.dev/x'` and `'hello'`
- **THEN** the first SHALL decode as `Address` and the second as `Text`

#### Scenario: Records are picked by their fields
- **GIVEN** a union with cases `Round(Circle)` and then `Dot(Point)`, where
  `record Circle(double X, double Y, double Radius)` and
  `record Point(double X, double Y)` are declared outside the union
- **WHEN** JavaScript passes `{ x: 1, y: 1, radius: 2 }`, `{ x: 1, y: 2 }`, and
  `{ x: 1 }`
- **THEN** the first SHALL decode as `Round`, the second as `Dot`
- **AND** the third SHALL throw `UnionCaseMismatchException` whose
  `InnerException` is the `Point` decode failure
- **AND** the mismatch message SHALL end with
  `\n→ Caused by: Value for property 'y' is required, got undefined`

#### Scenario: A list and a record in one union
- **GIVEN** a union with cases `Items(IReadOnlyList<int>)` and `Named(Entry)`,
  where `record Entry(string Name)`
- **WHEN** JavaScript passes `[1, 2]` and `{ name: 'a' }`
- **THEN** the first SHALL decode as `Items` and the second as `Named`

#### Scenario: Nested union
- **GIVEN** a union `Outer` with cases `Flag(bool)` and `Nested(Inner)`, where
  `Inner` is a union declared outside `Outer` with cases `Text(string)` and
  `Count(int)`
- **WHEN** JavaScript passes `true`, `'a'`, `3`, and `{}`
- **THEN** they SHALL decode as `Flag`, `Nested(Text)`, `Nested(Count)`, and
  throw `'Outer' expected bool or Inner, but received object`

#### Scenario: Kind gating skips without decoding
- **GIVEN** a union with cases `Text(string)` and `View(Uint8Array)`
- **WHEN** JavaScript passes an `ArrayBuffer`
- **THEN** it SHALL throw
  `'<Union>' expected string or Uint8Array, but received ArrayBuffer`
- **AND** `InnerException` SHALL be `null`, because no case was attempted

#### Scenario: Mismatch message and nullish input
- **GIVEN** `[Union] abstract record UnionSource` with cases `Text(string)` and
  `Options(UnionSourceOptions)` and a non-nullable parameter
- **WHEN** JavaScript passes `42`, `null`, and `undefined`
- **THEN** the call SHALL fail with a catchable JavaScript `Error` whose
  message contains
  `'UnionSource' expected string or UnionSourceOptions, but received number`,
  then `received null`, then `received undefined`
- **AND** authored code SHALL NOT run

#### Scenario: Mismatch has the upstream code
- **GIVEN** any union
- **WHEN** no case takes the value
- **THEN** JavaScript SHALL catch an `Error` with
  `code === 'ERR_UNION_CASE_MISMATCH'`
- **AND** when a case was attempted, its failure SHALL follow the message as
  `\n→ Caused by: <failure>`

#### Scenario: Nullable union
- **GIVEN** a `UnionSource?` parameter and a `UnionSource?` return
- **WHEN** JavaScript passes `null` and `undefined`, and authored code returns
  `null`
- **THEN** authored code SHALL receive C# `null` for both inputs, and
  JavaScript SHALL receive `null`

#### Scenario: Encode each case
- **GIVEN** methods that return `Base64("a")`, `Bytes([1, 2])`, `Length(3)`,
  and a `null` non-nullable union
- **WHEN** JavaScript calls them
- **THEN** JavaScript SHALL receive `'a'`, an `ArrayBuffer` with bytes `1, 2`,
  and `3`
- **AND** the `null` call SHALL fail with a catchable JavaScript error
- **AND** the generated `Encode` SHALL end with the `InvalidOperationException`
  throw for a type that is not a case

#### Scenario: Union in every position
- **GIVEN** a union with cases `Text(string)` and `Buffer(ArrayBuffer)`, used
  as a record field, a list element, a dictionary value, a sync return, a
  `Task` result, a property, a typed event payload on a module and on a shared
  object, a callback argument, a callback result, a shared-object constructor
  parameter, and a shared-object method parameter
- **WHEN** JavaScript passes and receives values through each position
- **THEN** each SHALL decode and encode the case that matches the value
- **AND** a returned buffer SHALL keep its JavaScript identity and release
  exactly once
- **AND** an event payload or callback argument SHALL stay with the author, and
  a callback result SHALL belong to the author

#### Scenario: Leaf payload releases once
- **GIVEN** `string Write(Content content)` where `Content` has cases
  `Text(string)` and `Buffer(ArrayBuffer)`, on a module and on a shared object
- **WHEN** JavaScript passes a string and then an `ArrayBuffer`
- **THEN** the buffer lease SHALL release exactly once at invocation end
- **AND** the string call SHALL hold no lease

#### Scenario: A failed attempt releases what it decoded
- **GIVEN** a union with cases `Packed(Pair)` and then `Raw(JavaScriptValue)`,
  where `record Pair(ArrayBuffer Data, int Count)` is declared outside the
  union
- **WHEN** JavaScript passes `{ data: buffer, count: 'x' }`
- **THEN** the `Packed` attempt SHALL release its `Data` lease exactly once
- **AND** the value SHALL decode as `Raw`, whose leaf SHALL release exactly
  once at invocation end

#### Scenario: Case construction fails after its payload decoded
- **GIVEN** a case whose constructor throws for a decoded `ArrayBuffer`, before
  a `Raw(JavaScriptValue)` case
- **WHEN** JavaScript passes a buffer that the constructor rejects
- **THEN** the lease SHALL release exactly once
- **AND** the call SHALL fail with the constructor's error, and the `Raw` case
  SHALL NOT be attempted

#### Scenario: A release failure in an attempt propagates
- **GIVEN** the generated codec of a union whose first case has a leaf-bearing
  record payload
- **WHEN** the generator runs
- **THEN** each attempt SHALL catch `AggregateException` and rethrow it before
  the catch that moves to the next case
- **AND** a release failure during a failed attempt SHALL therefore propagate,
  and the later case SHALL NOT be attempted

#### Scenario: Leaf-free union keeps leaf-free glue
- **GIVEN** `int Pick(Choice value)` where `Choice` has cases `Number(double)`
  and `Flag(bool)`
- **WHEN** the generator runs
- **THEN** the generated text SHALL NOT contain `CodecRelease`, and the union
  codec SHALL have no `Release`

#### Scenario: Invalid declarations report EXPOJSI029
- **GIVEN** `[Union]` on a non-record class, on a generic abstract record, on
  an abstract record with a parameter list, on a private nested union, on a
  union with no cases, on a union whose cases are in two partial declarations,
  and on a union with a case `Many(IReadOnlyList<Self>)` that contains the
  union itself
- **WHEN** the generator runs, with or without a member that uses each union
- **THEN** it SHALL report `EXPOJSI029` once per declaration, naming the union
  and the reason

#### Scenario: Payload record from a referenced assembly
- **GIVEN** an assembly that declares
  `public record ExternalRecord(string Name, int Count)`, and a project that
  references it and declares `[Union] abstract record Sth` with cases
  `Foo(ExternalRecord Bar)` and `Text(string Value)`, and a method
  `string Describe(Sth value)`
- **WHEN** JavaScript passes `{ name: 'a', count: 2 }` and `'x'`
- **THEN** the generator SHALL report no diagnostic
- **AND** the first value SHALL decode as `Foo` whose `Bar` is
  `ExternalRecord("a", 2)`, and the second as `Text("x")`
- **AND** a method that returns `new Sth.Foo(new ExternalRecord("a", 2))` SHALL
  give JavaScript the object `{ name: 'a', count: 2 }`

#### Scenario: A union from a referenced assembly reports EXPOJSI029
- **GIVEN** a `[Union]` declared in a referenced assembly and a method
  parameter of that type
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI029` at the parameter, naming the union and
  its assembly
- **AND** the parameter SHALL report `EXPOJSI001`, and no binding SHALL be
  emitted for the method

#### Scenario: Invalid cases report EXPOJSI030
- **GIVEN** a union with a case that has no positional parameter, a case with
  two, a non-sealed case, a case with `string Value = ""`, a case with
  `string? Value`, a case with a shared-object payload, a case with a
  `JavaScriptCallback<int>` payload, a case `Address(Uri Value)` declared next
  to a case named `Uri`, and a top-level record that derives from the union
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI030` for each, naming the case or type, the
  union, and the reason
- **AND** the no-parameter message SHALL point to an `enum` payload, the
  two-parameter message SHALL point to a record payload, and the nullable
  message SHALL point to a nullable union at the use site
- **AND** a case declared as `Uri(Uri Value)` SHALL also report `EXPOJSI030`,
  next to the C# error CS8910

#### Scenario: A payload that contains itself is rejected
- **GIVEN** a case `Tree(Node)` where
  `record Node(string Name, IReadOnlyList<Node> Children)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI030` for the case before any payload codec
  resolves, and the build SHALL NOT crash

#### Scenario: Duplicate payloads report EXPOJSI031
- **GIVEN** a union with cases `A(string)` and `B(string)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI031` naming the union, the payload type, and
  both cases

#### Scenario: Unreachable cases report EXPOJSI032
- **GIVEN** unions where a case follows a `JavaScriptValue` case; an `int` case
  follows a `double` case; an `int` case followed by a `double` case follows a
  `long` case; a `Uri` case follows a `string` case; a `byte[]` case follows an
  `ArrayBuffer` case; a record case follows a
  `Dictionary<string, JavaScriptValue>` case; an `IReadOnlyList<int>` case
  follows an `IReadOnlyList<JavaScriptValue>` case; and a case with record
  payload `Circle(double X, double Y, double Radius)` follows a case with
  record payload `Point(double X, double Y)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI032` for each later case, naming it, the
  union, and the earlier case
- **AND** it SHALL NOT report `EXPOJSI032` for `int` then `double`, `Uri` then
  `string`, payload `Circle` then payload `Point`, `Dictionary<string, int>`
  then `IReadOnlyList<int>`, or payload `Point` then a record payload
  `Line(string X, double Y)`

#### Scenario: Reachable through record defaults
- **GIVEN** a union with cases `Dot(Point)` and then `Relaxed(Partial)`, where
  `record Point(double X, double Y)` and
  `record Partial(double X, double Y = 0, double Z = 0)`
- **WHEN** the generator runs and JavaScript passes `{ x: 1 }`
- **THEN** it SHALL NOT report `EXPOJSI032`
- **AND** the value SHALL decode as `Relaxed`

#### Scenario: A JavaScriptValue dictionary takes every object
- **GIVEN** a union with cases `Bag(Dictionary<string, JavaScriptValue>)` and
  then `Text(string)`
- **WHEN** JavaScript passes a plain object, an array, a `Uint8Array`, an
  object with a null prototype, a `Map`, a `Date`, and an object with a
  non-enumerable property
- **THEN** every value SHALL decode as `Bag`

#### Scenario: A case type outside its union is rejected
- **GIVEN** a method `int M(NonceInput.Bytes bytes)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI001` naming `NonceInput.Bytes`, and SHALL NOT
  generate a record codec for it

#### Scenario: Generated union codecs avoid reflection
- **GIVEN** the generated provider of a project that uses unions
- **WHEN** a reviewer searches it for `GetType(`, `typeof(`, `MethodInfo`,
  `Activator`, `DynamicInvoke`, and `object?[]`
- **THEN** the union codecs SHALL contain none of them

#### Scenario: Either is gone
- **GIVEN** the repository after this change
- **WHEN** a reviewer runs
  `git grep -n -e "Either[<]" -e "Either[C]odec" -e "IJavaScriptValueKind[C]odec" -- packages apps docs/specs docs/module-authoring-guide.md`
- **THEN** it SHALL print nothing

### Requirement: Uint8Array Is A Byte View

`Uint8Array` SHALL be accepted in every codec position where `ArrayBuffer` is,
each also as `Uint8Array?`, and as a union payload, through
`Uint8ArrayCodec`, an ordinary `IJavaScriptCodec<Uint8Array>` that holds a
leaf. A `Uint8Array` owns one `ArrayBuffer` lease plus an offset and a length.
Decoding SHALL prove the value is a `Uint8Array` by prototype-chain membership
(`Uint8Array.prototype.isPrototypeOf`), accepting a subclass instance and
rejecting every other shape with `InvalidOperationException` and the message
`Value is not a Uint8Array.`. It SHALL retain the backing buffer without
copying bytes, and SHALL keep the offset and length. Encoding SHALL produce a
JavaScript `Uint8Array` over the encoded buffer with the same offset and
length through the global constructor, and SHALL leave the view usable.
`Release` SHALL dispose the view, which releases its lease once; `Dispose` is
idempotent. The view SHALL fail loudly when the buffer no longer covers it.
`byte[]`, `ArrayBuffer`, `Memory<byte>`, and `ReadOnlyMemory<byte>` SHALL NOT
accept a `Uint8Array`. No native or ABI surface SHALL be added for typed
arrays.

#### Scenario: In-place write
- **GIVEN** a method `void Fill(Uint8Array bytes)` that writes `0x42` to every
  byte
- **WHEN** JavaScript passes `new Uint8Array(4)`, and then an instance of a
  `Uint8Array` subclass, and reads each after its call
- **THEN** every element SHALL be `0x42`
- **AND** the lease SHALL be released once at invocation end

#### Scenario: Sub-view
- **GIVEN** a method `string Hex(Uint8Array bytes)`
- **WHEN** JavaScript passes `new Uint8Array(buffer, 4, 8)` over a 16-byte
  buffer
- **THEN** authored code SHALL see `ByteOffset 4`, `Length 8`, and exactly
  those bytes

#### Scenario: Return and property
- **GIVEN** a method `Uint8Array Make()`, a `Task<Uint8Array>` method, and a
  `Uint8Array?` property
- **WHEN** JavaScript reads them
- **THEN** each SHALL be a `Uint8Array` with matching bytes, `byteOffset`, and
  `length`, and `null` for a null property

#### Scenario: Wrong shapes are rejected
- **GIVEN** the `Fill` parameter and a `Uint8Array?` parameter
- **WHEN** JavaScript passes an `ArrayBuffer`, an `Int32Array`, a `DataView`, a
  plain object, `5`, `null`, or `undefined`
- **THEN** `Fill` SHALL throw `Value is not a Uint8Array.` for every input
- **AND** the nullable parameter SHALL pass `null` for `null` and `undefined`
  and throw for the rest

#### Scenario: Composition follows ArrayBuffer
- **GIVEN** `IReadOnlyList<Uint8Array>` and `Dictionary<string, Uint8Array>`
  parameters and returns, and a record with a `Uint8Array` field
- **WHEN** JavaScript passes and receives them
- **THEN** each view SHALL release its lease exactly once through `Release`,
  as for `ArrayBuffer`

#### Scenario: Callback, event, and union
- **GIVEN** a callback whose argument and result are `Uint8Array?`, a
  `Func<Uint8Array?, Task>` event, and a `StringOrView` union parameter with
  cases `Text(string)` and `View(Uint8Array)`
- **WHEN** JavaScript passes a view or `null`
- **THEN** a view or `null` passed to the callback or the event SHALL stay with
  the author, and a view returned by the callback SHALL belong to the author
- **AND** the union SHALL decode a string as `Text` and a view as `View`
- **AND** it SHALL throw
  `'StringOrView' expected string or Uint8Array, but received ArrayBuffer` for
  an `ArrayBuffer`, and
  `'StringOrView' expected string or Uint8Array, but received object` for
  another object, whose `InnerException` is `Value is not a Uint8Array.`, so
  the JavaScript message ends with `\n→ Caused by: Value is not a Uint8Array.`

### Requirement: Nullable Reference Codecs Are Annotation-Driven

Generated binding analysis SHALL activate nullable reference handling only for a
reference type whose Roslyn symbol carries `NullableAnnotation.Annotated`. It
SHALL remove only the top-level annotation before resolving the inner codec, and
it SHALL preserve nested annotations on type arguments and record fields.
Nullish handling SHALL live only in the nullable reference wrapper and the
nullable collection adapters. The generator SHALL NOT make an existing
non-nullable codec accept JavaScript `null` or `undefined`.

Generated codec type arguments SHALL be rendered with the nullable
reference-type modifier. A codec expression is used as a generic type argument
under a `where TCodec : IJavaScriptCodec<T>` constraint, so an
annotation-erased `T` paired with a nullable codec makes generated code report
`CS8631`, and a reproduced authored partial property reports `CS9256`.

#### Scenario: Annotated string selects a nullable wrapper
- **GIVEN** nullable annotations are enabled and a generated boundary declares
  `string?`
- **WHEN** the generator resolves its codec
- **THEN** it SHALL compose `StringCodec` through the nullable reference codec
- **AND** it SHALL resolve the inner `string` from a non-annotated top-level
  symbol

#### Scenario: Non-nullable reference stays strict
- **GIVEN** a generated method has a non-nullable `string` parameter
- **WHEN** JavaScript passes `null` or `undefined`
- **THEN** `StringCodec` SHALL reject the value before authored code runs
- **AND** generated dispatch SHALL NOT route the parameter through a nullable
  wrapper

#### Scenario: Oblivious reference keeps its current codec
- **GIVEN** nullable annotations are disabled and a generated boundary declares
  an unannotated `string`
- **WHEN** the generator resolves its codec
- **THEN** `NullableAnnotation.None` SHALL NOT activate nullable reference
  handling
- **AND** the generator SHALL use plain `StringCodec`

#### Scenario: Nullable value types keep their own dispatch
- **GIVEN** a generated boundary declares `int?` or another supported nullable
  value type
- **WHEN** the generator resolves its codec
- **THEN** it SHALL use `NullableCodec<T, TCodec>`, which SHALL stay constrained
  with `where T : struct`
- **AND** it SHALL NOT use `NullableReferenceCodec<T, TCodec>`

#### Scenario: Codec type argument carries the annotation
- **GIVEN** the generator pairs a value or event-payload type argument with a
  nullable codec expression
- **WHEN** it renders generated source
- **THEN** the type argument SHALL include the nullable reference-type modifier
- **AND** generated source SHALL compile without `CS8631` or `CS9256`

### Requirement: Supported Nullable Reference Values Use Dedicated Codecs

Generated bindings SHALL support nullable `string`, `Uri`, `byte[]`,
positional reference-record values, and `[Union]` values through
`NullableReferenceCodec<T, TCodec>`, constrained with `where T : class` and
`where TCodec : IJavaScriptCodec<T>`. Both the scoped and the owned decode
overloads SHALL return C# `null` for JavaScript `null` or `undefined` without
calling the inner codec. Encoding C# `null` SHALL produce JavaScript `null`.
Non-null conversion SHALL delegate to the existing codec for the same
non-nullable type, whose behavior SHALL remain unchanged. `ByteArrayCodec`
SHALL implement `IJavaScriptCodec<byte[]>` so it can serve as an inner codec,
keeping its existing copy-in-both-directions behavior.

#### Scenario: Nullish input decodes to null
- **GIVEN** a supported nullable reference codec receives JavaScript `null` or
  `undefined`
- **WHEN** either decode overload runs
- **THEN** it SHALL return C# `null`
- **AND** it SHALL NOT call the inner codec

#### Scenario: Null value encodes as JavaScript null
- **GIVEN** a supported nullable reference value is C# `null`
- **WHEN** generated binding glue encodes it
- **THEN** the result SHALL be JavaScript `null`
- **AND** it SHALL NOT be JavaScript `undefined`

#### Scenario: Non-null value uses the existing codec
- **GIVEN** a supported nullable reference value is not null
- **WHEN** it crosses the generated boundary in either direction
- **THEN** conversion SHALL delegate to the existing non-nullable codec
- **AND** that codec's non-null behavior SHALL remain unchanged

### Requirement: Non-Null Collections Compose Recursively

`JavaScriptArrayCodec<T, TCodec>` SHALL implement
`IJavaScriptCodec<IReadOnlyList<T>>`, and `JavaScriptDictionaryCodec<T,
TCodec>` SHALL implement `IJavaScriptCodec<Dictionary<string, T>>` and
`IJavaScriptCodec<IReadOnlyDictionary<string, T>>`. Their existing
`DecodeToArray`, `DecodeToDictionary`, and `Encode` methods SHALL keep their
signatures and behavior. Generated bindings SHALL support a non-null
`IReadOnlyList<T>`, `Dictionary<string, T>`, or `IReadOnlyDictionary<string,
T>` wherever a codec of `T` is supported: as a list element, a dictionary
value, a nullable container's content, a callback result or argument, a
generated record field, and a typed event payload. A list element or
dictionary value MAY be `ArrayBuffer`, `JavaScriptValue`, or any type that
holds a leaf. The same `JavaScriptArrayCodec<T, TCodec>` and
`JavaScriptDictionaryCodec<T, TCodec>` SHALL serve it.

#### Scenario: Nested non-null collections round-trip
- **GIVEN** a generated method accepts and returns
  `IReadOnlyList<IReadOnlyList<int>>` or
  `IReadOnlyDictionary<string, IReadOnlyList<string>>`
- **WHEN** JavaScript passes nested arrays and objects
- **THEN** generated dispatch SHALL decode and re-encode every level
- **AND** direct collection parameters SHALL still decode through
  `DecodeToArray` or `DecodeToDictionary`

#### Scenario: Collections of buffers round-trip
- **GIVEN** a method accepts and returns `IReadOnlyList<ArrayBuffer>`,
  `Dictionary<string, ArrayBuffer>`, or a record containing a list containing
  a dictionary of `ArrayBuffer`
- **WHEN** JavaScript passes JS-created buffers
- **THEN** each decoded lease and each returned source SHALL release exactly
  once
- **AND** the JavaScript result SHALL stay usable after the call releases its
  leaves

#### Scenario: Record collection field round-trips
- **GIVEN** a positional record has an `IReadOnlyList<T>` or
  `Dictionary<string, T>` field with a supported `T`
- **WHEN** the record crosses a generated boundary
- **THEN** the field SHALL decode from and encode to its lower-camel
  JavaScript property

#### Scenario: Typed event emits a collection payload
- **GIVEN** a module or shared object declares
  `[Event] Func<IReadOnlyList<T>, Task>` or a string-keyed dictionary payload
  with a supported `T`
- **WHEN** C# raises the event
- **THEN** generated code SHALL compile and listeners SHALL receive a
  JavaScript array or plain object

### Requirement: Nullable Collection Containers And Contents Compose Recursively

Generated bindings SHALL support nullable `IReadOnlyList<T>`,
`Dictionary<string, T>`, and `IReadOnlyDictionary<string, T>` containers through
dedicated adapters, because a nullable container needs nullish handling that
the non-null collection codecs do not have. The adapters SHALL be
`NullableReadOnlyListCodec<T, TCodec>`, `NullableDictionaryCodec<T, TCodec>`,
and `NullableReadOnlyDictionaryCodec<T, TCodec>`. Each SHALL return C# `null`
for JavaScript `null` or `undefined`, SHALL encode a null container as
JavaScript `null`, and SHALL delegate non-null conversion to the existing
collection helper. Element and value codecs SHALL resolve recursively, and
dictionary keys SHALL remain exactly `string`. `List<T>` SHALL remain
unsupported with and without a nullable annotation, because its non-nullable
form has no generated codec. These three adapters SHALL serve both leaf-free
and leaf-bearing contents. A null container releases nothing.

#### Scenario: Nullable collection container round-trips null
- **GIVEN** a generated boundary declares one of the supported nullable
  collection containers
- **WHEN** JavaScript supplies `null` or `undefined`
- **THEN** decoding SHALL return C# `null`
- **WHEN** C# supplies a null container
- **THEN** encoding SHALL return JavaScript `null`

#### Scenario: Nullable collection contents preserve null
- **GIVEN** a supported list element or dictionary value type is an annotated
  nullable reference
- **WHEN** a non-null container crosses the generated boundary
- **THEN** nullish nested values SHALL decode as C# `null`
- **AND** C# null elements or values SHALL encode as JavaScript `null`
- **AND** non-null elements or values SHALL use their existing inner codec

#### Scenario: Nested nullable container composes
- **GIVEN** a supported collection contains another supported nullable
  collection
- **WHEN** the generator resolves the nested codecs
- **THEN** it SHALL preserve each container and element annotation
- **AND** it SHALL emit compile-time codec composition without reflection

#### Scenario: List stays unsupported with a nullable annotation
- **GIVEN** a generated boundary declares `List<T>` or `List<T>?`
- **WHEN** the generator analyzes the type
- **THEN** it SHALL report the existing unsupported-type diagnostic for that
  position
- **AND** it SHALL NOT emit a `List<T>` codec

### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

The generator SHALL reject a nullable annotation on `JavaScriptValue`,
`SharedObject`, and `SharedRef<T>`. `JavaScriptValue` already represents
JavaScript `null` and `undefined`. The others carry shared-object identity,
and a null axis multiplies those states. The generator
SHALL report an existing context-specific diagnostic at error severity. It SHALL
NOT add a new diagnostic code, SHALL NOT raise a context-free diagnostic from
codec resolution, SHALL NOT emit a binding for the rejected member, and SHALL
NOT fall through to that type's non-nullable codec. The same non-nullable types
SHALL keep their current supported or diagnostic behavior. `ArrayBuffer?` and
`Uint8Array?` are supported through `NullableReferenceCodec` (see "Nullable
ArrayBuffer Is A Nullable Reference" and "Uint8Array Is A Byte View"). A
nullable callback is supported as a method or constructor parameter (see
"Nullish Callback Parameters Decode To Null Without Retention") and keeps the
non-null rejection on every other position.

Codec resolution does not know the member context, so the codes come from the
callers: `EXPOJSI001` for a parameter, `EXPOJSI002` for a return, `EXPOJSI007`
for a record field, `EXPOJSI008` for a callback, `EXPOJSI015` for a property,
`EXPOJSI019` for a module typed event payload, `EXPOJSI027` for a shared-object
typed event payload, and `EXPOJSI023` for a shared-object boundary. Existing
layered reporting stays as it is: a rejected record field reports both its own
`EXPOJSI007` and the containing member's diagnostic, and a member declared on a
shared-object class reports its `EXPOJSI001`, `EXPOJSI002`, `EXPOJSI008`, and
`EXPOJSI015` results as `EXPOJSI023`.

Typed event payload analysis SHALL check the exclusion itself. A
`Func<JavaScriptValue?, Task>` payload bypasses codec resolution, so only the
event payload check can report `EXPOJSI019` or `EXPOJSI027` for it. A
`Func<ArrayBuffer?, Task>` payload is the direct `ArrayBuffer` payload kind plus
nullability.

A nullable annotation SHALL NOT change shared-object type identity.
`SharedObject?` and `SharedRef<T>?` SHALL report `EXPOJSI023` with the same
reason as the non-null form. A valid concrete class annotated as nullable SHALL
select `NullableSharedObjectCodec<T>` (see "Nullable Concrete Shared Objects
Bypass The Registry On Null"). A rejected member is rejected at error severity,
no binding is emitted, and no shared-object codec is selected.

#### Scenario: Excluded nullable parameter or return reports the method codes
- **GIVEN** a generated method declares `JavaScriptValue?` as a parameter or as
  its return type
- **WHEN** the generator analyzes the method
- **THEN** it SHALL report `EXPOJSI001` for the parameter position and
  `EXPOJSI002` for the return position
- **AND** it SHALL NOT emit a binding or select `JavaScriptValueCodec`

#### Scenario: Excluded nullable record field reports the record code
- **GIVEN** a generated positional record declares a field whose type is an
  excluded nullable reference
- **WHEN** the generator analyzes the record
- **THEN** it SHALL report `EXPOJSI007` naming the record and the field
- **AND** it SHALL NOT emit a record codec that treats the field as
  non-nullable

#### Scenario: Nullable callback outside a parameter keeps its diagnostic
- **GIVEN** a generated return, async result, `[JS]` property, record
  field, list element, dictionary value, or typed event payload uses an
  annotated `JavaScriptCallback<...>?`
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report the same diagnostic as the non-null form
- **AND** it SHALL NOT emit the member

#### Scenario: Excluded nullable property reports the property code
- **GIVEN** a `[JS]` property declares an excluded nullable reference type
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI015`
- **AND** it SHALL emit neither accessor

#### Scenario: Excluded nullable event payload reports the event codes
- **GIVEN** a module typed event or a shared-object typed event declares an
  excluded nullable payload type
- **WHEN** the generator analyzes the event property
- **THEN** typed event payload analysis SHALL report `EXPOJSI019` for a module
  event and `EXPOJSI027` for a shared-object event
- **AND** it SHALL NOT emit event glue for that property

#### Scenario: Nullable SharedRef reports the shared-object code
- **GIVEN** a generated boundary declares an annotated `SharedRef<T>`
- **WHEN** shared-object boundary analysis reaches the annotated type
- **THEN** it SHALL report `EXPOJSI023`
- **AND** it SHALL NOT select a shared-object codec or emit the member

#### Scenario: Nullable polymorphic SharedObject reports the shared-object code
- **GIVEN** a generated boundary declares `SharedObject?` as a method
  parameter, a return type, an async result type, a property type, or a
  shared-object `[JS]` constructor parameter, on a module or on a
  shared-object class
- **WHEN** the generator analyzes the boundary
- **THEN** it SHALL report `EXPOJSI023` naming the member and the reason
  "which is the polymorphic SharedObject base"
- **AND** it SHALL NOT report `EXPOJSI001`, `EXPOJSI002`, or `EXPOJSI015` for
  that member
- **AND** it SHALL NOT select a shared-object codec or emit the member

#### Scenario: Nullable shared object inside a composed codec
- **GIVEN** a generated method parameter declares a composed type whose
  element is `SharedObject?`, such as `IReadOnlyList<SharedObject?>`
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL report `EXPOJSI023` with the composed-codec reason, as it
  does for `IReadOnlyList<SharedObject>`
- **AND** it SHALL NOT emit the member

#### Scenario: Valid concrete shared object stays accepted
- **GIVEN** a generated method parameter declares a non-null top-level, sealed,
  non-generic `[ExpoSharedObject]` class
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL select `SharedObjectCodec<T>` and emit the binding with no
  diagnostic
- **AND** the same class annotated as nullable SHALL select
  `NullableSharedObjectCodec<T>`

#### Scenario: Rejected nullable type does not fall back
- **GIVEN** codec resolution has handled an annotated excluded reference
- **WHEN** control returns to the analysis that asked for the codec
- **THEN** dispatch SHALL stop instead of continuing to that type's
  non-nullable codec branch
- **AND** the generator SHALL report only the existing context-specific
  diagnostics and SHALL NOT emit a binding

### Requirement: Nullable ArrayBuffer Is A Nullable Reference

`ArrayBuffer?` SHALL resolve to
`NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>` on every position
where `ArrayBuffer` is accepted. JavaScript `null` and `undefined` SHALL
decode to C# `null` without a lease. C# `null` SHALL encode as JavaScript
`null`. A non-null value SHALL follow the `ArrayBuffer` rules unchanged:
decode gives the leaf to generated glue, which releases it once through
`Release`, and `Encode` leaves the source usable. The generator SHALL NOT add
a nullable-specific cleanup path.

#### Scenario: Nullish parameter holds no leaf
- **GIVEN** a generated method declares an `ArrayBuffer?` parameter
- **WHEN** JavaScript passes `null` or `undefined`
- **THEN** authored code SHALL receive C# `null`
- **AND** `LongLivedArrayBuffersReleased` SHALL NOT change

#### Scenario: Non-null parameter releases once
- **GIVEN** the same method
- **WHEN** JavaScript passes a JavaScript-created buffer to a sync method,
  and to an async method that awaits before reading it
- **THEN** authored code SHALL read the buffer, after the await on the async
  path
- **AND** each call SHALL release the lease exactly once

#### Scenario: Optional nullable buffer uses the authored default
- **GIVEN** a generated method declares `ArrayBuffer? value = null`
- **WHEN** JavaScript omits the argument, passes `undefined`, or passes
  `null`
- **THEN** authored code SHALL receive C# `null` in all three cases
- **AND** no lease SHALL be retained

#### Scenario: Nullable return and property encode null or the buffer
- **GIVEN** a sync method, a `Task<ArrayBuffer?>` method, and a readable and
  writable `ArrayBuffer?` property
- **WHEN** authored code returns or stores `null`
- **THEN** JavaScript SHALL receive `null`
- **WHEN** authored code returns a buffer
- **THEN** JavaScript SHALL read its bytes
- **AND** the returned leaf SHALL release exactly once after encode
- **WHEN** JavaScript assigns a buffer to the property and authored code
  stores a `Retain()` copy
- **THEN** the setter's decoded leaf SHALL release once and the stored copy
  SHALL stay readable until the module releases it

#### Scenario: Nullable buffer inside a record and a collection
- **GIVEN** a record with an `ArrayBuffer?` field and an
  `IReadOnlyList<ArrayBuffer?>` parameter
- **WHEN** JavaScript passes `null` leaves next to buffer leaves
- **THEN** null leaves SHALL release nothing and buffer leaves SHALL
  release exactly once each
- **WHEN** a later field decode fails after a non-null buffer field decoded
- **THEN** the earlier lease SHALL release exactly once

#### Scenario: Shared-object members accept nullable buffers
- **GIVEN** a shared-object `[JS]` constructor parameter, method parameter,
  and property of type `ArrayBuffer?`
- **WHEN** JavaScript passes `null` and a buffer
- **THEN** they SHALL behave as the module positions above

#### Scenario: Nullable buffer in a callback and a nested event payload
- **GIVEN** a callback whose argument and result are `ArrayBuffer?`, and a
  typed event whose payload is a list of `ArrayBuffer?`
- **WHEN** authored code passes `null` and a buffer
- **THEN** JavaScript SHALL receive `null` and the buffer
- **AND** the author's buffer SHALL stay usable, and the author SHALL own a
  non-null callback result

#### Scenario: JavaScriptValue stays excluded
- **GIVEN** a generated member declares `JavaScriptValue?` next to an
  `ArrayBuffer?` member
- **WHEN** the generator runs
- **THEN** it SHALL report the member's existing diagnostic for
  `JavaScriptValue?` and no diagnostic for `ArrayBuffer?`

### Requirement: Nullish Callback Parameters Decode To Null Without Retention

`JavaScriptCallback<TResult>?` and `JavaScriptCallback<TArgs, TResult>?`
SHALL be accepted as parameters of generated module methods, sync and async,
and of supported shared-object constructors and methods, including with an
authored `= null` default. They SHALL resolve to
`NullableJavaScriptCallbackCodec<...>` with the same type arguments as the
non-null codec, `DecodeOnly`, requiring `DotnetRuntimeContext`, and
`ContextRetained`. JavaScript `null` and `undefined` SHALL decode to C#
`null` without calling `AsFunction` and without registering a callback with
the context. A non-null function SHALL decode through the non-null codec
unchanged. A nullable callback is never a leaf holder: generated glue SHALL
NOT release or dispose it at invocation end. Its argument and result types
follow the non-null rule, so they can hold leaves at any depth.

#### Scenario: Nullish callback reaches authored code as null
- **GIVEN** a method with a required `JavaScriptCallback<int>?` parameter
  and a method with an optional
  `JavaScriptCallback<ValueTuple<string>, string>? cb = null` parameter
- **WHEN** JavaScript passes `null`, passes `undefined`, or omits the
  optional argument
- **THEN** authored code SHALL receive C# `null`
- **AND** the context's retained callback count SHALL NOT change

#### Scenario: Non-function non-null value is rejected
- **GIVEN** the required nullable callback parameter
- **WHEN** JavaScript passes a number
- **THEN** the call SHALL throw a JavaScript error from the function check
- **AND** the retained callback count SHALL NOT change

#### Scenario: Non-null callback keeps context-retained lifetime
- **GIVEN** the same parameters
- **WHEN** JavaScript passes a function
- **THEN** authored code SHALL invoke it synchronously and after the method
  returns, including after an `await` on an async method
- **AND** the retained callback count SHALL increase by one per decode
- **AND** explicit `Dispose` SHALL release once, and context teardown SHALL
  release an undisposed callback once

#### Scenario: Leaf-bearing nullable callback follows the leaf rules
- **GIVEN** a `JavaScriptCallback<ValueTuple<ArrayBuffer>, ArrayBuffer>?`
  parameter
- **WHEN** JavaScript passes `null`, or a function that returns another
  buffer that JavaScript already holds
- **THEN** authored code SHALL receive C# `null` for `null`
- **AND** for a function, the buffer the author passes SHALL stay the
  author's, and the returned buffer SHALL be the author's to dispose

#### Scenario: Shared-object parameters accept nullable callbacks
- **GIVEN** a shared-object `[JS]` constructor and method with a nullable
  callback parameter
- **WHEN** JavaScript passes `null` and a function
- **THEN** they SHALL behave as the module positions

### Requirement: Nullable Concrete Shared Objects Bypass The Registry On Null

A top-level, non-generic, sealed `[ExpoSharedObject]` class annotated as
nullable, including a concrete subclass of `SharedRef<T>`, SHALL be accepted
on every boundary that accepts the non-null class and SHALL select
`NullableSharedObjectCodec<T>` with the non-annotated exact type. JavaScript
`null` and `undefined` SHALL decode to C# `null` without resolving registry
identity. C# `null` SHALL encode as JavaScript `null` without class
registration or pairing. A non-null value SHALL use `SharedObjectCodec<T>`
unchanged. A shared object is registry-owned, never a leaf holder: generated
glue SHALL NOT release or dispose it at invocation end.

#### Scenario: Null parameter, return, and property
- **GIVEN** a module with a `T?` parameter, a `T?` return, a `Task<T?>`
  method, and a readable and writable `T?` property
- **WHEN** JavaScript passes `null` or `undefined`, and authored code
  returns or stores `null`
- **THEN** authored code SHALL receive `null`, and JavaScript SHALL receive
  `null`
- **AND** no pairing step SHALL be observed

#### Scenario: Non-null keeps identity
- **GIVEN** the same members
- **WHEN** JavaScript passes a live instance and authored code returns the
  same instance twice
- **THEN** authored code SHALL receive the original managed instance
- **AND** both JavaScript results SHALL be strictly equal to the input

#### Scenario: Existing failures stay
- **GIVEN** the `T?` parameter
- **WHEN** JavaScript passes a plain object, an instance of another shared
  class, an instance from another context, or a released instance
- **THEN** the call SHALL fail as it does for `T`

#### Scenario: Concrete SharedRef subclass
- **GIVEN** a sealed `[ExpoSharedObject]` class that derives from
  `SharedRef<X>` used as `T?`
- **WHEN** it crosses the boundary as `null` and as an instance
- **THEN** it SHALL behave as the scenarios above and SHALL NOT dispose its
  carried value

#### Scenario: Bases and nested shapes stay rejected
- **GIVEN** `SharedObject?`, `SharedRef<X>?`, `IReadOnlyList<T?>`, a record
  with a `T?` field, and `Func<T?, Task>`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the
  existing reasons

### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults

A generated optional nullable reference parameter SHALL use the authored C#
default for omission or explicit JavaScript `undefined`. Explicit JavaScript
`null` SHALL decode as C# `null`. A required nullable reference parameter SHALL
decode explicit JavaScript `undefined` as C# `null`. This matches the existing
optional nullable value-type behavior.

#### Scenario: Optional nullable reference is omitted
- **GIVEN** a generated method declares an optional nullable reference parameter
  with a C# default
- **WHEN** JavaScript omits the argument or passes explicit `undefined`
- **THEN** generated dispatch SHALL pass the authored C# default

#### Scenario: Optional nullable reference receives explicit null
- **GIVEN** a generated method declares an optional nullable reference parameter
  with a non-null C# default
- **WHEN** JavaScript passes explicit `null`
- **THEN** generated dispatch SHALL pass C# `null`
- **AND** it SHALL NOT substitute the authored default

#### Scenario: Required nullable reference receives undefined
- **GIVEN** a generated method declares a required nullable reference parameter
- **WHEN** JavaScript passes explicit `undefined`
- **THEN** generated dispatch SHALL pass C# `null`

### Requirement: Generated Parameter Defaults Render Every Constant

The generator SHALL render each C# parameter default that a parameter type it
accepts can hold as a C# expression that compiles in generated glue and gives
the same constant. This SHALL apply to module method parameters, shared-object
constructor and method parameters, and record fields. It SHALL cover `null`,
`bool`, `string`, the ten numeric primitives (including `NaN`, the infinities
and negative zero for `float` and `double`), enum values (including a nullable
enum, a `[Flags]` combination and a value that names no member) as a cast of the
underlying constant, and `default(T)` for a non-nullable value type, including
a byte span, whose default is `default` or `new()`. The generator SHALL NOT
throw for a default value, SHALL NOT emit `null` for a non-nullable value type,
and its output SHALL NOT depend on the culture or runtime that hosts the
compiler.

#### Scenario: Enum parameter default
- **GIVEN** a module declares `[JS] public string Pick(Mode mode = Mode.Fast)`
  and a shared object declares `[JS] public Picker(Mode mode = Mode.Fast)`
- **WHEN** the project is compiled and JavaScript calls `pick()` and
  `new Picker()`
- **THEN** the generator SHALL report no diagnostic and SHALL NOT fail
- **AND** authored code SHALL receive `Mode.Fast`

#### Scenario: Value-type default
- **GIVEN** a module declares `[JS] public string Wait(TimeSpan delay = default)`
- **WHEN** JavaScript calls `wait()`
- **THEN** generated glue SHALL compile and authored code SHALL receive
  `TimeSpan.Zero`

#### Scenario: Small integer default
- **GIVEN** a module declares `[JS] public int Small(byte value = 5)`
- **WHEN** JavaScript calls `small()`
- **THEN** generated glue SHALL compile and authored code SHALL receive `5`

#### Scenario: Non-finite and negative-zero defaults
- **GIVEN** a module declares `[JS] public double Edge(int which, double nan =
  double.NaN, double low = double.NegativeInfinity, double zero = -0.0)` that
  returns the chosen parameter
- **WHEN** JavaScript calls `edge(0)`, `edge(1)` and `edge(2)`
- **THEN** JavaScript SHALL receive `NaN`, `-Infinity` and a value for which
  `Object.is(value, -0)` is true

### Requirement: Nullable Reference Codecs Apply Across Generated Binding Surfaces

Supported nullable reference codecs SHALL apply to method parameters,
synchronous returns, `Task<T?>` results, optional arguments, readable and
writable `[JS]` properties, positional record fields, supported collection
container and nested positions, typed event payloads, and shared-object
constructors and `[JS]` members. Classification and composition SHALL happen at
build time, and generated glue SHALL stay NativeAOT-compatible without runtime
reflection, dynamic invocation, JSON conversion, a new C ABI entry, or a
platform-specific dependency.

This requirement covers safe nullable reference values used by a shared-object
member. A nullable shared-object instance type follows "Nullable Concrete Shared
Objects Bypass The Registry On Null" instead and stays rejected in records,
collections, callbacks, and typed event payloads.

#### Scenario: Generated return paths encode null
- **GIVEN** a synchronous generated method or a `Task<T?>` result returns C#
  `null` for a supported nullable reference type
- **WHEN** generated glue encodes the result
- **THEN** JavaScript SHALL receive `null`
- **AND** an async method's promise SHALL resolve with `null`

#### Scenario: Nullable property round-trips
- **GIVEN** a readable and writable `[JS]` property uses a supported nullable
  reference type
- **WHEN** JavaScript assigns and reads null and non-null values
- **THEN** the setter and getter SHALL use the same nullable codec
- **AND** null SHALL round-trip as JavaScript `null`

#### Scenario: Nullable record field round-trips
- **GIVEN** a generated positional record has a supported nullable reference
  field
- **WHEN** the record crosses the generated boundary
- **THEN** the field SHALL preserve null and non-null values through its
  generated field codec

#### Scenario: Event and shared-object members use safe nullable values
- **GIVEN** a typed event payload, a shared-object constructor parameter, or a
  shared-object `[JS]` member uses a supported nullable reference value
- **WHEN** generated binding or event glue converts the value
- **THEN** it SHALL use the same nullable codec composition as module methods
- **AND** C# `null` SHALL encode as JavaScript `null`

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
apply unchanged to the public surface. Cross-runtime pairing and a
`JavaScriptObject` codec remain separate future capabilities;
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

The generated factory SHALL release leaf-bearing constructor arguments in a
`finally`, after the factory returns the instance or throws. Registry pairing
is unchanged.

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
constructor parameter, or property type, with or without a nullable annotation.
The codec SHALL be bound to the current `DotnetRuntimeContext` and exact managed
class. `SharedObject`, `SharedRef<T>`, and other polymorphic base types SHALL
NOT be generated-boundary codec types; an authored abstract base is the one
decode-only exception (see "Authored Abstract Bases Decode As Parameters") and
SHALL NOT be an encode-side codec type.

Decoding SHALL read the private NativeState token through the existing
registry, require an active entry owned by the current context, validate the
exact expected managed runtime type, and return the original managed instance.
Assignable base/derived compatibility SHALL NOT select an exact codec or a
prototype. An authored abstract base is decoded only through
`SharedObjectBaseCodec<TBase>` (see "Authored Abstract Bases Decode As
Parameters"). Decoding SHALL NOT construct, clone, or substitute a managed
object.

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

#### Scenario: Span function returns a shared object
- **GIVEN** a synchronous `[JS]` method has one `Span<byte>` or
  `ReadOnlySpan<byte>` parameter and returns a valid `[ExpoSharedObject]` class
- **WHEN** the method is generated
- **THEN** the return encode SHALL receive the current `DotnetRuntimeContext`
- **AND** the generated source SHALL compile

### Requirement: Authored Abstract Bases Decode As Parameters

A top-level, non-generic, abstract class whose base chain reaches
`SharedObject`, that is not `SharedObject` or `SharedRef<T>` itself, and that
carries no `[ExpoSharedObject]` attribute, SHALL be accepted as a parameter type
of generated module methods and of supported shared-object constructors and
methods. It SHALL select `SharedObjectBaseCodec<TBase>` with the descriptor
`StaticHelper`, `DecodeOnly`, requiring `DotnetRuntimeContext`, and `Borrowed`.
Decoding SHALL resolve the registry entry exactly as the exact codec does and
SHALL accept the managed instance when it is an instance of `TBase`; it SHALL
NOT construct, clone, or substitute an instance. The codec has no `Release`: a
shared object is registry-owned, never a leaf holder. The same type as a
return, async result, or property, with a nullable annotation, nested,
generic, or inside a composed codec SHALL report `EXPOJSI023` with a reason
that names the rule. A concrete non-sealed class SHALL keep its existing
diagnostic.

#### Scenario: Base parameter receives any generated subclass
- **GIVEN** an abstract `PathBase : SharedObject` with generated sealed
  subclasses `FileEntry` and `DirEntry`, a module method
  `string Describe(PathBase path)`, an async method
  `Task<string> DescribeLater(PathBase path)`, and a shared-object method
  `string Compare(PathBase other)` and constructor
  `DirEntry(string name, PathBase parent)`
- **WHEN** JavaScript passes a `FileEntry`, a `DirEntry`, and a JavaScript
  subclass instance of `FileEntry`
- **THEN** authored code SHALL receive the original managed instance each
  time, typed as `PathBase`
- **AND** no pairing step SHALL be observed during decode

#### Scenario: Existing failures stay
- **GIVEN** the `PathBase` parameter
- **WHEN** JavaScript passes a plain object, a primitive, `null`,
  `undefined`, a released instance, an instance from another context, or an
  instance of a shared class that does not derive from `PathBase`
- **THEN** the call SHALL fail before authored code runs
- **AND** the message for the unrelated class SHALL be the base codec's
  `is not a '...PathBase' instance` text

#### Scenario: Non-parameter positions and the nullable form stay rejected
- **GIVEN** `PathBase` as a return type, a `Task<PathBase>` result, a
  property type, a `PathBase?` parameter, a nested `PathBase` inside a
  list, dictionary, record, callback, or typed event payload, a generic or
  nested abstract base, and `SharedObject` and `SharedRef<X>` parameters
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the reason
  the design names, and SHALL NOT emit the member

#### Scenario: Concrete non-sealed classes keep their diagnostic
- **GIVEN** a non-abstract, non-sealed class deriving from `SharedObject`
  used as a parameter, with and without `[ExpoSharedObject]`
- **WHEN** the generator runs
- **THEN** it SHALL report the existing `EXPOJSI023` reasons

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

### Requirement: Shared Objects Declare Typed Awaitable Events

`[Event]` SHALL be valid on an `[ExpoSharedObject]` class only when it is an
instance, getter-only partial property of exactly `Func<Task>` or
`Func<T, Task>`, and `T` has an event-safe compile-time codec. Implicit names
SHALL lowercase only the first C# property character; explicit non-empty names
SHALL be preserved verbatim. The generator SHALL cache one delegate per
managed instance and initialize it during the first exact registered-class
pairing. Reading it before pairing SHALL fail clearly.

#### Scenario: Shared-object event is generated

- **GIVEN** a shared object declares
  `[Event] public partial Func<ProgressEvent, Task> OnProgress { get; }`
- **WHEN** its instance first pairs with its generated JavaScript class
- **THEN** the property SHALL return a cached awaitable delegate
- **AND** awaiting it SHALL dispatch `onProgress` with the generated payload
  codec

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

### Requirement: SharedObject Bindings Are Fully Generated And Portable

Shared-object discovery, validation, construction, member dispatch, and codec
selection SHALL be build-time generated and NativeAOT-compatible. The runtime
path SHALL use direct calls, typed codecs, context-owned generated host-function
registrations, and the existing managed registry and `Expo.JSI` primitives.
Private generated record-codec type names SHALL derive from fully qualified
record identity so distinct payload records with the same simple name do not
share or suppress one another's codec.
It SHALL NOT use runtime reflection, dynamic invocation, JSON conversion, raw
JSI layouts, a new C ABI entry, or a platform-specific dependency.

#### Scenario: Same simple-name event records keep distinct codecs

- **GIVEN** a module event and a shared-object event use distinct payload
  records with the same simple type name in different namespaces
- **WHEN** the generator emits and runs both dispatch paths
- **THEN** each path SHALL encode with the codec for its fully qualified payload
  type
- **AND** both generated codec declarations SHALL compile in one provider

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

### Requirement: Expo Constants Dotnet Exposes Typed Host Values

`packages/expo-constants-dotnet` SHALL register `ExponentConstants` only under
`_expoDotnet.modules` and export a read-only TypeScript facade through its own
package. It SHALL expose exactly six getter-only `[JS]` properties:

| Property | Type | Source |
| --- | --- | --- |
| `platform` | `windows` or `macos` | Portable .NET OS APIs. |
| `executionEnvironment` | `bare` | This app-owned host is not Expo Go. |
| `sessionId` | canonical GUID string | A new value per `DotnetRuntimeContext` that instantiates the module. |
| `nativeAppVersion` | nullable string | macOS main-bundle `CFBundleShortVersionString`; null on Windows or when unavailable. |
| `nativeBuildVersion` | nullable string | macOS main-bundle `CFBundleVersion` or Windows package identity's four-part version; null when unavailable. |
| `expoVersion` | null | Expo Go version does not apply. |

The two native version properties SHALL remain available even though upstream
`expo-constants@57.0.2` removed them. The package SHALL NOT export
`expoConfig`, other compatibility fields, or methods. It SHALL NOT install an
upstream package alias, use `NativeModulesProxy`, or register into the Expo
global registry. Access on Android, iOS, Linux, or an unknown OS SHALL fail
through a catchable `PlatformNotSupportedException`. It SHALL NOT read device
or installation identity or persist `sessionId`.

#### Scenario: Native versions retain their named sources
- **GIVEN** a desktop host supplies its native versions through
  `DotnetRuntimeContext.AppMetadata`
- **WHEN** JavaScript reads `ExponentConstants`
- **THEN** the version properties SHALL match those values, with missing
  values null
- **AND** no assembly, package, or process version SHALL replace a missing
  value

#### Scenario: Constants are read-only and runtime-scoped
- **GIVEN** the module is registered in one runtime context
- **WHEN** JavaScript reads its properties repeatedly and attempts strict-mode
  assignment
- **THEN** the values and `sessionId` SHALL remain stable, and assignment
  SHALL fail with `TypeError`
- **AND** a new runtime context SHALL receive a different `sessionId`

#### Scenario: Unsupported host accesses constants
- **GIVEN** the module is registered on a host other than Windows or macOS
- **WHEN** JavaScript first accesses it
- **THEN** it SHALL receive a catchable unsupported-platform error

### Requirement: Expo Asset Dotnet Is A Standalone Authored Module

`packages/expo-asset-dotnet` SHALL provide the Windows and macOS authored
module package for asset download and caching. Its TypeScript facade SHALL
export the `expo-asset@57.0.2`-compatible native function signature
`downloadAsync(url: string, md5Hash: string | null, type: string):
Promise<string>`. The generated module SHALL register as `ExpoAsset` only under
`_expoDotnet.modules` through the existing dotnet autolinking path.

The package SHALL NOT resolve or reuse the upstream `Asset` class, install a
package alias, register under `globalThis.expo`, branch on the runtime operating
system, or add a platform-specific dependency to the managed implementation.
Filesystem I/O, HTTP, and hashing SHALL use .NET APIs and SHALL add no C ABI
surface.

#### Scenario: JavaScript downloads through the dotnet module

- **GIVEN** an app has autolinked `expo-asset-dotnet`
- **WHEN** JavaScript imports and calls its `downloadAsync` facade
- **THEN** the facade SHALL resolve `_expoDotnet.modules.ExpoAsset`
- **AND** it SHALL forward `url`, `md5Hash`, and `type` in that order

#### Scenario: The package remains standalone

- **GIVEN** the app does not install the upstream `expo-asset` JavaScript
  package
- **WHEN** the facade evaluates
- **THEN** it SHALL NOT resolve an upstream native module or `Asset` class
- **AND** its public function SHALL remain available through its own package

### Requirement: Expo Asset Dotnet Uses Strict App-Scoped Cache Inputs

The module SHALL accept only a non-empty absolute `file`, `http`, or `https`
URL. `type` SHALL match `^[A-Za-z0-9]{1,16}$`. `md5Hash` SHALL be `null` or 32
hexadecimal characters and SHALL be normalized to lowercase. This stricter
filename-input validation is an intentional security divergence from
`expo-asset@57.0.2`.

A `file:` URL SHALL return byte-for-byte unchanged before any cache access. An
HTTP request SHALL read its cache root lazily and only from
`DotnetRuntimeContext.CacheDirectory`, append the `ExponentAsset`
subdirectory, and reject an invalid or unavailable host root without an
environment or user-directory fallback. The final path SHALL be checked for
containment within that subdirectory.

The cache identifier SHALL be the supplied normalized hash, or the lowercase
MD5 of the original URL when no hash is supplied. The filename SHALL be
`ExponentAsset-<cache-id>.<type>`. An existing no-hash path SHALL be an
unconditional hit. A supplied hash SHALL be compared with the existing file's
MD5; an unreadable or mismatched file SHALL be a cache miss. Freshly downloaded
bytes SHALL NOT be rejected when they differ from the supplied hash.

#### Scenario: File URL bypasses host cache policy

- **GIVEN** `downloadAsync` receives a valid `file:` URL
- **WHEN** the module handles the request
- **THEN** it SHALL return the original URL unchanged
- **AND** it SHALL NOT read `DotnetRuntimeContext.CacheDirectory` or send an
  HTTP request

#### Scenario: Unsafe filename input is rejected

- **GIVEN** `type` or `md5Hash` does not match its strict accepted shape
- **WHEN** the module validates the request
- **THEN** it SHALL reject before creating a cache path
- **AND** arbitrary path syntax SHALL NOT reach filename construction

### Requirement: Expo Asset Dotnet Owns Download Lifetime And Failures

The package SHALL own its `HttpClient`, responses, streams, hash objects, and a
module-scoped cancellation source. HTTP responses SHALL stream into a unique
same-directory `*.download` file and move into place with overwrite only after
the write succeeds. Failure, caller cancellation, and module destruction SHALL
leave no temporary file. A pre-cancelled HTTP request SHALL reject before a
cache hit is returned.

Caller cancellation SHALL remain an `OperationCanceledException`. An
`OperationCanceledException` raised by the HTTP handler or response stream
while the caller token is live SHALL use the controlled download or save error
family. Download errors SHALL start with `Unable to download asset from url:
'<url>'`; save errors SHALL start with `Unable to save asset to directory:
'ExponentAsset'`.

The package SHALL keep its full validation, cache, HTTP, concurrency,
cancellation, and cleanup matrix in pure offline tests with injected cache and
HTTP dependencies. A small Hermes-backed set SHALL verify generated module
visibility, `file:` passthrough, validation rejection, and teardown. Facade and
real metadata tests SHALL run through the package's Vitest suite.

#### Scenario: Download succeeds atomically

- **GIVEN** an HTTP response succeeds for a cache miss
- **WHEN** the response body is saved
- **THEN** it SHALL first be written to a unique same-directory temporary file
- **AND** only the completed file SHALL replace the final cache path

#### Scenario: Timeout-style cancellation uses a controlled error

- **GIVEN** the caller token is still live
- **WHEN** the HTTP handler or response stream throws
  `OperationCanceledException`
- **THEN** the module SHALL reject with the matching controlled outer error
- **AND** it SHALL retain the cancellation as the inner exception
- **AND** no temporary file SHALL remain

### Requirement: Expo File System Dotnet Exposes The Upstream Local-File Shape

`packages/expo-file-system-dotnet` SHALL register a native module named
`FileSystem` only under `_expoDotnet.modules`, with shared-object classes
`FileSystemFile` and `FileSystemDirectory`. Every exported native member SHALL
have the upstream `expo-file-system` name, sync or async form, and value
types. Option records (`CreateOptions`, `InfoOptions`, `WriteOptions`,
`RelocationOptions`) SHALL declare every field as a non-nullable type with
the upstream default as a C# record parameter default, so an omitted or
`undefined` field takes the default and an explicit JavaScript `null` field
fails before authored code runs. An options parameter SHALL stay nullable, so
an omitted, `undefined`, or `null` options argument takes every default. The
package SHALL NOT export a stub, a throwing placeholder, or a differently
shaped substitute for an upstream member it does not implement. It SHALL add
no C ABI surface.

The facade SHALL export each TypeScript type under its upstream
`expo-file-system` name: `FileCreateOptions` for `File.create`,
`DirectoryCreateOptions` for `Directory.create`, `FileDigestAlgorithm`,
`FileInfo`, `DirectoryInfo`, `InfoOptions`, `PathInfo`, `FileWriteOptions`,
`RelocationOptions`, and the runtime enum `EncodingType`. It SHALL NOT export
`CreateOptions` or any other name or alias that upstream does not export.

Precondition failures SHALL surface as subclasses of `FileSystemException`, a
`CodedException`. Each subclass with an upstream counterpart SHALL carry the
upstream class name, so JavaScript sees the upstream `code`, and its
`Exception.Message` SHALL be the upstream message. I/O failures inside an
operation SHALL reach JavaScript with the .NET message and no `code`.

#### Scenario: Facade subclasses the native classes

- **GIVEN** the facade defines `class File extends FileSystemFile`
- **WHEN** JavaScript evaluates `new File(Paths.document, 'a.txt')`
- **THEN** the object SHALL inherit from `File.prototype`
- **AND** `instanceof File` and `instanceof FileSystemFile` SHALL be `true`
- **AND** native members SHALL operate on the canonical location
- **AND** the scenario SHALL be proven in the ES5 form a derived class
  compiles to (`Reflect.construct` with `newTarget`), because the testhost
  Hermes does not compile ES6 `class`

#### Scenario: Unimplemented members are absent

- **GIVEN** a member needs a core feature that is not available yet
- **WHEN** the package's types and native module are inspected
- **THEN** the member SHALL NOT exist on either

#### Scenario: Unsupported operating system

- **GIVEN** the module runs on an OS other than Windows or macOS
- **WHEN** JavaScript reads a member
- **THEN** it SHALL throw a catchable error from
  `PlatformNotSupportedException`

#### Scenario: Option record defaults

- **GIVEN** `create`, `info`, `write`, and `copy` calls
- **WHEN** JavaScript omits the options, passes `{}`, or passes a field as
  `undefined`
- **THEN** each field SHALL take its upstream default (`false`, and `'utf8'`
  for `encoding`)
- **AND** an explicit `null` for a `bool` or `encoding` field SHALL fail
  before authored code runs

#### Scenario: Explicit null in an existing option record fails

- **GIVEN** a missing file location, an existing directory, and an existing
  file
- **WHEN** JavaScript calls `create({ overwrite: null })` on the file
  location, `create({ idempotent: null })` on the directory, and
  `info({ md5: null })` on the existing file
- **THEN** each call SHALL throw before authored code runs, and the
  filesystem SHALL be unchanged
- **AND** `create(null)` on the file location and `info(null)` on the
  existing file SHALL succeed with every default

#### Scenario: Facade exports the upstream type names

- **GIVEN** the package's public TypeScript entry point `src/index.ts`
- **WHEN** a caller imports types from it
- **THEN** `FileCreateOptions`, `DirectoryCreateOptions`,
  `FileDigestAlgorithm`, `FileWriteOptions`, and `RelocationOptions` SHALL
  resolve, and `FileCreateOptions` SHALL have no `idempotent` key
- **AND** an import of `CreateOptions` SHALL fail to compile

#### Scenario: Errors carry upstream codes

- **GIVEN** an existing file and a `File` object at its location
- **WHEN** `create()` runs, synchronously, and
  `copy(directoryThatDoesNotExist)` runs through the async form
- **THEN** the thrown error SHALL have `code` `ERR_FILE_ALREADY_EXISTS` and
  message `File '<uri>' already exists`
- **AND** the rejection SHALL have `code` `ERR_DESTINATION_DOES_NOT_EXIST`
  and message `The destination path does not exist`

### Requirement: Expo File System Dotnet Canonicalizes Locations

Every location SHALL be parsed by one tested value object before any
`System.IO` call. Inputs SHALL be a `file:` URI or an absolute native path.
Relative paths, other schemes, malformed percent escapes, Windows device
namespace paths, and a `file:` authority on macOS SHALL be rejected with
`Invalid file system location: '<input>'`. An invalid entry name SHALL be
rejected with `Invalid file name: '<name>'`. The canonical `uri` SHALL resolve
`.` and `..`, SHALL end with `/` exactly for directories, and SHALL round-trip
Windows drive letters, UNC authorities, and percent encoding. The drive letter
SHALL be canonicalized to uppercase.

Location parsing SHALL also reject, before any I/O:

- On the Windows flavor only, any device prefix with a separator (`\` or `/`)
  at positions 0, 1, and 3 and `.` or `?` at position 2.
- A UNC host, in native form or as a percent-decoded URI authority, that does
  not match `[A-Za-z0-9._-]+` or that consists only of dots.
- On the Windows flavor, a segment that ends with `.` or a space. Win32 strips
  them, which would alias another name past the bundle-tree check. `.` and `..`
  are exempt because they are normalized first.
- On the Windows flavor, a segment that contains `<`, `>`, `:`, `"`, `|`, `?`,
  `*`, or a character from U+0001 to U+001F. NTFS stream syntax such as
  `X::$INDEX_ALLOCATION` names directory `X` and would alias it past the
  bundle-tree check. The drive designator (`C:`) is not a segment and SHALL
  still parse.
- On the Windows flavor, an entry name passed to name validation that ends with
  `.` or a space, or that contains any character from the previous rule. A name
  with `:` SHALL remain valid on the Posix flavor.
- Input that is not valid UTF-16, such as a lone surrogate.

#### Scenario: Windows path becomes a file URI

- **GIVEN** the Windows path `C:\a b\x.txt`
- **WHEN** a `FileSystemFile` is created from it
- **THEN** `uri` SHALL be `file:///C:/a%20b/x.txt`

#### Scenario: UNC authority is preserved

- **GIVEN** the Windows path `\\server\share\x.txt`
- **WHEN** it is canonicalized
- **THEN** `uri` SHALL be `file://server/share/x.txt`

#### Scenario: Invalid input is rejected before I/O

- **GIVEN** a relative path, an `http:` URI, a malformed escape, or a
  `\\?\` path
- **WHEN** a shared object is created from it
- **THEN** creation SHALL fail with `Invalid file system location: '<input>'`
- **AND** no filesystem call SHALL have run

### Requirement: Expo File System Dotnet Roots Come From The Runtime Context

`documentDirectory` and `cacheDirectory` SHALL be the `file:` URIs of
`DotnetRuntimeContext.PersistentFilesDirectory` and `CacheDirectory`, read
when accessed. An unconfigured directory SHALL surface as a JavaScript error
and SHALL NOT fall back to another path. `bundleDirectory` SHALL be a plain
path derived from `Environment.ProcessPath`: the enclosing `.app` directory
on macOS when the executable is in `Contents/MacOS/`, otherwise the
executable's directory.

#### Scenario: Unconfigured cache directory

- **GIVEN** the host supplied no cache directory
- **WHEN** JavaScript reads `cacheDirectory`
- **THEN** it SHALL throw a catchable error naming the unconfigured accessor

#### Scenario: Bundle tree is read-only

- **GIVEN** a location inside `bundleDirectory`
- **WHEN** `create`, `delete`, `rename`, `write`, or `writeSync` runs on it,
  `move` runs with it as source or destination, or `copy` runs with it as
  destination
- **THEN** it SHALL fail with `code` `ERR_MISSING_PERMISSION` and message
  `Missing permission for uri: <uri>`
- **AND** the filesystem SHALL be unchanged

#### Scenario: Bundle tree is a copy source

- **GIVEN** a file inside `bundleDirectory` and a writable directory
- **WHEN** `copySync(directory)` and `bytesSync()` run on the bundle file
- **THEN** both SHALL succeed and the bundle file SHALL be unchanged

### Requirement: Expo File System Dotnet Local Operations Match Upstream Semantics

`create`, `delete`, `rename`, `info`, `text`, `base64`, `bytes`, `write`,
`digest`, `copy`, `move`, `listAsRecords`, `createFile`, `createDirectory`,
and the metadata properties SHALL follow upstream iOS semantics, except where
a requirement below names a different rule. Validation SHALL run before side
effects. Async members SHALL NOT block the JavaScript thread and SHALL use the
locations captured when they start. A completed `move` and `rename` SHALL
update the object's location.

#### Scenario: Create without overwrite on an existing file

- **GIVEN** a file exists at the location
- **WHEN** `create()` runs with default options
- **THEN** it SHALL fail with `code` `ERR_FILE_ALREADY_EXISTS` and message
  `File '<uri>' already exists`
- **AND** the file contents SHALL be unchanged

#### Scenario: Idempotent directory create

- **GIVEN** a directory exists at the location
- **WHEN** `create({idempotent: true})` runs
- **THEN** it SHALL succeed without changing the directory

#### Scenario: Type mismatch

- **GIVEN** a directory exists at a `FileSystemFile` location
- **WHEN** any file operation runs, `write`, `bytes`, `copy`, and `move`
  included
- **THEN** it SHALL fail with `code` `ERR_INVALID_TYPE_FILE` and message
  `A folder with the same name already exists in the file location`

#### Scenario: Rename updates the object

- **GIVEN** an existing file
- **WHEN** `rename('b.txt')` runs
- **THEN** the file SHALL be renamed in its parent directory
- **AND** `uri` SHALL end with `/b.txt`

#### Scenario: Unsupported digest algorithm

- **GIVEN** an existing file
- **WHEN** `digest('SHA-3')` runs
- **THEN** the promise SHALL reject with `Unsupported digest algorithm: SHA-3`

### Requirement: Expo File System Dotnet Errors Carry Upstream Codes

`FileSystemException` SHALL be an internal abstract `CodedException`. Every
precondition failure of the module, and every error the module builds as a
`FileSystemException`, SHALL be one of its sealed subclasses below, with this
`code` and this `Exception.Message`. A code SHALL come from the class name by
the `CodedException` rule, except where the table marks it explicit. Failures
of .NET I/O calls inside an operation SHALL NOT be wrapped and SHALL reach
JavaScript with no `code`. `PlatformNotSupportedException` from the
unsupported-OS check and the core `AppDirectoryNotConfiguredException` are not
module errors and SHALL stay uncoded. The one exception to the no-wrap rule is
the `catch` block of `FileSystemFile.Create`, `FileSystemDirectory.Create`,
and `FileSystemPath.DeleteCore`: there an `IOException` or
`UnauthorizedAccessException` SHALL become `UnableToCreateException` or
`UnableToDeleteException`, with that exception's `Message` as `<reason>`.
`UnableToCreateException` and `UnableToDeleteException` SHALL have no inner
exception, so the JavaScript message shows the reason once and has no
`→ Caused by:` line.

| Class | Code | Message |
| --- | --- | --- |
| `InvalidLocationException` | `ERR_INVALID_LOCATION` | `Invalid file system location: '<input>'` |
| `InvalidNameException` | `ERR_INVALID_NAME` | `Invalid file name: '<name>'` |
| `InvalidTypeFileException` | `ERR_INVALID_TYPE_FILE` | `A folder with the same name already exists in the file location` |
| `InvalidTypeDirectoryException` | `ERR_INVALID_TYPE_DIRECTORY` | `A file with the same name already exists in the directory location` |
| `WrongTypeException` | `ERR_WRONG TYPE` (explicit, the iOS code for `name: "wrong type"`) | `tried to create a file with a directory path`, or `tried to create a directory with a file path` |
| `FileAlreadyExistsException` | `ERR_FILE_ALREADY_EXISTS` | `File '<uri>' already exists` |
| `UnableToCreateException` | `ERR_UNABLE_TO_CREATE` | `Unable to create file or directory: <reason>` |
| `UnableToDeleteException` | `ERR_UNABLE_TO_DELETE` | `Unable to delete file or directory: <reason>` |
| `MissingPermissionException` | `ERR_MISSING_PERMISSION` | `Missing permission for uri: <uri>` |
| `UnsupportedDigestAlgorithmException` | `ERR_UNSUPPORTED_DIGEST_ALGORITHM` | `Unsupported digest algorithm: <algorithm>` |
| `UnableToWriteBase64DataException` | `ERR_UNABLE_TO_WRITE_BASE64_DATA` | `Unable to write base64 data to a file: <uri>` |
| `CopyOrMoveDirectoryToFileException` | `ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE` | `Unable to copy or move a directory to a file` |
| `DestinationAlreadyExistsException` | `ERR_DESTINATION_ALREADY_EXISTS` | `Destination already exists` |
| `DestinationDoesNotExistException` | `ERR_DESTINATION_DOES_NOT_EXIST` | `The destination path does not exist` |
| `UnableToCopyException` | `ERR_UNABLE_TO_COPY` | `Unable to copy file or directory: <reason>` |
| `UnableToMoveException` | `ERR_UNABLE_TO_MOVE` | `Unable to move file or directory: <reason>` |
| `UnableToDetermineBundleDirectoryException` | `ERR_UNABLE_TO_DETERMINE_BUNDLE_DIRECTORY` | `Unable to determine the bundle directory` |
| `FileTooLargeException` | `ERR_FILE_TOO_LARGE` | `File is too large to read into memory: <uri>` |

#### Scenario: Sync and async failures carry the same code

- **GIVEN** a directory at a `File` location
- **WHEN** JavaScript calls `textSync()` and awaits `text()`
- **THEN** the throw and the rejection SHALL both have `code`
  `ERR_INVALID_TYPE_FILE` and the same message

#### Scenario: Unable-to-create errors show the reason once

- **GIVEN** a `File` whose parent directory is missing
- **WHEN** JavaScript calls `create()` without `intermediates`
- **THEN** the error SHALL have `code` `ERR_UNABLE_TO_CREATE` and a message
  that starts with `Unable to create file or directory: ` followed by the
  .NET failure message, because `FileSystemFile.Create` wraps an I/O failure
- **AND** the message SHALL NOT contain `→ Caused by:`

#### Scenario: Directory create and delete failures have no cause

- **GIVEN** `FileSystemDirectory.Create` or `FileSystemPath.DeleteCore` meets
  an `IOException` or `UnauthorizedAccessException`
- **WHEN** the module throws `UnableToCreateException` or
  `UnableToDeleteException`
- **THEN** the exception SHALL have no inner exception, and its message SHALL
  end with the caught exception's `Message`
- **AND** its JavaScript message SHALL NOT contain `→ Caused by:`

#### Scenario: I/O failures stay uncoded

- **GIVEN** a `File` whose parent directory is missing
- **WHEN** `writeSync('x')` runs
- **THEN** the error SHALL have the .NET message and `'code' in error` SHALL
  be `false`

### Requirement: Expo File System Dotnet Writes Files

`FileSystemFile.write` (async) and `writeSync` SHALL take `WriteContent`, a
`[Union]` of `string`, `ArrayBuffer`, and `Uint8Array`, and an optional
`WriteOptions`. Text SHALL be written as UTF-8 without a byte order mark. With
`Encoding == base64`, characters outside the base64 alphabet SHALL be
ignored, and content that still does not decode SHALL fail with
`UnableToWriteBase64DataException` before the file is touched. `Encoding`
SHALL be ignored for binary content. `Append == false` SHALL create or
truncate the file. `Append == true` SHALL create a missing file and otherwise
append. A missing file SHALL be created. A missing parent SHALL fail with the
.NET error. The async form SHALL copy binary content on the JavaScript thread
before it returns, and SHALL do file I/O off the JavaScript thread. The facade
SHALL pass any typed array other than `Uint8Array` and `DataView` as a
`Uint8Array` over the same bytes.

#### Scenario: Each content kind writes its bytes

- **GIVEN** a `File` at a missing location in an existing directory
- **WHEN** JavaScript calls `writeSync('zażółć')`, then
  `writeSync(new Uint8Array([1, 2, 3]).buffer)`, then
  `writeSync(new Uint8Array(buf, 2, 3))` over an 8-byte `buf`
- **THEN** after each call the file SHALL hold exactly the UTF-8 bytes of the
  text with no BOM, then `01 02 03`, then bytes 2 to 4 of `buf`

#### Scenario: Facade passes other typed arrays as bytes

- **GIVEN** the facade `File` and an `Int16Array` view at byte offset 2 of a
  buffer
- **WHEN** JavaScript calls `file.writeSync(view)`
- **THEN** the native member SHALL receive a `Uint8Array` over the same
  buffer, offset, and byte length, and the file SHALL hold those bytes
- **AND** the same `Int16Array` passed to the native class without the facade
  SHALL fail with the union mismatch error

#### Scenario: Append and truncate

- **GIVEN** a file that holds `abc`
- **WHEN** `writeSync('d', { append: true })` runs, and then `writeSync('e')`
- **THEN** the file SHALL hold `abcd`, and then `e`

#### Scenario: Base64 content

- **GIVEN** a file location
- **WHEN** `writeSync('AQID\n', { encoding: 'base64' })` runs, and then
  `writeSync('A', { encoding: EncodingType.Base64 })`
- **THEN** the first call SHALL write `01 02 03`
- **AND** the second SHALL fail with `code`
  `ERR_UNABLE_TO_WRITE_BASE64_DATA` and leave `01 02 03` in place

#### Scenario: Invalid content is rejected before the call

- **GIVEN** a `File`
- **WHEN** JavaScript passes `5`, `{}`, `null`, `undefined`, or a `DataView`
  as content, or `{ encoding: 'hex' }` as options
- **THEN** `writeSync` SHALL throw and `write` SHALL reject before authored
  code runs
- **AND** the file SHALL be unchanged

#### Scenario: Async write captures the content

- **GIVEN** a `Uint8Array` that holds `01 02`
- **WHEN** JavaScript calls `const p = file.write(view)`, sets `view[0] = 9`,
  and awaits `p`
- **THEN** the file SHALL hold `01 02`
- **AND** the view's buffer lease SHALL release exactly once

### Requirement: Expo File System Dotnet Reads Bytes

`FileSystemFile.bytes` (async) and `bytesSync` SHALL return a `Uint8Array`
with `byteOffset` 0 over a new buffer whose length equals the file length,
read through one open stream. An empty file SHALL return a zero-length view.
A file longer than `int.MaxValue` bytes SHALL fail before allocation with
`FileTooLargeException`. The async form SHALL read off the JavaScript thread.
Reading SHALL be allowed inside the bundle tree.

#### Scenario: Bytes round-trip

- **GIVEN** a file that holds bytes 0 to 255
- **WHEN** JavaScript calls `bytesSync()` and `await bytes()`
- **THEN** each result SHALL be a `Uint8Array` with `byteOffset` 0, `length`
  256, `buffer.byteLength` 256, and the file bytes

#### Scenario: Empty and missing files

- **GIVEN** an empty file and a missing file
- **WHEN** `bytesSync()` runs on each
- **THEN** the empty file SHALL give a zero-length `Uint8Array`
- **AND** the missing file SHALL throw with no `code`

### Requirement: Expo File System Dotnet Copies And Moves

`copy`, `copySync`, `move`, and `moveSync` on `FileSystemFile` and
`FileSystemDirectory` SHALL take a `FileSystemPath` destination and an
optional `RelocationOptions`. The async forms SHALL capture both locations
when they start and SHALL do file I/O off the JavaScript thread. Each call
SHALL run these checks in this order, all before any side effect:

1. Permission. `copy` SHALL require the destination outside the bundle tree.
   `move` SHALL require the source and the destination outside it. A failure
   SHALL be `MissingPermissionException` with that object's `uri`. After step
   3 resolves the target, the target SHALL also be outside the bundle tree,
   with the same error and the target's `uri`.
2. Type. The source and the destination SHALL each pass their type check
   (`InvalidTypeFileException` or `InvalidTypeDirectoryException`).
3. Target resolution, by this table:

| Source | Destination | Target | Failure |
| --- | --- | --- | --- |
| File | `Directory` that exists | `<destination>/<source name>` | none |
| File | `Directory` that does not exist | none | `DestinationDoesNotExistException` |
| File | `File` | the destination | `DestinationDoesNotExistException` when its parent is missing |
| Directory | `Directory` that exists | `<destination>/<source name>/` | none |
| Directory | `Directory` that does not exist | the destination | `DestinationDoesNotExistException` when its parent is missing |
| Directory | `File` | none | `CopyOrMoveDirectoryToFileException` |

4. Source existence. A missing source SHALL fail with the .NET
   `FileNotFoundException` or `DirectoryNotFoundException`, uncoded.
5. Same item. The stored path of an existing entry is its path with each
   segment replaced by the name that the parent directory listing stores for
   it. The source and the target are the same item when the target exists
   and both stored paths are equal ordinally. Then `copy` SHALL fail with
   `UnableToCopyException` (`source and destination are the same`). `move`
   SHALL fail with `UnableToMoveException` (same reason) when the two
   canonical URIs are also equal ordinally. When the URIs differ, the request
   changes only letter case, and `move` SHALL rename the entry in place and
   SHALL delete nothing.
6. Nesting. A directory target inside the source directory SHALL fail with
   `UnableToCopyException` or `UnableToMoveException`
   (`destination is inside the source`).
7. Existing target. Without `Overwrite`, an existing target SHALL fail with
   `DestinationAlreadyExistsException`. With `Overwrite`, the existing entry
   SHALL be deleted recursively, whatever its type, after every other check
   passed.

A directory copy SHALL copy every file and subdirectory, hidden entries
included, and SHALL copy a symbolic link inside the tree as a link. A failed
copy SHALL delete the target entries it created, SHALL rethrow the original
error, and SHALL NOT change the source. A copy whose target exists when the
copy starts, because another writer created it after the checks, SHALL fail
with `DestinationAlreadyExistsException` and SHALL delete nothing. A directory move that
`Directory.Move` cannot do because the target is on another volume SHALL copy
the tree and then delete the source. If that copy fails, the partial target
SHALL be removed and the source SHALL stay. If the source delete fails after a
complete copy, the call SHALL fail with `UnableToMoveException`
(`Failed to delete source after move`) and the complete target SHALL stay. A
completed move SHALL set the object's location to the target. A failed move
SHALL leave the location unchanged.

#### Scenario: File into a directory

- **GIVEN** a file `a.txt` and an existing directory `d/`
- **WHEN** `a.copySync(d)` runs, and then `a.moveSync(d, { overwrite: true })`
- **THEN** `d/a.txt` SHALL hold the source bytes after the copy
- **AND** after the move the source SHALL be gone, `a.uri` SHALL end with
  `/d/a.txt`, and `a.name` SHALL be `a.txt`

#### Scenario: Directory into an existing and a missing directory

- **GIVEN** a directory `s/` with a nested file and a hidden file, an existing
  directory `d/`, and a missing `n/` whose parent exists
- **WHEN** `s.copySync(d)` runs, and then `s.copySync(n)`
- **THEN** the tree SHALL be at `d/s/` and at `n/`, entry for entry

#### Scenario: Preconditions fail without side effects

- **GIVEN** a source file, a source directory, and an existing target
- **WHEN** a file is copied to a missing directory, a directory is copied to
  a `File`, a file is copied to an existing target without `overwrite`, a
  directory is copied into its own subdirectory, a file is copied onto itself
  with `overwrite`, and a missing source is copied with `overwrite` onto an
  existing target
- **THEN** the calls SHALL fail with `ERR_DESTINATION_DOES_NOT_EXIST`,
  `ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE`, `ERR_DESTINATION_ALREADY_EXISTS`,
  `ERR_UNABLE_TO_COPY`, `ERR_UNABLE_TO_COPY`, and an uncoded error
- **AND** the filesystem SHALL be unchanged after each call

#### Scenario: Async forms

- **GIVEN** a file and a directory destination
- **WHEN** JavaScript awaits `copy(d)` and `move(d)`, and a failing `move`
- **THEN** each SHALL return a Promise that resolves `undefined` after the
  filesystem change
- **AND** the failing `move` SHALL reject with its code and leave `uri`
  unchanged

#### Scenario: Case-only rename through move

- **GIVEN** a file `a.txt` on Windows or on a case-insensitive macOS volume
- **WHEN** `moveSync(new File(dir, 'A.txt'))` runs
- **THEN** the file SHALL be renamed to `A.txt` with its contents unchanged

#### Scenario: Symbolic links in a copied tree

- **GIVEN** a directory that holds a symbolic link to a file outside it
- **WHEN** the directory is copied
- **THEN** the copy SHALL hold a symbolic link with the same target, not a
  copy of the file
