# Modules Core Functions And Properties

## Purpose

Define generated sync and async module functions, `[JS]` properties, the
JavaScript errors that module failures become, and the build diagnostics for
unsupported signatures.

## Requirements

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

#### Scenario: Record field holds a callback
- **GIVEN** a generated record has a `JavaScriptCallback` field, or a field
  whose codec the generic-argument rule rejects
- **WHEN** a `[JS]` method parameter, return, async result, or shared-object
  constructor parameter uses the record
- **THEN** the generator SHALL report `EXPOJSI007` naming the record and field
- **AND** the using member SHALL report its unsupported-type diagnostic
- **AND** a `[JS]` property whose type contains a callback SHALL keep
  reporting only `EXPOJSI015`
- **AND** a record with a concrete shared-object field SHALL be accepted

#### Scenario: Async method returns a byte span
- **GIVEN** an async `[JS]` method returns `Task<Span<byte>>` or
  `Task<ReadOnlySpan<byte>>`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI002` naming the method and type
- **AND** the generator SHALL NOT fail or drop its other output

### Requirement: Generated Bindings Avoid Hot-Path Reflection

Generated v2 runtime bindings SHALL avoid runtime hot-path reflection and
dynamic invocation.

#### Scenario: Module provider invokes a method
- **GIVEN** generated provider code handles a JavaScript call
- **WHEN** it invokes the authored module method
- **THEN** it SHALL NOT use `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]` as the normal argument container, or
  JSON serialization for ordinary JSI values

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
