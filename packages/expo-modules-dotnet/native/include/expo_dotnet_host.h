#pragma once

// Single source of truth for the runtime-context ABI shared between the native
// hosts and the generated managed entry points. Every host crosses these types
// through a function pointer, so they must never be redeclared per platform.

#include <stdint.h>

#include "expo_jsi.h"

namespace expo::modules::dotnet {

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

} // namespace expo::modules::dotnet
