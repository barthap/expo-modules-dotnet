# pnpm Public Adapter Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the repo into a pnpm workspace with `expo-modules-dotnet` as the public Expo adapter package and `example-module` as an authored .NET module package consumed by `apps/mobile-app`.

**Architecture:** Move reusable managed and native bridge code under `packages/expo-modules-dotnet`, because that package is the future public Expo adapter. Move app proofs under `apps/`, keep smoke proofs under `experiments/`, and stage the example NativeAOT library into adapter-owned native library locations until .NET module autolinking exists.

**Tech Stack:** pnpm workspace, Expo Modules, React Native TurboModules/codegen, CMake, CocoaPods podspecs, Kotlin, Objective-C++, C++20, .NET 10 NativeAOT, Hermes-backed managed tests.

---

## Current Context

- Current branch: `codex/pnpm-public-adapter-package`.
- Approved delta spec: `docs/changes/2026-07-01-pnpm-public-adapter-package/spec.md`.
- There may be pre-existing user edits in `docs/roadmap.md`. Preserve them. If implementation updates that file, stage only the intended path-reference changes.
- Do not use git worktrees.
- Do not publish packages, push branches, open PRs, or post GitHub comments.
- Before any commit, scan staged content for local machine paths and usernames.

## File Structure After Implementation

- Create `package.json`: root private workspace metadata and scripts.
- Create `pnpm-workspace.yaml`: workspace members `apps/*` and `packages/*`, with space for future catalogs.
- Move `experiments/mobile-app/` to `apps/mobile-app/`: example Expo app.
- Move `experiments/hermes-console-app/` to `apps/hermes-console-app/`: headless Hermes integration app.
- Keep `experiments/hostfxr-smoke/` and `experiments/nativeaot-smoke/`.
- Move `experiments/mobile-app/modules/expo-csharp-v2/` to `packages/expo-modules-dotnet/`: public adapter package root.
- Move `managed/` to `packages/expo-modules-dotnet/managed/`.
- Move `native/` to `packages/expo-modules-dotnet/native/`, including `native/testhost`.
- Move `experiments/mobile-app/dotnet/ExpoMobileV2Module/` to `packages/example-module/dotnet/ExampleModule/`.
- Move `experiments/mobile-app/scripts/build-dotnet-module.sh` to `packages/example-module/scripts/build-nativeaot.sh`.

## Task 1: Workspace Metadata And Mechanical Moves

**Files:**
- Create: `package.json`
- Create: `pnpm-workspace.yaml`
- Move: `experiments/mobile-app/` to `apps/mobile-app/`
- Move: `experiments/hermes-console-app/` to `apps/hermes-console-app/`
- Move: `apps/mobile-app/modules/expo-csharp-v2/` to `packages/expo-modules-dotnet/`
- Move: `managed/` to `packages/expo-modules-dotnet/managed/`
- Move: `native/` to `packages/expo-modules-dotnet/native/`
- Move: `apps/mobile-app/dotnet/ExpoMobileV2Module/` to `packages/example-module/dotnet/ExampleModule/`
- Move: `apps/mobile-app/scripts/build-dotnet-module.sh` to `packages/example-module/scripts/build-nativeaot.sh`

- [ ] **Step 1: Create workspace files**

Create `package.json`:

```json
{
  "name": "expo-modules-csharp",
  "private": true,
  "packageManager": "pnpm@11.7.0",
  "scripts": {
    "mobile:start": "pnpm --filter mobile-app start",
    "example-module:build-nativeaot": "pnpm --filter example-module build:nativeaot"
  }
}
```

Create `pnpm-workspace.yaml`:

```yaml
packages:
  - "apps/*"
  - "packages/*"

catalogs: {}
```

- [ ] **Step 2: Move app directories**

Run:

```sh
mkdir -p apps packages
git mv experiments/mobile-app apps/mobile-app
git mv experiments/hermes-console-app apps/hermes-console-app
```

Expected: `git status --short` shows renames from `experiments/mobile-app` and `experiments/hermes-console-app` to `apps/`.

- [ ] **Step 3: Move adapter package root**

Run:

```sh
git mv apps/mobile-app/modules/expo-csharp-v2 packages/expo-modules-dotnet
rmdir apps/mobile-app/modules
```

Expected: `packages/expo-modules-dotnet/package.json` exists and still contains the old `expo-csharp-v2` name before Task 2 renames it.

- [ ] **Step 4: Move managed and native core under the adapter package**

Run:

```sh
git mv managed packages/expo-modules-dotnet/managed
git mv native packages/expo-modules-dotnet/native
```

Expected:

```sh
test -f packages/expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj
test -f packages/expo-modules-dotnet/native/include/expo_jsi.h
test -f packages/expo-modules-dotnet/native/testhost/CMakeLists.txt
```

- [ ] **Step 5: Move example module source and build script**

Run:

```sh
mkdir -p packages/example-module/dotnet packages/example-module/scripts
git mv apps/mobile-app/dotnet/ExpoMobileV2Module packages/example-module/dotnet/ExampleModule
git mv apps/mobile-app/scripts/build-dotnet-module.sh packages/example-module/scripts/build-nativeaot.sh
rmdir apps/mobile-app/dotnet apps/mobile-app/scripts
```

Expected:

```sh
test -f packages/example-module/dotnet/ExampleModule/ExpoMobileV2Module.csproj
test -f packages/example-module/scripts/build-nativeaot.sh
```

- [ ] **Step 6: Verify moved structure**

Run:

```sh
find apps packages experiments -maxdepth 3 -type d | sort | sed -n '1,120p'
```

Expected: output includes `apps/mobile-app`, `apps/hermes-console-app`, `packages/expo-modules-dotnet`, `packages/example-module`, `experiments/hostfxr-smoke`, and `experiments/nativeaot-smoke`.

- [ ] **Step 7: Commit mechanical moves**

Run:

```sh
git diff --check
git diff --cached --check
git add package.json pnpm-workspace.yaml apps packages experiments
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "chore: introduce workspace package layout"
```

Expected: commit succeeds. If `docs/roadmap.md` was modified before this task, it remains unstaged unless this task intentionally changed it.

## Task 2: Rename The Public Adapter Package

**Files:**
- Modify: `packages/expo-modules-dotnet/package.json`
- Modify: `packages/expo-modules-dotnet/expo-module.config.json`
- Modify: `packages/expo-modules-dotnet/react-native.config.js`
- Rename: `packages/expo-modules-dotnet/ExpoCSharpV2.podspec` to `packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`
- Rename: `packages/expo-modules-dotnet/src/NativeExpoCSharpV2Installer.ts` to `packages/expo-modules-dotnet/src/NativeExpoModulesDotnetInstaller.ts`
- Rename: `packages/expo-modules-dotnet/android/src/main/java/expo/modules/csharpv2/` to `packages/expo-modules-dotnet/android/src/main/java/expo/modules/dotnet/`
- Rename: `packages/expo-modules-dotnet/android/src/main/cpp/ExpoCSharpV2BindingsInstaller.cpp` to `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`
- Rename: `packages/expo-modules-dotnet/ios/ExpoCSharpV2Installer.mm` to `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- Rename: `packages/expo-modules-dotnet/ios/ExpoCSharpV2Module.swift` to `packages/expo-modules-dotnet/ios/ExpoModulesDotnetModule.swift`
- Modify: `packages/expo-modules-dotnet/android/build.gradle`
- Modify: `packages/expo-modules-dotnet/android/src/main/cpp/CMakeLists.txt`

- [ ] **Step 1: Rename files and Java/Kotlin directory**

Run:

```sh
git mv packages/expo-modules-dotnet/ExpoCSharpV2.podspec packages/expo-modules-dotnet/ExpoModulesDotnet.podspec
git mv packages/expo-modules-dotnet/src/NativeExpoCSharpV2Installer.ts packages/expo-modules-dotnet/src/NativeExpoModulesDotnetInstaller.ts
mkdir -p packages/expo-modules-dotnet/android/src/main/java/expo/modules/dotnet
git mv packages/expo-modules-dotnet/android/src/main/java/expo/modules/csharpv2/*.kt packages/expo-modules-dotnet/android/src/main/java/expo/modules/dotnet/
rmdir packages/expo-modules-dotnet/android/src/main/java/expo/modules/csharpv2
git mv packages/expo-modules-dotnet/android/src/main/cpp/ExpoCSharpV2BindingsInstaller.cpp packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp
git mv packages/expo-modules-dotnet/ios/ExpoCSharpV2Installer.mm packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm
git mv packages/expo-modules-dotnet/ios/ExpoCSharpV2Module.swift packages/expo-modules-dotnet/ios/ExpoModulesDotnetModule.swift
```

Expected: old filenames no longer exist.

- [ ] **Step 2: Update adapter package metadata**

Replace `packages/expo-modules-dotnet/package.json` with:

```json
{
  "name": "expo-modules-dotnet",
  "version": "0.1.0",
  "description": "Expo adapter for .NET-backed Expo modules",
  "main": "src/index.ts",
  "types": "src/index.ts",
  "license": "MIT",
  "peerDependencies": {
    "expo": "*",
    "react": "*",
    "react-native": "*"
  },
  "codegenConfig": {
    "name": "ExpoModulesDotnetSpec",
    "type": "modules",
    "jsSrcsDir": "./src",
    "android": {
      "javaPackageName": "expo.modules.dotnet"
    },
    "ios": {
      "modules": {
        "ExpoModulesDotnetInstaller": {
          "className": "ExpoModulesDotnetInstaller"
        }
      }
    }
  }
}
```

Replace `packages/expo-modules-dotnet/expo-module.config.json` with:

```json
{
  "platforms": ["apple", "android"],
  "apple": {
    "podspecPath": "./ExpoModulesDotnet.podspec",
    "modules": ["ExpoModulesDotnetModule"]
  },
  "android": {
    "modules": ["expo.modules.dotnet.ExpoModulesDotnetModule"]
  }
}
```

Replace `packages/expo-modules-dotnet/react-native.config.js` with:

```js
module.exports = {
  dependency: {
    platforms: {
      android: {
        sourceDir: './android',
        packageImportPath: 'import expo.modules.dotnet.ExpoModulesDotnetTurboPackage;',
        packageInstance: 'new ExpoModulesDotnetTurboPackage()',
      },
      ios: {},
    },
  },
};
```

- [ ] **Step 3: Rename Kotlin classes and native library loading**

In `packages/expo-modules-dotnet/android/src/main/java/expo/modules/dotnet/ExpoCSharpV2Module.kt`, rename the file to `ExpoModulesDotnetModule.kt` and replace contents with:

```kotlin
package expo.modules.dotnet

import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition

class ExpoModulesDotnetModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("ExpoModulesDotnet")
  }
}
```

In `ExpoCSharpV2TurboModule.kt`, rename the file to `ExpoModulesDotnetTurboModule.kt` and replace contents with:

```kotlin
package expo.modules.dotnet

import com.facebook.proguard.annotations.DoNotStrip
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.module.annotations.ReactModule
import com.facebook.react.turbomodule.core.interfaces.BindingsInstallerHolder
import com.facebook.react.turbomodule.core.interfaces.TurboModule
import com.facebook.react.turbomodule.core.interfaces.TurboModuleWithJSIBindings
import com.facebook.soloader.SoLoader

@DoNotStrip
@ReactModule(name = ExpoModulesDotnetTurboModule.NAME)
class ExpoModulesDotnetTurboModule(reactContext: ReactApplicationContext) :
  ReactContextBaseJavaModule(reactContext),
  TurboModule,
  TurboModuleWithJSIBindings {
  override fun getName() = NAME

  @DoNotStrip
  external override fun getBindingsInstaller(): BindingsInstallerHolder

  companion object {
    const val NAME = "ExpoModulesDotnetInstaller"

    init {
      SoLoader.loadLibrary("ExampleModule")
      SoLoader.loadLibrary("expo-modules-dotnet")
    }
  }
}
```

In `ExpoCSharpV2TurboPackage.kt`, rename the file to `ExpoModulesDotnetTurboPackage.kt` and replace `ExpoCSharpV2` with `ExpoModulesDotnet`, keeping the same `BaseReactPackage` structure.

- [ ] **Step 4: Rename C++ installer identifiers**

In `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`:

- Replace log tag `ExpoCSharpV2` with `ExpoModulesDotnet`.
- Replace `expo_mobile_v2_register_modules` with `example_module_register_modules`.
- Replace `libExpoMobileV2Module.so` with `libExampleModule.so`.
- Replace namespace `expo::modules::csharpv2` with `expo::modules::dotnet`.
- Replace class `ExpoCSharpV2BindingsInstaller` with `ExpoModulesDotnetBindingsInstaller`.
- Replace Java descriptor with:

```cpp
static constexpr auto kJavaDescriptor = "Lexpo/modules/dotnet/ExpoModulesDotnetTurboModule;";
```

- Update log messages to say `ExampleModule.add` instead of `ExpoCSharpV2.add`.

- [ ] **Step 5: Rename iOS installer identifiers**

In `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`:

- Replace `expo_mobile_v2_register_modules` with `example_module_register_modules`.
- Replace `ExpoCSharpV2` log prefixes with `ExpoModulesDotnet`.
- Replace `ExpoCSharpV2InstallerTurboModule` with `ExpoModulesDotnetInstallerTurboModule`.
- Replace Objective-C interface and implementation `ExpoCSharpV2Installer` with `ExpoModulesDotnetInstaller`.
- Update log messages to say `ExampleModule.add`.

In `packages/expo-modules-dotnet/ios/ExpoModulesDotnetModule.swift`, replace contents with:

```swift
import ExpoModulesCore

public class ExpoModulesDotnetModule: Module {
  public func definition() -> ModuleDefinition {
    Name("ExpoModulesDotnet")
  }
}
```

- [ ] **Step 6: Update adapter native build files**

In `packages/expo-modules-dotnet/android/build.gradle`:

- Set `group = 'expo.modules.dotnet'`.
- Set `version = '0.1.0'`.
- Set `namespace "expo.modules.dotnet"`.
- Set `versionName "0.1.0"`.
- Replace the CMake argument with:

```gradle
"-DADAPTER_ROOT=${project.projectDir}/.."
```

In `packages/expo-modules-dotnet/android/src/main/cpp/CMakeLists.txt`:

- Set `project(expo-modules-dotnet)`.
- Replace `REPO_ROOT` with `ADAPTER_ROOT`.
- Set `REACT_NATIVE_DIR` to `${ADAPTER_ROOT}/../../apps/mobile-app/node_modules/react-native` for this slice.
- Rename the shared library target from `expo-csharp-v2` to `expo-modules-dotnet`.
- Reference bridge source files through `${ADAPTER_ROOT}/native/packages/jsi/src/...`.
- Reference include directories through `${ADAPTER_ROOT}/native/include` and `${ADAPTER_ROOT}/native/packages/jsi/include`.

In `packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`:

- Set `s.name = 'ExpoModulesDotnet'`.
- Set `s.version = '0.1.0'`.
- Set summary and description to `Expo adapter for .NET-backed Expo modules`.
- Set `s.vendored_libraries = 'ios/NativeLibs/libExampleModule.dylib'`.
- Set header search paths to:

```ruby
'$(PODS_TARGET_SRCROOT)/native/include',
'$(PODS_TARGET_SRCROOT)/native/packages/jsi/include'
```

- [ ] **Step 7: Verify adapter rename has no stale package identifiers**

Run:

```sh
rg -n "expo-csharp-v2|ExpoCSharpV2|csharpv2|ExpoMobileV2Module|expo_mobile_v2_register_modules|libExpoMobileV2Module" packages/expo-modules-dotnet
```

Expected: no matches, except comments intentionally preserved with explanation. Prefer no matches.

- [ ] **Step 8: Commit adapter rename**

Run:

```sh
git diff --check
git add packages/expo-modules-dotnet
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "refactor: rename dotnet adapter package"
```

Expected: commit succeeds.

## Task 3: Convert The Authored Example Module Package

**Files:**
- Create: `packages/example-module/package.json`
- Rename: `packages/example-module/dotnet/ExampleModule/ExpoMobileV2Module.csproj` to `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj`
- Modify: `packages/example-module/dotnet/ExampleModule/EntryPoints.cs`
- Modify: `packages/example-module/dotnet/ExampleModule/MobileV2MathModule.cs`
- Modify: `packages/example-module/scripts/build-nativeaot.sh`

- [ ] **Step 1: Add example module package metadata**

Create `packages/example-module/package.json`:

```json
{
  "name": "example-module",
  "version": "0.1.0",
  "private": true,
  "description": "Example authored .NET Expo module",
  "scripts": {
    "build:nativeaot": "./scripts/build-nativeaot.sh"
  },
  "peerDependencies": {
    "expo-modules-dotnet": "*"
  }
}
```

- [ ] **Step 2: Rename and update the C# project**

Run:

```sh
git mv packages/example-module/dotnet/ExampleModule/ExpoMobileV2Module.csproj packages/example-module/dotnet/ExampleModule/ExampleModule.csproj
```

Replace `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" />
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
    <ProjectReference
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
      Condition="'$(PublishAot)' != 'true'"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false" />
    <Analyzer
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/bin/Debug/netstandard2.0/Expo.ModulesCore.Generator.dll"
      Condition="'$(PublishAot)' == 'true'" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AssemblyName>ExampleModule</AssemblyName>
    <RootNamespace>ExampleModule</RootNamespace>
  </PropertyGroup>

  <PropertyGroup Condition="'$(PublishAot)' == 'true'">
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Rename C# namespace, export symbol, and module name**

Replace `packages/example-module/dotnet/ExampleModule/EntryPoints.cs` with:

```csharp
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;

namespace ExampleModule;

public static class EntryPoints
{
  [UnmanagedCallersOnly(
      EntryPoint = "example_module_register_modules",
      CallConvs = new[] { typeof(CallConvCdecl) }
  )]
  public static int RegisterModules(nint api, nint runtimeHandle)
  {
    try
    {
      var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
      using var modules = ModuleRegistry.GetOrCreateExpoModulesObject(runtime);
      ExpoModulesProvider_ExampleModule.Register(runtime, modules);
      Console.WriteLine("ExampleModule registered ExampleModule.add.");
      return 0;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine(ex);
      return 1;
    }
  }
}
```

Replace `packages/example-module/dotnet/ExampleModule/MobileV2MathModule.cs` with:

```csharp
using Expo.ModulesCore;

namespace ExampleModule;

[ExpoModule("ExampleModule")]
public sealed partial class ExampleMathModule
{
  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }
}
```

- [ ] **Step 4: Update NativeAOT staging script**

Replace `packages/example-module/scripts/build-nativeaot.sh` with:

```bash
#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
package_dir="$(cd "${script_dir}/.." && pwd)"
repo_root="$(cd "${package_dir}/../.." && pwd)"
project="${package_dir}/dotnet/ExampleModule/ExampleModule.csproj"
adapter_dir="${repo_root}/packages/expo-modules-dotnet"
android_jni_libs="${adapter_dir}/android/src/main/jniLibs/arm64-v8a"
ios_native_libs="${adapter_dir}/ios/NativeLibs"

android_home="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}"
if [[ -z "${android_home}" ]]; then
  echo "ANDROID_HOME or ANDROID_SDK_ROOT must point to an Android SDK." >&2
  exit 1
fi

ndk_root="${ANDROID_NDK_HOME:-}"
if [[ -z "${ndk_root}" ]]; then
  ndk_root="$(find "${android_home}/ndk" -mindepth 1 -maxdepth 1 -type d 2>/dev/null | sort -V | tail -1)"
fi
if [[ -z "${ndk_root}" ]]; then
  echo "Android NDK not found under \$ANDROID_HOME/ndk." >&2
  exit 1
fi

ndk_bin="${ndk_root}/toolchains/llvm/prebuilt/darwin-x86_64/bin"
android_clang="$(find "${ndk_bin}" -maxdepth 1 -type f -name 'aarch64-linux-android*-clang' | sort -V | tail -1)"
if [[ -z "${android_clang}" ]]; then
  echo "Could not find aarch64-linux-android*-clang in ${ndk_bin}." >&2
  exit 1
fi

dotnet publish "${project}" \
  -c Release \
  -r android-arm64 \
  -p:PublishAot=true \
  -p:PublishAotUsingRuntimePack=true \
  -p:CppCompilerAndLinker="${android_clang}" \
  -p:StripSymbols=false \
  --self-contained true

dotnet publish "${project}" \
  -c Release \
  -r iossimulator-arm64 \
  -p:PublishAot=true \
  -p:PublishAotUsingRuntimePack=true \
  --self-contained true

mkdir -p "${android_jni_libs}" "${ios_native_libs}"
cp "${package_dir}/dotnet/ExampleModule/bin/Release/net10.0/android-arm64/publish/ExampleModule.so" \
  "${android_jni_libs}/libExampleModule.so"
cp "${package_dir}/dotnet/ExampleModule/bin/Release/net10.0/iossimulator-arm64/publish/ExampleModule.dylib" \
  "${ios_native_libs}/libExampleModule.dylib"
install_name_tool -id "@rpath/libExampleModule.dylib" \
  "${ios_native_libs}/libExampleModule.dylib"

echo "Copied ExampleModule NativeAOT artifacts into expo-modules-dotnet:"
echo "  android/src/main/jniLibs/arm64-v8a/libExampleModule.so"
echo "  ios/NativeLibs/libExampleModule.dylib"
```

Run:

```sh
chmod +x packages/example-module/scripts/build-nativeaot.sh
```

- [ ] **Step 5: Build example module managed project**

Run:

```sh
dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -c Debug
```

Expected: build succeeds and generated provider name matches `ExpoModulesProvider_ExampleModule`.

- [ ] **Step 6: Commit example module conversion**

Run:

```sh
git diff --check
git add packages/example-module packages/expo-modules-dotnet/ios/NativeLibs packages/expo-modules-dotnet/android/src/main/jniLibs
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "refactor: split authored example module"
```

Expected: commit succeeds. If staged native library directories are empty, `git add` skips them; the build script creates them during verification.

## Task 4: Add requireDotnetModule And Update The Mobile App

**Files:**
- Modify: `packages/expo-modules-dotnet/src/index.ts`
- Modify: `packages/expo-modules-dotnet/src/NativeExpoModulesDotnetInstaller.ts`
- Modify: `apps/mobile-app/package.json`
- Delete: `apps/mobile-app/bun.lock`
- Modify: `apps/mobile-app/App.tsx`
- Modify: `apps/mobile-app/README.md`

- [ ] **Step 1: Implement adapter JS API**

Replace `packages/expo-modules-dotnet/src/NativeExpoModulesDotnetInstaller.ts` with:

```ts
import type { TurboModule } from 'react-native';
import { TurboModuleRegistry } from 'react-native';

export interface Spec extends TurboModule {}

export default TurboModuleRegistry.get<Spec>('ExpoModulesDotnetInstaller');
```

Replace `packages/expo-modules-dotnet/src/index.ts` with:

```ts
import ExpoModulesDotnetInstaller from './NativeExpoModulesDotnetInstaller';

declare global {
  // eslint-disable-next-line no-var
  var _expoDotnet:
    | {
        modules?: Record<string, unknown>;
      }
    | undefined;
}

function ensureInstalled(): void {
  if (ExpoModulesDotnetInstaller == null) {
    throw new Error('expo-modules-dotnet native installer is not available.');
  }
}

export function requireDotnetModule<T>(name: string): T {
  ensureInstalled();

  const module = globalThis._expoDotnet?.modules?.[name];
  if (module == null) {
    throw new Error(`.NET module '${name}' is not installed.`);
  }

  return module as T;
}
```

- [ ] **Step 2: Update mobile app package dependencies**

Replace the dependency block in `apps/mobile-app/package.json` so it includes:

```json
"dependencies": {
  "example-module": "workspace:*",
  "expo": "~57.0.0",
  "expo-modules-dotnet": "workspace:*",
  "expo-status-bar": "~57.0.0",
  "react": "19.2.3",
  "react-native": "0.86.0"
}
```

Keep existing `devDependencies`, scripts, `main`, `version`, and `private`.

Remove the old Bun lockfile:

```sh
git rm apps/mobile-app/bun.lock
```

- [ ] **Step 3: Update app code to use requireDotnetModule**

Replace `apps/mobile-app/App.tsx` with:

```tsx
import { StatusBar } from 'expo-status-bar';
import { requireDotnetModule } from 'expo-modules-dotnet';
import { useEffect, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';

type ExampleModule = {
  add(a: number, b: number): number;
};

export default function App() {
  const [message, setMessage] = useState('Loading C# module...');

  useEffect(() => {
    try {
      const exampleModule = requireDotnetModule<ExampleModule>('ExampleModule');
      const result = exampleModule.add(20, 22);
      if (result !== 42) {
        throw new Error(`Unexpected C# module result: ${String(result)}`);
      }

      console.log('[ExampleModule] C# add(20, 22) returned', result);
      setMessage(`C# add result: ${result}`);
    } catch (error) {
      console.error('[ExampleModule] module call failed', error);
      setMessage(error instanceof Error ? error.message : String(error));
    }
  }, []);

  return (
    <View style={styles.container}>
      <Text style={styles.label}>Expo.ModulesCore NativeAOT</Text>
      <Text style={styles.result}>{message}</Text>
      <StatusBar style="dark" />
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    padding: 24,
    backgroundColor: '#f7f7f2',
  },
  label: {
    color: '#24515c',
    fontSize: 18,
    fontWeight: '600',
    marginBottom: 12,
  },
  result: {
    color: '#111',
    fontSize: 28,
    fontWeight: '700',
    textAlign: 'center',
  },
});
```

- [ ] **Step 4: Install workspace dependencies**

Run:

```sh
pnpm install
```

Expected:

- Root `pnpm-lock.yaml` is created or updated.
- `apps/mobile-app/node_modules/expo-modules-dotnet` resolves to the workspace package.
- `apps/mobile-app/node_modules/example-module` resolves to the workspace package.

- [ ] **Step 5: Update mobile README for new roles**

Update `apps/mobile-app/README.md` so the opening describes:

- The app lives under `apps/mobile-app`.
- `packages/expo-modules-dotnet` is the public adapter package.
- `packages/example-module` is the authored .NET module package.
- `packages/example-module/scripts/build-nativeaot.sh` manually stages NativeAOT artifacts into `packages/expo-modules-dotnet`.
- The temporary staging convention is not .NET module autolinking.

Use repo-relative paths only.

- [ ] **Step 6: Type-check mobile app package**

Run:

```sh
pnpm --filter mobile-app exec tsc --noEmit
```

Expected: TypeScript succeeds. If Expo's app template lacks a direct `tsc` binary under pnpm, run:

```sh
pnpm --filter mobile-app exec expo customize tsconfig.json --no-install
pnpm --filter mobile-app exec tsc --noEmit
```

Expected: TypeScript succeeds after dependencies are installed.

- [ ] **Step 7: Commit JS API and app dependency update**

Run:

```sh
git diff --check
git add package.json pnpm-workspace.yaml pnpm-lock.yaml apps/mobile-app packages/expo-modules-dotnet/src packages/example-module/package.json
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "feat: add dotnet module require API"
```

Expected: commit succeeds.

## Task 5: Repair Managed, Native, And Script Paths

**Files:**
- Modify: `scripts/test-managed.sh`
- Modify: `scripts/run-hermes-experiment.sh`
- Modify: `scripts/build-hermes-macos.sh`
- Modify: `apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj`
- Modify: `apps/hermes-console-app/native/CMakeLists.txt`
- Modify: `apps/hermes-console-app/native/ManagedProofLoader.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/CMakeLists.txt`
- Modify: managed test project references under `packages/expo-modules-dotnet/managed/packages/*/*.csproj`

- [ ] **Step 1: Update managed test runner paths**

In `scripts/test-managed.sh`, replace root managed paths:

- `"$repo_root/managed/packages/Expo.JSI/Expo.JSI.csproj"` with `"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj"`.
- `"$repo_root/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj"` with `"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj"`.
- `"$repo_root/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"` with `"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"`.
- Test project paths with the same `packages/expo-modules-dotnet/managed/packages/...` prefix.
- Native testhost source or build references from `native/testhost` to `packages/expo-modules-dotnet/native/testhost`.

- [ ] **Step 2: Update Hermes console runner paths**

In `scripts/run-hermes-experiment.sh`:

- Set `experiment_dir="$repo_root/apps/hermes-console-app"`.
- Replace generator project path with `"$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"`.

In `scripts/build-hermes-macos.sh`, replace:

```sh
$cmake_bin -S experiments/hermes-console-app/native -B build/hermes-console-app
```

with:

```sh
$cmake_bin -S apps/hermes-console-app/native -B build/hermes-console-app
```

- [ ] **Step 3: Update Hermes console managed references**

In `apps/hermes-console-app/managed/HermesConsoleApp/HermesConsoleApp.csproj`, replace project and analyzer references with:

```xml
<ProjectReference Include="../../../../packages/expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" />
<ProjectReference Include="../../../../packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
<ProjectReference
  Include="../../../../packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
  Condition="'$(PublishAot)' != 'true'"
  OutputItemType="Analyzer"
  ReferenceOutputAssembly="false" />
