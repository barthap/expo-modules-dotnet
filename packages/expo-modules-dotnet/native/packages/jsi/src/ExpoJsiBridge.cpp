#include "ExpoJsiBridge.h"

#include <cstring>
#include <exception>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

#include "JsiRuntimeConnector.h"

namespace expo::dotnet {

class RuntimeHandle final {
public:
  explicit RuntimeHandle(JsiRuntimeConnector &connector)
    : connector_(&connector)
  {
  }

  jsi::Runtime &runtime()
  {
    if (connector_ == nullptr || !connector_->isRuntimeValid()) {
      throw std::runtime_error("Runtime connector is invalid.");
    }
    return connector_->runtime();
  }

  JsiRuntimeExecutor &runtimeExecutor()
  {
    if (connector_ == nullptr || !connector_->isRuntimeValid()) {
      throw std::runtime_error("Runtime connector is invalid.");
    }
    return connector_->runtimeExecutor();
  }

  bool isRuntimeValid() const
  {
    return connector_ != nullptr && connector_->isRuntimeValid();
  }

private:
  JsiRuntimeConnector *connector_;
};

class ValueHandle final {
public:
  static std::unique_ptr<ValueHandle> owned(jsi::Value value)
  {
    return std::unique_ptr<ValueHandle>(
      new ValueHandle(std::make_unique<jsi::Value>(std::move(value))));
  }

  static std::unique_ptr<ValueHandle> borrowed(const jsi::Value &value)
  {
    return std::unique_ptr<ValueHandle>(new ValueHandle(&value));
  }

  jsi::Value &value()
  {
    return ownedValue_ != nullptr ? *ownedValue_ : *const_cast<jsi::Value *>(borrowedValue_);
  }

  const jsi::Value &value() const
  {
    return ownedValue_ != nullptr ? *ownedValue_ : *borrowedValue_;
  }

  bool isOwned() const
  {
    return ownedValue_ != nullptr;
  }

private:
  explicit ValueHandle(std::unique_ptr<jsi::Value> value)
    : ownedValue_(std::move(value))
  {
  }

  explicit ValueHandle(const jsi::Value *value)
    : borrowedValue_(value)
  {
  }

  std::unique_ptr<jsi::Value> ownedValue_;
  const jsi::Value *borrowedValue_ = nullptr;
};

class PromiseHandle final {
public:
  static std::unique_ptr<PromiseHandle> owned(jsi::Object promise,
                                              jsi::Function resolve,
                                              jsi::Function reject)
  {
    return std::unique_ptr<PromiseHandle>(
      new PromiseHandle(std::move(promise), std::move(resolve), std::move(reject)));
  }

  jsi::Object &promise()
  {
    return *promise_;
  }

  void resolve(jsi::Runtime &runtime, const jsi::Value &value)
  {
    if (settled_ || !resolve_.has_value()) {
      return;
    }

    resolve_->call(runtime, value);
    settled_ = true;
    resolve_.reset();
    reject_.reset();
  }

  void reject(jsi::Runtime &runtime, const jsi::Value &value)
  {
    if (settled_ || !reject_.has_value()) {
      return;
    }

    reject_->call(runtime, value);
    settled_ = true;
    resolve_.reset();
    reject_.reset();
  }

private:
  PromiseHandle(jsi::Object promise, jsi::Function resolve, jsi::Function reject)
    : promise_(std::make_unique<jsi::Object>(std::move(promise))),
      resolve_(std::move(resolve)),
      reject_(std::move(reject))
  {
  }

  std::unique_ptr<jsi::Object> promise_;
  std::optional<jsi::Function> resolve_;
  std::optional<jsi::Function> reject_;
  bool settled_ = false;
};

class ArgumentsHandle final {
public:
  ArgumentsHandle(const jsi::Value *arguments, size_t count)
    : arguments_(arguments),
      count_(count)
  {
  }

  size_t count() const
  {
    return count_;
  }

  const jsi::Value &at(size_t index) const
  {
    if (index >= count_) {
      throw std::out_of_range("Argument index is out of range.");
    }
    return arguments_[index];
  }

  expo_jsi_value_handle borrowedValueAt(size_t index)
  {
    auto value = ValueHandle::borrowed(at(index));
    auto *handle = value.get();
    borrowedValues_.push_back(std::move(value));
    return handle;
  }

private:
  const jsi::Value *arguments_;
  size_t count_;
  std::vector<std::unique_ptr<ValueHandle>> borrowedValues_;
};

} // namespace expo::dotnet

