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
namespace proof = expo::dotnet::experiments;

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
    expo::dotnet::api()->release_value(runtime, value);
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

void register_modules(jsi::Runtime &rt, CSharpAPI &cs)
{
  int register_rc = cs.register_modules(cs.api, cs.runtime_handle);
  if (register_rc != 0) {
    throw std::runtime_error("Managed module registration failed with code " +
                             std::to_string(register_rc));
  }
}

void run_generated_module_checks(jsi::Runtime &rt)
{
  auto callback_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global._expoDotnet.modules.Math.add(41.5, true);"),
    "generated-module-dispatch.js");
  if (!callback_result.isNumber() || callback_result.asNumber() != 42.5) {
    throw std::runtime_error("Generated module dispatch proof failed.");
  }
  std::cout << "JS called generated-looking C# module: " << callback_result.asNumber() << std::endl;

  auto text_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global._expoDotnet.modules.Text.greet('Zoë\\u0000JS');"),
    "generated-module-string-dispatch.js");
  const auto expected_text = std::string("Hello, Zoë\0JS", sizeof("Hello, Zoë\0JS") - 1);
  if (!text_result.isString() || text_result.asString(rt).utf8(rt) != expected_text) {
    throw std::runtime_error("Generated string module dispatch proof failed.");
  }
  std::cout << "JS called generated-looking C# string module: " << text_result.asString(rt).utf8(rt)
            << std::endl;

  auto v2_result = rt.evaluateJavaScript(
    std::make_unique<jsi::StringBuffer>("global._expoDotnet.modules.V2Math.add(20.25, 22.25);"),
    "generated-v2-module-dispatch.js");
  if (!v2_result.isNumber() || v2_result.asNumber() != 42.5) {
    throw std::runtime_error("Generated v2 module dispatch proof failed.");
  }
  std::cout << "JS called generated v2 C# module: " << v2_result.asNumber() << std::endl;
}

void start_showcase_checks(jsi::Runtime &rt)
{
  rt.evaluateJavaScript(std::make_unique<jsi::StringBuffer>(R"(
    global.__consoleShowcase = {
      asyncMessage: null,
      callbackResult: null,
      eventDone: false,
      eventPayload: null,
      recordSummary: null
    };

    const showcase = global._expoDotnet.modules.Showcase;
    const subscription = showcase.addListener(
      'onStatus',
      value => { global.__consoleShowcase.eventPayload = value; }
    );

    const record = showcase.describeUser({ Name: 'Ada', Age: 37 });
    global.__consoleShowcase.recordSummary = `${record.Name}:${record.Age}:${record.Summary}`;
    global.__consoleShowcase.callbackResult =
      showcase.transformWithCallback('Hermes', value => `callback(${value})`);

    showcase.getMessageAsync().then(
      value => { global.__consoleShowcase.asyncMessage = value; },
      error => { global.__consoleShowcase.asyncMessage = error && error.message; }
    );
    showcase.emitStatusAsync('ready').then(
      () => {
        global.__consoleShowcase.eventDone = true;
        subscription.remove();
      },
      error => { global.__consoleShowcase.eventPayload = error && error.message; }
    );
    true;
  )"),
                        "showcase-start.js");
}

void verify_showcase_checks(jsi::Runtime &rt)
{
  auto global = rt.global();
  auto outcome = global.getPropertyAsObject(rt, "__consoleShowcase");

  auto async_message = outcome.getProperty(rt, "asyncMessage").asString(rt).utf8(rt);
  if (async_message != "Hello from async C#") {
    throw std::runtime_error("Async showcase failed: " + async_message);
  }

  auto record_summary = outcome.getProperty(rt, "recordSummary").asString(rt).utf8(rt);
  if (record_summary != "Ada:37:Ada is 37") {
    throw std::runtime_error("Record showcase failed: " + record_summary);
  }

  auto callback_result = outcome.getProperty(rt, "callbackResult").asString(rt).utf8(rt);
  if (callback_result != "callback(C# sent Hermes)") {
    throw std::runtime_error("Callback showcase failed: " + callback_result);
  }

  auto event_payload = outcome.getProperty(rt, "eventPayload").asString(rt).utf8(rt);
  if (event_payload != "C# event: ready") {
    throw std::runtime_error("Event showcase failed: " + event_payload);
  }

  if (!outcome.getProperty(rt, "eventDone").getBool()) {
    throw std::runtime_error("Event showcase promise did not complete.");
  }

  std::cout << "Showcased async functions, records, callbacks, and events" << std::endl;
}

} // namespace

int main()
{
  expo_jsi_runtime_handle runtime_handle = nullptr;

  try {
    int rc = 0;
    auto managed = proof::loadManagedEntryPoints();

    expo::dotnet::HermesConsoleRuntimeConnector connector;
    runtime_handle = expo::dotnet::createRuntimeHandle(connector);
    if (runtime_handle == nullptr) {
      throw std::runtime_error("Failed to create Expo JSI runtime handle.");
    }

    std::cout << "Created Hermes-backed JSI runtime" << std::endl;

    auto release_counter = make_release_counter(expo::dotnet::api());
    active_release_counter = &release_counter;
    auto cs = CSharpAPI{managed.register_modules, &release_counter.api, runtime_handle};

    connector.runtimeExecutor().executeSync([&](jsi::Runtime &rt) {
      register_modules(rt, cs);
      run_generated_module_checks(rt);
      start_showcase_checks(rt);
    });
    connector.waitUntilIdle();
    connector.runtimeExecutor().executeSync([&](jsi::Runtime &rt) { verify_showcase_checks(rt); });

    connector.runtimeExecutor().executeSync(
      [&](jsi::Runtime &) { rc = managed.run_proof(&release_counter.api, runtime_handle); });
    if (rc != 0) {
      throw std::runtime_error("Managed JSI proof failed with code " + std::to_string(rc));
    }

    auto value_release_count = release_counter.value_release_count;
    std::cout << "Released owned value handles: " << value_release_count << std::endl;
    if (value_release_count != 132) {
      throw std::runtime_error(
        "Expected exactly one hundred thirty-two counted owned value handle releases.");
    }

    auto string_release_count = release_counter.string_release_count;
    std::cout << "Released string result buffers: " << string_release_count << std::endl;
    if (string_release_count != 10) {
      throw std::runtime_error("Expected exactly ten counted string result buffer releases.");
    }

    expo::dotnet::releaseRuntimeHandle(runtime_handle);
    runtime_handle = nullptr;
    active_release_counter = nullptr;
    connector.invalidate();

    std::cout << "hermes console app: ok" << std::endl;
    return 0;
  } catch (const std::exception &error) {
    if (runtime_handle != nullptr) {
      expo::dotnet::releaseRuntimeHandle(runtime_handle);
    }
    active_release_counter = nullptr;
    std::cerr << "hermes_console_app failed: " << error.what() << std::endl;
    return 1;
  }
}
