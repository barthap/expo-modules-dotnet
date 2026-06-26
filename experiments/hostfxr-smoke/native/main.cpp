#include <cstddef>

#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <nethost.h>

#include <dlfcn.h>
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {

hostfxr_initialize_for_runtime_config_fn init_for_config = nullptr;
hostfxr_get_runtime_delegate_fn get_runtime_delegate = nullptr;
hostfxr_close_fn close_hostfxr = nullptr;

std::filesystem::path repo_root_from_current_directory()
{
  auto current = std::filesystem::current_path();
  while (!current.empty()) {
    if (std::filesystem::exists(current / "experiments/hostfxr-smoke")) {
      return current;
    }
    current = current.parent_path();
  }
  throw std::runtime_error("Could not locate repository root from current working directory.");
}

std::filesystem::path find_smoke_assembly()
{
  auto assembly = repo_root_from_current_directory() /
    "experiments/hostfxr-smoke/managed/HostFxrSmoke/bin/Debug/net10.0/HostFxrSmoke.dll";
  if (!std::filesystem::exists(assembly)) {
    throw std::runtime_error("Managed smoke assembly does not exist. Run dotnet build first: " + assembly.string());
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
  try {
    auto assembly = find_smoke_assembly();
    auto runtime_config = assembly;
    runtime_config.replace_extension(".runtimeconfig.json");

    load_hostfxr();
    auto load_assembly = get_dotnet_load_assembly(runtime_config);

    using get_message_fn = const char *(CORECLR_DELEGATE_CALLTYPE *)();
    using release_message_fn = void(CORECLR_DELEGATE_CALLTYPE *)(const char *);

    get_message_fn get_message = nullptr;
    release_message_fn release_message = nullptr;

    int rc = load_assembly(
      assembly.c_str(),
      "HostFxrSmoke.EntryPoints, HostFxrSmoke",
      "GetMessage",
      UNMANAGEDCALLERSONLY_METHOD,
      nullptr,
      reinterpret_cast<void **>(&get_message));
    if (rc != 0 || get_message == nullptr) {
      throw std::runtime_error("Failed to resolve managed get_message entry point: " + std::to_string(rc));
    }

    rc = load_assembly(
      assembly.c_str(),
      "HostFxrSmoke.EntryPoints, HostFxrSmoke",
      "ReleaseMessage",
      UNMANAGEDCALLERSONLY_METHOD,
      nullptr,
      reinterpret_cast<void **>(&release_message));
    if (rc != 0 || release_message == nullptr) {
      throw std::runtime_error("Failed to resolve managed release_message entry point: " + std::to_string(rc));
    }

    const char *message = get_message();
    std::cout << "Managed payload: " << message << std::endl;
    release_message(message);
    std::cout << "Released managed-owned payload buffer" << std::endl;
    return 0;
  } catch (const std::exception &error) {
    std::cerr << "hostfxr_smoke failed: " << error.what() << std::endl;
    return 1;
  }
}