namespace {

namespace jsi = facebook::jsi;

constexpr uint32_t kApiVersion = 12;
thread_local std::string lastErrorMessage;

struct StringResultBuffer {
  explicit StringResultBuffer(std::string value)
    : value(std::move(value))
  {
  }

  std::string value;
};

expo_jsi_error makeError(int32_t code, const char *message)
{
  lastErrorMessage = message == nullptr ? "Unknown native exception." : message;
  return expo_jsi_error{
    code,
    lastErrorMessage.c_str(),
    static_cast<int32_t>(lastErrorMessage.size()),
  };
}

expo_jsi_error makeOk()
{
  return expo_jsi_error{0, nullptr, 0};
}

expo::dotnet::JsiRuntimeTaskPriority toRuntimeTaskPriority(expo_jsi_task_priority priority)
{
  switch (priority) {
  case EXPO_JSI_TASK_IMMEDIATE:
    return expo::dotnet::JsiRuntimeTaskPriority::Immediate;
  case EXPO_JSI_TASK_USER_BLOCKING:
    return expo::dotnet::JsiRuntimeTaskPriority::UserBlocking;
  case EXPO_JSI_TASK_LOW:
    return expo::dotnet::JsiRuntimeTaskPriority::Low;
  case EXPO_JSI_TASK_IDLE:
    return expo::dotnet::JsiRuntimeTaskPriority::Idle;
  case EXPO_JSI_TASK_NORMAL:
  default:
    return expo::dotnet::JsiRuntimeTaskPriority::Normal;
  }
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

bool isUtf8Continuation(uint8_t value)
{
  return (value & 0xC0) == 0x80;
}

bool isValidUtf8(const uint8_t *data, int32_t length)
{
  if (length < 0 || (data == nullptr && length > 0)) {
    return false;
  }

  int32_t index = 0;
  while (index < length) {
    const uint8_t first = data[index];
    if (first <= 0x7F) {
      index++;
      continue;
    }

    if (first >= 0xC2 && first <= 0xDF) {
      if (index + 1 >= length || !isUtf8Continuation(data[index + 1])) {
        return false;
      }
      index += 2;
      continue;
    }

    if (first == 0xE0) {
      if (index + 2 >= length || data[index + 1] < 0xA0 || data[index + 1] > 0xBF ||
          !isUtf8Continuation(data[index + 2])) {
        return false;
      }
      index += 3;
      continue;
    }

    if ((first >= 0xE1 && first <= 0xEC) || (first >= 0xEE && first <= 0xEF)) {
      if (index + 2 >= length || !isUtf8Continuation(data[index + 1]) ||
          !isUtf8Continuation(data[index + 2])) {
        return false;
      }
      index += 3;
      continue;
    }

    if (first == 0xED) {
      if (index + 2 >= length || data[index + 1] < 0x80 || data[index + 1] > 0x9F ||
          !isUtf8Continuation(data[index + 2])) {
        return false;
      }
      index += 3;
      continue;
    }

    if (first == 0xF0) {
      if (index + 3 >= length || data[index + 1] < 0x90 || data[index + 1] > 0xBF ||
          !isUtf8Continuation(data[index + 2]) || !isUtf8Continuation(data[index + 3])) {
        return false;
      }
      index += 4;
      continue;
    }

    if (first >= 0xF1 && first <= 0xF3) {
      if (index + 3 >= length || !isUtf8Continuation(data[index + 1]) ||
          !isUtf8Continuation(data[index + 2]) || !isUtf8Continuation(data[index + 3])) {
        return false;
      }
      index += 4;
      continue;
    }

    if (first == 0xF4) {
      if (index + 3 >= length || data[index + 1] < 0x80 || data[index + 1] > 0x8F ||
          !isUtf8Continuation(data[index + 2]) || !isUtf8Continuation(data[index + 3])) {
        return false;
      }
      index += 4;
      continue;
    }

    return false;
  }

  return true;
}

expo::dotnet::RuntimeHandle *tryRuntimeHandle(expo_jsi_runtime_handle runtime,
                                              expo_jsi_error *error)
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

expo::dotnet::RuntimeHandle *tryRuntimeHandleWithoutAccess(expo_jsi_runtime_handle runtime,
                                                           expo_jsi_error *error)
{
  auto *handle = runtime;
  if (handle == nullptr) {
    writeError(error, 1, "Runtime handle is null.");
    return nullptr;
  }
  if (!handle->isRuntimeValid()) {
    writeError(error, 2, "Runtime connector is invalid.");
    return nullptr;
  }
  return handle;
}

expo_jsi_value_result makeValueResult(std::unique_ptr<expo::dotnet::ValueHandle> value)
{
  return expo_jsi_value_result{
    1,
    value.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_value_result makeBorrowedValueResult(expo_jsi_value_handle value)
{
  return expo_jsi_value_result{
    1,
    value,
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_promise_result makePromiseResult(std::unique_ptr<expo::dotnet::PromiseHandle> promise)
{
  return expo_jsi_promise_result{
    1,
    promise.release(),
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

expo_jsi_promise_result makePromiseErrorResult(int32_t code, const char *message)
{
  return expo_jsi_promise_result{0, nullptr, makeError(code, message)};
}

expo_jsi_string_result makeStringResult(std::string value)
{
  auto *buffer = new StringResultBuffer(std::move(value));
  return expo_jsi_string_result{
    1,
    reinterpret_cast<const uint8_t *>(buffer->value.data()),
    static_cast<int32_t>(buffer->value.size()),
    buffer,
    [](void *release_context) { delete static_cast<StringResultBuffer *>(release_context); },
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_string_result makeStringErrorResult(int32_t code, const char *message)
{
  return expo_jsi_string_result{0, nullptr, 0, nullptr, nullptr, makeError(code, message)};
}

jsi::Object checkedObject(jsi::Runtime &runtime, expo_jsi_value_handle value)
{
  if (value == nullptr) {
    throw std::invalid_argument("Value handle is null.");
  }
  if (!value->value().isObject()) {
    throw std::invalid_argument("Value is not an object.");
  }
  return value->value().asObject(runtime);
}

jsi::Array checkedArray(jsi::Runtime &runtime, expo_jsi_value_handle value)
{
  if (value == nullptr) {
    throw std::invalid_argument("Value handle is null.");
  }
  if (!value->value().isObject()) {
    throw std::invalid_argument("Value is not an array.");
  }
  auto object = value->value().asObject(runtime);
  if (!object.isArray(runtime)) {
    throw std::invalid_argument("Value is not an array.");
  }
  return object.asArray(runtime);
}

jsi::Function checkedFunction(jsi::Runtime &runtime, expo_jsi_value_handle value)
{
  if (value == nullptr) {
    throw std::invalid_argument("Value handle is null.");
  }
  if (!value->value().isObject()) {
    throw std::invalid_argument("Value is not a function.");
  }
  auto object = value->value().asObject(runtime);
  if (!object.isFunction(runtime)) {
    throw std::invalid_argument("Value is not a function.");
  }
  return object.asFunction(runtime);
}

expo_jsi_value_result createNumber(expo_jsi_runtime_handle runtime, double number)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(number)));
  } catch (const std::exception &ex) {
    return makeErrorResult(3, ex.what());
  } catch (...) {
    return makeErrorResult(4, "Unknown native exception while creating number.");
  }
}

expo_jsi_value_result createBool(expo_jsi_runtime_handle runtime, uint8_t value)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(value != 0)));
  } catch (const std::exception &ex) {
    return makeErrorResult(12, ex.what());
  } catch (...) {
    return makeErrorResult(13, "Unknown native exception while creating boolean.");
  }
}

expo_jsi_value_result createString(expo_jsi_runtime_handle runtime,
                                   const uint8_t *data,
                                   int32_t length)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (!isValidUtf8(data, length)) {
    return makeErrorResult(42, "String data is not valid UTF-8.");
  }

  try {
    const char *text = length == 0 ? "" : reinterpret_cast<const char *>(data);
    auto value =
      jsi::Value(runtimeHandle->runtime(),
                 jsi::String::createFromUtf8(runtimeHandle->runtime(),
                                             std::string(text, static_cast<size_t>(length))));
    return makeValueResult(expo::dotnet::ValueHandle::owned(std::move(value)));
  } catch (const std::exception &ex) {
    return makeErrorResult(43, ex.what());
  } catch (...) {
    return makeErrorResult(44, "Unknown native exception while creating string.");
  }
}

expo_jsi_value_result cloneValue(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (value == nullptr) {
    return makeErrorResult(101, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, value->value())));
  } catch (const std::exception &ex) {
    return makeErrorResult(102, ex.what());
  } catch (...) {
    return makeErrorResult(103, "Unknown native exception while cloning value.");
  }
}

