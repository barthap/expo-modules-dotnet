# macOS Integration Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `apps/desktop-app`, a React Native macOS / Expo Desktop proof that loads `example-module` through `expo-modules-dotnet` and displays `C# add result: 42`.

**Architecture:** Keep the proof app separate from `apps/mobile-app` because the version lanes differ. Add macOS adapter code under `packages/expo-modules-dotnet/macos`, reuse the existing native JSI bridge and `ReactNativeRuntimeConnector`, and make the desktop managed loader explicit so HostFXR is the default while NativeAOT remains a per-app option where supported. Direct synchronous module methods stay JSI host functions and must not depend on `executeSync` / `CallInvoker::invokeSync`.

**Tech Stack:** pnpm workspaces, Expo 54, Expo Desktop, React Native 0.81, React Native macOS 0.81, CocoaPods, Objective-C++, C++17, HostFXR, NativeAOT-compatible C ABI, .NET 10, TypeScript.

---

## File Structure

- Create `apps/desktop-app/package.json`: app scripts and RN 0.81 / Expo Desktop dependencies.
- Create `apps/desktop-app/App.tsx`: proof UI copied in spirit from `apps/mobile-app` but independent.
- Create `apps/desktop-app/index.ts`: registers the desktop app root.
- Create `apps/desktop-app/app.json`: Expo app metadata and desktop loader config.
- Create `apps/desktop-app/tsconfig.json`: TypeScript config scoped to the desktop app.
- Create `apps/desktop-app/metro.config.js`: macOS resolver mapping from `react-native` to `react-native-macos`.
- Create `apps/desktop-app/macos/**`: checked-in Expo Desktop / React Native macOS native project files copied from the Expo Desktop template or generated once by the Expo Desktop CLI, then adapted repo-locally.
- Create `apps/desktop-app/scripts/build-managed.sh`: builds `example-module` for the selected desktop loader and stages HostFXR artifacts into a repo-relative macOS app support location.
- Modify `packages/expo-modules-dotnet/package.json`: expose or document macOS codegen/config shape only if required by React Native macOS autolinking.
- Modify `packages/expo-modules-dotnet/expo-module.config.json`: include the macOS/Apple adapter when Expo modules autolinking needs it.
- Modify `packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`: add macOS platform support and macOS source files without changing iOS NativeAOT linking behavior.
- Create `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`: macOS installer and `RCTTurboModuleWithJSIBindings` integration.
- Create `packages/expo-modules-dotnet/macos/ExpoModulesDotnetModule.swift`: macOS Expo module definition if the iOS Swift module cannot be shared cleanly.
- Create `packages/expo-modules-dotnet/macos/ManagedLoader.h`: loader interface for HostFXR and NativeAOT module registration.
- Create `packages/expo-modules-dotnet/macos/ManagedHostFxr.h`: minimal HostFXR / nethost ABI declarations so the pod build does not need local .NET SDK header paths.
- Create `packages/expo-modules-dotnet/macos/ManagedLoader.cpp`: app-configurable loader selection and module entry-point resolution.
- Create or modify `docs/specs/runtime-and-abi.md`, `docs/specs/runtime-scheduling.md`, and `docs/specs/modules-core-boundary.md`: merge accepted macOS proof deltas after implementation.
- Modify `docs/README.md` and `docs/roadmap.md`: reflect completed macOS proof evidence after verification.
- Remove or archive this `docs/changes/2026-07-01-macos-integration-proof/` directory after deltas are merged into living specs.

---

### Task 1: Create Desktop App JavaScript Shell

**Files:**
- Create: `apps/desktop-app/package.json`
- Create: `apps/desktop-app/App.tsx`
- Create: `apps/desktop-app/index.ts`
- Create: `apps/desktop-app/app.json`
- Create: `apps/desktop-app/tsconfig.json`
- Create: `apps/desktop-app/metro.config.js`

- [ ] **Step 1: Create `apps/desktop-app/package.json`**

Use `catalog:react-native-81` for version-lane dependencies and include Expo Desktop as a hard dependency.

```json
{
  "name": "desktop-app",
  "version": "1.0.0",
  "private": true,
  "main": "index.ts",
  "scripts": {
    "start": "expo start",
    "macos": "react-native run-macos",
    "build:managed": "./scripts/build-managed.sh",
    "typecheck": "tsc --noEmit"
  },
  "dependencies": {
    "example-module": "workspace:*",
    "expo": "catalog:react-native-81",
    "expo-desktop": "^0.1.37",
    "expo-desktop-config-plugins": "^1.1.34",
    "expo-desktop-modules-core": "^54.0.13",
    "expo-desktop-prebuild-config": "^1.0.20",
    "expo-desktop-stubs": "^54.0.13",
    "expo-modules-dotnet": "workspace:*",
    "react": "catalog:react-native-81",
    "react-native": "catalog:react-native-81",
    "react-native-macos": "catalog:react-native-81"
  },
  "devDependencies": {
    "@react-native-community/cli": "^20.1.3",
    "@react-native/metro-config": "catalog:react-native-81",
    "@rnx-kit/metro-config": "^2.3.0",
    "@types/react": "catalog:react-native-81",
    "typescript": "^5.9.2"
  }
}
```

- [ ] **Step 2: Create `apps/desktop-app/App.tsx`**

```tsx
import { add } from 'example-module';
import { useEffect, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';

export default function App() {
  const [message, setMessage] = useState('Loading C# module...');

  useEffect(() => {
    try {
      const result = add(20, 22);
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
      <Text style={styles.label}>Expo.ModulesCore macOS</Text>
      <Text style={styles.result}>{message}</Text>
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

- [ ] **Step 3: Create `apps/desktop-app/index.ts`**

```ts
import { AppRegistry } from 'react-native';
import App from './App';

