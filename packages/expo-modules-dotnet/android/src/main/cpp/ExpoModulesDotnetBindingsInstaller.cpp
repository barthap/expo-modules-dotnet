#include <ReactCommon/BindingsInstallerHolder.h>
#include <android/log.h>
#include <dlfcn.h>
#include <fbjni/fbjni.h>
#include <memory>
#include <mutex>

#include "ReactNativeRuntimeConnector.h"

namespace {

constexpr const char *kLogTag = "ExpoModulesDotnet";

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using CreateRuntimeContextFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownRuntimeContextFn = void (*)(void *);

struct InstalledRuntime {
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  void *managedRuntimeContext = nullptr;
  TeardownRuntimeContextFn teardownRuntimeContext = nullptr;
  bool registered = false;

  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector(std::move(connector)),
      runtimeHandle(runtimeHandle)
  {
  }

  ~InstalledRuntime()
  {
    if (connector != nullptr) {
      connector->invalidate();
    }
    if (managedRuntimeContext != nullptr && teardownRuntimeContext != nullptr) {
      teardownRuntimeContext(managedRuntimeContext);
      managedRuntimeContext = nullptr;
    }
    if (runtimeHandle != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle);
    }
  }
};

std::mutex installedRuntimeMutex;
std::shared_ptr<InstalledRuntime> installedRuntime;

void *resolveDotnetAppSymbol(const char *symbolName)
{
  auto *symbol = dlsym(RTLD_DEFAULT, symbolName);
  if (symbol == nullptr) {
    dlerror();
    auto *library = dlopen("libExampleModule.so", RTLD_NOW | RTLD_GLOBAL);
    if (library != nullptr) {
      symbol = dlsym(library, symbolName);
    }
  }
  return symbol;
}

RegisterModulesFn resolveRegisterModules()
{
  auto *symbol = resolveDotnetAppSymbol("expo_dotnet_register_modules");
  if (symbol == nullptr) {
    __android_log_print(
      ANDROID_LOG_ERROR, kLogTag, "Failed to resolve expo_dotnet_register_modules: %s", dlerror());
    return nullptr;
  }
  return reinterpret_cast<RegisterModulesFn>(symbol);
}

CreateRuntimeContextFn resolveCreateRuntimeContext()
{
  auto *symbol = resolveDotnetAppSymbol("expo_dotnet_create_runtime_context");
  return reinterpret_cast<CreateRuntimeContextFn>(symbol);
}

TeardownRuntimeContextFn resolveTeardownRuntimeContext()
{
  auto *symbol = resolveDotnetAppSymbol("expo_dotnet_teardown_runtime_context");
  return reinterpret_cast<TeardownRuntimeContextFn>(symbol);
}

bool registerDotnetModules(InstalledRuntime &installedRuntime)
{
  if (installedRuntime.registered) {
    return true;
  }

  auto createRuntimeContext = resolveCreateRuntimeContext();
  auto teardownRuntimeContext = resolveTeardownRuntimeContext();
  if (createRuntimeContext != nullptr && teardownRuntimeContext != nullptr) {
    installedRuntime.managedRuntimeContext =
      createRuntimeContext(expo::dotnet::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle);
    installedRuntime.teardownRuntimeContext = teardownRuntimeContext;
    if (installedRuntime.managedRuntimeContext == nullptr) {
      __android_log_print(
        ANDROID_LOG_ERROR, kLogTag, "NativeAOT runtime context registration failed.");
      return false;
    }
  } else {
    auto registerModules = resolveRegisterModules();
    if (registerModules == nullptr) {
      return false;
    }

    auto status =
      registerModules(expo::dotnet::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle);
    if (status != 0) {
      __android_log_print(
        ANDROID_LOG_ERROR, kLogTag, "NativeAOT managed module registration failed.");
      return false;
    }
  }

  installedRuntime.registered = true;
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT ExampleModule.add module registered.");
  return true;
}

void prepareDotnetModuleRuntime(facebook::jsi::Runtime &runtime,
                                const std::shared_ptr<facebook::react::CallInvoker> &callInvoker)
{
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);

  std::lock_guard<std::mutex> lock(installedRuntimeMutex);
  installedRuntime = std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT module runtime captured.");
}

bool installDotnetModules()
{
  std::lock_guard<std::mutex> lock(installedRuntimeMutex);
  if (installedRuntime == nullptr) {
    __android_log_print(ANDROID_LOG_ERROR, kLogTag, "NativeAOT module runtime is not ready.");
    return false;
  }

  return registerDotnetModules(*installedRuntime);
}

void invalidateDotnetModules()
{
  std::lock_guard<std::mutex> lock(installedRuntimeMutex);
  installedRuntime.reset();
}

} // namespace

namespace expo::modules::dotnet {

class ExpoModulesDotnetBindingsInstaller
  : public facebook::jni::JavaClass<ExpoModulesDotnetBindingsInstaller> {
public:
  static constexpr auto kJavaDescriptor = "Lexpo/modules/dotnet/ExpoModulesDotnetTurboModule;";

  static void registerNatives()
  {
    javaClassLocal()->registerNatives({
      makeNativeMethod("getBindingsInstaller",
                       ExpoModulesDotnetBindingsInstaller::getBindingsInstaller),
      makeNativeMethod("installModules", ExpoModulesDotnetBindingsInstaller::installModules),
      makeNativeMethod("invalidateRuntime", ExpoModulesDotnetBindingsInstaller::invalidateRuntime),
    });
  }

private:
  static facebook::jni::local_ref<facebook::react::BindingsInstallerHolder::javaobject>
  getBindingsInstaller(facebook::jni::alias_ref<ExpoModulesDotnetBindingsInstaller>)
  {
    return facebook::react::BindingsInstallerHolder::newObjectCxxArgs(
      [](facebook::jsi::Runtime &runtime,
         const std::shared_ptr<facebook::react::CallInvoker> &callInvoker) {
        prepareDotnetModuleRuntime(runtime, callInvoker);
      });
  }

  static bool installModules(facebook::jni::alias_ref<ExpoModulesDotnetBindingsInstaller>)
  {
    return installDotnetModules();
  }

  static void invalidateRuntime(facebook::jni::alias_ref<ExpoModulesDotnetBindingsInstaller>)
  {
    invalidateDotnetModules();
  }
};

} // namespace expo::modules::dotnet

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *)
{
  return facebook::jni::initialize(
    vm, [] { expo::modules::dotnet::ExpoModulesDotnetBindingsInstaller::registerNatives(); });
}