expo_jsi_value_result createError(expo_jsi_runtime_handle runtime,
                                  const uint8_t *message,
                                  int32_t messageLength)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (!isValidUtf8(message, messageLength)) {
    return makeErrorResult(104, "Error message is not valid UTF-8.");
  }

  try {
    const char *text = messageLength == 0 ? "" : reinterpret_cast<const char *>(message);
    auto jsError =
      jsi::JSError(runtimeHandle->runtime(), std::string(text, static_cast<size_t>(messageLength)));
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsi::Value(runtimeHandle->runtime(), jsError.value())));
  } catch (const std::exception &ex) {
    return makeErrorResult(105, ex.what());
  } catch (...) {
    return makeErrorResult(106, "Unknown native exception while creating JavaScript error.");
  }
}

expo_jsi_value_kind getValueKind(expo_jsi_runtime_handle runtime,
                                 expo_jsi_value_handle value,
                                 expo_jsi_error *error)
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

uint8_t getBool(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value, expo_jsi_error *error)
{
  // The return value is only the bool payload. Error state is reported through `error`.
  if (tryRuntimeHandle(runtime, error) == nullptr) {
    return 0;
  }

  auto *valueHandle = value;
  if (valueHandle == nullptr) {
    writeError(error, 8, "Value handle is null.");
    return 0;
  }

  try {
    if (!valueHandle->value().isBool()) {
      writeError(error, 9, "Value is not a boolean.");
      return 0;
    }
    clearError(error);
    return valueHandle->value().asBool() ? 1 : 0;
  } catch (const std::exception &ex) {
    writeError(error, 10, ex.what());
    return 0;
  } catch (...) {
    writeError(error, 11, "Unknown native exception while reading boolean.");
    return 0;
  }
}

