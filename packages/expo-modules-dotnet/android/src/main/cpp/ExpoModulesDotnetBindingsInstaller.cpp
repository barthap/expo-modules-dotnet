#include <ReactCommon/BindingsInstallerHolder.h>
#include <android/log.h>
#include <dlfcn.h>
#include <fbjni/fbjni.h>
#include <memory>
#include <mutex>
#include <string>

#include "ReactNativeRuntimeConnector.h"

namespace {

constexpr const char *kLogTag = "ExpoModulesDotnet";

struct RuntimeContextError {
  const char *message = nullptr;
  int32_t messageLength = 0;
  void *releaseContext = nullptr;
  void (*release)(void *) = nullptr;
};

struct RuntimeContextResult {
  int32_t ok = 0;
  void *runtimeContext = nullptr;
  RuntimeContextError error;
};

using CreateRuntimeContextFn = void (*)(const expo_jsi_api *,
                                        expo_jsi_runtime_handle,
                                        RuntimeContextResult *);
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
      if (runtimeHandle != nullptr) {
        expo::dotnet::prepareRuntimeHandleForInvalidation(runtimeHandle);
      }
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
std::string lastError;

void setLastError(std::string message)
{
  __android_log_print(ANDROID_LOG_ERROR, kLogTag, "%s", message.c_str());
  lastError = std::move(message);
}

void clearLastError()
{
  lastError.clear();
}

std::string takeRuntimeContextError(RuntimeContextError &error)
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

std::string dlerrorMessage()
{
  auto *error = dlerror();
  return error == nullptr ? "unknown error" : error;
}

void *resolveDotnetAppSymbol(const char *symbolName, std::string &error)
{
  error.clear();
  // First check already-loaded symbols, then dlopen the app-owned host. Kotlin
  // intentionally avoids loading ExpoDotnetHost so failures stay diagnosable via JS.
  dlerror();
  auto *symbol = dlsym(RTLD_DEFAULT, symbolName);
  if (symbol == nullptr) {
    dlerror();
    auto *library = dlopen("libExpoDotnetHost.so", RTLD_NOW | RTLD_GLOBAL);
    if (library == nullptr) {
      error = "Failed to load libExpoDotnetHost.so: " + dlerrorMessage();
      return nullptr;
    }

    dlerror();
    symbol = dlsym(library, symbolName);
    if (symbol == nullptr) {
      error = "Failed to resolve " + std::string(symbolName) +
              " from libExpoDotnetHost.so: " + dlerrorMessage();
    }
  }
  return symbol;
}

CreateRuntimeContextFn resolveCreateRuntimeContext(std::string &error)
{
  auto *symbol = resolveDotnetAppSymbol("expo_dotnet_create_runtime_context_result", error);
  return reinterpret_cast<CreateRuntimeContextFn>(symbol);
}

TeardownRuntimeContextFn resolveTeardownRuntimeContext(std::string &error)
{
  auto *symbol = resolveDotnetAppSymbol("expo_dotnet_teardown_runtime_context", error);
  return reinterpret_cast<TeardownRuntimeContextFn>(symbol);
}

bool registerDotnetModules(InstalledRuntime &installedRuntime)
{
  if (installedRuntime.registered) {
    return true;
  }

  std::string createError;
  auto createRuntimeContext = resolveCreateRuntimeContext(createError);
  std::string teardownError;
  auto teardownRuntimeContext = resolveTeardownRuntimeContext(teardownError);
  if (createRuntimeContext == nullptr || teardownRuntimeContext == nullptr) {
    auto detail = createRuntimeContext == nullptr ? createError : teardownError;
    setLastError("Failed to resolve structured expo_dotnet_create/teardown_runtime_context. " +
                 detail +
                 " Run the expo-modules-dotnet-autolinking link command (or a full app build, "
                 "which runs it as a Gradle task) before launching the Android app.");
    return false;
  }

  RuntimeContextResult result;
  createRuntimeContext(
    expo::dotnet::reactNativeExpoJsiApi(), installedRuntime.runtimeHandle, &result);
  installedRuntime.teardownRuntimeContext = teardownRuntimeContext;
  if (result.ok == 0 || result.runtimeContext == nullptr) {
    auto managedError = takeRuntimeContextError(result.error);
    setLastError(managedError.empty() ? "NativeAOT runtime context registration failed."
                                      : std::move(managedError));
    return false;
  }

  installedRuntime.managedRuntimeContext = result.runtimeContext;
  installedRuntime.registered = true;
  clearLastError();
  __android_log_print(
    ANDROID_LOG_INFO, kLogTag, "NativeAOT ExpoDotnetHost managed modules registered.");
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
    setLastError("NativeAOT module runtime is not ready.");
    return false;
  }

  return registerDotnetModules(*installedRuntime);
}

std::string getDotnetModulesLastError()
{
  std::lock_guard<std::mutex> lock(installedRuntimeMutex);
  return lastError;
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
      makeNativeMethod("getLastError", ExpoModulesDotnetBindingsInstaller::getLastError),
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

  static facebook::jni::local_ref<facebook::jni::JString> getLastError(
    facebook::jni::alias_ref<ExpoModulesDotnetBindingsInstaller>)
  {
    return facebook::jni::make_jstring(getDotnetModulesLastError());
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
