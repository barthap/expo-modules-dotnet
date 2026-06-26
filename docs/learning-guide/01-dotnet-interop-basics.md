# 01 - .NET Interop Basics For This Bridge

## Audience And Goal

This guide is for a reader who knows Expo modules, React Native, JSI, and the
idea of native bridges, but is learning modern .NET interop. It explains the
interop concepts needed for the portable C# / JSI bridge plan.

The central mental model:

```text
Native C++ process owns JSI.
C# module code runs inside .NET.
Interop is the boundary where they call each other.
```

Interop is not one technology. It is a set of choices:

- how the native process starts or loads .NET;
- how C# exports entry points back to native;
- how C# calls native functions;
- how data crosses the boundary;
- who owns memory;
- what remains compatible with NativeAOT.

## HostFXR: Starting .NET From Native Code

HostFXR is the official native hosting layer for .NET. A native executable can
load HostFXR, point it at a `.runtimeconfig.json`, and ask it to resolve a
managed function from a C# assembly.

In this project, HostFXR is useful for phase 1 because it lets a small macOS
C++ proof load a normal framework-dependent C# assembly. That keeps iteration
fast:

```text
dotnet build
native host starts .NET with HostFXR
native host resolves managed entry point
managed code calls back into native function table
```

The platform-specific part is dynamic library loading:

```text
Windows: LoadLibraryW + GetProcAddress + .dll
macOS:   dlopen + dlsym + .dylib
Linux:   dlopen + dlsym + .so
```

HostFXR itself is portable, but your loader code must abstract these platform
loading differences.

## HostFXR Bootstrap Walkthrough

This section is illustrative, not final API. The phase 1 proof should make this
flow concrete enough that a future bridge can replace the smoke-test names with
real bridge names.

The native host does four jobs:

1. locate and load the `hostfxr` dynamic library;
2. initialize .NET from the managed assembly's `.runtimeconfig.json`;
3. ask HostFXR for the `load_assembly_and_get_function_pointer` delegate;
4. resolve a managed entry point and pass native bridge state into C#.

At a high level:

```text
native host process
  dlopen(hostfxr)
  hostfxr_initialize_for_runtime_config(Bridge.runtimeconfig.json)
  hostfxr_get_runtime_delegate(load_assembly_and_get_function_pointer)
  load_assembly_and_get_function_pointer(Bridge.dll, EntryPoints.Initialize)
  initialize(&native_function_table, native_context)
```

The native-side code shape may look like this:

```cpp
// Tutorial shape only. Omit error handling details in the first read, but do
// include them in the actual spike.
using hostfxr_initialize_for_runtime_config_fn = int32_t (*)(
  const char_t *runtime_config_path,
  const hostfxr_initialize_parameters *parameters,
  hostfxr_handle *host_context_handle);

using hostfxr_get_runtime_delegate_fn = int32_t (*)(
  hostfxr_handle host_context_handle,
  hostfxr_delegate_type type,
  void **delegate);

using load_assembly_and_get_function_pointer_fn = int32_t (*)(
  const char_t *assembly_path,
  const char_t *type_name,
  const char_t *method_name,
  const char_t *delegate_type_name,
  void *reserved,
  void **delegate);

using expo_initialize_fn = int32_t (*)(
  const expo_jsi_api *api,
  void *native_context);

void bootstrap_managed_bridge() {
  void *hostfxr = dlopen("libhostfxr.dylib", RTLD_NOW);

  auto initialize_for_runtime_config =
    reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
      dlsym(hostfxr, "hostfxr_initialize_for_runtime_config"));
  auto get_runtime_delegate =
    reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
      dlsym(hostfxr, "hostfxr_get_runtime_delegate"));

  hostfxr_handle host_context = nullptr;
  initialize_for_runtime_config(
    STR("Expo.CSharpJsi.runtimeconfig.json"),
    nullptr,
    &host_context);

  void *load_assembly_delegate = nullptr;
  get_runtime_delegate(
    host_context,
    hdt_load_assembly_and_get_function_pointer,
    &load_assembly_delegate);

  auto load_assembly_and_get_function_pointer =
    reinterpret_cast<load_assembly_and_get_function_pointer_fn>(
      load_assembly_delegate);

  void *initialize_ptr = nullptr;
  load_assembly_and_get_function_pointer(
    STR("Expo.CSharpJsi.dll"),
    STR("Expo.CSharpJsi.EntryPoints, Expo.CSharpJsi"),
    STR("Initialize"),
    STR("Expo.CSharpJsi.EntryPoints+InitializeDelegate, Expo.CSharpJsi"),
    nullptr,
    &initialize_ptr);

  auto initialize = reinterpret_cast<expo_initialize_fn>(initialize_ptr);
  initialize(&g_expo_jsi_api, &g_native_bridge_context);
}
```

The managed entry point should receive only ABI-safe data:

```csharp
public static unsafe class EntryPoints
{
  public delegate int InitializeDelegate(ExpoJsiApi* api, IntPtr nativeContext);

  public static int Initialize(ExpoJsiApi* api, IntPtr nativeContext)
  {
    try
    {
      BridgeRuntime.Initialize(api, nativeContext);
      return 1;
    }
    catch
    {
      return 0;
    }
  }
}
```

For NativeAOT, the exported entry point may use `[UnmanagedCallersOnly]`
instead of HostFXR delegate resolution, but the payload should stay the same:
C# receives a native function table pointer and native context/handle, then
uses that table for all JSI operations. That is the loader/runtime separation
the docs keep emphasizing.

Pitfall:

HostFXR can make reflection easy because normal .NET runtime facilities are
available. That does not mean the v2 bridge should use runtime reflection.
HostFXR is a loader. It is not the binding architecture.

## NativeAOT: Compiling .NET To A Native Binary

