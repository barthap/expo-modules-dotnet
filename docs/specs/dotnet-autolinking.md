# Dotnet Autolinking

## Purpose

This spec defines the app-level .NET module autolinking contract for the
portable C# / JSI bridge. The autolinking tool discovers dotnet Expo module
packages, generates a stable app-level aggregator assembly, builds it, and
stages loader-expected artifacts for platform adapters.

Normative terms such as SHALL, SHALL NOT, and MAY use RFC 2119 meanings.

## Requirements

### Requirement: Dotnet Modules Declare Autolinking Metadata

A dotnet Expo module package SHALL declare `"dotnet"` in
`expo-module.config.json` `platforms` and a `dotnet.projects` list of
package-root-relative csproj paths with optional `assemblyName`.

#### Scenario: Package without dotnet platform is skipped
- **GIVEN** a dependency whose `expo-module.config.json` lacks `"dotnet"` in
  `platforms`
- **WHEN** `resolve` runs
- **THEN** the package SHALL NOT appear in the manifest

#### Scenario: Configured csproj is missing
- **GIVEN** a `dotnet.projects[].path` that does not exist on disk
- **WHEN** `resolve` or `generate` runs
- **THEN** the tool SHALL fail with an error naming the package and path

### Requirement: Autolinking Generates One App-Level Aggregator

The tool SHALL generate a single app-level project with the stable assembly
name `ExpoDotnetHost` that references every resolved module project, calls
each library-local `ExpoModulesProvider_{assemblyName}.Register`, and owns the
`expo_dotnet_create_runtime_context` and
`expo_dotnet_teardown_runtime_context` entry points. Generated registration
order SHALL be deterministic. Generated files SHALL only be rewritten when
content changes.

#### Scenario: Multiple modules resolved
- **GIVEN** two resolved dotnet module packages
- **WHEN** `generate` runs
- **THEN** the aggregator SHALL register both providers
- **AND** module class discovery SHALL remain owned by each library's Roslyn
  generation step

#### Scenario: No modules resolved
- **GIVEN** zero resolved dotnet module packages
- **THEN** the tool SHALL emit a valid aggregator with an empty provider list

#### Scenario: Duplicate assembly names
- **GIVEN** two resolved projects with the same effective `assemblyName`
- **THEN** the tool SHALL fail naming both packages

### Requirement: Aggregator Is The Single NativeAOT Publish Unit

For NativeAOT platforms the tool SHALL publish exactly one native library,
`ExpoDotnetHost`, containing all modules. Per-module NativeAOT libraries SHALL
NOT be loaded into one app.

#### Scenario: NativeAOT publish
- **GIVEN** `build --mode nativeaot`
- **WHEN** the build succeeds
- **THEN** a single `libExpoDotnetHost` native library is produced

### Requirement: CLI Stages Loader-Expected Artifacts

`stage` SHALL copy the artifacts each platform loader expects into app-owned
locations: managed assemblies, runtime config, deps file, and platform
`nethost` runtime library for hostfxr platforms; the single native library for
nativeaot platforms. Staging SHALL skip byte-identical files.

#### Scenario: macOS hostfxr staging
- **GIVEN** a built aggregator and `stage --platform macos`
- **WHEN** staging completes
- **THEN** the app-owned `Managed` location contains everything the macOS
  HostFXR loader resolves at startup

## Documented, Not Implemented: iOS And Android Integration

The iOS and Android autolinking integration is documented future work. The
intended mode is `nativeaot`: `link --platform ios|android` publishes
`ExpoDotnetHost` with `/p:PublishAot=true` for the target RID
(`ios-arm64`, `iossimulator-arm64`, `linux-bionic-arm64`).

On iOS, the adapter podspec `vendored_libraries` path is expected to be
replaced by an app-side `script_phase` or Podfile helper that runs
`link --platform ios` and stages `libExpoDotnetHost.dylib` into an app-owned
location that the installer can link or `dlopen`. Simulator versus device RID
selection comes from the Xcode environment.

On Android, a Gradle task in the adapter or app runs
`link --platform android`, stages `libExpoDotnetHost.so` into the app's
`jniLibs`, and Kotlin loads `ExpoDotnetHost` instead of the legacy
`ExampleModule` library. Both mobile platforms reuse the same resolve and
generate steps; only build RIDs and stage destinations differ.

## Output Directory Migration Note

`<appRoot>/.expo/dotnet/` is the initial generated aggregator output
directory, not a permanent contract. If a different home is selected later,
such as `<appRoot>/dotnet/generated/` or per-platform directories, the
`generate` default can change because `--output` already overrides it. The
macOS script phase and Windows `.targets` file can pass `--output` or consume
the new default, and app `.gitignore` entries must be updated. Loader artifact
discovery is by staging location, not generation location, so generated-code
and loader semantics do not change.

## Hybrid Hooks Migration Note

The CLI commands are split so native build hooks can move from full build-time
generation to install-time generation without CLI changes. The expected path is
to run `generate` at install time through a macOS Podfile `post_install` hook,
the standalone Windows `ExpoDotnetGenerate` MSBuild target, or an npm
`postinstall` command, while keeping `build` and `stage` in the native build
phase. Once the output directory is final, projects may choose whether the
generated aggregator directory remains gitignored or becomes committed for IDE
stability.
