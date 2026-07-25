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

// App-scoped directories this host resolved, as UTF-8. An empty string means the
// host could not resolve that directory, and the managed side is told nothing
// rather than being handed a user-wide root.
struct ResolvedAppDirectories {
  std::string cacheDirectory;
  std::string persistentFilesDirectory;

  bool isConfigured() const
  {
    return !cacheDirectory.empty() && !persistentFilesDirectory.empty();
  }
};

// Rejects anything that is not a fully qualified path ending in the host's app
// identity. A bare user-wide root is shared by every app on the machine, which
// is the collision this bridge exists to avoid.
bool isAppScopedPath(const std::string &path, const std::string &appIdentity)
{
  if (path.empty() || path.front() != '/' || path.find('\0') != std::string::npos) {
    return false;
  }
  const std::string suffix = "/" + appIdentity;
  return path.size() > suffix.size() &&
    path.compare(path.size() - suffix.size(), suffix.size(), suffix) == 0;
}

std::string resolveAppScopedDirectory(NSSearchPathDirectory searchPath, NSString *appIdentity)
{
  NSArray<NSURL *> *roots = [[NSFileManager defaultManager] URLsForDirectory:searchPath
                                                                  inDomains:NSUserDomainMask];
  NSURL *root = roots.firstObject;
  if (root == nil) {
    return "";
  }
  NSString *path = [root URLByAppendingPathComponent:appIdentity isDirectory:YES].path;
  const char *utf8 = path.UTF8String;
  if (utf8 == nullptr) {
    return "";
  }
  return std::string(utf8);
}

// Only the host knows the app identity, so only the host can name a directory
// that belongs to this app alone. Caches and Application Support are the macOS
// analogues of upstream's cache and persistent-files directories; the visible
// ~/Documents is not app-private storage on macOS, so it is not used here.
ResolvedAppDirectories resolveAppDirectories()
{
  NSString *appIdentity = [[NSBundle mainBundle] bundleIdentifier];
  if (appIdentity.length == 0) {
    NSLog(@"[ExpoModulesDotnet] Host supplied no app identity, so app directories stay "
          @"unconfigured.");
    return {};
  }

  ResolvedAppDirectories directories{
    resolveAppScopedDirectory(NSCachesDirectory, appIdentity),
    resolveAppScopedDirectory(NSApplicationSupportDirectory, appIdentity),
  };
  if (!directories.isConfigured()) {
    NSLog(@"[ExpoModulesDotnet] Host did not resolve both app directories, so they stay "
          @"unconfigured.");
    return {};
  }

  const std::string identity(appIdentity.UTF8String == nullptr ? "" : appIdentity.UTF8String);
  if (identity.empty() || !isAppScopedPath(directories.cacheDirectory, identity) ||
      !isAppScopedPath(directories.persistentFilesDirectory, identity) ||
      directories.cacheDirectory == directories.persistentFilesDirectory) {
    NSLog(@"[ExpoModulesDotnet] Resolved app directories are not distinct app-scoped paths, so "
          @"they stay unconfigured.");
    return {};
  }

  return directories;
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
      auto entryPoints = expo::modules::dotnet::resolveRuntimeContextEntryPoints(moduleConfig_);
      if (entryPoints.createRuntimeContextV2 == nullptr ||
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

      // The struct borrows these strings for the duration of the create call, so
      // both must live in this frame until the call returns. A null pointer means
      // both directories are unconfigured.
      const auto appDirectories = resolveAppDirectories();
      expo::modules::dotnet::expo_dotnet_app_directories directories{};
      const expo::modules::dotnet::expo_dotnet_app_directories *directoriesPointer = nullptr;
      if (appDirectories.isConfigured()) {
        directories.size = sizeof(directories);
        directories.version = EXPO_DOTNET_HOST_ABI_VERSION;
        directories.cache_directory =
          reinterpret_cast<const uint8_t *>(appDirectories.cacheDirectory.data());
        directories.cache_directory_length =
          static_cast<int32_t>(appDirectories.cacheDirectory.size());
        directories.persistent_files_directory =
          reinterpret_cast<const uint8_t *>(appDirectories.persistentFilesDirectory.data());
        directories.persistent_files_directory_length =
          static_cast<int32_t>(appDirectories.persistentFilesDirectory.size());
        directoriesPointer = &directories;
        NSLog(@"[ExpoModulesDotnet] App directories configured: cache=app-scoped, "
              @"persistent=app-scoped.");
      }

      expo::modules::dotnet::RuntimeContextResult result;
      entryPoints.createRuntimeContextV2(
        expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, directoriesPointer, &result);
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
