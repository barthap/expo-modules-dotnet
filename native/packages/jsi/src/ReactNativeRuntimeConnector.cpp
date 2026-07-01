#include "ReactNativeRuntimeConnector.h"

#include <stdexcept>
#include <utility>

namespace expo::dotnet {

ReactNativeRuntimeState::ReactNativeRuntimeState(
  facebook::jsi::Runtime &runtime, std::shared_ptr<facebook::react::CallInvoker> callInvoker)
  : runtime_(&runtime),
    callInvoker_(std::move(callInvoker))
{
}

facebook::jsi::Runtime &ReactNativeRuntimeState::runtime() const
{
  if (runtime_ == nullptr) {
    throw std::runtime_error("React Native runtime is invalid.");
  }
  return *runtime_;
}

std::shared_ptr<facebook::react::CallInvoker> ReactNativeRuntimeState::callInvoker() const noexcept
{
  return callInvoker_;
}

bool ReactNativeRuntimeState::isRuntimeValid() const noexcept
{
  return runtime_ != nullptr;
}

void ReactNativeRuntimeState::invalidate() noexcept
{
  runtime_ = nullptr;
  callInvoker_.reset();
}

ReactNativeRuntimeExecutor::ReactNativeRuntimeExecutor(
  std::shared_ptr<ReactNativeRuntimeState> state)
  : state_(std::move(state))
{
}

void ReactNativeRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority, std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  try {
    if (state_ == nullptr || !state_->isRuntimeValid()) {
      return;
    }

    auto callInvoker = state_->callInvoker();
    if (callInvoker == nullptr) {
      return;
    }

    std::weak_ptr<ReactNativeRuntimeState> weakState = state_;
    callInvoker->invokeAsync(
      [weakState, work = std::move(work)](facebook::jsi::Runtime &runtime) mutable {
        auto state = weakState.lock();
        if (state == nullptr || !state->isRuntimeValid()) {
          return;
        }
        work(runtime);
      });
  } catch (...) {
    // JsiRuntimeExecutor::executeAsync is noexcept. Dropping the work releases
    // any captured managed task context through the existing ABI wrapper.
  }
}

bool ReactNativeRuntimeExecutor::canExecuteSync() const noexcept
{
  return state_ != nullptr && state_->isRuntimeValid() && state_->callInvoker() != nullptr;
}

void ReactNativeRuntimeExecutor::executeSync(std::function<void(facebook::jsi::Runtime &)> work)
{
  if (state_ == nullptr || !state_->isRuntimeValid()) {
    throw std::runtime_error("React Native runtime is invalid.");
  }

  auto callInvoker = state_->callInvoker();
  if (callInvoker == nullptr) {
    throw std::runtime_error("React Native runtime does not support synchronous dispatch.");
  }

  std::weak_ptr<ReactNativeRuntimeState> weakState = state_;
  callInvoker->invokeSync(
    [weakState, work = std::move(work)](facebook::jsi::Runtime &runtime) mutable {
      auto state = weakState.lock();
      if (state == nullptr || !state->isRuntimeValid()) {
        throw std::runtime_error("React Native runtime is invalid.");
      }
      work(runtime);
    });
}

void ReactNativeRuntimeExecutor::invalidate() noexcept
{
  if (state_ != nullptr) {
    state_->invalidate();
  }
}

ReactNativeRuntimeConnector::ReactNativeRuntimeConnector(
  facebook::jsi::Runtime &runtime, std::shared_ptr<facebook::react::CallInvoker> callInvoker)
  : state_(std::make_shared<ReactNativeRuntimeState>(runtime, std::move(callInvoker))),
    runtimeExecutor_(state_)
{
}

facebook::jsi::Runtime &ReactNativeRuntimeConnector::runtime()
{
  if (state_ == nullptr) {
    throw std::runtime_error("React Native runtime is invalid.");
  }
  return state_->runtime();
}

JsiRuntimeExecutor &ReactNativeRuntimeConnector::runtimeExecutor()
{
  return runtimeExecutor_;
}

bool ReactNativeRuntimeConnector::isRuntimeValid() const
{
  return state_ != nullptr && state_->isRuntimeValid();
}

void ReactNativeRuntimeConnector::invalidate()
{
  if (state_ != nullptr) {
    state_->invalidate();
  }
}

const expo_jsi_api *reactNativeExpoJsiApi() noexcept
{
  return api();
}

expo_jsi_runtime_handle createReactNativeRuntimeHandle(ReactNativeRuntimeConnector &connector)
{
  return createRuntimeHandle(connector);
}

void releaseReactNativeRuntimeHandle(expo_jsi_runtime_handle runtime)
{
  releaseRuntimeHandle(runtime);
}

} // namespace expo::dotnet
