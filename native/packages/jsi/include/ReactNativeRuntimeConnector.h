#pragma once

#include <functional>
#include <memory>

#include "ExpoJsiBridge.h"
#include "JsiRuntimeConnector.h"

#if __has_include(<ReactCommon/CallInvoker.h>)
#include <ReactCommon/CallInvoker.h>
#define EXPO_JSI_HAS_REACT_NATIVE_CALL_INVOKER 1
#else
#define EXPO_JSI_HAS_REACT_NATIVE_CALL_INVOKER 0
#endif

namespace expo::jsi {

class ReactNativeRuntimeConnector;

class ReactNativeRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  using Work = std::function<void()>;
  using AsyncDispatcher = std::function<void(Work)>;
  using SyncDispatcher = std::function<void(Work)>;
  using RuntimeThreadPredicate = std::function<bool()>;

  struct Options {
    AsyncDispatcher dispatchAsync;
    SyncDispatcher dispatchSync;
    RuntimeThreadPredicate isRuntimeThread;
    bool supportsSyncDispatch = false;
  };

#if EXPO_JSI_HAS_REACT_NATIVE_CALL_INVOKER
  static Options fromCallInvoker(std::shared_ptr<facebook::react::CallInvoker> callInvoker,
                                 RuntimeThreadPredicate isRuntimeThread = {});
#endif

  ReactNativeRuntimeExecutor(facebook::jsi::Runtime &runtime, Options options);

  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(facebook::jsi::Runtime &)> work) noexcept override;
  bool canExecuteSync() const noexcept override;
  void executeSync(std::function<void(facebook::jsi::Runtime &)> work) override;
  void drain() override;

private:
  bool isOnRuntimeThread() const noexcept;

  facebook::jsi::Runtime *runtime_;
  Options options_;
};

class ReactNativeRuntimeConnector final : public JsiRuntimeConnector {
public:
  ReactNativeRuntimeConnector(facebook::jsi::Runtime &runtime,
                              ReactNativeRuntimeExecutor::Options options);
  ~ReactNativeRuntimeConnector() override = default;

  facebook::jsi::Runtime &runtime() override;
  JsiRuntimeExecutor &runtimeExecutor() override;
  bool isRuntimeValid() const override;
  void invalidate() override;

private:
  facebook::jsi::Runtime *runtime_;
  ReactNativeRuntimeExecutor runtimeExecutor_;
};

const expo_jsi_api *reactNativeExpoJsiApi() noexcept;
expo_jsi_runtime_handle createReactNativeRuntimeHandle(ReactNativeRuntimeConnector &connector);
void releaseReactNativeRuntimeHandle(expo_jsi_runtime_handle runtime);

} // namespace expo::jsi
