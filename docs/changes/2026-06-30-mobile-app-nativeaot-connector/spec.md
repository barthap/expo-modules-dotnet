# Mobile App NativeAOT Connector Delta Spec

## Goal

Bootstrap a local Expo mobile app that proves a NativeAOT C# module can install
generated `Expo.ModulesCore` bindings into a real React Native Hermes runtime
through the existing `expo_jsi.h` ABI.

## Scope

This slice is a proof app under `experiments/mobile-app`. It adds the minimum
native connector, managed entry point, and app scaffold needed to run one
generated C# module on iOS Simulator and Android emulator.

This slice does not define the final dotnet autolinking format, publish a NuGet
package, support multiple module libraries, or replace the normal Expo
Swift/Kotlin modules API.

## Assumptions

- The app can use Expo prebuild output because native iOS and Android glue are
  required.
- The managed module library is built as NativeAOT and exposes a stable C ABI
  entry point that receives `const expo_jsi_api *` and
  `expo_jsi_runtime_handle`.
- React Native runtime installation happens through a TurboModule JSI bindings
  hook. The hook receives the active Hermes `facebook::jsi::Runtime` and React
  Native `CallInvoker` from React Native itself.

## Accepted Design

### Managed Module

The proof app SHALL contain a C# module library with a class modeled after the
existing `V2MathModule`. In a real Expo runtime, `expo.modules` is owned by
Expo after the runtime installer runs, so the proof targets the existing native
`ExpoCSharpV2` module object and lets C# add the generated sync function there:

```csharp
[ExpoModule("ExpoCSharpV2")]
public sealed partial class MobileV2MathModule
{
  [JS("add")]
  public double Add(double a, double b) => a + b;
}
```

The module library SHALL reference `Expo.JSI` and `Expo.ModulesCore`, wire the
`Expo.ModulesCore.Generator` analyzer, and expose a NativeAOT entry point that
constructs `JavaScriptRuntime.FromNative(...)` and calls the generated provider.

### Native React Native Connector

`native/packages/jsi` SHALL gain a React Native connector that adapts an
already-created Hermes `facebook::jsi::Runtime` plus the React Native
`CallInvoker` supplied by the TurboModule JSI bindings hook to
`JsiRuntimeConnector`.

The connector SHALL use `native/include/expo_jsi.h` through
`ExpoJsiBridge.{h,cpp}`. It SHALL NOT expose raw JSI layouts to managed code.

The connector SHALL provide:

- a borrowed runtime handle for the active React Native runtime;
- passive runtime-validity checks;
- async scheduling through the React Native call invoker;
- sync execution through the React Native call invoker when it is present,
  otherwise a loud unsupported result through the existing runtime executor path.

### App Integration

`experiments/mobile-app` SHALL be bootstrapped with `bunx create expo`. The app
SHALL display the return value from JavaScript calling:

```ts
globalThis.expo.modules.ExpoCSharpV2.add(20, 22)
```

The app SHALL log a clear module installation and invocation message from the
Metro/bundler-visible JavaScript path.

Native iOS and Android scaffolding SHALL link the NativeAOT output and call the
managed registration entry point with the `expo_jsi_api` table and borrowed
runtime handle.

The proof SHALL NOT patch `MainApplication`, override `JSRuntimeFactory`, or use
config-plugin text replacement to install JSI bindings. A nested local npm
package MAY be used so React Native codegen can discover the TurboModule
specification.

## Delta Requirements

### ADDED Requirement: Mobile App NativeAOT Proof

The repository SHALL include a local Expo app proof that calls a NativeAOT C#
module through generated `Expo.ModulesCore` bindings on iOS and Android.

#### Scenario: JavaScript calls generated C# module
- **GIVEN** the mobile proof app has installed the NativeAOT C# modules provider
- **WHEN** JavaScript calls `globalThis.expo.modules.ExpoCSharpV2.add(20, 22)`
- **THEN** the screen SHALL display `42`
- **AND** the JavaScript console SHALL log that the C# module returned `42`

### ADDED Requirement: React Native Runtime Connector

`native/packages/jsi` SHALL contain a React Native runtime connector that adapts
the real React Native Hermes runtime to the existing `expo_jsi.h` ABI.

#### Scenario: Native connector installs modules
- **GIVEN** React Native provides a Hermes `facebook::jsi::Runtime`
- **WHEN** the native proof installer invokes the managed NativeAOT entry point
- **THEN** managed code SHALL receive an `expo_jsi_api` table and opaque runtime
  handle
- **AND** generated `Expo.ModulesCore` registration SHALL install the generated
  function on the existing `globalThis.expo.modules.ExpoCSharpV2` object

### ADDED Requirement: TurboModule JSI Binding Installation

The mobile proof SHALL install JSI bindings through React Native TurboModule
JSI binding hooks instead of app-level runtime factory or application
replacement hooks.

#### Scenario: Android TurboModule installs bindings
- **GIVEN** React Native creates the Android TurboModule instance
- **WHEN** React Native invokes its `TurboModuleWithJSIBindings` installer
- **THEN** the installer SHALL receive the active `facebook::jsi::Runtime` and
  React Native `CallInvoker`
- **AND** the NativeAOT C# provider SHALL register through the existing
  `expo_jsi_api` ABI

#### Scenario: iOS TurboModule installs bindings
- **GIVEN** React Native creates the iOS TurboModule instance
- **WHEN** React Native invokes `installJSIBindingsWithRuntime:callInvoker:`
- **THEN** the installer SHALL receive the active `facebook::jsi::Runtime` and
  React Native `CallInvoker`
- **AND** the NativeAOT C# provider SHALL register through the existing
  `expo_jsi_api` ABI

### MODIFIED Requirement: Loader Choice Preserves ABI Shape

NativeAOT mobile loading SHALL use the same `expo_jsi_api` and opaque runtime
handle shape as the Hermes console proof.

#### Scenario: Mobile NativeAOT entry point runs
- **GIVEN** the mobile proof has linked the NativeAOT shared library
- **WHEN** the app calls the exported registration entry point
- **THEN** the entry point SHALL accept only the ABI table pointer and runtime
  handle needed to register generated modules
- **AND** it SHALL NOT depend on HostFXR, runtime assembly scanning, JSON, or
  hot-path reflection

## Verification

The slice is accepted when:

- `scripts/test-managed.sh` passes.
- `scripts/format.sh --check --all` passes.
- The iOS Simulator screenshot shows the app displaying `42`.
- The Android emulator screenshot shows the app displaying `42`.
- Metro logs show the JavaScript call returned `42`.
- A source scan confirms the managed proof uses `[ExpoModule]`, `[JS]`,
  `Expo.JSI`, and `Expo.ModulesCore`, while `native/packages/jsi` contains the
  React Native connector and uses `native/include/expo_jsi.h`.