AppRegistry.registerComponent('desktopapp', () => App);
```

- [ ] **Step 4: Create `apps/desktop-app/app.json`**

Use app config to carry the desktop loader selection. Keep paths repo-relative.

```json
{
  "expo": {
    "name": "Desktop App",
    "slug": "desktop-app",
    "scheme": "desktopapp",
    "extra": {
      "expoModulesDotnet": {
        "loader": "hostfxr",
        "assemblyName": "ExampleModule",
        "entryPointType": "ExampleModule.EntryPoints, ExampleModule",
        "entryPointMethod": "RegisterModules"
      }
    }
  }
}
```

- [ ] **Step 5: Create `apps/desktop-app/tsconfig.json`**

```json
{
  "extends": "expo/tsconfig.base",
  "compilerOptions": {
    "strict": true
  },
  "include": ["**/*.ts", "**/*.tsx"]
}
```

- [ ] **Step 6: Create `apps/desktop-app/metro.config.js`**

```js
const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);

config.resolver.resolveRequest = (context, moduleName, platform) => {
  if (
    platform === 'macos' &&
    (moduleName === 'react-native' || moduleName.startsWith('react-native/'))
  ) {
    const newModuleName = moduleName.replace('react-native', 'react-native-macos');
    return context.resolveRequest(context, newModuleName, platform);
  }
  return context.resolveRequest(context, moduleName, platform);
};

const originalGetModulesRunBeforeMainModule =
  config.serializer.getModulesRunBeforeMainModule;
config.serializer.getModulesRunBeforeMainModule = () => {
  try {
    return [
      require.resolve('react-native/Libraries/Core/InitializeCore'),
      require.resolve('react-native-macos/Libraries/Core/InitializeCore'),
    ];
  } catch {}
  return originalGetModulesRunBeforeMainModule();
};

module.exports = config;
```

- [ ] **Step 7: Install dependencies and update lockfile**

Run:

```bash
pnpm install
```

Expected: `pnpm-lock.yaml` updates and `apps/desktop-app` resolves all dependencies. If `expo-desktop` package versions differ from the plan because npm has newer compatible releases, use the versions selected by `pnpm` and record the exact resolved versions in the implementation notes.

- [ ] **Step 8: Verify TypeScript fails only on missing native project if applicable**

Run:

```bash
pnpm --filter desktop-app typecheck
```

Expected: TypeScript passes. If dependency installation reveals missing Expo Desktop package names, correct `package.json` using the packages exported by the current Expo Desktop release, rerun `pnpm install`, and rerun this command.

- [ ] **Step 9: Commit desktop JS shell**

```bash
git add apps/desktop-app/package.json apps/desktop-app/App.tsx apps/desktop-app/index.ts apps/desktop-app/app.json apps/desktop-app/tsconfig.json apps/desktop-app/metro.config.js pnpm-lock.yaml
git commit -m "feat: add desktop app shell"
```

---

### Task 2: Add Checked-In macOS Native Project

**Files:**
- Create: `apps/desktop-app/macos/Podfile`
- Create: `apps/desktop-app/macos/.xcode.env`
- Create: `apps/desktop-app/macos/.gitignore`
- Create: `apps/desktop-app/macos/PrivacyInfo.xcprivacy`
- Create: `apps/desktop-app/macos/desktopapp.xcodeproj/**`
- Create: `apps/desktop-app/macos/desktopapp.xcworkspace/contents.xcworkspacedata`
- Create: `apps/desktop-app/macos/desktopapp-macOS/AppDelegate.mm`
- Create: `apps/desktop-app/macos/desktopapp-macOS/AppDelegate.h`
- Create: `apps/desktop-app/macos/desktopapp-macOS/main.m`
- Create: `apps/desktop-app/macos/desktopapp-macOS/Info.plist`
- Create: `apps/desktop-app/macos/desktopapp-macOS/desktopapp.entitlements`
- Create: `apps/desktop-app/macos/desktopapp-macOS/Base.lproj/Main.storyboard`

- [ ] **Step 1: Generate or copy the native project from Expo Desktop template**

Preferred command:

```bash
cd apps
bunx expo-desktop@0.1.37 create-app desktopapp "Desktop App" --yes --no-install --no-agents-md --rdns dev.expo.modules.dotnet.desktopapp --version 0.81 --template github:shirakaba/expo-desktop-templates#main:app/bare-minimum/0.81/sdk-54
```

Expected: a temporary `apps/desktopapp` project with a `macos` directory. If the command shape has changed, use the same template URL and current Expo Desktop `create-app --help` output to produce equivalent checked-in native files. Do not commit generated `AGENTS.md`, `.claude`, package manager lockfiles from the temporary app, or unrelated mobile native directories.

- [ ] **Step 2: Move only the macOS project into `apps/desktop-app/macos`**

Run:

```bash
rm -rf apps/desktop-app/macos
mv apps/desktopapp/macos apps/desktop-app/macos
rm -rf apps/desktopapp
```

Expected: `apps/desktop-app/macos` exists and no temporary app remains.

- [ ] **Step 3: Edit `apps/desktop-app/macos/Podfile`**

Ensure the target uses Expo modules, React Native macOS, Hermes, and the app root. Use `bunx` nowhere in the Podfile; use `node` for package resolution.

```ruby
require File.join(File.dirname(`node --print "require.resolve('expo/package.json')"`), "scripts/autolinking")
require 'pathname'

ws_dir = Pathname.new(__dir__)
ws_dir = ws_dir.parent until
  File.exist?("#{ws_dir}/node_modules/react-native-macos/scripts/react_native_pods.rb") ||
  ws_dir.expand_path.to_s == '/'
require "#{ws_dir}/node_modules/react-native-macos/scripts/react_native_pods.rb"

ENV['RCT_NEW_ARCH_ENABLED'] = '1'

prepare_react_native_project!

target 'desktopapp-macOS' do
  platform :macos, '14.0'

  use_expo_modules!

  config_command = [
    'node',
    '--no-warnings',
    '--eval',
    'require("expo/bin/autolinking")',
    'expo-modules-autolinking',
    'react-native-config',
    '--json',
    '--platform',
    'macos',
    '--source-dir',
    File.expand_path('.', __dir__)
  ]
  config = use_native_modules!(config_command)

  use_react_native!(
    :path => File.dirname(`node --print "require.resolve('react-native-macos/package.json')"`),
    :hermes_enabled => true,
    :fabric_enabled => ENV['RCT_NEW_ARCH_ENABLED'] == '1',
    :app_path => "#{Pod::Config.instance.installation_root}/.."
  )

  post_install do |installer|
    installer.target_installation_results.pod_target_installation_results.each do |_pod_name, target_installation_result|
      target_installation_result.native_target.build_configurations.each do |config|
        config.build_settings['REACT_NATIVE_PATH'] = File.dirname(`node --print "require.resolve('react-native-macos/package.json')"`)
      end
    end

    react_native_post_install(installer)

    installer.pods_project.targets.each do |target|
      next unless target.name == 'fmt'

      target.build_configurations.each do |config|
        config.build_settings['CLANG_CXX_LANGUAGE_STANDARD'] = 'c++17'
      end
    end
  end
end
```

- [ ] **Step 4: Edit `apps/desktop-app/macos/desktopapp-macOS/AppDelegate.mm`**

Use the app module name from `index.ts`, and use Expo's virtual Metro entry in debug builds.

```objc
#import "AppDelegate.h"

#import <React/RCTBundleURLProvider.h>
#import <ReactAppDependencyProvider/RCTAppDependencyProvider.h>

@implementation AppDelegate

- (void)applicationDidFinishLaunching:(NSNotification *)notification
{
  self.moduleName = @"desktopapp";
  self.initialProps = @{};
  self.dependencyProvider = [RCTAppDependencyProvider new];
  return [super applicationDidFinishLaunching:notification];
}

- (NSURL *)sourceURLForBridge:(RCTBridge *)bridge
{
  return [self bundleURL];
}

- (NSURL *)bundleURL
{
#if DEBUG
  return [[RCTBundleURLProvider sharedSettings] jsBundleURLForBundleRoot:@".expo/.virtual-metro-entry"];
#else
  return [[NSBundle mainBundle] URLForResource:@"main" withExtension:@"jsbundle"];
#endif
}

- (BOOL)concurrentRootEnabled
{
#ifdef RN_FABRIC_ENABLED
  return true;
#else
  return false;
#endif
}

@end
```

- [ ] **Step 5: Run CocoaPods**

Run:

```bash
cd apps/desktop-app/macos
pod install
```

Expected: `apps/desktop-app/macos/Podfile.lock` is created and includes `ExpoModulesDotnet` after the adapter is visible to autolinking. At this task stage it may fail because the macOS adapter files do not exist yet; if so, record the failure and continue to Task 3 before retrying.

- [ ] **Step 6: Commit native project skeleton**

If `pod install` succeeded, include `Podfile.lock`. If it failed only because the adapter is not implemented yet, commit the native project files without `Pods/`.

```bash
git add apps/desktop-app/macos
git commit -m "feat: add desktop macos project"
```

---

### Task 3: Add macOS Adapter And Loader Boundary

**Files:**
- Modify: `packages/expo-modules-dotnet/ExpoModulesDotnet.podspec`
- Modify: `packages/expo-modules-dotnet/expo-module.config.json`
- Create: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Create: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetModule.swift`
- Create: `packages/expo-modules-dotnet/macos/ManagedLoader.h`
- Create: `packages/expo-modules-dotnet/macos/ManagedHostFxr.h`
- Create: `packages/expo-modules-dotnet/macos/ManagedLoader.cpp`

- [ ] **Step 1: Update `ExpoModulesDotnet.podspec` for macOS**

Keep iOS vendored NativeAOT linking scoped to iOS and add macOS source files. The final podspec should have this shape:

```ruby
Pod::Spec.new do |s|
  s.name           = 'ExpoModulesDotnet'
  s.version        = '0.1.0'
  s.summary        = 'Expo adapter for .NET-backed Expo modules'
  s.description    = 'Expo adapter for .NET-backed Expo modules'
  s.author         = ''
  s.homepage       = 'https://docs.expo.dev/modules/'
  s.platforms      = {
    :ios => '15.1',
    :tvos => '15.1',
    :osx => '14.0'
  }
  s.source         = { git: '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'
  install_modules_dependencies(s)

  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES',
    'HEADER_SEARCH_PATHS' => [
      '$(PODS_TARGET_SRCROOT)/native/include',
      '$(PODS_TARGET_SRCROOT)/native/packages/jsi/include'
    ].join(' '),
  }

  s.source_files = [
    'native/packages/jsi/**/*.{h,hpp,cpp}',
    'native/include/**/*.h'
  ]

  s.ios.source_files = 'ios/**/*.{h,m,mm,swift,hpp,cpp}'
  s.osx.source_files = 'macos/**/*.{h,m,mm,swift,hpp,cpp}'
  s.ios.vendored_libraries = 'ios/NativeLibs/libExampleModule.dylib'
