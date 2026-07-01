# macOS Integration Proof Result

## Hypothesis

An Expo Desktop / React Native macOS 0.81 app can load `example-module`
through `expo-modules-dotnet` using HostFXR, register generated C# module host
functions into the real Hermes runtime, and call `ExampleModule.add(20, 22)`
without relying on `executeSync()` / `CallInvoker::invokeSync()`.

## Commands Run

- `pnpm install`
- `pnpm --filter desktop-app typecheck`
- `RBENV_VERSION=system pod install` from `apps/desktop-app/macos`
- `xcodebuild build -workspace apps/desktop-app/macos/desktopapp.xcworkspace -scheme desktopapp-macOS -configuration Debug -destination 'platform=macOS' 2>&1 | xcsift -f toon`
- `apps/desktop-app/scripts/build-managed.sh`
- `pnpm --filter desktop-app start -- --localhost`
- `EXPO_DOTNET_LOADER=hostfxr <built desktopapp executable>`

## Expected Result

The macOS app starts from Metro, `expo-modules-dotnet` installs
`globalThis._expoDotnet.modules`, `example-module.add(20, 22)` returns `42`,
and the UI displays `C# add result: 42`.

## Actual Result

The proof succeeded after two macOS-specific fixes:

- React Native macOS 0.81 did not install the module runtime early enough via
  `RCTTurboModuleWithJSIBindings` for this proof. The macOS adapter now captures
  the current `facebook::jsi::Runtime` from the `installModules()` TurboModule
  host function when no runtime has already been captured.
- HostFXR requires `UNMANAGEDCALLERSONLY_METHOD` when resolving a managed
  `[UnmanagedCallersOnly]` method through
  `load_assembly_and_get_function_pointer`.

After those fixes, native logs reported:

- `ExampleModule registered ExampleModule.add.`
- `[ExpoModulesDotnet] HostFXR ExampleModule.add module registered.`

Metro logs reported:

- `[ExampleModule] C# add(20, 22) returned 42`

## Artifacts

- `apps/desktop-app` is the checked-in Expo Desktop / React Native macOS proof.
- `packages/expo-modules-dotnet/macos` is the macOS adapter.
- `apps/desktop-app/scripts/build-managed.sh` stages HostFXR artifacts into the
  app-owned `macos/Managed` bundle resource.

## Ownership And Lifetime Findings

The macOS adapter keeps the borrowed `facebook::jsi::Runtime` inside the
existing `ReactNativeRuntimeConnector` holder and owns the opaque runtime
handle in an install record. This matches the mobile proof ownership model:
native owns JSI mechanics and C# receives only the ABI table plus opaque runtime
handle.

Reload-safe invalidation and production teardown are not solved by this proof.
They remain follow-up P0 lifecycle work.

## Scheduler Findings

The synchronous module call is a direct JSI host function. It does not require
`executeSync()` or `CallInvoker::invokeSync()`.

The adapter still receives a React Native `CallInvoker` for scheduled work, but
priority mapping remains advisory. Broader async scheduling and reload
invalidation evidence remain follow-up work.

## Stop/Go Decision

Go. The first macOS proof is sufficient evidence to continue toward Windows/RNW
host evidence and the cross-host lifecycle contract. Do not treat this proof as
production-ready teardown.
