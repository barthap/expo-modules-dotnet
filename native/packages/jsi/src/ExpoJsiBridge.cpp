#include "ExpoJsiBridge.h"

#include <cstring>
#include <exception>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

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

  JsiRuntimeExecutor &runtimeExecutor()
  {
    if (connector_ == nullptr || !connector_->isRuntimeValid()) {
      throw std::runtime_error("Runtime connector is invalid.");
    }
    return connector_->runtimeExecutor();
  }

private:
  JsiRuntimeConnector *connector_;
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

class ObjectHandle final {
public:
  static std::unique_ptr<ObjectHandle> owned(facebook::jsi::Object object)
  {
    return std::unique_ptr<ObjectHandle>(
      new ObjectHandle(std::make_unique<facebook::jsi::Object>(std::move(object))));
  }

  facebook::jsi::Object &object()
  {
    return *object_;
  }

private:
  explicit ObjectHandle(std::unique_ptr<facebook::jsi::Object> object)
    : object_(std::move(object))
  {
  }

  std::unique_ptr<facebook::jsi::Object> object_;
};

class ArrayHandle final {
public:
  static std::unique_ptr<ArrayHandle> owned(facebook::jsi::Array array)
  {
    return std::unique_ptr<ArrayHandle>(
      new ArrayHandle(std::make_unique<facebook::jsi::Array>(std::move(array))));
  }

  facebook::jsi::Array &array()
  {
    return *array_;
  }

private:
  explicit ArrayHandle(std::unique_ptr<facebook::jsi::Array> array)
    : array_(std::move(array))
  {
  }

  std::unique_ptr<facebook::jsi::Array> array_;
};

class FunctionHandle final {
public:
  static std::unique_ptr<FunctionHandle> owned(facebook::jsi::Function function)
  {
    return std::unique_ptr<FunctionHandle>(
      new FunctionHandle(std::make_unique<facebook::jsi::Function>(std::move(function))));
  }

  facebook::jsi::Function &function()
  {
    return *function_;
  }

private:
  explicit FunctionHandle(std::unique_ptr<facebook::jsi::Function> function)
    : function_(std::move(function))
  {
  }

  std::unique_ptr<facebook::jsi::Function> function_;
};

class ArgumentsHandle final {
public:
  ArgumentsHandle(const facebook::jsi::Value *arguments, size_t count)
    : arguments_(arguments),
      count_(count)
  {
  }

  size_t count() const
  {
    return count_;
  }

  const facebook::jsi::Value &at(size_t index) const
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
  const facebook::jsi::Value *arguments_;
  size_t count_;
  std::vector<std::unique_ptr<ValueHandle>> borrowedValues_;
};

} // namespace expo::jsi

namespace {

constexpr uint32_t kApiVersion = 6;

struct StringResultBuffer {
  explicit StringResultBuffer(std::string value)
    : value(std::move(value))
  {
  }

