# Hermes .NET Test Suite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Hermes-backed xUnit test suite for `Expo.JSI` that runs through `dotnet test` and exercises real JSI wrapper behavior.

**Architecture:** Add a native testhost library that includes the production `expo_jsi.h` ABI and extends it with test-only runtime creation, evaluation, teardown, and counters. Managed xUnit tests use a fixture to create real Hermes runtimes, then test direct C# wrapper behavior and narrow JS-facing host-function behavior. Keep module tests temporary and out of the first required slice.

**Tech Stack:** C++20, Hermes JSI, CMake, .NET 10, xUnit, `dotnet test`, shell scripts.

---

## Reference Inputs

- Approved design: `docs/superpowers/specs/2026-06-27-hermes-dotnet-test-suite-design.md`
- Production ABI: `native/include/expo_jsi.h`
- Existing native bridge: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Existing Hermes connector: `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- Existing managed package: `managed/packages/Expo.JSI/`
- Existing smoke workflow: `scripts/run-hermes-experiment.sh`

## Stop Rules

- Do not use git worktrees unless the user explicitly asks.
- Do not commit local absolute paths, usernames, machine names, or machine-specific install paths.
- If error handling is swallowed, untestable, or turns expected JS/managed errors into uncatchable crashes, stop and notify the user. Treat it as a possible bridge architecture flaw, not as a test-fixture problem.
- Keep `Expo.JSI.Tests/Modules/` out of the first required slice. If added later, document that those tests move to `Expo.ModulesCore.Tests` when `Expo.ModulesCore` exists.

## File Structure

Create:

- `native/testhost/include/expo_jsi_testhost.h`
  Test-only C ABI extension. Includes `expo_jsi.h`.
- `native/testhost/src/ExpoJsiTestHost.cpp`
  Creates Hermes runtimes, owns the runtime connector, wraps API counters, evaluates JS, and releases testhost runtimes.
- `native/testhost/CMakeLists.txt`
  Builds `expo_jsi_testhost` as a shared library linked with Hermes and `ExpoJsiBridge.cpp`.
- `managed/packages/Expo.JSI.Tests/AssemblyInfo.cs`
  Disables xUnit parallelization because the first testhost counter wrapper uses a process-global active runtime.
- `managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj`
  xUnit test project referencing `Expo.JSI`.
- `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
  Unsafe P/Invoke layer for `expo_jsi_testhost.h`.
- `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
  Owns native runtime lifecycle and exposes `JavaScriptRuntime`.
- `managed/packages/Expo.JSI.Tests/Fixtures/JavaScriptTestRuntime.cs`
  Test-only wrapper for `JavaScriptRuntime` plus `Evaluate`.
- `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`
  Number, boolean, and string runtime tests.
- `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
  Narrow direct host-function callback-path tests.
- `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs`
  Narrow callback exception propagation tests.
- `scripts/test-jsi.sh`
  Canonical workflow: build managed package, build native testhost, run `dotnet test`.

Modify:

- `native/packages/jsi/include/ExpoJsiBridge.h`
  Add a C++-only helper that wraps an owned `facebook::jsi::Value` into an opaque value handle.
- `native/packages/jsi/src/ExpoJsiBridge.cpp`
  Implement the C++-only owned value helper.
- `managed/packages/Expo.JSI/Expo.JSI.csproj`
  Expose internals to `Expo.JSI.Tests`.
- `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  Add an internal method for tests to materialize an owned value handle returned by the native testhost.
Do not modify:

- `native/include/expo_jsi.h` unless a test exposes a real ABI gap.
- `managed/packages/Expo.JSI/` unless tests expose a real wrapper/API gap.
- `experiments/hermes-console-hostfxr/` except to keep its existing script working if a shared build change requires it.
- `.envrc` in the first implementation. Direnv convenience can be added after
  the test script is proven.

## Task 1: Native Testhost ABI And Library Skeleton

**Files:**
- Modify: `native/packages/jsi/include/ExpoJsiBridge.h`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Create: `native/testhost/include/expo_jsi_testhost.h`
- Create: `native/testhost/src/ExpoJsiTestHost.cpp`
- Create: `native/testhost/CMakeLists.txt`

- [ ] **Step 1: Add the C++-only owned value helper**

Modify `native/packages/jsi/include/ExpoJsiBridge.h`:

```cpp
expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector);
void releaseRuntimeHandle(expo_jsi_runtime_handle runtime);
expo_jsi_value_handle createOwnedValueHandle(facebook::jsi::Value value);
const expo_jsi_api *api();
```

Modify `native/packages/jsi/src/ExpoJsiBridge.cpp` near the existing exported helpers:

```cpp
expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector)
{
  return new RuntimeHandle(connector);
}

