#pragma once

#include <string>

#include "expo_jsi.h"

namespace expo::modules::dotnet {

enum class ManagedLoaderKind {
  HostFxr,
  NativeAot,
};

using CreateRuntimeContextFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownRuntimeContextFn = void (*)(void *);
using WindowsGetViewMetadataFn = int (*)(uint8_t **, int *);
using WindowsFreeBufferFn = void (*)(uint8_t *);
using WindowsCreateViewFn = void *(*)(void *, const uint8_t *, int);
using WindowsInitializeCompositionFn = intptr_t (*)(void *, intptr_t);
using WindowsUpdateLayoutFn = void (*)(void *, float, float);
using WindowsUpdateStringPropFn =
  int (*)(void *, void *, const uint8_t *, int, const uint8_t *, int, const uint8_t *, int);
using WindowsDestroyViewFn = void (*)(void *);

struct ManagedRuntimeContextEntryPoints {
  CreateRuntimeContextFn createRuntimeContext = nullptr;
  TeardownRuntimeContextFn teardownRuntimeContext = nullptr;
};

struct ManagedWindowsViewEntryPoints {
  WindowsGetViewMetadataFn getViewMetadata = nullptr;
  WindowsFreeBufferFn freeBuffer = nullptr;
  WindowsCreateViewFn createView = nullptr;
  WindowsInitializeCompositionFn initializeComposition = nullptr;
  WindowsUpdateLayoutFn updateLayout = nullptr;
  WindowsUpdateStringPropFn updateStringProp = nullptr;
  WindowsDestroyViewFn destroyView = nullptr;
};

struct ManagedModuleConfig {
  ManagedLoaderKind loaderKind = ManagedLoaderKind::HostFxr;
  std::wstring assemblyPath;
  std::wstring runtimeConfigPath;
  std::wstring nethostPath;
  std::wstring nativeLibraryPath;
  std::wstring typeName;
};

ManagedModuleConfig loadManagedHostConfig();
const wchar_t *managedLoaderKindName(ManagedLoaderKind loaderKind);
ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(
  const ManagedModuleConfig &config);
ManagedWindowsViewEntryPoints resolveWindowsViewEntryPoints(const ManagedModuleConfig &config);
std::wstring managedLoaderLastError();

} // namespace expo::modules::dotnet
