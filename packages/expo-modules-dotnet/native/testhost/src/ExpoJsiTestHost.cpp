#include <expo_jsi_testhost.h>
#include <jsi/jsilib.h>

#include <cstring>
#include <exception>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>

#include "ExpoJsiBridge.h"
#include "HermesConsoleRuntimeConnector.h"

struct expo_jsi_testhost_runtime_t {
  expo::dotnet::HermesConsoleRuntimeConnector connector;
  expo_jsi_runtime_handle runtime = nullptr;
  expo_jsi_api countedApi{};
  const expo_jsi_api *innerApi = nullptr;
  expo_jsi_testhost_counters counters{};
  bool syncExecutionSupported = true;
};

namespace {

namespace jsi = facebook::jsi;

struct ErrorResultBuffer {
  explicit ErrorResultBuffer(std::string value)
    : value(std::move(value))
  {
  }

  std::string value;
};

expo_jsi_error makeError(int32_t code, const char *message)
{
  auto *buffer =
    new ErrorResultBuffer(message == nullptr ? "Unknown native testhost error." : message);
  return expo_jsi_error{
    code,
    buffer->value.c_str(),
    static_cast<int32_t>(buffer->value.size()),
    buffer,
    [](void *release_context) { delete static_cast<ErrorResultBuffer *>(release_context); },
  };
}

expo_jsi_error makeOk()
{
  return expo_jsi_error{0, nullptr, 0, nullptr, nullptr};
}

expo_jsi_value_result makeErrorResult(int32_t code, const char *message)
{
  return expo_jsi_value_result{0, nullptr, makeError(code, message)};
}

struct CountedErrorReleaseContext {
  expo_jsi_testhost_runtime_t *testhost;
  expo_jsi_release_error_fn release;
  void *releaseContext;
};

void countedReleaseError(void *releaseContext)
{
  auto *context = static_cast<CountedErrorReleaseContext *>(releaseContext);
  if (context->testhost != nullptr) {
    context->testhost->counters.released_errors++;
  }
  if (context->release != nullptr) {
    context->release(context->releaseContext);
  }
  delete context;
}

expo_jsi_error countErrorRelease(expo_jsi_testhost_runtime_t *testhost, expo_jsi_error error)
{
  if (error.code == 0 || error.release == nullptr) {
    return error;
  }

  auto *context = new CountedErrorReleaseContext{testhost, error.release, error.release_context};
  error.release_context = context;
  error.release = countedReleaseError;
  return error;
}

std::mutex counterRuntimesMutex;
std::unordered_map<expo_jsi_runtime_handle, expo_jsi_testhost_runtime_t *> counterRuntimes;

void registerRuntimeForCounters(expo_jsi_testhost_runtime_t &runtime)
{
  std::lock_guard<std::mutex> lock(counterRuntimesMutex);
  counterRuntimes[runtime.runtime] = &runtime;
}

void unregisterRuntimeForCounters(expo_jsi_runtime_handle runtime)
{
  std::lock_guard<std::mutex> lock(counterRuntimesMutex);
  counterRuntimes.erase(runtime);
}

expo_jsi_testhost_runtime_t *runtimeFor(expo_jsi_runtime_handle runtime)
{
  std::lock_guard<std::mutex> lock(counterRuntimesMutex);
  auto iterator = counterRuntimes.find(runtime);
  if (iterator != counterRuntimes.end()) {
    return iterator->second;
  }
  return nullptr;
}

expo_jsi_value_result countedCreateNumber(expo_jsi_runtime_handle runtime, double value)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr) {
    testhost->counters.deprecated_number_creates++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  return api->create_number(runtime, value);
}

expo_jsi_value_result countedCreateBool(expo_jsi_runtime_handle runtime, uint8_t value)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr) {
    testhost->counters.deprecated_bool_creates++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  return api->create_bool(runtime, value);
}

expo_jsi_value_result countedCreatePrimitiveValue(expo_jsi_runtime_handle runtime,
                                                  expo_jsi_value_kind kind,
                                                  uint64_t value)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr) {
    testhost->counters.primitive_value_creates++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  return api->create_primitive_value(runtime, kind, value);
}

uint8_t countedGetBool(expo_jsi_runtime_handle runtime,
                       expo_jsi_value_handle value,
                       expo_jsi_error *error)
{
  auto *testhost = runtimeFor(runtime);
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  expo_jsi_error innerError{};
  auto result = api->get_bool(runtime, value, &innerError);
  if (error != nullptr) {
    *error = countErrorRelease(testhost, innerError);
  } else if (innerError.release != nullptr) {
    innerError.release(innerError.release_context);
  }
  return result;
}

