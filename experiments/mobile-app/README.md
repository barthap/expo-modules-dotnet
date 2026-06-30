# Expo C# NativeAOT Mobile Proof

This Expo app exercises a C# `Expo.ModulesCore` module inside real React
Native Hermes on iOS Simulator and Android emulator.

The interesting pieces are:

- `dotnet/ExpoMobileV2Module`: the managed C# module compiled with NativeAOT.
- `modules/expo-csharp-v2`: a local npm package that contains the native
  Android/iOS glue, the TurboModule metadata needed by React Native codegen,
  and the copied NativeAOT libraries.
- `../../native/packages/jsi`: the shared C++ JSI bridge and React Native
  runtime connector built into the local module.

## Build The NativeAOT Module

Install the mobile .NET workloads once:

```bash
dotnet workload install android ios
```

Then build and copy the NativeAOT libraries into the local Expo package:

```bash
cd experiments/mobile-app
./scripts/build-dotnet-module.sh
```

The script publishes:

- `android-arm64` to
  `modules/expo-csharp-v2/android/src/main/jniLibs/arm64-v8a/libExpoMobileV2Module.so`
- `iossimulator-arm64` to
  `modules/expo-csharp-v2/ios/NativeLibs/libExpoMobileV2Module.dylib`

Android requires `ANDROID_HOME` or `ANDROID_SDK_ROOT`; `ANDROID_NDK_HOME` is
optional if the SDK has an installed NDK under `$ANDROID_HOME/ndk`.

## How The Native Library Is Linked

Android links two shared libraries:

- `libExpoMobileV2Module.so`, the NativeAOT C# library copied by the script.
- `libexpo-csharp-v2.so`, the C++/JNI library built by Gradle from
  `modules/expo-csharp-v2/android/src/main/cpp`.

The C++ library includes `../../native/packages/jsi/src/ExpoJsiBridge.cpp` and
`../../native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`, then calls the
NativeAOT registration export after React Native invokes the TurboModule JSI
bindings installer.

iOS links `libExpoMobileV2Module.dylib` through
`modules/expo-csharp-v2/ExpoCSharpV2.podspec`. The Objective-C++ TurboModule in
`modules/expo-csharp-v2/ios/ExpoCSharpV2Installer.mm` receives the borrowed
`facebook::jsi::Runtime` and React Native `CallInvoker`, creates the shared JSI
runtime handle, and calls the NativeAOT registration export.

## Refresh Native Projects

After changing the local package metadata, podspec, Gradle files, or JS
dependencies:

```bash
cd experiments/mobile-app
bun install
bunx expo prebuild --platform all --clean
cd ios && RBENV_VERSION=system pod install
```

The nested `expo-csharp-v2` package intentionally has its own `package.json`
and React Native `codegenConfig`. Android also applies the React Native Gradle
plugin so `react_codegen_ExpoCSharpV2Spec` is generated before the app CMake
autolinking step.

## Run

Start Metro from the app directory. Do not bind it to localhost only when using
the Android emulator, because the emulator reaches Metro through `10.0.2.2`.

```bash
cd experiments/mobile-app
bunx expo start --dev-client
```

Build and install iOS:

```bash
xcodebuildmcp simulator build-and-run \
  --workspace-path ios/mobileapp.xcworkspace \
  --scheme mobileapp \
  --simulator-name "iPhone 17 Pro"
```

Build and install Android:

```bash
cd experiments/mobile-app/android
./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a
adb reverse tcp:8081 tcp:8081
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell monkey -p dev.expo.csharpv2proof 1
```

Expected Metro logs include:

```text
[ExpoCSharpV2] TurboModule install trigger returned true
[ExpoCSharpV2] C# add(20, 22) returned 42
```

The app screen should display `C# add result: 42`.
