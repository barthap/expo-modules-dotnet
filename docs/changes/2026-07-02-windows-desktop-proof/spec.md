# Windows Desktop Proof Delta Spec

## Goal

Add a Windows React Native Windows lane to `apps/desktop-app` that matches the
accepted macOS desktop proof: the app loads the authored `example-module`
through `expo-modules-dotnet`, registers generated C# module bindings into the
real React Native Hermes runtime, and displays the synchronous
`ExampleModule.add(20, 22)` result as `42`.

## Scope

- Add a checked-in `windows/` React Native Windows project under
  `apps/desktop-app`.
- Keep the app on the existing React Native 0.81 / Expo Desktop dependency
  lane.
- Add Windows-specific adapter glue under
  `packages/expo-modules-dotnet/windows`.
- Keep managed core packages and reusable native bridge code portable and
  headless.
- Stage managed proof artifacts manually. This remains temporary proof
  infrastructure, not .NET module autolinking.
- Prefer HostFXR as the default Windows desktop proof loader.
- Preserve a selectable NativeAOT loader shape where feasible.
- Preserve existing `apps/mobile-app` and macOS `apps/desktop-app` behavior.

Autolinking for .NET module libraries remains out of scope. RNW package/project
autolinking for the `expo-modules-dotnet` Windows native adapter is in scope
because React Native Windows normally discovers native package providers
through `react-native autolink-windows`.

## Accepted Design

`apps/desktop-app/windows` SHALL be a committed Expo Desktop / React Native
Windows app project rather than an untracked generated project. Template
generation may be used as an implementation aid, but durable source control
state SHALL contain the Windows project files needed by
`react-native run-windows`.

`apps/desktop-app` SHALL add ergonomic Windows scripts following the existing
desktop script style. The Windows run script SHALL be compatible with the RNW
CLI path used by Expo Desktop and `react-native run-windows`.

`packages/expo-modules-dotnet/windows` SHALL own RNW and WinRT-specific native
adapter glue. It MAY reference RNW and Windows project types. The portable C
ABI, reusable JSI bridge, and managed packages SHALL NOT gain Windows app,
WinUI, or RNW project dependencies for this proof.

The Windows adapter SHALL install modules by obtaining the active
`facebook::jsi::Runtime` from the RNW host path, creating the existing
`ReactNativeRuntimeConnector`, and calling the same managed registration ABI
used by macOS and NativeAOT proofs:

```text
int register_modules(const expo_jsi_api *, expo_jsi_runtime_handle)
```

The generated synchronous function path SHALL remain direct JSI host-function
execution. The proof SHALL NOT depend on an `executeSync()` or `invokeSync()`
round trip for `ExampleModule.add(20, 22)`. If RNW cannot expose a direct
runtime installation path for this proof, that limitation SHALL be recorded as
the central P0 result rather than hidden behind a different invocation model.

The default Windows desktop loader SHALL be HostFXR. The loader selection SHALL
be app-configurable at build and runtime using the same names as macOS where
practical:

- `EXPO_DOTNET_LOADER`
- `EXPO_JSI_DOTNET_LOADER` as a compatibility alias

The Windows adapter SHALL read staged app-local managed artifacts for the
selected loader. For HostFXR, the app proof SHALL stage `ExampleModule.dll`,
`ExampleModule.runtimeconfig.json`, `ExampleModule.deps.json`, managed bridge
assemblies, and the `nethost` runtime library needed to resolve HostFXR. For
NativeAOT, the app proof MAY stage the platform shared library export for
`example_module_register_modules` if the Windows toolchain supports that slice.

The JS app surface SHALL remain shared with macOS where possible. The displayed
proof result SHALL come from the existing `example-module` facade and
`expo-modules-dotnet` installer behavior, not from a Windows-only JS bypass.

## Delta Requirements

### ADDED Requirement: Windows Desktop RNW Proof

`apps/desktop-app` SHALL include a runnable Windows React Native Windows proof
that uses Expo Desktop dependency versions and React Native Windows 0.81.

#### Scenario: Windows app runs through RNW CLI

