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
using WindowsGetViewLastErrorFn = int (*)(uint8_t *, int);
using WindowsGetViewCountFn = int (*)();
using WindowsGetViewStringFn = int (*)(int, uint8_t *, int);
using WindowsGetViewPropCountFn = int (*)(int);
using WindowsGetViewPropNameFn = int (*)(int, int, uint8_t *, int);
using WindowsGetViewPropKindFn = int (*)(int, int);
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
  WindowsGetViewLastErrorFn getViewLastError = nullptr;
  WindowsGetViewCountFn getViewCount = nullptr;
  WindowsGetViewStringFn getViewModuleName = nullptr;
  WindowsGetViewStringFn getViewComponentName = nullptr;
  WindowsGetViewPropCountFn getViewPropCount = nullptr;
  WindowsGetViewPropNameFn getViewPropName = nullptr;
  WindowsGetViewPropKindFn getViewPropKind = nullptr;
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
