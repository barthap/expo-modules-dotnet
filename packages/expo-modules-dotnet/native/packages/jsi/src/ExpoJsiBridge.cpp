#include "ExpoJsiBridge.h"

#include <algorithm>
#include <bit>
#include <cstring>
#include <exception>
#include <functional>
#include <list>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
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

constexpr uint32_t kApiVersion = 21;

struct ErrorResultBuffer {
  explicit ErrorResultBuffer(std::string value)
    : value(std::move(value))
  {
  }

  std::string value;
};

struct StringResultBuffer {
  explicit StringResultBuffer(std::string value)
    : value(std::move(value))
  {
  }

  std::string value;
};

struct PropertyNamesResultBuffer {
  // Property names point into these owned strings until C# copies them.
  std::vector<std::string> strings;
  std::vector<expo_jsi_property_name> names;
};

struct ManagedNativeStateEntry {
  expo_jsi_native_state_token token{};
  void *releaseContext = nullptr;
  expo_jsi_release_native_state_fn release = nullptr;
  bool released = true;

  ManagedNativeStateEntry() = default;

  ManagedNativeStateEntry(expo_jsi_native_state_token token,
                          void *releaseContext,
                          expo_jsi_release_native_state_fn release)
    : token(token),
      releaseContext(releaseContext),
      release(release)
  {
  }

  ManagedNativeStateEntry(const ManagedNativeStateEntry &) = delete;
  ManagedNativeStateEntry &operator=(const ManagedNativeStateEntry &) = delete;

  ManagedNativeStateEntry(ManagedNativeStateEntry &&other) noexcept
    : token(other.token),
      releaseContext(other.releaseContext),
      release(other.release),
      released(other.released)
  {
    other.releaseContext = nullptr;
    other.release = nullptr;
    other.released = true;
  }

  ManagedNativeStateEntry &operator=(ManagedNativeStateEntry &&other) noexcept
  {
    if (this != &other) {
      releaseOnce();
      token = other.token;
      releaseContext = other.releaseContext;
      release = other.release;
      released = other.released;
      other.releaseContext = nullptr;
      other.release = nullptr;
      other.released = true;
    }
    return *this;
  }

  ~ManagedNativeStateEntry()
  {
    releaseOnce();
  }

  void armRelease() noexcept
  {
    released = false;
  }

  void releaseOnce() noexcept
  {
    if (released) {
      return;
    }
    released = true;
    if (release != nullptr) {
      try {
        release(releaseContext, token.type_id, token.registry_id, token.generation);
      } catch (...) {
      }
    }
  }
};

class ManagedNativeStateBag final : public jsi::NativeState {
public:
  ~ManagedNativeStateBag() override
  {
    clear();
  }

  void set(ManagedNativeStateEntry entry)
  {
    auto typeId = entry.token.type_id;
    auto existing = entries_.find(typeId);
    if (existing != entries_.end()) {
      existing->second = std::move(entry);
      existing->second.armRelease();
      return;
    }
    auto inserted = entries_.emplace(typeId, std::move(entry));
    inserted.first->second.armRelease();
  }

  const ManagedNativeStateEntry *get(uint64_t typeId) const
  {
    auto existing = entries_.find(typeId);
    return existing == entries_.end() ? nullptr : &existing->second;
  }

  void clear(uint64_t typeId)
  {
    entries_.erase(typeId);
  }

  void clear()
  {
    entries_.clear();
  }

private:
  std::unordered_map<uint64_t, ManagedNativeStateEntry> entries_;
};

expo_jsi_error makeError(int32_t code, const char *message)
{
  auto *buffer = new ErrorResultBuffer(message == nullptr ? "Unknown native exception." : message);
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
    *error = makeOk();
  }
}

jsi::Object createObjectWithPrototype(jsi::Runtime &runtime, jsi::Object &prototype)
{
  auto objectClass = runtime.global().getPropertyAsObject(runtime, "Object");
  auto create = objectClass.getPropertyAsFunction(runtime, "create");
  return create.callWithThis(runtime, objectClass, {jsi::Value(runtime, prototype)})
    .asObject(runtime);
}

