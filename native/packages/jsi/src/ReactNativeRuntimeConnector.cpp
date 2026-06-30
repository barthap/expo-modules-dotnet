#include "ReactNativeRuntimeConnector.h"

#include <stdexcept>
#include <utility>

namespace expo::jsi {

ReactNativeRuntimeExecutor::ReactNativeRuntimeExecutor(
  facebook::jsi::Runtime &runtime, std::shared_ptr<facebook::react::CallInvoker> callInvoker)
  : runtime_(&runtime),
    callInvoker_(std::move(callInvoker))
{
}

void ReactNativeRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority, std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  try {
    if (runtime_ == nullptr || callInvoker_ == nullptr) {
      return;
    }

    callInvoker_->invokeAsync(
      [work = std::move(work)](facebook::jsi::Runtime &runtime) mutable { work(runtime); });
  } catch (...) {
    // JsiRuntimeExecutor::executeAsync is noexcept. Dropping the work releases
    // any captured managed task context through the existing ABI wrapper.
  }
}

bool ReactNativeRuntimeExecutor::canExecuteSync() const noexcept
{
  return runtime_ != nullptr && callInvoker_ != nullptr;
}

void ReactNativeRuntimeExecutor::executeSync(std::function<void(facebook::jsi::Runtime &)> work)
{
  if (runtime_ == nullptr) {
    throw std::runtime_error("React Native runtime is invalid.");
  }

  if (callInvoker_ == nullptr) {
    throw std::runtime_error("React Native runtime does not support synchronous dispatch.");
  }

  callInvoker_->invokeSync(
    [work = std::move(work)](facebook::jsi::Runtime &runtime) mutable { work(runtime); });
}

void ReactNativeRuntimeExecutor::drain()
{
  // React Native owns the production runtime queue. Test-only draining remains
  // implemented by the headless Hermes connector.
}

void ReactNativeRuntimeExecutor::invalidate() noexcept
{
  runtime_ = nullptr;
  callInvoker_.reset();
}

ReactNativeRuntimeConnector::ReactNativeRuntimeConnector(
  facebook::jsi::Runtime &runtime, std::shared_ptr<facebook::react::CallInvoker> callInvoker)
  : runtime_(&runtime),
    runtimeExecutor_(runtime, std::move(callInvoker))
{
}

facebook::jsi::Runtime &ReactNativeRuntimeConnector::runtime()
{
  if (runtime_ == nullptr) {
    throw std::runtime_error("React Native runtime is invalid.");
  }
  return *runtime_;
}

JsiRuntimeExecutor &ReactNativeRuntimeConnector::runtimeExecutor()
{
  return runtimeExecutor_;
}

bool ReactNativeRuntimeConnector::isRuntimeValid() const
{
  return runtime_ != nullptr;
}

void ReactNativeRuntimeConnector::invalidate()
{
  runtime_ = nullptr;
  runtimeExecutor_.invalidate();
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

} // namespace expo::jsi