end
```

- [ ] **Step 2: Update `expo-module.config.json`**

Keep Apple module registration visible to Expo modules autolinking.

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

Expected: this may already be correct; only change it if macOS autolinking requires a separate module entry after `pod install`.

- [ ] **Step 3: Create `packages/expo-modules-dotnet/macos/ManagedLoader.h`**

```cpp
#pragma once

#include <string>

#include "expo_jsi.h"

namespace expo::dotnet::macos {

enum class ManagedLoaderKind {
  HostFxr,
  NativeAot,
};

struct ManagedModuleConfig {
  ManagedLoaderKind loader;
  std::string assemblyPath;
  std::string runtimeConfigPath;
  std::string nethostLibraryPath;
  std::string nativeLibraryPath;
  std::string entryPointType;
  std::string entryPointMethod;
  std::string nativeExportName;
};

ManagedModuleConfig loadManagedModuleConfig();

int registerManagedModules(const ManagedModuleConfig &config,
                           const expo_jsi_api *api,
                           expo_jsi_runtime_handle runtimeHandle);

const char *managedLoaderKindName(ManagedLoaderKind loader) noexcept;

} // namespace expo::dotnet::macos
```

- [ ] **Step 4: Create `packages/expo-modules-dotnet/macos/ManagedHostFxr.h`**

Use minimal ABI declarations instead of including `hostfxr.h`, `nethost.h`, or
`coreclr_delegates.h` from a local .NET installation.

```cpp
#pragma once

#include <cstddef>

