#pragma once

#include <functional>
#include <memory>

#include "ExpoJsiBridge.h"
#include "JsiRuntimeConnector.h"

#include <ReactCommon/CallInvoker.h>

namespace expo::jsi {

class ReactNativeRuntimeConnector;

class ReactNativeRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  ReactNativeRuntimeExecutor(facebook::jsi::Runtime &runtime,
                             std::shared_ptr<facebook::react::CallInvoker> callInvoker);

  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(facebook::jsi::Runtime &)> work) noexcept override;
  bool canExecuteSync() const noexcept override;
  void executeSync(std::function<void(facebook::jsi::Runtime &)> work) override;
  void drain() override;
  void invalidate() noexcept;

private:
  facebook::jsi::Runtime *runtime_;
  std::shared_ptr<facebook::react::CallInvoker> callInvoker_;
};

class ReactNativeRuntimeConnector final : public JsiRuntimeConnector {
public:
  ReactNativeRuntimeConnector(facebook::jsi::Runtime &runtime,
                              std::shared_ptr<facebook::react::CallInvoker> callInvoker);
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
