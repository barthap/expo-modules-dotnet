#include <ReactCommon/BindingsInstallerHolder.h>
#include <android/log.h>
#include <dlfcn.h>
#include <fbjni/fbjni.h>
#include <memory>
#include <mutex>
#include <vector>

#include "ReactNativeRuntimeConnector.h"

namespace {

constexpr const char *kLogTag = "ExpoModulesDotnet";

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using CreateSessionFn = void *(*)(const expo_jsi_api *, expo_jsi_runtime_handle);
using TeardownSessionFn = void (*)(void *);

struct InstalledRuntime {
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  void *managedSession = nullptr;
  TeardownSessionFn teardownSession = nullptr;
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
    if (managedSession != nullptr && teardownSession != nullptr) {
      teardownSession(managedSession);
      managedSession = nullptr;
    }
    if (runtimeHandle != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle);
    }
  }
};

std::mutex installedRuntimesMutex;
std::vector<std::shared_ptr<InstalledRuntime>> installedRuntimes;

void *resolveExampleModuleSymbol(const char *symbolName)
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
  auto *symbol = resolveExampleModuleSymbol("example_module_register_modules");
  if (symbol == nullptr) {
    __android_log_print(ANDROID_LOG_ERROR,
                        kLogTag,
                        "Failed to resolve example_module_register_modules: %s",
                        dlerror());
    return nullptr;
  }
  return reinterpret_cast<RegisterModulesFn>(symbol);
}

CreateSessionFn resolveCreateSession()
{
  auto *symbol = resolveExampleModuleSymbol("example_module_create_session");
  return reinterpret_cast<CreateSessionFn>(symbol);
}

TeardownSessionFn resolveTeardownSession()
{
  auto *symbol = resolveExampleModuleSymbol("example_module_teardown_session");
  return reinterpret_cast<TeardownSessionFn>(symbol);
}

bool registerExampleModule(InstalledRuntime &installedRuntime)
{
  if (installedRuntime.registered) {
    return true;
  }

  auto createSession = resolveCreateSession();
  auto teardownSession = resolveTeardownSession();
  if (createSession != nullptr && teardownSession != nullptr) {
    installedRuntime.managedSession =
      createSession(expo::dotnet::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle);
    installedRuntime.teardownSession = teardownSession;
    if (installedRuntime.managedSession == nullptr) {
      __android_log_print(
        ANDROID_LOG_ERROR, kLogTag, "NativeAOT ExampleModule session registration failed.");
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
        ANDROID_LOG_ERROR, kLogTag, "NativeAOT ExampleModule.add registration failed.");
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
  auto installedRuntime = std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  std::lock_guard<std::mutex> lock(installedRuntimesMutex);
  installedRuntimes.push_back(std::move(installedRuntime));
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT module runtime captured.");
}

bool installDotnetModules()
{
  std::lock_guard<std::mutex> lock(installedRuntimesMutex);

  bool installed = false;
  for (auto &installedRuntime : installedRuntimes) {
    installed = registerExampleModule(*installedRuntime) || installed;
  }

  if (!installed) {
    __android_log_print(ANDROID_LOG_ERROR, kLogTag, "NativeAOT module runtime is not ready.");
  }

  return installed;
}

void invalidateDotnetModules()
{
  std::lock_guard<std::mutex> lock(installedRuntimesMutex);
  installedRuntimes.clear();
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
