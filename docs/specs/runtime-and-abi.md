# Runtime And ABI

## Purpose

Define the native C ABI boundary that lets C# work with JavaScript values
without observing C++ JSI layouts. The ABI lives in
`packages/expo-modules-dotnet/native/include/expo_jsi.h`.

## Requirements

### Requirement: Opaque Handle Boundary

The system SHALL expose JavaScript runtime, value, promise capability, and
argument concepts to C# only as opaque handles.

#### Scenario: C# receives native handles
- **GIVEN** a native host has a valid `expo_jsi_api` table and runtime handle
- **WHEN** managed code calls `JavaScriptRuntime.FromNative`
- **THEN** managed code validates the table and wraps the runtime without
  taking ownership of the runtime handle

#### Scenario: Raw JSI layouts stay native
- **GIVEN** C# code needs to create, read, retain, or release a JavaScript value
- **WHEN** the operation crosses the bridge
- **THEN** it SHALL call an `expo_jsi_api` function pointer instead of reading a
  `facebook::jsi::*` layout

### Requirement: Value Handle Model

The ABI SHALL use `expo_jsi_value_handle` for ordinary JavaScript values,
objects, arrays, and functions. Promise capability ownership SHALL remain
separate as `expo_jsi_promise_handle`.

#### Scenario: Object and array operations use value handles
- **GIVEN** managed code has an owned object or array wrapper
- **WHEN** it gets/sets a property or array element
- **THEN** native receives an `expo_jsi_value_handle` and validates the expected
  JavaScript shape

#### Scenario: Promise settlement uses capability handle
- **GIVEN** managed code creates a promise capability
- **WHEN** it resolves or rejects the promise
- **THEN** native receives an `expo_jsi_promise_handle` plus an
  `expo_jsi_value_handle` settlement value

### Requirement: Function Call ABI

The ABI SHALL expose JavaScript function invocation through opaque handles.
Managed code SHALL NOT observe `facebook::jsi::Function`, `jsi::Value`, or
`jsi::Object` layouts when calling JavaScript functions.

#### Scenario: Function is called with undefined this
- **GIVEN** managed code owns a JavaScript function value handle
- **WHEN** it calls the ABI function-call entry with zero or more value handles
- **THEN** native SHALL call the underlying JSI function with JavaScript
  `undefined` as `this`
- **AND** return the JavaScript result as an owned value handle

#### Scenario: Function is called with explicit this
- **GIVEN** managed code owns a JavaScript function value handle
- **AND** managed code owns an object value handle to use as `this`
- **WHEN** it calls the ABI function-call-with-this entry
- **THEN** native SHALL call the underlying JSI function with that object as
  `this`
- **AND** return the JavaScript result as an owned value handle

#### Scenario: Function is called as constructor
- **GIVEN** managed code owns a JavaScript function value handle
- **WHEN** it calls the ABI constructor-call entry with zero or more value
  handles
- **THEN** native SHALL call the underlying JSI function as a constructor
- **AND** return the constructed JavaScript object as an owned value handle

### Requirement: JavaScript Class And Prototype ABI

The ABI SHALL expose reusable JavaScript class, subclass,
object-with-prototype, and constructor-call primitives through opaque handles.
These primitives SHALL support module event objects and future shared object
hierarchies without exposing raw JSI layouts to managed code. The ABI SHALL NOT
install Expo ModulesCore-specific classes or own module event listener state.

#### Scenario: Object is created with a prototype
- **GIVEN** managed code owns a JavaScript object wrapper to use as a prototype
- **WHEN** it creates an object with that prototype
- **THEN** native SHALL return an owned object handle whose `[[Prototype]]` is
  the supplied prototype
- **AND** inherited properties SHALL be visible through normal JavaScript
  lookup

#### Scenario: Class constructor is created
- **GIVEN** managed code needs a JavaScript constructor function with a named
  prototype
- **WHEN** it asks the ABI to create a class
- **THEN** native SHALL return an owned function handle that can be used with
  JavaScript `new`
- **AND** instances SHALL inherit from that function's `prototype`

#### Scenario: Subclass constructor is created
- **GIVEN** managed code owns a JavaScript function wrapper to use as a
  superclass
- **WHEN** it asks the ABI to create a class with that superclass
- **THEN** native SHALL return an owned function handle that can be used with
  JavaScript `new`
- **AND** the subclass prototype SHALL inherit from the superclass prototype
- **AND** the subclass constructor SHALL inherit from the superclass
  constructor
