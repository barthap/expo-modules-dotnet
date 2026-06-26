# Result: HostFXR Loader Proof

Date: 2026-06-26
Machine: macOS local development machine
Repo/path: <repo>
Branch or commit: main, final implementation commit containing this note

## Question

Can native macOS code load a framework-dependent .NET 10 assembly through
HostFXR, resolve unmanaged-callable managed entry points, receive a
managed-owned UTF-8 buffer, and release that buffer explicitly?

## Commands Run

```sh
dotnet --info
dotnet build experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug
cmake -S experiments/hostfxr-smoke/native -B build/hostfxr-smoke
cmake --build build/hostfxr-smoke --target hostfxr_smoke
./build/hostfxr-smoke/hostfxr_smoke
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" experiments/hostfxr-smoke
```

## Expected Result

The native executable prints the HostFXR path, prints
`expo-csharp-jsi-smoke`, and prints confirmation that the managed-owned buffer
was released. The forbidden reflection/JSON search returns no matches.

## Actual Result

`dotnet --info`:

```text
.NET SDK:
 Version:           10.0.201
 Commit:            4d3023de60
 Workload version:  10.0.200-manifests.0793c108
 MSBuild version:   18.3.0-release-26153-122+4d3023de6

Runtime Environment:
 OS Name:     macOS
 OS Platform: Darwin
 RID:         osx-arm64
 Base Path:   <dotnet-root>/sdk/10.0.201/

.NET workloads installed:
There are no installed workloads to display.
Configured to use workload sets when installing new manifests.
No workload sets are installed. Run "dotnet workload restore" to install a workload set.

Host:
  Version:      10.0.5
  Architecture: arm64
  Commit:       a612c2a105

.NET SDKs installed:
  8.0.419 [<dotnet-root>/sdk]
  10.0.201 [<dotnet-root>/sdk]

.NET runtimes installed:
  Microsoft.AspNetCore.App 8.0.25 [<dotnet-root>/shared/Microsoft.AspNetCore.App]
  Microsoft.AspNetCore.App 10.0.5 [<dotnet-root>/shared/Microsoft.AspNetCore.App]
  Microsoft.NETCore.App 8.0.25 [<dotnet-root>/shared/Microsoft.NETCore.App]
  Microsoft.NETCore.App 10.0.5 [<dotnet-root>/shared/Microsoft.NETCore.App]

Other architectures found:
  None

Environment variables:
  Not set

global.json file:
  Not found

Learn more:
  https://aka.ms/dotnet/info

Download .NET:
  https://aka.ms/dotnet/download
```

Managed build:

```text
  Determining projects to restore...
  All projects are up-to-date for restore.
  HostFxrSmoke -> <repo>/experiments/hostfxr-smoke/managed/HostFxrSmoke/bin/Debug/net10.0/HostFxrSmoke.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.42
```

CMake configure:

```text
-- Configuring done (0.0s)
-- Generating done (0.0s)
-- Build files have been written to: <repo>/build/hostfxr-smoke
```

Native build:

```text
[100%] Built target hostfxr_smoke
```

HostFXR smoke run:

```text
Loaded HostFXR path: <dotnet-root>/host/fxr/<version>/libhostfxr.dylib
Managed payload: expo-csharp-jsi-smoke
Released managed-owned payload buffer
```

Forbidden reflection/JSON search:

```text
No matches. `rg` exited with code 1, which is expected for no matches.
```

During implementation, the first native compile attempted to use lexicographic
host-pack sorting and selected the .NET 8 host pack even though the managed
assembly targets .NET 10. The experiment now uses natural sorting and loads
HostFXR 10.0.5. The class library also needed
`GenerateRuntimeConfigurationFiles=true` so HostFXR has a runtimeconfig file to
initialize from.

## Artifacts

- `.gitignore`
- `experiments/hostfxr-smoke/README.md`
- `experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj`
- `experiments/hostfxr-smoke/managed/HostFxrSmoke/EntryPoints.cs`
- `experiments/hostfxr-smoke/native/CMakeLists.txt`
- `experiments/hostfxr-smoke/native/main.cpp`

## Ownership And Lifetime Findings

The managed entry point allocates the UTF-8 buffer with `NativeMemory.Alloc`.
The native executable treats the returned pointer as managed-owned and calls
`hostfxr_smoke_release_message` exactly once after reading it. The native side
does not free the buffer directly.

## Platform Findings

The proof is macOS-local and uses HostFXR/nethost. It does not use Windows
loader APIs, RNW, WinUI, AppKit, expo-desktop, or real JSI.

## Scheduler Findings

No JS scheduler is involved in this loader proof. The managed entry point is a
synchronous unmanaged-callable function and does not touch JSI.

## Reflection/AOT Findings

The native executable resolves explicit unmanaged-callable entry points. It
does not use `Assembly.GetTypes`, `MethodInfo.Invoke`, `Delegate.DynamicInvoke`,
`object?[]`, or JSON conversion.

## Decision

Go. The HostFXR smoke proof loads .NET 10, invokes the managed entry point, and
releases the managed-owned buffer explicitly.

## Follow-Up Questions

- Decide the real JSI upstream for the ABI foundation: expo-desktop,
  React Native macOS, React Native Windows, or a narrow local JSI dependency.