bool isValidAsciiIdentifier(std::string_view value)
{
  if (value.empty()) {
    return false;
  }

  auto isStart = [](char ch) {
    return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '_' || ch == '$';
  };
  auto isPart = [isStart](char ch) { return isStart(ch) || (ch >= '0' && ch <= '9'); };

  if (!isStart(value.front())) {
    return false;
  }
  return std::all_of(value.begin() + 1, value.end(), isPart);
}

jsi::Function createClass(jsi::Runtime &runtime, std::string_view name)
{
  if (!isValidAsciiIdentifier(name)) {
    throw std::invalid_argument("Class name must be a non-empty ASCII JavaScript identifier.");
  }

  std::string source = std::string("(function ") + std::string(name) + "(...args) {})";
  auto sourceBuffer = std::make_shared<jsi::StringBuffer>(source);
  return runtime.evaluateJavaScript(sourceBuffer, "expo-jsi-create-class.js")
    .asObject(runtime)
    .asFunction(runtime);
}

jsi::Function createClassWithSuperclass(jsi::Runtime &runtime,
                                        std::string_view name,
                                        jsi::Function &superclass)
{
  auto superclassPrototype = superclass.getPropertyAsObject(runtime, "prototype");
  auto klass = createClass(runtime, name);
  auto prototype = klass.getPropertyAsObject(runtime, "prototype");

  auto objectClass = runtime.global().getPropertyAsObject(runtime, "Object");
  auto setPrototypeOf = objectClass.getPropertyAsFunction(runtime, "setPrototypeOf");
  setPrototypeOf.callWithThis(
    runtime,
    objectClass,
    {jsi::Value(runtime, prototype), jsi::Value(runtime, superclassPrototype)});
  setPrototypeOf.callWithThis(
    runtime, objectClass, {jsi::Value(runtime, klass), jsi::Value(runtime, superclass)});
  return klass;
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
    makeOk(),
  };
}

expo_jsi_value_result makeBorrowedValueResult(expo_jsi_value_handle value)
{
  return expo_jsi_value_result{
    1,
    value,
    makeOk(),
  };
}

expo_jsi_promise_result makePromiseResult(std::unique_ptr<expo::dotnet::PromiseHandle> promise)
{
  return expo_jsi_promise_result{
    1,
    promise.release(),
    makeOk(),
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
    makeOk(),
  };
}

expo_jsi_string_result makeStringErrorResult(int32_t code, const char *message)
{
  return expo_jsi_string_result{0, nullptr, 0, nullptr, nullptr, makeError(code, message)};
}

expo_jsi_property_names_result makePropertyNamesResult(
  std::unique_ptr<PropertyNamesResultBuffer> buffer)
{
  auto *releaseContext = buffer.release();
  return expo_jsi_property_names_result{
    1,
    releaseContext->names.data(),
    static_cast<int32_t>(releaseContext->names.size()),
    releaseContext,
    [](void *release_context) { delete static_cast<PropertyNamesResultBuffer *>(release_context); },
    makeOk(),
  };
}

