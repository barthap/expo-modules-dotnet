#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <dlfcn.h>
#include <memory>
#include <vector>

#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
@end

namespace {

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using CreateSessionFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownSessionFn = void (*)(void *);

RegisterModulesFn resolveRegisterModules()
{
  auto *symbol = dlsym(RTLD_DEFAULT, "example_module_register_modules");
  if (symbol == nullptr) {
    NSLog(@"[ExpoModulesDotnet] Failed to resolve example_module_register_modules: %s", dlerror());
    return nullptr;
  }
  return reinterpret_cast<RegisterModulesFn>(symbol);
}

CreateSessionFn resolveCreateSession()
{
  auto *symbol = dlsym(RTLD_DEFAULT, "example_module_create_session");
  return reinterpret_cast<CreateSessionFn>(symbol);
}

TeardownSessionFn resolveTeardownSession()
{
  auto *symbol = dlsym(RTLD_DEFAULT, "example_module_teardown_session");
  return reinterpret_cast<TeardownSessionFn>(symbol);
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
    if (connector_ != nullptr) {
      connector_->invalidate();
    }
    if (managedSession_ != nullptr && teardownSession_ != nullptr) {
      teardownSession_(managedSession_);
      managedSession_ = nullptr;
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

    auto createSession = resolveCreateSession();
    auto teardownSession = resolveTeardownSession();
    if (createSession != nullptr && teardownSession != nullptr) {
      managedSession_ = createSession(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
      teardownSession_ = teardownSession;
      if (managedSession_ == nullptr) {
        NSLog(@"[ExpoModulesDotnet] NativeAOT ExampleModule session registration failed.");
        return false;
      }
    } else {
      auto registerModules = resolveRegisterModules();
      if (registerModules == nullptr) {
        return false;
      }
      auto status = registerModules(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
      if (status != 0) {
        NSLog(@"[ExpoModulesDotnet] NativeAOT ExampleModule.add registration failed.");
        return false;
      }
    }

    NSLog(@"[ExpoModulesDotnet] NativeAOT ExampleModule.add module registered.");
    registered_ = true;
    return true;
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  void *managedSession_ = nullptr;
  TeardownSessionFn teardownSession_ = nullptr;
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
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  _installedRuntimes.clear();
  _installedRuntimes.push_back(std::move(installedRuntime));
}

- (BOOL)installModules
{
  BOOL installed = NO;
  for (const auto &installedRuntime : _installedRuntimes) {
    installed = installedRuntime->registerModules() || installed;
  }

  if (!installed) {
    NSLog(@"[ExpoModulesDotnet] NativeAOT module runtime is not ready.");
  }

  return installed;
}

- (void)invalidate
{
  _installedRuntimes.clear();
}

@end
