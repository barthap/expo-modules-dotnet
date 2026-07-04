# Delta Spec: .NET Module Autolinking Tool

Date: 2026-07-04
Status: approved design, pre-implementation
Affected living specs: `docs/specs/modules-core-boundary.md`,
`docs/specs/runtime-and-abi.md`
New living spec: `docs/specs/dotnet-autolinking.md`

## Goal

Replace the temporary app-composition entry point in `packages/example-module`
(`EntryPoints.cs`) and the manual per-app staging scripts with a TypeScript
autolinking tool that:

1. Discovers dotnet Expo module packages through `expo-module.config.json`.
2. Generates one app-level aggregator project that registers every discovered
   library-local `ExpoModulesProvider_{assemblyName}` and owns the
   `expo_dotnet_create_runtime_context` / `expo_dotnet_teardown_runtime_context`
   entry points.
3. Builds and stages managed artifacts for the platform loaders.

Primary targets: macOS and Windows (HostFXR default). iOS and Android
(NativeAOT) are documented for later implementation, not implemented in this
slice.

## Non-Goals

- Contributing a `dotnet` platform upstream to `expo-modules-autolinking`.
- NuGet packaging or publishing of managed packages.
- iOS/Android build-hook implementation (documented only).
- Config plugin / prebuild integration.

## Accepted Design

### Tool packaging (hybrid)

New workspace package `packages/expo-modules-dotnet-autolinking` (TypeScript,
CLI bin `expo-modules-dotnet-autolinking`). It depends on
`expo-modules-autolinking/exports` for package discovery only, through the
non-deprecated dependency-scanning surface (`makeCachedDependenciesLinker` +
`scanExpoModuleResolutionsForPlatform`, which covers
`expo-module.config.json` parsing and duplicate merging); the deprecated
`findModulesAsync` wrapper is not used.
Upstream `supportsPlatform('dotnet')` already gates through the exact-match
default branch, so no upstream changes are required. The tool MUST NOT call
upstream `resolveModulesAsync` or platform dispatch (they throw for unknown
platforms); all dotnet resolution, codegen, build, and staging logic is local.

### Module package metadata

A dotnet Expo module package declares:

```json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      {
        "path": "dotnet/ExampleModule/ExampleModule.csproj",
        "assemblyName": "ExampleModule"
      }
    ]
  }
}
```

- `path` is package-root-relative and required.
- `assemblyName` is optional; defaults to the csproj file basename. It must
  match the assembly name used by the Roslyn generator to name
  `ExpoModulesProvider_{assemblyName}`; a mismatch surfaces as a compile error
  in the generated aggregator, which is acceptable diagnostics for this slice.
- The schema leaves room for later fields (per-platform excludes, `debugOnly`,
  native package dependencies) without breaking existing configs.

### CLI commands

Composable commands; each usable standalone:

- `resolve --project-root <app> --json` — discovery; prints a JSON manifest:
  `{ modules: [{ packageName, packageRoot, projects: [{ csprojPath, assemblyName }] }] }`.
- `generate --project-root <app> [--output <dir>]` — emits the app-level
  aggregator project (below). Writes are content-compared and skipped when
  identical, to keep incremental native builds quiet.
- `build --mode hostfxr|nativeaot [--rid <rid>] [--configuration <c>]` —
  runs `dotnet build` (hostfxr) or `dotnet publish /p:PublishAot=true`
  (nativeaot) on the generated project. Mode defaults per platform:
  macOS/Windows → `hostfxr`, iOS/Android → `nativeaot`.
- `stage --platform <macos|windows|ios|android> --app-root <dir>` — copies
  built artifacts into the platform-expected location (managed assemblies,
  `runtimeconfig.json`, deps file, `nethost` runtime library for hostfxr; the
  single native library for nativeaot).
- `link --platform <p>` — resolve → generate → build → stage in one shot; this
  is what build hooks invoke.

The CLI replaces `apps/desktop-app/scripts/build-managed.sh`,
`apps/desktop-app/scripts/build-managed.ps1`, and (once iOS/Android land)
`packages/example-module/scripts/build-nativeaot.sh`.

### Generated aggregator

Default output directory: `<appRoot>/.expo/dotnet/` (gitignored, app-owned).
See "Output directory migration" for relocation guidance. Contents:

- `ExpoDotnetHost.csproj` — stable assembly name `ExpoDotnetHost`, with a
  `ProjectReference` to each resolved module csproj and to `Expo.ModulesCore`.
