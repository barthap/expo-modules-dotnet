#include <expo_jsi_testhost.h>
#include <jsi/jsilib.h>

#include <cstring>
#include <exception>
#include <memory>
#include <string>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"

struct expo_jsi_testhost_runtime_t {
  expo::jsi::HermesConsoleRuntimeConnector connector;
  expo_jsi_runtime_handle runtime = nullptr;
  expo_jsi_api countedApi{};
  const expo_jsi_api *innerApi = nullptr;
  expo_jsi_testhost_counters counters{};
};

namespace {

thread_local std::string lastErrorMessage;

expo_jsi_error makeError(int32_t code, const char *message)
{
  lastErrorMessage = message == nullptr ? "Unknown native testhost error." : message;
  return expo_jsi_error{
    code,
    lastErrorMessage.c_str(),
    static_cast<int32_t>(lastErrorMessage.size()),
  };
}

expo_jsi_value_result makeErrorResult(int32_t code, const char *message)
{
  return expo_jsi_value_result{0, nullptr, makeError(code, message)};
}

expo_jsi_testhost_runtime_t *activeCounterRuntime = nullptr;

expo_jsi_testhost_runtime_t *runtimeFor(expo_jsi_runtime_handle runtime)
{
  if (activeCounterRuntime != nullptr && activeCounterRuntime->runtime == runtime) {
    return activeCounterRuntime;
  }
  return nullptr;
}

void countedReleaseValue(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && value != nullptr) {
    testhost->counters.released_values++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  api->release_value(runtime, value);
}

void countedReleaseObject(expo_jsi_runtime_handle runtime, expo_jsi_object_handle object)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && object != nullptr) {
    testhost->counters.released_objects++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  api->release_object(runtime, object);
}

void countedReleaseFunction(expo_jsi_runtime_handle runtime, expo_jsi_function_handle function)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && function != nullptr) {
    testhost->counters.released_functions++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  api->release_function(runtime, function);
}

struct CountedStringReleaseContext {
  expo_jsi_testhost_runtime_t *testhost;
  expo_jsi_release_string_fn release;
  void *releaseContext;
};

void countedReleaseString(void *releaseContext)
{
  auto *context = static_cast<CountedStringReleaseContext *>(releaseContext);
  if (context->testhost != nullptr) {
    context->testhost->counters.released_strings++;
  }
  if (context->release != nullptr) {
    context->release(context->releaseContext);
  }
  delete context;
}

expo_jsi_string_result countedGetString(expo_jsi_runtime_handle runtime,
                                        expo_jsi_value_handle value)
{
  auto *testhost = runtimeFor(runtime);
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  auto result = api->get_string(runtime, value);
  if (result.ok == 0 || result.release == nullptr) {
    return result;
  }

  auto *context = new CountedStringReleaseContext{testhost, result.release, result.release_context};
  result.release_context = context;
  result.release = countedReleaseString;
  return result;
}

const expo_jsi_api *makeCountedApi(expo_jsi_testhost_runtime_t &runtime)
{
  runtime.innerApi = expo::jsi::api();
  runtime.countedApi = *runtime.innerApi;
  runtime.countedApi.release_value = countedReleaseValue;
  runtime.countedApi.release_object = countedReleaseObject;
  runtime.countedApi.release_function = countedReleaseFunction;
  runtime.countedApi.get_string = countedGetString;
  return &runtime.countedApi;
}

} // namespace

extern "C" expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void)
{
  try {
    auto *testhost = new expo_jsi_testhost_runtime_t();
    testhost->runtime = expo::jsi::createRuntimeHandle(testhost->connector);
    if (testhost->runtime == nullptr) {
      delete testhost;
      return expo_jsi_testhost_create_result{
        0,
        nullptr,
        nullptr,
        nullptr,
        makeError(1, "Failed to create runtime handle."),
      };
    }

    activeCounterRuntime = testhost;
    return expo_jsi_testhost_create_result{
      1,
      makeCountedApi(*testhost),
      testhost->runtime,
      testhost,
      expo_jsi_error{0, nullptr, 0},
    };
  } catch (const std::exception &error) {
    return expo_jsi_testhost_create_result{
      0,
      nullptr,
      nullptr,
      nullptr,
      makeError(2, error.what()),
    };
  } catch (...) {
    return expo_jsi_testhost_create_result{
      0,
      nullptr,
      nullptr,
      nullptr,
      makeError(3, "Unknown native exception while creating testhost runtime."),
    };
  }
}

extern "C" expo_jsi_value_result expo_jsi_testhost_evaluate_script(
  expo_jsi_testhost_runtime_handle testhostRuntime,
  const uint8_t *source,
  int32_t sourceLength,
  const uint8_t *sourceUrl,
  int32_t sourceUrlLength)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr || source == nullptr || sourceLength < 0) {
    return makeErrorResult(4, "Invalid evaluate_script arguments.");
  }
  if (sourceUrlLength < 0 || (sourceUrl == nullptr && sourceUrlLength > 0)) {
    return makeErrorResult(5, "Invalid evaluate_script source URL.");
  }

  try {
    auto &runtime = testhost->connector.runtime();
    auto script =
      std::string(reinterpret_cast<const char *>(source), static_cast<size_t>(sourceLength));
    auto url = sourceUrl == nullptr || sourceUrlLength == 0
                 ? std::string("expo-jsi-test.js")
                 : std::string(reinterpret_cast<const char *>(sourceUrl),
                               static_cast<size_t>(sourceUrlLength));
    auto value =
      runtime.evaluateJavaScript(std::make_unique<facebook::jsi::StringBuffer>(script), url);
    return expo_jsi_value_result{
      1,
      expo::jsi::createOwnedValueHandle(std::move(value)),
      expo_jsi_error{0, nullptr, 0},
    };
  } catch (const facebook::jsi::JSError &error) {
    return makeErrorResult(6, error.what());
  } catch (const std::exception &error) {
    return makeErrorResult(7, error.what());
  } catch (...) {
    return makeErrorResult(8, "Unknown native exception while evaluating script.");
  }
}

extern "C" expo_jsi_testhost_counters expo_jsi_testhost_get_counters(
  expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  return testhost == nullptr ? expo_jsi_testhost_counters{} : testhost->counters;
}

extern "C" void expo_jsi_testhost_reset_counters(expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost != nullptr) {
    testhost->counters = expo_jsi_testhost_counters{};
  }
}

extern "C" void expo_jsi_testhost_release_runtime(expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr) {
    return;
  }
  if (activeCounterRuntime == testhost) {
    activeCounterRuntime = nullptr;
  }
  expo::jsi::releaseRuntimeHandle(testhost->runtime);
  testhost->runtime = nullptr;
  testhost->connector.invalidate();
  delete testhost;
}
