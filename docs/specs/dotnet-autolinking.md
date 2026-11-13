# Dotnet Autolinking

## Purpose

This spec defines the app-level .NET module autolinking contract for the
portable C# / JSI bridge. The autolinking tool discovers dotnet Expo module
packages, generates a stable app-level aggregator assembly, builds it, and
stages loader-expected artifacts for platform adapters.

Normative terms such as SHALL, SHALL NOT, and MAY use RFC 2119 meanings.

## Requirements

### Requirement: Windows Solution Projection

The package-owned `sync-windows` command SHALL run the app-local RNW
`autolink-windows` command before updating a checked-in `.sln` with the
generated host, managed core packages, and resolved module projects under an
`Expo .NET Managed` solution folder. It SHALL NOT replace the platform build
hooks that build and stage managed artifacts. Its `--check` mode SHALL verify
only the deterministic managed projection because RNW `autolink-windows --check`
may report `NeedAutolinking` after a no-op normal invocation.

The same projection SHALL also run from the `expo-modules-dotnet` config
plugin's `windows` mod during expo-desktop prebuild, through the exported
`prepareWindowsSolutionAsync`. Both paths SHALL use that one function: it
generates `ExpoDotnetHost.csproj`, updates the `.sln`, and enables mixed-mode
debugging on the package project. It does not run `autolink-windows`.
`expo-desktop run windows` runs `autolink-windows` before it builds; that step
adds the native module projects and keeps the `Expo .NET Managed` folder.

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
`expo_dotnet_create_runtime_context_result_v3` and
`expo_dotnet_teardown_runtime_context` entry points. Generated registration
order SHALL be deterministic. Generated files SHALL only be rewritten when
content changes. Runtime-context creation SHALL write a structured result
containing success state, the runtime-context handle, and a self-contained
UTF-8 error message with a release callback when managed startup fails.

#### Scenario: Multiple modules resolved
- **GIVEN** two resolved dotnet module packages
- **WHEN** `generate` runs
- **THEN** the aggregator SHALL register both providers
- **AND** module class discovery SHALL remain owned by each library's Roslyn
  generation step

#### Scenario: No modules resolved
- **GIVEN** zero resolved dotnet module packages
- **THEN** the tool SHALL emit a valid aggregator with an empty provider list

#### Scenario: Managed startup fails
- **GIVEN** generated `ExpoDotnetHost` fails while creating the managed runtime
  context
- **WHEN** the generated runtime-context entry point returns
- **THEN** the result SHALL report `ok = 0`
- **AND** the runtime-context handle SHALL be null
- **AND** the error SHALL contain the managed exception message as UTF-8 bytes
- **AND** native adapters SHALL release the error after copying it

#### Scenario: Duplicate assembly names
- **GIVEN** two resolved projects with the same effective `assemblyName`
- **THEN** the tool SHALL fail naming both packages

### Requirement: The Generated Aggregator Owns Host-Context Marshalling

The generated aggregator SHALL own the private managed mirror of
`expo_dotnet_host_context`, its size-aware pointer decoding, and strict UTF-8
conversion, because its project compiles only the generated files. Its public
unmanaged create entry point SHALL declare the struct parameter as a native
integer, so no private mirror type reaches the generated host's public surface.

`Expo.ModulesCore` SHALL own the immutable public `AppDirectories` and
`HostAppMetadata` models and their platform-neutral validation. The aggregator
SHALL construct both models and pass them to the runtime context constructor
before module registration runs. The directory prefix SHALL retain its v2
layout; complete native app and build version fields MAY follow. A null pointer
means both models are unconfigured.

The aggregator SHALL emit its own private UTF-8 decoder. It SHALL NOT reach the
`Expo.JSI` decoder through a new `InternalsVisibleTo` edge. All decoding SHALL be
NativeAOT-safe and SHALL NOT use runtime reflection or dynamic invocation.

The generated entry-point type SHALL be emitted as `partial`, so a checked-in
harness compiled into the same assembly can reach the private decoder without
widening production visibility.

