#include "pch.h"

#include "ManagedViewHost.h"

#include <sstream>

namespace expo::modules::dotnet::windows {
namespace {

using CurrentRuntimeContextFn = void *(*)();

void logMessage(const std::string &message) noexcept
{
  OutputDebugStringA("[ExpoModulesDotnetWindows] ");
  OutputDebugStringA(message.c_str());
  OutputDebugStringA("\n");
}

std::string toUtf8(winrt::hstring const &value)
{
  return winrt::to_string(value);
}

CurrentRuntimeContextFn resolveCurrentRuntimeContext()
{
  HMODULE module = GetModuleHandleW(L"ExpoModulesDotnet.dll");
  if (module == nullptr) {
    module = LoadLibraryW(L"ExpoModulesDotnet.dll");
  }
  if (module == nullptr) {
    return nullptr;
  }

  return reinterpret_cast<CurrentRuntimeContextFn>(
    GetProcAddress(module, "expo_modules_dotnet_current_runtime_context"));
}

void *currentRuntimeContext() noexcept
{
  static auto runtimeContextFn = resolveCurrentRuntimeContext();
  return runtimeContextFn == nullptr ? nullptr : runtimeContextFn();
}

std::vector<DotnetViewDefinition> parseViewMetadata(const std::string &json)
{
  std::vector<DotnetViewDefinition> views;
  if (json.empty()) {
    return views;
  }

  auto array = winrt::Windows::Data::Json::JsonArray::Parse(winrt::to_hstring(json));
  for (auto const &item : array) {
    auto viewObject = item.GetObject();
    DotnetViewDefinition view;
    view.moduleName = toUtf8(viewObject.GetNamedString(L"ModuleName", L""));
    view.componentName = toUtf8(viewObject.GetNamedString(L"ComponentName", L""));

    if (viewObject.HasKey(L"Props")) {
      for (auto const &propItem : viewObject.GetNamedArray(L"Props")) {
        auto propObject = propItem.GetObject();
        DotnetViewPropDefinition prop;
        prop.name = toUtf8(propObject.GetNamedString(L"Name", L""));
        prop.kind = toUtf8(propObject.GetNamedString(L"Kind", L""));
        if (!prop.name.empty()) {
          view.props.push_back(std::move(prop));
        }
      }
    }

    if (!view.componentName.empty()) {
      views.push_back(std::move(view));
    }
  }
  return views;
}

bool hasRequiredEntryPoints(const expo::modules::dotnet::ManagedWindowsViewEntryPoints &entryPoints)
{
  return entryPoints.getViewMetadata != nullptr && entryPoints.freeBuffer != nullptr &&
    entryPoints.createView != nullptr && entryPoints.initializeComposition != nullptr &&
    entryPoints.updateLayout != nullptr && entryPoints.updateStringProp != nullptr &&
    entryPoints.destroyView != nullptr;
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

void *ManagedViewHost::CreateView(const std::string &componentName)
{
  EnsureInitialized();
  auto *runtimeContext = currentRuntimeContext();
  if (runtimeContext == nullptr || entryPoints_.createView == nullptr) {
    return nullptr;
  }

  return entryPoints_.createView(
    runtimeContext,
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
  void *viewHandle,
  const std::string &componentName,
  const std::string &propName,
  const std::optional<std::string> &value) noexcept
{
  try {
    EnsureInitialized();
    auto *runtimeContext = currentRuntimeContext();
    if (runtimeContext == nullptr || viewHandle == nullptr || entryPoints_.updateStringProp == nullptr) {
      return;
    }

    const auto *valueData =
      value.has_value() ? reinterpret_cast<const uint8_t *>(value->data()) : nullptr;
    const auto valueLength = value.has_value() ? static_cast<int>(value->size()) : 0;
    entryPoints_.updateStringProp(
      runtimeContext,
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

    uint8_t *buffer = nullptr;
    int length = 0;
    if (entryPoints_.getViewMetadata(&buffer, &length) != 0 || buffer == nullptr || length <= 0) {
      logMessage("Managed view metadata is empty or unavailable.");
      return;
    }

    std::string json(reinterpret_cast<const char *>(buffer), static_cast<size_t>(length));
    entryPoints_.freeBuffer(buffer);
    viewDefinitions_ = parseViewMetadata(json);

    std::ostringstream message;
    message << "Loaded " << viewDefinitions_.size() << " managed view definition(s).";
    logMessage(message.str());
  });
}

} // namespace expo::modules::dotnet::windows