void releaseRuntimeHandle(expo_jsi_runtime_handle runtime)
{
  delete runtime;
}

expo_jsi_value_handle createOwnedValueHandle(facebook::jsi::Value value)
{
  return ValueHandle::owned(std::move(value)).release();
}

const expo_jsi_api *api()
{
  return &kApi;
}
```

This helper stays C++-only. Do not add it to `native/include/expo_jsi.h`, and do
not expose raw JSI layouts to C#.

- [ ] **Step 2: Write the testhost header**

Create `native/testhost/include/expo_jsi_testhost.h`:

```c
#pragma once

#include <stdint.h>

#include <expo_jsi.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct expo_jsi_testhost_runtime_t *expo_jsi_testhost_runtime_handle;

typedef struct expo_jsi_testhost_create_result {
  int32_t ok;
  const expo_jsi_api *api;
  expo_jsi_runtime_handle runtime;
  expo_jsi_testhost_runtime_handle testhost_runtime;
  expo_jsi_error error;
} expo_jsi_testhost_create_result;

typedef struct expo_jsi_testhost_counters {
  uint32_t released_values;
  uint32_t released_objects;
  uint32_t released_functions;
  uint32_t released_strings;
} expo_jsi_testhost_counters;

expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void);

expo_jsi_value_result expo_jsi_testhost_evaluate_script(
  expo_jsi_testhost_runtime_handle testhost_runtime,
  const uint8_t *source,
  int32_t source_len,
  const uint8_t *source_url,
  int32_t source_url_len);

expo_jsi_testhost_counters expo_jsi_testhost_get_counters(
  expo_jsi_testhost_runtime_handle testhost_runtime);

void expo_jsi_testhost_reset_counters(expo_jsi_testhost_runtime_handle testhost_runtime);

void expo_jsi_testhost_release_runtime(expo_jsi_testhost_runtime_handle testhost_runtime);

#ifdef __cplusplus
} // extern "C"
#endif
```

- [ ] **Step 3: Write the native implementation**

Create `native/testhost/src/ExpoJsiTestHost.cpp`:

```cpp
#include <expo_jsi_testhost.h>
#include <jsi/jsilib.h>

#include <cstring>
#include <exception>
#include <memory>
#include <string>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"

namespace {

expo_jsi_error makeError(int32_t code, const char *message)
{
  return expo_jsi_error{code, message, static_cast<int32_t>(std::char_traits<char>::length(message))};
}

expo_jsi_value_result makeErrorResult(int32_t code, const char *message)
{
  return expo_jsi_value_result{0, nullptr, makeError(code, message)};
}

struct TestHostRuntime {
  expo::jsi::HermesConsoleRuntimeConnector connector;
  expo_jsi_runtime_handle runtime = nullptr;
  expo_jsi_api counted_api{};
  const expo_jsi_api *inner_api = nullptr;
  expo_jsi_testhost_counters counters{};
};

TestHostRuntime *active_counter_runtime = nullptr;

void counted_release_value(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  if (active_counter_runtime != nullptr && value != nullptr) {
    active_counter_runtime->counters.released_values++;
  }
  active_counter_runtime->inner_api->release_value(runtime, value);
}

void counted_release_object(expo_jsi_runtime_handle runtime, expo_jsi_object_handle object)
{
  if (active_counter_runtime != nullptr && object != nullptr) {
    active_counter_runtime->counters.released_objects++;
  }
  active_counter_runtime->inner_api->release_object(runtime, object);
}

void counted_release_function(expo_jsi_runtime_handle runtime, expo_jsi_function_handle function)
{
  if (active_counter_runtime != nullptr && function != nullptr) {
    active_counter_runtime->counters.released_functions++;
  }
  active_counter_runtime->inner_api->release_function(runtime, function);
}

struct CountedStringReleaseContext {
  expo_jsi_release_string_fn release;
  void *release_context;
};

void counted_release_string(void *release_context)
{
  auto *context = static_cast<CountedStringReleaseContext *>(release_context);
  if (active_counter_runtime != nullptr) {
    active_counter_runtime->counters.released_strings++;
  }
  if (context->release != nullptr) {
    context->release(context->release_context);
  }
  delete context;
}

expo_jsi_string_result counted_get_string(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  auto result = active_counter_runtime->inner_api->get_string(runtime, value);
  if (result.ok == 0 || result.release == nullptr) {
    return result;
  }

  auto *context = new CountedStringReleaseContext{result.release, result.release_context};
  result.release_context = context;
  result.release = counted_release_string;
  return result;
}

const expo_jsi_api *make_counted_api(TestHostRuntime &runtime)
{
  runtime.inner_api = expo::jsi::api();
  runtime.counted_api = *runtime.inner_api;
  runtime.counted_api.release_value = counted_release_value;
  runtime.counted_api.release_object = counted_release_object;
  runtime.counted_api.release_function = counted_release_function;
  runtime.counted_api.get_string = counted_get_string;
  return &runtime.counted_api;
}

} // namespace