- **AND** constructed instances SHALL satisfy `instanceof` checks for both the
  subclass and superclass

#### Scenario: Expo-specific classes stay above the ABI
- **GIVEN** `Expo.ModulesCore` needs `EventEmitter`, `NativeModule`, shared
  object, or future Expo-specific class hierarchies
- **WHEN** it initializes those classes
- **THEN** it SHALL compose them from generic ABI class and prototype
  primitives
- **AND** native `Expo.JSI` code SHALL NOT know `_expoDotnet`, event listener
  names, module objects, observing hooks, or Expo ModulesCore listener state

#### Scenario: JavaScript values are compared by strict equality
- **GIVEN** managed code owns two JavaScript value wrappers
- **WHEN** it asks the ABI to compare them
- **THEN** native SHALL compare the underlying values with JavaScript strict
  equality
- **AND** the operation SHALL NOT expose raw `jsi::Value` layout to managed
  code

### Requirement: HostObject ABI Boundary

The ABI SHALL expose a generic HostObject creation primitive using opaque
runtime and value handles. C++ owns the `facebook::jsi::HostObject`
implementation and JSI mechanics; managed code owns property callback logic.
The ABI SHALL NOT expose raw `jsi::Runtime`, `jsi::Value`, `jsi::Object`, or
`jsi::PropNameID` layouts to C#.

#### Scenario: HostObject gets a string property
- **GIVEN** managed code creates a HostObject with a getter callback
- **WHEN** JavaScript reads a string property on the object
- **THEN** native SHALL call the managed getter through the ABI
- **AND** the getter result SHALL be returned to JavaScript as the property
  value through an owned value handle
- **AND** C# SHALL observe the property name as a managed string

#### Scenario: HostObject setter receives a scoped value
- **GIVEN** managed code creates a HostObject with a setter callback
- **WHEN** JavaScript assigns a property on the object
- **THEN** native SHALL call the managed setter through the ABI
- **AND** managed code SHALL receive the assigned value as an invocation-scoped
  ref
- **AND** managed code SHALL retain an owned copy before storing that value
  beyond the setter callback

#### Scenario: HostObject property names are enumerated
- **GIVEN** managed code creates a HostObject with a property-name callback
- **WHEN** JavaScript enumerates the object's own property names
- **THEN** native SHALL call the managed property-name callback through the ABI
- **AND** native SHALL release the callback-owned property-name buffer exactly
  once after copying the names

#### Scenario: HostObject callback throws
- **GIVEN** a managed HostObject getter, setter, or property-name callback
  throws
- **WHEN** native receives the callback result
- **THEN** native SHALL surface the failure to JavaScript as a catchable
  `Error`
- **AND** no C++ or managed exception SHALL cross an unmanaged boundary
- **AND** callback-owned error buffers SHALL be released exactly once

#### Scenario: HostObject callback context is released
- **GIVEN** a HostObject is destroyed by JavaScript garbage collection or
  runtime teardown
- **WHEN** native releases the managed callback context
- **THEN** cleanup SHALL NOT touch JSI handles
- **AND** cleanup SHALL NOT throw across the unmanaged boundary
- **AND** cleanup SHALL tolerate the owning runtime context already being
  disposed

### Requirement: ABI Results Are Structured

The ABI SHALL report fallible operations through explicit result structs or
error structs. C++ exceptions and managed exceptions SHALL NOT cross unmanaged
frames.

#### Scenario: Value creation fails
- **GIVEN** native cannot create a requested JavaScript value
- **WHEN** an ABI create function returns
- **THEN** it SHALL return `ok = 0` with `expo_jsi_error` populated

#### Scenario: Native error message is consumed after another ABI call
- **GIVEN** a native ABI function returns a nonzero `expo_jsi_error`
- **WHEN** another ABI call is made before managed code reads the first error
  message
- **THEN** the first error message SHALL remain valid until managed code copies
  and releases it

#### Scenario: Managed code consumes a native error
- **GIVEN** managed code receives a nonzero `expo_jsi_error`
- **WHEN** it converts the error into a managed exception or message
- **THEN** it SHALL copy the UTF-8 message
- **AND** it SHALL invoke the native release callback exactly once when present

#### Scenario: Success error result is returned
- **GIVEN** an ABI operation succeeds
- **WHEN** native returns an `expo_jsi_error`
- **THEN** the error SHALL have code zero and no release callback

