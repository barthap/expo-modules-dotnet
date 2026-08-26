#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <dlfcn.h>
#include <memory>
#include <mutex>
#include <string>

#include "ReactNativeRuntimeConnector.h"
#include "expo_dotnet_host.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
- (NSString *)getLastError;
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

void *loadAggregatorLibrary()
{
  NSString *frameworksPath = [[NSBundle mainBundle] privateFrameworksPath];
  NSString *libraryPath = [frameworksPath stringByAppendingPathComponent:@"libExpoDotnetHost.dylib"];
  void *handle = dlopen(libraryPath.fileSystemRepresentation, RTLD_NOW | RTLD_GLOBAL);
  if (handle == nullptr) {
    NSLog(@"[ExpoModulesDotnet] dlopen(%@) failed: %s. Run the expo-modules-dotnet-autolinking link step (the pod install script phase stages the library).",
          libraryPath,
          dlerror());
  }
  return handle;
}

void *resolveAggregatorSymbol(const char *symbolName)
{
  static void *aggregatorHandle = loadAggregatorLibrary();
  void *symbol = nullptr;
  if (aggregatorHandle != nullptr) {
    symbol = dlsym(aggregatorHandle, symbolName);
  }
  if (symbol == nullptr) {
    symbol = dlsym(RTLD_DEFAULT, symbolName);
  }
  return symbol;
}

expo::modules::dotnet::CreateRuntimeContextV2Fn resolveCreateRuntimeContextV2()
{
  auto *symbol = resolveAggregatorSymbol("expo_dotnet_create_runtime_context_result_v2");
  return reinterpret_cast<expo::modules::dotnet::CreateRuntimeContextV2Fn>(symbol);
}

expo::modules::dotnet::TeardownRuntimeContextFn resolveTeardownRuntimeContext()
{
  auto *symbol = resolveAggregatorSymbol("expo_dotnet_teardown_runtime_context");
  return reinterpret_cast<expo::modules::dotnet::TeardownRuntimeContextFn>(symbol);
}

class InstalledRuntime final {
public:
  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector_(std::move(connector)),
      runtimeHandle_(runtimeHandle)
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
      if (runtimeHandle != nullptr) {
        expo::dotnet::prepareRuntimeHandleForInvalidation(runtimeHandle);
      }
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
      auto createRuntimeContextV2 = resolveCreateRuntimeContextV2();
      auto teardownRuntimeContext = resolveTeardownRuntimeContext();
      if (createRuntimeContextV2 == nullptr || teardownRuntimeContext == nullptr) {
        const std::string lastError =
          "Failed to resolve structured create/teardown runtime context entry points. "
          "Run the expo-modules-dotnet-autolinking link command (or a full app build, "
          "which runs it as a script phase) before launching the iOS app.";
        {
          std::lock_guard<std::mutex> lock(mutex_);
          registrationInProgress_ = false;
          lastError_ = lastError;
        }
        NSLog(@"[ExpoModulesDotnet] %s", lastError.c_str());
        return false;
      }

      expo::modules::dotnet::RuntimeContextResult result;
      // A null app-directories pointer means both directories are unconfigured.
      createRuntimeContextV2(
        expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, nullptr, &result);
      if (result.ok == 0 || result.runtimeContext == nullptr) {
        auto lastError = takeRuntimeContextError(result.error);
        if (lastError.empty()) {
          lastError = "NativeAOT runtime context registration failed.";
        }
        {
          std::lock_guard<std::mutex> lock(mutex_);
          registrationInProgress_ = false;
          teardownRuntimeContext_ = teardownRuntimeContext;
          lastError_ = lastError;
        }
        NSLog(@"[ExpoModulesDotnet] %s", lastError.c_str());
        return false;
      }

      {
        std::lock_guard<std::mutex> lock(mutex_);
        registrationInProgress_ = false;
        managedRuntimeContext_ = result.runtimeContext;
        teardownRuntimeContext_ = teardownRuntimeContext;
        registered_ = true;
        lastError_.clear();
      }
      NSLog(@"[ExpoModulesDotnet] NativeAOT managed modules registered.");
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
  void *managedRuntimeContext_ = nullptr;
  expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext_ = nullptr;
  std::string lastError_;
  bool registered_ = false;
  bool registrationInProgress_ = false;
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
    return facebook::jsi::Value([installerTurboModule.installer_ installModules]);
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
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    _installedRuntime.swap(installedRuntime);
  }
}

- (BOOL)installModules
{
  std::shared_ptr<InstalledRuntime> installedRuntime;
  {
    std::lock_guard<std::mutex> lock(_installedRuntimeMutex);
    installedRuntime = _installedRuntime;
  }

  if (installedRuntime == nullptr) {
    NSLog(@"[ExpoModulesDotnet] NativeAOT module runtime is not ready.");
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
    return @"NativeAOT module runtime is not ready.";
  }

  auto lastError = installedRuntime->lastError();
  return lastError.empty() ? @"" : @(lastError.c_str());
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
