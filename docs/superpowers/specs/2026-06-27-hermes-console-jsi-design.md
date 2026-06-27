# Hermes Console JSI Design

Date: 2026-06-27
Repo: `<repo>`

## Context

The repository already has standalone HostFXR and NativeAOT smoke experiments.
Those prove loader mechanics and explicit managed/native buffer ownership, but
they do not touch real JSI. The next proof should validate the first real
C++ JSI -> C ABI function table -> C# wrapper path without introducing
expo-desktop app integration, RNW/RN macOS adapters, module dispatch, or fake
JSI.

This design replaces the old idea of a fake ABI skeleton. A fake handle table
does not prove the bridge semantics that matter.

The governing architecture remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## Approved Direction

- Build a headless Hermes console proof first if Hermes can be consumed without
  becoming a dependency/build-system project.
- Use HostFXR as the loader for this proof.
- Keep the reusable managed package loader-neutral and NativeAOT-compatible.
- Do not use P/Invoke for core bridge calls.
- Pass a C ABI function table pointer from native into managed code.
- Hide C function pointer syntax behind typedefs on the C side and internal
  interop wrappers on the C# side.
- Implement all layers needed for the proof:
  - C++ runtime connector contract;
  - owned Hermes console connector;
  - native bridge core and opaque handles;
  - C ABI function table;
  - `Expo.JSI` managed package;
  - experiment-only HostFXR proof assembly.
- Keep experiment code under `experiments/`.
- Keep reusable package code under normal package-shaped paths.

## Non-Goals

Do not build in this slice:

- expo-desktop app integration;
- RNW adapter;
- React Native macOS adapter;
- generalized C# function or module dispatch;
- `Expo.ModulesCore`;
- autolinking package;
- npm package layout;
- source generator;
- view APIs;
- strings, objects, functions, host functions, promises, or ArrayBuffers;
- NativeAOT loader for this real-JSI path.

## Hermes Dependency Strategy

Use packaged Hermes artifacts first, preferably from React Native/npm ecosystem
artifacts, if they can be consumed by a tiny standalone native CMake proof with
small and understandable glue.

Evidence gate:

```text
Can we build and run a minimal native probe that:
  includes jsi/jsi.h
  includes Hermes runtime headers
  creates a Hermes-backed facebook::jsi::Runtime
  creates or evaluates a primitive value
  exits cleanly
```

Continue with the Hermes console proof only if this gate passes.

Stop and record findings if standalone Hermes consumption requires:

- custom Hermes builds;
- vendoring Hermes source;
- patching Hermes;
- reverse-engineering React Native or expo app build internals;
- large platform-specific build machinery unrelated to bridge semantics.

If the evidence gate fails, the result is a decision note, not a failed bridge
proof. The next design path should switch to a hosted expo-desktop/RN proof
that receives a runtime from a real host.

## Runtime Connector Contract

Define a C++-side runtime connector boundary. The bridge core receives this
capability instead of constructing or assuming a concrete runtime owner.

Conceptual contract:

```cpp
class JsiRuntimeConnector {
public:
  virtual ~JsiRuntimeConnector() = default;

  virtual facebook::jsi::Runtime &runtime() = 0;
  virtual JsiScheduler &scheduler() = 0;
  virtual bool isRuntimeValid() const = 0;
  virtual void invalidate() = 0;
};
```

Ownership is connector-specific. Bridge usage is connector-neutral.

First implementation:

```text
HermesConsoleRuntimeConnector
  owns Hermes runtime
  owns immediate/same-thread scheduler
  invalidates runtime on connector teardown
```

Future implementations:

```text
ExpoDesktopRuntimeConnector / RNWRuntimeConnector / RNMacOSRuntimeConnector
  borrow host-owned runtime
  borrow or wrap host scheduler
  never destroy the host runtime
  become invalid when host lifecycle says runtime is gone
```

Important lifecycle rule:

Disposing a bridge runtime handle releases bridge-owned state. It must not mean
"destroy the underlying JSI runtime" in every connector. In the Hermes console
connector, bridge teardown may also dispose the owned Hermes runtime. In hosted
connectors later, teardown must release bridge state without destroying the
host-owned runtime.

## Native Bridge Core

