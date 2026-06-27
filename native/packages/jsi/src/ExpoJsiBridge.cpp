#include "ExpoJsiBridge.h"

#include <cstring>
#include <exception>
#include <memory>
#include <string>

#include "JsiRuntimeConnector.h"

struct expo_jsi_runtime_t {
  expo::jsi::JsiRuntimeConnector *connector;
  uint32_t released_value_count;
};

struct expo_jsi_value_t {
  facebook::jsi::Value value;
};

namespace {

constexpr uint32_t kApiVersion = 1;

expo_jsi_error make_error(int32_t code, const char *message)
{
  return expo_jsi_error{
    code,
    message,
    static_cast<int32_t>(std::strlen(message)),
  };
}

void write_error(expo_jsi_error *error, int32_t code, const char *message)
{
  if (error != nullptr) {
    *error = make_error(code, message);
  }
}

void clear_error(expo_jsi_error *error)
{
  if (error != nullptr) {
    *error = expo_jsi_error{0, nullptr, 0};
  }
}

facebook::jsi::Runtime *try_runtime(expo_jsi_runtime_handle runtime, expo_jsi_error *error)
{
  if (runtime == nullptr || runtime->connector == nullptr) {
    write_error(error, 1, "Runtime handle is null.");
    return nullptr;
  }
  if (!runtime->connector->isRuntimeValid()) {
    write_error(error, 2, "Runtime connector is invalid.");
    return nullptr;
  }
  return &runtime->connector->runtime();
}

expo_jsi_value_result make_value_result(std::unique_ptr<expo_jsi_value_t> value)
{
  return expo_jsi_value_result{
    1,
    value.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_value_result make_error_result(int32_t code, const char *message)
{
  return expo_jsi_value_result{
    0,
    nullptr,
    make_error(code, message),
  };
}

expo_jsi_value_result create_number(expo_jsi_runtime_handle runtime, double number)
{
  expo_jsi_error error{};
  auto *js_runtime = try_runtime(runtime, &error);
  if (js_runtime == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    (void)js_runtime;
    return make_value_result(std::make_unique<expo_jsi_value_t>(
      expo_jsi_value_t{facebook::jsi::Value(number)}));
  } catch (const std::exception &ex) {
    return make_error_result(3, ex.what());
  } catch (...) {
    return make_error_result(4, "Unknown native exception while creating number.");
  }
}

expo_jsi_value_kind get_value_kind(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_error *error)
{
  auto *js_runtime = try_runtime(runtime, error);
  if (js_runtime == nullptr) {
    return EXPO_JSI_VALUE_UNDEFINED;
  }
  if (value == nullptr) {
    write_error(error, 5, "Value handle is null.");
    return EXPO_JSI_VALUE_UNDEFINED;
  }

  try {
    clear_error(error);
    auto &js_value = value->value;
    if (js_value.isUndefined()) {
      return EXPO_JSI_VALUE_UNDEFINED;
    }
    if (js_value.isNull()) {
      return EXPO_JSI_VALUE_NULL;
    }
    if (js_value.isBool()) {
      return EXPO_JSI_VALUE_BOOL;
    }
    if (js_value.isNumber()) {
      return EXPO_JSI_VALUE_NUMBER;
    }
    if (js_value.isString()) {
      return EXPO_JSI_VALUE_STRING;
    }
    if (js_value.isObject()) {
      auto object = js_value.asObject(*js_runtime);
      if (object.isFunction(*js_runtime)) {
        return EXPO_JSI_VALUE_FUNCTION;
      }
      if (object.isArrayBuffer(*js_runtime)) {
        return EXPO_JSI_VALUE_ARRAY_BUFFER;
      }
      return EXPO_JSI_VALUE_OBJECT;
    }
    return EXPO_JSI_VALUE_UNDEFINED;
  } catch (const std::exception &ex) {
    write_error(error, 6, ex.what());
    return EXPO_JSI_VALUE_UNDEFINED;
  } catch (...) {
    write_error(error, 7, "Unknown native exception while reading value kind.");
    return EXPO_JSI_VALUE_UNDEFINED;
  }
}

double get_double(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_error *error)
{
  if (try_runtime(runtime, error) == nullptr) {
    return 0.0;
  }
  if (value == nullptr) {
    write_error(error, 8, "Value handle is null.");
    return 0.0;
  }

  try {
    if (!value->value.isNumber()) {
      write_error(error, 9, "Value is not a number.");
      return 0.0;
    }
    clear_error(error);
    return value->value.asNumber();
  } catch (const std::exception &ex) {
    write_error(error, 10, ex.what());
    return 0.0;
  } catch (...) {
    write_error(error, 11, "Unknown native exception while reading number.");
    return 0.0;
  }
}

void release_value(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  if (runtime != nullptr) {
    runtime->released_value_count += value == nullptr ? 0 : 1;
  }
  delete value;
}

const expo_jsi_api kApi{
  sizeof(expo_jsi_api),
  kApiVersion,
  create_number,
  get_value_kind,
  get_double,
  release_value,
};

} // namespace

namespace expo::jsi {

expo_jsi_runtime_handle create_runtime_handle(JsiRuntimeConnector *connector)
{
  if (connector == nullptr) {
    return nullptr;
  }
  return new expo_jsi_runtime_t{connector, 0};
}

void release_runtime_handle(expo_jsi_runtime_handle runtime)
{
  delete runtime;
}

uint32_t released_value_count(expo_jsi_runtime_handle runtime)
{
  return runtime == nullptr ? 0 : runtime->released_value_count;
}

const expo_jsi_api *api()
{
  return &kApi;
}

} // namespace expo::jsi
