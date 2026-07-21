#pragma once

#include <atomic>
#include <cstdint>
#include <memory>
#include <mutex>
#include <unordered_map>

#include "JsiRuntimeConnector.h"

namespace expo::dotnet {

namespace jsi = facebook::jsi;

class RuntimeState;

class LongLivedObject {
public:
  virtual ~LongLivedObject() = default;
  virtual void release(jsi::Runtime &runtime) noexcept = 0;
  virtual void abandon() noexcept = 0;
};

enum class LongLivedEntryState {
  Active,
  ReleaseQueued,
  ReleaseDeferred,
  Released,
  Invalidated,
};

// RuntimeState owns this collection. It owns each entry until release or
// abandonment, then erases the entry to break any entry-to-RuntimeState cycle.
class LongLivedObjectCollection final {
public:
  LongLivedObjectCollection() = default;

  void setRuntimeState(std::weak_ptr<RuntimeState> state) noexcept;
  uint64_t add(std::shared_ptr<LongLivedObject> object);

  void requestRelease(uint64_t id, JsiRuntimeExecutor &executor) noexcept;
  void completeRelease(uint64_t id, jsi::Runtime &runtime) noexcept;
  void deferRelease(uint64_t id) noexcept;
  void drainDeferredReleases(jsi::Runtime &runtime) noexcept;
  void sweep(jsi::Runtime &runtime) noexcept;
  void invalidateWithoutRuntime() noexcept;
  bool empty() const noexcept;
  uint32_t size() const noexcept;

private:
  struct Entry {
    std::shared_ptr<LongLivedObject> object;
    LongLivedEntryState state;
  };

  std::shared_ptr<RuntimeState> runtimeState() const noexcept;

  mutable std::mutex mutex_;
  std::weak_ptr<RuntimeState> state_;
  std::unordered_map<uint64_t, Entry> entries_;
  uint64_t nextId_ = 1;
};

class ScheduledReleaseToken final {
public:
  ScheduledReleaseToken(std::shared_ptr<RuntimeState> state, uint64_t id)
    : state_(std::move(state)),
      id_(id)
  {
  }

  ~ScheduledReleaseToken();

  void run(jsi::Runtime &runtime) noexcept;

private:
  std::shared_ptr<RuntimeState> state_;
  uint64_t id_;
  std::atomic<bool> completed_{false};
};

} // namespace expo::dotnet