Verification SHALL compile and run the generated entry points, not only assert on
emitted source text. The harness SHALL call the v3 create entry point through its
unmanaged function pointer for every invalid input, check the structured error
result and release its buffer, call the private decoder for valid inputs, and
assert the managed struct size and every field offset for the running pointer
width. Source-text assertions SHALL remain for the exact emitted contract, but
they SHALL NOT be the only check.

#### Scenario: Decoding happens before context construction
- **GIVEN** a native adapter supplies a valid struct
- **WHEN** the generated create entry point runs
- **THEN** it SHALL decode available directories and native versions, construct
  the public models, and pass them to the runtime context constructor
- **AND** module registration SHALL run after that

#### Scenario: Decoding failure returns a structured error
- **GIVEN** any struct, version, pointer, length, or UTF-8 rule is violated
- **WHEN** the generated create entry point runs
- **THEN** it SHALL report failure through the existing structured
  `RuntimeContextResult` error channel
- **AND** it SHALL NOT create a partially configured runtime context

#### Scenario: The private mirror stays private
- **GIVEN** the generated host is compiled
- **WHEN** its public API surface is examined
- **THEN** the native struct mirror SHALL NOT be public
- **AND** the create entry point SHALL declare the struct parameter as a native
  integer

#### Scenario: The harness executes the unmanaged boundary
- **GIVEN** the generated host is compiled with the checked-in harness
- **WHEN** the harness runs
- **THEN** it SHALL invoke the create entry point through its unmanaged function
  pointer for a null struct pointer, directory-only and full prefixes, unknown
  trailing fields, a partial known field, an undersized size, a wrong version, a
  negative length, invalid pointer/length pairs, invalid UTF-8, and valid UTF-8
- **AND** a managed layout divergence SHALL fail verification rather than surface
  at runtime

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
nativeaot platforms. Desktop staging SHALL also write an
`ExpoDotnetHost.loader` marker containing the staged loader mode so packaged
apps can select HostFXR or NativeAOT without relying on shell environment
propagation. Environment variables MAY override the staged marker for local
debugging. Staging SHALL skip byte-identical files.

#### Scenario: macOS hostfxr staging
- **GIVEN** a built aggregator and `stage --platform macos`
- **WHEN** staging completes
- **THEN** the app-owned `Managed` location contains everything the macOS
  HostFXR loader resolves at startup

#### Scenario: Windows NativeAOT package launch
- **GIVEN** `link --platform windows --mode nativeaot` stages a Windows app
- **WHEN** the app is launched through AppX activation without inheriting the
  developer shell environment
- **THEN** the Windows loader SHALL select NativeAOT from the staged loader
  marker
- **AND** it SHALL NOT fall back to HostFXR because `EXPO_DOTNET_LOADER` is
  absent from the packaged process

### Requirement: Workspace CLI Bootstrap Avoids Stale Generated Hosts
The `expo-modules-dotnet-autolinking` package SHALL expose a package-owned
bootstrap entry point for local workspace development. When the package is a
workspace checkout that includes TypeScript source and the compiled `build/`
entry point is missing or older than source/config files, the bootstrap SHALL
refresh the package build before loading the CLI. Published npm packages SHALL
run the compiled `build/` output without rebuilding because they do not ship
the workspace TypeScript source.

#### Scenario: Workspace CLI source changed
- **GIVEN** a local workspace checkout where `src/` is newer than
  `build/index.js`
- **WHEN** a platform hook or direct command loads
  `expo-modules-dotnet-autolinking`
- **THEN** the package bootstrap SHALL rebuild the local CLI before generating,
  building, or staging app artifacts

#### Scenario: Published package install
- **GIVEN** an npm-published `expo-modules-dotnet-autolinking` package install
  containing only published package files
- **WHEN** a platform hook or direct command loads the package
- **THEN** the bootstrap SHALL load the compiled CLI without running a package
  build

### Requirement: Windows Build Hook Refreshes Managed Payloads Before Packaging
The Windows MSBuild hook SHALL run the dotnet autolinking `link` command before
`PrepareForBuild`, not only before C++ compilation, so up-to-date native
projects still refresh managed artifacts when switching between HostFXR and
NativeAOT or between Debug and Release builds.