extern "C" expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void)
{
  try {
    auto *testhost = new TestHostRuntime();
    testhost->runtime = expo::jsi::createRuntimeHandle(testhost->connector);
    if (testhost->runtime == nullptr) {
      delete testhost;
      return expo_jsi_testhost_create_result{0, nullptr, nullptr, nullptr, makeError(1, "Failed to create runtime handle.")};
    }

    active_counter_runtime = testhost;
    return expo_jsi_testhost_create_result{1, make_counted_api(*testhost), testhost->runtime, testhost, expo_jsi_error{0, nullptr, 0}};
  } catch (const std::exception &ex) {
    return expo_jsi_testhost_create_result{0, nullptr, nullptr, nullptr, makeError(2, ex.what())};
  } catch (...) {
    return expo_jsi_testhost_create_result{0, nullptr, nullptr, nullptr, makeError(3, "Unknown native exception while creating testhost runtime.")};
  }
}

extern "C" expo_jsi_value_result expo_jsi_testhost_evaluate_script(
  expo_jsi_testhost_runtime_handle testhost_runtime,
  const uint8_t *source,
  int32_t source_len,
  const uint8_t *source_url,
  int32_t source_url_len)
{
  auto *testhost = static_cast<TestHostRuntime *>(testhost_runtime);
  if (testhost == nullptr || source == nullptr || source_len < 0) {
    return makeErrorResult(4, "Invalid evaluate_script arguments.");
  }

  try {
    auto &rt = testhost->connector.runtime();
    auto script = std::string(reinterpret_cast<const char *>(source), static_cast<size_t>(source_len));
    auto url = source_url == nullptr || source_url_len <= 0
      ? std::string("expo-jsi-test.js")
      : std::string(reinterpret_cast<const char *>(source_url), static_cast<size_t>(source_url_len));
    auto value = rt.evaluateJavaScript(std::make_unique<facebook::jsi::StringBuffer>(script), url);
    return expo_jsi_value_result{
      1,
      expo::jsi::createOwnedValueHandle(std::move(value)),
      expo_jsi_error{0, nullptr, 0},
    };
  } catch (const facebook::jsi::JSError &ex) {
    return makeErrorResult(5, ex.what());
  } catch (const std::exception &ex) {
    return makeErrorResult(6, ex.what());
  } catch (...) {
    return makeErrorResult(7, "Unknown native exception while evaluating script.");
  }
}

extern "C" expo_jsi_testhost_counters expo_jsi_testhost_get_counters(
  expo_jsi_testhost_runtime_handle testhost_runtime)
{
  auto *testhost = static_cast<TestHostRuntime *>(testhost_runtime);
  return testhost == nullptr ? expo_jsi_testhost_counters{} : testhost->counters;
}

extern "C" void expo_jsi_testhost_reset_counters(expo_jsi_testhost_runtime_handle testhost_runtime)
{
  auto *testhost = static_cast<TestHostRuntime *>(testhost_runtime);
  if (testhost != nullptr) {
    testhost->counters = expo_jsi_testhost_counters{};
  }
}

