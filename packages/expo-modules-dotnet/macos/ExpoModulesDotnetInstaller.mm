#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <memory>
#include <string>

#include "ManagedLoader.h"
#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
- (NSString *)getLastError;
- (BOOL)installModulesWithRuntime:(facebook::jsi::Runtime &)runtime
                       callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker;
@end

namespace {

std::string takeRuntimeContextError(expo::modules::dotnet::RuntimeContextError &error)
{
  std::string message;
  if (error.message != nullptr && error.messageLength > 0) {
    message.assign(error.message, static_cast<size_t>(error.messageLength));
  }
  if (error.release != nullptr) {
    error.release(error.releaseContext);
  }
  return message;
}

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
      lastError_ = expo::modules::dotnet::managedLoaderLastError();
      if (lastError_.empty()) {
        lastError_ =
          "Failed to resolve structured create/teardown runtime context entry points. Rebuild the "
          "managed ExpoDotnetHost artifacts with expo-modules-dotnet-autolinking.";
      }
      NSLog(@"[ExpoModulesDotnet] %s", lastError_.c_str());
      return false;
    }

    expo::modules::dotnet::RuntimeContextResult result;
    entryPoints.createRuntimeContext(
      expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_, &result);
    teardownRuntimeContext_ = entryPoints.teardownRuntimeContext;
    if (result.ok == 0 || result.runtimeContext == nullptr) {
      lastError_ = takeRuntimeContextError(result.error);
      if (lastError_.empty()) {
        lastError_ =
          std::string(expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind)) +
          " runtime context registration failed.";
      }
      NSLog(@"[ExpoModulesDotnet] %s", lastError_.c_str());
      return false;
    }

    managedRuntimeContext_ = result.runtimeContext;
    NSLog(@"[ExpoModulesDotnet] %s managed modules registered.",
          expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
    registered_ = true;
    lastError_.clear();
    return true;
  }

  std::string lastError() const
  {
    return lastError_;
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig_;
  void *managedRuntimeContext_ = nullptr;
  expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext_ = nullptr;
  std::string lastError_;
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
    methodMap_["getLastError"] = MethodMetadata{
      .argCount = 0,
      .invoker = ExpoModulesDotnetInstallerTurboModule::getLastError,
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

  static facebook::jsi::Value getLastError(facebook::jsi::Runtime &runtime,
                                           facebook::react::TurboModule &turboModule,
                                           const facebook::jsi::Value *,
                                           size_t)
  {
    auto &installerTurboModule =
      static_cast<ExpoModulesDotnetInstallerTurboModule &>(turboModule);
    NSString *lastError = [installerTurboModule.installer_ getLastError];
    return facebook::jsi::String::createFromUtf8(runtime, lastError.UTF8String);
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
  auto moduleConfig = expo::modules::dotnet::loadManagedHostConfig();
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

- (NSString *)getLastError
{
  if (_installedRuntime == nullptr) {
    return @"macOS module runtime is not ready.";
  }

  auto lastError = _installedRuntime->lastError();
  if (!lastError.empty()) {
    return @(lastError.c_str());
  }

  auto loaderError = expo::modules::dotnet::managedLoaderLastError();
  return loaderError.empty() ? @"" : @(loaderError.c_str());
}

- (void)invalidate
{
  _installedRuntime.reset();
}

@end
