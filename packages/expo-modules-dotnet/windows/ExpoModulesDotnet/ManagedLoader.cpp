#include "pch.h"

#include "ManagedLoader.h"

#include "ManagedHostFxr.h"

#include <array>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <sstream>
#include <string>

namespace expo::modules::dotnet {
namespace {

constexpr const wchar_t *kManagedSubdirectory = L"Managed";
constexpr const wchar_t *kLoaderMarkerFile = L"ExpoDotnetHost.loader";
constexpr const char *kCreateRuntimeContextSymbol = "expo_dotnet_create_runtime_context_result";
constexpr const char *kTeardownRuntimeContextSymbol = "expo_dotnet_teardown_runtime_context";
constexpr const wchar_t *kEntryPointType =
  L"Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost";
constexpr const wchar_t *kCreateRuntimeContextMethod = L"CreateRuntimeContextResult";
constexpr const wchar_t *kTeardownRuntimeContextMethod = L"TeardownRuntimeContext";

std::mutex g_errorMutex;
std::wstring g_lastError;

void logMessage(const wchar_t *message)
{
  OutputDebugStringW(message);
  OutputDebugStringW(L"\n");
}

void setLastError(std::wstring message)
{
  logMessage(message.c_str());
  std::lock_guard<std::mutex> lock(g_errorMutex);
  g_lastError = std::move(message);
}

std::wstring statusMessage(const wchar_t *operation, int status)
{
  std::wstringstream message;
  message << L"[ExpoModulesDotnet] " << operation << L" failed with status " << status << L" (0x"
          << std::hex << static_cast<unsigned int>(status) << L").";
  return message.str();
}

std::wstring windowsErrorMessage(const wchar_t *operation, DWORD error)
{
  std::wstringstream message;
  message << L"[ExpoModulesDotnet] " << operation << L" failed with Windows error " << error
          << L" (0x" << std::hex << error << L").";
  return message.str();
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
    message +=
      L". Run the expo-modules-dotnet-autolinking link command (or a full app build, which runs "
      L"it as an MSBuild target) before launching the Windows app.";
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
  auto size = GetEnvironmentVariableW(
    L"EXPO_DOTNET_LOADER", loader.data(), static_cast<DWORD>(loader.size()));
  if (size == 0) {
    size = GetEnvironmentVariableW(
      L"EXPO_JSI_DOTNET_LOADER", loader.data(), static_cast<DWORD>(loader.size()));
  }
  return size == 0 ? std::wstring() : std::wstring(loader.data(), size);
}

std::wstring loaderKindFromMarker()
{
  std::ifstream marker(managedDirectory() / kLoaderMarkerFile);
  std::string value;
  if (!std::getline(marker, value)) {
    return L"";
  }
  return toWide(value.c_str());
}

ManagedLoaderKind selectedLoaderKind()
{
  auto loader = loaderKindFromEnvironment();
  if (loader.empty()) {
    loader = loaderKindFromMarker();
  }
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
    setLastError(windowsErrorMessage(L"LoadLibraryW(nethost.dll)", GetLastError()));
    return nullptr;
  }

  auto getHostFxrPath =
    reinterpret_cast<get_hostfxr_path_fn>(GetProcAddress(nethost, "get_hostfxr_path"));
  if (getHostFxrPath == nullptr) {
    setLastError(windowsErrorMessage(L"GetProcAddress(get_hostfxr_path)", GetLastError()));
    return nullptr;
  }

  std::array<char_t, MAX_PATH> hostFxrPath{};
  size_t hostFxrPathSize = hostFxrPath.size();
  auto status = getHostFxrPath(hostFxrPath.data(), &hostFxrPathSize, nullptr);
  if (status != 0) {
    setLastError(statusMessage(L"get_hostfxr_path", status));
    return nullptr;
  }

