#include <coreclr_delegates.h>
#include <expo_jsi.h>
#include <hostfxr.h>
#include <nethost.h>

#include <dlfcn.h>

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"

namespace {

hostfxr_initialize_for_runtime_config_fn init_for_config = nullptr;
hostfxr_get_runtime_delegate_fn get_runtime_delegate = nullptr;
hostfxr_close_fn close_hostfxr = nullptr;

std::filesystem::path repo_root_from_current_directory()
{
  auto current = std::filesystem::current_path();
  while (!current.empty()) {
    if (std::filesystem::exists(current / "experiments/hermes-console-hostfxr")) {
      return current;
    }
    current = current.parent_path();
  }
  throw std::runtime_error("Could not locate repository root from current working directory.");
}

std::filesystem::path find_proof_assembly()
{
  auto assembly = repo_root_from_current_directory() /
    "experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/bin/Debug/net10.0/HostFxrJSIProof.dll";
  if (!std::filesystem::exists(assembly)) {
    throw std::runtime_error("Managed proof assembly does not exist. Run dotnet build first: " + assembly.string());
  }
  return assembly;
}

void load_hostfxr()
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

load_assembly_and_get_function_pointer_fn get_dotnet_load_assembly(const std::filesystem::path &runtime_config)
{
  hostfxr_handle context = nullptr;
  int rc = init_for_config(runtime_config.c_str(), nullptr, &context);
  if (rc != 0 || context == nullptr) {
    throw std::runtime_error("hostfxr_initialize_for_runtime_config failed with code " + std::to_string(rc));
  }

  void *load_assembly = nullptr;
  rc = get_runtime_delegate(context, hdt_load_assembly_and_get_function_pointer, &load_assembly);
  close_hostfxr(context);

  if (rc != 0 || load_assembly == nullptr) {
    throw std::runtime_error("hostfxr_get_runtime_delegate failed with code " + std::to_string(rc));
  }

  return reinterpret_cast<load_assembly_and_get_function_pointer_fn>(load_assembly);
}

} // namespace

int main()
{
  expo_jsi_runtime_handle runtime_handle = nullptr;

  try {
    auto assembly = find_proof_assembly();
    auto runtime_config = assembly;
    runtime_config.replace_extension(".runtimeconfig.json");

    load_hostfxr();
    auto load_assembly = get_dotnet_load_assembly(runtime_config);

    using run_proof_fn = int(CORECLR_DELEGATE_CALLTYPE *)(const expo_jsi_api *, expo_jsi_runtime_handle);
    run_proof_fn run_proof = nullptr;

    int rc = load_assembly(
      assembly.c_str(),
      "HostFxrJSIProof.EntryPoints, HostFxrJSIProof",
      "Run",
      UNMANAGEDCALLERSONLY_METHOD,
      nullptr,
      reinterpret_cast<void **>(&run_proof));
    if (rc != 0 || run_proof == nullptr) {
      throw std::runtime_error("Failed to resolve managed proof entry point: " + std::to_string(rc));
    }

    expo::jsi::HermesConsoleRuntimeConnector connector;
    runtime_handle = expo::jsi::create_runtime_handle(&connector);
    if (runtime_handle == nullptr) {
      throw std::runtime_error("Failed to create Expo JSI runtime handle.");
    }

    std::cout << "Created Hermes-backed JSI runtime" << std::endl;

    rc = run_proof(expo::jsi::api(), runtime_handle);
    if (rc != 0) {
      throw std::runtime_error("Managed JSI proof failed with code " + std::to_string(rc));
    }

    auto release_count = expo::jsi::released_value_count(runtime_handle);
    std::cout << "Released owned value handles: " << release_count << std::endl;
    if (release_count != 1) {
      throw std::runtime_error("Expected exactly one owned value handle release.");
    }

    expo::jsi::release_runtime_handle(runtime_handle);
    runtime_handle = nullptr;
    connector.invalidate();

    std::cout << "hermes console hostfxr proof: ok" << std::endl;
    return 0;
  } catch (const std::exception &error) {
    if (runtime_handle != nullptr) {
      expo::jsi::release_runtime_handle(runtime_handle);
    }
    std::cerr << "hermes_console_hostfxr failed: " << error.what() << std::endl;
    return 1;
  }
}
