#include <expo_jsi.h>
#include <jsi/jsilib.h>

#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"
#include "ManagedProofLoader.h"
#include "jsi/jsi.h"

namespace {

namespace jsi = facebook::jsi;
namespace proof = expo::jsi::experiments;

struct ReleaseCounter {
  expo_jsi_api api;
  const expo_jsi_api *inner_api;
  uint32_t value_release_count = 0;
  uint32_t string_release_count = 0;
};

ReleaseCounter *active_release_counter = nullptr;

struct CountedStringReleaseContext {
  expo_jsi_release_string_fn release;
  void *release_context;
};

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

void counted_release_string(void *release_context)
{
  auto *context = static_cast<CountedStringReleaseContext *>(release_context);
  if (active_release_counter != nullptr) {
    active_release_counter->string_release_count++;
  }
  if (context->release != nullptr) {
    context->release(context->release_context);
  }
  delete context;
}

expo_jsi_string_result counted_get_string(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value)
{
  auto result = active_release_counter->inner_api->get_string(runtime, value);
  if (result.ok == 0 || result.release == nullptr) {
    return result;
  }

  auto *context = new CountedStringReleaseContext{result.release, result.release_context};
  result.release_context = context;
  result.release = counted_release_string;
  return result;
}

ReleaseCounter make_release_counter(const expo_jsi_api *inner_api)
{
  if (inner_api == nullptr || inner_api->release_value == nullptr) {
    throw std::runtime_error("Expo JSI API does not provide release_value.");
  }
  if (inner_api->get_string == nullptr) {
    throw std::runtime_error("Expo JSI API does not provide get_string.");
  }

  auto counter = ReleaseCounter{*inner_api, inner_api, 0};
  counter.api.release_value = counted_release_value;
  counter.api.get_string = counted_get_string;
  return counter;
}

struct CSharpAPI {
  proof::register_modules_fn register_modules;
  const expo_jsi_api *api;
  expo_jsi_runtime_handle runtime_handle;
};

void jsi_main(jsi::Runtime &rt, CSharpAPI &cs)
{
  int register_rc = cs.register_modules(cs.api, cs.runtime_handle);
  if (register_rc != 0) {
    throw std::runtime_error("Managed module registration failed with code " +
                             std::to_string(register_rc));
  }

  auto callback_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global.expo.modules.Math.add(41.5, true);"),
    "generated-module-dispatch.js");
  if (!callback_result.isNumber() || callback_result.asNumber() != 42.5) {
    throw std::runtime_error("Generated module dispatch proof failed.");
  }
  std::cout << "JS called generated-looking C# module: " << callback_result.asNumber() << std::endl;

  auto text_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global.expo.modules.Text.greet('Zoë\\u0000JS');"),
    "generated-module-string-dispatch.js");
  const auto expected_text = std::string("Hello, Zoë\0JS", sizeof("Hello, Zoë\0JS") - 1);
  if (!text_result.isString() || text_result.asString(rt).utf8(rt) != expected_text) {
    throw std::runtime_error("Generated string module dispatch proof failed.");
  }
  std::cout << "JS called generated-looking C# string module: " << text_result.asString(rt).utf8(rt)
            << std::endl;

  auto v2_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global.expo.modules.V2Math.add(20.25, 22.25);"),
    "generated-v2-module-dispatch.js");
  if (!v2_result.isNumber() || v2_result.asNumber() != 42.5) {
    throw std::runtime_error("Generated v2 module dispatch proof failed.");
  }
  std::cout << "JS called generated v2 C# module: " << v2_result.asNumber() << std::endl;

  try {
    rt.evaluateJavaScript(
      std::make_unique<jsi::StringBuffer>("global.expo.modules.Text.greet(42);"),
      "generated-module-string-type-error.js");
    throw std::runtime_error("Expected wrong-type Text.greet call to throw.");
  } catch (const jsi::JSError &) {
    std::cout << "Wrong-type string argument produced a JS error" << std::endl;
  }
}

} // namespace

int main()
{
  expo_jsi_runtime_handle runtime_handle = nullptr;

  try {
    int rc = 0;
    auto managed = proof::loadManagedEntryPoints();

    expo::jsi::HermesConsoleRuntimeConnector connector;
    runtime_handle = expo::jsi::createRuntimeHandle(connector);
    if (runtime_handle == nullptr) {
      throw std::runtime_error("Failed to create Expo JSI runtime handle.");
    }

    std::cout << "Created Hermes-backed JSI runtime" << std::endl;

    auto release_counter = make_release_counter(expo::jsi::api());
    active_release_counter = &release_counter;
    auto cs = CSharpAPI{managed.register_modules, &release_counter.api, runtime_handle};

    connector.runtimeExecutor().executeSync([&](jsi::Runtime &rt) { jsi_main(rt, cs); });

    connector.runtimeExecutor().executeSync(
      [&](jsi::Runtime &) { rc = managed.run_proof(&release_counter.api, runtime_handle); });
    if (rc != 0) {
      throw std::runtime_error("Managed JSI proof failed with code " + std::to_string(rc));
    }

    auto value_release_count = release_counter.value_release_count;
    std::cout << "Released owned value handles: " << value_release_count << std::endl;
    if (value_release_count != 27) {
      throw std::runtime_error(
        "Expected exactly twenty-seven counted owned value handle releases.");
    }

    auto string_release_count = release_counter.string_release_count;
    std::cout << "Released string result buffers: " << string_release_count << std::endl;
    if (string_release_count != 4) {
      throw std::runtime_error("Expected exactly four counted string result buffer releases.");
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