NativeAOT compiles C# into a native binary for a specific runtime identifier,
such as:

```text
osx-arm64
osx-x64
win-x64
```

NativeAOT matters because a future production bridge may want fewer runtime
dependencies and more predictable startup. But NativeAOT is not the fastest
first proof. It is stricter than HostFXR:

- reflection must be explicit and trimming-safe;
- dynamic code generation is limited or unavailable;
- exported entry points need blittable signatures;
- platform artifacts should be built on the target platform for normal React
  Native workflows.

Mapping to this project:

- Use HostFXR first to learn quickly.
- Design the ABI and generated bindings as if NativeAOT will run them later.
- Treat NativeAOT failures as design feedback, not as a reason to abandon the
  C ABI.

## C ABI: The Stable Boundary

A C ABI is a narrow binary contract that C++ and C# can both understand. It
does not expose C++ classes, templates, exceptions, or object layout.

Use C ABI shapes like:

```c
typedef struct expo_js_value_t *expo_js_value_handle;

typedef struct expo_js_error {
  int32_t code;
  const char *message;
  int32_t message_len;
} expo_js_error;

int32_t expo_js_value_get_bool(
  expo_js_runtime_handle runtime,
  expo_js_value_handle value,
  int32_t *out_value,
  expo_js_error *error);
```

Avoid shapes like:

```c++
jsi::Value *expo_get_value(jsi::Runtime &runtime);
std::string expo_get_string();
```

The first example is ABI-friendly. The second leaks C++ types and ownership
rules across the boundary.

## Blittable Types

A type is blittable when its memory representation can be passed between
managed and unmanaged code without conversion. For this bridge, prefer:

- `int32_t`, `uint32_t`, `double`, pointer-sized integers;
- raw pointers represented as `IntPtr` or typed unmanaged pointers in C#;
- structs containing only blittable fields;
- explicit pointer + length for strings and buffers.

Avoid:

- C# `string` directly in exported unmanaged signatures;
- C# arrays in unmanaged signatures;
- C++ `std::string`, `std::vector`, or exceptions in ABI signatures;
- arbitrary managed objects crossing unmanaged boundaries.

## C# Calling Native Functions

C# can call native functions through P/Invoke or unmanaged function pointers.
For a bridge that wants loader neutrality, a function table is attractive:

```csharp
public unsafe struct ExpoJsiApi
{
  public delegate* unmanaged<IntPtr, IntPtr, JavaScriptValueKind> GetValueKind;
  public delegate* unmanaged<IntPtr, IntPtr, double*, ExpoError*, int> TryGetDouble;
  public delegate* unmanaged<IntPtr, IntPtr, ExpoStringResult> GetStringUtf8;
}
```

This lets native initialize C# with a table:

```csharp
public static unsafe class BridgeRuntime
{
  private static ExpoJsiApi* s_api;

  public static void Initialize(ExpoJsiApi* api)
  {
    s_api = api;
  }
}
```

That same idea can work whether the managed code was loaded with HostFXR or
compiled with NativeAOT.

## C# Exporting Entry Points

Native code needs a way to enter managed code. NativeAOT supports
`[UnmanagedCallersOnly]`:

```csharp
using System.Runtime.InteropServices;

public static unsafe class EntryPoints
{
  [UnmanagedCallersOnly(EntryPoint = "expo_modules_initialize")]
  public static int Initialize(ExpoJsiApi* api, IntPtr runtime)
  {
    BridgeRuntime.Initialize(api);
    return 0;
  }
}
```

Important constraints:

- methods must be static;
- parameters and return type must be unmanaged/blittable;
- managed exceptions must not escape the method;
- convert failures into error codes or structured error results.

HostFXR can resolve managed methods differently, but the phase 1 proof should
still keep signatures close to NativeAOT-friendly shapes.

## Strings And Buffers

Strings are not magic at the ABI boundary. Decide the encoding and ownership.
For this bridge, prefer UTF-8 pointer + byte length:

```c
typedef struct expo_string_result {
  int32_t ok;
  const uint8_t *data;
  int32_t len;
  void *release_context;
  void (*release)(void *release_context);
  expo_js_error error;
} expo_string_result;
```

C# can copy it into a managed string:

```csharp
public static unsafe string CopyUtf8AndRelease(ExpoStringResult result)
{
  if (result.Ok == 0)
  {
    throw new JavaScriptBridgeException(result.Error.ToMessage());
  }

  try
  {
    return Encoding.UTF8.GetString(result.Data, result.Length);
  }
  finally
  {
    result.Release?.Invoke(result.ReleaseContext);
  }
}
```

Buffers follow the same principle, except the content may be binary and may be
borrowed. Do not keep a pointer after the borrow window ends.

## How This Maps To The Project

HostFXR proof:

- native process starts .NET;
- managed entry point receives function table;
- memory ownership is explicit.

C ABI proof:

- C++ owns JSI;
- C# receives handles;
- wrappers call function pointers.

NativeAOT audit:

- publish a small NativeAOT binary;
- inspect exported symbols;
- check for reflection/dynamic-code blockers.

Source generator:

- attributes are read at build time;
- generated runtime code calls wrappers directly;
- runtime does not scan assemblies or invoke methods reflectively for v2.

## Common Pitfalls

- Confusing HostFXR with a runtime design. HostFXR loads .NET; it does not
  decide how modules are invoked.
- Letting C# see C++ object layouts. Use opaque handles.
- Forgetting release functions for strings or buffers.
- Throwing exceptions across unmanaged boundaries.
- Using reflection because it is easy in a HostFXR proof, then discovering the
  design is hostile to NativeAOT.
- Treating macOS NativeAOT success as Windows success. Windows NativeAOT and
  RNW packaging still need Windows verification.
