# Windows Desktop Proof

## Hypothesis

React Native Windows 0.81 can host the existing `expo-modules-dotnet` JSI ABI
and register `example-module` so `ExampleModule.add(20, 22)` executes as a
direct JSI host function and returns `42`.

## Commands Run

- `pnpm install`: failed first with `UNABLE_TO_VERIFY_LEAF_SIGNATURE` from the
  npm registry, then succeeded with command-local `--config.strict-ssl=false`.
- `pnpm --filter desktop-app list react-native-windows --depth 0`: resolved
  `react-native-windows@0.81.29`.
- `react-native autolink-windows`: succeeded after restoring the RNW-required
  `PowerShell` NuGet package so `pwsh.exe` was available.
- `pnpm --filter desktop-app build:managed:windows`: succeeded and staged
  HostFXR artifacts into `apps/desktop-app/windows/Managed`.
- `MSBuild.exe apps/desktop-app/node_modules/expo-modules-dotnet/windows/ExpoModulesDotnet.sln /restore /p:Configuration=Debug /p:Platform=x64 /m:1 /v:minimal /clp:ErrorsOnly`: succeeded.
- `MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /p:PlatformToolset=v145 /m:1 /v:minimal /clp:ErrorsOnly`: succeeded after a retry once stale MSBuild/PDB helper locks cleared.
- `pnpm --filter desktop-app exec react-native run-windows --no-launch --no-packager --no-deploy --singleproc --msbuildprops PlatformToolset=v145`: restored and autolinked, then failed in the CLI-driven build with adapter PDB/PCH file locking errors.
- `pnpm --filter desktop-app typecheck`: succeeded.
- `scripts/test-managed.sh`: blocked on this Windows checkout. Direct `bash`
  sees CRLF in the working tree; an LF-normalized invocation then reaches the
  script's macOS Hermes destroot expectation and fails because
  `build/hermes/source/destroot` is not present.
- `scripts/format.sh --check --all`: blocked on this Windows checkout because
  `clang-format` is not on PATH.
- `git diff --check`: succeeded.

## Expected Result

The Windows desktop app displays or logs `42` from the C# example module.

## Actual Result

The checked-in RNW solution builds with direct MSBuild and copies the managed
HostFXR payload beside the app binary. The shared JS app still calls
`ExampleModule.add(20, 22)`, throws unless the result is `42`, logs the
successful value, and displays `C# add result: 42`.

The app was not successfully launched through `react-native run-windows` in
this spike. The earliest remaining blocker is the RNW CLI build/deploy path on
this machine: after restore and autolink, MSBuild leaves or reuses locked
adapter PDB/PCH tracking files under the package project. Direct MSBuild of the
same solution succeeds after those helper locks clear. This is recorded as a
Windows toolchain/build invocation blocker, not as evidence against the JSI ABI
or generated sync function path.

## Artifacts

- `apps/desktop-app/windows`
- `apps/desktop-app/windows/Managed`
- `packages/expo-modules-dotnet/windows`

## Ownership/Lifetime Findings

The Windows adapter owns an `InstalledRuntime` instance containing the
`ReactNativeRuntimeConnector`, the opaque runtime handle, and the loaded module
library handles. The connector borrows RNW's `facebook::jsi::Runtime`; adapter
teardown invalidates the connector and releases the runtime handle before
dropping loaded libraries. Reload-safe production teardown remains future work.

## Scheduler Findings

RNW exposes the runtime through `ReactContext.CallInvoker()->invokeAsync`; the
adapter schedules installation work there and receives the active
`facebook::jsi::Runtime`. Generated synchronous module functions do not require
`invokeSync`; once registered, they run directly as JSI host functions inside
the JavaScript call. This proof did not establish a production RNW sync
scheduling contract beyond that direct host-function path.

## Stop/Go Decision

Go for the next lifecycle-contract design slice with a caveat: the Windows
adapter and direct MSBuild proof are sufficient ABI and packaging evidence, but
the RNW CLI build/deploy path still needs a follow-up for VS 2026 toolset and
PDB/PCH locking behavior before this is treated as a smooth developer workflow.
