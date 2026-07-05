# Module Runtime Context Groundwork

## Goal

Prepare `Expo.ModulesCore` for module-authored lifecycle APIs, future
EventEmitter support, and advanced managed JSI access by making runtime context
available from authored module instances without making inheritance mandatory.

The immediate change is module authoring and generated-binding groundwork. Event
emission itself remains out of scope for this delta.

## Assumptions

- Authored modules may continue to use a public parameterless constructor when
  they do not need runtime context.
- Authored modules that need runtime context may declare a public constructor
  accepting `DotnetRuntimeContext`.
- A convenience `Module` base class may exist, but authored modules are not
  required to inherit from it.
- If a module declares both supported constructors, generated registration
  prefers the `DotnetRuntimeContext` constructor.
- Direct `JavaScriptRuntime` access from an authored module is an advanced,
  thread-unsafe escape hatch. It does not replace scheduler APIs and does not
  marshal work onto the JavaScript runtime thread.
- EventEmitter implementation is deferred, but this groundwork should not make
  the future event contract harder to implement.

## Scope

### Included

- Extend generated module instantiation to support
  `new ModuleName(DotnetRuntimeContext context)` in addition to the existing
  parameterless constructor.
- Expose an optional `Expo.ModulesCore.Module` base class that stores the
  runtime context for ergonomic authored modules.
- Refactor `ModuleRegistry` from a static helper-only shape into a
  runtime-context-owned module registry while preserving clear helpers for
  JavaScript module object installation.
- Add an advanced `DotnetRuntimeContext.Runtime` accessor with documentation
  that states scheduler, thread-safety, disposal, and teardown constraints.
- Add `Expo.ModulesCore` codecs for low-level `Expo.JSI` value wrappers where
  ownership semantics can be preserved explicitly, starting with
  `JavaScriptValue`.
- Keep event payload conversion aligned with the existing
  `IJavaScriptCodec<T>` model.

### Excluded

- EventEmitter API implementation.
- Listener count tracking, `onStartObserving`, `onStopObserving`, event
  declaration syntax, or JavaScript event target dispatch.
- New ABI entries.
- Runtime-thread static analysis attributes or analyzers.
- Lazy module initialization through HostObject-backed dynamic property access.
- A runtime codec registry or reflection-based codec lookup.

## Accepted Design

`DotnetRuntimeContext` owns the runtime-scoped module registry and exposes the
advanced runtime wrapper. Generated provider code receives a
`DotnetRuntimeContext`, obtains the target JavaScript modules object as it does
today, and asks the context-owned registry to instantiate or reuse authored
module instances.

The registry is responsible for authored module identity and construction.
JavaScript object installation remains a separate responsibility inside
`Expo.ModulesCore`, even if implemented by the same `ModuleRegistry` type. The
public method names must make the distinction clear: authored module instance
lookup is not the same operation as creating or reusing
`globalThis._expoDotnet.modules`.

Module authors that want ergonomic context access may inherit from
`Expo.ModulesCore.Module`. Authors that need another base class may instead
accept `DotnetRuntimeContext` directly and store it themselves.

`DotnetRuntimeContext.Runtime` is intentionally low-level. Its documentation
must say that callers are responsible for using it only during valid runtime
access, for scheduling work when needed, and for disposing owned JSI wrappers.
The property is allowed in this groundwork because future events and advanced
modules need a direct path to JSI primitives, but normal generated method
bindings continue to use generated codecs and scheduling helpers.

`Expo.ModulesCore` may define codecs for `Expo.JSI` wrapper types because
codecs are generated-binding helpers above `Expo.JSI`, not module DSL syntax.
`JavaScriptValue` codec support must preserve owned-wrapper semantics while
keeping the default module-argument lifetime invocation-scoped and managed by
generated glue. It must not store borrowed/scoped values beyond their valid
lifetime.

## Delta Requirements

### MODIFIED Requirement: ModulesCore Owns Generated-Binding Helpers

`Expo.ModulesCore` SHALL own module registration helpers, generated dispatch
helpers, typed conversion helpers, and runtime-scoped authored module instance
helpers above `Expo.JSI`.

#### Scenario: Context-backed module is constructed

- **GIVEN** generated provider code has a `DotnetRuntimeContext`
- **AND** an authored module declares a public constructor accepting
  `DotnetRuntimeContext`
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL pass the current context to the constructor
- **AND** it SHALL use the resulting instance for generated function bindings

#### Scenario: Simple module is constructed

- **GIVEN** generated provider code has a `DotnetRuntimeContext`
- **AND** an authored module declares a public parameterless constructor
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL construct the module without requiring context access

#### Scenario: Module supports both constructors

- **GIVEN** an authored module declares both a public parameterless constructor
  and a public constructor accepting `DotnetRuntimeContext`
