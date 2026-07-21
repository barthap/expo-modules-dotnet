#pragma once

#include <memory>
#include <mutex>
#include <stdexcept>

#include <jsi/jsi.h>

#include "RuntimeState.h"

namespace expo::dotnet {

enum class PromiseSettlementState { Active, Settling, Settled };
enum class PromiseCleanupState { None, ReleasePending, AbandonPending, Terminal };

class PromiseEntry final : public LongLivedObject {
public:
  PromiseEntry(std::shared_ptr<RuntimeState> state,
               std::unique_ptr<jsi::Object> promise,
               std::unique_ptr<jsi::Function> resolve,
               std::unique_ptr<jsi::Function> reject)
    : state_(std::move(state)),
      promise_(std::move(promise)),
      resolve_(std::move(resolve)),
      reject_(std::move(reject))
  {
  }
  jsi::Value promiseValue(jsi::Runtime &runtime)
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (promise_ == nullptr || cleanup_ == PromiseCleanupState::Terminal)
      throw std::runtime_error("Promise storage is no longer valid.");
    return jsi::Value(runtime, *promise_);
  }
  void resolve(jsi::Runtime &runtime, const jsi::Value &value)
  {
    settle(runtime, value, false);
  }
  void reject(jsi::Runtime &runtime, const jsi::Value &value)
  {
    settle(runtime, value, true);
  }
  void release(jsi::Runtime &) noexcept override
  {
    std::unique_ptr<jsi::Object> promise;
    std::unique_ptr<jsi::Function> resolve;
    std::unique_ptr<jsi::Function> reject;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (cleanup_ != PromiseCleanupState::None)
        return;
      if (settlement_ == PromiseSettlementState::Settling) {
        cleanup_ = PromiseCleanupState::ReleasePending;
        return;
      }
      cleanup_ = PromiseCleanupState::Terminal;
      promise = std::move(promise_);
      resolve = std::move(resolve_);
      reject = std::move(reject_);
    }
    state_->notePromiseReleased();
  }
  void abandon() noexcept override
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (cleanup_ != PromiseCleanupState::None)
      return;
    cleanup_ = PromiseCleanupState::Terminal;
    (void)promise_.release();
    (void)resolve_.release();
    (void)reject_.release();
    state_->notePromiseAbandoned();
  }

private:
  void settle(jsi::Runtime &runtime, const jsi::Value &value, bool reject)
  {
    jsi::Function *resolver = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (cleanup_ != PromiseCleanupState::None || settlement_ != PromiseSettlementState::Active)
        return;
      resolver = reject ? reject_.get() : resolve_.get();
      if (resolver == nullptr)
        return;
      settlement_ = PromiseSettlementState::Settling;
    }
    try {
      resolver->call(runtime, value);
    } catch (...) {
      finishSettlement(false);
      throw;
    }
    finishSettlement(true);
  }
  void finishSettlement(bool succeeded)
  {
    std::unique_ptr<jsi::Object> promise;
    std::unique_ptr<jsi::Function> resolve;
    std::unique_ptr<jsi::Function> reject;
    bool released = false;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (cleanup_ == PromiseCleanupState::ReleasePending) {
        cleanup_ = PromiseCleanupState::Terminal;
        promise = std::move(promise_);
        resolve = std::move(resolve_);
        reject = std::move(reject_);
        released = true;
      } else if (succeeded) {
        settlement_ = PromiseSettlementState::Settled;
        resolve_.reset();
        reject_.reset();
      } else {
        settlement_ = PromiseSettlementState::Active;
      }
    }
    if (released)
      state_->notePromiseReleased();
  }
  std::shared_ptr<RuntimeState> state_;
  std::mutex mutex_;
  std::unique_ptr<jsi::Object> promise_;
  std::unique_ptr<jsi::Function> resolve_;
  std::unique_ptr<jsi::Function> reject_;
  PromiseSettlementState settlement_ = PromiseSettlementState::Active;
  PromiseCleanupState cleanup_ = PromiseCleanupState::None;
};
class PromiseHandle final {
public:
  PromiseHandle(std::shared_ptr<RuntimeState> state,
                std::shared_ptr<PromiseEntry> entry,
                uint64_t entryId)
    : state_(std::move(state)),
      entry_(std::move(entry)),
      entryId_(entryId)
  {
  }
  ~PromiseHandle()
  {
    if (state_ != nullptr)
      state_->releaseLongLivedObject(entryId_);
  }
  std::shared_ptr<PromiseEntry> entry() const noexcept
  {
    return entry_;
  }

private:
  std::shared_ptr<RuntimeState> state_;
  std::shared_ptr<PromiseEntry> entry_;
  uint64_t entryId_;
};
} // namespace expo::dotnet
