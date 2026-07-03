#pragma once

#include <string>

#include "expo_jsi.h"

namespace expo::modules::dotnet {

enum class ManagedLoaderKind {
  HostFxr,
  NativeAot,
};

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using CreateRuntimeContextFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownRuntimeContextFn = void (*)(void *);

struct ManagedRuntimeContextEntryPoints {
  RegisterModulesFn registerModules = nullptr;
  CreateRuntimeContextFn createRuntimeContext = nullptr;
  TeardownRuntimeContextFn teardownRuntimeContext = nullptr;
};

struct ManagedModuleConfig {
  ManagedLoaderKind loaderKind = ManagedLoaderKind::HostFxr;
  std::wstring assemblyPath;
  std::wstring runtimeConfigPath;
  std::wstring nethostPath;
  std::wstring nativeLibraryPath;
  std::wstring typeName;
  std::wstring methodName;
};

ManagedModuleConfig loadExampleModuleConfig();
const wchar_t *managedLoaderKindName(ManagedLoaderKind loaderKind);
RegisterModulesFn resolveRegisterModules(const ManagedModuleConfig &config);
ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(
  const ManagedModuleConfig &config);
std::wstring managedLoaderLastError();

} // namespace expo::modules::dotnet
