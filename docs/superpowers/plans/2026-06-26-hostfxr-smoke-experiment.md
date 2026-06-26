# HostFXR Smoke Experiment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build only the standalone HostFXR smoke experiment under `experiments/hostfxr-smoke/`.

**Architecture:** This slice proves native macOS code can load a framework-dependent .NET 10 assembly through HostFXR, invoke explicit unmanaged-callable managed entry points, receive a managed-owned UTF-8 buffer, and release that buffer. It does not create real bridge package code, ABI headers, fake JSI, expo-desktop examples, RNW/macOS adapters, autolinking, npm packaging, source generators, or module APIs.

**Tech Stack:** CMake 4.x, C++20, .NET `net10.0`, HostFXR/nethost.

---

## Scope

Create:

- `experiments/hostfxr-smoke/README.md`
- `experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj`
- `experiments/hostfxr-smoke/managed/HostFxrSmoke/EntryPoints.cs`
- `experiments/hostfxr-smoke/native/CMakeLists.txt`
- `experiments/hostfxr-smoke/native/main.cpp`
- `docs/spike-results/2026-06-26-hostfxr-loader-proof.md`

Modify:

- `.gitignore`

Do not create:

- `native/`
- `managed/packages/Expo.CSharpJsi/`
- `managed/packages/Expo.ModulesCore/`
- `examples/`
- `packages/autolinking/`
- fake JSI or fake ABI code

## Task 1: Build Hygiene For The Standalone Experiment

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Write build hygiene files**

Use this `.gitignore` content:

```gitignore
CLAUDE.local.md
AGENTS.local.md

.DS_Store
.idea/
.vscode/

bin/
obj/
TestResults/

build/
cmake-build-*/
CMakeFiles/
CMakeCache.txt
compile_commands.json

*.user
*.suo
*.log
```

- [ ] **Step 2: Confirm no root CMake entry references the experiment**

Run:

```bash
test ! -e CMakeLists.txt
```

Expected: command exits successfully. The experiment must stay standalone and must not be referenced from a repo-root CMake file.

- [ ] **Step 3: Commit build hygiene**

```bash
git add .gitignore
git commit -m "Set up HostFXR smoke build entry"
```

## Task 2: Managed HostFXR Smoke Assembly

**Files:**
- Create: `experiments/hostfxr-smoke/README.md`
- Create: `experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj`
- Create: `experiments/hostfxr-smoke/managed/HostFxrSmoke/EntryPoints.cs`

- [ ] **Step 1: Create experiment README**

Use this content:

```markdown
# HostFXR Smoke Experiment

This experiment proves that a native macOS executable can load a
framework-dependent .NET assembly through HostFXR, call unmanaged-callable
managed entry points, receive an explicitly owned UTF-8 buffer, and release that
buffer.

This directory is standalone proof code. It is not a dependency of
`Expo.CSharpJsi`, `Expo.ModulesCore`, native bridge packages, expo-desktop
examples, or future autolinking packages.
```

- [ ] **Step 2: Add managed project**

Use this `HostFxrSmoke.csproj` content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AssemblyName>HostFxrSmoke</AssemblyName>
    <RootNamespace>HostFxrSmoke</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Add managed entry points**

Use this `EntryPoints.cs` content:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace HostFxrSmoke;

public static unsafe class EntryPoints
{
  private const string Payload = "expo-csharp-jsi-smoke";

  [UnmanagedCallersOnly(EntryPoint = "hostfxr_smoke_get_message", CallConvs = new[] { typeof(CallConvCdecl) })]
  public static nint GetMessage()
  {
    var bytes = Encoding.UTF8.GetBytes(Payload + "\0");
    var buffer = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
    bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
    return (nint)buffer;
  }