#### Scenario: Primitive value creation uses a generic slot
- **GIVEN** managed code needs a primitive number, boolean, null, or undefined
  value
- **WHEN** it calls the primitive value creation ABI entry
- **THEN** native SHALL receive the JavaScript value kind ordinal plus an
  8-byte payload
- **AND** null and undefined SHALL ignore the payload
- **AND** number SHALL interpret the payload as double bits
- **AND** boolean SHALL interpret a non-zero payload as true
- **AND** legacy number and boolean create functions SHALL remain in the ABI as
  deprecated compatibility entries

#### Scenario: Boolean read fails
- **GIVEN** a boolean ABI function returns `0`
- **WHEN** the caller needs to distinguish false from failure
- **THEN** it SHALL inspect the structured error out-parameter

### Requirement: UTF-8 String Contract

The ABI SHALL represent strings as UTF-8 pointer plus byte length and SHALL
provide a release callback for owned native string buffers.
Native SHALL validate managed-provided string, property-name, and host-function-name
byte spans as strict UTF-8 before constructing `jsi::String` or `jsi::PropNameID`
values.

#### Scenario: Managed code reads a JavaScript string
- **GIVEN** native returns `expo_jsi_string_result`
- **WHEN** managed code converts it to a C# `string`
- **THEN** managed code copies the UTF-8 bytes and releases the native buffer
  through the provided callback

#### Scenario: Managed code supplies invalid UTF-8

- **GIVEN** managed code supplies invalid UTF-8 bytes for a JavaScript string,
  property name, or host-function name
- **WHEN** native receives the bytes through the ABI or a HostObject callback
- **THEN** native SHALL reject them before constructing the corresponding JSI
  string or property-name value

### Requirement: ABI Version And Size Validation

The managed interop layer SHALL validate the native API table before using it.

#### Scenario: Runtime is created from native handles
- **GIVEN** an API table pointer is passed into managed code
- **WHEN** `JavaScriptRuntime.FromNative` runs
- **THEN** it SHALL reject null handles and validate the native API table before
  returning a runtime wrapper

#### Scenario: API versions differ
- **GIVEN** native `kApiVersion` and managed `ExpoJsiApi.ExpectedVersion`
  disagree
- **WHEN** managed code validates the API table
- **THEN** managed code SHALL reject the table before calling ABI functions
- **AND** the rejection message SHALL include both versions as
  `native=X managed=Y`

### Requirement: Loader Choice Preserves ABI Shape

The Hermes console app and desktop React Native macOS/Windows example apps MAY
load managed module logic through HostFXR or a NativeAOT shared library, but
the loader choice SHALL NOT change the C ABI shape passed into managed code.

#### Scenario: NativeAOT example app runs against the same ABI
- **GIVEN** the Hermes console app is built with `EXPO_JSI_DOTNET_LOADER=nativeaot`
- **WHEN** native code resolves the NativeAOT managed exports and invokes the
  app
- **THEN** managed code SHALL receive the same `expo_jsi_api` table and opaque
  runtime handle shape used by the HostFXR path

#### Scenario: Mobile NativeAOT entry point runs against React Native Hermes
- **GIVEN** a React Native Hermes runtime is exposed to the native connector
- **WHEN** native code invokes a NativeAOT module registration export
- **THEN** managed code SHALL receive only the `expo_jsi_api` table pointer and
  opaque runtime handle needed for registration
- **AND** the registration path SHALL NOT depend on HostFXR, runtime assembly
  scanning, JSON, or hot-path reflection

#### Scenario: Desktop HostFXR entry point runs against React Native macOS Hermes
- **GIVEN** `apps/desktop-app` stages `ExampleModule.dll`,
  `ExampleModule.runtimeconfig.json`, `ExampleModule.deps.json`, managed bridge
  assemblies, and `libnethost.dylib` into the macOS app bundle
- **WHEN** the macOS adapter selects the `hostfxr` loader
- **THEN** native code SHALL initialize HostFXR from the staged runtime config
- **AND** resolve the `[UnmanagedCallersOnly]` registration method using
  `UNMANAGEDCALLERSONLY_METHOD`
- **AND** call the resolved entry point with the same `expo_jsi_api` table and
  opaque runtime handle shape used by NativeAOT

#### Scenario: Desktop HostFXR entry point runs against React Native Windows Hermes
- **GIVEN** `apps/desktop-app` stages `ExampleModule.dll`,
  `ExampleModule.runtimeconfig.json`, `ExampleModule.deps.json`, managed bridge
  assemblies, and `nethost.dll` into the Windows app `Managed` directory
