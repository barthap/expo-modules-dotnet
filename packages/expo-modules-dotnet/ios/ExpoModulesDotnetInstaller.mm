#import <Foundation/Foundation.h>
#import <React/RCTBridgeModule.h>
#import <ReactCommon/RCTTurboModule.h>
#import <ReactCommon/RCTTurboModuleWithJSIBindings.h>

#include <dlfcn.h>
#include <memory>

#include "ReactNativeRuntimeConnector.h"

@protocol ExpoModulesDotnetInstalling
- (BOOL)installModules;
@end

namespace {

using CreateRuntimeContextFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownRuntimeContextFn = void (*)(void *);

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

CreateRuntimeContextFn resolveCreateRuntimeContext()
{
  auto *symbol = resolveAggregatorSymbol("expo_dotnet_create_runtime_context");
  return reinterpret_cast<CreateRuntimeContextFn>(symbol);
}

TeardownRuntimeContextFn resolveTeardownRuntimeContext()
{
  auto *symbol = resolveAggregatorSymbol("expo_dotnet_teardown_runtime_context");
  return reinterpret_cast<TeardownRuntimeContextFn>(symbol);
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

    auto createRuntimeContext = resolveCreateRuntimeContext();
    auto teardownRuntimeContext = resolveTeardownRuntimeContext();
    if (createRuntimeContext == nullptr || teardownRuntimeContext == nullptr) {
      NSLog(@"[ExpoModulesDotnet] Failed to resolve create/teardown runtime context entry points. Run the expo-modules-dotnet-autolinking link command (or a full app build, which runs it as a script phase) before launching the iOS app.");
      return false;
    }

    managedRuntimeContext_ = createRuntimeContext(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle_);
    teardownRuntimeContext_ = teardownRuntimeContext;
    if (managedRuntimeContext_ == nullptr) {
      NSLog(@"[ExpoModulesDotnet] NativeAOT runtime context registration failed.");
      return false;
    }

    NSLog(@"[ExpoModulesDotnet] NativeAOT managed modules registered.");
    registered_ = true;
    return true;
  }

private:
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector_;
  expo_jsi_runtime_handle runtimeHandle_ = nullptr;
  void *managedRuntimeContext_ = nullptr;
  TeardownRuntimeContextFn teardownRuntimeContext_ = nullptr;
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
  auto installedRuntime =
    std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  _installedRuntime = std::move(installedRuntime);
}

- (BOOL)installModules
{
  if (_installedRuntime == nullptr) {
    NSLog(@"[ExpoModulesDotnet] NativeAOT module runtime is not ready.");
    return NO;
  }

  return _installedRuntime->registerModules();
}

- (void)invalidate
{
  _installedRuntime.reset();
}

@end
