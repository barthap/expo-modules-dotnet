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
};

ManagedModuleConfig loadManagedHostConfig();
const wchar_t *managedLoaderKindName(ManagedLoaderKind loaderKind);
ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(
  const ManagedModuleConfig &config);
std::wstring managedLoaderLastError();

} // namespace expo::modules::dotnet
