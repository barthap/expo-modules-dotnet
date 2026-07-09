#include "ManagedLoader.h"

#include "ManagedHostFxr.h"

#import <Foundation/Foundation.h>

#include <array>
#include <cstdlib>
#include <dlfcn.h>
#include <mutex>

namespace expo::modules::dotnet {
namespace {

constexpr const char *kManagedSubdirectory = "Managed";
NSString *const kLoaderInfoPlistKey = @"ExpoModulesDotnetLoader";
constexpr const char *kCreateRuntimeContextSymbol = "expo_dotnet_create_runtime_context_result";
constexpr const char *kTeardownRuntimeContextSymbol = "expo_dotnet_teardown_runtime_context";
constexpr const char *kEntryPointType = "Expo.ModulesCore.Generated.EntryPoints, ExpoDotnetHost";
constexpr const char *kCreateRuntimeContextMethod = "CreateRuntimeContextResult";
constexpr const char *kTeardownRuntimeContextMethod = "TeardownRuntimeContext";

std::mutex g_errorMutex;
std::string g_lastError;

void setLastError(std::string message)
{
  NSLog(@"[ExpoModulesDotnet] %s", message.c_str());
  std::lock_guard<std::mutex> lock(g_errorMutex);
  g_lastError = std::move(message);
}

void clearLastError()
{
  std::lock_guard<std::mutex> lock(g_errorMutex);
  g_lastError.clear();
}

std::string dlerrorMessage(const char *operation)
{
  const char *error = dlerror();
  return std::string(operation) + " failed: " + (error == nullptr ? "unknown error" : error);
}

std::string pathForBundledResource(NSString *name, NSString *extension)
{
  NSURL *url = [[NSBundle mainBundle] URLForResource:name
                                      withExtension:extension
                                       subdirectory:@(kManagedSubdirectory)];
  if (url == nil) {
    url = [[NSBundle mainBundle] URLForResource:name withExtension:extension];
  }
  if (url == nil) {
    setLastError(
      "Missing managed artifact Managed/" + std::string([name UTF8String]) + "." +
      std::string([extension UTF8String]) +
      ". Run the expo-modules-dotnet-autolinking link command (or a full app build, which runs it "
      "as a script phase) before launching the macOS app.");
  }
  return url == nil ? std::string() : std::string([[url path] UTF8String]);
}

ManagedLoaderKind parseLoaderKind(NSString *loader)
{
  if ([loader isEqualToString:@"nativeaot"]) {
    return ManagedLoaderKind::NativeAot;
  }
  return ManagedLoaderKind::HostFxr;
}

NSString *loaderKindFromEnvironment()
{
  const char *loader = getenv("EXPO_DOTNET_LOADER");
  if (loader == nullptr || loader[0] == '\0') {
    loader = getenv("EXPO_JSI_DOTNET_LOADER");
  }
  return loader == nullptr || loader[0] == '\0' ? nil : @(loader);
}

NSString *loaderKindFromInfoPlist()
{
  id loader = [[NSBundle mainBundle] objectForInfoDictionaryKey:kLoaderInfoPlistKey];
  if (![loader isKindOfClass:NSString.class]) {
    return nil;
  }

  return [loader stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
}

ManagedLoaderKind selectedLoaderKind()
{
  NSString *loader = loaderKindFromEnvironment();
  if (loader == nil) {
    loader = loaderKindFromInfoPlist();
  }
  return parseLoaderKind(loader);
}

void *openLibrary(const std::string &path)
{
  if (path.empty()) {
    return nullptr;
  }
  return dlopen(path.c_str(), RTLD_NOW | RTLD_GLOBAL);
}

void *openHostFxr(const ManagedModuleConfig &config)
{
  void *nethost = openLibrary(config.nethostPath);
  if (nethost == nullptr) {
    setLastError(dlerrorMessage("dlopen(libnethost.dylib)"));
    return nullptr;
  }

  auto getHostFxrPath = reinterpret_cast<get_hostfxr_path_fn>(dlsym(nethost, "get_hostfxr_path"));
  if (getHostFxrPath == nullptr) {
    setLastError(dlerrorMessage("dlsym(get_hostfxr_path)"));
    return nullptr;
  }

  std::array<char_t, 4096> hostFxrPath{};
  size_t hostFxrPathSize = hostFxrPath.size();
  auto status = getHostFxrPath(hostFxrPath.data(), &hostFxrPathSize, nullptr);
  if (status != 0) {
    setLastError("get_hostfxr_path failed with status " + std::to_string(status) + ".");
    return nullptr;
  }

  void *hostFxr = dlopen(hostFxrPath.data(), RTLD_NOW | RTLD_LOCAL);
  if (hostFxr == nullptr) {
    setLastError(dlerrorMessage("dlopen(hostfxr)"));
  }
  return hostFxr;
}

void *resolveNativeAotSymbol(const ManagedModuleConfig &config, const char *symbolName)
{
  void *library = openLibrary(config.nativeLibraryPath);
  if (library == nullptr) {
    setLastError(dlerrorMessage("dlopen(NativeAOT ExpoDotnetHost library)"));
    return nullptr;
  }

  auto *symbol = dlsym(library, symbolName);
  if (symbol == nullptr) {
    setLastError(dlerrorMessage((std::string("dlsym(") + symbolName + ")").c_str()));
    return nullptr;
  }

  clearLastError();
  return symbol;
}

void *resolveHostFxrMethod(const ManagedModuleConfig &config, const char *methodName)
{
  void *hostFxr = openHostFxr(config);
  if (hostFxr == nullptr) {
    return nullptr;
  }

  auto initializeForRuntimeConfig =
    reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
      dlsym(hostFxr, "hostfxr_initialize_for_runtime_config"));
  auto getRuntimeDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
    dlsym(hostFxr, "hostfxr_get_runtime_delegate"));
  auto closeHostContext = reinterpret_cast<hostfxr_close_fn>(dlsym(hostFxr, "hostfxr_close"));

