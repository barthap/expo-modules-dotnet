# Windows Unified Solution Projection

## Goal

Give React Native Windows app developers one checked-in Visual Studio solution
that exposes the app, RNW-autolinked native projects, the Expo .NET native
adapter, and the C# projects loaded by the app's generated `ExpoDotnetHost`.
The Debug / x64 HostFXR workflow SHALL support mixed native-and-managed source
debugging without changing the C++ / C ABI / C# runtime boundary.

## Scope

This change applies to Windows React Native apps that consume the published
`expo-modules-dotnet` and `expo-modules-dotnet-autolinking` packages. The
repository's `apps/desktop-app` is a verification consumer, not a required
part of the public developer workflow.

The existing platform build hooks remain the runtime-authoritative path:

- Windows MSBuild continues to run `link --platform windows` before the
  native build.
- macOS and Android hooks are unchanged.
- `link` continues to resolve, generate, build, and stage the exact managed
  artifacts loaded by the native adapter.

The new Windows workflow is a developer-experience projection only. It SHALL
NOT replace `link`, invoke `dotnet build`, or stage runtime artifacts.

## Requirements

### Requirement: Package-Owned Windows Solution Synchronization

`expo-modules-dotnet-autolinking` SHALL expose a `sync-windows` command. The
command SHALL accept an app root and the RNW solution and app-project paths,
run the app-local React Native CLI's `autolink-windows` command, and then
synchronize the Expo .NET managed project projection in that solution.

The command SHALL resolve the app-local `react-native` CLI rather than rely on
a globally installed executable, a package-manager-specific binary path, or
network-backed `npx` resolution. It SHALL forward supported RNW autolinking
arguments to the upstream command.

#### Scenario: Published-package consumer refreshes Windows autolinking

- **GIVEN** an RNW app has installed the published adapter and autolinking CLI
- **WHEN** the developer runs `expo-modules-dotnet-autolinking sync-windows`
  with its app and RNW paths
- **THEN** the app-local RNW autolinker refreshes its native generated files
- **AND** the same solution gains the current managed project projection
- **AND** no repo-local example-app script is required.

### Requirement: Managed Solution Projection Reflects the Resolved Host Graph

After RNW autolinking completes, `sync-windows` SHALL resolve the .NET module
manifest and generate the normal `.expo/dotnet/ExpoDotnetHost` project. It
SHALL add a clearly named Expo .NET managed solution folder containing:

- the generated `ExpoDotnetHost` project;
- `Expo.JSI`;
- `Expo.ModulesCore`; and
- every resolved module project declared by `expo-module.config.json`.

The projection SHALL use deterministic project identities and project ordering,
map each Windows solution configuration to the compatible managed Debug or
Release configuration, and preserve all RNW-owned and user-owned solution
content. Managed project entries SHALL not be selected for normal solution
Build, because building the app project already invokes the authoritative
`link` build and staging path.

#### Scenario: Example module appears in the solution

- **GIVEN** `ExampleModule` is resolved for a Windows app
- **WHEN** `sync-windows` completes
- **THEN** the solution shows `ExpoDotnetHost`, `Expo.JSI`,
  `Expo.ModulesCore`, and `ExampleModule` under the Expo .NET managed folder
- **AND** building the app still produces and stages only the host generated
  through `link`.

#### Scenario: Module set changes

- **GIVEN** a module is added or removed from the resolved .NET manifest
- **WHEN** `sync-windows` runs
- **THEN** the managed solution projection is updated to match exactly
- **AND** obsolete managed entries are removed without changing RNW or
  user-owned entries.

### Requirement: Synchronization Is Idempotent and Safe Around Visual Studio

`sync-windows` SHALL rewrite the solution only when its resulting contents
differ. It SHALL support a `--check` mode that reports a stale managed
projection without writing files. A normal native build SHALL not rewrite an
open solution.

The command SHALL validate every resolved C# project, the solution path, and
the app project path before changing the managed projection. Failures SHALL
identify the missing or unsupported input and leave the existing solution
unchanged by the managed synchronization step.

#### Scenario: Up-to-date solution

- **GIVEN** RNW and managed solution entries already match the current graph
- **WHEN** `sync-windows` runs
- **THEN** neither the solution nor generated managed files are rewritten.

#### Scenario: Stale check during IDE validation

- **GIVEN** the managed module graph differs from the checked-in solution
- **WHEN** `sync-windows --check` runs
- **THEN** it exits unsuccessfully with a refresh instruction
- **AND** it does not modify the solution.

### Requirement: HostFXR Debug Configuration Supports C# Breakpoints

For a Windows app using the default HostFXR loader, synchronization SHALL
ensure the app package launches with a mixed native-and-managed Visual Studio
debugger configuration. The app continues to use its package project as the
startup project.

This requirement applies only to HostFXR Debug builds. NativeAOT remains a
native-debugging configuration and SHALL NOT claim managed C# debugger
support.

#### Scenario: Managed breakpoint in a resolved module

- **GIVEN** a developer opens the synchronized solution in Visual Studio,
  selects Debug / x64, and launches the package project using HostFXR
- **WHEN** execution reaches a source breakpoint in a resolved C# module
- **THEN** Visual Studio binds and hits that breakpoint in the same debugging
  session as native C++ code.

### Requirement: Public Consumer Documentation

The public Windows setup documentation SHALL describe the package-owned
`sync-windows` command, the optional app-level convenience script, the
checked-in solution expectation, HostFXR mixed-mode debugging, and the
separate NativeAOT limitation. It SHALL explain that existing build hooks are
not replaced.

## Non-Goals

- Patching or forking the upstream React Native Windows CLI.
- Replacing Windows, macOS, or Android `link` build/staging hooks.
- Updating solutions during every app build.
- Providing managed C# source debugging for a published NativeAOT host.
- Adding RNW, WinUI, or packaging dependencies to portable managed core
  packages or the reusable JSI bridge.

## Verification

- Unit-test deterministic solution generation, preservation of RNW and
  user-owned entries, add/remove behavior, configuration mapping, idempotence,
  and `--check` behavior.
- Test app-local React Native CLI resolution and forwarding without a global
  CLI dependency.
- Verify the desktop app's generated solution contains the expected native and
  managed project graph.
- Run the RNW autolink check, desktop TypeScript typecheck, managed test suite,
  and repository formatting checks.
- On a Windows Visual Studio machine, verify a HostFXR Debug / x64 launch hits
  breakpoints in both `ExampleModule` C# and the native adapter.
