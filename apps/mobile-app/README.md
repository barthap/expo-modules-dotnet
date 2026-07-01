# Expo .NET NativeAOT Mobile Proof

This Expo app exercises an authored .NET module inside real React Native Hermes
on iOS Simulator and Android emulator.

The app lives under `apps/mobile-app` and consumes two workspace packages:

- `packages/expo-modules-dotnet`: the public Expo adapter package. It owns the
  autolinkable TurboModule installer, JavaScript API, Android/iOS glue, and
  reusable C++ JSI bridge.
- `packages/example-module`: an authored .NET Expo module package. It owns the
  C# module code and NativeAOT publish output used by this app.

This slice still uses manual NativeAOT artifact staging. That staging is a
temporary substitute for future .NET module autolinking.

## Build The NativeAOT Module

Install the mobile .NET workloads once:

```bash
dotnet workload install android ios
```

Then publish and stage the example module NativeAOT libraries:

```bash
pnpm --filter example-module build:nativeaot
```

The script publishes `packages/example-module/dotnet/ExampleModule` and stages:

- `android-arm64` to
  `packages/expo-modules-dotnet/android/src/main/jniLibs/arm64-v8a/libExampleModule.so`
- `iossimulator-arm64` to
  `packages/expo-modules-dotnet/ios/NativeLibs/libExampleModule.dylib`

Android requires `ANDROID_HOME` or `ANDROID_SDK_ROOT`; `ANDROID_NDK_HOME` is
optional if the SDK has an installed NDK under `$ANDROID_HOME/ndk`.

## How The Native Library Is Linked

Android links two shared libraries:

- `libExampleModule.so`, the staged NativeAOT C# library.
- `libexpo-modules-dotnet.so`, the C++/JNI adapter library built by Gradle from
  `packages/expo-modules-dotnet/android/src/main/cpp`.

iOS links `libExampleModule.dylib` through
`packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`. The Objective-C++
TurboModule installer receives the borrowed `facebook::jsi::Runtime` and React
Native `CallInvoker`, creates the shared JSI runtime handle, and calls the
NativeAOT registration export.

## Refresh Native Projects

After changing package metadata, podspec, Gradle files, or JS dependencies:

```bash
pnpm install
cd apps/mobile-app
pnpm exec expo prebuild --platform all --clean
cd ios && RBENV_VERSION=system pod install
```

## Run

Start Metro from the app directory. Do not bind it to localhost only when using
the Android emulator, because the emulator reaches Metro through `10.0.2.2`.

```bash
cd apps/mobile-app
pnpm exec expo start --dev-client
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
cd apps/mobile-app/android
./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a
adb reverse tcp:8081 tcp:8081
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell monkey -p dev.expo.csharpv2proof 1
```

Expected Metro logs include:

```text
[ExampleModule] C# add(20, 22) returned 42
```

The app screen should display `C# add result: 42`.
