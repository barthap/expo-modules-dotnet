#include "ExpoJsiBridge.h"

#include <cstring>
#include <exception>
#include <memory>
#include <stdexcept>
#include <string>

#include "JsiRuntimeConnector.h"

namespace expo::jsi {

class RuntimeHandle final {
public:
  explicit RuntimeHandle(JsiRuntimeConnector &connector)
    : connector_(&connector)
  {
  }

  facebook::jsi::Runtime &runtime()
  {
    if (connector_ == nullptr || !connector_->isRuntimeValid()) {
      throw std::runtime_error("Runtime connector is invalid.");
    }
    return connector_->runtime();
  }

  void recordValueRelease()
  {
    releasedValueCount_++;
  }

  uint32_t releasedValueCount() const
  {
    return releasedValueCount_;
  }

private:
  JsiRuntimeConnector *connector_;
  uint32_t releasedValueCount_ = 0;
};

class ValueHandle final {
public:
  static std::unique_ptr<ValueHandle> owned(facebook::jsi::Value value)
  {
    return std::unique_ptr<ValueHandle>(
      new ValueHandle(std::make_unique<facebook::jsi::Value>(std::move(value))));
  }

  static std::unique_ptr<ValueHandle> borrowed(const facebook::jsi::Value &value)
  {
    return std::unique_ptr<ValueHandle>(new ValueHandle(&value));
  }

  facebook::jsi::Value &value()
  {
    return ownedValue_ != nullptr ? *ownedValue_
                                  : *const_cast<facebook::jsi::Value *>(borrowedValue_);
  }

  const facebook::jsi::Value &value() const
  {
    return ownedValue_ != nullptr ? *ownedValue_ : *borrowedValue_;
  }

  bool isOwned() const
  {
    return ownedValue_ != nullptr;
  }

private:
  explicit ValueHandle(std::unique_ptr<facebook::jsi::Value> value)
    : ownedValue_(std::move(value))
  {
  }

  explicit ValueHandle(const facebook::jsi::Value *value)
    : borrowedValue_(value)
  {
  }

  std::unique_ptr<facebook::jsi::Value> ownedValue_;
  const facebook::jsi::Value *borrowedValue_ = nullptr;
};

} // namespace expo::jsi

namespace {

constexpr uint32_t kApiVersion = 1;

expo_jsi_error makeError(int32_t code, const char *message)
{
  return expo_jsi_error{
    code,
    message,
    static_cast<int32_t>(std::strlen(message)),
  };
}

void writeError(expo_jsi_error *error, int32_t code, const char *message)
{
  if (error != nullptr) {
    *error = makeError(code, message);
  }
}

void clearError(expo_jsi_error *error)
{
  if (error != nullptr) {
    *error = expo_jsi_error{0, nullptr, 0};
  }
}

expo::jsi::RuntimeHandle *tryRuntimeHandle(expo_jsi_runtime_handle runtime, expo_jsi_error *error)
{
  auto *handle = runtime;
  if (handle == nullptr) {
    writeError(error, 1, "Runtime handle is null.");
    return nullptr;
  }
  try {
    (void)handle->runtime();
  } catch (const std::exception &ex) {
    writeError(error, 2, ex.what());
    return nullptr;
  }
  return handle;
}

expo_jsi_value_result makeValueResult(std::unique_ptr<expo::jsi::ValueHandle> value)
{
  return expo_jsi_value_result{
    1,
    value.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_value_result makeErrorResult(int32_t code, const char *message)
{
  return expo_jsi_value_result{
    0,
    nullptr,
    makeError(code, message),
  };
}

expo_jsi_value_result createNumber(expo_jsi_runtime_handle runtime, double number)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    (void)runtimeHandle;
    return makeValueResult(expo::jsi::ValueHandle::owned(facebook::jsi::Value(number)));
  } catch (const std::exception &ex) {
    return makeErrorResult(3, ex.what());
  } catch (...) {
    return makeErrorResult(4, "Unknown native exception while creating number.");
  }
}

expo_jsi_value_kind
getValueKind(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value, expo_jsi_error *error)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, error);
  if (runtimeHandle == nullptr) {
    return EXPO_JSI_VALUE_UNDEFINED;
  }
  auto *valueHandle = value;
  if (valueHandle == nullptr) {
    writeError(error, 5, "Value handle is null.");
    return EXPO_JSI_VALUE_UNDEFINED;
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    clearError(error);
    auto &js_value = valueHandle->value();
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
      auto object = js_value.asObject(jsRuntime);
      if (object.isFunction(jsRuntime)) {
        return EXPO_JSI_VALUE_FUNCTION;
      }
      if (object.isArrayBuffer(jsRuntime)) {
        return EXPO_JSI_VALUE_ARRAY_BUFFER;
      }
      return EXPO_JSI_VALUE_OBJECT;
    }
    return EXPO_JSI_VALUE_UNDEFINED;
  } catch (const std::exception &ex) {
    writeError(error, 6, ex.what());
    return EXPO_JSI_VALUE_UNDEFINED;
  } catch (...) {
    writeError(error, 7, "Unknown native exception while reading value kind.");
    return EXPO_JSI_VALUE_UNDEFINED;
  }
}

double
getDouble(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value, expo_jsi_error *error)
{
  if (tryRuntimeHandle(runtime, error) == nullptr) {
    return 0.0;
  }
  auto *valueHandle = value;
  if (valueHandle == nullptr) {
    writeError(error, 8, "Value handle is null.");
    return 0.0;
  }

  try {
    if (!valueHandle->value().isNumber()) {
      writeError(error, 9, "Value is not a number.");
      return 0.0;
    }
    clearError(error);
    return valueHandle->value().asNumber();
  } catch (const std::exception &ex) {
    writeError(error, 10, ex.what());
    return 0.0;
  } catch (...) {
    writeError(error, 11, "Unknown native exception while reading number.");
    return 0.0;
  }
}

void releaseValue(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  auto *valueHandle = value;
  if (valueHandle != nullptr && !valueHandle->isOwned()) {
    return;
  }
  if (auto *runtimeHandle = runtime) {
    if (valueHandle != nullptr) {
      runtimeHandle->recordValueRelease();
    }
  }
  delete valueHandle;
}

const expo_jsi_api kApi{
  sizeof(expo_jsi_api),
  kApiVersion,
  createNumber,
  getValueKind,
  getDouble,
  releaseValue,
};

} // namespace

namespace expo::jsi {

expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector)
{
  return new RuntimeHandle(connector);
}

void releaseRuntimeHandle(expo_jsi_runtime_handle runtime)
{
  delete runtime;
}

uint32_t releasedValueCount(expo_jsi_runtime_handle runtime)
{
  return runtime == nullptr ? 0 : runtime->releasedValueCount();
}

expo_jsi_value_handle createBorrowedValueHandle(const facebook::jsi::Value &value)
{
  return ValueHandle::borrowed(value).release();
}

void releaseBorrowedValueHandle(expo_jsi_value_handle value)
{
  if (value == nullptr) {
    return;
  }
  if (value->isOwned()) {
    throw std::runtime_error("releaseBorrowedValueHandle received an owned value handle.");
  }
  delete value;
}

facebook::jsi::Value copyValueToJsi(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    throw std::runtime_error(error.message == nullptr ? "Invalid runtime." : error.message);
  }
  if (value == nullptr) {
    throw std::runtime_error("Value handle is null.");
  }
  return facebook::jsi::Value(runtimeHandle->runtime(), value->value());
}

const expo_jsi_api *api()
{
  return &kApi;
}

} // namespace expo::jsi