double getDouble(expo_jsi_runtime_handle runtime,
                 expo_jsi_value_handle value,
                 expo_jsi_error *error)
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

expo_jsi_string_result getString(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_string_result{0, nullptr, 0, nullptr, nullptr, error};
  }
  auto *valueHandle = value;
  if (valueHandle == nullptr) {
    return makeStringErrorResult(45, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    if (!valueHandle->value().isString()) {
      return makeStringErrorResult(46, "Value is not a string.");
    }
    return makeStringResult(valueHandle->value().getString(jsRuntime).utf8(jsRuntime));
  } catch (const std::exception &ex) {
    return makeStringErrorResult(47, ex.what());
  } catch (...) {
    return makeStringErrorResult(48, "Unknown native exception while reading string.");
  }
}

expo_jsi_string_result coerceToString(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_string_result{0, nullptr, 0, nullptr, nullptr, error};
  }
  auto *valueHandle = value;
  if (valueHandle == nullptr) {
    return makeStringErrorResult(113, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    return makeStringResult(valueHandle->value().toString(jsRuntime).utf8(jsRuntime));
  } catch (const std::exception &ex) {
    return makeStringErrorResult(114, ex.what());
  } catch (...) {
    return makeStringErrorResult(115, "Unknown native exception while coercing value to string.");
  }
}

expo_jsi_value_result getGlobalObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, jsRuntime.global())));
  } catch (const std::exception &ex) {
    return makeErrorResult(14, ex.what());
  } catch (...) {
    return makeErrorResult(15, "Unknown native exception while getting global object.");
  }
}

expo_jsi_value_result createObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, jsi::Object(jsRuntime))));
  } catch (const std::exception &ex) {
    return makeErrorResult(16, ex.what());
  } catch (...) {
    return makeErrorResult(17, "Unknown native exception while creating object.");
  }
}

expo_jsi_value_result valueRetainAs(expo_jsi_runtime_handle runtime,
                                    expo_jsi_value_handle value,
                                    expo_jsi_value_expectation expectation)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (value == nullptr) {
    return makeErrorResult(38, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    switch (expectation) {
    case EXPO_JSI_EXPECT_OBJECT:
      (void)checkedObject(jsRuntime, value);
      break;
    case EXPO_JSI_EXPECT_ARRAY:
      (void)checkedArray(jsRuntime, value);
      break;
    case EXPO_JSI_EXPECT_FUNCTION:
      (void)checkedFunction(jsRuntime, value);
      break;
    default:
      return makeErrorResult(39, "Unknown value expectation.");
    }

    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, value->value())));
  } catch (const std::exception &ex) {
    return makeErrorResult(40, ex.what());
  } catch (...) {
    return makeErrorResult(41, "Unknown native exception while retaining checked value.");
  }
}

expo_jsi_value_result createArray(expo_jsi_runtime_handle runtime, uint32_t length)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto array = jsi::Array(jsRuntime, length);
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, array)));
  } catch (const std::exception &ex) {
    return makeErrorResult(63, ex.what());
  } catch (...) {
    return makeErrorResult(64, "Unknown native exception while creating array.");
  }
}

