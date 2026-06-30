#include "ReactNativeRuntimeConnector.h"

#include <stdexcept>
#include <utility>

namespace expo::jsi {

ReactNativeRuntimeExecutor::ReactNativeRuntimeExecutor(facebook::jsi::Runtime &runtime,
                                                       Options options)
  : runtime_(&runtime),
    options_(std::move(options))
{
}

#if EXPO_JSI_HAS_REACT_NATIVE_CALL_INVOKER
ReactNativeRuntimeExecutor::Options ReactNativeRuntimeExecutor::fromCallInvoker(
  std::shared_ptr<facebook::react::CallInvoker> callInvoker, RuntimeThreadPredicate isRuntimeThread)
{
  Options options;
  options.dispatchAsync = [callInvoker = std::move(callInvoker)](Work work) {
    if (callInvoker != nullptr) {
      callInvoker->invokeAsync(std::move(work));
    }
  };
  options.isRuntimeThread = std::move(isRuntimeThread);
  return options;
}
#endif

void ReactNativeRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority, std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  try {
    if (runtime_ == nullptr) {
      return;
    }

    auto run = [runtime = runtime_, work = std::move(work)]() mutable {
      if (runtime != nullptr) {
        work(*runtime);
      }
    };

    if (options_.dispatchAsync) {
      options_.dispatchAsync(std::move(run));
      return;
    }

    if (isOnRuntimeThread()) {
      run();
    }
  } catch (...) {
    // JsiRuntimeExecutor::executeAsync is noexcept. Dropping the work releases
    // any captured managed task context through the existing ABI wrapper.
  }
}

bool ReactNativeRuntimeExecutor::canExecuteSync() const noexcept
{
  return runtime_ != nullptr && (isOnRuntimeThread() || options_.supportsSyncDispatch);
}

void ReactNativeRuntimeExecutor::executeSync(std::function<void(facebook::jsi::Runtime &)> work)
{
  if (runtime_ == nullptr) {
    throw std::runtime_error("React Native runtime is invalid.");
  }

  if (isOnRuntimeThread()) {
    work(*runtime_);
    return;
  }

  if (!options_.supportsSyncDispatch || !options_.dispatchSync) {
    throw std::runtime_error("React Native runtime does not support synchronous dispatch.");
  }

  options_.dispatchSync([runtime = runtime_, work = std::move(work)]() mutable {
    if (runtime == nullptr) {
      throw std::runtime_error("React Native runtime is invalid.");
    }
    work(*runtime);
  });
}

void ReactNativeRuntimeExecutor::drain()
{
  // React Native owns the production runtime queue. Test-only draining remains
  // implemented by the headless Hermes connector.
}

bool ReactNativeRuntimeExecutor::isOnRuntimeThread() const noexcept
{
  if (!options_.isRuntimeThread) {
    return false;
  }

  try {
    return options_.isRuntimeThread();
  } catch (...) {
    return false;
  }
}

ReactNativeRuntimeConnector::ReactNativeRuntimeConnector(
  facebook::jsi::Runtime &runtime, ReactNativeRuntimeExecutor::Options options)
  : runtime_(&runtime),
    runtimeExecutor_(runtime, std::move(options))
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
