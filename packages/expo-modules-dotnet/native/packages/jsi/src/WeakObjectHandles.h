#pragma once

#include <atomic>
#include <memory>
#include <optional>
#include <stdexcept>

#include "RuntimeState.h"
#include "WeakObjectCapabilities.h"

namespace expo::dotnet {

class WeakObjectEntry final : public LongLivedObject {
public:
  WeakObjectEntry(std::shared_ptr<RuntimeState> state,
                  std::unique_ptr<detail::WeakObjectPayload> payload)
    : state_(std::move(state)),
      payload_(std::move(payload))
  {
  }

  std::optional<jsi::Object> lock(jsi::Runtime &runtime)
  {
    if (!state_->isActive() || terminal_.load(std::memory_order_acquire) || payload_ == nullptr) {
      throw std::runtime_error("WeakObject storage is no longer valid.");
    }
    return detail::lockWeakObjectPayload(runtime, *payload_);
  }

  void release(jsi::Runtime &runtime) noexcept override
  {
    if (terminal_.exchange(true, std::memory_order_acq_rel)) {
      return;
    }
    detail::releaseWeakObjectPayloadOnRuntime(runtime, std::move(payload_));
    state_->noteWeakObjectReleased();
  }

  void abandon() noexcept override
  {
    if (terminal_.exchange(true, std::memory_order_acq_rel)) {
      return;
    }
    detail::abandonWeakObjectPayload(std::move(payload_));
    state_->noteWeakObjectAbandoned();
  }

private:
  std::shared_ptr<RuntimeState> state_;
  std::unique_ptr<detail::WeakObjectPayload> payload_;
  std::atomic<bool> terminal_{false};
};

class WeakObjectHandle final {
public:
  WeakObjectHandle(std::shared_ptr<RuntimeState> state,
                   std::shared_ptr<WeakObjectEntry> entry,
                   uint64_t entryId)
    : state_(std::move(state)),
      entry_(std::move(entry)),
      entryId_(entryId)
  {
  }

  ~WeakObjectHandle()
  {
    if (state_ != nullptr) {
      state_->releaseLongLivedObject(entryId_);
    }
  }

  std::shared_ptr<RuntimeState> state() const noexcept
  {
    return state_;
  }
  std::shared_ptr<WeakObjectEntry> entry() const noexcept
  {
    return entry_;
  }

private:
  std::shared_ptr<RuntimeState> state_;
  std::shared_ptr<WeakObjectEntry> entry_;
  uint64_t entryId_;
};

inline std::unique_ptr<WeakObjectHandle> createWeakObjectHandle(jsi::Runtime &runtime,
                                                                std::shared_ptr<RuntimeState> state,
                                                                jsi::Object object)
{
  auto payload = detail::createWeakObjectPayload(runtime, std::move(object));
  auto entry = std::make_shared<WeakObjectEntry>(state, std::move(payload));
  auto entryId = state->longLivedObjects().add(entry);
  return std::make_unique<WeakObjectHandle>(std::move(state), std::move(entry), entryId);
}

} // namespace expo::dotnet
