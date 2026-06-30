#pragma once

#include <expo_jsi.h>

#if EXPO_JSI_USE_HOSTFXR
#include <coreclr_delegates.h>
#define EXPO_JSI_MANAGED_CALLTYPE CORECLR_DELEGATE_CALLTYPE
#else
#define EXPO_JSI_MANAGED_CALLTYPE
#endif

namespace expo::jsi::experiments {

using run_proof_fn = int(EXPO_JSI_MANAGED_CALLTYPE *)(const expo_jsi_api *,
                                                      expo_jsi_runtime_handle);
using register_modules_fn = int(EXPO_JSI_MANAGED_CALLTYPE *)(const expo_jsi_api *,
                                                             expo_jsi_runtime_handle);

struct ManagedEntryPoints {
  run_proof_fn run_proof;
  register_modules_fn register_modules;
};

ManagedEntryPoints loadManagedEntryPoints();

} // namespace expo::jsi::experiments