#### Scenario: NativeAOT to HostFXR switch
- **GIVEN** a previous Windows build staged NativeAOT artifacts
- **WHEN** a default Debug Windows build runs and the native project is already
  up-to-date
- **THEN** the build SHALL still restage HostFXR artifacts before packaging
- **AND** the packaged AppX layout SHALL contain `nethost.dll`,
  `ExpoDotnetHost.runtimeconfig.json`, and `ExpoDotnetHost.loader`

### Requirement: Default RIDs Cover Mobile Platforms
The CLI SHALL select `iossimulator-arm64` for `link --platform ios` when
`PLATFORM_NAME` is `iphonesimulator` or does not indicate a device build. The
CLI SHALL select `ios-arm64` when `PLATFORM_NAME` is `iphoneos`. The CLI SHALL
select `android-arm64` for `link --platform android`. An explicit `--rid`
SHALL override the platform default.

#### Scenario: iOS simulator build from Xcode
- **GIVEN** `link --platform ios` runs inside an Xcode script phase with
  `PLATFORM_NAME=iphonesimulator`
- **WHEN** the CLI publishes the aggregator
- **THEN** the publish RID SHALL be `iossimulator-arm64`

### Requirement: NativeAOT Publish Handles Mobile Toolchains
`build --mode nativeaot` for mobile RIDs SHALL publish the `ExpoDotnetHost`
aggregator with `/p:PublishAot=true`, `/p:NativeLib=Shared`,
`/p:PublishAotUsingRuntimePack=true`, and `--self-contained true`. Before
publishing, the CLI SHALL remove the generated host's RID-specific NativeAOT
`obj` and `bin` directories so regenerated entry-point exports cannot reuse
stale NativeAOT intermediates. For
`android-arm64`, the CLI SHALL discover the NDK clang from
`ANDROID_NDK_HOME`, or from the newest NDK under `$ANDROID_HOME/ndk` or
`$ANDROID_SDK_ROOT/ndk`, and pass
`/p:CppCompilerAndLinker=<aarch64-linux-android*-clang>` and
`/p:StripSymbols=false`. If no NDK is found, the CLI SHALL fail with an error
that names `ANDROID_NDK_HOME`, `ANDROID_HOME`, and `ANDROID_SDK_ROOT`. Mobile
publishes SHALL use the sanitized dotnet environment used by other native
build hooks.

#### Scenario: Android publish without NDK
- **GIVEN** `build --mode nativeaot --rid android-arm64`
- **AND** no Android NDK is discoverable
- **WHEN** the CLI prepares the publish command
- **THEN** it SHALL fail with an error naming the Android NDK discovery
  environment variables

### Requirement: Mobile Staging Destinations Are Loader-Owned
`stage --platform ios` SHALL copy only the native library
`ExpoDotnetHost.dylib` as `<appRoot>/ios/Managed/libExpoDotnetHost.dylib` and
SHALL set its install name to `@rpath/libExpoDotnetHost.dylib` with
`install_name_tool -id`. `stage --platform android` SHALL copy
`ExpoDotnetHost.so` to
`<appRoot>/android/app/src/main/jniLibs/arm64-v8a/libExpoDotnetHost.so`.
NativeAOT staging SHALL NOT stage `nethost` or managed `.dll` files. Mobile
staging SHALL remove stale files and skip byte-identical files consistently
with desktop staging.

#### Scenario: iOS staging
- **GIVEN** `link --platform ios --project-root apps/mobile-app`
- **WHEN** staging completes
- **THEN** `apps/mobile-app/ios/Managed/libExpoDotnetHost.dylib` SHALL exist
- **AND** its install name SHALL be `@rpath/libExpoDotnetHost.dylib`

