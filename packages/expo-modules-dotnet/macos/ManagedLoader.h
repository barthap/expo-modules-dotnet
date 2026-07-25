#pragma once

#include <string>

#include "expo_dotnet_host.h"
#include "expo_jsi.h"

namespace expo::modules::dotnet {

enum class ManagedLoaderKind {
  HostFxr,
  NativeAot,
};

struct ManagedRuntimeContextEntryPoints {
  CreateRuntimeContextV2Fn createRuntimeContextV2 = nullptr;
  TeardownRuntimeContextFn teardownRuntimeContext = nullptr;
};

struct ManagedModuleConfig {
  ManagedLoaderKind loaderKind = ManagedLoaderKind::HostFxr;
  std::string assemblyPath;
  std::string runtimeConfigPath;
  std::string nethostPath;
  std::string nativeLibraryPath;
  std::string typeName;
};

ManagedModuleConfig loadManagedHostConfig();
const char *managedLoaderKindName(ManagedLoaderKind loaderKind);
ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(
  const ManagedModuleConfig &config);
std::string managedLoaderLastError();

} // namespace expo::modules::dotnet