- **WHEN** the Windows adapter selects the `hostfxr` loader
- **THEN** native code SHALL initialize HostFXR from the staged runtime config
- **AND** resolve the `[UnmanagedCallersOnly]` registration method using
  `UNMANAGEDCALLERSONLY_METHOD`
- **AND** call the resolved entry point with the same `expo_jsi_api` table and
  opaque runtime handle shape used by macOS

#### Scenario: ArrayBuffer support follows the selected JSI declaration
- **GIVEN** a native target selects a `jsi::ArrayBuffer` declaration that
  provides `detached(jsi::Runtime&)` and `tryGetMutableBuffer(jsi::Runtime&)`
- **WHEN** the common JSI bridge compiles
- **THEN** it SHALL use those APIs for detachment validation and MutableBuffer
  access
- **AND** it SHALL not use a React Native version macro to select that behavior

#### Scenario: Older JSI declarations omit ArrayBuffer extensions
- **GIVEN** a native target selects a `jsi::ArrayBuffer` declaration without
  `detached(jsi::Runtime&)` or `tryGetMutableBuffer(jsi::Runtime&)`
- **WHEN** the common JSI bridge compiles
- **THEN** it SHALL compile without referring to the missing member
- **AND** MutableBuffer discovery SHALL report no backing MutableBuffer
- **AND** detachment probing SHALL treat the buffer as not introspectable

#### Scenario: Windows config-plugin resolution is deferred to expo-desktop
- **GIVEN** a Windows app lists `expo-modules-dotnet` as an Expo config plugin
- **WHEN** standard Expo prebuild or the React Native Windows CLI runs
- **THEN** the app SHALL NOT assume that the plugin's `windows` dangerous mod
  has executed or that it generated `.expo/dotnet/windows/` properties
- **AND** standard Expo prebuild and the RNW CLI SHALL NOT be documented as
  supported Windows prebuild hosts for that mod
- **AND** the app-scoped Node resolver remains deferred until expo-desktop
  provides a supported Windows prebuild/mod execution path
- **AND** the adapter project SHALL not derive `ReactNativeDir` from its own
  package directory or assume it is a sibling of `react-native-windows`

#### Scenario: Windows bridge proves a build-host-provided core-header path
- **GIVEN** a Windows build host supplies `ReactNativeDir` before the adapter
  project evaluates
- **WHEN** the adapter compiles for either HostFXR or NativeAOT
- **THEN** it SHALL compile a dedicated `ReactNativeVersion.h` translation
  unit through `$(ReactNativeDir)\\ReactCommon`
- **AND** that translation unit SHALL use a React Native version macro only as
  a compile-time assertion with no runtime behavior
- **AND** imported RNW property sheets SHALL continue to provide the JSI and
  CallInvoker include paths used by the adapter

#### Scenario: Windows core-header resolution does not change ArrayBuffer selection
- **GIVEN** the Windows adapter compiles its app-scoped header proof
- **WHEN** the shared JSI bridge selects ArrayBuffer behavior
- **THEN** `ArrayBufferCapabilities.h` and `ExpoJsiBridge.cpp` SHALL continue
  to use selected-JSI C++20 capability checks
- **AND** neither file SHALL include `ReactNativeVersion.h`, use
  `REACT_NATIVE_VERSION_*`, or introduce a preprocessor version gate

#### Scenario: Desktop NativeAOT entry point uses the same registration ABI
- **GIVEN** `apps/desktop-app` selects the `nativeaot` loader and stages a
  platform `ExpoDotnetHost` native library
- **WHEN** the macOS adapter registers modules
- **THEN** native code SHALL resolve the app-composed `expo_dotnet_register_modules`
  entry point
- **AND** call it with the same `expo_jsi_api` table and opaque runtime handle
  shape used by HostFXR

### Requirement: React Native Runtime Connector Preserves ABI Ownership

`packages/expo-modules-dotnet/native/packages/jsi` SHALL provide a React Native
runtime connector that adapts an already-created Hermes
`facebook::jsi::Runtime` to the existing `expo_jsi.h` ABI without exposing raw
JSI layouts to managed code. The connector MAY store the borrowed runtime as a
raw pointer, but it SHALL keep that pointer inside an owned holder that models
invalidation separately from React Native runtime ownership. The current
implementation evidence is the `apps/mobile-app` example app and the
`apps/desktop-app` React Native macOS and Windows example apps; this
requirement does not by itself define a production adapter lifecycle.

