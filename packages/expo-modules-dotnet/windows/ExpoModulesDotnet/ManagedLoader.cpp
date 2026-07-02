#include "pch.h"

#include "ManagedLoader.h"

#include "ManagedHostFxr.h"

#include <array>
#include <filesystem>
#include <string>

namespace expo::modules::dotnet {
namespace {

constexpr const wchar_t *kManagedSubdirectory = L"Managed";
constexpr const wchar_t *kRegisterModulesSymbol = L"example_module_register_modules";
constexpr const wchar_t *kEntryPointType = L"ExampleModule.EntryPoints, ExampleModule";
constexpr const wchar_t *kEntryPointMethod = L"RegisterModules";

void logMessage(const wchar_t *message)
{
  OutputDebugStringW(message);
  OutputDebugStringW(L"\n");
}

std::filesystem::path moduleDirectory()
{
  static const int anchor = 0;
  HMODULE module = nullptr;
  GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                       GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                     reinterpret_cast<LPCWSTR>(&anchor),
                     &module);

  std::array<wchar_t, MAX_PATH> path{};
  GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
  return std::filesystem::path(path.data()).parent_path();
}

std::filesystem::path executableDirectory()
{
  std::array<wchar_t, MAX_PATH> path{};
  GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
  return std::filesystem::path(path.data()).parent_path();
}

std::filesystem::path managedDirectory()
{
  const std::array<std::filesystem::path, 3> candidates{
    executableDirectory() / kManagedSubdirectory,
    moduleDirectory() / kManagedSubdirectory,
    moduleDirectory() / L".." / kManagedSubdirectory,
  };

  for (const auto &candidate : candidates) {
    if (std::filesystem::exists(candidate)) {
      return std::filesystem::weakly_canonical(candidate);
    }
  }

  return candidates.front();
}

std::wstring pathForManagedArtifact(const wchar_t *name)
{
  auto path = managedDirectory() / name;
  if (!std::filesystem::exists(path)) {
    std::wstring message = L"[ExpoModulesDotnet] Missing managed artifact ";
    message += path.wstring();
    message += L". Run apps/desktop-app/scripts/build-managed.ps1 before launching the Windows app.";
    logMessage(message.c_str());
  }
  return path.wstring();
}

ManagedLoaderKind parseLoaderKind(std::wstring loader)
{
  for (auto &ch : loader) {
    ch = static_cast<wchar_t>(towlower(ch));
  }
  if (loader == L"nativeaot") {
    return ManagedLoaderKind::NativeAot;
  }
  return ManagedLoaderKind::HostFxr;
}

std::wstring loaderKindFromEnvironment()
{
  std::array<wchar_t, 64> loader{};
  auto size = GetEnvironmentVariableW(L"EXPO_DOTNET_LOADER",
                                      loader.data(),
                                      static_cast<DWORD>(loader.size()));
  if (size == 0) {
    size = GetEnvironmentVariableW(L"EXPO_JSI_DOTNET_LOADER",
                                   loader.data(),
                                   static_cast<DWORD>(loader.size()));
  }
  return size == 0 ? std::wstring() : std::wstring(loader.data(), size);
}

ManagedLoaderKind selectedLoaderKind()
{
  auto loader = loaderKindFromEnvironment();
  return loader.empty() ? ManagedLoaderKind::HostFxr : parseLoaderKind(loader);
}

HMODULE openLibrary(const std::wstring &path)
{
  if (path.empty()) {
    return nullptr;
  }
  return LoadLibraryW(path.c_str());
}

HMODULE openHostFxr(const ManagedModuleConfig &config)
{
  auto nethost = openLibrary(config.nethostPath);
  if (nethost == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to load nethost.dll.");
    return nullptr;
  }

  auto getHostFxrPath =
    reinterpret_cast<get_hostfxr_path_fn>(GetProcAddress(nethost, "get_hostfxr_path"));
  if (getHostFxrPath == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to resolve get_hostfxr_path.");
    return nullptr;
  }

  std::array<char_t, MAX_PATH> hostFxrPath{};
  size_t hostFxrPathSize = hostFxrPath.size();
  auto status = getHostFxrPath(hostFxrPath.data(), &hostFxrPathSize, nullptr);
  if (status != 0) {
    logMessage(L"[ExpoModulesDotnet] get_hostfxr_path failed.");
    return nullptr;
  }

  auto hostFxr = LoadLibraryW(hostFxrPath.data());
  if (hostFxr == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to load hostfxr.");
  }
  return hostFxr;
}

RegisterModulesFn resolveNativeAotRegisterModules(const ManagedModuleConfig &config)
{
  auto library = openLibrary(config.nativeLibraryPath);
  if (library == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to load NativeAOT ExampleModule library.");
    return nullptr;
  }

  auto symbol = GetProcAddress(library, "example_module_register_modules");
  if (symbol == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to resolve example_module_register_modules.");
    return nullptr;
  }

  return reinterpret_cast<RegisterModulesFn>(symbol);
}

RegisterModulesFn resolveHostFxrRegisterModules(const ManagedModuleConfig &config)
{
  auto hostFxr = openHostFxr(config);
  if (hostFxr == nullptr) {
    return nullptr;
  }

  auto initializeForRuntimeConfig =
    reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
      GetProcAddress(hostFxr, "hostfxr_initialize_for_runtime_config"));
  auto getRuntimeDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
    GetProcAddress(hostFxr, "hostfxr_get_runtime_delegate"));
  auto closeHostContext =
    reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostFxr, "hostfxr_close"));

  if (initializeForRuntimeConfig == nullptr || getRuntimeDelegate == nullptr ||
      closeHostContext == nullptr) {
    logMessage(L"[ExpoModulesDotnet] Failed to resolve required hostfxr exports.");
    return nullptr;
  }

  hostfxr_handle hostContext = nullptr;
  auto status =
    initializeForRuntimeConfig(config.runtimeConfigPath.c_str(), nullptr, &hostContext);
  if (status < 0 || status > 2 || hostContext == nullptr) {
    logMessage(L"[ExpoModulesDotnet] hostfxr_initialize_for_runtime_config failed.");
    return nullptr;
  }

  void *loadAssemblyDelegate = nullptr;
  status = getRuntimeDelegate(hostContext,
                              hdt_load_assembly_and_get_function_pointer,
                              &loadAssemblyDelegate);
  closeHostContext(hostContext);
  if (status != 0 || loadAssemblyDelegate == nullptr) {
    logMessage(L"[ExpoModulesDotnet] hostfxr_get_runtime_delegate failed.");
    return nullptr;
  }

  auto loadAssemblyAndGetFunctionPointer =
    reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadAssemblyDelegate);

  void *registerModules = nullptr;
  status = loadAssemblyAndGetFunctionPointer(config.assemblyPath.c_str(),
                                             config.typeName.c_str(),
                                             config.methodName.c_str(),
                                             unmanagedCallersOnlyMethod,
                                             nullptr,
                                             &registerModules);
  if (status != 0 || registerModules == nullptr) {
    logMessage(L"[ExpoModulesDotnet] load_assembly_and_get_function_pointer failed.");
    return nullptr;
  }

  return reinterpret_cast<RegisterModulesFn>(registerModules);
}

} // namespace