- **WHEN** generated registration instantiates that module
- **THEN** it SHALL prefer the `DotnetRuntimeContext` constructor

#### Scenario: Unsupported constructor shape is reported

- **GIVEN** an authored module declares no supported constructor
- **WHEN** the generator builds the module model
- **THEN** it SHALL report the existing unsupported-constructor diagnostic
- **AND** it SHALL suppress invalid generated registration for that module

### NEW Requirement: Authored Module Base Class Is Optional

`Expo.ModulesCore` MAY expose a convenience `Module` base class for authored
modules that want direct runtime-context access.

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

### MODIFIED Requirement: Runtime Context Owns Runtime-Scoped State

`DotnetRuntimeContext` SHALL own runtime-scoped authored module instances and
SHALL provide controlled access to the JavaScript runtime wrapper.

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

#### Scenario: Runtime accessor is documented as advanced

- **GIVEN** authored module code accesses `DotnetRuntimeContext.Runtime`
- **WHEN** the author reads the API documentation
- **THEN** the documentation SHALL state that the accessor does not marshal to
  the JavaScript runtime thread
- **AND** callers are responsible for using scheduler APIs when needed
- **AND** callers must not retain or use wrappers after runtime teardown
- **AND** owned wrappers returned from `Expo.JSI` APIs must be disposed according
  to their ownership contract

### NEW Requirement: JavaScript Wrapper Codecs Preserve Ownership

`Expo.ModulesCore` SHALL provide codec support for `Expo.JSI` wrapper types
only when the codec can preserve explicit owned-wrapper semantics.

#### Scenario: JavaScriptValue is accepted as a module argument

- **GIVEN** generated dispatch receives a `JavaScriptValue`
- **WHEN** it decodes that value through the `JavaScriptValue` codec
- **THEN** the authored method SHALL receive an owned wrapper that is valid for
  the generated invocation
- **AND** generated glue SHALL dispose that wrapper after the synchronous
  invocation returns or after the asynchronous invocation settles
- **AND** authored module code SHALL NOT dispose the argument wrapper
- **AND** authored module code SHALL NOT store the argument wrapper in module
  state or otherwise use it after the generated invocation lifetime

#### Scenario: Module author wants to retain a JavaScriptValue argument

- **GIVEN** authored module code receives a `JavaScriptValue` argument
- **WHEN** it needs to store the value beyond the generated invocation lifetime
- **THEN** it SHALL use an explicit retain or ownership-transfer API
- **AND** the retained wrapper SHALL become the module author's disposal
  responsibility

#### Scenario: JavaScriptValue argument ownership is documented

- **GIVEN** authored module code declares a `JavaScriptValue` argument
- **WHEN** the author reads the `JavaScriptValue` API documentation
- **THEN** the documentation SHALL state that generated module arguments are
  owned by generated glue for the invocation lifetime
- **AND** authored module code must not dispose or retain that argument wrapper
  unless it uses an explicit retain or ownership-transfer API

#### Scenario: JavaScriptValue is encoded

- **GIVEN** authored code returns a `JavaScriptValue`
- **WHEN** generated dispatch encodes that value through the `JavaScriptValue`
  codec
- **THEN** generated glue SHALL take ownership of the returned wrapper
- **AND** generated glue SHALL dispose the returned wrapper after producing the
  JavaScript return value or after the asynchronous invocation settles
- **AND** authored module code SHALL NOT dispose a wrapper after returning it to
  generated glue

#### Scenario: Module author returns a retained JavaScriptValue copy

- **GIVEN** authored module code owns a `JavaScriptValue`
- **WHEN** it needs to keep that original wrapper or dispose it locally
- **THEN** it SHALL return an explicit retained copy
- **AND** ownership of the retained copy SHALL transfer to generated glue
- **AND** ownership of the original wrapper SHALL remain with the module author

#### Scenario: Unsupported scoped value is rejected

- **GIVEN** a future borrowed or scoped JSI wrapper cannot be safely retained
- **WHEN** codec support is considered for that wrapper type
- **THEN** `Expo.ModulesCore` SHALL NOT expose a general-purpose generated
  codec until ownership and lifetime semantics are specified

### FUTURE Requirement Direction: EventEmitter Uses Context-Owned Services

Future EventEmitter work SHOULD build on the context-owned module registry,
runtime scheduling APIs, and `IJavaScriptCodec<T>` payload conversion.

#### Scenario: Future event payload is emitted

- **GIVEN** authored module code emits an event with payload type `T`
- **WHEN** EventEmitter support is implemented
- **THEN** the payload SHOULD be converted through an `IJavaScriptCodec<T>`
- **AND** event dispatch SHOULD schedule onto the JavaScript runtime path before
  touching JavaScript objects
- **AND** dispatch SHOULD stop safely when the runtime context is disposed or
  the JavaScript event target is no longer associated with the module instance
