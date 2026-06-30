#import <Foundation/Foundation.h>

#include <dlfcn.h>
#include <memory>
#include <react/runtime/JSRuntimeFactory.h>
#include <react/runtime/JSRuntimeFactoryCAPI.h>

#include "ReactNativeRuntimeConnector.h"

namespace {

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);

RegisterModulesFn resolveRegisterModules()
{
  auto *symbol = dlsym(RTLD_DEFAULT, "expo_mobile_v2_register_modules");
  if (symbol == nullptr) {
    NSLog(@"[ExpoCSharpV2] Failed to resolve expo_mobile_v2_register_modules: %s", dlerror());
    return nullptr;
  }
  return reinterpret_cast<RegisterModulesFn>(symbol);
}

class ExpoCSharpV2JSRuntime final : public facebook::react::JSRuntime {
public:
  explicit ExpoCSharpV2JSRuntime(std::unique_ptr<facebook::react::JSRuntime> inner)
    : inner_(std::move(inner))
  {
  }

  ~ExpoCSharpV2JSRuntime() override
  {
    if (runtimeHandle_ != nullptr) {
      expo::jsi::releaseReactNativeRuntimeHandle(runtimeHandle_);
    }
    if (connector_ != nullptr) {
      connector_->invalidate();
    }
  }

  facebook::jsi::Runtime &getRuntime() noexcept override
  {
    return inner_->getRuntime();
  }

  facebook::react::jsinspector_modern::RuntimeTargetDelegate &getRuntimeTargetDelegate() override
  {
    return inner_->getRuntimeTargetDelegate();
  }

  void unstable_initializeOnJsThread() override
  {
    inner_->unstable_initializeOnJsThread();
    installModuleLoader();
  }

private:
  bool registerModules()
  {
    if (registered_) {
      return true;
    }

    auto registerModules = resolveRegisterModules();
    if (registerModules == nullptr) {
      return false;
    }

    auto status = registerModules(expo::jsi::reactNativeExpoJsiApi(), runtimeHandle_);
    if (status != 0) {
      NSLog(@"[ExpoCSharpV2] NativeAOT ExpoCSharpV2.add registration failed.");
      return false;
    }

    NSLog(@"[ExpoCSharpV2] NativeAOT ExpoCSharpV2.add module registered.");
    registered_ = true;
    return true;
  }

  void installModuleLoader()
  {
    if (loaderInstalled_) {
      return;
    }

    expo::jsi::ReactNativeRuntimeExecutor::Options options;
    options.isRuntimeThread = [] { return true; };
    options.supportsSyncDispatch = true;

    connector_ =
      std::make_unique<expo::jsi::ReactNativeRuntimeConnector>(inner_->getRuntime(), std::move(options));
    runtimeHandle_ = expo::jsi::createReactNativeRuntimeHandle(*connector_);

    auto &runtime = inner_->getRuntime();
    auto installer = facebook::jsi::Function::createFromHostFunction(
      runtime,
      facebook::jsi::PropNameID::forAscii(runtime, "__expoCSharpV2InstallModules"),
      0,
      [this](
        facebook::jsi::Runtime &,
        const facebook::jsi::Value &,
        const facebook::jsi::Value *,
        size_t) -> facebook::jsi::Value {
        return registerModules();
      });
    runtime.global().setProperty(runtime, "__expoCSharpV2InstallModules", std::move(installer));
    NSLog(@"[ExpoCSharpV2] NativeAOT ExpoCSharpV2.add loader installed.");
    loaderInstalled_ = true;
  }

  std::unique_ptr<facebook::react::JSRuntime> inner_;
  std::unique_ptr<expo::jsi::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  bool registered_ = false;
  bool loaderInstalled_ = false;
};

class ExpoCSharpV2JSRuntimeFactory final : public facebook::react::JSRuntimeFactory {
public:
  explicit ExpoCSharpV2JSRuntimeFactory(facebook::react::JSRuntimeFactory *inner)
    : inner_(inner)
  {
  }

  std::unique_ptr<facebook::react::JSRuntime> createJSRuntime(
    std::shared_ptr<facebook::react::MessageQueueThread> msgQueueThread) noexcept override
  {
    auto runtime = inner_->createJSRuntime(std::move(msgQueueThread));
    return std::make_unique<ExpoCSharpV2JSRuntime>(std::move(runtime));
  }

private:
  std::unique_ptr<facebook::react::JSRuntimeFactory> inner_;
};

} // namespace

extern "C" JSRuntimeFactoryRef ExpoCSharpV2CreateJSRuntimeFactory(JSRuntimeFactoryRef factory)
{
  auto *inner = reinterpret_cast<facebook::react::JSRuntimeFactory *>(factory);
  return reinterpret_cast<JSRuntimeFactoryRef>(new ExpoCSharpV2JSRuntimeFactory(inner));
}
