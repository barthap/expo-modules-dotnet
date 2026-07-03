#include "ManagedLoader.h"

#include "ManagedHostFxr.h"

#import <Foundation/Foundation.h>

#include <array>
#include <cstdlib>
#include <dlfcn.h>

namespace expo::modules::dotnet {
namespace {

constexpr const char *kManagedSubdirectory = "Managed";
NSString *const kLoaderInfoPlistKey = @"ExpoModulesDotnetLoader";
constexpr const char *kRegisterModulesSymbol = "expo_dotnet_register_modules";
constexpr const char *kCreateRuntimeContextSymbol = "expo_dotnet_create_runtime_context";
constexpr const char *kTeardownRuntimeContextSymbol = "expo_dotnet_teardown_runtime_context";
constexpr const char *kEntryPointType = "ExampleModule.EntryPoints, ExampleModule";
constexpr const char *kEntryPointMethod = "RegisterModules";
constexpr const char *kCreateRuntimeContextMethod = "CreateRuntimeContext";
constexpr const char *kTeardownRuntimeContextMethod = "TeardownRuntimeContext";

std::string pathForBundledResource(NSString *name, NSString *extension)
{
  NSURL *url = [[NSBundle mainBundle] URLForResource:name
                                      withExtension:extension
                                       subdirectory:@(kManagedSubdirectory)];
  if (url == nil) {
    url = [[NSBundle mainBundle] URLForResource:name withExtension:extension];
  }
  if (url == nil) {
    NSLog(@"[ExpoModulesDotnet] Missing managed artifact Managed/%@.%@. Run "
           "apps/desktop-app/scripts/build-managed.sh before launching the macOS app.",
          name,
          extension);
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
    NSLog(@"[ExpoModulesDotnet] Failed to load libnethost.dylib: %s", dlerror());
    return nullptr;
  }

  auto getHostFxrPath = reinterpret_cast<get_hostfxr_path_fn>(dlsym(nethost, "get_hostfxr_path"));
  if (getHostFxrPath == nullptr) {
    NSLog(@"[ExpoModulesDotnet] Failed to resolve get_hostfxr_path: %s", dlerror());
    return nullptr;
  }

  std::array<char_t, 4096> hostFxrPath{};
  size_t hostFxrPathSize = hostFxrPath.size();
  auto status = getHostFxrPath(hostFxrPath.data(), &hostFxrPathSize, nullptr);
  if (status != 0) {
    NSLog(@"[ExpoModulesDotnet] get_hostfxr_path failed with status %d.", status);
    return nullptr;
  }

  void *hostFxr = dlopen(hostFxrPath.data(), RTLD_NOW | RTLD_LOCAL);
  if (hostFxr == nullptr) {
    NSLog(@"[ExpoModulesDotnet] Failed to load hostfxr from %s: %s", hostFxrPath.data(), dlerror());
  }
  return hostFxr;
}

void *resolveNativeAotSymbol(const ManagedModuleConfig &config, const char *symbolName)
{
  void *library = openLibrary(config.nativeLibraryPath);
  if (library == nullptr) {
    NSLog(@"[ExpoModulesDotnet] Failed to load NativeAOT ExampleModule library: %s", dlerror());
    return nullptr;
  }

  auto *symbol = dlsym(library, symbolName);
  if (symbol == nullptr) {
    NSLog(@"[ExpoModulesDotnet] Failed to resolve %s: %s", symbolName, dlerror());
    return nullptr;
  }

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
    NSLog(@"[ExpoModulesDotnet] Failed to resolve required hostfxr exports: %s", dlerror());
    return nullptr;
  }

  hostfxr_handle hostContext = nullptr;
  auto status = initializeForRuntimeConfig(config.runtimeConfigPath.c_str(), nullptr, &hostContext);
  if (status < 0 || status > 2 || hostContext == nullptr) {
    NSLog(@"[ExpoModulesDotnet] hostfxr_initialize_for_runtime_config failed with status %d.",
          status);
    return nullptr;
  }

