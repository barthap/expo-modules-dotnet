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
  std::string assemblyPath;
  std::string runtimeConfigPath;
  std::string nethostPath;
  std::string nativeLibraryPath;
  std::string typeName;
  std::string methodName;
};

ManagedModuleConfig loadExampleModuleConfig();
const char *managedLoaderKindName(ManagedLoaderKind loaderKind);
RegisterModulesFn resolveRegisterModules(const ManagedModuleConfig &config);
ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(
  const ManagedModuleConfig &config);

} // namespace expo::modules::dotnet