  std::string value;
};

expo_jsi_error makeError(int32_t code, const char *message)
{
  return expo_jsi_error{
    code,
    message,
    static_cast<int32_t>(std::strlen(message)),
  };
}

expo_jsi_error makeOk()
{
  return expo_jsi_error{0, nullptr, 0};
}

expo::jsi::JsiRuntimeTaskPriority toRuntimeTaskPriority(expo_jsi_task_priority priority)
{
  switch (priority) {
  case EXPO_JSI_TASK_IMMEDIATE:
    return expo::jsi::JsiRuntimeTaskPriority::Immediate;
  case EXPO_JSI_TASK_USER_BLOCKING:
    return expo::jsi::JsiRuntimeTaskPriority::UserBlocking;
  case EXPO_JSI_TASK_LOW:
    return expo::jsi::JsiRuntimeTaskPriority::Low;
  case EXPO_JSI_TASK_IDLE:
    return expo::jsi::JsiRuntimeTaskPriority::Idle;
  case EXPO_JSI_TASK_NORMAL:
  default:
    return expo::jsi::JsiRuntimeTaskPriority::Normal;
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

expo_jsi_value_result makeBorrowedValueResult(expo_jsi_value_handle value)
{
  return expo_jsi_value_result{
    1,
    value,
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_object_result makeObjectResult(std::unique_ptr<expo::jsi::ObjectHandle> object)
{
  return expo_jsi_object_result{
    1,
    object.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_array_result makeArrayResult(std::unique_ptr<expo::jsi::ArrayHandle> array)
{
  return expo_jsi_array_result{
    1,
    array.release(),
    expo_jsi_error{0, nullptr, 0},
  };
}

expo_jsi_function_result makeFunctionResult(std::unique_ptr<expo::jsi::FunctionHandle> function)
{
  return expo_jsi_function_result{
    1,
    function.release(),
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

expo_jsi_object_result makeObjectErrorResult(int32_t code, const char *message)
{
  return expo_jsi_object_result{0, nullptr, makeError(code, message)};
}

expo_jsi_array_result makeArrayErrorResult(int32_t code, const char *message)
{
  return expo_jsi_array_result{0, nullptr, makeError(code, message)};
}

expo_jsi_function_result makeFunctionErrorResult(int32_t code, const char *message)
{
  return expo_jsi_function_result{0, nullptr, makeError(code, message)};
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

expo_jsi_value_result createNumber(expo_jsi_runtime_handle runtime, double number)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::owned(facebook::jsi::Value(number)));
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
    return makeValueResult(expo::jsi::ValueHandle::owned(facebook::jsi::Value(value != 0)));
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
    auto value = facebook::jsi::Value(
      runtimeHandle->runtime(),
      facebook::jsi::String::createFromUtf8(runtimeHandle->runtime(),
                                            std::string(text, static_cast<size_t>(length))));
    return makeValueResult(expo::jsi::ValueHandle::owned(std::move(value)));
  } catch (const std::exception &ex) {
    return makeErrorResult(43, ex.what());
  } catch (...) {
    return makeErrorResult(44, "Unknown native exception while creating string.");
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

expo_jsi_object_result getGlobalObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }

  try {
    return makeObjectResult(expo::jsi::ObjectHandle::owned(runtimeHandle->runtime().global()));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(14, ex.what());
  } catch (...) {
    return makeObjectErrorResult(15, "Unknown native exception while getting global object.");
  }
}

expo_jsi_object_result createObject(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }

  try {
    return makeObjectResult(
      expo::jsi::ObjectHandle::owned(facebook::jsi::Object(runtimeHandle->runtime())));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(16, ex.what());
  } catch (...) {
    return makeObjectErrorResult(17, "Unknown native exception while creating object.");
  }
}

expo_jsi_value_result objectAsValue(expo_jsi_runtime_handle runtime, expo_jsi_object_handle object)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (object == nullptr) {
    return makeErrorResult(18, "Object handle is null.");
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::owned(
      facebook::jsi::Value(runtimeHandle->runtime(), object->object())));
  } catch (const std::exception &ex) {
    return makeErrorResult(19, ex.what());
  } catch (...) {
    return makeErrorResult(20, "Unknown native exception while converting object to value.");
  }
}

expo_jsi_object_result valueAsObject(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }
  if (value == nullptr) {
    return makeObjectErrorResult(38, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    if (!value->value().isObject()) {
      return makeObjectErrorResult(39, "Value is not an object.");
    }
    return makeObjectResult(expo::jsi::ObjectHandle::owned(value->value().asObject(jsRuntime)));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(40, ex.what());
  } catch (...) {
    return makeObjectErrorResult(41, "Unknown native exception while converting value to object.");
  }
}

expo_jsi_array_result createArray(expo_jsi_runtime_handle runtime, uint32_t length)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_array_result{0, nullptr, error};
  }

  try {
    return makeArrayResult(
      expo::jsi::ArrayHandle::owned(facebook::jsi::Array(runtimeHandle->runtime(), length)));
  } catch (const std::exception &ex) {
    return makeArrayErrorResult(63, ex.what());
  } catch (...) {
    return makeArrayErrorResult(64, "Unknown native exception while creating array.");
  }
}

expo_jsi_value_result arrayAsValue(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (array == nullptr) {
    return makeErrorResult(65, "Array handle is null.");
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::owned(
      facebook::jsi::Value(runtimeHandle->runtime(), array->array())));
  } catch (const std::exception &ex) {
    return makeErrorResult(66, ex.what());
  } catch (...) {
    return makeErrorResult(67, "Unknown native exception while converting array to value.");
  }
}

expo_jsi_object_result arrayAsObject(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_object_result{0, nullptr, error};
  }
  if (array == nullptr) {
    return makeObjectErrorResult(68, "Array handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto arrayValue = facebook::jsi::Value(jsRuntime, array->array());
    return makeObjectResult(expo::jsi::ObjectHandle::owned(arrayValue.asObject(jsRuntime)));
  } catch (const std::exception &ex) {
    return makeObjectErrorResult(69, ex.what());
  } catch (...) {
    return makeObjectErrorResult(70, "Unknown native exception while converting array to object.");
  }
}

expo_jsi_array_result valueAsArray(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_array_result{0, nullptr, error};
  }
  if (value == nullptr) {
    return makeArrayErrorResult(71, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    if (!value->value().isObject()) {
      return makeArrayErrorResult(72, "Value is not an array.");
    }
    auto object = value->value().asObject(jsRuntime);
    if (!object.isArray(jsRuntime)) {
      return makeArrayErrorResult(73, "Value is not an array.");
    }
    return makeArrayResult(expo::jsi::ArrayHandle::owned(object.asArray(jsRuntime)));
  } catch (const std::exception &ex) {
    return makeArrayErrorResult(74, ex.what());
  } catch (...) {
    return makeArrayErrorResult(75, "Unknown native exception while converting value to array.");
  }
}

uint32_t arrayGetLength(expo_jsi_runtime_handle runtime,
                        expo_jsi_array_handle array,
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
    clearError(error);
    return static_cast<uint32_t>(array->array().length(runtimeHandle->runtime()));
  } catch (const std::exception &ex) {
    writeError(error, 77, ex.what());
    return 0;
  } catch (...) {
    writeError(error, 78, "Unknown native exception while reading array length.");
    return 0;
  }
}

