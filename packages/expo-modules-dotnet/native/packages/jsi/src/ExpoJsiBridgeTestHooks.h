#pragma once

#include <cstdint>

#include <expo_jsi.h>

namespace expo::dotnet {

// Private testhost-only observability and validation hooks. Production hosts
// include ExpoJsiBridge.h instead and do not depend on these test controls.
struct RuntimeLongLivedCounters {
  uint32_t arrayBuffersReleased = 0;
  uint32_t arrayBuffersAbandoned = 0;
  uint32_t weakObjectsReleased = 0;
  uint32_t weakObjectsAbandoned = 0;
  uint32_t promisesReleased = 0;
  uint32_t promisesAbandoned = 0;
  uint32_t remaining = 0;
};

RuntimeLongLivedCounters getRuntimeLongLivedCounters(expo_jsi_runtime_handle runtime) noexcept;
void resetRuntimeLongLivedCounters(expo_jsi_runtime_handle runtime) noexcept;
expo_jsi_error validateArrayBufferSnapshotForTesting(uint8_t detached,
                                                     int32_t currentLength,
                                                     int32_t capturedLength) noexcept;
expo_jsi_error validateArrayBufferLengthForTesting(uint64_t length) noexcept;
RuntimeLongLivedCounters releaseRuntimeHandleAndGetLongLivedCounters(
  expo_jsi_runtime_handle runtime) noexcept;
void failNextPromiseHandleAllocationForTesting() noexcept;
void pauseNextPromiseRegistrationForTesting(expo_jsi_runtime_handle runtime) noexcept;
bool waitUntilPromiseRegistrationPausedForTesting(expo_jsi_runtime_handle runtime) noexcept;
void resumePromiseRegistrationForTesting(expo_jsi_runtime_handle runtime) noexcept;
void invalidateRuntimeStateWithoutDeletingHandleForTesting(
  expo_jsi_runtime_handle runtime) noexcept;

} // namespace expo::dotnet