- `LinkedExpoModulesProvider.g.cs`:

  ```csharp
  public static class LinkedExpoModulesProvider
  {
      public static void Register(DotnetRuntimeContext context)
      {
          Expo.ModulesCore.Generated.ExpoModulesProvider_ExampleModule.Register(context);
          // one line per resolved assembly, deterministic order
      }
  }
  ```

- `EntryPoints.g.cs` — `UnmanagedCallersOnly` exports
  `expo_dotnet_create_runtime_context` and
  `expo_dotnet_teardown_runtime_context`, delegating registration to
  `LinkedExpoModulesProvider.Register`. This supersedes
  `packages/example-module/dotnet/ExampleModule/EntryPoints.cs`; that file is
  excluded from compilation by default (opt-in via a `LegacyMobileEntryPoints`
  MSBuild property for the not-yet-migrated mobile NativeAOT proof — compiling
  it into an aggregator-referenced project would duplicate
  `UnmanagedCallersOnly` symbols) and is deleted when iOS/Android migrate.

NativeAOT constraint: each NativeAOT library carries its own runtime and GC,
so separate per-module NativeAOT libraries cannot share one
`DotnetRuntimeContext`. The aggregator is therefore the single publish unit:
one `libExpoDotnetHost.dylib` / `libExpoDotnetHost.so` containing all modules.

### Native loader changes

- HostFXR loaders (`packages/expo-modules-dotnet/macos/ManagedLoader.mm`,
  `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.cpp`)
  replace the hardcoded `"ExampleModule.EntryPoints, ExampleModule"` type with
  the stable constant `"Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost"`.
- NativeAOT loaders replace `libExampleModule.*` with stable
  `libExpoDotnetHost.*` / `ExpoDotnetHost.dll`.
- No per-app native configuration is required; the stable aggregator assembly
  name is the contract.
- Adapter-owned artifact staging locations
  (`packages/expo-modules-dotnet/ios/NativeLibs/`,
  `packages/expo-modules-dotnet/android/src/main/jniLibs/`) are retired when
  the corresponding platform migrates to autolinking; artifacts become
  app-owned.

### Build integration (macOS/Windows, this slice)

Native build hooks invoke the CLI on every app build (incremental via
write-skip codegen, incremental `dotnet build`, and hash-compared staging):

- macOS: `expo-modules-dotnet` exports a Ruby Podfile helper (same pattern as
  upstream `autolinking_manager.rb`) that adds an Xcode script phase to the app
  target running `expo-modules-dotnet-autolinking link --platform macos` via
  `node` before compilation.
- Windows: `packages/expo-modules-dotnet/windows/` ships an
  `ExpoDotnetAutolink.targets` file that the app `.vcxproj` imports (one
  documented line). A pre-build `<Exec>` runs
  `expo-modules-dotnet-autolinking link --platform windows`.

### Migration path: install-time generation (hybrid hooks)

The commands are already split, so migrating from build-hook generation to
install-time generation requires only hook plumbing, no CLI changes:

1. Move the `generate` invocation to install time: Podfile `post_install` hook
   on macOS; an explicit MSBuild target (for example
   `msbuild /t:ExpoDotnetGenerate`) or npm `postinstall` on Windows.
2. Keep `build` + `stage` in the native build phase.
3. Optionally flip the output directory from gitignored to committed for IDE
   stability once its location is final.

### Output directory migration

`.expo/dotnet/` is the initial default, not a commitment. Relocation steps if
a different home is chosen later (for example `<appRoot>/dotnet/generated/` or
per-platform directories):

1. Change the single default in the `generate` command; `--output` already
   overrides it.
2. Update the macOS script phase and Windows `.targets` to pass `--output` or
   consume the new default.
3. Update app `.gitignore` entries.
4. No generated-code or loader changes: artifact discovery is by staging
   location, not by generation location.

### Future iOS/Android integration (documented, not implemented)

- Mode: `nativeaot`. `link --platform ios|android` publishes
  `ExpoDotnetHost` with `/p:PublishAot=true` for the target RID
  (`ios-arm64`, `iossimulator-arm64`, `linux-bionic-arm64`).
- iOS: replace the adapter podspec `vendored_libraries` with an app-side
  `script_phase` (or Podfile helper) that runs `link --platform ios` and
  stages `libExpoDotnetHost.dylib` into an app-owned location that the
  installer can `dlopen`/link; simulator vs device RID selection comes from
  the Xcode environment.