namespace expo::dotnet::macos {

using char_t = char;
using hostfxr_handle = void *;

enum hostfxr_delegate_type {
  hdt_load_assembly_and_get_function_pointer = 5,
};

using get_hostfxr_path_fn = int (*)(char_t *buffer, size_t *buffer_size, const void *parameters);

using hostfxr_initialize_for_runtime_config_fn =
  int (*)(const char_t *runtime_config_path, const void *parameters, hostfxr_handle *host_context);

using hostfxr_get_runtime_delegate_fn =
  int (*)(hostfxr_handle host_context, hostfxr_delegate_type type, void **delegate);

using hostfxr_close_fn = int (*)(hostfxr_handle host_context);

using load_assembly_and_get_function_pointer_fn =
  int (*)(const char_t *assembly_path,
          const char_t *type_name,
          const char_t *method_name,
          const char_t *delegate_type_name,
          void *reserved,
          void **delegate);

inline constexpr const char_t *kUnmanagedCallersOnlyMethod = nullptr;

} // namespace expo::dotnet::macos
```

- [ ] **Step 5: Create `packages/expo-modules-dotnet/macos/ManagedLoader.cpp`**

Start with HostFXR implemented and NativeAOT shape explicit. If NativeAOT cannot be verified in this milestone, return a loud unsupported error for `nativeaot` and keep the config surface stable.

```cpp
#include "ManagedHostFxr.h"
#include "ManagedLoader.h"

#include <Foundation/Foundation.h>

#include <dlfcn.h>

#include <filesystem>
#include <stdexcept>
#include <string>

namespace expo::dotnet::macos {
namespace {

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);

template <typename Function> Function resolveSymbol(void *library, const char *name)
{
  auto *symbol = dlsym(library, name);
  if (symbol == nullptr) {
    throw std::runtime_error("Failed to resolve symbol: " + std::string(name));
  }
  return reinterpret_cast<Function>(symbol);
}

std::string resourcePath(NSString *name, NSString *extension)
{
  auto *url = [[NSBundle mainBundle] URLForResource:name withExtension:extension];
  if (url == nil) {
    throw std::runtime_error("Missing managed resource in app bundle.");
  }
  return std::string([[url path] UTF8String]);
}

ManagedLoaderKind parseLoader(NSString *value)
{
  if (value == nil || [value isEqualToString:@"hostfxr"]) {
    return ManagedLoaderKind::HostFxr;
  }
  if ([value isEqualToString:@"nativeaot"]) {
    return ManagedLoaderKind::NativeAot;
  }
  throw std::runtime_error("Unsupported EXPO_DOTNET_LOADER value.");
}

load_assembly_and_get_function_pointer_fn loadHostFxrDelegate(const ManagedModuleConfig &config)
{
  void *nethostLibrary = dlopen(config.nethostLibraryPath.c_str(), RTLD_NOW | RTLD_LOCAL);
  if (nethostLibrary == nullptr) {
    throw std::runtime_error(dlerror());
  }

  auto getHostFxrPath = resolveSymbol<get_hostfxr_path_fn>(nethostLibrary, "get_hostfxr_path");

  char_t hostfxrPath[4096];
  size_t hostfxrPathSize = sizeof(hostfxrPath) / sizeof(char_t);
  int rc = getHostFxrPath(hostfxrPath, &hostfxrPathSize, nullptr);
  if (rc != 0) {
    throw std::runtime_error("get_hostfxr_path failed with code " + std::to_string(rc));
  }

  void *library = dlopen(hostfxrPath, RTLD_LAZY | RTLD_LOCAL);
  if (library == nullptr) {
    throw std::runtime_error(dlerror());
  }

  auto initForConfig = resolveSymbol<hostfxr_initialize_for_runtime_config_fn>(
    library, "hostfxr_initialize_for_runtime_config");
  auto getRuntimeDelegate = resolveSymbol<hostfxr_get_runtime_delegate_fn>(
    library, "hostfxr_get_runtime_delegate");
  auto closeHostFxr = resolveSymbol<hostfxr_close_fn>(library, "hostfxr_close");

  hostfxr_handle context = nullptr;
  rc = initForConfig(config.runtimeConfigPath.c_str(), nullptr, &context);
  if (rc != 0 || context == nullptr) {
    throw std::runtime_error("hostfxr_initialize_for_runtime_config failed with code " +
                             std::to_string(rc));
  }

  void *loadAssembly = nullptr;
  rc = getRuntimeDelegate(context, hdt_load_assembly_and_get_function_pointer, &loadAssembly);
  closeHostFxr(context);
  if (rc != 0 || loadAssembly == nullptr) {
    throw std::runtime_error("hostfxr_get_runtime_delegate failed with code " +
                             std::to_string(rc));
  }

  return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadAssembly);
}

int registerHostFxr(const ManagedModuleConfig &config,
                    const expo_jsi_api *api,
                    expo_jsi_runtime_handle runtimeHandle)
{
  auto loadAssembly = loadHostFxrDelegate(config);

  RegisterModulesFn registerModules = nullptr;
  int rc = loadAssembly(config.assemblyPath.c_str(),
                        config.entryPointType.c_str(),
                        config.entryPointMethod.c_str(),
                        kUnmanagedCallersOnlyMethod,
                        nullptr,
                        reinterpret_cast<void **>(&registerModules));
  if (rc != 0 || registerModules == nullptr) {
    throw std::runtime_error("Failed to resolve HostFXR module entry point: " +
                             std::to_string(rc));
  }

  return registerModules(api, runtimeHandle);
}

int registerNativeAot(const ManagedModuleConfig &config,
                      const expo_jsi_api *api,
                      expo_jsi_runtime_handle runtimeHandle)
{
  void *library = dlopen(config.nativeLibraryPath.c_str(), RTLD_NOW | RTLD_LOCAL);
  if (library == nullptr) {
    throw std::runtime_error(dlerror());
  }
  auto registerModules = resolveSymbol<RegisterModulesFn>(library, config.nativeExportName.c_str());
  return registerModules(api, runtimeHandle);
}

} // namespace