The native bridge core owns the handle table and performs all direct JSI work.
It consumes a `JsiRuntimeConnector` and exposes only opaque C handles to managed
code.

First proof handles:

```c
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
```

First proof operations:

- create a number value in the real Hermes runtime;
- read value kind;
- read number as double;
- release an owned value handle.

The bridge core must not expose raw `facebook::jsi::Runtime`,
`facebook::jsi::Value`, `facebook::jsi::Object`, or `facebook::jsi::Function`
layouts to C#.

## C ABI Function Table

`Expo.JSI` should not import an `expo_jsi` native library with P/Invoke for core
bridge calls. Native already owns or receives the JSI runtime, so native passes
managed code the exact ABI capabilities it may call.

Use typedefs for all function pointers:

```c
typedef enum expo_jsi_value_kind {
  EXPO_JSI_VALUE_UNDEFINED = 0,
  EXPO_JSI_VALUE_NULL = 1,
  EXPO_JSI_VALUE_BOOL = 2,
  EXPO_JSI_VALUE_NUMBER = 3,
  EXPO_JSI_VALUE_STRING = 4,
  EXPO_JSI_VALUE_OBJECT = 5,
  EXPO_JSI_VALUE_FUNCTION = 6,
  EXPO_JSI_VALUE_ARRAY_BUFFER = 7
} expo_jsi_value_kind;

typedef struct expo_jsi_error {
  int32_t code;
  const char *message;
  int32_t message_len;
} expo_jsi_error;

typedef struct expo_jsi_value_result {
  int32_t ok;
  expo_jsi_value_handle value;
  expo_jsi_error error;
} expo_jsi_value_result;

typedef expo_jsi_value_result (*expo_jsi_create_number_fn)(
  expo_jsi_runtime_handle runtime,
  double value);

typedef expo_jsi_value_kind (*expo_jsi_get_value_kind_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_error *error);

typedef double (*expo_jsi_get_double_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_error *error);

typedef void (*expo_jsi_release_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value);

typedef struct expo_jsi_api {
  uint32_t size;
  uint32_t version;

  expo_jsi_create_number_fn create_number;
  expo_jsi_get_value_kind_fn get_value_kind;
  expo_jsi_get_double_fn get_double;
  expo_jsi_release_value_fn release_value;
} expo_jsi_api;
```

The table starts with `size` and `version` for compatibility checks.

Structured error behavior follows the existing architecture docs:

- C++ exceptions do not cross the C ABI.
- C# exceptions do not cross unmanaged frames.
- ABI calls return `ok/error` structs or write structured errors.

## `Expo.JSI` Managed Package

Reusable managed package location:

```text
managed/
  packages/
    Expo.JSI/
      Expo.JSI.csproj
      Interop/
        ExpoJsiApi.cs
        ExpoJsiTypes.cs
      JavaScriptRuntime.cs
      JavaScriptValue.cs
      JavaScriptValueKind.cs
```

`Expo.JSI` is a normal reusable library. It must not contain:

- HostFXR-specific code;
- NativeAOT-specific loader code;
- experiment entry points;
- P/Invoke declarations for core bridge calls;
- module APIs;
- generalized dispatch APIs.

Minimal public API:

```csharp
namespace Expo.JSI;

public enum JavaScriptValueKind
{
  Undefined = 0,
  Null = 1,
  Bool = 2,
  Number = 3,
  String = 4,
  Object = 5,
  Function = 6,
  ArrayBuffer = 7,
}

public sealed class JavaScriptRuntime
{
  public static JavaScriptRuntime FromNative(nint api, nint runtimeHandle);
  public JavaScriptValue CreateNumber(double value);
}

public sealed class JavaScriptValue : IDisposable
{
  public JavaScriptValueKind Kind { get; }
  public double AsDouble();
}
```

`FromNative` is intentionally low-level. It exists so experiments and future
bootstrap layers can construct wrappers from native-provided capabilities. It
should validate API `size`, API `version`, non-null function pointers, and the
runtime handle before exposing a runtime wrapper.

Internal interop code may use unsafe C# function pointers, but public
`Expo.JSI` APIs must hide them.

## HostFXR Experiment

Experiment location:

