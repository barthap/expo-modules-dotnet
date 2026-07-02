#pragma once

#include "ReactPackageProvider.g.h"

namespace winrt::ExpoModulesDotnet::implementation
{
struct ReactPackageProvider : ReactPackageProviderT<ReactPackageProvider>
{
  void CreatePackage(winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept;
};
} // namespace winrt::ExpoModulesDotnet::implementation

namespace winrt::ExpoModulesDotnet::factory_implementation
{
struct ReactPackageProvider : ReactPackageProviderT<ReactPackageProvider, implementation::ReactPackageProvider>
{
};
} // namespace winrt::ExpoModulesDotnet::factory_implementation
