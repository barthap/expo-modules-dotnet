#pragma once

#include <functional>
#include <memory>

#include "ExpoJsiBridge.h"
#include "JsiRuntimeConnector.h"

#include <ReactCommon/CallInvoker.h>

namespace expo::dotnet {

class ReactNativeRuntimeConnector;

class ReactNativeRuntimeState final {
public:
  ReactNativeRuntimeState(facebook::jsi::Runtime &runtime,
                          std::shared_ptr<facebook::react::CallInvoker> callInvoker);

  facebook::jsi::Runtime &runtime() const;
  std::shared_ptr<facebook::react::CallInvoker> callInvoker() const noexcept;
  bool isRuntimeValid() const noexcept;
  void invalidate() noexcept;

private:
  // React Native owns the runtime. This raw pointer is only the borrowed JSI
  // boundary value; lifetime is modeled by this holder and explicit
  // invalidation, not by shared ownership of facebook::jsi::Runtime.
  facebook::jsi::Runtime *runtime_;
  std::shared_ptr<facebook::react::CallInvoker> callInvoker_;
};

class ReactNativeRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  explicit ReactNativeRuntimeExecutor(std::shared_ptr<ReactNativeRuntimeState> state);

  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(facebook::jsi::Runtime &)> work) noexcept override;
  bool canExecuteSync() const noexcept override;
  void executeSync(std::function<void(facebook::jsi::Runtime &)> work) override;
  void invalidate() noexcept;

private:
  std::shared_ptr<ReactNativeRuntimeState> state_;
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
  std::shared_ptr<ReactNativeRuntimeState> state_;
  ReactNativeRuntimeExecutor runtimeExecutor_;
};

const expo_jsi_api *reactNativeExpoJsiApi() noexcept;
expo_jsi_runtime_handle createReactNativeRuntimeHandle(ReactNativeRuntimeConnector &connector);
void releaseReactNativeRuntimeHandle(expo_jsi_runtime_handle runtime);

} // namespace expo::dotnet