  [UnmanagedCallersOnly(EntryPoint = "hostfxr_smoke_release_message", CallConvs = new[] { typeof(CallConvCdecl) })]
  public static void ReleaseMessage(nint message)
  {
    NativeMemory.Free((void*)message);
  }
}
```

- [ ] **Step 4: Build managed assembly**

Run:

```bash
dotnet build experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug
```

Expected: build succeeds and creates `experiments/hostfxr-smoke/managed/HostFxrSmoke/bin/Debug/net10.0/HostFxrSmoke.dll`.

## Task 3: Native HostFXR Loader

**Files:**
- Create: `experiments/hostfxr-smoke/native/CMakeLists.txt`
- Create: `experiments/hostfxr-smoke/native/main.cpp`

- [ ] **Step 1: Add native CMake file**

Use this `CMakeLists.txt` content:

```cmake
cmake_minimum_required(VERSION 3.24)
project(HostFxrSmoke LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(DOTNET_ROOT_DEFAULT "<dotnet-root>")
set(DOTNET_ROOT "$ENV{DOTNET_ROOT}")
if(NOT DOTNET_ROOT)
  set(DOTNET_ROOT "${DOTNET_ROOT_DEFAULT}")
endif()

if(CMAKE_SYSTEM_PROCESSOR MATCHES "arm64|aarch64")
  set(DOTNET_HOST_RID "osx-arm64")
else()
  set(DOTNET_HOST_RID "osx-x64")
endif()

file(GLOB DOTNET_HOST_NATIVE_DIRS
  "${DOTNET_ROOT}/packs/Microsoft.NETCore.App.Host.${DOTNET_HOST_RID}/*/runtimes/${DOTNET_HOST_RID}/native")
list(SORT DOTNET_HOST_NATIVE_DIRS COMPARE NATURAL)
list(POP_BACK DOTNET_HOST_NATIVE_DIRS DOTNET_HOST_NATIVE_DIR)

if(NOT DOTNET_HOST_NATIVE_DIR)
  message(FATAL_ERROR "Could not find Microsoft.NETCore.App.Host native assets for ${DOTNET_HOST_RID}. DOTNET_ROOT=${DOTNET_ROOT}")
endif()

add_executable(hostfxr_smoke main.cpp)
target_include_directories(hostfxr_smoke PRIVATE "${DOTNET_HOST_NATIVE_DIR}")
target_link_libraries(hostfxr_smoke PRIVATE "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib")

add_custom_command(TARGET hostfxr_smoke POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${DOTNET_HOST_NATIVE_DIR}/libnethost.dylib"
    "$<TARGET_FILE_DIR:hostfxr_smoke>/libnethost.dylib")
```

- [ ] **Step 2: Add native loader**

Use this `main.cpp` content:

```cpp
#include <cstddef>

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <dlfcn.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {

hostfxr_initialize_for_runtime_config_fn init_for_config = nullptr;
hostfxr_get_runtime_delegate_fn get_runtime_delegate = nullptr;
hostfxr_close_fn close_hostfxr = nullptr;

std::filesystem::path repo_root_from_current_directory()
{
  auto current = std::filesystem::current_path();
  while (!current.empty()) {
    if (std::filesystem::exists(current / "experiments/hostfxr-smoke")) {
      return current;
    }
    current = current.parent_path();
  }
  throw std::runtime_error("Could not locate repository root from current working directory.");
}

std::filesystem::path find_smoke_assembly()
{
  auto assembly = repo_root_from_current_directory() /
    "experiments/hostfxr-smoke/managed/HostFxrSmoke/bin/Debug/net10.0/HostFxrSmoke.dll";
  if (!std::filesystem::exists(assembly)) {
    throw std::runtime_error("Managed smoke assembly does not exist. Run dotnet build first: " + assembly.string());
  }
  return assembly;
}

void load_hostfxr()
{
  char_t hostfxr_path[4096];
  size_t hostfxr_path_size = sizeof(hostfxr_path) / sizeof(char_t);
  int rc = get_hostfxr_path(hostfxr_path, &hostfxr_path_size, nullptr);
  if (rc != 0) {
    throw std::runtime_error("get_hostfxr_path failed with code " + std::to_string(rc));
  }

  std::cout << "Loaded HostFXR path: " << hostfxr_path << std::endl;

  void *library = dlopen(hostfxr_path, RTLD_LAZY | RTLD_LOCAL);
  if (library == nullptr) {
    throw std::runtime_error(dlerror());
  }

  init_for_config = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
    dlsym(library, "hostfxr_initialize_for_runtime_config"));
  get_runtime_delegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
    dlsym(library, "hostfxr_get_runtime_delegate"));
  close_hostfxr = reinterpret_cast<hostfxr_close_fn>(dlsym(library, "hostfxr_close"));

  if (init_for_config == nullptr || get_runtime_delegate == nullptr || close_hostfxr == nullptr) {
    throw std::runtime_error("Failed to resolve required HostFXR exports.");
  }
}

load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const std::filesystem::path &runtime_config)
{
  hostfxr_handle context = nullptr;
  int rc = init_for_config(runtime_config.c_str(), nullptr, &context);
  if (rc != 0 || context == nullptr) {
    throw std::runtime_error("hostfxr_initialize_for_runtime_config failed with code " + std::to_string(rc));
  }

  void *load_assembly = nullptr;
  rc = get_runtime_delegate(context, hdt_load_assembly_and_get_function_pointer, &load_assembly);
  close_hostfxr(context);

  if (rc != 0 || load_assembly == nullptr) {
    throw std::runtime_error("hostfxr_get_runtime_delegate failed with code " + std::to_string(rc));
  }

  return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(load_assembly);
}

} // namespace