expo_jsi_property_names_result makePropertyNamesErrorResult(int32_t code, const char *message)
{
  return expo_jsi_property_names_result{0, nullptr, 0, nullptr, nullptr, makeError(code, message)};
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

std::vector<jsi::Value> copyCallArguments(jsi::Runtime &runtime,
                                          const expo_jsi_value_handle *arguments,
                                          uint32_t argumentCount)
{
  if (argumentCount == 0) {
    return {};
  }
  if (arguments == nullptr) {
    throw std::invalid_argument("Function call arguments are null.");
  }

  std::vector<jsi::Value> copied;
  copied.reserve(argumentCount);
  for (uint32_t index = 0; index < argumentCount; index++) {
    auto *argument = arguments[index];
    if (argument == nullptr) {
      throw std::invalid_argument("Function call argument handle is null.");
    }
    copied.emplace_back(runtime, argument->value());
  }
  return copied;
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

expo_jsi_value_result createPrimitiveValue(expo_jsi_runtime_handle runtime,
                                           expo_jsi_value_kind kind,
                                           uint64_t value)
{
  expo_jsi_error error{};
  if (tryRuntimeHandle(runtime, &error) == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    switch (kind) {
    case EXPO_JSI_VALUE_UNDEFINED:
      return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value::undefined()));
    case EXPO_JSI_VALUE_NULL:
      return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value::null()));
    case EXPO_JSI_VALUE_BOOL:
      return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(value != 0)));
    case EXPO_JSI_VALUE_NUMBER: {
      static_assert(sizeof(double) == sizeof(uint64_t));
      double number = std::bit_cast<double>(value);
      return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(number)));
    }
    default:
      return makeErrorResult(116, "Unsupported primitive value kind.");
    }
  } catch (const std::exception &ex) {
    return makeErrorResult(117, ex.what());
  } catch (...) {
    return makeErrorResult(118, "Unknown native exception while creating primitive value.");
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
    static constexpr uint8_t emptyStringData = 0;
    const uint8_t *utf8 = length == 0 ? &emptyStringData : data;
    auto value = jsi::Value(
      runtimeHandle->runtime(),
      jsi::String::createFromUtf8(runtimeHandle->runtime(), utf8, static_cast<size_t>(length)));
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

expo_jsi_value_result createObjectWithPrototypeValue(expo_jsi_runtime_handle runtime,
                                                     expo_jsi_value_handle prototype)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (prototype == nullptr) {
    return makeErrorResult(119, "Prototype value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsPrototype = checkedObject(jsRuntime, prototype);
    auto object = createObjectWithPrototype(jsRuntime, jsPrototype);
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, object)));
  } catch (const std::exception &ex) {
    return makeErrorResult(120, ex.what());
  } catch (...) {
    return makeErrorResult(121, "Unknown native exception while creating object with prototype.");
  }
}

expo_jsi_value_result createClassValue(expo_jsi_runtime_handle runtime,
                                       const char *name,
                                       int32_t name_len)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (name == nullptr || name_len < 0) {
    return makeErrorResult(122, "Class name is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto className = std::string(name, static_cast<size_t>(name_len));
    auto klass = createClass(jsRuntime, className);
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, klass)));
  } catch (const std::exception &ex) {
    return makeErrorResult(123, ex.what());
  } catch (...) {
    return makeErrorResult(124, "Unknown native exception while creating class.");
  }
}

expo_jsi_value_result createClassWithSuperclassValue(expo_jsi_runtime_handle runtime,
                                                     const char *name,
                                                     int32_t name_len,
                                                     expo_jsi_value_handle superclass)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (name == nullptr || name_len < 0) {
    return makeErrorResult(125, "Class name is invalid.");
  }
  if (superclass == nullptr) {
    return makeErrorResult(126, "Superclass value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto className = std::string(name, static_cast<size_t>(name_len));
    auto jsSuperclass = checkedFunction(jsRuntime, superclass);
    auto klass = createClassWithSuperclass(jsRuntime, className, jsSuperclass);
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, klass)));
  } catch (const std::exception &ex) {
    return makeErrorResult(127, ex.what());
  } catch (...) {
    return makeErrorResult(128, "Unknown native exception while creating class with superclass.");
  }
}

