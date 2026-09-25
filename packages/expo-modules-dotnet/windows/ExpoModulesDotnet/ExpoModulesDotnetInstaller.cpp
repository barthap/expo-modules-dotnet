#include "pch.h"

#include "ExpoModulesDotnetInstaller.h"

#include <ReactNotificationService.h>
#include <appmodel.h>
#include <winrt/Windows.ApplicationModel.h>
#include <winrt/Windows.Storage.h>

#include <optional>
#include <sstream>
#include <stdexcept>
#include <utility>

namespace winrt::ExpoModulesDotnet {
namespace {

void logMessage(const std::wstring &message)
{
  OutputDebugStringW(message.c_str());
  OutputDebugStringW(L"\n");
}

std::wstring toWide(const char *value)
{
  if (value == nullptr) {
    return L"";
  }
  const int length = MultiByteToWideChar(CP_UTF8, 0, value, -1, nullptr, 0);
  if (length <= 0) {
    return L"";
  }
  std::wstring result(static_cast<size_t>(length - 1), L'\0');
  MultiByteToWideChar(CP_UTF8, 0, value, -1, result.data(), length);
  return result;
}

std::string toUtf8(const std::wstring &value)
{
  if (value.empty()) {
    return "";
  }
  const int length = WideCharToMultiByte(
    CP_UTF8, 0, value.c_str(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
  if (length <= 0) {
    return "";
  }
  std::string result(static_cast<size_t>(length), '\0');
  WideCharToMultiByte(CP_UTF8,
                      0,
                      value.c_str(),
                      static_cast<int>(value.size()),
                      result.data(),
                      length,
                      nullptr,
                      nullptr);
  return result;
}

std::wstring takeRuntimeContextError(expo::modules::dotnet::RuntimeContextError &error)
{
  std::wstring message;
  if (error.message != nullptr && error.messageLength > 0) {
    std::string utf8(error.message, static_cast<size_t>(error.messageLength));
    message = toWide(utf8.c_str());
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

// Accepts a drive-rooted or UNC path only. A relative path would anchor to the
// process working directory, which is not the app scope the host means.
bool isFullyQualifiedPath(const std::string &path)
{
  if (path.find('\0') != std::string::npos) {
    return false;
  }
  if (path.size() >= 2 && path[0] == '\\' && path[1] == '\\') {
    return true;
  }
  if (path.size() < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) {
    return false;
  }
  const char drive = path[0];
  return (drive >= 'A' && drive <= 'Z') || (drive >= 'a' && drive <= 'z');
}

// Only the host knows the app identity, so only the host can name a directory
// that belongs to this app alone. `ApplicationData::Current()` is per-package by
// definition and throws without package identity, so its folders are app-scoped
// whenever this succeeds; there is no unpackaged fallback, because an executable
// name is not a stable app identity.
ResolvedAppDirectories resolveAppDirectories()
{
  ResolvedAppDirectories directories;
  try {
    const auto applicationData = winrt::Windows::Storage::ApplicationData::Current();
    directories.cacheDirectory = winrt::to_string(applicationData.LocalCacheFolder().Path());
    directories.persistentFilesDirectory = winrt::to_string(applicationData.LocalFolder().Path());
  } catch (const winrt::hresult_error &) {
    logMessage(L"[ExpoModulesDotnet] Unpackaged host supplied no app identity, so app directories "
               L"stay unconfigured.");
    return {};
  }

  if (!directories.isConfigured()) {
    logMessage(L"[ExpoModulesDotnet] Host did not resolve both app directories, so they stay "
               L"unconfigured.");
    return {};
  }

  if (!isFullyQualifiedPath(directories.cacheDirectory) ||
      !isFullyQualifiedPath(directories.persistentFilesDirectory) ||
      directories.cacheDirectory == directories.persistentFilesDirectory) {
    logMessage(L"[ExpoModulesDotnet] Resolved app directories are not distinct fully qualified "
               L"paths, so they stay unconfigured.");
    return {};
  }

  return directories;
}

std::optional<std::string> resolveNativeBuildVersion()
{
  // Package.Current is only valid in a packaged process. Check identity first
  // so only the known no-package case maps to an unavailable version.
  UINT32 packageIdLength = 0;
  const LONG packageStatus = GetCurrentPackageId(&packageIdLength, nullptr);
  if (packageStatus == APPMODEL_ERROR_NO_PACKAGE) {
    return std::nullopt;
  }
  if (packageStatus != ERROR_INSUFFICIENT_BUFFER) {
    throw std::runtime_error("GetCurrentPackageId failed with Windows error " +
                             std::to_string(packageStatus) + ".");
  }

  const auto version = winrt::Windows::ApplicationModel::Package::Current().Id().Version();
  return std::to_string(version.Major) + "." + std::to_string(version.Minor) + "." +
         std::to_string(version.Build) + "." + std::to_string(version.Revision);
}

} // namespace

struct ExpoModulesDotnetInstaller::InstalledRuntime final
  : std::enable_shared_from_this<ExpoModulesDotnetInstaller::InstalledRuntime> {
  InstalledRuntime(std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector,
                   expo_jsi_runtime_handle runtimeHandle,
                   expo::modules::dotnet::ManagedModuleConfig moduleConfig)
    : connector(std::move(connector)),
      runtimeHandle(runtimeHandle),
      moduleConfig(std::move(moduleConfig))
  {
  }

  ~InstalledRuntime()
  {
    teardown();
  }

  bool registerModules(std::wstring &error)
  {
    auto entryPoints = expo::modules::dotnet::resolveRuntimeContextEntryPoints(moduleConfig);

    if (entryPoints.createRuntimeContextV3 == nullptr ||
        entryPoints.teardownRuntimeContext == nullptr) {
      error = expo::modules::dotnet::managedLoaderLastError();
      if (error.empty()) {
        error =
          L"Failed to resolve structured create/teardown runtime context entry points. Rebuild "
          L"the managed ExpoDotnetHost artifacts with expo-modules-dotnet-autolinking.";
      }
      return false;
    }

    // The struct borrows these strings for the duration of the create call, so
    // they must live in this frame until the call returns.
    const auto appDirectories = resolveAppDirectories();
    const auto buildVersion = resolveNativeBuildVersion();
    expo::modules::dotnet::expo_dotnet_host_context hostContext{};
    hostContext.size = sizeof(hostContext);
    hostContext.version = EXPO_DOTNET_HOST_ABI_VERSION;
    if (appDirectories.isConfigured()) {
      hostContext.cache_directory =
        reinterpret_cast<const uint8_t *>(appDirectories.cacheDirectory.data());
      hostContext.cache_directory_length =
        static_cast<int32_t>(appDirectories.cacheDirectory.size());
      hostContext.persistent_files_directory =
        reinterpret_cast<const uint8_t *>(appDirectories.persistentFilesDirectory.data());
      hostContext.persistent_files_directory_length =
        static_cast<int32_t>(appDirectories.persistentFilesDirectory.size());
      logMessage(L"[ExpoModulesDotnet] App directories configured: cache=app-scoped, "
                 L"persistent=app-scoped.");
    }
    if (buildVersion.has_value()) {
      hostContext.native_build_version = reinterpret_cast<const uint8_t *>(buildVersion->data());
      hostContext.native_build_version_length = static_cast<int32_t>(buildVersion->size());
    }

    expo::modules::dotnet::RuntimeContextResult result;
    entryPoints.createRuntimeContextV3(
      expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, &hostContext, &result);
    if (result.ok == 0 || result.runtimeContext == nullptr) {
      error = takeRuntimeContextError(result.error);
      if (error.empty()) {
        error = L"Managed runtime context registration failed.";
      }
      return false;
    }

    std::lock_guard<std::mutex> lock(mutex);
    managedRuntimeContext = result.runtimeContext;
    teardownRuntimeContext = entryPoints.teardownRuntimeContext;
    registered = true;
    return true;
  }

  bool isRegistered() const
  {
    std::lock_guard<std::mutex> lock(mutex);
    return registered;
  }

  void subscribeToInstanceDestroyed(
    winrt::Microsoft::ReactNative::ReactNotificationService const &notifications)
  {
    auto notificationId = winrt::Microsoft::ReactNative::ReactNotificationId<
      winrt::Microsoft::ReactNative::InstanceDestroyedEventArgs>{L"ReactNative.InstanceSettings",
                                                                 L"InstanceDestroyed"};

    std::weak_ptr<InstalledRuntime> weakRuntime{shared_from_this()};
    auto subscription = winrt::Microsoft::ReactNative::ReactNotificationService::Subscribe(
      notifications.Handle(),
      notificationId,
      [weakRuntime](
        winrt::Windows::Foundation::IInspectable const &,
        winrt::Microsoft::ReactNative::ReactNotificationArgs<
          winrt::Microsoft::ReactNative::InstanceDestroyedEventArgs> const &args) noexcept {
        if (auto runtime = weakRuntime.lock()) {
          runtime->teardown();
        }
        args.Subscription().Unsubscribe();
      });

    std::lock_guard<std::mutex> lock(mutex);
    destroyedSubscription = std::move(subscription);
  }

  void teardown() noexcept
  {
    std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connectorToRelease;
    expo_jsi_runtime_handle runtimeHandleToRelease = nullptr;
    void *managedRuntimeContextToTeardown = nullptr;
    expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContextFn = nullptr;
    winrt::Microsoft::ReactNative::ReactNotificationSubscription subscriptionToRelease{nullptr};

    {
      std::lock_guard<std::mutex> lock(mutex);
      if (tornDown) {
        return;
      }

      tornDown = true;
      registered = false;
      connectorToRelease = std::move(connector);
      managedRuntimeContextToTeardown = managedRuntimeContext;
      managedRuntimeContext = nullptr;
      teardownRuntimeContextFn = teardownRuntimeContext;
      teardownRuntimeContext = nullptr;
      runtimeHandleToRelease = runtimeHandle;
      runtimeHandle = nullptr;
      subscriptionToRelease = std::move(destroyedSubscription);
      destroyedSubscription = nullptr;
    }

    if (runtimeHandleToRelease != nullptr) {
      expo::dotnet::prepareRuntimeHandleForInvalidation(runtimeHandleToRelease);
    }
    if (connectorToRelease != nullptr) {
      connectorToRelease->invalidate();
    }
    if (teardownRuntimeContextFn != nullptr && managedRuntimeContextToTeardown != nullptr) {
      teardownRuntimeContextFn(managedRuntimeContextToTeardown);
    }
    if (runtimeHandleToRelease != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandleToRelease);
    }
    subscriptionToRelease.Unsubscribe();
  }

  mutable std::mutex mutex;
  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig;
  void *managedRuntimeContext = nullptr;
  expo::modules::dotnet::TeardownRuntimeContextFn teardownRuntimeContext = nullptr;
  bool registered = false;
  bool tornDown = false;
  winrt::Microsoft::ReactNative::ReactNotificationSubscription destroyedSubscription{nullptr};
};

struct ExpoModulesDotnetInstaller::InstallerState final {
  std::mutex mutex;
  std::shared_ptr<InstalledRuntime> installedRuntime;
  bool installStarted = false;
  bool registered = false;
  std::wstring lastError;
};

void ExpoModulesDotnetInstaller::Initialize(
  winrt::Microsoft::ReactNative::ReactContext const &reactContext,
  facebook::jsi::Runtime &runtime) noexcept
{
  auto state = std::make_shared<InstallerState>();
  state_ = state;

  auto callInvoker = reactContext.CallInvoker();
  if (callInvoker == nullptr) {
    std::lock_guard<std::mutex> lock(state->mutex);
    state->lastError = L"RNW ReactContext did not provide a CallInvoker.";
    logMessage(L"[ExpoModulesDotnet] RNW ReactContext did not provide a CallInvoker.");
    return;
  }

  {
    std::lock_guard<std::mutex> lock(state->mutex);
    state->installStarted = true;
  }

  try {
    auto connector =
      std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
    auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
    auto moduleConfig = expo::modules::dotnet::loadManagedHostConfig();

    auto installedRuntime = std::make_shared<InstalledRuntime>(
      std::move(connector), runtimeHandle, std::move(moduleConfig));

    std::wstring registrationError;
    if (!installedRuntime->registerModules(registrationError)) {
      if (registrationError.empty()) {
        throw std::runtime_error("Failed to register managed runtime context.");
      }
      throw std::runtime_error(toUtf8(registrationError).c_str());
    }

    installedRuntime->subscribeToInstanceDestroyed(reactContext.Notifications());

    {
      std::lock_guard<std::mutex> lock(state->mutex);
      state->installedRuntime = std::move(installedRuntime);
      state->registered = state->installedRuntime->isRegistered();
      state->lastError.clear();
    }

    logMessage(L"[ExpoModulesDotnet] Windows managed modules registered.");
  } catch (const std::exception &ex) {
    std::wstring message = L"[ExpoModulesDotnet] Windows module registration failed: ";
    message += toWide(ex.what());
    {
      std::lock_guard<std::mutex> lock(state->mutex);
      state->lastError = message;
      state->registered = false;
    }
    logMessage(message);
  } catch (...) {
    const std::wstring message =
      L"[ExpoModulesDotnet] Windows module registration failed with an unknown exception.";
    {
      std::lock_guard<std::mutex> lock(state->mutex);
      state->lastError = message;
      state->registered = false;
    }
    logMessage(message);
  }
}

bool ExpoModulesDotnetInstaller::installModules() noexcept
{
  auto state = state_;
  if (state == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Windows installer has not been initialized.");
    return false;
  }

  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->registered && state->installedRuntime != nullptr &&
      state->installedRuntime->isRegistered()) {
    return true;
  }

  if (!state->lastError.empty()) {
    logMessage(state->lastError);
  } else if (state->installStarted) {
    logMessage(L"[ExpoModulesDotnet] Windows module registration is still pending.");
  } else {
    logMessage(L"[ExpoModulesDotnet] Windows module registration has not started.");
  }
  return false;
}

std::string ExpoModulesDotnetInstaller::getLastError() noexcept
{
  auto state = state_;
  if (state == nullptr) {
    return "Windows installer has not been initialized.";
  }

  std::lock_guard<std::mutex> lock(state->mutex);
  if (!state->lastError.empty()) {
    return toUtf8(state->lastError);
  }
  if (state->registered && state->installedRuntime != nullptr &&
      state->installedRuntime->isRegistered()) {
    return "";
  }
  if (state->installStarted) {
    return "Windows module registration did not complete.";
  }
  return "Windows module registration has not started.";
}

} // namespace winrt::ExpoModulesDotnet
