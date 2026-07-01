# macOS Integration Proof

## Goal

Add the first React Native macOS integration proof for the portable C# / JSI
bridge.

The proof SHALL create `apps/desktop-app`, a plain Expo Desktop / React Native
macOS app that loads the authored `example-module` through
`expo-modules-dotnet` and displays the result of `ExampleModule.add(20, 22)`.
The first implementation slice SHALL collect real lifecycle and scheduler
evidence from a React Native macOS 0.81 / Expo 54-era host without changing the
existing `apps/mobile-app` behavior.

## Assumptions

- `apps/desktop-app` is a separate app because `apps/mobile-app` uses Expo 57 /
  React Native 0.86 while React Native macOS currently targets the repository's
  `react-native-81` catalog.
- `expo-desktop` is a hard dependency of `apps/desktop-app`. The implementation
  MAY use existing local app templates or checked-in native project files
  instead of running the full Expo Desktop setup workflow, but the app SHALL
  depend on Expo Desktop packages.
- `expo-modules-dotnet` owns the public Expo adapter package. The macOS adapter
  code belongs beside existing platform adapters, under a macOS-specific package
  directory such as `packages/expo-modules-dotnet/macos`.
- References to an `expo-modules-macos` package in design discussion are
  interpreted as the macOS adapter directory inside `expo-modules-dotnet`,
  because this repository currently has one public adapter package and no
  separate `expo-modules-macos` package.
- Desktop hosts MAY choose HostFXR or NativeAOT per app. The first macOS proof
  SHOULD default to HostFXR because it is more flexible for desktop development.
- iOS and Android remain limited to NativeAOT in this repository slice. Future
  mobile Mono AOT work is out of scope.
- Autolinking for .NET module libraries remains out of scope. The app may use
  explicit build scripts, package configuration, or app-local staging to make
  the example managed assembly available to the macOS adapter.

## Scope

### Included

- Add `apps/desktop-app` as an Expo Desktop / React Native macOS app using the
  `react-native-81` catalog versions.
- Add `expo-desktop` as an app dependency and keep the app compatible with the
  Expo Desktop / React Native macOS package shape.
- Configure macOS Metro resolution so app code can import `react-native` while
  macOS resolves to `react-native-macos`.
- Add a macOS adapter path to `expo-modules-dotnet` beside the existing
  Android and iOS adapters.
- Reuse the existing native JSI bridge and `ReactNativeRuntimeConnector`
  instead of introducing a new managed runtime boundary.
- Add per-app managed-loader configuration for desktop, modeled after
  `scripts/run-hermes-experiment.sh`:
  - `hostfxr` for framework-dependent managed assemblies;
  - `nativeaot` for NativeAOT shared libraries where supported.
- Load the `example-module` through the selected desktop loader and register
  its generated provider into `globalThis._expoDotnet.modules`.
- Display `C# add result: 42` in the desktop app when
  `example-module.add(20, 22)` succeeds.
- Record runtime install, reload, invalidation, teardown, scheduler, and sync
  host-function findings in the implementation result.

### Excluded

- Do not alter `apps/mobile-app` behavior or version lane.
- Do not implement .NET module autolinking.
- Do not implement RNW or Windows desktop integration.
- Do not add views or platform-specific view APIs.
- Do not introduce AppKit, React Native macOS, Expo Desktop, or packaging
  dependencies into `Expo.JSI`, `Expo.ModulesCore`, or the reusable headless
  native bridge.
- Do not require `executeSync()` / `CallInvoker::invokeSync()` for synchronous
  module function calls.

## Accepted Design

### Desktop App

`apps/desktop-app` SHALL be an app-level proof, not a shared mobile/desktop app
refactor. It SHALL use the repository's `react-native-81` catalog for Expo 54,
React 19.1, React Native 0.81, and React Native macOS 0.81. It SHALL depend on:

- `expo`;
- `expo-desktop`;
- the Expo Desktop packages needed by the selected template or setup shape;
- `react`;
- `react-native`;
- `react-native-macos`;
- `expo-modules-dotnet`;
- `example-module`.

The app UI MAY copy the small `apps/mobile-app/App.tsx` proof behavior, but
shared UI extraction is not required for this milestone. Avoiding a shared app
layer keeps the Expo 57 / RN 0.86 mobile proof isolated from the Expo 54 /
RN 0.81 macOS proof.

### macOS Adapter Package Shape

`expo-modules-dotnet` SHALL gain a macOS adapter directory beside existing
platform adapters. The macOS adapter SHALL integrate with the React Native
macOS / Expo modules CocoaPods shape and SHALL use the same public JavaScript
API as the existing package:

```ts
requireDotnetModule<T>(name: string): T
```

The adapter SHALL install generated C# module host functions into the real
React Native macOS Hermes runtime. It SHALL keep the C++ / C# boundary aligned
with the current rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

The macOS adapter MAY reuse the Objective-C++ TurboModule JSI bindings shape
already used by iOS when React Native macOS exposes
`RCTTurboModuleWithJSIBindings`. If the RN macOS host requires a different
runtime-install hook, the implementation SHALL document the hook actually used
and why the iOS hook could not be reused.

### Loader Configuration

Desktop loader selection SHALL be app-configurable and explicit. The design
SHALL mirror the semantics of `scripts/run-hermes-experiment.sh` without
coupling the desktop app to that script:

- loader name values are `hostfxr` and `nativeaot`;
- HostFXR loads a framework-dependent managed assembly using its
  `.runtimeconfig.json`;
