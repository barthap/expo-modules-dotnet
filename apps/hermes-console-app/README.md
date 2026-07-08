# Hermes Console App

This experiment creates a real Hermes-backed JSI runtime in native C++, passes
an `expo_jsi_api` function table plus opaque runtime handle into managed code
through HostFXR or NativeAOT, and verifies bridge paths without P/Invoke:

- managed code can create, inspect, read, and release JavaScript values through
  opaque JSI handles;
- JavaScript installs and calls a generated-looking module function at
  `globalThis._expoDotnet.modules.Math.add`. Native C++ owns the JSI host function
  plumbing, while C# generated-looking code decodes borrowed arguments, calls
  `MathModule.Add`, and returns an owned JavaScript value handle.
- JavaScript installs and calls a v2-syntax module function at
  `globalThis._expoDotnet.modules.V2Math.add`. The module lives in the same
  `HermesConsoleApp.csproj`, uses `[ExpoModule]` / `[JS]` authored syntax, and
  is registered through the Roslyn-generated provider.
- JavaScript calls the generated `Showcase` module to verify async functions,
  record codecs, JavaScript callbacks, and module events in the headless Hermes
  app.
- managed code can create and read JavaScript strings as strict UTF-8 text,
  including non-ASCII data and embedded NUL bytes;
- JavaScript installs and calls `globalThis._expoDotnet.modules.Text.greet`,
  which decodes a borrowed string argument in C# and returns a JavaScript
  string value.

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
scripts/run-hermes-console-app.sh
```

Run the NativeAOT path:

```sh
EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh
```

`EXPO_JSI_DOTNET_LOADER` accepts `hostfxr` or `nativeaot`. HostFXR remains the
default and uses `dotnet build`; NativeAOT uses `dotnet publish -r <rid>
/p:PublishAot=true /p:NativeLib=Shared` and loads the exported managed entry
points from the published shared library. The native CMake flag
`EXPO_JSI_DOTNET_LOADER` mirrors the script environment variable.

## Windows

Build or stage a Windows Hermes prebuilt first:

```powershell
.\scripts\build-hermes-windows.ps1 -Arch x64
```

The Windows script builds the official shared `hermesvm` target with Intl
disabled by default. The Intl-enabled upstream build is currently blocked by
Hermes/ICU header issues at the pinned Hermes revision.

Run the HostFXR console proof:

```powershell
.\scripts\run-hermes-console-app.ps1
```

Run the Windows managed Hermes test suite:

```powershell
.\scripts\test-managed.ps1
```

## NativeAOT Spike Record

- Hypothesis: the Hermes console proof can keep the same opaque C ABI and JSI
  proof logic while swapping the managed loader from HostFXR to NativeAOT.
- Commands run:
  - `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh --no-run`
  - `EXPO_JSI_DOTNET_LOADER=nativeaot scripts/run-hermes-console-app.sh`
  - `scripts/run-hermes-console-app.sh`
- Expected result: NativeAOT publishes `HermesConsoleApp` as a shared library,
  the native app resolves `hermes_console_app_run`,
  `hermes_console_app_create_session`, and
  `hermes_console_app_teardown_session`, and the existing Hermes proof prints
  `ok`.
- Actual result: NativeAOT publish, native build, NativeAOT run, and default
  HostFXR run all completed successfully.
- Artifacts: NativeAOT publishes under
  `apps/hermes-console-app/managed/HermesConsoleApp/bin/<configuration>/net10.0/<rid>/publish/`.
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