ManagedModuleConfig loadManagedModuleConfig()
{
  NSDictionary *environment = [[NSProcessInfo processInfo] environment];
  auto loader = parseLoader(environment[@"EXPO_DOTNET_LOADER"]);

  if (loader == ManagedLoaderKind::HostFxr) {
    return ManagedModuleConfig{
      loader,
      resourcePath(@"ExampleModule", @"dll"),
      resourcePath(@"ExampleModule.runtimeconfig", @"json"),
      resourcePath(@"libnethost", @"dylib"),
      "",
      "ExampleModule.EntryPoints, ExampleModule",
      "RegisterModules",
      "example_module_register_modules",
    };
  }

  return ManagedModuleConfig{
    loader,
    "",
    "",
    "",
    resourcePath(@"ExampleModule", @"dylib"),
    "ExampleModule.EntryPoints, ExampleModule",
    "RegisterModules",
    "example_module_register_modules",
  };
}

int registerManagedModules(const ManagedModuleConfig &config,
                           const expo_jsi_api *api,
                           expo_jsi_runtime_handle runtimeHandle)
{
  switch (config.loader) {
    case ManagedLoaderKind::HostFxr:
      return registerHostFxr(config, api, runtimeHandle);
    case ManagedLoaderKind::NativeAot:
      return registerNativeAot(config, api, runtimeHandle);
  }
  throw std::runtime_error("Unhandled managed loader kind.");
}

const char *managedLoaderKindName(ManagedLoaderKind loader) noexcept
{
  switch (loader) {
    case ManagedLoaderKind::HostFxr:
      return "hostfxr";
    case ManagedLoaderKind::NativeAot:
      return "nativeaot";
  }
  return "unknown";
}

} // namespace expo::dotnet::macos
```

- [ ] **Step 6: Create `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`**

Reuse the iOS installer pattern but call the loader abstraction. Include logs for scheduler evidence.

```objc
#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <memory>
#include <vector>

#include "ManagedLoader.h"
#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
@end

namespace {

class InstalledRuntime final {
public:
  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector_(std::move(connector)),
      runtimeHandle_(runtimeHandle)
  {
  }

  ~InstalledRuntime()
  {
    if (runtimeHandle_ != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle_);
    }
    if (connector_ != nullptr) {
      connector_->invalidate();
    }
  }

  bool registerModules()
  {
    if (registered_) {
      return true;
    }

    try {
      auto config = expo::dotnet::macos::loadManagedModuleConfig();
      auto status = expo::dotnet::macos::registerManagedModules(
        config, expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
      if (status != 0) {
        NSLog(@"[ExpoModulesDotnet] %s module registration failed with status %d.",
              expo::dotnet::macos::managedLoaderKindName(config.loader),
              status);
        return false;
      }
      registered_ = true;
      NSLog(@"[ExpoModulesDotnet] %s module registered.",
            expo::dotnet::macos::managedLoaderKindName(config.loader));
      return true;
    } catch (const std::exception &error) {
      NSLog(@"[ExpoModulesDotnet] module registration failed: %s", error.what());
      return false;
    }
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  bool registered_ = false;
};

class ExpoModulesDotnetInstallerTurboModule final : public facebook::react::TurboModule {
public:
  explicit ExpoModulesDotnetInstallerTurboModule(
    const facebook::react::ObjCTurboModule::InitParams &params)
    : facebook::react::TurboModule(params.moduleName, params.jsInvoker)
    , installer_(static_cast<id<ExpoModulesDotnetInstalling>>(params.instance))
  {
    methodMap_["installModules"] = MethodMetadata{
      .argCount = 0,
      .invoker = ExpoModulesDotnetInstallerTurboModule::installModules,
    };
  }

private:
  static facebook::jsi::Value installModules(facebook::jsi::Runtime &runtime,
                                             facebook::react::TurboModule &turboModule,
                                             const facebook::jsi::Value *,
                                             size_t)
  {
    auto &installerTurboModule =
      static_cast<ExpoModulesDotnetInstallerTurboModule &>(turboModule);
    return facebook::jsi::Value([installerTurboModule.installer_ installModules]);
  }

  id<ExpoModulesDotnetInstalling> installer_;
};

} // namespace

@interface ExpoModulesDotnetInstaller
  : NSObject <RCTBridgeModule, RCTTurboModuleWithJSIBindings, ExpoModulesDotnetInstalling>
@end

@implementation ExpoModulesDotnetInstaller {
  std::vector<std::shared_ptr<InstalledRuntime>> _installedRuntimes;
}

RCT_EXPORT_MODULE()

- (std::shared_ptr<facebook::react::TurboModule>)getTurboModule:
  (const facebook::react::ObjCTurboModule::InitParams &)params
{
  return std::make_shared<ExpoModulesDotnetInstallerTurboModule>(params);
}

- (void)installJSIBindingsWithRuntime:(facebook::jsi::Runtime &)runtime
                          callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker
{
  NSLog(@"[ExpoModulesDotnet] macOS JSI bindings installed. callInvoker=%p canExecuteSyncEvidence=unknown", callInvoker.get());
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  _installedRuntimes.push_back(std::move(installedRuntime));
}

- (BOOL)installModules
{
  BOOL installed = NO;
  for (const auto &installedRuntime : _installedRuntimes) {
    installed = installedRuntime->registerModules() || installed;
  }

  if (!installed) {
    NSLog(@"[ExpoModulesDotnet] macOS module runtime is not ready.");
  }

  return installed;
}

@end
```

- [ ] **Step 7: Create `packages/expo-modules-dotnet/macos/ExpoModulesDotnetModule.swift`**

```swift
import ExpoModulesCore

public class ExpoModulesDotnetModule: Module {
  public func definition() -> ModuleDefinition {
    Name("ExpoModulesDotnet")
  }
}
```

- [ ] **Step 8: Run pod install**

Run:

```bash
cd apps/desktop-app/macos
pod install
```

Expected: `ExpoModulesDotnet` appears in `Podfile.lock`, and the pod builds source from `packages/expo-modules-dotnet/macos` plus the reusable native bridge. If duplicate Swift module definitions occur because iOS and macOS Swift files are both compiled for macOS, narrow `s.source_files` by platform-specific subspecs or platform conditionals and rerun.

- [ ] **Step 9: Commit macOS adapter**

```bash
git add packages/expo-modules-dotnet/ExpoModulesDotnet.podspec packages/expo-modules-dotnet/expo-module.config.json packages/expo-modules-dotnet/macos apps/desktop-app/macos/Podfile.lock
git commit -m "feat: add macos dotnet adapter"
```

---

### Task 4: Build And Stage ExampleModule For HostFXR

**Files:**
- Create: `apps/desktop-app/scripts/build-managed.sh`
- Modify: `apps/desktop-app/macos/desktopapp.xcodeproj/project.pbxproj`
- Modify: `apps/desktop-app/README.md`

- [ ] **Step 1: Create `apps/desktop-app/scripts/build-managed.sh`**

The script builds the generator first, builds `ExampleModule`, and stages the HostFXR artifacts into the macOS app resources directory used by Xcode.

```bash
#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd -P)"
loader="${EXPO_JSI_DOTNET_LOADER:-hostfxr}"
configuration="${CONFIGURATION:-Debug}"
example_project="$repo_root/packages/example-module/dotnet/ExampleModule/ExampleModule.csproj"
generator_project="$repo_root/packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
resource_dir="$repo_root/apps/desktop-app/macos/desktopapp-macOS/Managed"

usage() {
  cat <<'EOF'
Usage: apps/desktop-app/scripts/build-managed.sh

Environment:
  CONFIGURATION              .NET configuration. Default: Debug
  EXPO_JSI_DOTNET_LOADER     Managed loader: hostfxr or nativeaot. Default: hostfxr
EOF
}

nativeaot_rid() {
  case "$(uname -m)" in
    arm64) printf '%s\n' "osx-arm64" ;;
    x86_64) printf '%s\n' "osx-x64" ;;
    *)
      echo "Unsupported macOS architecture for NativeAOT: $(uname -m)" >&2
      exit 1
      ;;
  esac
}