### Requirement: iOS App Wiring Uses The Config Plugin And Podfile Helper
`use_expo_modules_dotnet!` in
`packages/expo-modules-dotnet/scripts/autolinking.rb` SHALL accept a
`platform:` option that defaults to `:macos` and SHALL pass
`--platform ios` or `--platform macos` to the CLI. For iOS, the emitted script
phase SHALL copy `ios/Managed/libExpoDotnetHost.dylib` into
`${TARGET_BUILD_DIR}/${FRAMEWORKS_FOLDER_PATH}`, creating the destination so
the dylib ships in the app bundle. The script phase SHALL forward
`EXPO_DOTNET_LOADER` and `CONFIGURATION` consistently with macOS, SHALL use
`${SRCROOT}`-relative paths, and SHALL source `.xcode.env` /
`.xcode.env.local` to resolve `NODE_BINARY` and `DOTNET_BINARY` for Xcode.app
build environments.

Because `apps/mobile-app` is CNG, `packages/expo-modules-dotnet/app.plugin.js`
SHALL inject the Podfile helper during Expo prebuild. The plugin SHALL
idempotently add the helper `require` and
`use_expo_modules_dotnet!(platform: :ios, project_root: ...)` after
`use_expo_modules!`. CNG apps SHALL list `expo-modules-dotnet` in
`expo.plugins`; committed native apps MAY call the helper directly.

#### Scenario: Prebuild emits portable iOS script phase
- **GIVEN** `expo prebuild -p ios` runs for a CNG app that lists
  `expo-modules-dotnet` in `expo.plugins`
- **WHEN** CocoaPods installs the generated Podfile
- **THEN** the Podfile SHALL call `use_expo_modules_dotnet!`
- **AND** the pbxproj SHALL contain a `[CP-User] Link Expo .NET Modules`
  phase with no machine-local absolute paths

### Requirement: Desktop App Wiring Uses The Config Plugin
`packages/expo-modules-dotnet/app.plugin.js` SHALL own every .NET edit to the
`macos` and `windows` native projects that `expo-desktop prebuild` generates.
A CNG desktop app SHALL build and run after prebuild with no hand edit. The
plugin SHALL register its `macos` mods through `withMod` and its `windows` mod
through `withDangerousMod` from `expo/config-plugins`, so it has no runtime
dependency on expo-desktop. Expo CLI filters mods by the requested
`platforms`, so `expo prebuild -p ios` does not run the `macos` or `windows`
mods.

On macOS the plugin SHALL:
- add `use_expo_modules_dotnet!(platform: :macos, ...)` to the Podfile after
  `use_expo_modules!`;
- set `EXPO_DOTNET_LOADER` to the configured loader on every build
  configuration of the one `-macOS` application target, and never on the
  `-iOS` target;
- set the `ExpoModulesDotnetLoader` Info.plist key to `$(EXPO_DOTNET_LOADER)`;
- create `macos/Managed/.gitignore`;
- set `com.apple.security.app-sandbox` to `false` when the loader is
  `hostfxr`, because the App Sandbox denies reads of the machine-wide .NET
  install, and to `true` when the loader is `nativeaot`. It writes both
  values, so a loader change takes effect on a prebuild without `--clean`.

On Windows the plugin SHALL:
- write `.expo/dotnet/windows/ExpoDotnetReactNativeDir.props` with
  `ReactNativeDir`, `ExpoDotnetLoader`, and `ExpoDotnetManagedDir`, each set
  only when the property is empty, and import it from the app-root
  `Directory.Build.props`;
- add one `<Import>` of `windows/ExpoDotnetApp.targets` to the app `.vcxproj`
  and one of `windows/ExpoDotnetPackage.targets` to the app `.wapproj`. It
  finds both projects through the one `.sln` under `windows/`, not by name.
  When the import already exists, it rewrites the import path, so a moved
  package location (such as a new pnpm store folder) takes effect on a
  prebuild without `--clean`;
- create `windows/Managed/.gitignore`;
- call `prepareWindowsSolutionAsync` from `expo-modules-dotnet-autolinking`,
  resolved from the app root.

#### Scenario: macOS prebuild
- **GIVEN** a CNG app that lists `expo-modules-dotnet` in `expo.plugins`
- **WHEN** `expo-desktop prebuild --platform macos` runs and CocoaPods
  installs
- **THEN** the Podfile SHALL call
  `use_expo_modules_dotnet!(platform: :macos, ...)`
