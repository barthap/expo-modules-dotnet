#pragma once

#include "ReactPackageProvider.g.h"

namespace winrt::ExpoModulesDotnetWindows::implementation {
struct ReactPackageProvider : ReactPackageProviderT<ReactPackageProvider> {
  void CreatePackage(
    winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept;
};
} // namespace winrt::ExpoModulesDotnetWindows::implementation

namespace winrt::ExpoModulesDotnetWindows::factory_implementation {
struct ReactPackageProvider
  : ReactPackageProviderT<ReactPackageProvider, implementation::ReactPackageProvider> {};
} // namespace winrt::ExpoModulesDotnetWindows::factory_implementation
