#include <ReactCommon/BindingsInstallerHolder.h>
#include <android/log.h>
#include <dlfcn.h>
#include <fbjni/fbjni.h>
#include <memory>
#include <mutex>
#include <vector>

#include "ReactNativeRuntimeConnector.h"

namespace {

constexpr const char *kLogTag = "ExpoCSharpV2";

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);

struct InstalledRuntime {
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  bool registered = false;

  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector(std::move(connector)),
      runtimeHandle(runtimeHandle)
  {
  }

  ~InstalledRuntime()
  {
    if (runtimeHandle != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle);
    }
    if (connector != nullptr) {
      connector->invalidate();
    }
  }
};

std::mutex installedRuntimesMutex;
// Proof lifetime: Android keeps install records for the process so the
// NativeAOT module can keep calling through the borrowed RN runtime. A
// production integration should mirror Expo's JSIContext teardown: invalidate
// the connector state and reset the holder before React Native releases the
// runtime.
std::vector<std::shared_ptr<InstalledRuntime>> installedRuntimes;

RegisterModulesFn resolveRegisterModules()
{
  auto *symbol = dlsym(RTLD_DEFAULT, "expo_mobile_v2_register_modules");
  if (symbol == nullptr) {
    dlerror();
    auto *library = dlopen("libExpoMobileV2Module.so", RTLD_NOW | RTLD_GLOBAL);
    if (library != nullptr) {
      symbol = dlsym(library, "expo_mobile_v2_register_modules");
    }
  }

  if (symbol == nullptr) {
    __android_log_print(ANDROID_LOG_ERROR,
                        kLogTag,
                        "Failed to resolve expo_mobile_v2_register_modules: %s",
                        dlerror());
    return nullptr;
  }
  return reinterpret_cast<RegisterModulesFn>(symbol);
}

bool registerV2Module(InstalledRuntime &installedRuntime)
{
  if (installedRuntime.registered) {
    return true;
  }

  auto registerModules = resolveRegisterModules();
  if (registerModules == nullptr) {
    return false;
  }

  auto status =
    registerModules(expo::dotnet::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle);
  if (status != 0) {
    __android_log_print(
      ANDROID_LOG_ERROR, kLogTag, "NativeAOT ExpoCSharpV2.add registration failed.");
    return false;
  }

  installedRuntime.registered = true;
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT ExpoCSharpV2.add module registered.");
  return true;
}

void installV2Module(facebook::jsi::Runtime &runtime,
                     const std::shared_ptr<facebook::react::CallInvoker> &callInvoker)
{
  auto connector =
    std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
  auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
  auto installedRuntime = std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  registerV2Module(*installedRuntime);

  std::lock_guard<std::mutex> lock(installedRuntimesMutex);
  installedRuntimes.push_back(std::move(installedRuntime));
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT ExpoCSharpV2.add installer ran.");
}

} // namespace

namespace expo::modules::csharpv2 {

class ExpoCSharpV2BindingsInstaller
  : public facebook::jni::JavaClass<ExpoCSharpV2BindingsInstaller> {
public:
  static constexpr auto kJavaDescriptor = "Lexpo/modules/csharpv2/ExpoCSharpV2TurboModule;";

  static void registerNatives()
  {
    javaClassLocal()->registerNatives({
      makeNativeMethod("getBindingsInstaller", ExpoCSharpV2BindingsInstaller::getBindingsInstaller),
    });
  }

private:
  static facebook::jni::local_ref<facebook::react::BindingsInstallerHolder::javaobject>
  getBindingsInstaller(facebook::jni::alias_ref<ExpoCSharpV2BindingsInstaller>)
  {
    return facebook::react::BindingsInstallerHolder::newObjectCxxArgs(
      [](facebook::jsi::Runtime &runtime,
         const std::shared_ptr<facebook::react::CallInvoker> &callInvoker) {
        installV2Module(runtime, callInvoker);
      });
  }
};

} // namespace expo::modules::csharpv2

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *)
{
  return facebook::jni::initialize(
    vm, [] { expo::modules::csharpv2::ExpoCSharpV2BindingsInstaller::registerNatives(); });
}