- **AND** the `-macOS` target SHALL have `EXPO_DOTNET_LOADER`, the
  `ExpoModulesDotnetLoader` Info.plist key, and the CocoaPods
  `[CP-User] Link Expo .NET Modules` phase that copies `macos/Managed` into the
  bundle resources

#### Scenario: Windows prebuild
- **GIVEN** the same app
- **WHEN** `expo-desktop prebuild --platform windows` runs
- **THEN** the app `.vcxproj` and `.wapproj` SHALL each import one
  package-owned targets file
- **AND** the `.sln` SHALL contain the generated host, the managed core
  packages, and the resolved module projects under `Expo .NET Managed`

#### Scenario: Prebuild runs twice
- **GIVEN** a prebuild already ran
- **WHEN** prebuild runs again without `--clean`
- **THEN** no generated file SHALL change

### Requirement: Config Plugin Options Are Validated
The plugin SHALL accept `macos.loader` and `windows.loader`, each `"hostfxr"`
or `"nativeaot"`, each defaulting to `"hostfxr"`. It SHALL throw at prebuild
time for an unknown key or value, and the error SHALL name the key and the
allowed values. There is no shared `loader` key, because iOS and Android
always load the NativeAOT library.

#### Scenario: No options
- **GIVEN** `"plugins": ["expo-modules-dotnet"]`
- **WHEN** prebuild runs for `macos` or `windows`
- **THEN** the loader SHALL be `hostfxr`

#### Scenario: Shared loader key
- **GIVEN** `["expo-modules-dotnet", { "loader": "nativeaot" }]`
- **WHEN** the plugin runs
- **THEN** it SHALL throw, and the error SHALL name `loader` as an unknown
  option

### Requirement: Dotnet Modules Declare Upstream Substitutions
A dotnet Expo module package MAY declare a `dotnet.substitutions` list in
`expo-module.config.json`. Each entry SHALL have exactly the string fields
`package`, `versionRange`, `accessor`, and `replacement`:

| Field | Meaning |
| --- | --- |
| `package` | The upstream npm package name. |
| `versionRange` | A valid semver range of upstream versions. |
| `accessor` | The upstream native-accessor file, relative to the upstream package root. |
| `replacement` | The replacing entry point, relative to the declaring package root. It SHALL exist. |

`accessor` and `replacement` SHALL be relative paths with forward slashes and
no `.` or `..` segment. A module SHALL declare a substitution only for an
upstream version range whose native module shape it implements in full.
Discovery SHALL fail when an entry is invalid, when a package declares
substitutions without `dotnet.projects`, when one module declares the same
`package` twice, or when two installed modules declare the same `package`. The
error SHALL name the module and the field or the conflicting modules. The
discovery manifest SHALL include each module's substitutions with the absolute
replacement path.

The module declares what it can replace, and the app selects what to replace
(`### Requirement: Windows Apps Opt In To Substitutions Through Metro`). A
list of substitutions kept in `expo-modules-dotnet` would have to change for
every new module, and a module author knows best which upstream versions the
module matches.

#### Scenario: Valid declaration
- **GIVEN** `expo-asset-dotnet` declares a substitution for `expo-asset`
- **WHEN** discovery runs
- **THEN** the manifest entry for `expo-asset-dotnet` SHALL list the
  substitution with the absolute path of its `replacement` file

#### Scenario: Replacement file missing
- **GIVEN** a declaration whose `replacement` file does not exist
- **WHEN** discovery runs
- **THEN** it SHALL fail with an error naming the package and the path

#### Scenario: Two modules declare one package
- **GIVEN** two installed dotnet modules each declare a substitution for
  `expo-asset`
- **WHEN** discovery runs
- **THEN** it SHALL fail with an error naming both modules

### Requirement: Windows Apps Opt In To Substitutions Through Metro
`expo-modules-dotnet/metro` SHALL export `withDotnetModules(config, options)`.
`options` SHALL be absent or an object with only a `substitute` key, whose
value SHALL be a list of unique upstream package names. The wrapper SHALL throw
synchronously for an unknown key, a wrong type, an empty name, or a repeated
name. It SHALL return a `Promise` of a Metro config, and SHALL accept `config`
as a config object or a `Promise` of one.