expo_jsi_value_result arrayGetValueAtIndex(expo_jsi_runtime_handle runtime,
                                           expo_jsi_array_handle array,
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
    return makeValueResult(expo::jsi::ValueHandle::owned(
      array->array().getValueAtIndex(runtimeHandle->runtime(), index)));
  } catch (const std::exception &ex) {
    return makeErrorResult(80, ex.what());
  } catch (...) {
    return makeErrorResult(81, "Unknown native exception while reading array value.");
  }
}

expo_jsi_error arraySetValueAtIndex(expo_jsi_runtime_handle runtime,
                                    expo_jsi_array_handle array,
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
    array->array().setValueAtIndex(runtimeHandle->runtime(), index, value->value());
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(85, ex.what());
  } catch (...) {
    return makeError(86, "Unknown native exception while setting array value.");
  }
}

expo_jsi_error objectSetProperty(expo_jsi_runtime_handle runtime,
                                 expo_jsi_object_handle object,
                                 const char *name,
                                 int32_t name_len,
                                 expo_jsi_value_handle value)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(21, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeError(22, "Object handle is null.");
  }
  if (name == nullptr || name_len < 0) {
    return makeError(23, "Property name is invalid.");
  }
  if (value == nullptr) {
    return makeError(24, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto propertyName = facebook::jsi::PropNameID::forUtf8(
      jsRuntime, reinterpret_cast<const uint8_t *>(name), static_cast<size_t>(name_len));
    object->object().setProperty(jsRuntime, propertyName, value->value());
    return expo_jsi_error{0, nullptr, 0};
  } catch (const std::exception &ex) {
    return makeError(25, ex.what());
  } catch (...) {
    return makeError(26, "Unknown native exception while setting object property.");
  }
}

expo_jsi_value_result objectGetProperty(expo_jsi_runtime_handle runtime,
                                        expo_jsi_object_handle object,
                                        const char *name,
                                        int32_t name_len)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeErrorResult(49, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeErrorResult(50, "Object handle is null.");
  }
  if (name == nullptr || name_len < 0) {
    return makeErrorResult(51, "Property name is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto propertyName = facebook::jsi::PropNameID::forUtf8(
      jsRuntime, reinterpret_cast<const uint8_t *>(name), static_cast<size_t>(name_len));
    return makeValueResult(
      expo::jsi::ValueHandle::owned(object->object().getProperty(jsRuntime, propertyName)));
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

expo_jsi_function_result createHostFunction(
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
    return expo_jsi_function_result{0, nullptr, error};
  }
  if (name == nullptr || name_len < 0) {
    return makeFunctionErrorResult(28, "Host function name is invalid.");
  }

  try {
    auto functionName = std::string(name, static_cast<size_t>(name_len));
    auto context = std::make_shared<HostFunctionContext>(
      callback, callback_context, release_callback_context, runtime);

    auto function = facebook::jsi::Function::createFromHostFunction(
      runtimeHandle->runtime(),
      facebook::jsi::PropNameID::forUtf8(runtimeHandle->runtime(), functionName),
      parameter_count,
      [context](facebook::jsi::Runtime &jsRuntime,
                const facebook::jsi::Value &thisValue,
                const facebook::jsi::Value *arguments,
                size_t count) -> facebook::jsi::Value {
        auto thisHandle = expo::jsi::ValueHandle::borrowed(thisValue);
        auto argumentsHandle = expo::jsi::ArgumentsHandle(arguments, count);
        expo_jsi_value_result result{};
        try {
          result = context->call(thisHandle.get(), &argumentsHandle);
          if (result.ok == 0 || result.value == nullptr) {
            const char *message = result.error.message != nullptr ? result.error.message
                                                                  : "Managed host function failed.";
            throw facebook::jsi::JSError(jsRuntime, message);
          }
          auto jsResult = facebook::jsi::Value(jsRuntime, result.value->value());
          delete result.value;
          return jsResult;
        } catch (const facebook::jsi::JSError &) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw;
        } catch (const std::exception &ex) {
          if (result.value != nullptr) {
            delete result.value;
          }
          throw facebook::jsi::JSError(jsRuntime, ex.what());
        }
      });

    return makeFunctionResult(expo::jsi::FunctionHandle::owned(std::move(function)));
  } catch (const std::exception &ex) {
    return makeFunctionErrorResult(29, ex.what());
  } catch (...) {
    return makeFunctionErrorResult(30, "Unknown native exception while creating host function.");
  }
}

