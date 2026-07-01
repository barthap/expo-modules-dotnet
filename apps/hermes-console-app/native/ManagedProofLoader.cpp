#include "ManagedProofLoader.h"

#if EXPO_JSI_USE_HOSTFXR
#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>
#endif

#include <dlfcn.h>

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

#ifndef EXPO_JSI_MANAGED_CONFIGURATION
#define EXPO_JSI_MANAGED_CONFIGURATION "Debug"
#endif

namespace expo::dotnet::experiments {
namespace {

#if EXPO_JSI_USE_HOSTFXR
hostfxr_initialize_for_runtime_config_fn init_for_config = nullptr;
hostfxr_get_runtime_delegate_fn get_runtime_delegate = nullptr;
hostfxr_close_fn close_hostfxr = nullptr;
#endif

std::filesystem::path repoRootFromCurrentDirectory()
{
  auto current = std::filesystem::current_path();
  while (!current.empty()) {
    if (std::filesystem::exists(current / "experiments/hermes-console-app")) {
      return current;
    }
    current = current.parent_path();
  }
  throw std::runtime_error("Could not locate repository root from current working directory.");
}

template <typename Function> Function resolveExport(void *library, const char *name)
{
  auto symbol = dlsym(library, name);
  if (symbol == nullptr) {
    throw std::runtime_error("Failed to resolve managed export: " + std::string(name));
  }
  return reinterpret_cast<Function>(symbol);
}

#if EXPO_JSI_USE_HOSTFXR
std::filesystem::path findProofAssembly()
{
  auto assembly = repoRootFromCurrentDirectory() /
                  "experiments/hermes-console-app/managed/HermesConsoleApp/"
                  "bin" /
                  EXPO_JSI_MANAGED_CONFIGURATION / "net10.0/HermesConsoleApp.dll";
  if (!std::filesystem::exists(assembly)) {
    throw std::runtime_error("Managed proof assembly does not exist. Run dotnet build first: " +
                             assembly.string());
  }
  return assembly;
}

void loadHostFxr()
{
  char_t hostfxr_path[4096];
  size_t hostfxr_path_size = sizeof(hostfxr_path) / sizeof(char_t);
  int rc = get_hostfxr_path(hostfxr_path, &hostfxr_path_size, nullptr);
  if (rc != 0) {
    throw std::runtime_error("get_hostfxr_path failed with code " + std::to_string(rc));
  }

  std::cout << "Loaded HostFXR path: " << hostfxr_path << std::endl;

  void *library = dlopen(hostfxr_path, RTLD_LAZY | RTLD_LOCAL);
  if (library == nullptr) {
    throw std::runtime_error(dlerror());
  }

  init_for_config = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
    dlsym(library, "hostfxr_initialize_for_runtime_config"));
  get_runtime_delegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
    dlsym(library, "hostfxr_get_runtime_delegate"));
  close_hostfxr = reinterpret_cast<hostfxr_close_fn>(dlsym(library, "hostfxr_close"));

  if (init_for_config == nullptr || get_runtime_delegate == nullptr || close_hostfxr == nullptr) {
    throw std::runtime_error("Failed to resolve required HostFXR exports.");
  }
}

load_assembly_and_get_function_pointer_fn getDotnetLoadAssembly(
  const std::filesystem::path &runtime_config)
{
  hostfxr_handle context = nullptr;
  int rc = init_for_config(runtime_config.c_str(), nullptr, &context);
  if (rc != 0 || context == nullptr) {
    throw std::runtime_error("hostfxr_initialize_for_runtime_config failed with code " +
                             std::to_string(rc));
  }

  void *load_assembly = nullptr;
  rc = get_runtime_delegate(context, hdt_load_assembly_and_get_function_pointer, &load_assembly);
  close_hostfxr(context);

  if (rc != 0 || load_assembly == nullptr) {
    throw std::runtime_error("hostfxr_get_runtime_delegate failed with code " + std::to_string(rc));
  }

  return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(load_assembly);
}

ManagedEntryPoints loadHostFxrEntryPoints()
{
  auto assembly = findProofAssembly();
  auto runtime_config = assembly;
  runtime_config.replace_extension(".runtimeconfig.json");

  loadHostFxr();
  auto load_assembly = getDotnetLoadAssembly(runtime_config);

  run_proof_fn run_proof = nullptr;
  register_modules_fn register_modules = nullptr;

  int rc = load_assembly(assembly.c_str(),
                         "HermesConsoleApp.EntryPoints, HermesConsoleApp",
                         "Run",
                         UNMANAGEDCALLERSONLY_METHOD,
                         nullptr,
                         reinterpret_cast<void **>(&run_proof));
  if (rc != 0 || run_proof == nullptr) {
    throw std::runtime_error("Failed to resolve managed proof entry point: " + std::to_string(rc));
  }

  rc = load_assembly(assembly.c_str(),
                     "HermesConsoleApp.EntryPoints, HermesConsoleApp",
                     "RegisterModules",
                     UNMANAGEDCALLERSONLY_METHOD,
                     nullptr,
                     reinterpret_cast<void **>(&register_modules));
  if (rc != 0 || register_modules == nullptr) {
    throw std::runtime_error("Failed to resolve managed RegisterModules entry point: " +
                             std::to_string(rc));
  }

  return ManagedEntryPoints{run_proof, register_modules};
}
#endif

#if EXPO_JSI_USE_NATIVEAOT
std::filesystem::path nativeAotRid()
{
#if defined(__APPLE__) && defined(__aarch64__)
  return "osx-arm64";
#elif defined(__APPLE__) && defined(__x86_64__)
  return "osx-x64";
#else
  throw std::runtime_error("NativeAOT Hermes console proof currently supports macOS only.");
#endif
}

std::filesystem::path findNativeAotLibrary()
{
  auto library =
    repoRootFromCurrentDirectory() / "experiments/hermes-console-app/managed/HermesConsoleApp/bin" /
    EXPO_JSI_MANAGED_CONFIGURATION / "net10.0" / nativeAotRid() / "publish/HermesConsoleApp.dylib";
  if (!std::filesystem::exists(library)) {
    throw std::runtime_error("NativeAOT proof library does not exist. Run dotnet publish first: " +
                             library.string());
  }
  return library;
}

ManagedEntryPoints loadNativeAotEntryPoints()
{
  auto library_path = findNativeAotLibrary();
  void *library = dlopen(library_path.c_str(), RTLD_NOW | RTLD_LOCAL);
  if (library == nullptr) {
    throw std::runtime_error(dlerror());
  }

  std::cout << "Loaded NativeAOT library: " << library_path.string() << std::endl;

  return ManagedEntryPoints{
    resolveExport<run_proof_fn>(library, "hermes_console_app_run"),
    resolveExport<register_modules_fn>(library, "hermes_console_app_register_modules"),
  };
}
#endif

} // namespace

ManagedEntryPoints loadManagedEntryPoints()
{
#if EXPO_JSI_USE_HOSTFXR
  return loadHostFxrEntryPoints();
#elif EXPO_JSI_USE_NATIVEAOT
  return loadNativeAotEntryPoints();
#else
#error "Expected EXPO_JSI_USE_HOSTFXR or EXPO_JSI_USE_NATIVEAOT"
#endif
}

} // namespace expo::dotnet::experiments
