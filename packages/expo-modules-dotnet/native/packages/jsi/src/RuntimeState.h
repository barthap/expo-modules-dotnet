#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>

#include "JsiRuntimeConnector.h"
#include "LongLivedObjectCollection.h"

namespace expo::dotnet {

// RuntimeHandle owns this shared state. The host owns the connector; this
// state only borrows it while Active or Closing, then clears the borrow during
// invalidation before the host may destroy the connector.
class RuntimeState final : public std::enable_shared_from_this<RuntimeState> {
public:
  static std::shared_ptr<RuntimeState> create(JsiRuntimeConnector &connector);

  jsi::Runtime &runtime();
  JsiRuntimeExecutor &executor();
  void drainDeferredReleases(jsi::Runtime &runtime) noexcept;
  void prepareForInvalidation();
  bool tryInvalidateWithoutRuntime() noexcept;
  void invalidateWithoutRuntime() noexcept;
  void releaseLongLivedObject(uint64_t id) noexcept;
  LongLivedObjectCollection &longLivedObjects() noexcept;
  bool isActive() const noexcept;
  bool isValid() const noexcept;
  void noteArrayBufferReleased() noexcept;
  void noteArrayBufferAbandoned() noexcept;
  uint32_t arrayBuffersReleased() const noexcept;
  uint32_t arrayBuffersAbandoned() const noexcept;
  void resetArrayBufferCounters() noexcept;

private:
  enum class State { Active, Closing, Invalid };

  explicit RuntimeState(JsiRuntimeConnector &connector);

  mutable std::mutex mutex_;
  JsiRuntimeConnector *connector_;
  std::atomic<State> state_{State::Active};
  LongLivedObjectCollection longLivedObjects_;
  std::atomic<uint32_t> released_{0};
  std::atomic<uint32_t> abandoned_{0};
};

} // namespace expo::dotnet