#### Scenario: React Native connector creates managed runtime handle
- **GIVEN** React Native provides an active Hermes runtime and `CallInvoker`
- **WHEN** platform glue creates a React Native runtime handle
- **THEN** the handle SHALL be created through `ExpoJsiBridge` and
  `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- **AND** managed code SHALL observe only the ABI table and opaque handle

#### Scenario: Borrowed runtime lifetime is bounded by native host
- **GIVEN** React Native invokes a TurboModule installer or JSI bindings
  installer
- **WHEN** the installer registers module bindings into the borrowed runtime
- **THEN** it SHALL keep the borrowed runtime connector and opaque runtime handle
  alive at least as long as those bindings can run
- **AND** invalidation SHALL clear the holder before downstream code can use the
  borrowed runtime pointer again

### Requirement: Managed Runtime Lifecycle Entry Points

The generated `ExpoDotnetHost` aggregator SHALL expose a runtime context
creation entry point and an idempotent teardown entry point that native host
adapters can call for one JavaScript runtime. HostFXR loaders SHALL resolve the
stable managed type name
`Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost`.

The native ABI keeps `expo_jsi_runtime_handle` opaque. The managed runtime context
handle is also opaque to native code and SHALL be passed back only to the
matching managed teardown entry point.

#### Scenario: Native adapter creates a managed runtime context
- **GIVEN** a host adapter has an `expo_jsi_api` table and opaque runtime handle
- **WHEN** it calls the managed create-runtime-context entry point
- **THEN** managed code SHALL register modules through a runtime-scoped context
- **AND** native SHALL retain only the opaque managed runtime context handle and teardown
  function pointer

#### Scenario: Native adapter tears down a managed runtime context
- **GIVEN** the host reports runtime or module invalidation
- **WHEN** the adapter tears down the runtime context
- **THEN** it SHALL invalidate the runtime holder
- **AND** call the managed teardown entry point exactly once for that native
  install record
- **AND** release the opaque runtime handle
- **AND** drop borrowed runtime and scheduler references

#### Scenario: Host reports late invalidation
- **GIVEN** a host cannot report invalidation while JSI access is still valid
- **WHEN** the adapter tears down the managed runtime context
- **THEN** teardown SHALL avoid JSI access
- **AND** still release managed pins and non-JSI module state
- **AND** stale scheduled work SHALL not touch the runtime

### Requirement: Object NativeState ABI

The ABI SHALL expose generic object-associated native state operations through
opaque object handles. Native `Expo.JSI` SHALL install at most one generic JSI
NativeState holder on a JavaScript object and multiplex managed entries by
handwritten managed state type id. The holder SHALL store opaque managed token
tuples and release callbacks; it SHALL NOT know EventEmitter names, listener
storage, module objects, SharedObject semantics, or other ModulesCore behavior.

#### Scenario: State entry is attached
- **GIVEN** managed code owns a JavaScript object wrapper
- **AND** managed code has registered a managed state object in the current
  runtime context's NativeState registry
- **WHEN** managed code attaches that state to the object for `TState`
- **THEN** native SHALL create or reuse the object's generic NativeState holder
- **AND** native SHALL store the `type_id`, `registry_id`, `generation`, release
  context, and release callback for `TState`
- **AND** native SHALL NOT observe the managed state object's layout or module
  semantics

#### Scenario: State entry is retrieved
- **GIVEN** a JavaScript object has a native state entry for `TState`
- **WHEN** managed code requests the entry by `TState`'s type id
- **THEN** native SHALL return the matching opaque token tuple when found
- **AND** return a structured not-found result when missing
- **AND** native SHALL NOT expose raw `facebook::jsi::NativeState` or managed
  object addresses

#### Scenario: State entry is replaced or cleared
- **GIVEN** a JavaScript object has native state entries for multiple managed
  state types
- **WHEN** managed code replaces or clears the entry for `TState`
- **THEN** native SHALL affect only the entry whose type id matches `TState`
- **AND** entries for other managed state types SHALL remain attached
- **AND** the replaced or cleared entry SHALL invoke its release callback
  exactly once

#### Scenario: State attach fails before ownership transfers
- **GIVEN** managed code passes a registered state token and release context to
  the native `set` operation
- **WHEN** native fails before storing and arming the entry in the object's
  NativeState holder
- **THEN** native SHALL NOT invoke the release callback for that new entry
- **AND** managed code SHALL remain responsible for releasing the registry entry
  and release context on the failed attach path

#### Scenario: NativeState release callback runs
- **GIVEN** a native state entry is replaced, cleared, destroyed with the
  JavaScript object, or released during runtime teardown
- **WHEN** native invokes the managed release callback
- **THEN** the callback SHALL be no-throw and idempotent
- **AND** it SHALL only release or invalidate the managed registry entry
- **AND** it SHALL NOT call JavaScript, touch JSI handles, schedule runtime
  work, block on runtime work, or throw across the unmanaged boundary

### Requirement: Native runtime lifetime ownership

The host SHALL own each `JsiRuntimeConnector`; the connector owns or borrows
`jsi::Runtime` according to its host implementation. The opaque ABI
`RuntimeHandle` SHALL own its shared `RuntimeState`. `RuntimeState` SHALL borrow
the connector only while it is Active or Closing, and SHALL own the
`LongLivedObjectCollection`. The collection SHALL own its entries; an entry MAY
retain `RuntimeState` until collection erase, at which point that cycle SHALL be
broken. A final entry release SHALL retain RuntimeState's connector coordination
through executor enqueue; it SHALL NOT use an executor reference after that
coordination is released.

Production adapter teardown SHALL preserve this order while its connector can
still use JSI:

```text
prepare runtime handle -> invalidate connector -> tear down managed context
-> release runtime handle -> destroy connector
```

#### Scenario: Production adapter tears down a runtime with retained bridge objects
- **GIVEN** a production adapter still owns a runtime handle and connector
- **WHEN** the host begins teardown
- **THEN** it SHALL prepare the runtime handle before invalidating the connector
- **AND** preparation SHALL sweep JSI-backed long-lived entries on the runtime
  executor when JSI remains valid
- **AND** runtime-handle release after connector invalidation SHALL not
  dereference the connector
- **AND** the host SHALL destroy the connector only after releasing the runtime
  handle

### Requirement: Runtime-owned ArrayBuffer ABI

The ABI SHALL expose ArrayBuffer and MutableBuffer storage through opaque
handles and SHALL report version `22`. The native runtime state SHALL own a
generic long-lived-object collection whose first consumer is JavaScript-backed
ArrayBuffer storage. Successful handle results SHALL carry a checked `int32`
logical byte length; managed callers SHALL never recover that length by a later
JSI access. MutableBuffer ABI function targets SHALL remain callable for the
lifetime of any retained managed MutableBuffer dispatch, even when the storage
containing the original API table no longer exists.

#### Scenario: ArrayBuffer handles cross the ABI
- **GIVEN** native retains, allocates, copies, or clones ArrayBuffer storage
- **WHEN** the operation returns
- **THEN** it SHALL return an opaque handle and captured logical byte length
- **AND** the managed API table SHALL validate every ArrayBuffer function
  pointer before use
- **AND** no JSI object layout or raw runtime pointer SHALL cross into C#

#### Scenario: Runtime teardown has a JSI-safe phase
- **GIVEN** a host can report invalidation while JSI remains usable
- **WHEN** the host prepares runtime teardown
- **THEN** the long-lived collection SHALL sweep retained JSI objects on the
  runtime executor before connector invalidation
- **AND** late invalidation SHALL abandon retained payloads without invoking JSI
- **AND** each entry SHALL transition exactly once

### Requirement: Opaque Weak Object ABI

The ABI SHALL report version `23` and expose weak JavaScript-object references
only through an opaque weak-object handle. Native code owns the JSI weak
reference; managed code receives structured create and lock results plus an
idempotent opaque-handle release operation.

#### Scenario: Weak object is created and locked
- **GIVEN** managed code has an object value belonging to runtime `R`
- **WHEN** it creates and then locks an opaque weak-object handle in a valid
  access frame for `R`
- **THEN** the lock result SHALL contain a new owned object value handle when
  the referent is live
- **AND** it SHALL report no value when the referent has been collected

#### Scenario: Weak object reaches teardown
- **GIVEN** a weak-object handle has queued runtime-affine release work
- **WHEN** the host prepares teardown while JSI remains valid
- **THEN** native SHALL release the weak JSI payload on the runtime executor
- **AND** remove its long-lived collection entry
- **WHEN** connector invalidation has already made JSI unavailable
- **THEN** native SHALL abandon the payload without dereferencing JSI and
  remove that same collection entry