When `options` is absent or `substitute` is empty, the result SHALL be the
given config object, and the wrapper SHALL run no discovery. Otherwise the
wrapper SHALL discover the app's dotnet modules with the autolinking discovery,
loaded from `config.projectRoot`, and SHALL install a resolver that chains to
the previous one: `config.resolver.resolveRequest` when set, otherwise
Metro's default resolver. For the `windows` platform only, when the previous
resolver returns a source file whose nearest named `package.json` names a
selected upstream package and whose package-relative path equals the declared
`accessor`, the resolver SHALL return the declared `replacement` file instead.
Every other resolution SHALL be returned unchanged. The wrapper SHALL NOT write
`globalThis.expo`, change native projects, or change resolution for any
platform other than `windows`.

The alias works only in the bundler. Upstream Expo code then reaches
`_expoDotnet` modules through its own imports, and the expo-desktop stubs stay
installed but unused. A native entry in `globalThis.expo.modules` would replace
those stubs for every app, opted in or not. The opt-in lives in
`metro.config.js`, not in the app config, because the bundler reads it there.
`withDotnetModules` is asynchronous because it reuses the asynchronous
autolinking discovery, so Metro and the native build see the same set of
modules; Metro awaits a `Promise` exported from `metro.config.js`. The
resolver matches a file by package name and package-relative path, not by
absolute path, so it covers every installed copy of the upstream package.

#### Scenario: Opt-in off
- **GIVEN** an app that calls `withDotnetModules(config)` or passes
  `substitute: []`
- **WHEN** Metro loads the config
- **THEN** the result SHALL be the same config object
- **AND** on Windows the expo-desktop stubs SHALL serve `ExpoAsset` and
  `ExponentConstants`

#### Scenario: Windows resolves a selected accessor to the replacement
- **GIVEN** the app selects `expo-asset` and `expo-constants`
- **WHEN** Metro resolves `./ExpoAsset` from upstream
  `expo-asset/build/Asset.js`, and `./ExponentConstants` from upstream
  `expo-constants/build/Constants.js`, for `windows`
- **THEN** the results SHALL be the `replacement` files of `expo-asset-dotnet`
  and `expo-constants-dotnet`

#### Scenario: Other platforms are unchanged
- **GIVEN** the same opt-in
- **WHEN** Metro resolves the same requests for `macos`, `ios`, or `android`
- **THEN** each result SHALL equal the previous resolver's result

#### Scenario: Unselected modules are unchanged
- **GIVEN** the app selects only `expo-constants`
- **WHEN** Metro resolves `./ExpoAsset` from upstream `expo-asset` for
  `windows`
- **THEN** the result SHALL equal the previous resolver's result

### Requirement: Substitution Selection Is Checked When Metro Starts
With the opt-in on, before it returns the config, `withDotnetModules` SHALL
check each name in `substitute`. Exactly one installed dotnet module SHALL
declare it. The upstream package SHALL resolve from the app root, or from the
app's `expo` package when the app does not depend on it directly. The resolved
version SHALL satisfy the declared `versionRange`, and the declared `accessor`
file SHALL exist in it. A failed check SHALL reject the returned `Promise` with
an error that names the upstream package and, as they apply, the installed
version, the declared range, the accessor file, and the declaring module.

Package lookup SHALL walk the enclosing `node_modules` folders from the
starting directory and SHALL NOT use `NODE_PATH` or the global folders. pnpm
sets `NODE_PATH` to its hoisted store, which can hold a copy of the upstream
package that the app does not use. Upstream can rename a `build/` file in a
patch release; without the accessor check, the alias would then stop matching
without an error, and the stubs would run again.

#### Scenario: Unknown name
- **GIVEN** `substitute: ['expo-camera']` and no installed module declares
  `expo-camera`
- **WHEN** Metro loads the config
- **THEN** it SHALL fail with an error naming `expo-camera` and the names that
  installed modules declare

#### Scenario: Accessor file missing
- **GIVEN** the installed upstream package does not contain the declared
  `accessor` file