extern "C" void expo_jsi_testhost_release_runtime(expo_jsi_testhost_runtime_handle testhost_runtime)
{
  auto *testhost = static_cast<TestHostRuntime *>(testhost_runtime);
  if (testhost == nullptr) {
    return;
  }
  if (active_counter_runtime == testhost) {
    active_counter_runtime = nullptr;
  }
  expo::jsi::releaseRuntimeHandle(testhost->runtime);
  testhost->runtime = nullptr;
  testhost->connector.invalidate();
  delete testhost;
}
```

- [ ] **Step 4: Add CMake file**

Create `native/testhost/CMakeLists.txt` by copying the Hermes and .NET-host-pack discovery style from `experiments/hermes-console-hostfxr/native/CMakeLists.txt`, but build a shared library:

```cmake
cmake_minimum_required(VERSION 3.24)
project(ExpoJsiTestHost LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

get_filename_component(REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)

set(HERMES_PREBUILT_ROOT
    "$ENV{HERMES_PREBUILT_ROOT}"
    CACHE PATH "Local Hermes prebuilt root created by scripts/build-hermes-macos.sh")
if(NOT HERMES_PREBUILT_ROOT)
  set(HERMES_PREBUILT_ROOT "${REPO_ROOT}/build/hermes/source/destroot")
endif()

set(HERMES_ROOT "${HERMES_PREBUILT_ROOT}")
set(HERMES_FRAMEWORK_DIR "${HERMES_ROOT}/Library/Frameworks/macosx")

if(NOT EXISTS "${HERMES_ROOT}/include/hermes/hermes.h")
  message(FATAL_ERROR "Could not find Hermes headers under ${HERMES_ROOT}. Run scripts/build-hermes-macos.sh first, or pass -DHERMES_PREBUILT_ROOT=<destroot>.")
endif()
if(NOT EXISTS "${HERMES_FRAMEWORK_DIR}/hermesvm.framework/hermesvm")
  message(FATAL_ERROR "Could not find hermesvm.framework under ${HERMES_FRAMEWORK_DIR}. Run scripts/build-hermes-macos.sh first, or pass -DHERMES_PREBUILT_ROOT=<destroot>.")
endif()

add_library(
  expo_jsi_testhost SHARED
  src/ExpoJsiTestHost.cpp
  "${REPO_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp"
  "${REPO_ROOT}/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp")

target_include_directories(
  expo_jsi_testhost
  PRIVATE "${HERMES_ROOT}/include"
          "${REPO_ROOT}/native/include"
          "${REPO_ROOT}/native/packages/jsi/include"
          "${CMAKE_CURRENT_LIST_DIR}/include")

target_link_options(expo_jsi_testhost PRIVATE "-F${HERMES_FRAMEWORK_DIR}" "-Wl,-rpath,${HERMES_FRAMEWORK_DIR}")
target_link_libraries(expo_jsi_testhost PRIVATE "-framework hermesvm")
```

- [ ] **Step 5: Build to verify the native target**

Run:

```sh
cmake -S native/testhost -B build/jsi-testhost
cmake --build build/jsi-testhost --target expo_jsi_testhost
```

Expected: build succeeds and creates `build/jsi-testhost/libexpo_jsi_testhost.dylib`.

- [ ] **Step 6: Commit native testhost skeleton**

Run:

```sh
git add native/testhost
git add native/packages/jsi/include/ExpoJsiBridge.h native/packages/jsi/src/ExpoJsiBridge.cpp
git commit -m "test: add Hermes JSI native testhost"
```

## Task 2: Canonical Test Script

**Files:**
- Create: `scripts/test-jsi.sh`

- [ ] **Step 1: Create the script**

Create `scripts/test-jsi.sh`:

```bash
#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/build/jsi-testhost"
configuration="${CONFIGURATION:-Debug}"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"
testhost_library="$build_dir/libexpo_jsi_testhost.dylib"

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  cat <<'EOF'
Usage: scripts/test-jsi.sh [dotnet test args...]

Builds the Hermes-backed native JSI testhost and runs Expo.JSI.Tests.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
  exit 0
fi

if [[ ! -d "$hermes_root/include" ]]; then
  cat >&2 <<EOF
Hermes prebuilt was not found at:
  $hermes_root

Run:
  scripts/build-hermes-macos.sh
EOF
  exit 1
fi

echo "==> Building Expo.JSI"
dotnet build "$repo_root/managed/packages/Expo.JSI/Expo.JSI.csproj" -c "$configuration"

echo
echo "==> Configuring native testhost"
cmake -S "$repo_root/native/testhost" -B "$build_dir" -DHERMES_PREBUILT_ROOT="$hermes_root"

echo
echo "==> Building native testhost"
cmake --build "$build_dir" --target expo_jsi_testhost

echo
echo "==> Running Expo.JSI.Tests"
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
  dotnet test "$repo_root/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj" \
  -c "$configuration" \
  "$@"
```

- [ ] **Step 2: Make it executable**

Run:

```sh
chmod +x scripts/test-jsi.sh
```

- [ ] **Step 3: Verify the script reaches the expected missing-test-project failure**

Run:

```sh
scripts/test-jsi.sh
```

Expected before Task 3: native build succeeds, then `dotnet test` fails because `managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj` does not exist.

- [ ] **Step 4: Commit the script**

Run:

```sh
git add scripts/test-jsi.sh
git commit -m "test: add JSI test workflow script"
```

## Task 3: xUnit Project And Native Fixture

**Files:**
- Modify: `managed/packages/Expo.JSI/Expo.JSI.csproj`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Create: `managed/packages/Expo.JSI.Tests/AssemblyInfo.cs`
- Create: `managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj`
- Create: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Create: `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Create: `managed/packages/Expo.JSI.Tests/Fixtures/JavaScriptTestRuntime.cs`

- [ ] **Step 1: Create the test project**

Create `managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Expo.JSI/Expo.JSI.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Expose internals to the test assembly**

Modify `managed/packages/Expo.JSI/Expo.JSI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Expo.JSI.Tests" />
  </ItemGroup>
</Project>
```

If the SDK does not emit the assembly attribute from the item, replace the
`InternalsVisibleTo` item with `managed/packages/Expo.JSI/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Expo.JSI.Tests")]
```

- [ ] **Step 3: Add an internal owned-handle materializer**

Modify `managed/packages/Expo.JSI/JavaScriptRuntime.cs` by adding this method
inside `JavaScriptRuntime`:

```csharp
internal JavaScriptValue FromOwnedValueHandle(ExpoJsiValueHandle valueHandle)
{
  if (valueHandle == 0)
  {
    throw new ArgumentNullException(nameof(valueHandle));
  }

  return JavaScriptValue.FromOwnedHandle(context, valueHandle);
}
```

This is internal and exists so the testhost can return evaluated JS values as
normal owned `JavaScriptValue` wrappers. Do not make it public in this slice.

- [ ] **Step 4: Disable xUnit parallelization**

Create `managed/packages/Expo.JSI.Tests/AssemblyInfo.cs`:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

This is required for the first slice because the native counter wrapper has one
process-global active runtime.

- [ ] **Step 5: Add native P/Invoke layer**

Create `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;
using Expo.JSI;
using Expo.JSI.Interop;

namespace Expo.JSI.Tests.Fixtures;

internal static unsafe partial class NativeTestHost
{
  private const string LibraryEnvVar = "EXPO_JSI_TESTHOST_LIBRARY";
  private static readonly Lazy<nint> LibraryHandle = new(LoadLibrary);

  [StructLayout(LayoutKind.Sequential)]
  internal readonly struct CreateResult
  {
    public readonly int Ok;
    public readonly ExpoJsiApiHandle Api;
    public readonly ExpoJsiRuntimeHandle Runtime;
    public readonly nint TestHostRuntime;
    public readonly ExpoJsiError Error;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal readonly struct Counters
  {
    public readonly uint ReleasedValues;
    public readonly uint ReleasedObjects;
    public readonly uint ReleasedFunctions;
    public readonly uint ReleasedStrings;
  }

  private delegate* unmanaged[Cdecl]<CreateResult> createRuntime;
  private delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, ExpoJsiValueResult> evaluateScript;
  private delegate* unmanaged[Cdecl]<nint, Counters> getCounters;
  private delegate* unmanaged[Cdecl]<nint, void> resetCounters;
  private delegate* unmanaged[Cdecl]<nint, void> releaseRuntime;

  private static bool initialized;

  internal static CreateResult CreateRuntime()
  {
    EnsureLoaded();
    return createRuntime();
  }

  internal static JavaScriptValue Evaluate(JavaScriptRuntime runtime, nint testHostRuntime, string source, string sourceUrl)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(sourceUrl);
    EnsureLoaded();

    var sourceBytes = Encoding.UTF8.GetBytes(source);
    var sourceUrlBytes = Encoding.UTF8.GetBytes(sourceUrl);

    fixed (byte* sourcePtr = sourceBytes)
    fixed (byte* sourceUrlPtr = sourceUrlBytes)
    {
      var result = evaluateScript(testHostRuntime, sourcePtr, sourceBytes.Length, sourceUrlPtr, sourceUrlBytes.Length);
      if (result.Ok == 0 || result.Value == 0)
      {
        ThrowNativeError(result.Error, "Failed to evaluate JavaScript.");
      }
      return runtime.FromOwnedValueHandle(result.Value);
    }
  }

  internal static Counters GetCounters(nint testHostRuntime)
  {
    EnsureLoaded();
    return getCounters(testHostRuntime);
  }

  internal static void ResetCounters(nint testHostRuntime)
  {
    EnsureLoaded();
    resetCounters(testHostRuntime);
  }

  internal static void ReleaseRuntime(nint testHostRuntime)
  {
    EnsureLoaded();
    releaseRuntime(testHostRuntime);
  }

  private static void EnsureLoaded()
  {
    if (initialized)
    {
      return;
    }

    var library = LibraryHandle.Value;
    createRuntime = LoadExport<delegate* unmanaged[Cdecl]<CreateResult>>(library, "expo_jsi_testhost_create_runtime");
    evaluateScript = LoadExport<delegate* unmanaged[Cdecl]<nint, byte*, int, byte*, int, ExpoJsiValueResult>>(library, "expo_jsi_testhost_evaluate_script");
    getCounters = LoadExport<delegate* unmanaged[Cdecl]<nint, Counters>>(library, "expo_jsi_testhost_get_counters");
    resetCounters = LoadExport<delegate* unmanaged[Cdecl]<nint, void>>(library, "expo_jsi_testhost_reset_counters");
    releaseRuntime = LoadExport<delegate* unmanaged[Cdecl]<nint, void>>(library, "expo_jsi_testhost_release_runtime");
    initialized = true;
  }

  private static nint LoadLibrary()
  {
    var path = Environment.GetEnvironmentVariable(LibraryEnvVar);
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new InvalidOperationException($"{LibraryEnvVar} is not set. Run scripts/test-jsi.sh.");
    }
    if (!File.Exists(path))
    {
      throw new FileNotFoundException($"{LibraryEnvVar} points to a missing library.", path);
    }
    return NativeLibrary.Load(path);
  }

  private static T LoadExport<T>(nint library, string name)
    where T : unmanaged
  {
    if (!NativeLibrary.TryGetExport(library, name, out var symbol))
    {
      throw new MissingMethodException($"Native testhost export not found: {name}");
    }
    return (T)symbol;
  }

  private static void ThrowNativeError(ExpoJsiError error, string fallback)
  {
    var message = error.GetMessage();
    throw new InvalidOperationException(string.IsNullOrEmpty(message) ? fallback : message);
  }
}
```

- [ ] **Step 6: Add fixture classes**

Create `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;

