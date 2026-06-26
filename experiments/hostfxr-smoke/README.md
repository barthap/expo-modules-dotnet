# HostFXR Smoke Experiment

This experiment proves that a native macOS executable can load a
framework-dependent .NET assembly through HostFXR, call unmanaged-callable
managed entry points, receive an explicitly owned UTF-8 buffer, and release that
buffer.

This directory is standalone proof code. It is not a dependency of
`Expo.CSharpJsi`, `Expo.ModulesCore`, native bridge packages, expo-desktop
examples, or future autolinking packages.

## Prerequisites

- macOS
- .NET SDK 10
- CMake
- Xcode command line tools or another C++20-capable compiler

## Build And Run

Run commands from the repository root:

```sh
dotnet build experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug
cmake -S experiments/hostfxr-smoke/native -B build/hostfxr-smoke
cmake --build build/hostfxr-smoke --target hostfxr_smoke
./build/hostfxr-smoke/hostfxr_smoke
```

Expected output includes:

```text
Loaded HostFXR path: <dotnet-root>/host/fxr/<version>/libhostfxr.dylib
Managed payload: expo-csharp-jsi-smoke
Released managed-owned payload buffer
```

The exact HostFXR patch version may differ if the local .NET installation
changes.

## Scope Check

This experiment is intentionally built from its own CMake project:

```sh
cmake -S experiments/hostfxr-smoke/native -B build/hostfxr-smoke
```

Do not add a repository-root `CMakeLists.txt` that references this experiment.

To confirm the experiment does not use forbidden runtime-reflection or JSON
bridge shortcuts, run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" experiments/hostfxr-smoke \
  -g '*.cs' -g '*.cpp' -g '*.csproj' -g 'CMakeLists.txt'
```

No matches are expected.