- Android: a Gradle task in the adapter (or an app-level task) runs
  `link --platform android`, staging `libExpoDotnetHost.so` into the app's
  `jniLibs`; Kotlin `SoLoader.loadLibrary("ExpoDotnetHost")` replaces the
  hardcoded `ExampleModule` load.
- Both reuse the same resolve/generate steps; only `build` RIDs and `stage`
  destinations differ.

## Delta Requirements

### ADDED: `docs/specs/dotnet-autolinking.md`

#### Requirement: Dotnet Modules Declare Autolinking Metadata

A dotnet Expo module package SHALL declare `"dotnet"` in
`expo-module.config.json` `platforms` and a `dotnet.projects` list of
package-root-relative csproj paths with optional `assemblyName`.

##### Scenario: Package without dotnet platform is skipped
- **GIVEN** a dependency whose `expo-module.config.json` lacks `"dotnet"` in
  `platforms`
- **WHEN** `resolve` runs
- **THEN** the package SHALL NOT appear in the manifest

##### Scenario: Configured csproj is missing
- **GIVEN** a `dotnet.projects[].path` that does not exist on disk
- **WHEN** `resolve` or `generate` runs
- **THEN** the tool SHALL fail with an error naming the package and path

#### Requirement: Autolinking Generates One App-Level Aggregator

The tool SHALL generate a single app-level project with the stable assembly
name `ExpoDotnetHost` that references every resolved module project, calls
each library-local `ExpoModulesProvider_{assemblyName}.Register`, and owns the
`expo_dotnet_create_runtime_context` and
`expo_dotnet_teardown_runtime_context` entry points. Generated registration
order SHALL be deterministic. Generated files SHALL only be rewritten when
content changes.

##### Scenario: Multiple modules resolved
- **GIVEN** two resolved dotnet module packages
- **WHEN** `generate` runs
- **THEN** the aggregator SHALL register both providers
- **AND** module class discovery SHALL remain owned by each library's Roslyn
  generation step

##### Scenario: No modules resolved
- **GIVEN** zero resolved dotnet module packages
- **WHEN** `generate` runs
- **THEN** the tool SHALL emit a valid aggregator with an empty provider list

##### Scenario: Duplicate assembly names
- **GIVEN** two resolved projects with the same effective `assemblyName`
- **WHEN** `generate` runs
- **THEN** the tool SHALL fail naming both packages

#### Requirement: Aggregator Is The Single NativeAOT Publish Unit

For NativeAOT platforms the tool SHALL publish exactly one native library,
`ExpoDotnetHost`, containing all modules. Per-module NativeAOT libraries SHALL
NOT be loaded into one app.

##### Scenario: NativeAOT publish
- **GIVEN** `build --mode nativeaot`
- **WHEN** the build succeeds
- **THEN** a single `libExpoDotnetHost` native library is produced

#### Requirement: CLI Stages Loader-Expected Artifacts

`stage` SHALL copy the artifacts each platform loader expects into app-owned
locations: managed assemblies, runtime config, deps file, and platform
`nethost` runtime library for hostfxr platforms; the single native library for
nativeaot platforms. Staging SHALL skip byte-identical files.

##### Scenario: macOS hostfxr staging
- **GIVEN** a built aggregator and `stage --platform macos`
- **WHEN** staging completes
- **THEN** the app-owned `Managed` location contains everything the macOS
  HostFXR loader resolves at startup

### MODIFIED: `docs/specs/modules-core-boundary.md`

#### Requirement: App Aggregation Remains Future Autolinking Work

Replaced by autolinking behavior above: manual adapter-owned NativeAOT staging
and manual app-owned HostFXR staging clauses are retired for platforms
migrated to the tool; the "Desktop app stages HostFXR artifacts manually"
scenario is superseded by CLI staging. The `requireDotnetModule` adapter
lookup scenario is unchanged.

### MODIFIED: `docs/specs/runtime-and-abi.md`

#### Requirement: Managed Runtime Lifecycle Entry Points

Unchanged semantics; the entry points move from authored module assemblies to
the generated `ExpoDotnetHost` aggregator, and HostFXR loaders SHALL resolve
the stable type `Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost`
instead of a hardcoded authored-module type.

## Verification

- Unit tests for resolver and codegen with fixture packages (vitest).
- End-to-end: run the CLI against this workspace; assert generated file
  contents and staging layout.
- `scripts/test-managed.sh` stays green (managed suite unaffected).
- Manual proof: `apps/desktop-app` macOS build via Podfile hook; Windows build
  via `.targets` on the Windows test machine.
- `scripts/format.sh --check --all`, `git diff --check`.
