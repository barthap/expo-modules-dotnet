#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <dlfcn.h>
#include <memory>
#include <vector>

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

class InstalledRuntime final {
public:
  InstalledRuntime(std::unique_ptr<expo::jsi::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector_(std::move(connector)),
      runtimeHandle_(runtimeHandle)
  {
  }

  ~InstalledRuntime()
  {
    if (runtimeHandle_ != nullptr) {
      expo::jsi::releaseReactNativeRuntimeHandle(runtimeHandle_);
    }
    if (connector_ != nullptr) {
      connector_->invalidate();
    }
  }

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

private:
  std::unique_ptr<expo::jsi::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  bool registered_ = false;
};

class ExpoCSharpV2InstallerTurboModule final : public facebook::react::TurboModule {
public:
  explicit ExpoCSharpV2InstallerTurboModule(
    const facebook::react::ObjCTurboModule::InitParams &params)
    : facebook::react::TurboModule(params.moduleName, params.jsInvoker)
  {
  }
};

} // namespace

@interface ExpoCSharpV2Installer : NSObject <RCTBridgeModule, RCTTurboModuleWithJSIBindings>
@end

@implementation ExpoCSharpV2Installer {
  std::vector<std::shared_ptr<InstalledRuntime>> _installedRuntimes;
}

RCT_EXPORT_MODULE()

- (std::shared_ptr<facebook::react::TurboModule>)getTurboModule:
  (const facebook::react::ObjCTurboModule::InitParams &)params
{
  return std::make_shared<ExpoCSharpV2InstallerTurboModule>(params);
}

- (void)installJSIBindingsWithRuntime:(facebook::jsi::Runtime &)runtime
                          callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker
{
  auto connector = std::make_unique<expo::jsi::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::jsi::createReactNativeRuntimeHandle(*connector);
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  installedRuntime->registerModules();
  _installedRuntimes.push_back(std::move(installedRuntime));
}

@end
