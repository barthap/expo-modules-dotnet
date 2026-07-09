#include "pch.h"

#include "ExpoModulesDotnetInstaller.h"

#include <ReactNotificationService.h>

#include <sstream>
#include <stdexcept>
#include <utility>

namespace winrt::ExpoModulesDotnet {
namespace {

std::mutex g_currentRuntimeMutex;
void *g_currentManagedRuntimeContext = nullptr;

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

    if (entryPoints.createRuntimeContext == nullptr ||
        entryPoints.teardownRuntimeContext == nullptr) {
      error = expo::modules::dotnet::managedLoaderLastError();
      if (error.empty()) {
        error = L"Managed runtime context entry points were not resolved.";
      }
      return false;
    }

    auto runtimeContext =
      entryPoints.createRuntimeContext(expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle);
    if (runtimeContext == nullptr) {
      error = L"Managed CreateRuntimeContext returned a null runtime context.";
      return false;
    }

    std::lock_guard<std::mutex> lock(mutex);
    managedRuntimeContext = runtimeContext;
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
      if (connector != nullptr) {
        connector->invalidate();
      }
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

    if (teardownRuntimeContextFn != nullptr && managedRuntimeContextToTeardown != nullptr) {
      teardownRuntimeContextFn(managedRuntimeContextToTeardown);
    }
    {
      std::lock_guard<std::mutex> currentLock(g_currentRuntimeMutex);
      if (g_currentManagedRuntimeContext == managedRuntimeContextToTeardown) {
        g_currentManagedRuntimeContext = nullptr;
      }
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
      state->installedRuntime = installedRuntime;
      state->registered = state->installedRuntime->isRegistered();
      state->lastError.clear();
    }
    {
      std::lock_guard<std::mutex> lock(g_currentRuntimeMutex);
      g_currentManagedRuntimeContext = installedRuntime->managedRuntimeContext;
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

void *CurrentManagedRuntimeContext() noexcept
{
  std::lock_guard<std::mutex> lock(g_currentRuntimeMutex);
  return g_currentManagedRuntimeContext;
}

} // namespace winrt::ExpoModulesDotnet

extern "C" __declspec(dllexport) void *expo_modules_dotnet_current_runtime_context() noexcept
{
  return winrt::ExpoModulesDotnet::CurrentManagedRuntimeContext();
}
