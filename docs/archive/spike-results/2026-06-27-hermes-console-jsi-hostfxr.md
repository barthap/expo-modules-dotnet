# Result: Hermes Console JSI HostFXR Proof

Date: 2026-06-27
Machine: local macOS development machine
Repo/path: `<repo>`
Branch or commit: current `codex/generated-module-dispatch`

## Question

Can native C++ create a real Hermes-backed JSI runtime, pass a C ABI function
table and opaque runtime handle into managed code through HostFXR, and let
`Expo.JSI` create, inspect, read, return, and release JavaScript primitive
values, including strings, without P/Invoke?

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
JS called generated-looking C# string module: Hello, Zoë<NUL>JS
Wrong-type string argument produced a JS error
managed JSI proof: primitive strings round-tripped
Released owned value handles: 11
Released string result buffers: 4
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
- Managed code creates JavaScript strings, reads them back as strict UTF-8
  managed strings, preserves non-ASCII text, and preserves embedded NUL bytes.
- JavaScript calls `global.expo.modules.Math.add(41.5, true)`.
- JavaScript calls `global.expo.modules.Text.greet("Zoë\0JS")`.
- JavaScript calls `global.expo.modules.Text.greet(42)` and receives a JS error
  instead of letting an exception cross the unmanaged boundary.
- Native C++ owns the JSI host-function plumbing and forwards borrowed `this`
  and argument-buffer handles into C#.
- Generated-looking managed code decodes a number argument and a boolean
  argument through `JavaScriptArguments`, calls `MathModule.Add` directly,
  creates an owned result value, and returns that handle to native C++ for
  conversion back to JSI.
- Generated-looking managed code decodes a borrowed string argument through
  `JavaScriptBorrowedValue.AsString()`, calls `TextModule.Greet` directly,
  creates an owned JavaScript string result, and returns that handle to native
  C++ for conversion back to JSI.

## Actual Result

The proof succeeded. `HostFxrJSIProof.EntryPoints.Run` received the native API
table and runtime handle, used `Expo.JSI.JavaScriptRuntime`, created a number
through native JSI, read its kind and double value, and disposed the owned value
handle.

The proof also installed a generated-looking module graph at
`global.expo.modules.Math.add` and `global.expo.modules.Text.greet`.
JavaScript called the math function with a number and boolean, then called the
text function with a string containing non-ASCII text and an embedded NUL byte.
Native C++ created the host functions and passed call-scoped borrowed `this`
and argument-buffer handles into the managed callbacks. Generated-looking C#
code read all arguments through `JavaScriptArguments`, called the authored C#
modules directly, and returned new owned handles. Native copied those results
back into JSI and released the owned handles exactly once.

The wrong-type `Text.greet(42)` call produced a JS error. The current managed
host-function trampoline logs caught managed exceptions to stderr before
returning an error result, so this expected negative case still prints a
managed stack trace during the proof.

Native observed exactly eleven counted owned value handle releases through the
API table: seven registration-time value wrappers for the generated object
graph and four values from the independent managed smoke entry point. Native
also observed exactly four string result buffer releases: three managed
round-trip string reads and one borrowed string argument read. Borrowed callback
arguments were not released by C#. Callback return values are released by the
native host-function bridge after copying them back into JSI.

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

String reads use native-owned UTF-8 result buffers. C# copies each result into a
managed `string` with strict UTF-8 decoding and always invokes the native
release callback in a `finally` block. String creation uses strict UTF-8
encoding on the managed side and native UTF-8 validation before JSI string
creation.

The host-function callback context is retained by a managed `GCHandle` and is
released through native host-function context teardown. Return values from
managed callbacks are owned handles copied back to JSI and released exactly once
by native.

The native proof counts API-table owned value releases and native string result
buffer releases. It fails unless exactly eleven owned value handles and four
string result buffers are released.

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

Go. The headless generated-looking dispatch shape now works for primitive
numbers, booleans, and strings. Next slice should run a NativeAOT compatibility
audit or add the next minimal wrapper capability before real host adapter work.
