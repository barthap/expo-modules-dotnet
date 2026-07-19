#pragma once

#include <cstdint>

#include <expo_jsi.h>

namespace expo::dotnet {

// Private testhost-only observability and validation hooks. Production hosts
// include ExpoJsiBridge.h instead and do not depend on these test controls.
void getRuntimeArrayBufferCounters(expo_jsi_runtime_handle runtime,
                                   uint32_t *released,
                                   uint32_t *abandoned) noexcept;
void resetRuntimeArrayBufferCounters(expo_jsi_runtime_handle runtime) noexcept;
expo_jsi_error validateArrayBufferSnapshotForTesting(uint8_t detached,
                                                     int32_t currentLength,
                                                     int32_t capturedLength) noexcept;
expo_jsi_error validateArrayBufferLengthForTesting(uint64_t length) noexcept;
void releaseRuntimeHandleAndGetArrayBufferCounters(expo_jsi_runtime_handle runtime,
                                                   uint32_t *released,
                                                   uint32_t *abandoned) noexcept;

} // namespace expo::dotnet
