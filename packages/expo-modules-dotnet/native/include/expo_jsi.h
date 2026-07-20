#pragma once

#include <stdint.h>

#ifdef __cplusplus
namespace expo::dotnet {
class RuntimeHandle;
class ValueHandle;
class PromiseHandle;
class ArgumentsHandle;
class ArrayBufferHandle;
class MutableBufferHandle;
class WeakObjectHandle;
} // namespace expo::dotnet

using expo_jsi_runtime_t = expo::dotnet::RuntimeHandle;
using expo_jsi_value_t = expo::dotnet::ValueHandle;
using expo_jsi_promise_t = expo::dotnet::PromiseHandle;
using expo_jsi_arguments_t = expo::dotnet::ArgumentsHandle;
using expo_jsi_array_buffer_t = expo::dotnet::ArrayBufferHandle;
using expo_jsi_mutable_buffer_t = expo::dotnet::MutableBufferHandle;
using expo_jsi_weak_object_t = expo::dotnet::WeakObjectHandle;

extern "C" {

typedef expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef expo_jsi_value_t *expo_jsi_value_handle;
typedef expo_jsi_promise_t *expo_jsi_promise_handle;
typedef expo_jsi_arguments_t *expo_jsi_arguments_handle;
typedef expo_jsi_array_buffer_t *expo_jsi_array_buffer_handle;
typedef expo_jsi_mutable_buffer_t *expo_jsi_mutable_buffer_handle;
typedef expo_jsi_weak_object_t *expo_jsi_weak_object_handle;
#else
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
typedef struct expo_jsi_promise_t *expo_jsi_promise_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;
typedef struct expo_jsi_array_buffer_t *expo_jsi_array_buffer_handle;
typedef struct expo_jsi_mutable_buffer_t *expo_jsi_mutable_buffer_handle;
typedef struct expo_jsi_weak_object_t *expo_jsi_weak_object_handle;
#endif

typedef enum expo_jsi_value_kind {
  EXPO_JSI_VALUE_UNDEFINED = 0,
  EXPO_JSI_VALUE_NULL = 1,
  EXPO_JSI_VALUE_BOOL = 2,
  EXPO_JSI_VALUE_NUMBER = 3,
  EXPO_JSI_VALUE_STRING = 4,
  EXPO_JSI_VALUE_OBJECT = 5,
  EXPO_JSI_VALUE_FUNCTION = 6,
  EXPO_JSI_VALUE_ARRAY_BUFFER = 7
} expo_jsi_value_kind;

typedef enum expo_jsi_task_priority {
  EXPO_JSI_TASK_IMMEDIATE = 1,
  EXPO_JSI_TASK_USER_BLOCKING = 2,
  EXPO_JSI_TASK_NORMAL = 3,
  EXPO_JSI_TASK_LOW = 4,
  EXPO_JSI_TASK_IDLE = 5
} expo_jsi_task_priority;

typedef enum expo_jsi_value_expectation {
  EXPO_JSI_EXPECT_OBJECT = 1,
  EXPO_JSI_EXPECT_ARRAY = 2,
  EXPO_JSI_EXPECT_FUNCTION = 3
} expo_jsi_value_expectation;

typedef enum expo_jsi_promise_settlement {
  EXPO_JSI_PROMISE_RESOLVE = 0,
  EXPO_JSI_PROMISE_REJECT = 1
} expo_jsi_promise_settlement;

typedef void (*expo_jsi_release_error_fn)(void *release_context);

typedef struct expo_jsi_error {
  int32_t code;
  const char *message;
  int32_t message_len;
  void *release_context;
  expo_jsi_release_error_fn release;
} expo_jsi_error;

typedef struct expo_jsi_value_result {
  int32_t ok;
  expo_jsi_value_handle value;
  expo_jsi_error error;
} expo_jsi_value_result;

typedef struct expo_jsi_promise_result {
  int32_t ok;
  expo_jsi_promise_handle promise;
  expo_jsi_error error;
} expo_jsi_promise_result;

typedef void (*expo_jsi_release_string_fn)(void *release_context);

typedef struct expo_jsi_string_result {
  int32_t ok;
  const uint8_t *data;
  int32_t length;
  void *release_context;
  expo_jsi_release_string_fn release;
  expo_jsi_error error;
} expo_jsi_string_result;

typedef void (*expo_jsi_release_property_names_fn)(void *release_context);

typedef struct expo_jsi_property_name {
  const uint8_t *data;
  int32_t length;
} expo_jsi_property_name;

typedef struct expo_jsi_property_names_result {
  int32_t ok;
  const expo_jsi_property_name *names;
  int32_t count;
  void *release_context;
  expo_jsi_release_property_names_fn release;
  expo_jsi_error error;
} expo_jsi_property_names_result;

typedef struct expo_jsi_array_buffer_result {
  int32_t ok;
  expo_jsi_array_buffer_handle array_buffer;
  int32_t byte_length;
  expo_jsi_error error;
} expo_jsi_array_buffer_result;

typedef struct expo_jsi_mutable_buffer_result {
  int32_t ok;
  int32_t found;
  expo_jsi_mutable_buffer_handle mutable_buffer;
  int32_t byte_length;
  expo_jsi_error error;
} expo_jsi_mutable_buffer_result;

typedef struct expo_jsi_byte_span_result {
  int32_t ok;
  uint8_t *data;
  int32_t length;
  expo_jsi_error error;
} expo_jsi_byte_span_result;

typedef struct expo_jsi_weak_object_result {
  int32_t ok;
  expo_jsi_weak_object_handle weak_object;
  expo_jsi_error error;
} expo_jsi_weak_object_result;

typedef struct expo_jsi_weak_object_lock_result {
  int32_t ok;
  int32_t found;
  expo_jsi_value_handle value;
  expo_jsi_error error;
} expo_jsi_weak_object_lock_result;

// NativeState release callbacks may run during JavaScript object destruction,
// entry replacement, explicit clear, or runtime teardown. They must not throw,
// block on JavaScript runtime work, or touch JSI handles.
typedef void (*expo_jsi_release_native_state_fn)(void *release_context,
                                                 uint64_t type_id,
                                                 uint64_t registry_id,
                                                 uint32_t generation);

typedef struct expo_jsi_native_state_token {
  uint64_t type_id;
  uint64_t registry_id;
  uint32_t generation;
} expo_jsi_native_state_token;

typedef struct expo_jsi_native_state_result {
  int32_t ok;
  int32_t found;
  expo_jsi_native_state_token token;
  expo_jsi_error error;
} expo_jsi_native_state_result;

typedef expo_jsi_value_result (*expo_jsi_host_function_callback_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle this_value,
  expo_jsi_arguments_handle arguments);

typedef void (*expo_jsi_release_callback_context_fn)(void *callback_context);

typedef expo_jsi_value_result (*expo_jsi_host_object_get_fn)(void *callback_context,
                                                             expo_jsi_runtime_handle runtime,
                                                             const char *name,
                                                             int32_t name_len);

typedef expo_jsi_error (*expo_jsi_host_object_set_fn)(void *callback_context,
                                                      expo_jsi_runtime_handle runtime,
                                                      const char *name,
                                                      int32_t name_len,
                                                      expo_jsi_value_handle value);

typedef expo_jsi_property_names_result (*expo_jsi_host_object_get_property_names_fn)(
  void *callback_context, expo_jsi_runtime_handle runtime);

typedef void (*expo_jsi_task_callback_fn)(void *task_context);

typedef void (*expo_jsi_release_task_context_fn)(void *task_context);

// Deprecated: use expo_jsi_create_primitive_value_fn with EXPO_JSI_VALUE_NUMBER.
typedef expo_jsi_value_result (*expo_jsi_create_number_fn)(expo_jsi_runtime_handle runtime,
                                                           double value);

// Deprecated: use expo_jsi_create_primitive_value_fn with EXPO_JSI_VALUE_BOOL.
typedef expo_jsi_value_result (*expo_jsi_create_bool_fn)(expo_jsi_runtime_handle runtime,
                                                         uint8_t value);

typedef expo_jsi_value_result (*expo_jsi_create_primitive_value_fn)(expo_jsi_runtime_handle runtime,
                                                                    expo_jsi_value_kind kind,
                                                                    uint64_t value);

typedef expo_jsi_value_result (*expo_jsi_create_string_fn)(expo_jsi_runtime_handle runtime,
                                                           const uint8_t *data,
                                                           int32_t length);

typedef expo_jsi_value_result (*expo_jsi_clone_value_fn)(expo_jsi_runtime_handle runtime,
                                                         expo_jsi_value_handle value);

typedef expo_jsi_value_result (*expo_jsi_create_error_fn)(expo_jsi_runtime_handle runtime,
                                                          const uint8_t *message,
                                                          int32_t message_len);

typedef expo_jsi_value_kind (*expo_jsi_get_value_kind_fn)(expo_jsi_runtime_handle runtime,
                                                          expo_jsi_value_handle value,
                                                          expo_jsi_error *error);

// Boolean ABI values are encoded as 0 or 1. Failures also return 0 and are
// distinguished from false by the structured error out-parameter.
typedef uint8_t (*expo_jsi_get_bool_fn)(expo_jsi_runtime_handle runtime,
                                        expo_jsi_value_handle value,
                                        expo_jsi_error *error);

typedef double (*expo_jsi_get_double_fn)(expo_jsi_runtime_handle runtime,
                                         expo_jsi_value_handle value,
                                         expo_jsi_error *error);

typedef expo_jsi_string_result (*expo_jsi_get_string_fn)(expo_jsi_runtime_handle runtime,
                                                         expo_jsi_value_handle value);

typedef expo_jsi_value_result (*expo_jsi_get_global_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_create_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_value_retain_as_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value,
  expo_jsi_value_expectation expectation);

typedef expo_jsi_value_result (*expo_jsi_create_array_fn)(expo_jsi_runtime_handle runtime,
                                                          uint32_t length);

typedef uint32_t (*expo_jsi_array_get_length_fn)(expo_jsi_runtime_handle runtime,
                                                 expo_jsi_value_handle array,
                                                 expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_array_get_value_at_index_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle array, uint32_t index);

typedef expo_jsi_error (*expo_jsi_array_set_value_at_index_fn)(expo_jsi_runtime_handle runtime,
                                                               expo_jsi_value_handle array,
                                                               uint32_t index,
                                                               expo_jsi_value_handle value);

typedef expo_jsi_promise_result (*expo_jsi_create_promise_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_promise_as_value_fn)(expo_jsi_runtime_handle runtime,
                                                              expo_jsi_promise_handle promise);

typedef expo_jsi_error (*expo_jsi_promise_settle_fn)(expo_jsi_runtime_handle runtime,
                                                     expo_jsi_promise_handle promise,
                                                     expo_jsi_promise_settlement settlement,
                                                     expo_jsi_value_handle value);

typedef expo_jsi_error (*expo_jsi_object_set_property_fn)(expo_jsi_runtime_handle runtime,
                                                          expo_jsi_value_handle object,
                                                          const char *name,
                                                          int32_t name_len,
                                                          expo_jsi_value_handle value);

typedef expo_jsi_value_result (*expo_jsi_object_get_property_fn)(expo_jsi_runtime_handle runtime,
                                                                 expo_jsi_value_handle object,
                                                                 const char *name,
                                                                 int32_t name_len);

typedef expo_jsi_property_names_result (*expo_jsi_object_get_own_property_names_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle object);

typedef expo_jsi_error (*expo_jsi_object_set_native_state_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle object,
  expo_jsi_native_state_token token,
  void *release_context,
  expo_jsi_release_native_state_fn release);

typedef expo_jsi_native_state_result (*expo_jsi_object_get_native_state_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle object, uint64_t type_id);

typedef expo_jsi_error (*expo_jsi_object_clear_native_state_fn)(expo_jsi_runtime_handle runtime,
                                                                expo_jsi_value_handle object,
                                                                uint64_t type_id);

typedef expo_jsi_value_result (*expo_jsi_function_call_fn)(expo_jsi_runtime_handle runtime,
                                                           expo_jsi_value_handle function,
                                                           const expo_jsi_value_handle *arguments,
                                                           uint32_t argument_count);

typedef expo_jsi_value_result (*expo_jsi_function_call_with_this_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle function,
  expo_jsi_value_handle this_object,
  const expo_jsi_value_handle *arguments,
  uint32_t argument_count);

typedef expo_jsi_value_result (*expo_jsi_function_call_as_constructor_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle function,
  const expo_jsi_value_handle *arguments,
  uint32_t argument_count);

typedef expo_jsi_value_result (*expo_jsi_create_host_function_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);

typedef expo_jsi_value_result (*expo_jsi_create_host_object_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_host_object_get_fn get,
  expo_jsi_host_object_set_fn set,
  expo_jsi_host_object_get_property_names_fn get_property_names,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);

typedef uint32_t (*expo_jsi_get_arguments_count_fn)(expo_jsi_runtime_handle runtime,
                                                    expo_jsi_arguments_handle arguments,
                                                    expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_get_argument_value_fn)(expo_jsi_runtime_handle runtime,
                                                                expo_jsi_arguments_handle arguments,
                                                                uint32_t index);

typedef void (*expo_jsi_release_value_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value);

typedef void (*expo_jsi_release_promise_fn)(expo_jsi_runtime_handle runtime,
                                            expo_jsi_promise_handle promise);

typedef expo_jsi_error (*expo_jsi_runtime_schedule_task_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_priority priority,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef uint8_t (*expo_jsi_runtime_can_execute_sync_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_error (*expo_jsi_runtime_execute_sync_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef uint8_t (*expo_jsi_is_promise_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value,
                                          expo_jsi_error *error);

typedef uint8_t (*expo_jsi_is_error_fn)(expo_jsi_runtime_handle runtime,
                                        expo_jsi_value_handle value,
                                        expo_jsi_error *error);

typedef expo_jsi_string_result (*expo_jsi_coerce_to_string_fn)(expo_jsi_runtime_handle runtime,
                                                               expo_jsi_value_handle value);

typedef expo_jsi_value_result (*expo_jsi_create_object_with_prototype_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle prototype);

typedef expo_jsi_value_result (*expo_jsi_create_class_fn)(expo_jsi_runtime_handle runtime,
                                                          const char *name,
                                                          int32_t name_len);

typedef expo_jsi_value_result (*expo_jsi_create_class_with_superclass_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  expo_jsi_value_handle superclass);

typedef uint8_t (*expo_jsi_strict_equals_fn)(expo_jsi_runtime_handle runtime,
                                             expo_jsi_value_handle left,
                                             expo_jsi_value_handle right,
                                             expo_jsi_error *error);

typedef expo_jsi_array_buffer_result (*expo_jsi_array_buffer_retain_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle value);

typedef expo_jsi_array_buffer_result (*expo_jsi_array_buffer_clone_handle_fn)(
  expo_jsi_array_buffer_handle array_buffer);

typedef expo_jsi_byte_span_result (*expo_jsi_array_buffer_get_bytes_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_array_buffer_handle array_buffer);

typedef expo_jsi_value_result (*expo_jsi_array_buffer_as_value_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_array_buffer_handle array_buffer);

typedef void (*expo_jsi_array_buffer_release_fn)(expo_jsi_array_buffer_handle array_buffer);

typedef expo_jsi_mutable_buffer_result (*expo_jsi_array_buffer_try_get_mutable_buffer_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle value);

typedef expo_jsi_mutable_buffer_result (*expo_jsi_mutable_buffer_allocate_fn)(int32_t length);

typedef expo_jsi_mutable_buffer_result (*expo_jsi_mutable_buffer_copy_fn)(const uint8_t *data,
                                                                          int32_t length);

typedef expo_jsi_mutable_buffer_result (*expo_jsi_mutable_buffer_clone_handle_fn)(
  expo_jsi_mutable_buffer_handle mutable_buffer);

typedef expo_jsi_byte_span_result (*expo_jsi_mutable_buffer_get_bytes_fn)(
  expo_jsi_mutable_buffer_handle mutable_buffer);

typedef expo_jsi_value_result (*expo_jsi_mutable_buffer_as_value_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_mutable_buffer_handle mutable_buffer);

typedef void (*expo_jsi_mutable_buffer_release_fn)(expo_jsi_mutable_buffer_handle mutable_buffer);

typedef expo_jsi_weak_object_result (*expo_jsi_object_create_weak_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_value_handle value);

typedef expo_jsi_weak_object_lock_result (*expo_jsi_weak_object_lock_fn)(
  expo_jsi_runtime_handle runtime, expo_jsi_weak_object_handle weak_object);

typedef void (*expo_jsi_weak_object_release_fn)(expo_jsi_weak_object_handle weak_object);

typedef struct expo_jsi_api {
  uint32_t size;
  uint32_t version;

  expo_jsi_create_number_fn create_number;
  expo_jsi_create_bool_fn create_bool;
  expo_jsi_get_value_kind_fn get_value_kind;
  expo_jsi_get_bool_fn get_bool;
  expo_jsi_get_double_fn get_double;
  expo_jsi_get_global_object_fn get_global_object;
  expo_jsi_create_object_fn create_object;
  expo_jsi_value_retain_as_fn value_retain_as;
  expo_jsi_create_array_fn create_array;
  expo_jsi_array_get_length_fn array_get_length;
  expo_jsi_array_get_value_at_index_fn array_get_value_at_index;
  expo_jsi_array_set_value_at_index_fn array_set_value_at_index;
  expo_jsi_create_promise_fn create_promise;
  expo_jsi_promise_as_value_fn promise_as_value;
  expo_jsi_promise_settle_fn promise_settle;
  expo_jsi_object_set_property_fn object_set_property;
  expo_jsi_object_get_property_fn object_get_property;
  expo_jsi_object_get_own_property_names_fn object_get_own_property_names;
  expo_jsi_function_call_fn function_call;
  expo_jsi_function_call_with_this_fn function_call_with_this;
  expo_jsi_function_call_as_constructor_fn function_call_as_constructor;
  expo_jsi_create_host_function_fn create_host_function;
  expo_jsi_get_arguments_count_fn get_arguments_count;
  expo_jsi_get_argument_value_fn get_argument_value;
  expo_jsi_release_promise_fn release_promise;
  expo_jsi_release_value_fn release_value;
  expo_jsi_create_string_fn create_string;
  expo_jsi_clone_value_fn clone_value;
  expo_jsi_create_error_fn create_error;
  expo_jsi_get_string_fn get_string;
  expo_jsi_runtime_schedule_task_fn runtime_schedule_task;
  expo_jsi_runtime_can_execute_sync_fn runtime_can_execute_sync;
  expo_jsi_runtime_execute_sync_fn runtime_execute_sync;
  expo_jsi_is_promise_fn is_promise;
  expo_jsi_is_error_fn is_error;
  expo_jsi_coerce_to_string_fn coerce_to_string;
  expo_jsi_create_primitive_value_fn create_primitive_value;
  expo_jsi_create_object_with_prototype_fn create_object_with_prototype;
  expo_jsi_create_class_fn create_class;
  expo_jsi_create_class_with_superclass_fn create_class_with_superclass;
  expo_jsi_strict_equals_fn strict_equals;
  expo_jsi_object_set_native_state_fn object_set_native_state;
  expo_jsi_object_get_native_state_fn object_get_native_state;
  expo_jsi_object_clear_native_state_fn object_clear_native_state;
  expo_jsi_create_host_object_fn create_host_object;
  expo_jsi_array_buffer_retain_fn array_buffer_retain;
  expo_jsi_array_buffer_clone_handle_fn array_buffer_clone_handle;
  expo_jsi_array_buffer_get_bytes_fn array_buffer_get_bytes;
  expo_jsi_array_buffer_as_value_fn array_buffer_as_value;
  expo_jsi_array_buffer_release_fn array_buffer_release;
  expo_jsi_array_buffer_try_get_mutable_buffer_fn array_buffer_try_get_mutable_buffer;
  expo_jsi_mutable_buffer_allocate_fn mutable_buffer_allocate;
  expo_jsi_mutable_buffer_copy_fn mutable_buffer_copy;
  expo_jsi_mutable_buffer_clone_handle_fn mutable_buffer_clone_handle;
  expo_jsi_mutable_buffer_get_bytes_fn mutable_buffer_get_bytes;
  expo_jsi_mutable_buffer_as_value_fn mutable_buffer_as_value;
  expo_jsi_mutable_buffer_release_fn mutable_buffer_release;
  expo_jsi_object_create_weak_fn object_create_weak;
  expo_jsi_weak_object_lock_fn weak_object_lock;
  expo_jsi_weak_object_release_fn weak_object_release;
} expo_jsi_api;

#ifdef __cplusplus
} // extern "C"
#endif
