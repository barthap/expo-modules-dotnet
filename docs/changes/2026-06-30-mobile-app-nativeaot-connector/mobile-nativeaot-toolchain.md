# Mobile NativeAOT Toolchain Finding

## Hypothesis

The `ExpoMobileV2Module` managed library can be published as a NativeAOT shared
library for the mobile proof app, then linked into iOS Simulator and Android
emulator native builds.

## Commands Run

```sh
dotnet workload list
experiments/mobile-app/scripts/build-dotnet-module.sh
cd experiments/mobile-app/ios && RBENV_VERSION=system pod install
cd experiments/mobile-app && xcodebuildmcp simulator build --workspace-path ios/mobileapp.xcworkspace --scheme mobileapp --simulator-id 19046C77-3797-4356-97D2-B372A3F01383
cd experiments/mobile-app/android && ./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a
```

## Expected Result

- The managed project builds and the Roslyn generator emits the module provider.
- NativeAOT publish succeeds for the platform runtime identifiers needed by the
  iOS Simulator and Android emulator.
- The generated artifacts can be linked into a real React Native Hermes app.

## Actual Result

- `dotnet workload list` reported installed `android` and `ios` workloads after
  the local firewall/sudo issue was resolved outside the agent.
- NativeAOT publish succeeds for `android-arm64` when the build script passes
  the Android NDK `aarch64-linux-android*-clang` as
  `CppCompilerAndLinker`, enables `PublishAotUsingRuntimePack`, and disables
  symbol stripping with `StripSymbols=false`.
- NativeAOT publish succeeds for `iossimulator-arm64` with a plain `net10.0`
  target framework and `PublishAotUsingRuntimePack=true`.
- The iOS dylib install name must match the CocoaPods-embedded filename. The
  build script rewrites the copied dylib to
  `@rpath/libExpoMobileV2Module.dylib`.
- Android loads the NativeAOT library through SoLoader, but its export is not
  visible through `RTLD_DEFAULT`; the Android connector reopens
  `libExpoMobileV2Module.so` with `RTLD_NOW | RTLD_GLOBAL` before `dlsym`.
- Real Expo runtimes own `globalThis.expo.modules` after startup. The proof
  therefore reuses the existing `ExpoCSharpV2` native module object and lets the
  generated C# provider add `add` there instead of creating a brand-new
  `V2Math` key.

## Artifacts

- NativeAOT build helper:
  `experiments/mobile-app/scripts/build-dotnet-module.sh`
- Android NativeAOT library copied to:
  `experiments/mobile-app/modules/expo-csharp-v2/android/src/main/jniLibs/arm64-v8a/libExpoMobileV2Module.so`
- iOS NativeAOT library copied to:
  `experiments/mobile-app/modules/expo-csharp-v2/ios/NativeLibs/libExpoMobileV2Module.dylib`
- iOS screenshot:
  `experiments/mobile-app/artifacts/mobile-app/ios-csharp-module.png`
- Android screenshot:
  `experiments/mobile-app/artifacts/mobile-app/android-csharp-module.png`

## Ownership And Lifetime Findings

The managed entry point keeps the intended ownership boundary: native code
passes an `expo_jsi_api` pointer and opaque runtime handle, while C# constructs
`JavaScriptRuntime.FromNative(...)` and registers generated `Expo.ModulesCore`
bindings. No raw JSI layout crosses into C#.

The React Native host functions must keep the borrowed runtime connector and
opaque runtime handle alive for as long as the installed JS host function can
call into C#. The iOS runtime factory releases the handle when the wrapped
runtime is destroyed. The Android proof stores install records for process
lifetime.

## Scheduler Findings

The React Native connector adapts a borrowed runtime and injected async/sync
scheduling callbacks. The shared connector also includes an optional
`CallInvoker` adapter when React Native exposes that header.

Android uses React Native 0.86 `BindingsInstaller` to install a deferred host
function. iOS wraps the default `JSRuntimeFactory` and installs the same
deferred host function from `unstable_initializeOnJsThread`.

The deferred host function is required because installing into `expo.modules`
too early is overwritten by Expo startup, while installing a brand-new key after
startup is rejected by Expo's read-only module namespace.

## Stop/Go Decision

Go. The proof app runs on iOS Simulator and Android emulator, Metro logs the C#
return value `42` on both platforms, and both screenshots show the module call
result.