- **WHEN** Metro loads the config
- **THEN** it SHALL fail with an error naming the package, its version, and
  the file

#### Scenario: Upstream version outside the range
- **GIVEN** the installed upstream version does not satisfy the declared
  `versionRange`
- **WHEN** Metro loads the config
- **THEN** it SHALL fail with an error naming the package, the installed
  version, and the declared range

### Requirement: Substituted Modules Load On Windows Cold Start
On Windows, with the opt-in on, a cold start of the desktop app SHALL reach the
.NET modules through the replacement entry points, before app code runs, with
no change to the native installer. Upstream `expo-asset` SHALL download assets
through `ExpoAssetDotnet`. Upstream `Constants` SHALL take `sessionId`,
`executionEnvironment`, `platform`, `nativeAppVersion`, `nativeBuildVersion`,
and `expoVersion` from `ExpoConstantsDotnet`. Upstream `Constants.expoConfig`
SHALL be `null` until the module provides a manifest. When the app opts in and
a selected module is not registered, startup SHALL fail with the
`requireDotnetModule` error that names the module.

A missing module fails startup instead of giving upstream a `null` module:
the app opted in, so a missing module is a setup fault, and a `null` module
would hide it behind upstream's "not linked" warning.

#### Scenario: Cold start with the opt-in on
- **GIVEN** the desktop app opts in to `expo-asset` and `expo-constants`
- **WHEN** it cold-starts on Windows
- **THEN** upstream `Constants.sessionId` SHALL equal `sessionId` of
  `expo-constants-dotnet`
- **AND** upstream `Constants.executionEnvironment` SHALL be `bare`
- **AND** upstream `Constants.expoConfig` SHALL be `null`
- **AND** upstream `Asset.downloadAsync()` SHALL return a local URI from the
  `ExpoAssetDotnet` cache

### Requirement: Android Gradle Hook Runs Before Native Library Merges
`packages/expo-modules-dotnet/android/build.gradle` SHALL expose an
`expoDotnetLink` task that runs the autolinking CLI with
`link --platform android --project-root <appRoot>`. The app root SHALL be
resolved from the Gradle project layout and SHALL NOT use hardcoded machine
paths. The task SHALL declare the loader-owned
`android/app/src/main/jniLibs/arm64-v8a` staging directory as an output and
run before every application `merge*JniLibFolders` and `merge*NativeLibs` task
that consumes that directory. Loader selection SHALL come from a Gradle
property or the `EXPO_DOTNET_LOADER` environment variable, and build
configuration SHALL map from the Gradle build type to the CLI `--configuration`
value.

#### Scenario: Gradle build stages the aggregator
- **GIVEN** `./gradlew :app:assembleDebug` runs in `apps/mobile-app/android`
- **WHEN** the JNI-folder and native-library merge tasks execute
- **THEN** `libExpoDotnetHost.so` SHALL be present in the app's `jniLibs`
- **AND** the APK SHALL package the current staged native library

### Requirement: Mobile Loaders Use The Aggregator
The iOS installer SHALL `dlopen` `libExpoDotnetHost.dylib` from the app
bundle `Frameworks` directory with `RTLD_NOW | RTLD_GLOBAL` before resolving
entry point symbols, and MAY fall back to `dlsym(RTLD_DEFAULT, ...)` for
compatibility. iOS loader errors SHALL reference the autolinking CLI. The
Android loader SHALL load `ExpoDotnetHost` through SoLoader, and its C++
fallback SHALL `dlopen` `libExpoDotnetHost.so`. Mobile loaders SHALL resolve
the generated `expo_dotnet_create_runtime_context_result_v3` and
`expo_dotnet_teardown_runtime_context` symbols from the generated aggregator.
If the structured create-runtime-context symbol or HostFXR method cannot be
resolved, loaders SHALL fail with a diagnostic that tells the developer to
rebuild or regenerate the managed artifacts.

#### Scenario: Mobile example app runtime
- **GIVEN** the iOS and Android app builds run through their autolinking hooks
- **WHEN** JavaScript calls the example module
- **THEN** Metro SHALL log `[ExampleModule] C# add(20, 22) returned 42`

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
