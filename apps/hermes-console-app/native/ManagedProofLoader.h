#pragma once

#include <expo_jsi.h>

#if EXPO_JSI_USE_HOSTFXR
#include <coreclr_delegates.h>
#define EXPO_JSI_MANAGED_CALLTYPE CORECLR_DELEGATE_CALLTYPE
#else
#define EXPO_JSI_MANAGED_CALLTYPE
#endif

namespace expo::dotnet::experiments {

using run_proof_fn = int(EXPO_JSI_MANAGED_CALLTYPE *)(const expo_jsi_api *,
                                                      expo_jsi_runtime_handle);
using create_session_fn = void *(EXPO_JSI_MANAGED_CALLTYPE *)(const expo_jsi_api *,
                                                              expo_jsi_runtime_handle);
using teardown_session_fn = void(EXPO_JSI_MANAGED_CALLTYPE *)(void *);

struct ManagedEntryPoints {
  run_proof_fn run_proof;
  create_session_fn create_session;
  teardown_session_fn teardown_session;
};

ManagedEntryPoints loadManagedEntryPoints();

} // namespace expo::dotnet::experiments
