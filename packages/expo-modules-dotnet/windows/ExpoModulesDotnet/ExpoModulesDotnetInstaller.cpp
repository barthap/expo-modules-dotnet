#include "pch.h"

#include "ExpoModulesDotnetInstaller.h"

#include <sstream>
#include <utility>

namespace winrt::ExpoModulesDotnet
{
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

} // namespace

struct ExpoModulesDotnetInstaller::InstalledRuntime final
{
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
    if (runtimeHandle != nullptr) {
      expo::dotnet::releaseReactNativeRuntimeHandle(runtimeHandle);
    }
    if (connector != nullptr) {
      connector->invalidate();
    }
  }

  std::unique_ptr<expo::dotnet::ReactNativeRuntimeConnector> connector;
  expo_jsi_runtime_handle runtimeHandle = nullptr;
  expo::modules::dotnet::ManagedModuleConfig moduleConfig;
};

struct ExpoModulesDotnetInstaller::InstallerState final
{
  std::mutex mutex;
  std::shared_ptr<InstalledRuntime> installedRuntime;
  bool installStarted = false;
  bool registered = false;
  std::wstring lastError;
};

void ExpoModulesDotnetInstaller::Initialize(
  winrt::Microsoft::ReactNative::ReactContext const &reactContext) noexcept
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

  callInvoker->invokeAsync(
    [state, callInvoker](facebook::jsi::Runtime &runtime) mutable {
      try {
        auto connector =
          std::make_unique<expo::dotnet::ReactNativeRuntimeConnector>(runtime, callInvoker);
        auto runtimeHandle = expo::dotnet::createReactNativeRuntimeHandle(*connector);
        auto moduleConfig = expo::modules::dotnet::loadExampleModuleConfig();

        auto installedRuntime = std::make_shared<InstalledRuntime>(
          std::move(connector), runtimeHandle, std::move(moduleConfig));

        auto registerModules =
          expo::modules::dotnet::resolveRegisterModules(installedRuntime->moduleConfig);
        if (registerModules == nullptr) {
          throw std::runtime_error("Failed to resolve ExampleModule registration entry point.");
        }

        auto status = registerModules(expo::dotnet::reactNativeExpoJsiApi(),
                                      installedRuntime->runtimeHandle);
        if (status != 0) {
          throw std::runtime_error("ExampleModule registration failed.");
        }

        {
          std::lock_guard<std::mutex> lock(state->mutex);
          state->installedRuntime = std::move(installedRuntime);
          state->registered = true;
          state->lastError.clear();
        }

        logMessage(L"[ExpoModulesDotnet] Windows ExampleModule.add module registered.");
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
    });
}

bool ExpoModulesDotnetInstaller::installModules() noexcept
{
  auto state = state_;
  if (state == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Windows installer has not been initialized.");
    return false;
  }

  std::lock_guard<std::mutex> lock(state->mutex);
  if (state->registered) {
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

} // namespace winrt::ExpoModulesDotnet