dotnet_root() {
  if [[ -n "${DOTNET_ROOT:-}" ]]; then
    printf '%s\n' "$DOTNET_ROOT"
    return
  fi

  local base_path
  base_path="$(dotnet --info | awk -F: '/Base Path/ { gsub(/^[ \t]+/, "", $2); print $2; exit }')"
  if [[ -z "$base_path" ]]; then
    echo "Could not determine .NET Base Path from dotnet --info." >&2
    exit 1
  fi

  cd "$base_path/../.."
  pwd -P
}

host_pack_dir() {
  local rid="$1"
  local root
  root="$(dotnet_root)"

  find "$root/packs/Microsoft.NETCore.App.Host.$rid" \
    -mindepth 1 \
    -maxdepth 1 \
    -type d |
    awk -F/ '$NF ~ /^10[.]/ { print }' |
    sort -t. -k1,1n -k2,2n -k3,3n |
    tail -n 1
}

copy_nethost() {
  local rid
  local pack_dir

  rid="$(nativeaot_rid)"
  pack_dir="$(host_pack_dir "$rid")"
  if [[ -z "$pack_dir" ]]; then
    echo "Could not find Microsoft.NETCore.App.Host.$rid 10.x pack." >&2
    exit 1
  fi

  cp "$pack_dir/runtimes/$rid/native/libnethost.dylib" "$resource_dir/"
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

if [[ "$loader" != "hostfxr" && "$loader" != "nativeaot" ]]; then
  echo "EXPO_JSI_DOTNET_LOADER must be hostfxr or nativeaot, got: $loader" >&2
  exit 1
fi

rm -rf "$resource_dir"
mkdir -p "$resource_dir"
copy_nethost

echo "==> Building modules generator analyzer"
dotnet build "$generator_project" -c Debug

if [[ "$loader" == "hostfxr" ]]; then
  echo "==> Building ExampleModule for HostFXR"
  dotnet build "$example_project" -c "$configuration"
  output_dir="$repo_root/packages/example-module/dotnet/ExampleModule/bin/$configuration/net10.0"
  cp "$output_dir/ExampleModule.dll" "$resource_dir/"
  cp "$output_dir/ExampleModule.runtimeconfig.json" "$resource_dir/"
  cp "$output_dir/Expo.JSI.dll" "$resource_dir/"
  cp "$output_dir/Expo.ModulesCore.dll" "$resource_dir/"
else
  rid="$(nativeaot_rid)"
  echo "==> Publishing ExampleModule for NativeAOT ($rid)"
  dotnet publish "$example_project" \
    -c "$configuration" \
    -r "$rid" \
    /p:PublishAot=true \
    /p:NativeLib=Shared
  output_dir="$repo_root/packages/example-module/dotnet/ExampleModule/bin/$configuration/net10.0/$rid/publish"
  cp "$output_dir/ExampleModule.dylib" "$resource_dir/"
fi

echo "==> Staged managed artifacts in apps/desktop-app/macos/desktopapp-macOS/Managed"
```

- [ ] **Step 2: Make script executable**

Run:

```bash
chmod +x apps/desktop-app/scripts/build-managed.sh
```

- [ ] **Step 3: Run HostFXR staging**

Run:

```bash
pnpm --filter desktop-app build:managed
```

Expected: `apps/desktop-app/macos/desktopapp-macOS/Managed/ExampleModule.dll` and `ExampleModule.runtimeconfig.json` exist. Do not commit generated managed binaries unless the project already commits equivalent proof binaries for the same purpose and the file is intentionally reviewed.

- [ ] **Step 4: Add the `Managed` folder to Xcode resources**

Open `apps/desktop-app/macos/desktopapp.xcodeproj/project.pbxproj` and add the `desktopapp-macOS/Managed` folder as a folder reference in the app target's resources phase. Use a folder reference, not one build file per DLL, so rebuilds can refresh staged managed files without rewriting the project.

Expected project entry shape:

```text
/* Managed */ = {isa = PBXFileReference; lastKnownFileType = folder; path = Managed; sourceTree = "<group>"; };
/* Managed in Resources */ = {isa = PBXBuildFile; fileRef = /* Managed */; };
```

If the generated project uses a different object layout, add the folder reference with Xcode or `xcodeproj` tooling and inspect the diff to ensure only the resource folder reference changed.

- [ ] **Step 5: Ignore staged managed artifacts if they are generated locally**

Add to `apps/desktop-app/macos/.gitignore`:

```gitignore
desktopapp-macOS/Managed/
```

If Xcode requires the folder reference to exist, commit `apps/desktop-app/macos/desktopapp-macOS/Managed/.gitkeep` and ignore all other files under that directory:

```gitignore
desktopapp-macOS/Managed/*
!desktopapp-macOS/Managed/.gitkeep
```

- [ ] **Step 6: Create `apps/desktop-app/README.md`**

```markdown
# Expo .NET macOS Desktop Proof

This app exercises an authored .NET module inside real React Native macOS
Hermes through `expo-modules-dotnet`.

The default desktop loader is HostFXR:

```bash
pnpm --filter desktop-app build:managed
cd apps/desktop-app/macos
pod install
cd ..
pnpm start
pnpm macos
```

To select a loader:

```bash
EXPO_JSI_DOTNET_LOADER=hostfxr pnpm --filter desktop-app build:managed
EXPO_JSI_DOTNET_LOADER=nativeaot CONFIGURATION=Release pnpm --filter desktop-app build:managed
```

Expected app output is `C# add result: 42`.

This proof does not implement .NET module autolinking. The managed artifacts
are staged explicitly into the macOS app resources.
```

- [ ] **Step 7: Commit managed staging**

```bash
git add apps/desktop-app/scripts/build-managed.sh apps/desktop-app/macos/.gitignore apps/desktop-app/macos/desktopapp.xcodeproj/project.pbxproj apps/desktop-app/macos/desktopapp-macOS/Managed/.gitkeep apps/desktop-app/README.md
git commit -m "feat: stage desktop managed module"
```

---

### Task 5: Verify Direct Host Function Semantics And Scheduler Evidence

**Files:**
- Modify: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/macos/ManagedLoader.cpp`
- Create: `docs/archive/spike-results/2026-07-01-macos-integration-proof.md`

- [ ] **Step 1: Add evidence logs without changing behavior**

In `ExpoModulesDotnetInstaller.mm`, log the install hook and registration timing:

```objc
NSLog(@"[ExpoModulesDotnet] macOS installJSIBindingsWithRuntime called.");
NSLog(@"[ExpoModulesDotnet] macOS installModules called with %zu captured runtime(s).", _installedRuntimes.size());
```

In `ManagedLoader.cpp`, log the selected loader and artifact path using repo-independent app-bundle paths:

```cpp
NSLog(@"[ExpoModulesDotnet] macOS managed loader selected: %s", managedLoaderKindName(config.loader));
```

- [ ] **Step 2: Run desktop proof with HostFXR**

Terminal 1:

```bash
cd apps/desktop-app
pnpm start
```

Terminal 2:

```bash
pnpm --filter desktop-app build:managed
cd apps/desktop-app
pnpm macos
```

Expected: the app launches and displays `C# add result: 42`. Logs include:

```text
[ExampleModule] C# add(20, 22) returned 42
[ExpoModulesDotnet] macOS installJSIBindingsWithRuntime called.
[ExpoModulesDotnet] macOS managed loader selected: hostfxr
```

- [ ] **Step 3: Confirm direct sync host function behavior**

Temporarily instrument `ReactNativeRuntimeExecutor::executeSync` in `packages/expo-modules-dotnet/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp`:

```cpp
throw std::runtime_error("executeSync must not be required for direct module host functions.");
```

Run the desktop proof again:

```bash
pnpm --filter desktop-app build:managed
cd apps/desktop-app
pnpm macos
```

Expected: `ExampleModule.add(20, 22)` still returns `42`. If the app fails because this throw is hit, stop and document a design flaw; do not work around it by adding `executeSync` usage.

- [ ] **Step 4: Revert only the temporary instrumentation**

Restore the original `executeSync` implementation from `ReactNativeRuntimeConnector.cpp`.

Run:

```bash
git diff -- packages/expo-modules-dotnet/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp
```

Expected: no diff remains for the temporary instrumentation.

- [ ] **Step 5: Write spike result**

Create `docs/archive/spike-results/2026-07-01-macos-integration-proof.md`:

```markdown
# macOS Integration Proof

## Hypothesis

A React Native macOS 0.81 / Expo 54 host can install the portable C# / JSI
bridge through the macOS adapter, load `ExampleModule` through HostFXR, and run
generated synchronous module functions as direct JSI host functions without
requiring `executeSync` / `CallInvoker::invokeSync`.

## Commands Run

- `pnpm install`
- `pnpm --filter desktop-app build:managed`
- `cd apps/desktop-app/macos && pod install`
- `cd apps/desktop-app && pnpm start`
- `cd apps/desktop-app && pnpm macos`
- `pnpm --filter desktop-app typecheck`
- `scripts/test-managed.sh`
- `scripts/format.sh --check --all`
- `git diff --check`

## Expected Result

The macOS app displays `C# add result: 42`, and temporary `executeSync`
failure instrumentation does not affect the direct module call.

## Actual Result

If the proof succeeds, write:

`The macOS app displayed C# add result: 42. Metro logged [ExampleModule] C# add(20, 22) returned 42. The macOS adapter logged installJSIBindingsWithRuntime and selected the hostfxr loader.`

If the proof fails because the direct host-function call depends on
`executeSync` / `CallInvoker::invokeSync`, write:

`The proof stopped because ExampleModule.add(20, 22) depended on executeSync / CallInvoker::invokeSync. This is a design flaw finding for the direct synchronous module path.`

## Artifacts

If no artifact is saved, write:

`No screenshot or log artifact was saved; verification used the live app window and terminal logs.`

If an artifact is saved, write a repo-relative path such as
`docs/archive/spike-results/artifacts/macos-integration-proof.png`.

## Ownership And Lifetime Findings

Write the runtime install hook name, the number of retained runtime records,
and the invalidation or reload hook evidence observed during the run. If no
reload hook is found, write:

`No reload-specific invalidation hook was confirmed in this slice; adapter state is invalidated when the retained InstalledRuntime records are released.`

## Scheduler Findings

Write the call invoker evidence, async scheduling evidence, and whether
`CanExecuteSync` is true or false. If the instrumentation proof passes, include
this exact sentence:

`ExampleModule.add(20, 22) completed as a direct JSI host function and did not require executeSync or CallInvoker::invokeSync.`

## Stop/Go Decision

If the proof succeeds, write:

`Go: the macOS proof is sufficient to compare against mobile and proceed toward cross-host lifecycle contract work.`

If the proof exposes a design flaw, write:

`Stop: fix the direct synchronous module host-function path before using this proof as lifecycle/scheduler evidence.`
```

- [ ] **Step 6: Commit evidence instrumentation and spike result**

```bash
git add packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm packages/expo-modules-dotnet/macos/ManagedLoader.cpp docs/archive/spike-results/2026-07-01-macos-integration-proof.md
git commit -m "docs: record macos scheduler proof"
```

---

### Task 6: Merge Accepted Deltas Into Living Specs

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/runtime-scheduling.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/README.md`
- Modify: `docs/roadmap.md`
- Delete or archive: `docs/changes/2026-07-01-macos-integration-proof/spec.md`
- Delete or archive: `docs/changes/2026-07-01-macos-integration-proof/plan.md`

- [ ] **Step 1: Update `docs/specs/runtime-and-abi.md`**

Add a requirement for the macOS adapter preserving the ABI boundary:

```markdown
### Requirement: React Native macOS Adapter Preserves ABI Ownership

`packages/expo-modules-dotnet/macos` SHALL adapt an already-created React
Native macOS Hermes `facebook::jsi::Runtime` to the existing `expo_jsi.h` ABI
without exposing raw JSI layouts to managed code. The adapter MAY load managed
module logic through HostFXR or NativeAOT, but loader choice SHALL NOT change
the `expo_jsi_api` table or opaque runtime handle shape passed into managed
code.

#### Scenario: HostFXR macOS module registration uses the existing ABI
- **GIVEN** a React Native macOS host provides an active Hermes runtime
- **WHEN** the macOS adapter loads `ExampleModule` through HostFXR
- **THEN** managed code SHALL receive the existing `expo_jsi_api` table and
  opaque runtime handle
- **AND** generated module providers SHALL register into
  `globalThis._expoDotnet.modules`
```

- [ ] **Step 2: Update `docs/specs/runtime-scheduling.md`**

Add the direct host-function requirement:

```markdown
### Requirement: Direct Module Host Functions Do Not Require Sync Dispatch

Generated synchronous module functions SHALL run as direct JSI host functions
inside the current JavaScript call. They SHALL NOT require managed code to call
`JavaScriptRuntime.Execute`, native `executeSync`, or
`CallInvoker::invokeSync`.

#### Scenario: Sync dispatch is unavailable
- **GIVEN** a React Native host adapter reports `CanExecuteSync` as false
- **WHEN** JavaScript calls a generated synchronous C# module function
- **THEN** the host function SHALL decode arguments, call managed module logic,
  and encode the result without using sync runtime dispatch
```

- [ ] **Step 3: Update `docs/specs/modules-core-boundary.md`**

Add macOS proof ownership:

```markdown
### Requirement: Desktop Proof Uses Existing Module Registry Contract

The macOS desktop proof SHALL use `Expo.ModulesCore` generated providers and
the existing default dotnet module namespace.

#### Scenario: Desktop app requires the example module
- **GIVEN** `apps/desktop-app` calls `requireDotnetModule("ExampleModule")`
- **WHEN** the macOS adapter has registered the managed provider
- **THEN** the module SHALL be read from `globalThis._expoDotnet.modules`
- **AND** the proof SHALL NOT create or mutate `globalThis.expo`
```

- [ ] **Step 4: Update `docs/README.md` current state**

Add:

```markdown
- `apps/desktop-app/` is the React Native macOS / Expo Desktop integration
  proof app. It consumes `expo-modules-dotnet` and `example-module`, defaults
  to HostFXR for desktop managed loading, and records macOS lifecycle and
  scheduler evidence.
```

- [ ] **Step 5: Update `docs/roadmap.md` P0 status**

Mark the macOS lifecycle/scheduler proof as completed or partially completed based on Task 5 evidence. Keep RNW and cross-host lifecycle contract as future work.

- [ ] **Step 6: Archive the change directory**

Move the accepted delta artifacts under `docs/archive/changes/2026-07-01-macos-integration-proof/`:

```bash
mkdir -p docs/archive/changes
mv docs/changes/2026-07-01-macos-integration-proof docs/archive/changes/
```

- [ ] **Step 7: Run docs checks**

```bash
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: `git diff --check` passes. `rg` produces no output unless a match is intentionally historical and explained.

- [ ] **Step 8: Commit living spec merge**

```bash
git add docs/README.md docs/roadmap.md docs/specs docs/archive/changes/2026-07-01-macos-integration-proof
git add -u docs/changes/2026-07-01-macos-integration-proof
git commit -m "docs: merge macos integration proof specs"
```

---

### Task 7: Final Verification

**Files:**
- Inspect all changed files.

- [ ] **Step 1: Run dependency verification**

```bash
pnpm install --frozen-lockfile
```

Expected: lockfile is current.

- [ ] **Step 2: Run desktop typecheck**

```bash
pnpm --filter desktop-app typecheck
```

Expected: TypeScript passes.

- [ ] **Step 3: Run managed test suite**

```bash
scripts/test-managed.sh
```

Expected: all managed tests pass.

- [ ] **Step 4: Run format check**

```bash
scripts/format.sh --check --all
```

Expected: all formatting checks pass. If it fails due to formatting, run `scripts/format.sh`, then repeat this check.

- [ ] **Step 5: Run diff check**

```bash
git diff --check
```

Expected: no whitespace errors.

- [ ] **Step 6: Confirm no local absolute paths are staged**

```bash
git diff --cached | rg "/Users/|~/|<username>|private hostname|machine-specific|<repo_root>"
```

Expected: no output. If this finds a path in untracked or generated native files, replace it with a repo-relative path or remove the file from the commit.

- [ ] **Step 7: Confirm direct sync requirement remains true**

Inspect `docs/archive/spike-results/2026-07-01-macos-integration-proof.md` and the final app run logs. Confirm the result explicitly states whether `ExampleModule.add(20, 22)` required `executeSync` / `CallInvoker::invokeSync`.

- [ ] **Step 8: Commit final verification fixes if needed**

If verification required formatting or small doc corrections:

```bash
git status --short
git add -u
git diff --cached --name-only
git commit -m "chore: verify macos integration proof"
```

Expected: working tree is clean or contains only intentionally uncommitted generated local artifacts.
