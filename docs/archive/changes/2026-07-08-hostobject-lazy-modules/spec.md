# HostObject And Lazy Dotnet Modules

## Goal

Add the generic HostObject primitive required by future dynamic object surfaces,
and use it for a one-stage lazy `_expoDotnet.modules` registry. The first
consumer is lazy module access; SharedObject and SharedRef are documented as
future work so their direction is visible without expanding this slice.

## Scope

This change adds:

- A low-level `Expo.JSI` HostObject ABI and managed wrapper surface.
- A one-stage lazy `_expoDotnet.modules` HostObject in `Expo.ModulesCore`.
- A required-module JavaScript lookup boundary that throws the autolinking
  guidance error when a required module is missing.
- A roadmap note that two-stage lazy module shells are a future performance
  optimization.
- Directional documentation for SharedObject and SharedRef planning.

This change does not add:

- SharedObject or SharedRef implementation.
- Two-stage `LazyObject` module shells.
- Symbol-key HostObject support.
- A stable public API guarantee for HostObject naming or helper types.

## Design

### Low-Level HostObject Primitive

`Expo.JSI` will expose HostObject as a generic JavaScript object primitive above
the C ABI and below module semantics. C++ continues to own JSI mechanics; C#
owns callback logic; the ABI connects them with opaque runtime and value
handles.

A HostObject descriptor provides:

- `get(propertyName)`, returning an owned JavaScript value.
- optional `set(propertyName, value)`, receiving a value valid for the callback
  invocation.
- optional `getPropertyNames()`, returning string property names.
- native-side cleanup of the managed callback context when the HostObject is
  destroyed.

Managed exceptions raised by HostObject callbacks become catchable JavaScript
errors. Cleanup callbacks must not touch JSI, must not throw, and must tolerate
runtime-context teardown having already happened.

### One-Stage Lazy Dotnet Modules

`Expo.ModulesCore` will install `_expoDotnet.modules` as a HostObject backed by
the app-composed generated module table. Access is one-stage lazy: the first
read of a known module property creates and caches the real JavaScript module
object and creates or reuses the authored C# module instance through
`DotnetRuntimeContext.ModuleRegistry`.

`_expoDotnet.modules.Camera` behavior:

- If `Camera` is registered, create/cache/return the real module object.
- If `Camera` is unknown, return JavaScript `undefined`.
- If the runtime context is disposed, throw a catchable JavaScript error.
- If the property is a known no-initialization probe such as `$$typeof`, return
  JavaScript `undefined` without creating a module.

`Object.keys(_expoDotnet.modules)` returns the generated/autolinked module names
without creating module instances. Assigning a property on the root modules
HostObject is rejected with a catchable JavaScript error.

### Required Module Lookup

Raw HostObject property reads preserve ordinary JavaScript object behavior for
unknown properties by returning `undefined`. Required module lookups are handled
above the HostObject layer. A generated JavaScript facade or shared helper that
requires a module will read `_expoDotnet.modules[moduleName]` and throw when the
result is `undefined`.

The required-module error should clearly identify the missing module and point
at autolinking:

```text
Module '<name>' is not registered. Check that it is autolinked correctly.
```

This keeps optional feature detection safe while preserving a loud failure for
required module APIs.

### Generated Module Table

The app-composed registration path supplies the lazy module registry with
build-time module metadata. The table includes at least:

- module name
- module object creation/installation callback
- authored module instance creation callback through `ModuleRegistry`

The module table must not rely on runtime hot-path reflection, JSON dispatch,
`object?[]` invocation, or dynamic invocation.

### Batch 2 Direction: SharedObject And SharedRef

SharedObject and SharedRef should follow the upstream direction: ordinary
JavaScript class/prototype instances with hidden native identity, not
HostObject-first objects. Shared object identity should be registry-backed and
hidden from JavaScript properties. The current type-indexed NativeState
primitive is the local model for hidden object-associated identity.

Because JSI does not natively attach NativeState to HostObject instances through
the existing generic object state path, this HostObject implementation may keep
an internal native-state store for HostObject instances. That store is useful
for HostObject-backed surfaces, but it does not change the planned
SharedObject/SharedRef default shape.

SharedObject and SharedRef remain a separate future batch covering:

- class/prototype hierarchy
- registry-backed native object pairing
- release semantics
- EventEmitter integration
- generated class metadata
- codecs for shared object references

### Roadmap Direction: Two-Stage Laziness

The current lazy registry is intentionally one-stage. If profiling later shows
root module property access creates measurable overhead, the registry can move
to a two-stage model similar to upstream `LazyObject`: root module access would
return a cached lazy shell, and the real module object would be materialized on
first access to the shell.

## Delta Requirements

### ADDED: HostObject ABI Boundary