expo_jsi_value_result functionAsValue(expo_jsi_runtime_handle runtime,
                                      expo_jsi_function_handle function)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (function == nullptr) {
    return makeErrorResult(31, "Function handle is null.");
  }

  try {
    return makeValueResult(expo::jsi::ValueHandle::owned(
      facebook::jsi::Value(runtimeHandle->runtime(), function->function())));
  } catch (const std::exception &ex) {
    return makeErrorResult(32, ex.what());
  } catch (...) {
    return makeErrorResult(33, "Unknown native exception while converting function to value.");
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

void releaseObject(expo_jsi_runtime_handle, expo_jsi_object_handle object)
{
  delete object;
}

void releaseArray(expo_jsi_runtime_handle, expo_jsi_array_handle array)
{
  delete array;
}

void releaseFunction(expo_jsi_runtime_handle, expo_jsi_function_handle function)
{
  delete function;
}

expo_jsi_error scheduleTask(expo_jsi_runtime_handle runtime,
                            expo_jsi_task_priority priority,
                            expo_jsi_task_callback_fn callback,
                            void *taskContext,
                            expo_jsi_release_task_context_fn releaseTaskContext)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }
  if (callback == nullptr) {
    return makeError(54, "Task callback is null.");
  }

  try {
    auto task = std::make_shared<ScheduledTaskContext>(callback, taskContext, releaseTaskContext);
    runtimeHandle->runtimeExecutor().executeAsync(
      toRuntimeTaskPriority(priority), [task](facebook::jsi::Runtime &) { task->invoke(); });
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
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
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
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }
  if (callback == nullptr) {
    return makeError(57, "Task callback is null.");
  }
  if (!runtimeHandle->runtimeExecutor().canExecuteSync()) {
    return makeError(58, "Synchronous runtime execution is not supported.");
  }

  try {
    auto task = std::make_shared<ScheduledTaskContext>(callback, taskContext, releaseTaskContext);
    runtimeHandle->runtimeExecutor().executeSync(
      [task](facebook::jsi::Runtime &) { task->invoke(); });
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(59, ex.what());
  } catch (...) {
    return makeError(60, "Unknown native exception while executing runtime task.");
  }
}

expo_jsi_error drainTasks(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }

  try {
    runtimeHandle->runtimeExecutor().drain();
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(61, ex.what());
  } catch (...) {
    return makeError(62, "Unknown native exception while draining runtime tasks.");
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
  objectAsValue,
  valueAsObject,
  createArray,
  arrayAsValue,
  arrayAsObject,
  valueAsArray,
  arrayGetLength,
  arrayGetValueAtIndex,
  arraySetValueAtIndex,
  objectSetProperty,
  objectGetProperty,
  createHostFunction,
  functionAsValue,
  getArgumentsCount,
  getArgumentValue,
  releaseObject,
  releaseArray,
  releaseFunction,
  releaseValue,
  createString,
  getString,
  scheduleTask,
  canExecuteSync,
  executeSync,
  drainTasks,
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

expo_jsi_value_handle createOwnedValueHandle(facebook::jsi::Value value)
{
  return ValueHandle::owned(std::move(value)).release();
}

const expo_jsi_api *api()
{
  return &kApi;
}

} // namespace expo::jsi