ManagedModuleConfig loadExampleModuleConfig()
{
  ManagedModuleConfig config;
  config.loaderKind = selectedLoaderKind();
  config.typeName = kEntryPointType;
  config.methodName = kEntryPointMethod;

  if (config.loaderKind == ManagedLoaderKind::NativeAot) {
    config.nativeLibraryPath = pathForManagedArtifact(L"ExampleModule.dll");
  } else {
    config.assemblyPath = pathForManagedArtifact(L"ExampleModule.dll");
    config.runtimeConfigPath = pathForManagedArtifact(L"ExampleModule.runtimeconfig.json");
    config.nethostPath = pathForManagedArtifact(L"nethost.dll");
  }

  return config;
}

const wchar_t *managedLoaderKindName(ManagedLoaderKind loaderKind)
{
  switch (loaderKind) {
    case ManagedLoaderKind::HostFxr:
      return L"HostFXR";
    case ManagedLoaderKind::NativeAot:
      return L"NativeAOT";
  }
  return L"Unknown";
}

RegisterModulesFn resolveRegisterModules(const ManagedModuleConfig &config)
{
  switch (config.loaderKind) {
    case ManagedLoaderKind::NativeAot:
      return resolveNativeAotRegisterModules(config);
    case ManagedLoaderKind::HostFxr:
      return resolveHostFxrRegisterModules(config);
  }
  return nullptr;
}

} // namespace expo::modules::dotnet
