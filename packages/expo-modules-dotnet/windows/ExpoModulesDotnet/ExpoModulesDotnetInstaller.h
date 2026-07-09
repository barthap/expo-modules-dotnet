#pragma once

#include <memory>
#include <mutex>
#include <string>

#include <NativeModules.h>

#include "ManagedLoader.h"
#include "ReactNativeRuntimeConnector.h"

namespace winrt::ExpoModulesDotnet {

void *CurrentManagedRuntimeContext() noexcept;

REACT_MODULE(ExpoModulesDotnetInstaller)
struct ExpoModulesDotnetInstaller {
  REACT_INIT(Initialize)
  void Initialize(winrt::Microsoft::ReactNative::ReactContext const &reactContext,
                  facebook::jsi::Runtime &runtime) noexcept;

  REACT_SYNC_METHOD(installModules)
  bool installModules() noexcept;

  REACT_SYNC_METHOD(getLastError)
  std::string getLastError() noexcept;

private:
  struct InstalledRuntime;
  struct InstallerState;

  std::shared_ptr<InstallerState> state_;
};

} // namespace winrt::ExpoModulesDotnet

extern "C" __declspec(dllexport) void *expo_modules_dotnet_current_runtime_context() noexcept;