void countedReleaseValue(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && value != nullptr) {
    testhost->counters.released_values++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  api->release_value(runtime, value);
}

void countedReleasePromise(expo_jsi_runtime_handle runtime, expo_jsi_promise_handle promise)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && promise != nullptr) {
    testhost->counters.released_promises++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  api->release_promise(runtime, promise);
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
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  auto result = api->get_string(runtime, value);
  if (result.ok == 0 || result.release == nullptr) {
    return result;
  }

  auto *context = new CountedStringReleaseContext{testhost, result.release, result.release_context};
  result.release_context = context;
  result.release = countedReleaseString;
  return result;
}

struct CountedTaskContext {
  expo_jsi_testhost_runtime_t *testhost;
  expo_jsi_task_callback_fn callback;
  void *taskContext;
  expo_jsi_release_task_context_fn release;
};

void countedTaskCallback(void *taskContext)
{
  auto *context = static_cast<CountedTaskContext *>(taskContext);
  if (context->callback != nullptr) {
    context->callback(context->taskContext);
  }
}

void countedReleaseTaskContext(void *taskContext)
{
  auto *context = static_cast<CountedTaskContext *>(taskContext);
  if (context->testhost != nullptr) {
    context->testhost->counters.released_task_contexts++;
  }
  if (context->release != nullptr) {
    context->release(context->taskContext);
  }
  delete context;
}

expo_jsi_error countedScheduleTask(expo_jsi_runtime_handle runtime,
                                   expo_jsi_task_priority priority,
                                   expo_jsi_task_callback_fn callback,
                                   void *taskContext,
                                   expo_jsi_release_task_context_fn releaseTaskContext)
{
  auto *testhost = runtimeFor(runtime);
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  auto *countedContext =
    new CountedTaskContext{testhost, callback, taskContext, releaseTaskContext};
  // runtime_schedule_task owns countedContext after this call, including error
  // paths where the inner ABI validates the runtime before queueing work.
  auto error = api->runtime_schedule_task(
    runtime, priority, countedTaskCallback, countedContext, countedReleaseTaskContext);
  return error;
}

uint8_t countedCanExecuteSync(expo_jsi_runtime_handle runtime)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && !testhost->syncExecutionSupported) {
    return 0;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  return api->runtime_can_execute_sync(runtime);
}

expo_jsi_error countedExecuteSync(expo_jsi_runtime_handle runtime,
                                  expo_jsi_task_callback_fn callback,
                                  void *taskContext,
                                  expo_jsi_release_task_context_fn releaseTaskContext)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr) {
    testhost->counters.sync_execute_calls++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  auto *countedContext =
    new CountedTaskContext{testhost, callback, taskContext, releaseTaskContext};
  // runtime_execute_sync owns countedContext after this call. In particular,
  // shutdown may release queued sync work before returning an error here.
  auto error = api->runtime_execute_sync(
    runtime, countedTaskCallback, countedContext, countedReleaseTaskContext);
  return error;
}

struct CountedNativeStateReleaseContext {
  expo_jsi_testhost_runtime_t *testhost;
  void *innerContext;
  expo_jsi_release_native_state_fn innerRelease;
};

void countedReleaseNativeState(void *releaseContext,
                               uint64_t typeId,
                               uint64_t registryId,
                               uint32_t generation)
{
  auto *context = static_cast<CountedNativeStateReleaseContext *>(releaseContext);
  if (context->testhost != nullptr) {
    context->testhost->counters.released_native_states++;
  }
  if (context->innerRelease != nullptr) {
    context->innerRelease(context->innerContext, typeId, registryId, generation);
  }
  delete context;
}

expo_jsi_error countedSetNativeState(expo_jsi_runtime_handle runtime,
                                     expo_jsi_value_handle object,
                                     expo_jsi_native_state_token token,
                                     void *releaseContext,
                                     expo_jsi_release_native_state_fn release)
{
  auto *testhost = runtimeFor(runtime);
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::dotnet::api();
  auto *countedContext = new CountedNativeStateReleaseContext{testhost, releaseContext, release};
  auto error =
    api->object_set_native_state(runtime, object, token, countedContext, countedReleaseNativeState);
  if (error.code != 0) {
    delete countedContext;
  }
  return error;
}