uint32_t arrayGetLength(expo_jsi_runtime_handle runtime,
                        expo_jsi_value_handle array,
                        expo_jsi_error *error)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, error);
  if (runtimeHandle == nullptr) {
    return 0;
  }
  if (array == nullptr) {
    writeError(error, 76, "Array handle is null.");
    return 0;
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsArray = checkedArray(jsRuntime, array);
    clearError(error);
    return static_cast<uint32_t>(jsArray.length(jsRuntime));
  } catch (const std::exception &ex) {
    writeError(error, 77, ex.what());
    return 0;
  } catch (...) {
    writeError(error, 78, "Unknown native exception while reading array length.");
    return 0;
  }
}

expo_jsi_value_result arrayGetValueAtIndex(expo_jsi_runtime_handle runtime,
                                           expo_jsi_value_handle array,
                                           uint32_t index)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (array == nullptr) {
    return makeErrorResult(79, "Array handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsArray = checkedArray(jsRuntime, array);
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsArray.getValueAtIndex(jsRuntime, index)));
  } catch (const std::exception &ex) {
    return makeErrorResult(80, ex.what());
  } catch (...) {
    return makeErrorResult(81, "Unknown native exception while reading array value.");
  }
}

expo_jsi_error arraySetValueAtIndex(expo_jsi_runtime_handle runtime,
                                    expo_jsi_value_handle array,
                                    uint32_t index,
                                    expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(82, "Runtime handle is invalid.");
  }
  if (array == nullptr) {
    return makeError(83, "Array handle is null.");
  }
  if (value == nullptr) {
    return makeError(84, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsArray = checkedArray(jsRuntime, array);
    jsArray.setValueAtIndex(jsRuntime, index, value->value());
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(85, ex.what());
  } catch (...) {
    return makeError(86, "Unknown native exception while setting array value.");
  }
}

expo_jsi_promise_result createPromise(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_promise_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    std::optional<jsi::Function> resolveFunction;
    std::optional<jsi::Function> rejectFunction;

    auto setup = jsi::Function::createFromHostFunction(
      jsRuntime,
      jsi::PropNameID::forAscii(jsRuntime, "promiseExecutor"),
      2,
      [&resolveFunction, &rejectFunction](jsi::Runtime &runtime,
                                          const jsi::Value &,
                                          const jsi::Value *arguments,
                                          size_t count) -> jsi::Value {
        if (count < 2 || !arguments[0].isObject() || !arguments[1].isObject()) {
          throw jsi::JSError(runtime, "Promise executor expected resolve and reject.");
        }

        auto resolveObject = arguments[0].asObject(runtime);
        auto rejectObject = arguments[1].asObject(runtime);
        if (!resolveObject.isFunction(runtime) || !rejectObject.isFunction(runtime)) {
          throw jsi::JSError(runtime, "Promise executor arguments must be functions.");
        }

        resolveFunction = resolveObject.asFunction(runtime);
        rejectFunction = rejectObject.asFunction(runtime);
        return jsi::Value::undefined();
      });

    auto promiseConstructor = jsRuntime.global().getPropertyAsFunction(jsRuntime, "Promise");
    auto promiseValue = promiseConstructor.callAsConstructor(jsRuntime, setup);
    if (!promiseValue.isObject() || !resolveFunction.has_value() || !rejectFunction.has_value()) {
      return makePromiseErrorResult(85, "Failed to create JavaScript promise.");
    }

    return makePromiseResult(expo::dotnet::PromiseHandle::owned(
      promiseValue.asObject(jsRuntime), std::move(*resolveFunction), std::move(*rejectFunction)));
  } catch (const std::exception &ex) {
    return makePromiseErrorResult(86, ex.what());
  } catch (...) {
    return makePromiseErrorResult(87, "Unknown native exception while creating promise.");
  }
}

expo_jsi_value_result promiseAsValue(expo_jsi_runtime_handle runtime,
                                     expo_jsi_promise_handle promise)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (promise == nullptr) {
    return makeErrorResult(88, "Promise handle is null.");
  }

  try {
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsi::Value(runtimeHandle->runtime(), promise->promise())));
  } catch (const std::exception &ex) {
    return makeErrorResult(89, ex.what());
  } catch (...) {
    return makeErrorResult(90, "Unknown native exception while converting promise to value.");
  }
}