- **GIVEN** workspace dependencies are installed
- **WHEN** a developer runs the Windows desktop app command
- **THEN** the command SHALL use the React Native Windows CLI path compatible
  with `react-native run-windows`
- **AND** the checked-in `windows/` project SHALL be the project being built

#### Scenario: Shared JS calls the C# example module

- **GIVEN** the Windows app has started with Hermes
- **WHEN** the React app calls `ExampleModule.add(20, 22)`
- **THEN** the result SHALL be `42`
- **AND** the UI or logs SHALL show the successful result

### ADDED Requirement: Windows Adapter Owns RNW Glue

`packages/expo-modules-dotnet/windows` SHALL contain RNW-specific package,
TurboModule, and runtime-installation glue.

#### Scenario: RNW autolinks the adapter package

- **GIVEN** the app depends on `expo-modules-dotnet`
- **WHEN** `react-native autolink-windows` runs for the app
- **THEN** RNW SHALL be able to discover the adapter's Windows project and
  package provider metadata
- **AND** app-level .NET module library autolinking SHALL remain out of scope

#### Scenario: Windows adapter registers C# modules into JSI

- **GIVEN** RNW provides an active `facebook::jsi::Runtime`
- **WHEN** the Windows adapter installs modules
- **THEN** it SHALL create a runtime handle through the existing
  `ReactNativeRuntimeConnector`
- **AND** call the managed registration ABI with the existing
  `expo_jsi_api` table and opaque runtime handle

### MODIFIED Requirement: Loader Choice Preserves ABI Shape

The desktop React Native proof MAY load managed module logic through HostFXR or
NativeAOT on macOS and Windows, but the loader choice SHALL NOT change the C
ABI shape passed into managed code.

#### Scenario: Desktop HostFXR entry point runs against RNW Hermes

- **GIVEN** `apps/desktop-app` stages HostFXR artifacts for Windows
- **WHEN** the Windows adapter selects the `hostfxr` loader
- **THEN** native code SHALL initialize HostFXR from the staged runtime config
- **AND** resolve the `[UnmanagedCallersOnly]` registration method using
  `UNMANAGEDCALLERSONLY_METHOD`
- **AND** call the resolved entry point with the same `expo_jsi_api` table and
  opaque runtime handle shape used by macOS

#### Scenario: Desktop NativeAOT entry point uses the same registration ABI

- **GIVEN** `apps/desktop-app` selects the `nativeaot` loader and stages a
  Windows `ExampleModule` native library
- **WHEN** the Windows adapter registers modules
- **THEN** native code SHALL resolve `example_module_register_modules`
- **AND** call it with the same `expo_jsi_api` table and opaque runtime handle
  shape used by HostFXR

### MODIFIED Requirement: Runtime Scheduling Evidence

The Windows proof SHALL record whether RNW exposes synchronous dispatch support
and whether the generated sync module path needs it.

#### Scenario: Generated sync function runs directly

- **GIVEN** the Windows adapter has registered generated C# module functions
  as JSI host functions
- **WHEN** JavaScript calls `ExampleModule.add(20, 22)`
- **THEN** the generated host function SHALL run inside the current JavaScript
  call
- **AND** the call SHALL NOT require `CallInvoker::invokeSync`

#### Scenario: RNW scheduler behavior is recorded

- **GIVEN** the Windows proof has been built or attempted
- **WHEN** verification evidence is recorded
- **THEN** the notes SHALL include runtime ownership and lifetime findings
- **AND** scheduler findings, including whether RNW supports sync dispatch for
  scheduled work
- **AND** a stop/go decision for using the RNW path as production contract
  evidence

## Verification Requirements

The implementation SHALL run or record the result of:

- `pnpm install --frozen-lockfile` when package dependencies change.
- `pnpm --filter desktop-app typecheck`.
- The Windows desktop build or run command introduced by the implementation.
- `scripts/test-managed.sh`.
- `scripts/format.sh --check --all`.
- `git diff --check`.

The Windows proof evidence SHALL record:

- hypothesis
- commands run
- expected result
- actual result
- artifacts
- ownership/lifetime findings
- scheduler findings
- stop/go decision
