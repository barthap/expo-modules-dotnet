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

The ABI SHALL expose reusable JavaScript class, subclass, object-with-prototype,
constructor-call, and bridge-owned base-class installation primitives through
opaque handles. These primitives SHALL support module event objects and future
shared object hierarchies without exposing raw JSI layouts to managed code.

#### Scenario: Object is created with a prototype
- **GIVEN** managed code owns a JavaScript object wrapper to use as a prototype
- **WHEN** it creates an object with that prototype
- **THEN** native SHALL return an owned object handle whose `[[Prototype]]` is
  the supplied prototype
- **AND** inherited properties SHALL be visible through normal JavaScript
  lookup

#### Scenario: Bridge-owned base classes are installed
- **GIVEN** managed runtime-context initialization needs class-backed module
  objects
- **WHEN** managed code asks the ABI to ensure base classes
- **THEN** native SHALL install `globalThis._expoDotnet.EventEmitter`
- **AND** native SHALL install `globalThis._expoDotnet.NativeModule`
- **AND** `_expoDotnet.NativeModule.prototype` SHALL inherit from
  `_expoDotnet.EventEmitter.prototype`
- **AND** native SHALL NOT create or mutate `globalThis.expo`

#### Scenario: Compatible base classes already exist
- **GIVEN** a previous context initialized compatible bridge-owned base classes
- **WHEN** managed code asks the ABI to ensure base classes again
- **THEN** native SHALL preserve the compatible constructors and prototypes
- **AND** initialization SHALL be idempotent

#### Scenario: Incompatible base classes exist
- **GIVEN** `globalThis._expoDotnet.EventEmitter` or
  `globalThis._expoDotnet.NativeModule` exists but was not installed by this
  bridge
- **WHEN** managed code asks the ABI to ensure base classes
- **THEN** native SHALL replace the incompatible constructor with a compatible
  bridge-owned class

#### Scenario: EventEmitter native state remains encapsulated
- **GIVEN** JavaScript uses inherited event listener APIs on a generated module
  object
- **WHEN** listener state is created, updated, or released
- **THEN** native SHALL own that listener state inside the installed
  `EventEmitter` implementation
- **AND** C# SHALL NOT observe native-state pointers or raw JSI layouts

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

#### Scenario: Managed code reads a JavaScript string
- **GIVEN** native returns `expo_jsi_string_result`
- **WHEN** managed code converts it to a C# `string`
- **THEN** managed code copies the UTF-8 bytes and releases the native buffer
  through the provided callback

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

### Requirement: Loader Choice Preserves ABI Shape

The Hermes console proof and desktop React Native macOS/Windows proofs MAY load
managed module logic through HostFXR or a NativeAOT shared library, but the
loader choice SHALL NOT change the C ABI shape passed into managed code.

#### Scenario: NativeAOT proof runs against the same ABI
- **GIVEN** the Hermes console proof is built with `EXPO_JSI_DOTNET_LOADER=nativeaot`
- **WHEN** native code resolves the NativeAOT managed exports and invokes the
  proof
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
implementation evidence is the `apps/mobile-app` proof and the
`apps/desktop-app` React Native macOS and Windows proofs; this requirement does
not by itself define a production adapter lifecycle.

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

### Requirement: ArrayBuffer Is Not Yet Wrapped

The ABI value-kind enum MAY identify `ArrayBuffer`, but the managed package
SHALL NOT be specified as supporting an ArrayBuffer wrapper until such a wrapper
exists in `packages/expo-modules-dotnet/managed/packages/Expo.JSI`.

#### Scenario: Specs mention implemented wrappers
- **GIVEN** a living spec lists current managed wrapper types
- **WHEN** the spec names supported low-level wrappers
- **THEN** it SHALL NOT imply implemented `JavaScriptArrayBuffer` support until
  code and tests exist
