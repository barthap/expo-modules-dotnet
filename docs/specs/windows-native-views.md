# Windows Native Views

## Purpose

This spec defines the Windows-only native view sidecar for C# Expo Modules v2.
The sidecar consumes platform-neutral generated view metadata and registers
React Native Windows Fabric view components while preserving the portable core
boundary:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

The first supported view host is composition-backed. XAML control hosting,
cross-platform view adapters, commands, and native view events are out of scope
for this slice.

## Requirements

### Requirement: Windows Sidecar Owns Native View Hosting

Windows native view hosting SHALL live in a Windows-only sidecar package or
behind a clearly Windows-scoped compilation boundary. The sidecar MAY depend on
React Native Windows, WinRT projection packages, and Windows composition APIs.
Portable core packages SHALL NOT depend on the sidecar package.

#### Scenario: Windows sidecar registers generated views
- **GIVEN** the generated app-level aggregator exposes view metadata
- **WHEN** the Windows package creates its RNW package
- **THEN** it SHALL register generated view components with React Native
  Windows Fabric
- **AND** it SHALL create composition-backed managed view instances for the
  first proof

#### Scenario: Portable core is reused outside Windows
- **GIVEN** an app builds `Expo.JSI` and `Expo.ModulesCore` for a non-Windows
  target
- **WHEN** Windows native view support exists in the repository
- **THEN** the portable managed packages SHALL remain headless and reusable
  without RNW, WinUI, XAML, or Windows composition dependencies

### Requirement: Generated Metadata Drives Managed Views

The Windows sidecar SHALL consume generated metadata for `[View]` and `[Prop]`
declarations. It SHALL NOT discover authored view modules through runtime
module scanning as the ordinary dispatch path.

#### Scenario: React creates a generated view component
- **GIVEN** a C# module declares a generated view component and prop setters
- **WHEN** React Native Windows creates that component
- **THEN** the sidecar SHALL create the authored managed view instance
- **AND** RNW prop updates SHALL call the generated prop dispatch path
- **AND** generated prop dispatch SHALL call the authored prop setter directly

#### Scenario: View metadata is unavailable
- **GIVEN** the generated aggregator has no view metadata
- **WHEN** the Windows sidecar registers native components
- **THEN** it SHALL skip view registration or report an actionable installer
  error
- **AND** it SHALL NOT crash because metadata is absent

### Requirement: Desktop App Renders A Custom Windows View

The Windows desktop app SHALL render a custom native view backed by authored C#
module code.

#### Scenario: Desktop app renders the example view
- **GIVEN** `apps/desktop-app` runs on Windows with the dotnet aggregator staged
- **WHEN** React renders the example view component
- **THEN** RNW SHALL host a native composition visual created by managed C#
  code
- **AND** changing the React prop SHALL update the native visual through the
  generated `[Prop]` dispatch path

### Requirement: Windows HostFXR Staging Includes Sidecar Dependencies

When Windows native view support is linked through HostFXR, the generated host
build and staging path SHALL include the transitive managed dependency closure
required by the Windows sidecar.

#### Scenario: Windows sidecar is staged for hostfxr
- **GIVEN** the generated Windows aggregator references the Windows sidecar
- **WHEN** the autolinking CLI builds and stages the aggregator for
  `--platform windows --mode hostfxr`
- **THEN** the staged `windows/Managed` directory SHALL include the generated
  host assembly, runtime configuration, deps file, transitive managed
  dependency assemblies, and platform `nethost` runtime library
- **AND** WinRT and Windows App SDK projection assemblies needed by the sidecar
  SHALL be present in the staged dependency closure

## Verification

Windows native view changes SHOULD be verified with:

- generator tests for generated view metadata and invalid view declarations;
- autolinking tests for Windows aggregator generation and dependency staging;
- `pnpm --filter desktop-app typecheck`;
- React Native Windows autolinking check for `apps/desktop-app`;
- a Windows desktop build and launch path;
- runtime confirmation that the desktop app renders the custom native view and
  prop updates reach the managed `[Prop]` dispatch path.
