#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <memory>
#include <mutex>
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
    std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
    expo_jsi_runtime_handle runtimeHandle = nullptr;
    void *managedRuntimeContext = nullptr;
    expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext = nullptr;

    {
      std::lock_guard<std::mutex> lock(mutex_);
      connector = std::move(connector_);
      runtimeHandle = runtimeHandle_;
      runtimeHandle_ = nullptr;
      managedRuntimeContext = managedRuntimeContext_;
      managedRuntimeContext_ = nullptr;
      teardownRuntimeContext = teardownRuntimeContext_;
      teardownRuntimeContext_ = nullptr;
      lastError_.clear();
      registered_ = false;
      registrationInProgress_ = false;
    }

    if (connector != nullptr) {
      connector->invalidate();
    }
    if (managedRuntimeContext != nullptr && teardownRuntimeContext != nullptr) {
      teardownRuntimeContext(managedRuntimeContext);
    }
    if (runtimeHandle != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle);
    }
  }

  bool registerModules()
  {
    expo_jsi_runtime_handle runtimeHandle = nullptr;
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (registered_) {
        return true;
      }
      if (registrationInProgress_) {
        lastError_ = "Module registration is already in progress.";
        return false;
      }
      registrationInProgress_ = true;
      runtimeHandle = runtimeHandle_;
    }

    try {
      auto entryPoints = expo::modules::dotnet::resolveRuntimeContextEntryPoints(moduleConfig_);
      if (entryPoints.createRuntimeContext == nullptr ||
          entryPoints.teardownRuntimeContext == nullptr) {
        auto lastError = expo::modules::dotnet::managedLoaderLastError();
        if (lastError.empty()) {
          lastError =
            "Failed to resolve structured create/teardown runtime context entry points. Rebuild "
            "the managed ExpoDotnetHost artifacts with expo-modules-dotnet-autolinking.";
        }
        {
          std::lock_guard<std::mutex> lock(mutex_);
          registrationInProgress_ = false;
          lastError_ = lastError;
        }
        NSLog(@"[ExpoModulesDotnet] %s", lastError.c_str());
        return false;
      }

      expo::modules::dotnet::RuntimeContextResult result;
      entryPoints.createRuntimeContext(
        expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, &result);
      if (result.ok == 0 || result.runtimeContext == nullptr) {
        auto lastError = takeRuntimeContextError(result.error);
        if (lastError.empty()) {
          lastError =
            std::string(expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind)) +
            " runtime context registration failed.";
        }
        {
          std::lock_guard<std::mutex> lock(mutex_);
          registrationInProgress_ = false;
          teardownRuntimeContext_ = entryPoints.teardownRuntimeContext;
          lastError_ = lastError;
        }
        NSLog(@"[ExpoModulesDotnet] %s", lastError.c_str());
        return false;
      }

      {
        std::lock_guard<std::mutex> lock(mutex_);
        registrationInProgress_ = false;
        managedRuntimeContext_ = result.runtimeContext;
        teardownRuntimeContext_ = entryPoints.teardownRuntimeContext;
        registered_ = true;
        lastError_.clear();
      }
      NSLog(@"[ExpoModulesDotnet] %s managed modules registered.",
            expo::modules::dotnet::managedLoaderKindName(moduleConfig_.loaderKind));
      return true;
    } catch (...) {
      {
        std::lock_guard<std::mutex> lock(mutex_);
        registrationInProgress_ = false;
      }
      throw;
    }
  }

  std::string lastError() const
  {
    std::lock_guard<std::mutex> lock(mutex_);
    return lastError_;
  }

private:
  mutable std::mutex mutex_;
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig_;
  void *managedRuntimeContext_ = nullptr;
  expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext_ = nullptr;
  std::string lastError_;
  bool registered_ = false;
  bool registrationInProgress_ = false;
};

std::shared_ptr<InstalledRuntime> createInstalledRuntime(
  facebook::jsi::Runtime &runtime,
  const std::shared_ptr<facebook::react::CallInvoker> &callInvoker)
{
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
  auto moduleConfig = expo::modules::dotnet::loadManagedHostConfig();
  return std::make_shared<InstalledRuntime>(
    std::move(connector), runtimeHandle, std::move(moduleConfig));
}

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
  std::mutex _installedRuntimeMutex;
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
  auto installedRuntime = createInstalledRuntime(runtime, callInvoker);

  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    _installedRuntime.swap(installedRuntime);
  }
}

- (BOOL)installModulesWithRuntime:(facebook::jsi::Runtime &)runtime
                       callInvoker:(const std::shared_ptr<facebook::react::CallInvoker> &)callInvoker
{
  std::shared_ptr<InstalledRuntime> installedRuntime;
  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    installedRuntime = _installedRuntime;
  }

  if (installedRuntime == nullptr) {
    auto candidateRuntime = createInstalledRuntime(runtime, callInvoker);
    {
      std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
      if (_installedRuntime == nullptr) {
        _installedRuntime.swap(candidateRuntime);
      }
      installedRuntime = _installedRuntime;
    }
  }

  return installedRuntime->registerModules();
}

- (BOOL)installModules
{
  std::shared_ptr<InstalledRuntime> installedRuntime;
  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    installedRuntime = _installedRuntime;
  }

  if (installedRuntime == nullptr) {
    NSLog(@"[ExpoModulesDotnet] macOS module runtime is not ready.");
    return NO;
  }

  return installedRuntime->registerModules();
}

- (NSString *)getLastError
{
  std::shared_ptr<InstalledRuntime> installedRuntime;
  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    installedRuntime = _installedRuntime;
  }

  if (installedRuntime == nullptr) {
    return @"macOS module runtime is not ready.";
  }

  auto lastError = installedRuntime->lastError();
  if (!lastError.empty()) {
    return @(lastError.c_str());
  }

  auto loaderError = expo::modules::dotnet::managedLoaderLastError();
  return loaderError.empty() ? @"" : @(loaderError.c_str());
}

- (void)invalidate
{
  std::shared_ptr<InstalledRuntime> installedRuntime;
  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    installedRuntime = std::move(_installedRuntime);
  }
}

@end