uint8_t strictEquals(expo_jsi_runtime_handle runtime,
                     expo_jsi_value_handle left,
                     expo_jsi_value_handle right,
                     expo_jsi_error *error)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, error);
  if (runtimeHandle == nullptr) {
    return 0;
  }
  if (left == nullptr || right == nullptr) {
    writeError(error, 129, "Value handle is null.");
    return 0;
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    const auto &leftValue = left->value();
    const auto &rightValue = right->value();
    clearError(error);
    return jsi::Value::strictEquals(jsRuntime, leftValue, rightValue) ? 1 : 0;
  } catch (const std::exception &ex) {
    writeError(error, 130, ex.what());
    return 0;
  } catch (...) {
    writeError(error, 131, "Unknown native exception while comparing values.");
    return 0;
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
  if (!isValidUtf8(reinterpret_cast<const uint8_t *>(name), name_len)) {
    return makeError(139, "Property name is not valid UTF-8.");
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
    return makeOk();
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
  if (!isValidUtf8(reinterpret_cast<const uint8_t *>(name), name_len)) {
    return makeErrorResult(140, "Property name is not valid UTF-8.");
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

expo_jsi_property_names_result objectGetOwnPropertyNames(expo_jsi_runtime_handle runtime,
                                                         expo_jsi_value_handle object)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makePropertyNamesErrorResult(97, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makePropertyNamesErrorResult(98, "Value handle is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsObject = checkedObject(jsRuntime, object);
    auto objectConstructor = jsRuntime.global().getPropertyAsObject(jsRuntime, "Object");
    auto getOwnPropertyNames =
      objectConstructor.getPropertyAsFunction(jsRuntime, "getOwnPropertyNames");
    auto propertyNamesValue = getOwnPropertyNames.call(jsRuntime, jsi::Value(jsRuntime, jsObject));
    auto propertyNames = propertyNamesValue.asObject(jsRuntime).asArray(jsRuntime);
    auto length = propertyNames.size(jsRuntime);
    auto buffer = std::make_unique<PropertyNamesResultBuffer>();
    buffer->strings.reserve(length);
    buffer->names.reserve(length);

    for (size_t index = 0; index < length; index++) {
      auto nameValue = propertyNames.getValueAtIndex(jsRuntime, index);
      buffer->strings.push_back(nameValue.asString(jsRuntime).utf8(jsRuntime));
    }

    for (const auto &name : buffer->strings) {
      buffer->names.push_back(expo_jsi_property_name{
        reinterpret_cast<const uint8_t *>(name.data()),
        static_cast<int32_t>(name.size()),
      });
    }

    return makePropertyNamesResult(std::move(buffer));
  } catch (const std::exception &ex) {
    return makePropertyNamesErrorResult(99, ex.what());
  } catch (...) {
    return makePropertyNamesErrorResult(
      100, "Unknown native exception while getting object property names.");
  }
}

expo_jsi_native_state_result makeNativeStateResult(expo_jsi_native_state_token token)
{
  return expo_jsi_native_state_result{1, 1, token, makeOk()};
}

expo_jsi_native_state_result makeNativeStateNotFoundResult()
{
  return expo_jsi_native_state_result{1, 0, expo_jsi_native_state_token{0, 0, 0}, makeOk()};
}

expo_jsi_native_state_result makeNativeStateErrorResult(int32_t code, const char *message)
{
  return expo_jsi_native_state_result{
    0,
    0,
    expo_jsi_native_state_token{0, 0, 0},
    makeError(code, message),
  };
}

std::shared_ptr<ManagedNativeStateBag> getNativeStateBag(jsi::Runtime &runtime,
                                                         jsi::Object &object,
                                                         bool create)
{
  if (object.hasNativeState<ManagedNativeStateBag>(runtime)) {
    return object.getNativeState<ManagedNativeStateBag>(runtime);
  }
  if (object.hasNativeState<jsi::NativeState>(runtime)) {
    throw std::runtime_error("JavaScript object has incompatible native state.");
  }
  if (!create) {
    return nullptr;
  }

  auto bag = std::make_shared<ManagedNativeStateBag>();
  object.setNativeState(runtime, bag);
  return bag;
}

expo_jsi_error objectSetNativeState(expo_jsi_runtime_handle runtime,
                                    expo_jsi_value_handle object,
                                    expo_jsi_native_state_token token,
                                    void *releaseContext,
                                    expo_jsi_release_native_state_fn release)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(122, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeError(123, "Value handle is null.");
  }
  if (token.type_id == 0 || token.registry_id == 0 || token.generation == 0) {
    return makeError(124, "NativeState token is invalid.");
  }
  if (release == nullptr) {
    return makeError(125, "NativeState release callback is null.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsObject = checkedObject(jsRuntime, object);
    auto bag = getNativeStateBag(jsRuntime, jsObject, true);
    bag->set(ManagedNativeStateEntry(token, releaseContext, release));
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(126, ex.what());
  } catch (...) {
    return makeError(127, "Unknown native exception while setting object native state.");
  }
}

expo_jsi_native_state_result objectGetNativeState(expo_jsi_runtime_handle runtime,
                                                  expo_jsi_value_handle object,
                                                  uint64_t typeId)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeNativeStateErrorResult(128, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeNativeStateErrorResult(129, "Value handle is null.");
  }
  if (typeId == 0) {
    return makeNativeStateErrorResult(130, "NativeState type id is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsObject = checkedObject(jsRuntime, object);
    auto bag = getNativeStateBag(jsRuntime, jsObject, false);
    if (bag == nullptr) {
      return makeNativeStateNotFoundResult();
    }

    auto *entry = bag->get(typeId);
    return entry == nullptr ? makeNativeStateNotFoundResult() : makeNativeStateResult(entry->token);
  } catch (const std::exception &ex) {
    return makeNativeStateErrorResult(131, ex.what());
  } catch (...) {
    return makeNativeStateErrorResult(
      132, "Unknown native exception while getting object native state.");
  }
}

expo_jsi_error objectClearNativeState(expo_jsi_runtime_handle runtime,
                                      expo_jsi_value_handle object,
                                      uint64_t typeId)
{
  auto *runtimeHandle = tryRuntimeHandle(runtime, nullptr);
  if (runtimeHandle == nullptr) {
    return makeError(133, "Runtime handle is invalid.");
  }
  if (object == nullptr) {
    return makeError(134, "Value handle is null.");
  }
  if (typeId == 0) {
    return makeError(135, "NativeState type id is invalid.");
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsObject = checkedObject(jsRuntime, object);
    auto bag = getNativeStateBag(jsRuntime, jsObject, false);
    if (bag != nullptr) {
      bag->clear(typeId);
    }
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(136, ex.what());
  } catch (...) {
    return makeError(137, "Unknown native exception while clearing object native state.");
  }
}

expo_jsi_value_result callFunction(expo_jsi_runtime_handle runtime,
                                   expo_jsi_value_handle function,
                                   const expo_jsi_value_handle *arguments,
                                   uint32_t argumentCount)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsFunction = checkedFunction(jsRuntime, function);
    auto copiedArguments = copyCallArguments(jsRuntime, arguments, argumentCount);
    const jsi::Value *argumentData = copiedArguments.empty() ? nullptr : copiedArguments.data();
    auto result = jsFunction.call(jsRuntime, argumentData, copiedArguments.size());
    return makeValueResult(expo::dotnet::ValueHandle::owned(std::move(result)));
  } catch (const std::exception &ex) {
    return makeErrorResult(105, ex.what());
  } catch (...) {
    return makeErrorResult(106, "Unknown native exception while calling function.");
  }
}

expo_jsi_value_result callFunctionWithThis(expo_jsi_runtime_handle runtime,
                                           expo_jsi_value_handle function,
                                           expo_jsi_value_handle thisObject,
                                           const expo_jsi_value_handle *arguments,
                                           uint32_t argumentCount)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsFunction = checkedFunction(jsRuntime, function);
    auto receiver = checkedObject(jsRuntime, thisObject);
    auto copiedArguments = copyCallArguments(jsRuntime, arguments, argumentCount);
    const jsi::Value *argumentData = copiedArguments.empty() ? nullptr : copiedArguments.data();
    auto result =
      jsFunction.callWithThis(jsRuntime, receiver, argumentData, copiedArguments.size());
    return makeValueResult(expo::dotnet::ValueHandle::owned(std::move(result)));
  } catch (const std::exception &ex) {
    return makeErrorResult(107, ex.what());
  } catch (...) {
    return makeErrorResult(108, "Unknown native exception while calling function with this.");
  }
}

