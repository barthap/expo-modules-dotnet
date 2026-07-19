# Android Generated Host Artifact Delta

## Goal

Make the Android NativeAOT host a declared Gradle output and a direct input to
the native-library merge tasks so an APK cannot silently retain a host library
from an earlier managed ABI.

## Scope

- Declare the Android staging directory as the `expoDotnetLink` task output.
- Make the Android Gradle hook run before both JNI-folder and native-library
  merge tasks that consume the staged host.
- Keep the current loader-owned app-source staging destination.
- Update the living autolinking spec and verify the actual debug APK contains
  the generated host.

## Explicitly Deferred

- Changing the generated aggregator output directory under `.expo/dotnet`.
- Changing Android RIDs, loader symbols, or the managed/native JSI ABI.
- Generalizing generated-artifact wiring for iOS, macOS, or Windows. Their
  platform build systems have separate staging contracts.

## Requirements

### Requirement: Android staging is a declared Gradle output

The Android Gradle hook SHALL declare the loader-owned
`jniLibs/arm64-v8a` directory as the `expoDotnetLink` task output. The link
task SHALL continue to stage the host and loader marker in that directory with
its existing stale-file removal and byte-identical copy behavior.

#### Scenario: Direct Android CLI staging remains compatible

- **GIVEN** `stage --platform android` runs without a destination override
- **WHEN** staging completes
- **THEN** `libExpoDotnetHost.so` SHALL be staged under the app source
  `jniLibs/arm64-v8a` directory

#### Scenario: Android Gradle declares its staging directory

- **GIVEN** the Android Gradle hook runs for an app
- **WHEN** the link task stages `ExpoDotnetHost`
- **THEN** `libExpoDotnetHost.so` and `ExpoDotnetHost.loader` SHALL be written
  beneath the loader-owned Android JNI directory
- **AND** Gradle SHALL know that directory is an output of the link task

### Requirement: Android Gradle orders host staging before JNI merges

The Android Gradle hook SHALL make `expoDotnetLink` a direct dependency of each
application `merge*JniLibFolders` and `merge*NativeLibs` task. These tasks SHALL
consume the staged host only after the link task completes.

#### Scenario: Debug APK consumes the current staged host

- **GIVEN** `:app:assembleDebug` runs after a managed ABI or generated-host
  change
- **WHEN** Gradle builds the debug APK
- **THEN** the APK's `lib/arm64-v8a/libExpoDotnetHost.so` entry SHALL originate
  from the library staged by the current `expoDotnetLink` task through AGP's
  normal symbol-stripping step
- **AND** the JNI-folder merge and native-library merge SHALL rerun when the
  staged host changes

### Requirement: Android packaging behavior is verified

The Android integration verification SHALL compare the staged host with the
APK's arm64-v8a entry after a debug build.