namespace Expo.JSI.Tests.Fixtures;

public sealed class HermesRuntimeFixture : IDisposable
{
  private nint testHostRuntime;

  private HermesRuntimeFixture(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    Runtime = runtime;
    this.testHostRuntime = testHostRuntime;
    TestRuntime = new JavaScriptTestRuntime(runtime, testHostRuntime);
  }

  public JavaScriptRuntime Runtime { get; }

  public JavaScriptTestRuntime TestRuntime { get; }

  public static HermesRuntimeFixture Create()
  {
    var result = NativeTestHost.CreateRuntime();
    if (result.Ok == 0 || result.Api == 0 || result.Runtime == 0 || result.TestHostRuntime == 0)
    {
      var message = result.Error.GetMessage();
      throw new InvalidOperationException(string.IsNullOrEmpty(message) ? "Failed to create Hermes test runtime." : message);
    }

    var runtime = JavaScriptRuntime.FromNative(result.Api, result.Runtime);
    return new HermesRuntimeFixture(runtime, result.TestHostRuntime);
  }

  public NativeTestHost.Counters Counters => NativeTestHost.GetCounters(testHostRuntime);

  public void ResetCounters() => NativeTestHost.ResetCounters(testHostRuntime);

  public JavaScriptValue Evaluate(string source, string sourceUrl = "expo-jsi-test.js")
  {
    return TestRuntime.Evaluate(source, sourceUrl);
  }

