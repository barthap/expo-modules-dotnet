#include "LongLivedObjectCollection.h"

#include <stdexcept>

#include "RuntimeState.h"

namespace expo::dotnet {

void LongLivedObjectCollection::setRuntimeState(std::weak_ptr<RuntimeState> state) noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  state_ = std::move(state);
}

uint64_t LongLivedObjectCollection::add(std::shared_ptr<LongLivedObject> object)
{
  if (object == nullptr) {
    throw std::invalid_argument("Long-lived object is null.");
  }
  std::lock_guard<std::mutex> lock(mutex_);
  const auto id = nextId_++;
  entries_.emplace(id, Entry{std::move(object), LongLivedEntryState::Active});
  return id;
}

void LongLivedObjectCollection::requestRelease(uint64_t id, JsiRuntimeExecutor &executor) noexcept
{
  std::shared_ptr<RuntimeState> state;
  {
    std::lock_guard<std::mutex> lock(mutex_);
    auto it = entries_.find(id);
    if (it == entries_.end() || it->second.state != LongLivedEntryState::Active) {
      return;
    }
    it->second.state = LongLivedEntryState::ReleaseQueued;
    state = state_.lock();
  }
  if (state == nullptr) {
    deferRelease(id);
    return;
  }

  try {
    auto token = std::make_shared<ScheduledReleaseToken>(state, id);
    executor.executeAsync(JsiRuntimeTaskPriority::Normal,
                          [token](jsi::Runtime &runtime) { token->run(runtime); });
  } catch (...) {
    deferRelease(id);
  }
}

void LongLivedObjectCollection::completeRelease(uint64_t id, jsi::Runtime &runtime) noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = entries_.find(id);
  if (it == entries_.end()) {
    return;
  }
  if (it->second.state == LongLivedEntryState::Released ||
      it->second.state == LongLivedEntryState::Invalidated) {
    return;
  }
  it->second.state = LongLivedEntryState::Released;
  if (it->second.object != nullptr) {
    it->second.object->release(runtime);
  }
  entries_.erase(it);
}

void LongLivedObjectCollection::deferRelease(uint64_t id) noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  auto it = entries_.find(id);
  if (it != entries_.end() && it->second.state == LongLivedEntryState::ReleaseQueued) {
    it->second.state = LongLivedEntryState::ReleaseDeferred;
  }
}

void LongLivedObjectCollection::drainDeferredReleases(jsi::Runtime &runtime) noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  for (auto it = entries_.begin(); it != entries_.end();) {
    if (it->second.state == LongLivedEntryState::ReleaseQueued ||
        it->second.state == LongLivedEntryState::ReleaseDeferred) {
      it->second.state = LongLivedEntryState::Released;
      if (it->second.object != nullptr) {
        it->second.object->release(runtime);
      }
      it = entries_.erase(it);
    } else {
      ++it;
    }
  }
}

void LongLivedObjectCollection::sweep(jsi::Runtime &runtime) noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  for (auto it = entries_.begin(); it != entries_.end();) {
    if (it->second.state != LongLivedEntryState::Released &&
        it->second.state != LongLivedEntryState::Invalidated) {
      it->second.state = LongLivedEntryState::Released;
      if (it->second.object != nullptr) {
        it->second.object->release(runtime);
      }
      it = entries_.erase(it);
    } else {
      ++it;
    }
  }
}

void LongLivedObjectCollection::invalidateWithoutRuntime() noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  for (auto &[id, entry] : entries_) {
    if (entry.state != LongLivedEntryState::Released &&
        entry.state != LongLivedEntryState::Invalidated) {
      entry.state = LongLivedEntryState::Invalidated;
      if (entry.object != nullptr) {
        entry.object->abandon();
      }
    }
  }
  entries_.clear();
}

bool LongLivedObjectCollection::empty() const noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  return entries_.empty();
}

uint32_t LongLivedObjectCollection::size() const noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  return static_cast<uint32_t>(entries_.size());
}

std::shared_ptr<RuntimeState> LongLivedObjectCollection::runtimeState() const noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  return state_.lock();
}

ScheduledReleaseToken::~ScheduledReleaseToken()
{
  if (!completed_.load(std::memory_order_acquire) && state_ != nullptr) {
    state_->longLivedObjects().deferRelease(id_);
  }
}

void ScheduledReleaseToken::run(jsi::Runtime &runtime) noexcept
{
  if (completed_.exchange(true, std::memory_order_acq_rel) || state_ == nullptr) {
    return;
  }
  if (state_->isValid()) {
    state_->longLivedObjects().completeRelease(id_, runtime);
  } else {
    state_->longLivedObjects().invalidateWithoutRuntime();
  }
}

} // namespace expo::dotnet
