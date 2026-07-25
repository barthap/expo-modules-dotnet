#include "pch.h"

#include "ExpoModulesDotnetInstaller.h"

#include <ReactNotificationService.h>

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

    if (entryPoints.createRuntimeContextV2 == nullptr ||
        entryPoints.teardownRuntimeContext == nullptr) {
      error = expo::modules::dotnet::managedLoaderLastError();
      if (error.empty()) {
        error =
          L"Failed to resolve structured create/teardown runtime context entry points. Rebuild "
          L"the managed ExpoDotnetHost artifacts with expo-modules-dotnet-autolinking.";
      }
      return false;
    }

    expo::modules::dotnet::RuntimeContextResult result;
    // A null app-directories pointer means both directories are unconfigured.
    entryPoints.createRuntimeContextV2(
      expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, nullptr, &result);
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
