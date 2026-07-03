#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <memory>

#include "ManagedLoader.h"
#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
- (BOOL)installModulesWithRuntime:(facebook::jsi::Runtime &)runtime
                       callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker;
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
    if (connector_ != nullptr) {
      connector_->invalidate();
    }
    if (managedRuntimeContext_ != nullptr && teardownRuntimeContext_ != nullptr) {
      teardownRuntimeContext_(managedRuntimeContext_);
      managedRuntimeContext_ = nullptr;
    }
    if (runtimeHandle_ != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle_);
    }
  }

  bool registerModules()
  {
    if (registered_) {
      return true;
    }

    auto entryPoints = expo::modules::dotnet::resolveRuntimeContextEntryPoints(moduleConfig_);
    if (entryPoints.createRuntimeContext == nullptr || entryPoints.teardownRuntimeContext == nullptr) {
      NSLog(@"[ExpoModulesDotnet] Failed to resolve create/teardown runtime context entry points.");
      return false;
    }

    managedRuntimeContext_ =
      entryPoints.createRuntimeContext(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
    teardownRuntimeContext_ = entryPoints.teardownRuntimeContext;
    if (managedRuntimeContext_ == nullptr) {
      NSLog(@"[ExpoModulesDotnet] %s runtime context registration failed.",
            expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
      return false;
    }

    NSLog(@"[ExpoModulesDotnet] %s managed modules registered.",
          expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
    registered_ = true;
    return true;
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig_;
  void *managedRuntimeContext_ = nullptr;
  expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext_ = nullptr;
  bool registered_ = false;
};

class ExpoModulesDotnetInstallerTurboModule final : public facebook::react::TurboModule {
public:
  explicit ExpoModulesDotnetInstallerTurboModule(
    const facebook::react::ObjCTurboModule::InitParams &params)
    : facebook::react::TurboModule(params.moduleName, params.jsInvoker)
    , installer_(static_cast<id<ExpoModulesDotnetInstalling>>(params.instance))
    , jsInvoker_(params.jsInvoker)
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
    return facebook::jsi::Value(
      [installerTurboModule.installer_ installModulesWithRuntime:runtime
                                                     callInvoker:installerTurboModule.jsInvoker_]);
  }

  id<ExpoModulesDotnetInstalling> installer_;
  std::shared_ptr<facebook::react::CallInvoker> jsInvoker_;
};

} // namespace

@interface ExpoModulesDotnetInstaller
  : NSObject <RCTBridgeModule, RCTTurboModuleWithJSIBindings, ExpoModulesDotnetInstalling>
@end

@implementation ExpoModulesDotnetInstaller {
  // The install record owns connector state, not the RN runtime. Resetting it
  // invalidates the borrowed runtime holder before the managed ABI handle is
  // released.
  std::shared_ptr<InstalledRuntime> _installedRuntime;
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

  _installedRuntime = std::move(installedRuntime);
}

- (BOOL)installModulesWithRuntime:(facebook::jsi::Runtime &)runtime
                       callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker
{
  if (_installedRuntime == nullptr) {
    [self installJSIBindingsWithRuntime:runtime callInvoker:callInvoker];
  }

  return [self installModules];
}

- (BOOL)installModules
{
  if (_installedRuntime == nullptr) {
    NSLog(@"[ExpoModulesDotnet] macOS module runtime is not ready.");
    return NO;
  }

  return _installedRuntime->registerModules();
}

- (void)invalidate
{
  _installedRuntime.reset();
}

@end
