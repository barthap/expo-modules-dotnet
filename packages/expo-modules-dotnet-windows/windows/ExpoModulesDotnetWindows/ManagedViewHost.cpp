#include "pch.h"

#include "ManagedViewHost.h"

#include <sstream>

namespace expo::modules::dotnet::windows {
namespace {

void logMessage(const std::string &message) noexcept
{
  OutputDebugStringA("[ExpoModulesDotnetWindows] ");
  OutputDebugStringA(message.c_str());
  OutputDebugStringA("\n");
}

winrt::Microsoft::ReactNative::ReactPropertyId<int64_t> runtimeContextProperty() noexcept
{
  static const winrt::Microsoft::ReactNative::ReactPropertyId<int64_t> property{
    L"Expo.ModulesDotnet", L"RuntimeContext"};
  return property;
}

void *RuntimeContextFromReactContext(
  winrt::Microsoft::ReactNative::IReactContext const &reactContext) noexcept
{
  auto value = winrt::Microsoft::ReactNative::ReactPropertyBag(reactContext.Properties())
                 .Get(runtimeContextProperty());
  return value.has_value() ? reinterpret_cast<void *>(*value) : nullptr;
}

std::string readLastViewError(
  const expo::modules::dotnet::ManagedWindowsViewEntryPoints &entryPoints)
{
  if (entryPoints.getViewLastError == nullptr) {
    return "";
  }

  const auto length = entryPoints.getViewLastError(nullptr, 0);
  if (length <= 0) {
    return "";
  }

  std::string value(static_cast<size_t>(length), '\0');
  return entryPoints.getViewLastError(reinterpret_cast<uint8_t *>(value.data()),
                                      static_cast<int>(value.size())) == length
           ? value
           : "";
}

bool readMetadataString(expo::modules::dotnet::WindowsGetViewStringFn getString,
                        int viewIndex,
                        std::string &value)
{
  const auto length = getString(viewIndex, nullptr, 0);
  if (length < 0) {
    return false;
  }
  if (length == 0) {
    value.clear();
    return true;
  }
  value.resize(static_cast<size_t>(length));
  return getString(viewIndex,
                   reinterpret_cast<uint8_t *>(value.data()),
                   static_cast<int>(value.size())) == length;
}

bool readMetadataPropString(expo::modules::dotnet::WindowsGetViewPropNameFn getString,
                            int viewIndex,
                            int propIndex,
                            std::string &value)
{
  const auto length = getString(viewIndex, propIndex, nullptr, 0);
  if (length < 0) {
    return false;
  }
  if (length == 0) {
    value.clear();
    return true;
  }
  value.resize(static_cast<size_t>(length));
  return getString(viewIndex,
                   propIndex,
                   reinterpret_cast<uint8_t *>(value.data()),
                   static_cast<int>(value.size())) == length;
}

std::vector<DotnetViewDefinition> readViewMetadata(
  const expo::modules::dotnet::ManagedWindowsViewEntryPoints &entryPoints)
{
  std::vector<DotnetViewDefinition> views;
  const auto viewCount = entryPoints.getViewCount();
  if (viewCount < 0) {
    auto error = readLastViewError(entryPoints);
    if (error.empty()) {
      error = "managed view metadata count failed.";
    }
    logMessage("Managed view metadata is unavailable: " + error);
    return views;
  }
  if (viewCount == 0) {
    return views;
  }

  for (int viewIndex = 0; viewIndex < viewCount; viewIndex++) {
    DotnetViewDefinition view;
    if (!readMetadataString(entryPoints.getViewModuleName, viewIndex, view.moduleName) ||
        !readMetadataString(entryPoints.getViewComponentName, viewIndex, view.componentName) ||
        view.componentName.empty()) {
      continue;
    }

    const auto propCount = entryPoints.getViewPropCount(viewIndex);
    for (int propIndex = 0; propIndex < propCount; propIndex++) {
      DotnetViewPropDefinition prop;
      if (!readMetadataPropString(entryPoints.getViewPropName, viewIndex, propIndex, prop.name) ||
          prop.name.empty()) {
        continue;
      }
      const auto kind = entryPoints.getViewPropKind(viewIndex, propIndex);
      prop.kind = kind == 0 ? "String" : "";
      view.props.push_back(std::move(prop));
    }

    views.push_back(std::move(view));
  }
  return views;
}

bool hasRequiredEntryPoints(const expo::modules::dotnet::ManagedWindowsViewEntryPoints &entryPoints)
{
  return entryPoints.getViewLastError != nullptr && entryPoints.getViewCount != nullptr &&
         entryPoints.getViewModuleName != nullptr && entryPoints.getViewComponentName != nullptr &&
         entryPoints.getViewPropCount != nullptr && entryPoints.getViewPropName != nullptr &&
         entryPoints.getViewPropKind != nullptr && entryPoints.createView != nullptr &&
         entryPoints.initializeComposition != nullptr && entryPoints.updateLayout != nullptr &&
         entryPoints.updateStringProp != nullptr && entryPoints.destroyView != nullptr;
}

} // namespace

ManagedViewHost &ManagedViewHost::Instance()
{
  static ManagedViewHost host;
  return host;
}

const std::vector<DotnetViewDefinition> &ManagedViewHost::ViewDefinitions()
{
  EnsureInitialized();
  return viewDefinitions_;
}

void *ManagedViewHost::CreateView(winrt::Microsoft::ReactNative::IReactContext const &reactContext,
                                  const std::string &componentName)
{
  EnsureInitialized();
  auto *runtimeContext = RuntimeContextFromReactContext(reactContext);
  if (runtimeContext == nullptr || entryPoints_.createView == nullptr) {
    return nullptr;
  }

  return entryPoints_.createView(runtimeContext,
                                 reinterpret_cast<const uint8_t *>(componentName.data()),
                                 static_cast<int>(componentName.size()));
}

intptr_t ManagedViewHost::InitializeComposition(void *viewHandle, intptr_t compositor) noexcept
{
  try {
    EnsureInitialized();
    if (viewHandle == nullptr || entryPoints_.initializeComposition == nullptr) {
      return 0;
    }
    return entryPoints_.initializeComposition(viewHandle, compositor);
  } catch (const std::exception &ex) {
    logMessage(std::string("InitializeComposition failed: ") + ex.what());
    return 0;
  } catch (...) {
    logMessage("InitializeComposition failed.");
    return 0;
  }
}

void ManagedViewHost::UpdateLayout(void *viewHandle, float width, float height) noexcept
{
  try {
    EnsureInitialized();
    if (viewHandle != nullptr && entryPoints_.updateLayout != nullptr) {
      entryPoints_.updateLayout(viewHandle, width, height);
    }
  } catch (const std::exception &ex) {
    logMessage(std::string("UpdateLayout failed: ") + ex.what());
  } catch (...) {
    logMessage("UpdateLayout failed.");
  }
}

void ManagedViewHost::UpdateStringProp(
  winrt::Microsoft::ReactNative::IReactContext const &reactContext,
  void *viewHandle,
  const std::string &componentName,
  const std::string &propName,
  const std::optional<std::string> &value) noexcept
{
  try {
    EnsureInitialized();
    auto *runtimeContext = RuntimeContextFromReactContext(reactContext);
    if (runtimeContext == nullptr || viewHandle == nullptr ||
        entryPoints_.updateStringProp == nullptr) {
      return;
    }

    const auto *valueData =
      value.has_value() ? reinterpret_cast<const uint8_t *>(value->data()) : nullptr;
    const auto valueLength = value.has_value() ? static_cast<int>(value->size()) : 0;
    entryPoints_.updateStringProp(runtimeContext,
                                  viewHandle,
                                  reinterpret_cast<const uint8_t *>(componentName.data()),
                                  static_cast<int>(componentName.size()),
                                  reinterpret_cast<const uint8_t *>(propName.data()),
                                  static_cast<int>(propName.size()),
                                  valueData,
                                  valueLength);
  } catch (const std::exception &ex) {
    logMessage(std::string("UpdateStringProp failed: ") + ex.what());
  } catch (...) {
    logMessage("UpdateStringProp failed.");
  }
}

void ManagedViewHost::DestroyView(void *viewHandle) noexcept
{
  try {
    EnsureInitialized();
    if (viewHandle != nullptr && entryPoints_.destroyView != nullptr) {
      entryPoints_.destroyView(viewHandle);
    }
  } catch (const std::exception &ex) {
    logMessage(std::string("DestroyView failed: ") + ex.what());
  } catch (...) {
    logMessage("DestroyView failed.");
  }
}

void ManagedViewHost::EnsureInitialized()
{
  std::call_once(initFlag_, [this]() {
    auto config = expo::modules::dotnet::loadManagedHostConfig();
    entryPoints_ = expo::modules::dotnet::resolveWindowsViewEntryPoints(config);
    if (!hasRequiredEntryPoints(entryPoints_)) {
      auto error = expo::modules::dotnet::managedLoaderLastError();
      logMessage("Managed view entrypoints are unavailable: " + winrt::to_string(error));
      return;
    }

    viewDefinitions_ = readViewMetadata(entryPoints_);
    if (viewDefinitions_.empty()) {
      logMessage("Managed view metadata is empty or unavailable.");
      return;
    }

    std::ostringstream message;
    message << "Loaded " << viewDefinitions_.size() << " managed view definition(s).";
    logMessage(message.str());
  });
}

} // namespace expo::modules::dotnet::windows