  public void Dispose()
  {
    if (testHostRuntime != 0)
    {
      NativeTestHost.ReleaseRuntime(testHostRuntime);
      testHostRuntime = 0;
    }
  }
}
```

Create `managed/packages/Expo.JSI.Tests/Fixtures/JavaScriptTestRuntime.cs`:

```csharp
namespace Expo.JSI.Tests.Fixtures;

public sealed class JavaScriptTestRuntime
{
  private readonly JavaScriptRuntime runtime;
  private readonly nint testHostRuntime;

  internal JavaScriptTestRuntime(JavaScriptRuntime runtime, nint testHostRuntime)
  {
    this.runtime = runtime;
    this.testHostRuntime = testHostRuntime;
  }

  public JavaScriptValue Evaluate(string source, string sourceUrl = "expo-jsi-test.js")
  {
    return NativeTestHost.Evaluate(runtime, testHostRuntime, source, sourceUrl);
  }
}
```

- [ ] **Step 7: Run compile check**

Run:

```sh
scripts/test-jsi.sh --no-restore
```

Expected after this task but before behavior tests: the project compiles and
`dotnet test` reports no tests or an empty test run, depending on xUnit runner
behavior. If the compile fails because `InternalsVisibleTo` did not apply, use
the `AssemblyInfo.cs` fallback from Step 2.

- [ ] **Step 8: Commit fixture foundation**

Run:

```sh
git add managed/packages/Expo.JSI managed/packages/Expo.JSI.Tests
git commit -m "test: add Expo.JSI xUnit fixture"
```

## Task 4: Primitive Runtime Tests

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`