  void *loadAssemblyDelegate = nullptr;
  status = getRuntimeDelegate(hostContext,
                              hdt_load_assembly_and_get_function_pointer,
                              &loadAssemblyDelegate);
  closeHostContext(hostContext);
  if (status != 0 || loadAssemblyDelegate == nullptr) {
    NSLog(@"[ExpoModulesDotnet] hostfxr_get_runtime_delegate failed with status %d.", status);
    return nullptr;
  }

  auto loadAssemblyAndGetFunctionPointer =
    reinterpret_cast<load_assembly_and_get_function_pointer_fn>(loadAssemblyDelegate);

  void *registerModules = nullptr;
  status = loadAssemblyAndGetFunctionPointer(config.assemblyPath.c_str(),
                                             config.typeName.c_str(),
                                             methodName,
                                             unmanagedCallersOnlyMethod,
                                             nullptr,
                                             &registerModules);
  if (status != 0 || registerModules == nullptr) {
    NSLog(@"[ExpoModulesDotnet] load_assembly_and_get_function_pointer failed with status %d.",
          status);
    return nullptr;
  }

  return registerModules;
}

} // namespace

ManagedModuleConfig loadExampleModuleConfig()
{
  ManagedModuleConfig config;
  config.loaderKind = selectedLoaderKind();
  config.typeName = kEntryPointType;
  config.methodName = kEntryPointMethod;

  if (config.loaderKind == ManagedLoaderKind::NativeAot) {
    config.nativeLibraryPath = pathForBundledResource(@"libExampleModule", @"dylib");
  } else {
    config.assemblyPath = pathForBundledResource(@"ExampleModule", @"dll");
    config.runtimeConfigPath = pathForBundledResource(@"ExampleModule.runtimeconfig", @"json");
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

RegisterModulesFn resolveRegisterModules(const ManagedModuleConfig &config)
{
  switch (config.loaderKind) {
    case ManagedLoaderKind::NativeAot:
      return reinterpret_cast<RegisterModulesFn>(
        resolveNativeAotSymbol(config, kRegisterModulesSymbol));
    case ManagedLoaderKind::HostFxr:
      return reinterpret_cast<RegisterModulesFn>(
        resolveHostFxrMethod(config, config.methodName.c_str()));
  }
}

ManagedRuntimeContextEntryPoints resolveRuntimeContextEntryPoints(const ManagedModuleConfig &config)
{
  ManagedRuntimeContextEntryPoints entryPoints;
  switch (config.loaderKind) {
    case ManagedLoaderKind::NativeAot:
      entryPoints.registerModules =
        reinterpret_cast<RegisterModulesFn>(resolveNativeAotSymbol(config, kRegisterModulesSymbol));
      entryPoints.createRuntimeContext =
        reinterpret_cast<CreateRuntimeContextFn>(
          resolveNativeAotSymbol(config, kCreateRuntimeContextSymbol));
      entryPoints.teardownRuntimeContext =
        reinterpret_cast<TeardownRuntimeContextFn>(
          resolveNativeAotSymbol(config, kTeardownRuntimeContextSymbol));
      return entryPoints;
    case ManagedLoaderKind::HostFxr:
      entryPoints.registerModules =
        reinterpret_cast<RegisterModulesFn>(resolveHostFxrMethod(config, config.methodName.c_str()));
      entryPoints.createRuntimeContext =
        reinterpret_cast<CreateRuntimeContextFn>(
          resolveHostFxrMethod(config, kCreateRuntimeContextMethod));
      entryPoints.teardownRuntimeContext =
        reinterpret_cast<TeardownRuntimeContextFn>(
          resolveHostFxrMethod(config, kTeardownRuntimeContextMethod));
      return entryPoints;
  }
}

} // namespace expo::modules::dotnet