The ABI SHALL expose a generic HostObject creation primitive using opaque
runtime and value handles. The HostObject ABI SHALL call managed callbacks for
string property get, optional property set, and property-name enumeration
without exposing raw JSI layouts to C#.

#### Scenario: HostObject gets a property
- **GIVEN** managed code creates a HostObject with a getter
- **WHEN** JavaScript reads a string property on that object
- **THEN** native SHALL call the managed getter through the ABI
- **AND** the getter result SHALL be returned to JavaScript as the property
  value
- **AND** C# SHALL NOT observe raw JSI value or object layouts

#### Scenario: HostObject getter throws
- **GIVEN** a HostObject getter throws a managed exception
- **WHEN** JavaScript reads the property
- **THEN** the exception SHALL be surfaced to JavaScript as a catchable `Error`
- **AND** no C++ or managed exception SHALL cross an unmanaged boundary

#### Scenario: HostObject set is unsupported
- **GIVEN** managed code creates a HostObject without a setter
- **WHEN** JavaScript assigns a property on that object
- **THEN** assignment SHALL fail with a catchable JavaScript error

#### Scenario: HostObject cleanup releases callback context
- **GIVEN** a HostObject is destroyed by JavaScript garbage collection or
  runtime teardown
- **WHEN** native releases the HostObject callback context
- **THEN** cleanup SHALL NOT touch JSI handles
- **AND** cleanup SHALL NOT throw across the unmanaged boundary
- **AND** cleanup SHALL tolerate the owning runtime context already being
  disposed

### ADDED: Managed HostObject Wrapper

`Expo.JSI` SHALL expose a managed HostObject creation API on
`JavaScriptRuntime`. The API SHALL remain below `Expo.ModulesCore` and SHALL NOT
know about `_expoDotnet`, module names, autolinking, SharedObject, or
SharedRef.

#### Scenario: Runtime creates a HostObject
- **GIVEN** managed code has a live `JavaScriptRuntime`
- **WHEN** it creates a HostObject from managed callbacks
- **THEN** it SHALL receive an owned `JavaScriptObject`
- **AND** that object SHALL dispatch property access through the supplied
  callbacks

#### Scenario: Setter receives invocation-scoped input
- **GIVEN** a HostObject setter receives a JavaScript value
- **WHEN** the setter callback is running
- **THEN** the value SHALL be valid for the setter invocation
- **AND** managed code SHALL explicitly retain an owned copy before storing the
  value beyond the callback

### ADDED: One-Stage Lazy Dotnet Module Registry

`Expo.ModulesCore` SHALL install `_expoDotnet.modules` as a HostObject backed by
the app-composed generated module table. The first read of a registered module
property SHALL create and cache the real module object for the current runtime
context.

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
- **THEN** the HostObject SHALL return the module names from the generated
  module table
- **AND** it SHALL NOT create module objects or module instances

#### Scenario: Root module registry is mutated
- **GIVEN** `_expoDotnet.modules` is installed as a HostObject
- **WHEN** JavaScript assigns a property on `_expoDotnet.modules`
- **THEN** assignment SHALL fail with a catchable JavaScript error

#### Scenario: Cached registry is used after teardown
- **GIVEN** JavaScript still holds a reference to `_expoDotnet.modules`
- **AND** the owning `DotnetRuntimeContext` has been disposed
- **WHEN** JavaScript reads, writes, or enumerates the HostObject
- **THEN** the operation SHALL fail with a catchable JavaScript error
- **AND** it SHALL NOT crash or touch an invalid runtime handle

### ADDED: Required Module Lookup Error

Generated JavaScript facades or shared JavaScript helpers SHALL throw a clear
module-registration error when a required module is missing from
`_expoDotnet.modules`.

#### Scenario: Required module is missing
- **GIVEN** a JavaScript facade requires the `Camera` module
- **AND** `_expoDotnet.modules.Camera` is `undefined`
- **WHEN** the facade resolves the required module
- **THEN** it SHALL throw a JavaScript `Error`
- **AND** the error message SHALL say that `Camera` is not registered
- **AND** the error message SHALL tell the user to check autolinking

### MODIFIED: Module Registration Target

Generated and app-composed module registration SHALL be able to supply lazy
module metadata to `Expo.ModulesCore` instead of eagerly defining all module
objects during registration. Existing explicit registration into a caller
supplied modules object MAY remain as a compatibility path during the
transition.

#### Scenario: Lazy registry receives generated module metadata
- **GIVEN** the app-composed module provider is registering modules for a
  runtime context
- **WHEN** it installs the default dotnet modules object
- **THEN** it SHALL provide `Expo.ModulesCore` with generated module names and
  module creation callbacks
- **AND** those callbacks SHALL use direct generated calls rather than runtime
  hot-path reflection
