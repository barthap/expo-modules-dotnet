#include "pch.h"

#include "ReactPackageProvider.h"
#if __has_include("ReactPackageProvider.g.cpp")
#include "ReactPackageProvider.g.cpp"
#endif

#include "ExpoDotnetViewManager.h"

namespace winrt::ExpoModulesDotnetWindows::implementation {

void ReactPackageProvider::CreatePackage(
  winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept
{
  expo::modules::dotnet::windows::RegisterDotnetViewComponents(packageBuilder);
}

} // namespace winrt::ExpoModulesDotnetWindows::implementation
