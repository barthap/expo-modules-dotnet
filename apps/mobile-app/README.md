# Expo .NET NativeAOT Mobile Proof

This Expo app exercises an authored .NET module inside real React Native Hermes
on iOS Simulator and Android emulator.

The app lives under `apps/mobile-app` and consumes these workspace packages:

- `packages/expo-modules-dotnet`: the public Expo adapter package. It owns the
  autolinkable TurboModule installer, JavaScript API, Android/iOS glue, the
  reusable C++ JSI bridge, and the Expo config plugin that wires the iOS build
  hook.
- `packages/expo-modules-dotnet-autolinking`: the CLI that resolves .NET-backed
  modules, generates the `ExpoDotnetHost` aggregator, publishes it with
  NativeAOT, and stages the native library into this app.
- `packages/example-module`: an authored .NET Expo module package owning the
  C# module code.

## Managed Artifacts

The native build hooks run `expo-modules-dotnet-autolinking link` automatically:

- iOS: the Expo config plugin (listed in `app.json` `plugins`) injects
  `use_expo_modules_dotnet!(platform: :ios, ...)` into the generated Podfile at
  prebuild; pod install adds a `[CP-User] Link Expo .NET Modules` script phase
  that publishes for `iossimulator-arm64` (or `ios-arm64` for device builds),
  stages `ios/Managed/libExpoDotnetHost.dylib`, and copies it into the app
  bundle `Frameworks/` directory.
- Android: the adapter's Gradle project registers `expoDotnetLink`, which runs
  before `:app:preBuild`, publishes for `android-arm64`, and stages
  `android/app/src/main/jniLibs/arm64-v8a/libExpoDotnetHost.so`.

Prerequisites (once):

```bash
dotnet workload install android ios
```

Android additionally requires `ANDROID_HOME` or `ANDROID_SDK_ROOT`;
`ANDROID_NDK_HOME` is optional if the SDK has an installed NDK under
`$ANDROID_HOME/ndk`.

### iOS Xcode Environment

This app is Expo prebuilt; `ios/` is generated and uncommitted. The
`expo-modules-dotnet` config plugin injects the same `[CP-User] Link Expo .NET
Modules` phase that the desktop macOS app uses. When building the generated
workspace from Xcode.app, script phases may not inherit the interactive shell
environment, so configure the generated local env file if Xcode cannot find
`node` or `dotnet`:

```bash
cd apps/mobile-app/ios
{
  printf 'export NODE_BINARY="%s"\n' "$(command -v node)"
  printf 'export DOTNET_BINARY="%s"\n' "$(command -v dotnet)"
} > .xcode.env.local
```

Regenerate this file after a clean prebuild if needed. It is local machine
configuration and should stay out of committed repo artifacts.

## How The Native Library Is Loaded

Android loads two shared libraries:

- `libExpoDotnetHost.so`, the staged NativeAOT aggregator containing all
  linked .NET modules.
- `libexpo-modules-dotnet.so`, the C++/JNI adapter library built by Gradle from
  `packages/expo-modules-dotnet/android/src/main/cpp`.

iOS dlopens `libExpoDotnetHost.dylib` from the app bundle `Frameworks/`
directory. The Objective-C++ TurboModule installer receives the borrowed
`facebook::jsi::Runtime` and React Native `CallInvoker`, creates the shared JSI
runtime handle, and calls the NativeAOT registration export.

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