int main()
{
  try {
    auto assembly = find_smoke_assembly();
    auto runtime_config = assembly;
    runtime_config.replace_extension(".runtimeconfig.json");

    load_hostfxr();
    auto load_assembly = get_dotnet_load_assembly(runtime_config);

    using get_message_fn = const char *(CORECLR_DELEGATE_CALLTYPE *)();
    using release_message_fn = void(CORECLR_DELEGATE_CALLTYPE *)(const char *);

    get_message_fn get_message = nullptr;
    release_message_fn release_message = nullptr;

    int rc = load_assembly(
      assembly.c_str(),
      "HostFxrSmoke.EntryPoints, HostFxrSmoke",
      "GetMessage",
      UNMANAGEDCALLERSONLY_METHOD,
      nullptr,
      reinterpret_cast<void **>(&get_message));
    if (rc != 0 || get_message == nullptr) {
      throw std::runtime_error("Failed to resolve managed get_message entry point: " + std::to_string(rc));
    }

    rc = load_assembly(
      assembly.c_str(),
      "HostFxrSmoke.EntryPoints, HostFxrSmoke",
      "ReleaseMessage",
      UNMANAGEDCALLERSONLY_METHOD,
      nullptr,
      reinterpret_cast<void **>(&release_message));
    if (rc != 0 || release_message == nullptr) {
      throw std::runtime_error("Failed to resolve managed release_message entry point: " + std::to_string(rc));
    }

    const char *message = get_message();
    std::cout << "Managed payload: " << message << std::endl;
    release_message(message);
    std::cout << "Released managed-owned payload buffer" << std::endl;
    return 0;
  } catch (const std::exception &error) {
    std::cerr << "hostfxr_smoke failed: " << error.what() << std::endl;
    return 1;
  }
}
```

- [ ] **Step 3: Build and run native smoke executable**

Run:

```bash
cmake -S experiments/hostfxr-smoke/native -B build/hostfxr-smoke
cmake --build build/hostfxr-smoke --target hostfxr_smoke
./build/hostfxr-smoke/hostfxr_smoke
```

Expected output includes:

```text
Loaded HostFXR path:
Managed payload: expo-csharp-jsi-smoke
Released managed-owned payload buffer
```

## Task 4: Result Note And Commit

**Files:**
- Create: `docs/spike-results/2026-06-26-hostfxr-loader-proof.md`

- [ ] **Step 1: Run final verification commands**

Run:

```bash
dotnet --info
dotnet build experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug
cmake -S experiments/hostfxr-smoke/native -B build/hostfxr-smoke
cmake --build build/hostfxr-smoke --target hostfxr_smoke
./build/hostfxr-smoke/hostfxr_smoke
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" experiments/hostfxr-smoke
```

Expected:

- `dotnet --info` prints SDK/runtime information.
- Managed smoke project builds.
- CMake configures.
- Native smoke target builds.
- HostFXR smoke prints the managed payload and release confirmation.
- Forbidden reflection/JSON search returns no matches.

- [ ] **Step 2: Create result note**

Use the required result-note sections and paste exact observed output into the Actual Result section.

```markdown
# Result: HostFXR Loader Proof

Date: 2026-06-26
Machine: macOS local development machine
Repo/path: <repo>
Branch or commit: current branch after implementation commits

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
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" experiments/hostfxr-smoke
```

## Expected Result

The native executable prints the HostFXR path, prints
`expo-csharp-jsi-smoke`, and prints confirmation that the managed-owned buffer
was released. The forbidden reflection/JSON search returns no matches.

## Actual Result

Record the observed command output here during execution.

## Artifacts

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

Go if the actual result matches the expected result. Stop if HostFXR loading or
buffer release fails.

## Follow-Up Questions

- Decide the real JSI upstream for the ABI foundation: expo-desktop,
  React Native macOS, React Native Windows, or a narrow local JSI dependency.
```

- [ ] **Step 3: Commit the HostFXR smoke experiment**

```bash
git add .gitignore experiments/hostfxr-smoke docs/spike-results/2026-06-26-hostfxr-loader-proof.md
git commit -m "Add standalone HostFXR smoke proof"
```

## Final Completion Check

- [ ] Run `git status --short --branch` and confirm only intentional files are changed.
- [ ] Run `dotnet build experiments/hostfxr-smoke/managed/HostFxrSmoke/HostFxrSmoke.csproj -c Debug`.
- [ ] Run `cmake --build build/hostfxr-smoke --target hostfxr_smoke`.
- [ ] Run `./build/hostfxr-smoke/hostfxr_smoke`.
- [ ] Run `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" experiments/hostfxr-smoke`.
- [ ] Confirm no root `CMakeLists.txt`, `native/`, `managed/packages/`, `examples/`, `packages/autolinking/`, fake JSI, or fake ABI code was created.

## Self-Review

- This plan covers only the standalone HostFXR smoke experiment.
- The plan uses `.NET 10` / `net10.0`.
- The plan contains no fake JSI or fake ABI implementation.
- The smoke experiment is isolated under `experiments/hostfxr-smoke/`.