uint8_t isInstanceOfGlobalConstructor(expo_jsi_runtime_handle runtime,
                                      expo_jsi_value_handle value,
                                      const char *constructorName,
                                      int32_t nullValueErrorCode,
                                      int32_t exceptionErrorCode,
                                      int32_t unknownErrorCode,
                                      expo_jsi_error *error)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, error);
  if (runtimeHandle == nullptr) {
    return 0;
  }
  if (value == nullptr) {
    writeError(error, nullValueErrorCode, "Value handle is null.");
    return 0;
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    clearError(error);
    if (!value->value().isObject()) {
      return 0;
    }

    auto constructorValue = jsRuntime.global().getProperty(jsRuntime, constructorName);
    if (!constructorValue.isObject()) {
      return 0;
    }

    auto constructorObject = constructorValue.asObject(jsRuntime);
    if (!constructorObject.isFunction(jsRuntime)) {
      return 0;
    }

    auto object = value->value().asObject(jsRuntime);
    auto constructor = constructorObject.asFunction(jsRuntime);
    return object.instanceOf(jsRuntime, constructor) ? 1 : 0;
  } catch (const std::exception &ex) {
    writeError(error, exceptionErrorCode, ex.what());
    return 0;
  } catch (...) {
    writeError(error, unknownErrorCode, "Unknown native exception while checking value type.");
    return 0;
  }
}

uint8_t isPromise(expo_jsi_runtime_handle runtime,
                  expo_jsi_value_handle value,
                  expo_jsi_error *error)
{
  return isInstanceOfGlobalConstructor(runtime, value, "Promise", 107, 108, 109, error);
}

uint8_t isError(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value, expo_jsi_error *error)
{
  return isInstanceOfGlobalConstructor(runtime, value, "Error", 110, 111, 112, error);
}

expo_jsi_error promiseSettle(expo_jsi_runtime_handle runtime,
                             expo_jsi_promise_handle promise,
                             expo_jsi_promise_settlement settlement,
                             expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(91, "Runtime handle is invalid.");
  }
  if (promise == nullptr) {
    return makeError(92, "Promise handle is null.");
  }
  if (value == nullptr) {
    return makeError(93, "Value handle is null.");
  }

  try {
    if (settlement == EXPO_JSI_PROMISE_RESOLVE) {
      promise->resolve(runtimeHandle->runtime(), value->value());
    } else if (settlement == EXPO_JSI_PROMISE_REJECT) {
      promise->reject(runtimeHandle->runtime(), value->value());
    } else {
      return makeError(94, "Unknown promise settlement.");
    }
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(95, ex.what());
  } catch (...) {
    return makeError(96, "Unknown native exception while settling promise.");
  }
}

expo_jsi_error objectSetProperty(expo_jsi_runtime_handle runtime,
                                 expo_jsi_value_handle object,
                                 const char *name,
                                 int32_t name_len,
                                 expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(21, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeError(22, "Value handle is null.");
  }
  if (name == nullptr || name_len < 0) {
    return makeError(23, "Property name is invalid.");
  }
  if (value == nullptr) {
    return makeError(24, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto propertyName = jsi::PropNameID::forUtf8(
      jsRuntime, reinterpret_cast<const uint8_t *>(name), static_cast<size_t>(name_len));
    auto jsObject = checkedObject(jsRuntime, object);
    jsObject.setProperty(jsRuntime, propertyName, value->value());
    return expo_jsi_error{0, nullptr, 0};
  } catch (const std::exception &ex) {
    return makeError(25, ex.what());
  } catch (...) {
    return makeError(26, "Unknown native exception while setting object property.");
  }
}

expo_jsi_value_result objectGetProperty(expo_jsi_runtime_handle runtime,
                                        expo_jsi_value_handle object,
                                        const char *name,
                                        int32_t name_len)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeErrorResult(49, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeErrorResult(50, "Value handle is null.");
  }
  if (name == nullptr || name_len < 0) {
    return makeErrorResult(51, "Property name is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto propertyName = jsi::PropNameID::forUtf8(
      jsRuntime, reinterpret_cast<const uint8_t *>(name), static_cast<size_t>(name_len));
    auto jsObject = checkedObject(jsRuntime, object);
    return makeValueResult(
      expo::dotnet::ValueHandle::owned(jsObject.getProperty(jsRuntime, propertyName)));
  } catch (const std::exception &ex) {
    return makeErrorResult(52, ex.what());
  } catch (...) {
    return makeErrorResult(53, "Unknown native exception while getting object property.");
  }
}