expo_jsi_value_result callFunctionAsConstructor(expo_jsi_runtime_handle runtime,
                                                expo_jsi_value_handle function,
                                                const expo_jsi_value_handle *arguments,
                                                uint32_t argumentCount)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }

  try {
    auto &jsRuntime = runtimeHandle->runtime();
    auto jsFunction = checkedFunction(jsRuntime, function);
    auto copiedArguments = copyCallArguments(jsRuntime, arguments, argumentCount);
    const jsi::Value *argumentData = copiedArguments.empty() ? nullptr : copiedArguments.data();
    auto result = jsFunction.callAsConstructor(jsRuntime, argumentData, copiedArguments.size());
    return makeValueResult(expo::dotnet::ValueHandle::owned(std::move(result)));
  } catch (const std::exception &ex) {
    return makeErrorResult(109, ex.what());
  } catch (...) {
    return makeErrorResult(110, "Unknown native exception while calling function as constructor.");
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

std::string copyAndReleaseErrorMessage(expo_jsi_error error, const char *fallback)
{
  std::string message;
  if (error.message != nullptr && error.message_len > 0) {
    message.assign(error.message, static_cast<size_t>(error.message_len));
  }
  if (error.release != nullptr) {
    error.release(error.release_context);
  }
  return message.empty() ? std::string(fallback) : message;
}

class ManagedHostObject final : public jsi::HostObject {
public:
  ManagedHostObject(expo_jsi_runtime_handle runtime,
                    expo_jsi_host_object_get_fn get,
                    expo_jsi_host_object_set_fn set,
                    expo_jsi_host_object_get_property_names_fn getPropertyNames,
                    void *callbackContext,
                    expo_jsi_release_callback_context_fn releaseContext)
    : runtime_(runtime),
      get_(get),
      set_(set),
      getPropertyNames_(getPropertyNames),
      callbackContext_(callbackContext),
      releaseContext_(releaseContext)
  {
  }

  ~ManagedHostObject() override
  {
    if (releaseContext_ != nullptr && callbackContext_ != nullptr) {
      releaseContext_(callbackContext_);
    }
  }

  jsi::Value get(jsi::Runtime &runtime, const jsi::PropNameID &name) override
  {
    auto propertyName = name.utf8(runtime);
    auto result = get_(
      callbackContext_, runtime_, propertyName.data(), static_cast<int32_t>(propertyName.size()));
    if (result.ok == 0 || result.value == nullptr) {
      delete result.value;
      throw jsi::JSError(
        runtime, copyAndReleaseErrorMessage(result.error, "Managed host object getter failed."));
    }

    try {
      auto value = jsi::Value(runtime, result.value->value());
      delete result.value;
      return value;
    } catch (...) {
      delete result.value;
      throw;
    }
  }

  void set(jsi::Runtime &runtime, const jsi::PropNameID &name, const jsi::Value &value) override
  {
    auto propertyName = name.utf8(runtime);
    if (set_ == nullptr) {
      throw jsi::JSError(runtime,
                         "Cannot set property '" + propertyName + "' on a read-only host object.");
    }

    auto valueHandle = expo::dotnet::ValueHandle::borrowed(value);
    auto error = set_(callbackContext_,
                      runtime_,
                      propertyName.data(),
                      static_cast<int32_t>(propertyName.size()),
                      valueHandle.get());
    if (error.code != 0) {
      throw jsi::JSError(runtime,
                         copyAndReleaseErrorMessage(error, "Managed host object setter failed."));
    }
  }

  std::vector<jsi::PropNameID> getPropertyNames(jsi::Runtime &runtime) override
  {
    if (getPropertyNames_ == nullptr) {
      return {};
    }

    auto result = getPropertyNames_(callbackContext_, runtime_);
    if (result.ok == 0) {
      throw jsi::JSError(
        runtime,
        copyAndReleaseErrorMessage(result.error, "Managed host object property names failed."));
    }

    struct ResultReleaseGuard {
      expo_jsi_property_names_result *result;

      ~ResultReleaseGuard()
      {
        if (result != nullptr && result->release != nullptr) {
          result->release(result->release_context);
        }
      }

      void release()
      {
        if (result != nullptr && result->release != nullptr) {
          result->release(result->release_context);
          result = nullptr;
        }
      }
    } releaseGuard{&result};

    if (result.count < 0) {
      throw jsi::JSError(runtime, "Managed host object returned a negative property count.");
    }
    if (result.count > 0 && result.names == nullptr) {
      throw jsi::JSError(runtime, "Managed host object returned null property names.");
    }

    std::vector<jsi::PropNameID> names;
    names.reserve(static_cast<size_t>(result.count));
    for (int32_t index = 0; index < result.count; index++) {
      auto propertyName = result.names[index];
      if (propertyName.length < 0 || (propertyName.length > 0 && propertyName.data == nullptr)) {
        throw jsi::JSError(runtime, "Managed host object returned an invalid property name.");
      }
      if (!isValidUtf8(propertyName.data, propertyName.length)) {
        throw jsi::JSError(runtime,
                           "Managed host object returned a property name that is not valid UTF-8.");
      }
      names.push_back(jsi::PropNameID::forUtf8(
        runtime, propertyName.data, static_cast<size_t>(propertyName.length)));
    }

    releaseGuard.release();
    return names;
  }

private:
  expo_jsi_runtime_handle runtime_;
  expo_jsi_host_object_get_fn get_;
  expo_jsi_host_object_set_fn set_;
  expo_jsi_host_object_get_property_names_fn getPropertyNames_;
  void *callbackContext_;
  expo_jsi_release_callback_context_fn releaseContext_;
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
  if (!isValidUtf8(reinterpret_cast<const uint8_t *>(name), name_len)) {
    return makeErrorResult(141, "Host function name is not valid UTF-8.");
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

expo_jsi_value_result createHostObject(expo_jsi_runtime_handle runtime,
                                       expo_jsi_host_object_get_fn get,
                                       expo_jsi_host_object_set_fn set,
                                       expo_jsi_host_object_get_property_names_fn getPropertyNames,
                                       void *callbackContext,
                                       expo_jsi_release_callback_context_fn releaseCallbackContext)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return expo_jsi_value_result{0, nullptr, error};
  }
  if (get == nullptr) {
    return makeErrorResult(136, "Host object getter callback is null.");
  }

  auto releaseOnError = true;
  try {
    auto &jsRuntime = runtimeHandle->runtime();
    std::shared_ptr<ManagedHostObject> hostObject;
    hostObject = std::make_shared<ManagedHostObject>(
      runtime, get, set, getPropertyNames, callbackContext, releaseCallbackContext);
    releaseOnError = false;
    auto object = jsi::Object::createFromHostObject(jsRuntime, std::move(hostObject));
    return makeValueResult(expo::dotnet::ValueHandle::owned(jsi::Value(jsRuntime, object)));
  } catch (const std::exception &ex) {
    if (releaseOnError && releaseCallbackContext != nullptr && callbackContext != nullptr) {
      releaseCallbackContext(callbackContext);
    }
    return makeErrorResult(137, ex.what());
  } catch (...) {
    if (releaseOnError && releaseCallbackContext != nullptr && callbackContext != nullptr) {
      releaseCallbackContext(callbackContext);
    }
    return makeErrorResult(138, "Unknown native exception while creating host object.");
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
  objectGetOwnPropertyNames,
  callFunction,
  callFunctionWithThis,
  callFunctionAsConstructor,
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
  createPrimitiveValue,
  createObjectWithPrototypeValue,
  createClassValue,
  createClassWithSuperclassValue,
  strictEquals,
  objectSetNativeState,
  objectGetNativeState,
  objectClearNativeState,
  createHostObject,
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
