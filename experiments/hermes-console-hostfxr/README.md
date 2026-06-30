# Hermes Console HostFXR Proof

This experiment creates a real Hermes-backed JSI runtime in native C++, passes
an `expo_jsi_api` function table plus opaque runtime handle into managed code
through HostFXR or NativeAOT, and verifies bridge paths without P/Invoke:

- managed code can create, inspect, read, and release JavaScript values through
  opaque JSI handles;
- JavaScript installs and calls a generated-looking module function at
  `global.expo.modules.Math.add`. Native C++ owns the JSI host function
  plumbing, while C# generated-looking code decodes borrowed arguments, calls
  `MathModule.Add`, and returns an owned JavaScript value handle.
- JavaScript installs and calls a v2-syntax module function at
  `global.expo.modules.V2Math.add`. The module lives in the same
  `HostFxrJSIProof.csproj`, uses `[ExpoModule]` / `[JS]` authored syntax, and
  is registered through the Roslyn-generated provider.
- managed code can create and read JavaScript strings as strict UTF-8 text,
  including non-ASCII data and embedded NUL bytes;
- JavaScript installs and calls `global.expo.modules.Text.greet`, which decodes
  a borrowed string argument in C# and returns a JavaScript string value.

Managed JSI calls must run on the Hermes executor thread. The native proof
uses the console runtime executor's synchronous path before entering managed
module registration and primitive wrapper checks.

Build Hermes from the official repository first:

```sh
scripts/build-hermes-macos.sh
```

The script downloads `facebook/hermes` into `build/hermes/source`, builds the
macOS `hermesvm.framework`, and leaves headers/frameworks under
`build/hermes/source/destroot`.

CMake defaults to that `destroot`. To use a different local prebuilt, pass
`-DHERMES_PREBUILT_ROOT=<destroot>`.

Run the default HostFXR path:

```sh
scripts/run-hermes-experiment.sh
```

Run the NativeAOT path:

```sh
EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-experiment.sh
```

`EXPO_JSI_DOTNET_LOADER` accepts `hostfxr` or `nativeaot`. HostFXR remains the
default and uses `dotnet build`; NativeAOT uses `dotnet publish -r <rid>
/p:PublishAot=true /p:NativeLib=Shared` and loads the exported managed entry
points from the published shared library. The native CMake flag
`EXPO_JSI_DOTNET_LOADER` mirrors the script environment variable.

## NativeAOT Spike Record

- Hypothesis: the Hermes console proof can keep the same opaque C ABI and JSI
  proof logic while swapping the managed loader from HostFXR to NativeAOT.
- Commands run:
  - `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-experiment.sh --no-run`
  - `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-experiment.sh`
  - `scripts/run-hermes-experiment.sh`
- Expected result: NativeAOT publishes `HostFxrJSIProof` as a shared library,
  the native app resolves `hostfxr_jsi_proof_run` and
  `hostfxr_jsi_register_modules`, and the existing Hermes proof prints `ok`.
- Actual result: NativeAOT publish, native build, NativeAOT run, and default
  HostFXR run all completed successfully.
- Artifacts: NativeAOT publishes under
  `experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/bin/<configuration>/net10.0/<rid>/publish/`.
- Ownership/lifetime findings: NativeAOT did not require changing the
  `expo_jsi_api` table, opaque runtime handle, owned value release counting, or
  string result release counting.
- Scheduler findings: managed JSI calls still run through the Hermes console
  runtime executor's synchronous path; loader selection does not change runtime
  thread ownership.
- Stop/go decision: go for the Hermes console proof. The managed proof and
  module helper closure are NativeAOT-compatible in this experiment. The
  Hermes testhost remains HostFXR/native-library based until a separate slice
  explicitly targets it.