  auto hostFxr = LoadLibraryW(hostFxrPath.data());
  if (hostFxr == nullptr) {
    setLastError(windowsErrorMessage(L"LoadLibraryW(hostfxr)", GetLastError()));
  }
  return hostFxr;
}

void *resolveNativeAotSymbol(const ManagedModuleConfig &config, const char *symbolName)
{
  auto library = openLibrary(config.nativeLibraryPath);
  if (library == nullptr) {
    setLastError(windowsErrorMessage(L"LoadLibraryW(NativeAOT managed library)", GetLastError()));
    return nullptr;
  }

  auto symbol = GetProcAddress(library, symbolName);
  if (symbol == nullptr) {
    std::wstring operation = L"GetProcAddress(";
    operation += toWide(symbolName);
    operation += L")";
    setLastError(windowsErrorMessage(operation.c_str(), GetLastError()));
    return nullptr;
  }

  {
    std::lock_guard<std::mutex> lock(g_errorMutex);
    g_lastError.clear();
  }
  return reinterpret_cast<void *>(symbol);
}

void *resolveHostFxrMethod(const ManagedModuleConfig &config, const wchar_t *methodName)
{
  auto hostFxr = openHostFxr(config);
  if (hostFxr == nullptr) {
    return nullptr;
  }

  auto initializeForRuntimeConfig = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
    GetProcAddress(hostFxr, "hostfxr_initialize_for_runtime_config"));
  auto getRuntimeDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
    GetProcAddress(hostFxr, "hostfxr_get_runtime_delegate"));
  auto closeHostContext =
    reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostFxr, "hostfxr_close"));

  if (initializeForRuntimeConfig == nullptr || getRuntimeDelegate == nullptr ||
      closeHostContext == nullptr) {
    setLastError(windowsErrorMessage(L"GetProcAddress(required hostfxr exports)", GetLastError()));
    return nullptr;
  }

  hostfxr_handle hostContext = nullptr;
  auto status = initializeForRuntimeConfig(config.runtimeConfigPath.c_str(), nullptr, &hostContext);
  if (status < 0 || status > 2 || hostContext == nullptr) {
    setLastError(statusMessage(L"hostfxr_initialize_for_runtime_config", status));
    return nullptr;
  }

  void *loadAssemblyDelegate = nullptr;
  status = getRuntimeDelegate(
    hostContext, hdt_load_assembly_and_get_function_pointer, &loadAssemblyDelegate);
  closeHostContext(hostContext);
  if (status != 0 || loadAssemblyDelegate == nullptr) {
    setLastError(statusMessage(L"hostfxr_get_runtime_delegate", status));
    return nullptr;
  }

  auto loadAssemblyAndGetFunctionPointer =
    reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadAssemblyDelegate);

  void *method = nullptr;
  status = loadAssemblyAndGetFunctionPointer(config.assemblyPath.c_str(),
                                             config.typeName.c_str(),
                                             methodName,
                                             unmanagedCallersOnlyMethod,
                                             nullptr,
                                             &method);
  if (status != 0 || method == nullptr) {
    std::wstringstream message;
    message << statusMessage(L"load_assembly_and_get_function_pointer", status) << L" assembly="
            << config.assemblyPath << L" type=" << config.typeName << L" method=" << methodName
            << L".";
    setLastError(message.str());
    return nullptr;
  }

  {
    std::lock_guard<std::mutex> lock(g_errorMutex);
    g_lastError.clear();
  }
  return method;
}

} // namespace

ManagedModuleConfig loadManagedHostConfig()
{
  ManagedModuleConfig config;
  config.loaderKind = selectedLoaderKind();
  config.typeName = kEntryPointType;

  if (config.loaderKind == ManagedLoaderKind::NativeAot) {
    config.nativeLibraryPath = pathForManagedArtifact(L"ExpoDotnetHost.dll");
  } else {
    config.assemblyPath = pathForManagedArtifact(L"ExpoDotnetHost.dll");
    config.runtimeConfigPath = pathForManagedArtifact(L"ExpoDotnetHost.runtimeconfig.json");
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

ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(const ManagedModuleConfig &config)
{
  ManagedRuntimeContextEntryPoints entryPoints;
  switch (config.loaderKind) {
  case ManagedLoaderKind::NativeAot:
    entryPoints.createRuntimeContext = reinterpret_cast<CreateRuntimeContextFn>(
      resolveNativeAotSymbol(config, kCreateRuntimeContextSymbol));
    entryPoints.teardownRuntimeContext = reinterpret_cast<TeardownRuntimeContextFn>(
      resolveNativeAotSymbol(config, kTeardownRuntimeContextSymbol));
    break;
  case ManagedLoaderKind::HostFxr:
    entryPoints.createRuntimeContext = reinterpret_cast<CreateRuntimeContextFn>(
      resolveHostFxrMethod(config, kCreateRuntimeContextMethod));
    entryPoints.teardownRuntimeContext = reinterpret_cast<TeardownRuntimeContextFn>(
      resolveHostFxrMethod(config, kTeardownRuntimeContextMethod));
    break;
  }
  return entryPoints;
}

std::wstring managedLoaderLastError()
{
  std::lock_guard<std::mutex> lock(g_errorMutex);
  return g_lastError;
}

} // namespace expo::modules::dotnet