- NativeAOT loads a platform shared library and resolves exported entry points;
- invalid loader values fail loudly during build or startup;
- missing managed artifacts fail with actionable messages that name the
  expected repo-relative build step or artifact shape.

The first desktop proof SHOULD use HostFXR by default and SHOULD keep
NativeAOT as a supported configuration path only if it can be implemented
without broadening the slice. If NativeAOT support would significantly delay
the HostFXR proof, the spec MAY be implemented with a documented loader config
surface and HostFXR-only execution evidence for this milestone.

### Synchronous Module Functions

Generated synchronous module functions SHALL be direct JSI host functions.
Calling `ExampleModule.add` from JavaScript SHALL run inside the current host
function invocation and SHALL NOT require managed code to call
`JavaScriptRuntime.Execute`, native `executeSync`, or
`CallInvoker::invokeSync`.

`executeSync` support remains scheduler evidence, not a prerequisite for
direct synchronous module calls. If `ExampleModule.add(20, 22)` cannot complete
without `executeSync` / `invokeSync`, the implementation SHALL treat that as a
design flaw finding and stop instead of adding a workaround that masks the
problem.

### Scheduler And Lifecycle Evidence

The implementation SHALL record the macOS host facts discovered during the
proof:

- the runtime install hook used;
- when module registration runs relative to runtime creation and JS module
  access;
- the `CallInvoker` / scheduler primitive received by the adapter;
- whether async scheduling through the adapter works;
- whether `CanExecuteSync` is true or false and what native primitive backs it;
- whether reload or invalidation hooks are available;
- how adapter-owned runtime state is invalidated;
- whether stale runtime/module calls fail loudly after invalidation.

The proof SHALL compare these findings with the existing mobile proof where the
comparison is useful, but it SHALL NOT change the portable teardown contract in
this milestone.

## Delta Requirements

### ADDED Requirement: macOS Desktop Proof App

The repository SHALL contain a plain Expo Desktop / React Native macOS app at
`apps/desktop-app` that depends on Expo Desktop and the repository's
`react-native-81` catalog lane.

#### Scenario: Desktop app displays the managed module result
- **GIVEN** the desktop app has been built with the selected managed loader
- **WHEN** the macOS app starts and JavaScript calls `example-module.add(20, 22)`
- **THEN** the app SHALL display `C# add result: 42`
- **AND** the call SHALL go through `expo-modules-dotnet`
- **AND** the existing `apps/mobile-app` proof SHALL remain behaviorally
  unchanged

### ADDED Requirement: macOS Adapter Boundary

`expo-modules-dotnet` SHALL provide a macOS adapter beside its existing
platform adapters without moving React Native macOS dependencies into the
managed core or reusable headless bridge.

#### Scenario: macOS adapter installs generated module bindings
- **GIVEN** React Native macOS provides an active Hermes runtime to the adapter
- **WHEN** the adapter creates the `Expo.JSI` runtime handle
- **THEN** it SHALL use the existing C ABI table and opaque runtime handle
- **AND** generated C# module providers SHALL register into
  `globalThis._expoDotnet.modules`

### ADDED Requirement: Desktop Loader Selection

Desktop apps SHALL select the managed loader explicitly.

#### Scenario: HostFXR loader is selected
- **GIVEN** the desktop app config selects `hostfxr`
- **WHEN** the macOS adapter registers modules
- **THEN** it SHALL initialize HostFXR from the managed assembly runtime config
- **AND** resolve the explicit module registration entry point
- **AND** call that entry point with the `expo_jsi_api` table and runtime handle

#### Scenario: NativeAOT loader is selected
- **GIVEN** the desktop app config selects `nativeaot`
- **WHEN** the macOS adapter registers modules
- **THEN** it SHALL load a platform shared library
- **AND** resolve the explicit exported module registration entry point
- **AND** call that entry point with the same `expo_jsi_api` table and runtime
  handle shape used by HostFXR

### MODIFIED Requirement: React Native Runtime Scheduling Adapter

Direct synchronous module host functions SHALL NOT depend on the runtime
synchronous execution capability.

#### Scenario: Sync module function runs as a host function
- **GIVEN** JavaScript calls a generated synchronous C# module function
- **WHEN** React Native invokes the JSI host function
- **THEN** the generated binding SHALL decode arguments, call managed module
  logic, and encode the return value in that host-function call
- **AND** it SHALL NOT require `executeSync` or `CallInvoker::invokeSync`

#### Scenario: Sync execution is unavailable
- **GIVEN** the macOS runtime adapter reports `CanExecuteSync` as false
- **WHEN** JavaScript calls `ExampleModule.add(20, 22)`
- **THEN** the direct host-function call SHALL still be expected to return `42`
- **AND** any failure caused by depending on `executeSync` SHALL be recorded as
  a design flaw finding

## Verification

Implementation verification SHALL include:

- `pnpm install --frozen-lockfile` or the repository-selected pnpm install
  command after adding workspace dependencies;
- `pnpm --filter desktop-app typecheck`;
- a macOS build/run command for `apps/desktop-app` that proves the window
  displays `C# add result: 42`;
- `scripts/test-managed.sh`;
- `scripts/format.sh --check --all`;
- `git diff --check`.

If the desktop run cannot be fully automated, the implementation SHALL still
record the exact command run, the expected result, the actual result, and any
artifacts such as logs or screenshots.