  if (initializeForRuntimeConfig == nullptr || getRuntimeDelegate == nullptr ||
      closeHostContext == nullptr) {
    setLastError(dlerrorMessage("dlsym(required hostfxr exports)"));
    return nullptr;
  }

  hostfxr_handle hostContext = nullptr;
  auto status = initializeForRuntimeConfig(config.runtimeConfigPath.c_str(), nullptr, &hostContext);
  if (status < 0 || status > 2 || hostContext == nullptr) {
    setLastError("hostfxr_initialize_for_runtime_config failed with status " +
                 std::to_string(status) + ".");
    return nullptr;
  }

  void *loadAssemblyDelegate = nullptr;
  status = getRuntimeDelegate(hostContext,
                              hdt_load_assembly_and_get_function_pointer,
                              &loadAssemblyDelegate);
  closeHostContext(hostContext);
  if (status != 0 || loadAssemblyDelegate == nullptr) {
    setLastError("hostfxr_get_runtime_delegate failed with status " + std::to_string(status) + ".");
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
    setLastError("load_assembly_and_get_function_pointer failed with status " +
                 std::to_string(status) + ".");
    return nullptr;
  }

  clearLastError();
  return method;
}

} // namespace

ManagedModuleConfig loadManagedHostConfig()
{
  ManagedModuleConfig config;
  config.loaderKind = selectedLoaderKind();
  config.typeName = kEntryPointType;

  if (config.loaderKind == ManagedLoaderKind::NativeAot) {
    config.nativeLibraryPath = pathForBundledResource(@"libExpoDotnetHost", @"dylib");
  } else {
    config.assemblyPath = pathForBundledResource(@"ExpoDotnetHost", @"dll");
    config.runtimeConfigPath = pathForBundledResource(@"ExpoDotnetHost.runtimeconfig", @"json");
    config.nethostPath = pathForBundledResource(@"libnethost", @"dylib");
  }

  return config;
}

const char *managedLoaderKindName(ManagedLoaderKind loaderKind)
{
  switch (loaderKind) {
    case ManagedLoaderKind::HostFxr:
      return "HostFXR";
    case ManagedLoaderKind::NativeAot:
      return "NativeAOT";
  }
}

ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(const ManagedModuleConfig &config)
{
  ManagedRuntimeContextEntryPoints entryPoints;
  switch (config.loaderKind) {
    case ManagedLoaderKind::NativeAot:
      entryPoints.createRuntimeContext =
        reinterpret_cast<CreateRuntimeContextFn>(
          resolveNativeAotSymbol(config, kCreateRuntimeContextSymbol));
      entryPoints.teardownRuntimeContext =
        reinterpret_cast<TeardownRuntimeContextFn>(
          resolveNativeAotSymbol(config, kTeardownRuntimeContextSymbol));
      return entryPoints;
    case ManagedLoaderKind::HostFxr:
      entryPoints.createRuntimeContext =
        reinterpret_cast<CreateRuntimeContextFn>(
          resolveHostFxrMethod(config, kCreateRuntimeContextMethod));
      entryPoints.teardownRuntimeContext =
        reinterpret_cast<TeardownRuntimeContextFn>(
          resolveHostFxrMethod(config, kTeardownRuntimeContextMethod));
      return entryPoints;
  }
}

std::string managedLoaderLastError()
{
  std::lock_guard<std::mutex> lock(g_errorMutex);
  return g_lastError;
}

} // namespace expo::modules::dotnet
