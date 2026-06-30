# Runtime And ABI

## Purpose

Define the native C ABI boundary that lets C# work with JavaScript values
without observing C++ JSI layouts. The ABI lives in `native/include/expo_jsi.h`.

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

### Requirement: ABI Results Are Structured

The ABI SHALL report fallible operations through explicit result structs or
error structs. C++ exceptions and managed exceptions SHALL NOT cross unmanaged
frames.

#### Scenario: Value creation fails
- **GIVEN** native cannot create a requested JavaScript value
- **WHEN** an ABI create function returns
- **THEN** it SHALL return `ok = 0` with `expo_jsi_error` populated

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

The Hermes console proof MAY load managed module logic through HostFXR or a
NativeAOT shared library, but the loader choice SHALL NOT change the C ABI
shape passed into managed code.

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

### Requirement: React Native Runtime Connector Preserves ABI Ownership

`native/packages/jsi` SHALL provide a React Native runtime connector that adapts
an already-created Hermes `facebook::jsi::Runtime` to the existing
`expo_jsi.h` ABI without exposing raw JSI layouts to managed code.

#### Scenario: React Native connector creates managed runtime handle
- **GIVEN** React Native provides an active Hermes runtime and `CallInvoker`
- **WHEN** platform glue creates a React Native runtime handle
- **THEN** the handle SHALL be created through `ExpoJsiBridge` and
  `native/include/expo_jsi.h`
- **AND** managed code SHALL observe only the ABI table and opaque handle

#### Scenario: Borrowed runtime lifetime is bounded by native host
- **GIVEN** React Native invokes a TurboModule JSI bindings installer
- **WHEN** the installer registers NativeAOT module bindings into the borrowed
  runtime
- **THEN** it SHALL keep the borrowed runtime connector and opaque runtime handle
  alive at least as long as those bindings can run

### Requirement: ArrayBuffer Is Not Yet Wrapped

The ABI value-kind enum MAY identify `ArrayBuffer`, but the managed package
SHALL NOT be specified as supporting an ArrayBuffer wrapper until such a wrapper
exists in `managed/packages/Expo.JSI`.

#### Scenario: Specs mention implemented wrappers
- **GIVEN** a living spec lists current managed wrapper types
- **WHEN** the spec names supported low-level wrappers
- **THEN** it SHALL NOT imply implemented `JavaScriptArrayBuffer` support until
  code and tests exist