- [ ] **Step 1: Write primitive tests**

Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptPrimitiveTests
{
  [Fact]
  public void CreateNumberRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateNumber(42.5);

    Assert.Equal(JavaScriptValueKind.Number, value.Kind);
    Assert.Equal(42.5, value.AsDouble());
  }

  [Fact]
  public void CreateBoolTrueRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateBool(true);

    Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
    Assert.True(value.AsBool());
  }

  [Fact]
  public void CreateBoolFalseRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateBool(false);

    Assert.Equal(JavaScriptValueKind.Bool, value.Kind);
    Assert.False(value.AsBool());
  }

  [Fact]
  public void CreateAsciiStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("hello");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("hello", value.AsString());
  }

  [Fact]
  public void CreateNonAsciiStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("Zoë");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("Zoë", value.AsString());
  }

  [Fact]
  public void CreateEmbeddedNulStringRoundTrips()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Runtime.CreateString("a\0b");

    Assert.Equal(JavaScriptValueKind.String, value.Kind);
    Assert.Equal("a\0b", value.AsString());
  }
}
```

- [ ] **Step 2: Run tests and verify failures are meaningful**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptPrimitiveTests
```

Expected: tests pass. If they fail due to native setup or string release errors, fix the fixture/testhost, not the tests.

- [ ] **Step 3: Commit primitive tests**

Run:

```sh
git add managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs
git commit -m "test: cover JSI primitive wrappers"
```

## Task 5: Evaluate Helper And Host Function Success Path

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`

- [ ] **Step 1: Write host-function success test**

Create `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionTests
{
  [Fact]
  public void HostFunctionReceivesBorrowedArgumentAndReturnsOwnedValue()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "addOne",
        1,
        (runtime, thisValue, arguments, context) =>
        {
          Assert.Equal(1u, arguments.Count);
          var input = arguments.GetBorrowedValue(0);
          Assert.Equal(JavaScriptValueKind.Number, input.Kind);
          return runtime.CreateNumber(input.AsDouble() + 1);
        },
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("addOne", functionValue);

    using var result = fixture.Evaluate("globalThis.addOne(41.5)", "host-function-success.js");

    Assert.Equal(JavaScriptValueKind.Number, result.Kind);
    Assert.Equal(42.5, result.AsDouble());
  }
}
```

- [ ] **Step 2: Run host-function success test**

Run:

```sh
scripts/test-jsi.sh --filter HostFunctionReceivesBorrowedArgumentAndReturnsOwnedValue
```

Expected: PASS.

- [ ] **Step 3: Commit host-function success test**

Run:

```sh
git add managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs
git commit -m "test: cover direct JSI host function callback"
```

The native evaluated-value helper is committed in Task 1. This task commits only
the host-function behavior test.

## Task 6: Host Function Error Propagation

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs`

- [ ] **Step 1: Write error propagation test**

Create `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.HostFunctions;

public sealed class HostFunctionErrorTests
{
  [Fact]
  public void HostFunctionManagedExceptionIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "throwFromManaged",
        0,
        static (runtime, thisValue, arguments, context) =>
        {
          throw new InvalidOperationException("managed boom");
        },
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("throwFromManaged", functionValue);

    using var result = fixture.Evaluate(
        "try { globalThis.throwFromManaged(); 'no error'; } catch (e) { e.message; }",
        "host-function-error.js"
    );

    Assert.Equal(JavaScriptValueKind.String, result.Kind);
    Assert.Contains("managed boom", result.AsString());
  }
}
```

- [ ] **Step 2: Run the error propagation test**

Run:

```sh
scripts/test-jsi.sh --filter HostFunctionManagedExceptionIsCatchableInJavaScript
```

Expected: PASS. If this crashes the process, returns `"no error"`, or loses the message, stop and notify the user before changing architecture.

- [ ] **Step 3: Commit error propagation test**

Run:

```sh
git add managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs
git commit -m "test: cover host function error propagation"
```

Do not change production error handling in this task. If the test fails by
crashing, returning `"no error"`, or losing `"managed boom"`, stop and notify
the user with the exact failure.

## Task 7: Ownership Counter Checks

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`

- [ ] **Step 1: Add scoped release-counter test**

Append to `JavaScriptPrimitiveTests`:

```csharp
[Fact]
public void DisposingOwnedValueIncrementsReleaseCounter()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.ResetCounters();

  using (fixture.Runtime.CreateNumber(1))
  {
  }

  var counters = fixture.Counters;
  Assert.True(counters.ReleasedValues >= 1);
}
```

- [ ] **Step 2: Add evaluated-result release-counter test**

Append to `HostFunctionTests`:

```csharp
[Fact]
public void DisposingEvaluatedResultIncrementsReleaseCounter()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.ResetCounters();

  using (fixture.Evaluate("21 + 21", "counter-evaluate.js"))
  {
  }

  var counters = fixture.Counters;
  Assert.True(counters.ReleasedValues >= 1);
}
```

- [ ] **Step 3: Run ownership tests**

Run:

```sh
scripts/test-jsi.sh --filter "DisposingOwnedValueIncrementsReleaseCounter|DisposingEvaluatedResultIncrementsReleaseCounter"
```

Expected: PASS.

- [ ] **Step 4: Commit counter tests**

Run:

```sh
git add managed/packages/Expo.JSI.Tests native/testhost/src/ExpoJsiTestHost.cpp
git commit -m "test: cover JSI owned handle release counters"
```

## Task 8: Final Verification And Documentation Touches

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/README.md`

- [ ] **Step 1: Add short test README**

Create `managed/packages/Expo.JSI.Tests/README.md`:

````md
# Expo.JSI.Tests

Run the Hermes-backed test suite with:

```sh
scripts/test-jsi.sh
```

The script builds the native Hermes testhost and passes
`EXPO_JSI_TESTHOST_LIBRARY` to `dotnet test`.

Add low-level runtime, value, string, ownership, and host-function tests here.
Module behavior tests are temporary here until `Expo.ModulesCore` exists; move
them to `Expo.ModulesCore.Tests` when that package is added.
```
````

- [ ] **Step 2: Run canonical test suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: PASS.

- [ ] **Step 3: Run existing smoke proof**

Run:

```sh
scripts/run-hermes-experiment.sh
```

Expected: PASS and output includes `hermes console hostfxr proof: ok`.

- [ ] **Step 4: Run formatting check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: PASS. If it fails due to formatting, run `scripts/format.sh`, then rerun `scripts/format.sh --check --all`.

- [ ] **Step 5: Search for forbidden committed local paths**

Run:

```sh
python3 - <<'PY'
from pathlib import Path

patterns = ["/" + "Users" + "/", "~" + "/"]
roots = [
    Path("native"),
    Path("managed"),
    Path("scripts"),
    Path("docs/superpowers/plans/2026-06-27-hermes-dotnet-test-suite.md"),
]

matches = []
for root in roots:
    files = [root] if root.is_file() else [p for p in root.rglob("*") if p.is_file()]
    for path in files:
        text = path.read_text(errors="ignore")
        for line_number, line in enumerate(text.splitlines(), 1):
            if any(pattern in line for pattern in patterns):
                matches.append(f"{path}:{line_number}:{line}")

if matches:
    print("\n".join(matches))
    raise SystemExit(1)
PY
```

Expected: no matches. If matches appear in files intended for commit, replace
them with repo-relative placeholders.

- [ ] **Step 6: Commit final docs**

Run:

```sh
git add managed/packages/Expo.JSI.Tests/README.md
git commit -m "docs: explain Hermes JSI tests"
```

## Final Completion Criteria

- `scripts/test-jsi.sh` passes.
- `scripts/run-hermes-experiment.sh` passes.
- `scripts/format.sh --check --all` passes.
- The first slice includes primitive number/bool/string tests.
- The first slice includes direct host-function success and error tests.
- The native testhost includes `expo_jsi.h` and exposes only test-specific extensions.
- No committed local absolute paths or machine-specific paths were introduced.
