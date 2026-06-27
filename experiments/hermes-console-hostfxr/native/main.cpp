#include <coreclr_delegates.h>
#include <expo_jsi.h>
#include <hostfxr.h>
#include <jsi/jsilib.h>
#include <nethost.h>

#include <dlfcn.h>

#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"
#include "jsi/jsi.h"

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
                  "experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/"
                  "bin/Debug/net10.0/HostFxrJSIProof.dll";
  if (!std::filesystem::exists(assembly)) {
    throw std::runtime_error("Managed proof assembly does not exist. Run dotnet build first: " +
                             assembly.string());
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

load_assembly_and_get_function_pointer_fn
get_dotnet_load_assembly(const std::filesystem::path &runtime_config)
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

} // namespace

using add_one_fn = expo_jsi_value_handle(CORECLR_DELEGATE_CALLTYPE *)(const expo_jsi_api *,
                                                                      expo_jsi_runtime_handle,
                                                                      expo_jsi_value_handle,
                                                                      expo_jsi_value_handle);

namespace jsi = facebook::jsi;

struct ReleaseCounter {
  expo_jsi_api api;
  const expo_jsi_api *inner_api;
  uint32_t value_release_count = 0;
};

ReleaseCounter *active_release_counter = nullptr;

void counted_release_value(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  if (active_release_counter == nullptr) {
    expo::jsi::api()->release_value(runtime, value);
    return;
  }
  if (value != nullptr) {
    active_release_counter->value_release_count++;
  }
  active_release_counter->inner_api->release_value(runtime, value);
}

ReleaseCounter make_release_counter(const expo_jsi_api *inner_api)
{
  if (inner_api == nullptr || inner_api->release_value == nullptr) {
    throw std::runtime_error("Expo JSI API does not provide release_value.");
  }

  auto counter = ReleaseCounter{*inner_api, inner_api, 0};
  counter.api.release_value = counted_release_value;
  return counter;
}

struct CSharpAPI {
  add_one_fn add_one;
  const expo_jsi_api *api;
  expo_jsi_runtime_handle runtime_handle;

  // TODO: make this struct own the ptrs and free them
  ~CSharpAPI() {}
};

// actual JSI meat, with boilerplate left to main
void jsi_main(jsi::Runtime &rt, CSharpAPI &cs)
{
  auto call_csharp = jsi::Function::createFromHostFunction(
    rt,
    jsi::PropNameID::forAscii(rt, "callCSharp"),
    1,
    [cs](jsi::Runtime &runtime, const facebook::jsi::Value &, const jsi::Value *args, size_t count)
      -> jsi::Value {
      if (count < 2) {
        throw jsi::JSError(runtime, "callCSharp expects 2 arguments.");
      }

      auto borrowed_argument = expo::jsi::createBorrowedValueHandle(args[0]);
      if (borrowed_argument == nullptr) {
        throw jsi::JSError(runtime, "Failed to borrow argument handle.");
      }

      auto borrowed_argument2 = expo::jsi::createBorrowedValueHandle(args[1]);
      if (borrowed_argument2 == nullptr) {
        throw jsi::JSError(runtime, "Failed to borrow argument handle.");
      }

      expo_jsi_value_handle result = nullptr;
      try {
        result = cs.add_one(cs.api, cs.runtime_handle, borrowed_argument, borrowed_argument2);
        expo::jsi::releaseBorrowedValueHandle(borrowed_argument);
        expo::jsi::releaseBorrowedValueHandle(borrowed_argument2);
        borrowed_argument = nullptr;
        borrowed_argument2 = nullptr;
        if (result == nullptr) {
          throw std::runtime_error("Managed AddOne returned a null value handle.");
        }

        auto js_result = expo::jsi::copyValueToJsi(cs.runtime_handle, result);
        cs.api->release_value(cs.runtime_handle, result);
        return js_result;
      } catch (const std::exception &ex) {
        if (borrowed_argument != nullptr) {
          expo::jsi::releaseBorrowedValueHandle(borrowed_argument);
        }
        if (result != nullptr) {
          cs.api->release_value(cs.runtime_handle, result);
        }
        throw jsi::JSError(runtime, ex.what());
      }
    });
  rt.global().setProperty(rt, "callCSharp", std::move(call_csharp));

  auto callback_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("callCSharp(41.5, true);"), "hardcoded-csharp-call.js");
  if (!callback_result.isNumber() || callback_result.asNumber() != 42.5) {
    throw std::runtime_error("JS -> C# host function proof failed.");
  }
  std::cout << "JS called C# through JSI value handles: " << callback_result.asNumber()
            << std::endl;
}

int main()
{
  expo_jsi_runtime_handle runtime_handle = nullptr;

  try {
    auto assembly = find_proof_assembly();
    auto runtime_config = assembly;
    runtime_config.replace_extension(".runtimeconfig.json");

    load_hostfxr();
    auto load_assembly = get_dotnet_load_assembly(runtime_config);

    using run_proof_fn =
      int(CORECLR_DELEGATE_CALLTYPE *)(const expo_jsi_api *, expo_jsi_runtime_handle);

    run_proof_fn run_proof = nullptr;
    add_one_fn add_one = nullptr;

    int rc = load_assembly(assembly.c_str(),
                           "HostFxrJSIProof.EntryPoints, HostFxrJSIProof",
                           "Run",
                           UNMANAGEDCALLERSONLY_METHOD,
                           nullptr,
                           reinterpret_cast<void **>(&run_proof));
    if (rc != 0 || run_proof == nullptr) {
      throw std::runtime_error("Failed to resolve managed proof entry point: " +
                               std::to_string(rc));
    }

    rc = load_assembly(assembly.c_str(),
                       "HostFxrJSIProof.EntryPoints, HostFxrJSIProof",
                       "AddOne",
                       UNMANAGEDCALLERSONLY_METHOD,
                       nullptr,
                       reinterpret_cast<void **>(&add_one));
    if (rc != 0 || add_one == nullptr) {
      throw std::runtime_error("Failed to resolve managed AddOne entry point: " +
                               std::to_string(rc));
    }

    expo::jsi::HermesConsoleRuntimeConnector connector;
    runtime_handle = expo::jsi::createRuntimeHandle(connector);
    if (runtime_handle == nullptr) {
      throw std::runtime_error("Failed to create Expo JSI runtime handle.");
    }

    std::cout << "Created Hermes-backed JSI runtime" << std::endl;

    auto &rt = connector.runtime();
    auto release_counter = make_release_counter(expo::jsi::api());
    active_release_counter = &release_counter;
    auto cs = CSharpAPI{add_one, &release_counter.api, runtime_handle};

    jsi_main(rt, cs);

    rc = run_proof(&release_counter.api, runtime_handle);
    if (rc != 0) {
      throw std::runtime_error("Managed JSI proof failed with code " + std::to_string(rc));
    }

    auto release_count = release_counter.value_release_count;
    std::cout << "Released owned value handles: " << release_count << std::endl;
    if (release_count != 2) {
      throw std::runtime_error("Expected exactly two owned value handle releases.");
    }

    expo::jsi::releaseRuntimeHandle(runtime_handle);
    runtime_handle = nullptr;
    active_release_counter = nullptr;
    connector.invalidate();

    std::cout << "hermes console hostfxr proof: ok" << std::endl;
    return 0;
  } catch (const std::exception &error) {
    if (runtime_handle != nullptr) {
      expo::jsi::releaseRuntimeHandle(runtime_handle);
    }
    active_release_counter = nullptr;
    std::cerr << "hermes_console_hostfxr failed: " << error.what() << std::endl;
    return 1;
  }
}