<Analyzer
  Include="../../../../packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/bin/Debug/netstandard2.0/Expo.ModulesCore.Generator.dll"
  Condition="'$(PublishAot)' == 'true'" />
```

- [ ] **Step 4: Update native CMake include/source paths**

In `apps/hermes-console-app/native/CMakeLists.txt`, replace:

- `${REPO_ROOT}/native/packages/jsi/src/ExpoJsiBridge.cpp` with `${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`.
- `${REPO_ROOT}/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp` with `${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`.
- `${REPO_ROOT}/native/include` with `${REPO_ROOT}/packages/expo-modules-dotnet/native/include`.
- `${REPO_ROOT}/native/packages/jsi/include` with `${REPO_ROOT}/packages/expo-modules-dotnet/native/packages/jsi/include`.

Make the same replacements in `packages/expo-modules-dotnet/native/testhost/CMakeLists.txt`.

- [ ] **Step 5: Update Hermes loader repo-relative app path**

In `apps/hermes-console-app/native/ManagedProofLoader.cpp`, replace:

- `experiments/hermes-console-app` with `apps/hermes-console-app`.
- `experiments/hermes-console-app/managed/HermesConsoleApp` with `apps/hermes-console-app/managed/HermesConsoleApp`.

- [ ] **Step 6: Update moved managed project references**

Run:

```sh
rg -n "managed/packages|native/include|native/packages/jsi|experiments/hermes-console-app|experiments/mobile-app" scripts apps packages --glob '!**/bin/**' --glob '!**/obj/**'
```

For each non-archive match in `scripts`, `apps`, or `packages`, update it to the new `apps/...` or `packages/expo-modules-dotnet/...` location. Keep historical archive files unchanged.

- [ ] **Step 7: Verify managed tests**

Run:

```sh
scripts/test-managed.sh
```

Expected: native testhost builds and both `Expo.JSI.Tests` and `Expo.ModulesCore.Tests` pass.

- [ ] **Step 8: Verify Hermes console app paths**

Run:

```sh
scripts/run-hermes-experiment.sh --no-run
```

Expected: Hermes console managed build and native configure/build complete without old-path failures.

- [ ] **Step 9: Commit path repairs**

Run:

```sh
git diff --check
git add scripts apps/hermes-console-app packages/expo-modules-dotnet/managed packages/expo-modules-dotnet/native
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "chore: repair workspace build paths"
```

Expected: commit succeeds.

## Task 6: Verify NativeAOT Staging And Mobile Integration

**Files:**
- Modify as needed from failures in:
  - `packages/example-module/scripts/build-nativeaot.sh`
  - `packages/expo-modules-dotnet/android/build.gradle`
  - `packages/expo-modules-dotnet/android/src/main/cpp/CMakeLists.txt`
  - `packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`
  - `apps/mobile-app/README.md`

- [ ] **Step 1: Build and stage the example NativeAOT module**

Run:

```sh
pnpm --filter example-module build:nativeaot
```

Expected:

```sh
test -f packages/expo-modules-dotnet/android/src/main/jniLibs/arm64-v8a/libExampleModule.so
test -f packages/expo-modules-dotnet/ios/NativeLibs/libExampleModule.dylib
```

- [ ] **Step 2: Refresh native projects after package metadata changes**

Run:

```sh
cd apps/mobile-app
pnpm exec expo prebuild --platform all --clean
cd ios
RBENV_VERSION=system pod install
```

Expected: prebuild and pod install complete, and generated native projects refer to `ExpoModulesDotnet`.

- [ ] **Step 3: Build Android debug app**

Run:

```sh
cd apps/mobile-app/android
./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a
```

Expected: Gradle builds `libexpo-modules-dotnet.so`, packages `libExampleModule.so`, and produces `app/build/outputs/apk/debug/app-debug.apk`.

- [ ] **Step 4: Build iOS app**

Run:

```sh
xcodebuildmcp simulator build \
  --workspace-path apps/mobile-app/ios/mobileapp.xcworkspace \
  --scheme mobileapp \
  --simulator-name "iPhone 17 Pro"
```

Expected: build succeeds and links `libExampleModule.dylib`.

- [ ] **Step 5: Run app smoke check when a simulator/emulator is available**

For iOS:

```sh
xcodebuildmcp simulator build-and-run \
  --workspace-path apps/mobile-app/ios/mobileapp.xcworkspace \
  --scheme mobileapp \
  --simulator-name "iPhone 17 Pro"
```

For Android:

```sh
cd apps/mobile-app/android
adb reverse tcp:8081 tcp:8081
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell monkey -p dev.expo.csharpv2proof 1
```

Expected Metro or device logs include:

```text
[ExampleModule] C# add(20, 22) returned 42
```

- [ ] **Step 6: Commit mobile integration fixes**

Run:

```sh
git diff --check
git add apps/mobile-app packages/example-module packages/expo-modules-dotnet pnpm-lock.yaml
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "test: verify dotnet adapter mobile proof"
```

Expected: commit succeeds if Task 6 changed tracked files. If no files changed, skip this commit and record the verification evidence in the final handoff.

## Task 7: Update Current Docs And Living Specs

**Files:**
- Modify: `docs/README.md`
- Modify: `docs/specs/README.md`
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/hermes-testhost.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/runtime-scheduling.md`
- Modify: `docs/roadmap.md` if path references still point at old current-state locations
- Archive or remove: `docs/changes/2026-07-01-pnpm-public-adapter-package/plan.md` after implementation if following repo workflow closeout
- Archive or remove: `docs/changes/2026-07-01-pnpm-public-adapter-package/spec.md` after accepted deltas are merged into living specs

- [ ] **Step 1: Update front-door docs**

In `docs/README.md`, replace current-state bullets so they describe:

- `packages/expo-modules-dotnet/native/include/expo_jsi.h`.
- `packages/expo-modules-dotnet/native/testhost/`.
- Managed packages under `packages/expo-modules-dotnet/managed/packages/`.
- `packages/expo-modules-dotnet` as the public Expo adapter package.
- `packages/example-module` as the authored example .NET module.
- `apps/hermes-console-app` and `apps/mobile-app` as runnable app proofs.
- `experiments/hostfxr-smoke` and `experiments/nativeaot-smoke` as smoke proofs.

- [ ] **Step 2: Update living specs for moved paths and adapter package**

Update specs:

- `docs/specs/README.md`: paths for managed package/test ownership.
- `docs/specs/runtime-and-abi.md`: ABI header path and React Native connector path under `packages/expo-modules-dotnet/native/...`; mobile proof path under `apps/mobile-app`.
- `docs/specs/hermes-testhost.md`: testhost path under `packages/expo-modules-dotnet/native/testhost`, managed test paths under `packages/expo-modules-dotnet/managed/packages`.
- `docs/specs/modules-core-boundary.md`: managed core path under `packages/expo-modules-dotnet/managed/packages` and JS lookup through `expo-modules-dotnet`.
- `docs/specs/runtime-scheduling.md`: mobile proof path under `apps/mobile-app`.

- [ ] **Step 3: Update roadmap current-state paths without overwriting user edits**

Run:

```sh
git diff -- docs/roadmap.md
```

If the file has pre-existing edits, preserve them. Update only stale current-state path references:

- `native/include/expo_jsi.h` to `packages/expo-modules-dotnet/native/include/expo_jsi.h`.
- `managed/packages/Expo.JSI` to `packages/expo-modules-dotnet/managed/packages/Expo.JSI`.
- `managed/packages/Expo.ModulesCore` to `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore`.
- `managed/packages/Expo.ModulesCore.Tests` to `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests`.
- `experiments/mobile-app` to `apps/mobile-app` when the reference is current-state, not historical provenance.

- [ ] **Step 4: Ensure no stale current paths remain outside archives**

Run:

```sh
rg -n "experiments/mobile-app|experiments/hermes-console-app|managed/packages|native/include|native/packages/jsi|expo-csharp-v2|ExpoCSharpV2|ExpoMobileV2Module" docs scripts apps packages \
  --glob '!docs/archive/**' \
  --glob '!**/bin/**' \
  --glob '!**/obj/**' \
  --glob '!**/node_modules/**'
```

Expected: no stale matches. If a match is intentionally historical, add wording that identifies it as historical.

- [ ] **Step 5: Commit docs and living specs**

Run:

```sh
git diff --check
git add docs/README.md docs/specs docs/roadmap.md apps packages scripts
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "docs: update specs for workspace package layout"
```

Expected: commit succeeds. If `docs/roadmap.md` contains unrelated pre-existing user edits, stage only the intended hunks with `git add -p docs/roadmap.md`.

## Task 8: Final Verification And Change Artifact Closeout

**Files:**
- Modify: `docs/changes/2026-07-01-pnpm-public-adapter-package/spec.md` only if final implementation diverged and the living spec merge needs a note before archival.
- Remove or archive: `docs/changes/2026-07-01-pnpm-public-adapter-package/plan.md`
- Remove or archive: `docs/changes/2026-07-01-pnpm-public-adapter-package/spec.md`

- [ ] **Step 1: Run canonical managed verification**

Run:

```sh
scripts/test-managed.sh
```

Expected: all managed test suites pass.

- [ ] **Step 2: Run format check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: format check passes. If it reports required formatting changes, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

Then stage and commit only formatting changes caused by this implementation.

- [ ] **Step 3: Run hot-path reflection scan**

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected: no matches in production generated-binding hot paths. Matches in tests require an explicit explanation before finishing.

- [ ] **Step 4: Run workspace and mobile package checks**

Run:

```sh
pnpm install --frozen-lockfile
pnpm --filter mobile-app exec tsc --noEmit
pnpm --filter example-module build:nativeaot
```

Expected: install, TypeScript, and NativeAOT staging succeed.

- [ ] **Step 5: Run native build checks**

Run:

```sh
scripts/run-hermes-experiment.sh --no-run
cd apps/mobile-app/android
./gradlew :app:assembleDebug -PreactNativeArchitectures=arm64-v8a
```

Expected: Hermes console build and Android app build succeed.

If iOS tooling is available in the current session, also run:

```sh
xcodebuildmcp simulator build \
  --workspace-path apps/mobile-app/ios/mobileapp.xcworkspace \
  --scheme mobileapp \
  --simulator-name "iPhone 17 Pro"
```

Expected: iOS app build succeeds.

- [ ] **Step 6: Close out change artifacts**

After living specs reflect the accepted implementation, remove or archive the transient change artifacts according to repo convention. If removing:

```sh
git rm docs/changes/2026-07-01-pnpm-public-adapter-package/plan.md
git rm docs/changes/2026-07-01-pnpm-public-adapter-package/spec.md
```

If archiving:

```sh
mkdir -p docs/archive/changes
git mv docs/changes/2026-07-01-pnpm-public-adapter-package docs/archive/changes/2026-07-01-pnpm-public-adapter-package
```

Use the option that matches the current repo convention at implementation time.

- [ ] **Step 7: Final full diff review**

Run:

```sh
git status --short
git diff --stat
git diff --check
git diff | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
```

Expected:

- No unintended local-machine paths in tracked changes.
- `docs/roadmap.md` changes are intentional and preserve any pre-existing user edits.
- No stale current-state references outside archives.

- [ ] **Step 8: Commit closeout**

Run:

```sh
git add docs apps packages scripts package.json pnpm-workspace.yaml pnpm-lock.yaml
git diff --cached | rg -n "Users/|var/folders|MacBook|localhost|127\\.0\\.0\\.1|homebrew" && exit 1 || true
git commit -m "chore: finalize workspace adapter migration"
```

Expected: commit succeeds if there are closeout changes. If no files changed after verification, skip this commit.

## Coverage Checklist

- pnpm workspace boundaries: Tasks 1 and 4.
- Apps under `apps/`: Tasks 1, 5, and 7.
- Smoke proofs remain under `experiments/`: Task 1.
- Public adapter owns TurboModule/native/managed core: Tasks 1, 2, and 5.
- `example-module` owns authored module code and NativeAOT output: Task 3.
- Temporary NativeAOT artifact staging: Tasks 3 and 6.
- `requireDotnetModule<T>` forces lazy installer initialization: Task 4.
- `_expoDotnet` namespace remains current module lookup: Tasks 3 and 4.
- Living specs merged after implementation: Task 7.
- Repo-owned verification: Task 8.
