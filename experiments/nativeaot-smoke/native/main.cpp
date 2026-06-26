#include <dlfcn.h>

#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {

std::filesystem::path repo_root_from_current_directory()
{
  auto current = std::filesystem::current_path();
  while (!current.empty()) {
    if (std::filesystem::exists(current / "experiments/nativeaot-smoke")) {
      return current;
    }
    current = current.parent_path();
  }
  throw std::runtime_error("Could not locate repository root from current working directory.");
}

std::filesystem::path find_nativeaot_library()
{
  auto library = repo_root_from_current_directory() /
    "experiments/nativeaot-smoke/managed/NativeAotSmoke/bin/Release/net10.0/osx-arm64/publish/NativeAotSmoke.dylib";
  if (!std::filesystem::exists(library)) {
    throw std::runtime_error("NativeAOT smoke library does not exist. Run dotnet publish first: " + library.string());
  }
  return library;
}

template <typename Function>
Function resolve_export(void *library, const char *name)
{
  auto symbol = dlsym(library, name);
  if (symbol == nullptr) {
    throw std::runtime_error("Failed to resolve NativeAOT export: " + std::string(name));
  }
  return reinterpret_cast<Function>(symbol);
}

} // namespace

int main()
{
  try {
    auto library_path = find_nativeaot_library();
    void *library = dlopen(library_path.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (library == nullptr) {
      throw std::runtime_error(dlerror());
    }

    std::cout << "Loaded NativeAOT library: " << library_path.string() << std::endl;

    using get_message_fn = const char *(*)();
    using release_message_fn = void (*)(const char *);

    auto get_message = resolve_export<get_message_fn>(library, "nativeaot_smoke_get_message");
    auto release_message = resolve_export<release_message_fn>(library, "nativeaot_smoke_release_message");

    const char *message = get_message();
    std::cout << "Managed payload: " << message << std::endl;
    release_message(message);
    std::cout << "Released managed-owned payload buffer" << std::endl;

    dlclose(library);
    return 0;
  } catch (const std::exception &error) {
    std::cerr << "nativeaot_smoke failed: " << error.what() << std::endl;
    return 1;
  }
}
