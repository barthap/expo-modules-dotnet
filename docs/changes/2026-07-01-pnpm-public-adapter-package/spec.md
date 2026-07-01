# pnpm Workspace And Public Adapter Package

## Goal

Restructure the repository so roadmap work can continue against package
boundaries that match the intended production shape. The repository should
become a pnpm workspace, the runnable app proofs should move under `apps/`, and
the reusable Expo/.NET adapter should live in `packages/expo-modules-dotnet`.

The package boundary is:

```text
packages/expo-modules-dotnet
  Public Expo adapter package. Owns the autolinkable TurboModule installer,
  JavaScript API, reusable C++ JSI bridge, and managed core packages.

packages/example-module
  Authored .NET Expo module package used by example apps. Owns module C# code
  and NativeAOT publish output, but not React Native installer glue.

apps/mobile-app
  Example Expo app that consumes the public adapter and the authored example
  module.

apps/hermes-console-app
  Headless Hermes integration app/proof moved out of experiments.

experiments
  Smoke proofs only.
```

This slice intentionally does not implement .NET module autolinking. It keeps a
manual NativeAOT artifact staging convention until a later milestone defines
package discovery and aggregation.

## Scope

In scope:

- Add a root pnpm workspace with room for future `catalogs`.
- Move `experiments/mobile-app` to `apps/mobile-app`.
- Move `experiments/hermes-console-app` to `apps/hermes-console-app`.
- Keep HostFXR and NativeAOT smoke proofs in `experiments/`.
- Create `packages/expo-modules-dotnet` as the public Expo adapter package.
- Move reusable managed core packages and reusable native JSI bridge code under
  `packages/expo-modules-dotnet`.
- Convert the current local `expo-csharp-v2` package into
  `packages/example-module`.
- Make `apps/mobile-app` consume `expo-modules-dotnet` and `example-module` as
  workspace dependencies.
- Add a minimal public JavaScript API,
  `requireDotnetModule<T>(name: string)`, in `expo-modules-dotnet`.
- Preserve the existing mobile proof behavior where the example module returns
  `42` for `add(20, 22)`.

Out of scope:

- .NET module autolinking.
- Multiple authored .NET module package discovery.
- macOS, Windows, or React Native version compatibility proof work.
- Splitting `expo-modules-dotnet` into separate adapter and core npm packages.
- Production reload-safe teardown beyond the behavior already present in the
  mobile proof.

## Delta Requirements

### ADDED Requirement: Repository Uses pnpm Workspace Boundaries

The repository SHALL use a root pnpm workspace for JavaScript packages and apps.
The workspace SHALL make `apps/*` and `packages/*` workspace members.

#### Scenario: Workspace dependencies resolve locally
- **GIVEN** the root workspace is installed with pnpm
- **WHEN** `apps/mobile-app` declares dependencies on `expo-modules-dotnet` and
  `example-module`
- **THEN** those dependencies SHALL resolve to the local workspace packages
- **AND** package versions MAY use `workspace:*` for this slice

#### Scenario: Catalogs are available for later
- **GIVEN** dependency versions need centralization in a later milestone
- **WHEN** the root workspace is inspected
- **THEN** it SHALL be compatible with pnpm `catalogs`
- **AND** this slice MAY leave catalog population minimal if no dependency
  version should be centralized yet

### ADDED Requirement: Apps Live Under apps

Runnable applications SHALL live under `apps/`, while `experiments/` SHALL
remain for smoke proofs and narrow throwaway evidence.

#### Scenario: Mobile proof moves to apps
- **GIVEN** the current mobile proof app exists
- **WHEN** the repository is restructured
- **THEN** it SHALL live at `apps/mobile-app`
- **AND** its documentation and scripts SHALL use repo-relative paths

#### Scenario: Hermes console app moves to apps
- **GIVEN** the Hermes console app is a reusable headless integration app/proof
- **WHEN** the repository is restructured
- **THEN** it SHALL live at `apps/hermes-console-app`
- **AND** runner scripts and documentation SHALL refer to that location

#### Scenario: Smoke proofs stay in experiments
- **GIVEN** `hostfxr-smoke` and `nativeaot-smoke` are narrow smoke proofs
- **WHEN** the repository is restructured
- **THEN** they SHALL remain under `experiments/`

### ADDED Requirement: expo-modules-dotnet Is The Public Expo Adapter

`packages/expo-modules-dotnet` SHALL be the public Expo adapter package. It
SHALL own the autolinkable TurboModule package metadata, JavaScript API,
Android and iOS installer glue, reusable C++ JSI bridge source and headers, and
managed core packages.

#### Scenario: Adapter owns React Native autolinking metadata
- **GIVEN** an Expo app depends on `expo-modules-dotnet`
- **WHEN** React Native or Expo autolinking inspects installed packages
- **THEN** `expo-modules-dotnet` SHALL present the TurboModule metadata and
  native package files needed to install its JSI bindings
- **AND** authored .NET modules SHALL NOT need to own this React Native
  installer glue

#### Scenario: Adapter native code is package-internal
- **GIVEN** Android CMake or an iOS podspec builds the adapter
- **WHEN** it references reusable bridge headers or source files
- **THEN** those paths SHALL be inside `packages/expo-modules-dotnet`
- **AND** the adapter SHALL NOT reach back to a root-level reusable
  `native/packages/jsi` directory

