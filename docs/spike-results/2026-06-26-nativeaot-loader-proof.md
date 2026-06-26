# Result: NativeAOT Loader Proof

Date: 2026-06-26
Machine: macOS local development machine
Repo/path: <repo>
Branch or commit: main, working tree implementation

## Question

Can native macOS code load a NativeAOT-produced shared library with `dlopen`,
resolve explicit unmanaged-callable managed exports with `dlsym`, receive a
managed-owned UTF-8 buffer, and release that buffer explicitly?

## Commands Run

```sh
dotnet publish experiments/nativeaot-smoke/managed/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r osx-arm64
cmake -S experiments/nativeaot-smoke/native -B build/nativeaot-smoke
cmake --build build/nativeaot-smoke --target nativeaot_smoke
./build/nativeaot-smoke/nativeaot_smoke
nm -gU experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib | rg "nativeaot_smoke_get_message|nativeaot_smoke_release_message"
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" experiments/nativeaot-smoke -g '*.cs' -g '*.cpp' -g '*.csproj' -g 'CMakeLists.txt'
file experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib
```

Before creating the experiment, the publish command was also run once to
verify the red state:

```text
MSBUILD : error MSB1009: Project file does not exist.
Switch: experiments/nativeaot-smoke/managed/NativeAotSmoke/NativeAotSmoke.csproj
```

## Expected Result

The NativeAOT publish creates an `osx-arm64` `.dylib`. The native executable
prints the NativeAOT library path, prints `expo-csharp-jsi-smoke`, and prints
confirmation that the managed-owned buffer was released. The exported symbol
check finds the get/release exports. The forbidden reflection/JSON search
returns no matches.

## Actual Result

`dotnet publish`:

```text
  Determining projects to restore...
  Restored <repo>/experiments/nativeaot-smoke/managed/NativeAotSmoke/NativeAotSmoke.csproj (in 91 ms).
  NativeAotSmoke -> <repo>/experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/NativeAotSmoke.dll
  Generating native code
  NativeAotSmoke -> <repo>/experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/
```

CMake configure:

```text
-- The CXX compiler identification is AppleClang 17.0.0.17000603
-- Detecting CXX compiler ABI info
-- Detecting CXX compiler ABI info - done
-- Check for working CXX compiler: <system-cxx-compiler> - skipped
-- Detecting CXX compile features
-- Detecting CXX compile features - done
-- Configuring done (0.2s)
-- Generating done (0.0s)
-- Build files have been written to: <repo>/build/nativeaot-smoke
```

Native build and smoke run after the final output formatting change:

```text
[ 50%] Building CXX object CMakeFiles/nativeaot_smoke.dir/main.cpp.o
[100%] Linking CXX executable nativeaot_smoke
[100%] Built target nativeaot_smoke
Loaded NativeAOT library: <repo>/experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib
Managed payload: expo-csharp-jsi-smoke
Released managed-owned payload buffer
```

Exported symbol check:

```text
0000000000062ff0 S _nativeaot_smoke_get_message
0000000000063090 S _nativeaot_smoke_release_message
```

Published library file type:

```text
experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib: Mach-O 64-bit dynamically linked shared library arm64
```

Forbidden reflection/JSON search:

```text
No matches. `rg` exited with code 1, which is expected for no matches.
```

## Artifacts

- `experiments/nativeaot-smoke/README.md`
- `experiments/nativeaot-smoke/managed/NativeAotSmoke/NativeAotSmoke.csproj`
- `experiments/nativeaot-smoke/managed/NativeAotSmoke/EntryPoints.cs`
- `experiments/nativeaot-smoke/native/CMakeLists.txt`
- `experiments/nativeaot-smoke/native/main.cpp`
- `docs/spike-results/2026-06-26-nativeaot-loader-proof.md`

## Ownership And Lifetime Findings

The managed entry point allocates the UTF-8 buffer with `NativeMemory.Alloc`.
The native executable treats the returned pointer as managed-owned and calls
`nativeaot_smoke_release_message` exactly once after reading it. The native
side does not free the buffer directly.

## Platform Findings

The proof is macOS-local and publishes a NativeAOT shared library for
`osx-arm64`. The native executable loads the library with `dlopen` and resolves
exports with `dlsym`. It does not use Windows loader APIs, RNW, WinUI, AppKit,
expo-desktop, HostFXR, nethost, runtimeconfig files, assembly loading, or real
JSI.

## Scheduler Findings

No JS scheduler is involved in this loader proof. The managed entry point is a
synchronous unmanaged-callable function and does not touch JSI.

## Reflection/AOT Findings

The NativeAOT shared library exposes explicit unmanaged-callable entry points.
The experiment does not use `Assembly.GetTypes`, `MethodInfo.Invoke`,
`Delegate.DynamicInvoke`, `object?[]`, or JSON conversion.

## Decision

Go. The NativeAOT smoke proof publishes an `osx-arm64` shared library, the
native executable loads it directly, invokes the managed entry point, and
releases the managed-owned buffer explicitly.

## Follow-Up Questions

- Decide whether future ABI skeleton work should keep HostFXR and NativeAOT
  experiments separate or share one generated ABI contract once the first real
  bridge header exists.
