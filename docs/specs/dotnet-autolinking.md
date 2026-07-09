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
`expo_dotnet_create_runtime_context_result` and
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

### Requirement: Android Gradle Hook Runs Before App Prebuild
`packages/expo-modules-dotnet/android/build.gradle` SHALL expose an
`expoDotnetLink` task that runs the autolinking CLI with
`link --platform android --project-root <appRoot>`. The app root SHALL be
resolved from the Gradle project layout and SHALL NOT use hardcoded machine
paths. The task SHALL run before the app's native `preBuild` task. Loader
selection SHALL come from a Gradle property or the `EXPO_DOTNET_LOADER`
environment variable, and build configuration SHALL map from the Gradle build
type to the CLI `--configuration` value.

#### Scenario: Gradle build stages the aggregator
- **GIVEN** `./gradlew :app:assembleDebug` runs in `apps/mobile-app/android`
- **WHEN** `:app:preBuild` executes
- **THEN** `libExpoDotnetHost.so` SHALL be present in the app's `jniLibs`
- **AND** the APK SHALL package the staged native library

### Requirement: Mobile Loaders Use The Aggregator
The iOS installer SHALL `dlopen` `libExpoDotnetHost.dylib` from the app
bundle `Frameworks` directory with `RTLD_NOW | RTLD_GLOBAL` before resolving
entry point symbols, and MAY fall back to `dlsym(RTLD_DEFAULT, ...)` for
compatibility. iOS loader errors SHALL reference the autolinking CLI. The
Android loader SHALL load `ExpoDotnetHost` through SoLoader, and its C++
fallback SHALL `dlopen` `libExpoDotnetHost.so`. Mobile loaders SHALL resolve
the generated `expo_dotnet_create_runtime_context_result` and
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
