#include "RuntimeState.h"

#include <stdexcept>

namespace expo::dotnet {

std::shared_ptr<RuntimeState> RuntimeState::create(JsiRuntimeConnector &connector)
{
  // The constructor is private so callers must establish the collection's
  // weak back-reference through this factory.
  auto state = std::shared_ptr<RuntimeState>(new RuntimeState(connector));
  state->longLivedObjects_.setRuntimeState(state);
  return state;
}

RuntimeState::RuntimeState(JsiRuntimeConnector &connector)
  : connector_(&connector)
{
}

jsi::Runtime &RuntimeState::runtime()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (state_.load(std::memory_order_acquire) != State::Active || connector_ == nullptr ||
      !connector_->isRuntimeValid()) {
    throw std::runtime_error("Runtime connector is invalid.");
  }
  return connector_->runtime();
}

JsiRuntimeExecutor &RuntimeState::executor()
{
  std::lock_guard<std::mutex> lock(mutex_);
  if (state_.load(std::memory_order_acquire) != State::Active || connector_ == nullptr ||
      !connector_->isRuntimeValid()) {
    throw std::runtime_error("Runtime connector is invalid.");
  }
  return connector_->runtimeExecutor();
}

void RuntimeState::drainDeferredReleases(jsi::Runtime &runtime) noexcept
{
  longLivedObjects_.drainDeferredReleases(runtime);
}

void RuntimeState::prepareForInvalidation()
{
  State expected = State::Active;
  if (!state_.compare_exchange_strong(expected, State::Closing, std::memory_order_acq_rel)) {
    return;
  }

  JsiRuntimeConnector *connector = nullptr;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    connector = connector_;
  }
  if (connector == nullptr || !connector->isRuntimeValid()) {
    invalidateWithoutRuntime();
    return;
  }

  try {
    connector->runtimeExecutor().executeSync(
      [this](jsi::Runtime &runtime) { longLivedObjects_.sweep(runtime); });
  } catch (...) {
    invalidateWithoutRuntime();
    return;
  }
  invalidateWithoutRuntime();
}

void RuntimeState::invalidateWithoutRuntime() noexcept
{
  State previous = state_.exchange(State::Invalid, std::memory_order_acq_rel);
  if (previous == State::Invalid) {
    return;
  }
  longLivedObjects_.invalidateWithoutRuntime();
  std::lock_guard<std::mutex> lock(mutex_);
  connector_ = nullptr;
}

void RuntimeState::releaseLongLivedObject(uint64_t id) noexcept
{
  bool shouldInvalidateWithoutRuntime = false;
  {
    // Keep the connector borrow protected through executeAsync. A concurrent
    // prepare waits on this mutex before it can sweep, invalidate, and let the
    // host destroy the connector.
    std::lock_guard<std::mutex> lock(mutex_);
    if (state_.load(std::memory_order_acquire) == State::Active) {
      try {
        if (connector_ != nullptr && connector_->isRuntimeValid()) {
          longLivedObjects_.requestRelease(id, connector_->runtimeExecutor());
          return;
        }
      } catch (...) {
        // Preserve the late-invalidation fallback below.
      }
      shouldInvalidateWithoutRuntime = true;
    }
  }

  if (shouldInvalidateWithoutRuntime && tryInvalidateWithoutRuntime()) {
    return;
  }
  if (isValid()) {
    longLivedObjects_.deferRelease(id);
  } else {
    longLivedObjects_.invalidateWithoutRuntime();
  }
}

bool RuntimeState::tryInvalidateWithoutRuntime() noexcept
{
  State expected = State::Active;
  if (!state_.compare_exchange_strong(expected, State::Invalid, std::memory_order_acq_rel)) {
    return false;
  }
  longLivedObjects_.invalidateWithoutRuntime();
  std::lock_guard<std::mutex> lock(mutex_);
  connector_ = nullptr;
  return true;
}

LongLivedObjectCollection &RuntimeState::longLivedObjects() noexcept
{
  return longLivedObjects_;
}

bool RuntimeState::isActive() const noexcept
{
  return state_.load(std::memory_order_acquire) == State::Active;
}

bool RuntimeState::isValid() const noexcept
{
  return state_.load(std::memory_order_acquire) != State::Invalid;
}

void RuntimeState::noteArrayBufferReleased() noexcept
{
  released_.fetch_add(1, std::memory_order_relaxed);
}

void RuntimeState::noteArrayBufferAbandoned() noexcept
{
  abandoned_.fetch_add(1, std::memory_order_relaxed);
}

uint32_t RuntimeState::arrayBuffersReleased() const noexcept
{
  return released_.load();
}

uint32_t RuntimeState::arrayBuffersAbandoned() const noexcept
{
  return abandoned_.load();
}

void RuntimeState::resetArrayBufferCounters() noexcept
{
  released_.store(0, std::memory_order_relaxed);
  abandoned_.store(0, std::memory_order_relaxed);
}

void RuntimeState::noteWeakObjectReleased() noexcept
{
  weakReleased_.fetch_add(1, std::memory_order_relaxed);
}

void RuntimeState::noteWeakObjectAbandoned() noexcept
{
  weakAbandoned_.fetch_add(1, std::memory_order_relaxed);
}

uint32_t RuntimeState::weakObjectsReleased() const noexcept
{
  return weakReleased_.load(std::memory_order_relaxed);
}

uint32_t RuntimeState::weakObjectsAbandoned() const noexcept
{
  return weakAbandoned_.load(std::memory_order_relaxed);
}

void RuntimeState::resetWeakObjectCounters() noexcept
{
  weakReleased_.store(0, std::memory_order_relaxed);
  weakAbandoned_.store(0, std::memory_order_relaxed);
}

uint32_t RuntimeState::longLivedObjectCount() const noexcept
{
  return longLivedObjects_.size();
}

} // namespace expo::dotnet
