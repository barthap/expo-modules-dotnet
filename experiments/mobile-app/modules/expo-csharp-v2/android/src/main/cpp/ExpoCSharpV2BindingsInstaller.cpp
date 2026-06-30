#include <android/log.h>
#include <dlfcn.h>
#include <fbjni/fbjni.h>
#include <memory>
#include <mutex>
#include <react/runtime/ReactInstance.h>
#include <react/runtime/jni/JBindingsInstaller.h>
#include <vector>

#include "ReactNativeRuntimeConnector.h"

namespace {

constexpr const char *kLogTag = "ExpoCSharpV2";

using RegisterModulesFn = int (*)(const expo_jsi_api *, expo_jsi_runtime_handle);

struct InstalledRuntime {
  std::unique_ptr<expo::jsi::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  bool registered = false;

  InstalledRuntime(std::unique_ptr<expo::jsi::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle)
    : connector(std::move(connector)),
      runtimeHandle(runtimeHandle)
  {
  }
};

std::mutex installedRuntimesMutex;
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

  auto status = registerModules(expo::jsi::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle);
  if (status != 0) {
    __android_log_print(
      ANDROID_LOG_ERROR, kLogTag, "NativeAOT ExpoCSharpV2.add registration failed.");
    return false;
  }

  installedRuntime.registered = true;
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT ExpoCSharpV2.add module registered.");
  return true;
}

void installV2ModuleLoader(facebook::jsi::Runtime &runtime)
{
  expo::jsi::ReactNativeRuntimeExecutor::Options options;
  options.isRuntimeThread = [] { return true; };
  options.supportsSyncDispatch = true;

  auto connector =
    std::make_unique<expo::jsi::ReactNativeRuntimeConnector>(runtime, std::move(options));
  auto runtimeHandle = expo::jsi::createReactNativeRuntimeHandle(*connector);
  auto installedRuntime = std::make_shared<InstalledRuntime>(std::move(connector), runtimeHandle);

  auto installer = facebook::jsi::Function::createFromHostFunction(
    runtime,
    facebook::jsi::PropNameID::forAscii(runtime, "__expoCSharpV2InstallModules"),
    0,
    [installedRuntime](
      facebook::jsi::Runtime &, const facebook::jsi::Value &, const facebook::jsi::Value *, size_t)
      -> facebook::jsi::Value { return registerV2Module(*installedRuntime); });
  runtime.global().setProperty(runtime, "__expoCSharpV2InstallModules", std::move(installer));

  std::lock_guard<std::mutex> lock(installedRuntimesMutex);
  installedRuntimes.push_back(std::move(installedRuntime));
  __android_log_print(ANDROID_LOG_INFO, kLogTag, "NativeAOT ExpoCSharpV2.add loader installed.");
}

} // namespace

namespace expo::modules::csharpv2 {

class ExpoCSharpV2BindingsInstaller
  : public facebook::jni::HybridClass<ExpoCSharpV2BindingsInstaller,
                                      facebook::react::JBindingsInstaller> {
public:
  static constexpr auto kJavaDescriptor = "Lexpo/modules/csharpv2/ExpoCSharpV2BindingsInstaller;";

  static facebook::jni::local_ref<jhybriddata> initHybrid(facebook::jni::alias_ref<jclass>)
  {
    return makeCxxInstance();
  }

  static void registerNatives()
  {
    registerHybrid({
      makeNativeMethod("initHybrid", ExpoCSharpV2BindingsInstaller::initHybrid),
    });
  }

  facebook::react::ReactInstance::BindingsInstallFunc getBindingsInstallFunc() override
  {
    return [](facebook::jsi::Runtime &runtime) { installV2ModuleLoader(runtime); };
  }

private:
  friend HybridBase;
};

} // namespace expo::modules::csharpv2

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *)
{
  return facebook::jni::initialize(
    vm, [] { expo::modules::csharpv2::ExpoCSharpV2BindingsInstaller::registerNatives(); });
}
