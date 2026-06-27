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

constexpr uint32_t kApiVersion = 2;

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

expo_jsi_function_result makeFunctionErrorResult(int32_t code, const char *message)
{
  return expo_jsi_function_result{0, nullptr, makeError(code, message)};
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
    auto propertyName = std::string(name, static_cast<size_t>(name_len));
    object->object().setProperty(runtimeHandle->runtime(), propertyName.c_str(), value->value());
    return expo_jsi_error{0, nullptr, 0};
  } catch (const std::exception &ex) {
    return makeError(25, ex.what());
  } catch (...) {
    return makeError(26, "Unknown native exception while setting object property.");
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

void releaseFunction(expo_jsi_runtime_handle, expo_jsi_function_handle function)
{
  delete function;
}

const expo_jsi_api kApi{
  sizeof(expo_jsi_api), kApiVersion,     createNumber,      createBool,
  getValueKind,         getBool,         getDouble,         getGlobalObject,
  createObject,         objectAsValue,   valueAsObject,     objectSetProperty,
  createHostFunction,   functionAsValue, getArgumentsCount, getArgumentValue,
  releaseObject,        releaseFunction, releaseValue,
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

const expo_jsi_api *api()
{
  return &kApi;
}

} // namespace expo::jsi
