#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <memory>
#include <vector>

#include "ManagedLoader.h"
#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
@end

namespace {

class InstalledRuntime final {
public:
  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle,
                   expo::modules::dotnet::ManagedModuleConfig moduleConfig)
    : connector_(std::move(connector)),
      runtimeHandle_(runtimeHandle),
      moduleConfig_(std::move(moduleConfig))
  {
  }

  ~InstalledRuntime()
  {
    if (runtimeHandle_ != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle_);
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

    auto registerModules = expo::modules::dotnet::resolveRegisterModules(moduleConfig_);
    if (registerModules == nullptr) {
      return false;
    }

    auto status = registerModules(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
    if (status != 0) {
      NSLog(@"[ExpoModulesDotnet] %s ExampleModule.add registration failed.",
            expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
      return false;
    }

    NSLog(@"[ExpoModulesDotnet] %s ExampleModule.add module registered.",
          expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
    registered_ = true;
    return true;
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig_;
  bool registered_ = false;
};

class ExpoModulesDotnetInstallerTurboModule final : public facebook::react::TurboModule {
public:
  explicit ExpoModulesDotnetInstallerTurboModule(
    const facebook::react::ObjCTurboModule::InitParams &params)
    : facebook::react::TurboModule(params.moduleName, params.jsInvoker)
    , installer_(static_cast<id<ExpoModulesDotnetInstalling>>(params.instance))
  {
    methodMap_["installModules"] = MethodMetadata{
      .argCount = 0,
      .invoker = ExpoModulesDotnetInstallerTurboModule::installModules,
    };
  }

private:
  static facebook::jsi::Value installModules(facebook::jsi::Runtime &runtime,
                                             facebook::react::TurboModule &turboModule,
                                             const facebook::jsi::Value *,
                                             size_t)
  {
    auto &installerTurboModule =
      static_cast<ExpoModulesDotnetInstallerTurboModule &>(turboModule);
    return facebook::jsi::Value([installerTurboModule.installer_ installModules]);
  }

  id<ExpoModulesDotnetInstalling> installer_;
};

} // namespace

@interface ExpoModulesDotnetInstaller
  : NSObject <RCTBridgeModule, RCTTurboModuleWithJSIBindings, ExpoModulesDotnetInstalling>
@end

@implementation ExpoModulesDotnetInstaller {
  // The install records own the connector state, not the RN runtime. Releasing
  // this vector invalidates the borrowed runtime holder before the managed ABI
  // handle is released.
  std::vector<std::shared_ptr<InstalledRuntime>> _installedRuntimes;
}

RCT_EXPORT_MODULE()

- (std::shared_ptr<facebook::react::TurboModule>)getTurboModule:
  (const facebook::react::ObjCTurboModule::InitParams &)params
{
  return std::make_shared<ExpoModulesDotnetInstallerTurboModule>(params);
}

- (void)installJSIBindingsWithRuntime:(facebook::jsi::Runtime &)runtime
                          callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker
{
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
  auto moduleConfig = expo::modules::dotnet::loadExampleModuleConfig();
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle, std::move(moduleConfig));

  _installedRuntimes.push_back(std::move(installedRuntime));
}

- (BOOL)installModules
{
  BOOL installed = NO;
  for (const auto &installedRuntime : _installedRuntimes) {
    installed = installedRuntime->registerModules() || installed;
  }

  if (!installed) {
    NSLog(@"[ExpoModulesDotnet] macOS module runtime is not ready.");
  }

  return installed;
}

@end