class HostFunctionContext final {
public:
  HostFunctionContext(expo_jsi_host_function_callback_fn callback,
                      void *callbackContext,
                      expo_jsi_release_callback_context_fn releaseCallbackContext,
                      expo_jsi_runtime_handle runtimeHandle)
    : callback_(callback),
      callbackContext_(callbackContext),
      releaseCallbackContext_(releaseCallbackContext),
      runtimeHandle_(runtimeHandle)
  {
  }

  ~HostFunctionContext()
  {
    if (releaseCallbackContext_ != nullptr && callbackContext_ != nullptr) {
      releaseCallbackContext_(callbackContext_);
    }
  }

  expo_jsi_value_result call(expo_jsi_value_handle thisValue,
                             expo_jsi_arguments_handle arguments) const
  {
    if (callback_ == nullptr) {
      return makeErrorResult(27, "Host function callback is null.");
    }
    return callback_(callbackContext_, runtimeHandle_, thisValue, arguments);
  }

private:
  expo_jsi_host_function_callback_fn callback_;
  void *callbackContext_;
  expo_jsi_release_callback_context_fn releaseCallbackContext_;
  expo_jsi_runtime_handle runtimeHandle_;
};

class ScheduledTaskContext final {
public:
  ScheduledTaskContext(expo_jsi_task_callback_fn callback,
                       void *taskContext,
                       expo_jsi_release_task_context_fn releaseTaskContext)
    : callback_(callback),
      taskContext_(taskContext),
      releaseTaskContext_(releaseTaskContext)
  {
  }

  ~ScheduledTaskContext()
  {
    releaseOnce();
  }

  void invoke()
  {
    try {
      callback_(taskContext_);
      releaseOnce();
    } catch (...) {
      releaseOnce();
      throw;
    }
  }

private:
  void releaseOnce() noexcept
  {
    if (released_) {
      return;
    }
    released_ = true;
    if (releaseTaskContext_ != nullptr) {
      releaseTaskContext_(taskContext_);
    }
  }

  expo_jsi_task_callback_fn callback_;
  void *taskContext_;
  expo_jsi_release_task_context_fn releaseTaskContext_;
  bool released_ = false;
};

std::shared_ptr<ScheduledTaskContext> makeScheduledTaskContext(
  expo_jsi_task_callback_fn callback,
  void *taskContext,
  expo_jsi_release_task_context_fn releaseTaskContext)
{
  try {
    return std::make_shared<ScheduledTaskContext>(callback, taskContext, releaseTaskContext);
  } catch (...) {
    if (releaseTaskContext != nullptr) {
      releaseTaskContext(taskContext);
    }
    throw;
  }
}

expo_jsi_value_result createHostFunction(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (name == nullptr || name_len < 0) {
    return makeErrorResult(28, "Host function name is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto functionName = std::string(name, static_cast<size_t>(name_len));
    auto context = std::make_shared<HostFunctionContext>(
      callback, callback_context, release_callback_context, runtime);

    auto function = jsi::Function::createFromHostFunction(
      jsRuntime,
      jsi::PropNameID::forUtf8(jsRuntime, functionName),
      parameter_count,
      [context](jsi::Runtime &jsRuntime,
                const jsi::Value &thisValue,
                const jsi::Value *arguments,
                size_t count) -> jsi::Value {
        auto thisHandle = expo::dotnet::ValueHandle::borrowed(thisValue);
        auto argumentsHandle = expo::dotnet::ArgumentsHandle(arguments, count);
        expo_jsi_value_result result{};
        try {
          result = context->call(thisHandle.get(), &argumentsHandle);
          if (result.ok == 0 || result.value == nullptr) {
            const char *message = result.error.message != nullptr ? result.error.message
                                                                  : "Managed host function failed.";
            throw jsi::JSError(jsRuntime, message);
          }
          auto jsResult = jsi::Value(jsRuntime, result.value->value());
          delete result.value;
          return jsResult;
        } catch (const jsi::JSError &) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw;
        } catch (const std::exception &ex) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw jsi::JSError(jsRuntime, ex.what());
        }
      });

    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, function)));
  } catch (const std::exception &ex) {
    return makeErrorResult(29, ex.what());
  } catch (...) {
    return makeErrorResult(30, "Unknown native exception while creating host function.");
  }
}

uint32_t getArgumentsCount(expo_jsi_runtime_handle runtime,
                           expo_jsi_arguments_handle arguments,
                           expo_jsi_error *error)
{
  if (tryRuntimeHandle(runtime, error) == nullptr) {
    return 0;
  }
  if (arguments == nullptr) {
    writeError(error, 34, "Arguments handle is null.");
    return 0;
  }
  clearError(error);
  return static_cast<uint32_t>(arguments->count());
}

