#pragma once

#include <string>

#include "expo_jsi.h"

namespace expo::modules::dotnet {

enum class ManagedLoaderKind {
  HostFxr,
  NativeAot,
};

struct RuntimeContextError {
  const char *message = nullptr;
  int32_t messageLength = 0;
  void *releaseContext = nullptr;
  void (*release)(void *) = nullptr;
};

struct RuntimeContextResult {
  int32_t ok = 0;
  void *runtimeContext = nullptr;
  RuntimeContextError error;
};

using CreateRuntimeContextFn = void (*)(const expo_jsi_api *,
                                        expo_jsi_runtime_handle,
                                        RuntimeContextResult *);
using TeardownRuntimeContextFn = void (*)(void *);

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