```text
experiments/
  hermes-console-hostfxr/
    README.md
    native/
      CMakeLists.txt
      main.cpp
    managed/
      HostFxrJSIProof/
        HostFxrJSIProof.csproj
        EntryPoints.cs
```

The experiment consumes reusable package code:

```text
native/
  include/
    expo_jsi.h
  packages/
    jsi/

managed/
  packages/
    Expo.JSI/
```

Experiment-only responsibilities:

- HostFXR loading;
- Hermes console executable setup;
- `HostFxrJSIProof` managed entry point;
- proof README;
- proof result note.

`HostFxrJSIProof` is not part of public `Expo.JSI`. It may reference
`managed/packages/Expo.JSI`, but it must live under `experiments/`.

Experiment flow:

```text
native executable
  runs Hermes dependency evidence probe
  creates HermesConsoleRuntimeConnector
  creates bridge runtime handle
  prepares expo_jsi_api function table
  loads HostFxrJSIProof.dll through HostFXR
  resolves experiment-only proof entry point
  calls Run(api*, runtimeHandle)
  receives 0 for success or nonzero for failure
```

Managed proof flow:

```text
HostFxrJSIProof.EntryPoints.Run(api*, runtimeHandle)
  JavaScriptRuntime.FromNative(api*, runtimeHandle)
  runtime.CreateNumber(42.5)
  value.Kind == Number
  value.AsDouble() == 42.5
  value.Dispose()
  return 0
```

The proof entry point can be hardcoded in the experiment because it belongs to
the experiment assembly. It must not be presented as the future generalized C#
function/module dispatch model.

## Verification

The proof is complete only when command output or result notes demonstrate:

- the Hermes dependency evidence gate passed, or the proof stopped with a clear
  decision note;
- native code used a real Hermes-backed `facebook::jsi::Runtime`;
- managed code used `Expo.JSI.JavaScriptRuntime` and `JavaScriptValue`;
- a number value was created through real JSI;
- managed code read value kind as `Number`;
- managed code read the double value as `42.5`;
- owned value handle release ran exactly once;
- `Expo.JSI` contains no P/Invoke declarations for core bridge calls;
- `Expo.JSI` contains no HostFXR-specific code;
- forbidden reflection/dynamic invocation/JSON searches return no matches;
- result note records ownership, scheduler, platform, and loader findings.

Expected result note path:

```text
docs/spike-results/YYYY-MM-DD-hermes-console-jsi-hostfxr.md
```

## Stop Gates

Stop before bridge implementation if:

- packaged Hermes cannot be consumed with small, understandable CMake glue;
- a fake runtime or fake JSI value would be required;
- C# needs raw JSI layouts;
- `Expo.JSI` needs to know whether it was loaded by HostFXR or NativeAOT;
- core bridge calls require P/Invoke to a named native library;
- ownership cannot be described for runtime or value handles;
- synchronous proof code starts depending on real RN scheduler/call-invoker
  behavior;
- implementation drifts into modules, host objects, promises, app integration,
  autolinking, source generation, or views.

Stop after implementation if:

- managed or native exceptions can cross unmanaged frames;
- value release can leak or double-release;
- disposing bridge runtime would incorrectly destroy a future host-owned
  runtime.

## What This Proves

If successful, this slice proves:

- a real Hermes-backed JSI runtime can be used outside an app host for bridge
  semantics;
- native can pass a function table and opaque runtime handle into managed code;
- `Expo.JSI` can create/read/release a real number value without P/Invoke;
- the connector model can support an owned runtime without baking ownership into
  the bridge core.

## What This Does Not Prove

This slice does not prove:

- expo-desktop integration;
- RNW integration;
- React Native macOS integration;
- app lifecycle behavior;
- JS thread scheduling through call invokers/runtime executors;
- generalized C# function or module dispatch;
- NativeAOT loader for the real-JSI path;
- module DSL or generated bindings.

## Self-Review

- No fake JSI or fake ABI path remains in the design.
- Experiment code is separate from reusable package code.
- `HostFxrJSIProof` is experiment-only and not part of public `Expo.JSI`.
- The function table avoids P/Invoke for core bridge calls.
- The connector contract allows owned Hermes and future borrowed host runtimes.
- The Hermes dependency strategy has an evidence-based stop gate.
- The design carries forward the existing structured-error rule.