expo_jsi_value_result getArgumentValue(expo_jsi_runtime_handle runtime,
                                       expo_jsi_arguments_handle arguments,
                                       uint32_t index)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (arguments == nullptr) {
    return makeErrorResult(35, "Arguments handle is null.");
  }

  try {
    return makeBorrowedValueResult(arguments->borrowedValueAt(index));
  } catch (const std::exception &ex) {
    return makeErrorResult(36, ex.what());
  } catch (...) {
    return makeErrorResult(37, "Unknown native exception while reading argument.");
  }
}

void releaseValue(expo_jsi_runtime_handle, expo_jsi_value_handle value)
{
  auto *valueHandle = value;
  if (valueHandle != nullptr && !valueHandle->isOwned()) {
    return;
  }
  delete valueHandle;
}

void releasePromise(expo_jsi_runtime_handle, expo_jsi_promise_handle promise)
{
  delete promise;
}

expo_jsi_error scheduleTask(expo_jsi_runtime_handle runtime,
                            expo_jsi_task_priority priority,
                            expo_jsi_task_callback_fn callback,
                            void *taskContext,
                            expo_jsi_release_task_context_fn releaseTaskContext)
{
  if (callback == nullptr) {
    return makeError(54, "Task callback is null.");
  }

  try {
    // From this point native owns taskContext. If validation fails, the local
    // RAII wrapper releases it before returning the error to managed code.
    auto task = makeScheduledTaskContext(callback, taskContext, releaseTaskContext);

    expo_jsi_error error{};
    auto *runtimeHandle = tryRuntimeHandleWithoutAccess(runtime, &error);
    if (runtimeHandle == nullptr) {
      return error;
    }

    runtimeHandle->runtimeExecutor().executeAsync(toRuntimeTaskPriority(priority),
                                                  [task](jsi::Runtime &) { task->invoke(); });
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(55, ex.what());
  } catch (...) {
    return makeError(56, "Unknown native exception while scheduling runtime task.");
  }
}

uint8_t canExecuteSync(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandleWithoutAccess(runtime, &error);
  if (runtimeHandle == nullptr) {
    return 0;
  }
  return runtimeHandle->runtimeExecutor().canExecuteSync() ? 1 : 0;
}

expo_jsi_error executeSync(expo_jsi_runtime_handle runtime,
                           expo_jsi_task_callback_fn callback,
                           void *taskContext,
                           expo_jsi_release_task_context_fn releaseTaskContext)
{
  if (callback == nullptr) {
    return makeError(57, "Task callback is null.");
  }

  try {
    // From this point native owns taskContext. This avoids a double-release
    // when shutdown clears queued sync work and executeSync returns an error.
    auto task = makeScheduledTaskContext(callback, taskContext, releaseTaskContext);

    expo_jsi_error error{};
    auto *runtimeHandle = tryRuntimeHandleWithoutAccess(runtime, &error);
    if (runtimeHandle == nullptr) {
      return error;
    }
    if (!runtimeHandle->runtimeExecutor().canExecuteSync()) {
      return makeError(58, "Synchronous runtime execution is not supported.");
    }

    runtimeHandle->runtimeExecutor().executeSync([task](jsi::Runtime &) { task->invoke(); });
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(59, ex.what());
  } catch (...) {
    return makeError(60, "Unknown native exception while executing runtime task.");
  }
}

const expo_jsi_api kApi{
  sizeof(expo_jsi_api),
  kApiVersion,
  createNumber,
  createBool,
  getValueKind,
  getBool,
  getDouble,
  getGlobalObject,
  createObject,
  valueRetainAs,
  createArray,
  arrayGetLength,
  arrayGetValueAtIndex,
  arraySetValueAtIndex,
  createPromise,
  promiseAsValue,
  promiseSettle,
  objectSetProperty,
  objectGetProperty,
  createHostFunction,
  getArgumentsCount,
  getArgumentValue,
  releasePromise,
  releaseValue,
  createString,
  cloneValue,
  createError,
  getString,
  scheduleTask,
  canExecuteSync,
  executeSync,
  isPromise,
  isError,
  coerceToString,
};

} // namespace

namespace expo::dotnet {

expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector)
{
  return new RuntimeHandle(connector);
}

void releaseRuntimeHandle(expo_jsi_runtime_handle runtime)
{
  delete runtime;
}

expo_jsi_value_handle createOwnedValueHandle(jsi::Value value)
{
  return ValueHandle::owned(std::move(value)).release();
}

const expo_jsi_api *api()
{
  return &kApi;
}

} // namespace expo::dotnet