#### Scenario: Managed core moves with the adapter
- **GIVEN** the managed core packages are part of the .NET module runtime
  surface
- **WHEN** the repository is restructured
- **THEN** `Expo.JSI`, `Expo.ModulesCore`, and
  `Expo.ModulesCore.Generator` SHALL live under
  `packages/expo-modules-dotnet/managed/packages`
- **AND** project references in tests, apps, and example modules SHALL be
  updated to that location

#### Scenario: Test-only native host stays near adapter code
- **GIVEN** the Hermes-backed native testhost is repo test infrastructure
- **WHEN** reusable adapter code moves into `packages/expo-modules-dotnet`
- **THEN** test-only host code MAY live under `packages/expo-modules-dotnet`
  so native and managed test linking can use package-local paths
- **AND** package publish configuration MAY exclude that testhost from the
  published npm package

### ADDED Requirement: example-module Is An Authored .NET Module Package

`packages/example-module` SHALL represent an authored .NET Expo module package.
It SHALL own C# module source and NativeAOT publish output. It SHALL NOT own the
React Native TurboModule installer.

#### Scenario: Example module depends on the adapter package
- **GIVEN** `example-module` is consumed by `apps/mobile-app`
- **WHEN** package dependencies are installed
- **THEN** `example-module` MAY declare `expo-modules-dotnet` as a workspace
  dependency or peer dependency according to the package-manager behavior needed
  by this slice
- **AND** the mobile app SHALL depend on both packages explicitly

#### Scenario: Example module builds NativeAOT artifacts
- **GIVEN** the example module contains authored C# module code
- **WHEN** its build script runs
- **THEN** it SHALL publish Android and iOS simulator NativeAOT artifacts for
  the module
- **AND** it SHALL stage those artifacts for the adapter using the temporary
  convention defined by this slice

#### Scenario: Example module does not install JSI bindings
- **GIVEN** React Native initializes installed native packages
- **WHEN** `example-module` is present in the workspace
- **THEN** it SHALL NOT provide the TurboModule installer that creates
  `globalThis._expoDotnet`
- **AND** that installer SHALL come from `expo-modules-dotnet`

### ADDED Requirement: NativeAOT Artifact Staging Is Explicitly Temporary

Until .NET module autolinking exists, the example module SHALL use a manual
artifact staging convention so `expo-modules-dotnet` can link the authored
module NativeAOT library.

#### Scenario: Android artifact is staged
- **GIVEN** `example-module` publishes an Android arm64 NativeAOT shared
  library
- **WHEN** the staging script completes
- **THEN** the library SHALL be copied into a documented adapter-owned Android
  native-library location
- **AND** the copy path SHALL be described as temporary manual staging, not the
  final autolinking architecture

#### Scenario: iOS simulator artifact is staged
- **GIVEN** `example-module` publishes an iOS simulator arm64 NativeAOT dynamic
  library
- **WHEN** the staging script completes
- **THEN** the library SHALL be copied into a documented adapter-owned iOS
  native-library location
- **AND** install names or podspec references SHALL be updated so the app links
  the staged artifact

### ADDED Requirement: requireDotnetModule Forces Adapter Installation

`expo-modules-dotnet` SHALL expose
`requireDotnetModule<T>(name: string): T` as the minimal public JavaScript API
for this slice.

#### Scenario: Adapter installation is lazy
- **GIVEN** React Native TurboModules initialize lazily
- **WHEN** JavaScript calls `requireDotnetModule<T>(name)`
- **THEN** the function SHALL first touch the adapter TurboModule installer so
  native JSI bindings can install `globalThis._expoDotnet`
- **AND** it SHALL read the module from `globalThis._expoDotnet.modules[name]`

#### Scenario: Module is missing
- **GIVEN** `globalThis._expoDotnet.modules[name]` is absent after installer
  initialization
- **WHEN** JavaScript calls `requireDotnetModule<T>(name)`
- **THEN** the function SHALL throw a plain JavaScript `Error`
- **AND** this slice SHALL NOT introduce a named custom JS error class

#### Scenario: Module is present
- **GIVEN** the example module registered a JavaScript module object under
  `globalThis._expoDotnet.modules`
- **WHEN** JavaScript calls `requireDotnetModule<ExampleModule>("ExampleModule")`
- **THEN** it SHALL return that module object typed as `ExampleModule`

### MODIFIED Requirement: ModulesCore Default Namespace Remains _expoDotnet

The package restructuring SHALL preserve the existing module namespace decision.
Generated or generated-looking module registration SHALL continue to install
under `globalThis._expoDotnet.modules` for this slice.

#### Scenario: Example app calls a .NET module
- **GIVEN** `apps/mobile-app` has initialized the adapter
- **WHEN** it calls the example module through `requireDotnetModule`
- **THEN** the module lookup SHALL use `globalThis._expoDotnet.modules`
- **AND** this slice SHALL NOT switch to `globalThis.expo.modules`

## Verification

Implementation should verify:

- `pnpm install` or the repo-selected pnpm install command succeeds from the
  root workspace.
- `apps/mobile-app` can resolve `expo-modules-dotnet` and `example-module` as
  workspace dependencies.
- The example NativeAOT staging script publishes and stages Android and iOS
  simulator artifacts.
- The mobile app still reaches the existing proof behavior where `add(20, 22)`
  returns `42`.
- `scripts/test-managed.sh` passes.
- `scripts/format.sh --check --all` passes.
- `git diff --check` passes.