const expo_jsi_api *makeCountedApi(expo_jsi_testhost_runtime_t &runtime)
{
  runtime.innerApi = expo::dotnet::api();
  runtime.countedApi = *runtime.innerApi;
  runtime.countedApi.create_number = countedCreateNumber;
  runtime.countedApi.create_bool = countedCreateBool;
  runtime.countedApi.create_primitive_value = countedCreatePrimitiveValue;
  runtime.countedApi.get_bool = countedGetBool;
  runtime.countedApi.release_value = countedReleaseValue;
  runtime.countedApi.release_promise = countedReleasePromise;
  runtime.countedApi.get_string = countedGetString;
  runtime.countedApi.runtime_schedule_task = countedScheduleTask;
  runtime.countedApi.runtime_can_execute_sync = countedCanExecuteSync;
  runtime.countedApi.runtime_execute_sync = countedExecuteSync;
  runtime.countedApi.object_set_native_state = countedSetNativeState;
  return &runtime.countedApi;
}

void installQueueMicrotask(expo_jsi_testhost_runtime_t &testhost)
{
  testhost.connector.runtimeExecutor().executeSync([](jsi::Runtime &runtime) {
    auto queueMicrotask = jsi::Function::createFromHostFunction(
      runtime,
      jsi::PropNameID::forAscii(runtime, "queueMicrotask"),
      1,
      [](jsi::Runtime &runtime, const jsi::Value &, const jsi::Value *arguments, size_t count)
        -> jsi::Value {
        if (count < 1 || !arguments[0].isObject()) {
          throw jsi::JSError(runtime, "queueMicrotask expects a function.");
        }

        auto callbackObject = arguments[0].asObject(runtime);
        if (!callbackObject.isFunction(runtime)) {
          throw jsi::JSError(runtime, "queueMicrotask expects a function.");
        }

        runtime.queueMicrotask(callbackObject.asFunction(runtime));
        return jsi::Value::undefined();
      });

    runtime.global().setProperty(runtime, "queueMicrotask", queueMicrotask);
  });
}

} // namespace

extern "C" expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void)
{
  try {
    auto *testhost = new expo_jsi_testhost_runtime_t();
    testhost->runtime = expo::dotnet::createRuntimeHandle(testhost->connector);
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

    registerRuntimeForCounters(*testhost);
    installQueueMicrotask(*testhost);
    return expo_jsi_testhost_create_result{
      1,
      makeCountedApi(*testhost),
      testhost->runtime,
      testhost,
      makeOk(),
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
    auto script =
      std::string(reinterpret_cast<const char *>(source), static_cast<size_t>(sourceLength));
    auto url = sourceUrl == nullptr || sourceUrlLength == 0
                 ? std::string("expo-jsi-test.js")
                 : std::string(reinterpret_cast<const char *>(sourceUrl),
                               static_cast<size_t>(sourceUrlLength));

    expo_jsi_value_result result{};
    testhost->connector.runtimeExecutor().executeSync([&](jsi::Runtime &runtime) {
      auto value = runtime.evaluateJavaScript(std::make_unique<jsi::StringBuffer>(script), url);
      result = expo_jsi_value_result{
        1,
        expo::dotnet::createOwnedValueHandle(std::move(value)),
        makeOk(),
      };
    });
    return result;
  } catch (const jsi::JSError &error) {
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

extern "C" void expo_jsi_testhost_drain_tasks(expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto error = expo_jsi_testhost_wait_until_idle(testhostRuntime);
  if (error.release != nullptr) {
    error.release(error.release_context);
  }
}

extern "C" expo_jsi_error expo_jsi_testhost_wait_until_idle(
  expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr) {
    return makeError(9, "Testhost runtime is null.");
  }
  try {
    testhost->connector.waitUntilIdle();
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(10, ex.what());
  } catch (...) {
    return makeError(11, "Unknown native exception while waiting for Hermes runtime idle.");
  }
}

extern "C" void expo_jsi_testhost_set_sync_execution_supported(
  expo_jsi_testhost_runtime_handle testhostRuntime, uint8_t supported)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost != nullptr) {
    testhost->syncExecutionSupported = supported != 0;
  }
}

extern "C" void expo_jsi_testhost_invalidate_runtime(
  expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost != nullptr) {
    testhost->connector.invalidate();
  }
}

extern "C" void expo_jsi_testhost_release_runtime(expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr) {
    return;
  }
  unregisterRuntimeForCounters(testhost->runtime);
  expo::dotnet::releaseRuntimeHandle(testhost->runtime);
  testhost->runtime = nullptr;
  testhost->connector.invalidate();
  delete testhost;
}
