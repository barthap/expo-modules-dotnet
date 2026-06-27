# Result: Hermes Console JSI HostFXR Proof

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: `<repo>`
Branch or commit: current `main`

## Question

Can native C++ create a real Hermes-backed JSI runtime, pass a C ABI function
table and opaque runtime handle into managed code through HostFXR, and let
`Expo.JSI` create, inspect, read, and release a number value without P/Invoke?

## Commands Run

```sh
scripts/build-hermes-macos.sh
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
<cmake> -S experiments/hermes-console-hostfxr/native -B build/hermes-console-hostfxr
<cmake> --build build/hermes-console-hostfxr --target hermes_console_hostfxr
./build/hermes-console-hostfxr/hermes_console_hostfxr
```

Meaningful output:

```text
Expo.JSI -> <repo>/managed/packages/Expo.JSI/bin/Debug/net10.0/Expo.JSI.dll
HostFxrJSIProof -> <repo>/experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/bin/Debug/net10.0/HostFxrJSIProof.dll
Build succeeded.
[100%] Built target hermesvm
Hermes macOS prebuilt ready:
  HERMES_PREBUILT_ROOT=<repo>/build/hermes/source/destroot
[100%] Built target hermes_console_hostfxr
Loaded HostFXR path: <dotnet-root>/host/fxr/<version>/libhostfxr.dylib
Created Hermes-backed JSI runtime
managed JSI proof: number kind=Number value=42.5
Released owned value handles: 1
hermes console hostfxr proof: ok
```

## Expected Result

- Hermes dependency evidence gate passes.
- Native code uses a real Hermes-backed `facebook::jsi::Runtime`.
- Native passes an `expo_jsi_api` function table and runtime handle to managed
  code.
- Managed code constructs `Expo.JSI.JavaScriptRuntime` from native-provided
  capabilities.
- Managed code creates a number value, reads `Number`, reads `42.5`, disposes
  the value, and exits 0.

## Actual Result

The proof succeeded. `HostFxrJSIProof.EntryPoints.Run` received the native API
table and runtime handle, used `Expo.JSI.JavaScriptRuntime`, created a number
through native JSI, read its kind and double value, and disposed the owned value
handle. Native observed exactly one owned value handle release.

## Artifacts

- `native/include/expo_jsi.h`
- `native/packages/jsi/include/JsiRuntimeConnector.h`
- `native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
- `native/packages/jsi/include/ExpoJsiBridge.h`
- `native/packages/jsi/src/ExpoJsiBridge.cpp`
- `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- `managed/packages/Expo.JSI/`
- `experiments/hermes-console-hostfxr/`

## Ownership And Lifetime Findings

`HermesConsoleRuntimeConnector` owns the Hermes runtime for this console proof.
The bridge runtime handle owns only bridge state and borrows the connector. The
managed package owns `JavaScriptValue` wrapper lifetime and calls
`release_value` once from `Dispose`. The native proof counts releases and fails
unless exactly one owned value handle is released.

Future hosted connectors must release bridge-owned state without destroying a
host-owned JSI runtime.

## Scheduler Findings

The proof uses an immediate same-thread scheduler object because only
synchronous number creation and reading are tested. No RN scheduler,
call-invoker, or runtime executor behavior is proven here.

## Platform Findings

This proof is macOS-local and headless. It uses packaged Hermes artifacts built
locally from the official `facebook/hermes` repository, but does not use
expo-desktop, RNW, RN macOS, app packaging, or views.

## Loader And NativeAOT Findings

HostFXR is experiment-only. `Expo.JSI` has no HostFXR-specific code and no
P/Invoke declarations for core bridge calls. The managed package receives a
native function table pointer and opaque runtime handle, which keeps the core
bridge shape compatible with a later NativeAOT entry path.

## Stop/Go Decision

Go. The next proof can build on the function-table and opaque-handle bridge
shape to add more JSI value kinds or wrapper behavior while still staying
headless.
