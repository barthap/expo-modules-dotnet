# Result: Hermes Console JSI HostFXR Proof

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: `<repo>`
Branch or commit: current `codex/generated-module-dispatch`

## Question

Can native C++ create a real Hermes-backed JSI runtime, pass a C ABI function
table and opaque runtime handle into managed code through HostFXR, and let
`Expo.JSI` create, inspect, read, return, and release JavaScript values without
P/Invoke?

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
registered generated-looking Math module
JS called generated-looking C# module: 42.5
managed JSI proof: number kind=Number value=42.5
Released owned value handles: 6
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
- JavaScript calls `global.expo.modules.Math.add(41.5, true)`.
- Native C++ owns the JSI host-function plumbing and forwards borrowed `this`
  and argument-buffer handles into C#.
- Generated-looking managed code decodes a number argument and a boolean
  argument through `JavaScriptArguments`, calls `MathModule.Add` directly,
  creates an owned result value, and returns that handle to native C++ for
  conversion back to JSI.

## Actual Result

The proof succeeded. `HostFxrJSIProof.EntryPoints.Run` received the native API
table and runtime handle, used `Expo.JSI.JavaScriptRuntime`, created a number
through native JSI, read its kind and double value, and disposed the owned value
handle.

The proof also installed a generated-looking module graph at
`global.expo.modules.Math.add`. JavaScript called it with a number and boolean.
Native C++ created the host function and passed call-scoped borrowed `this` and
argument-buffer handles into the managed callback. Generated-looking C# code
read both arguments through `JavaScriptArguments`, called `MathModule.Add`
directly, and returned a new owned number handle. Native copied that result back
into JSI and released the owned handle exactly once.

Native observed exactly six counted owned value handle releases through the API
table: five registration-time value wrappers for the generated object graph and
one value from the independent managed smoke entry point. Borrowed callback
arguments were not released by C#. The callback return value is released by the
native host-function bridge after copying it back into JSI.

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
managed package owns `JavaScriptValue`, `JavaScriptObject`, and
`JavaScriptFunction` wrapper lifetime and calls the matching release function
from `Dispose` or transfers value ownership with `Detach`. `JavaScriptArguments`
and `JavaScriptBorrowedValue` are callback-scoped borrowed wrappers and are not
released by C#.

The host-function callback context is retained by a managed `GCHandle` and is
released through native host-function context teardown. Return values from
managed callbacks are owned handles copied back to JSI and released exactly once
by native.

The native proof counts API-table owned value releases and fails unless exactly
six counted owned value handles are released.

Future hosted connectors must release bridge-owned state without destroying a
host-owned JSI runtime.

## Scheduler Findings

The proof uses an immediate same-thread scheduler object because only
synchronous value creation, reading, and JS host-function dispatch are tested.
No RN scheduler, call-invoker, or runtime executor behavior is proven here.

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

Go. The headless generated-looking dispatch shape works. Next slice should add
string conversion or run a NativeAOT compatibility audit before real host
adapter work.
