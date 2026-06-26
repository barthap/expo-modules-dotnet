# NativeAOT Smoke Experiment

This experiment proves that a native macOS executable can load a NativeAOT
shared library with `dlopen`, resolve explicit unmanaged-callable managed
exports with `dlsym`, receive an explicitly owned UTF-8 buffer, and release
that buffer.

This directory is standalone proof code. It is not a dependency of
`Expo.CSharpJsi`, `Expo.ModulesCore`, native bridge packages, expo-desktop
examples, future autolinking packages, or the HostFXR smoke experiment.

## Prerequisites

- macOS arm64
- .NET SDK 10
- CMake
- Xcode command line tools or another C++20-capable compiler

## Build And Run

Run commands from the repository root:

```sh
dotnet publish experiments/nativeaot-smoke/managed/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r osx-arm64
cmake -S experiments/nativeaot-smoke/native -B build/nativeaot-smoke
cmake --build build/nativeaot-smoke --target nativeaot_smoke
./build/nativeaot-smoke/nativeaot_smoke
```

Expected output includes:

```text
Loaded NativeAOT library: <repo>/experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib
Managed payload: expo-csharp-jsi-smoke
Released managed-owned payload buffer
```

## Scope Check

This experiment is intentionally built from its own CMake project:

```sh
cmake -S experiments/nativeaot-smoke/native -B build/nativeaot-smoke
```

Do not add a repository-root `CMakeLists.txt` that references this experiment.

To confirm the experiment does not use HostFXR, forbidden runtime-reflection,
or JSON bridge shortcuts, run:

```sh
rg "hostfxr|nethost|Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" experiments/nativeaot-smoke \
  -g '*.cs' -g '*.cpp' -g '*.csproj' -g 'CMakeLists.txt'
```

No matches are expected.
