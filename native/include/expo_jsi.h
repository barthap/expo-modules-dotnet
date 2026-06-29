#pragma once

#include <stdint.h>

#ifdef __cplusplus
namespace expo::jsi {
class RuntimeHandle;
class ValueHandle;
class PromiseHandle;
class ArgumentsHandle;
} // namespace expo::jsi

using expo_jsi_runtime_t = expo::jsi::RuntimeHandle;
using expo_jsi_value_t = expo::jsi::ValueHandle;
using expo_jsi_promise_t = expo::jsi::PromiseHandle;
using expo_jsi_arguments_t = expo::jsi::ArgumentsHandle;

extern "C" {

typedef expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef expo_jsi_value_t *expo_jsi_value_handle;
typedef expo_jsi_promise_t *expo_jsi_promise_handle;
typedef expo_jsi_arguments_t *expo_jsi_arguments_handle;
#else
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
typedef struct expo_jsi_promise_t *expo_jsi_promise_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;
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

typedef struct expo_jsi_error {
  int32_t code;
  const char *message;
  int32_t message_len;
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

typedef expo_jsi_value_result (*expo_jsi_host_function_callback_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle this_value,
  expo_jsi_arguments_handle arguments);

typedef void (*expo_jsi_release_callback_context_fn)(void *callback_context);

typedef void (*expo_jsi_task_callback_fn)(void *task_context);

typedef void (*expo_jsi_release_task_context_fn)(void *task_context);

typedef expo_jsi_value_result (*expo_jsi_create_number_fn)(expo_jsi_runtime_handle runtime,
                                                           double value);

typedef expo_jsi_value_result (*expo_jsi_create_bool_fn)(expo_jsi_runtime_handle runtime,
                                                         uint8_t value);

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

typedef expo_jsi_value_result (*expo_jsi_create_host_function_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
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

typedef expo_jsi_error (*expo_jsi_runtime_drain_tasks_fn)(expo_jsi_runtime_handle runtime);

typedef uint8_t (*expo_jsi_is_promise_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value,
                                          expo_jsi_error *error);

typedef uint8_t (*expo_jsi_is_error_fn)(expo_jsi_runtime_handle runtime,
                                        expo_jsi_value_handle value,
                                        expo_jsi_error *error);

typedef expo_jsi_string_result (*expo_jsi_coerce_to_string_fn)(expo_jsi_runtime_handle runtime,
                                                               expo_jsi_value_handle value);

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
  expo_jsi_runtime_drain_tasks_fn runtime_drain_tasks;
  expo_jsi_is_promise_fn is_promise;
  expo_jsi_is_error_fn is_error;
  expo_jsi_coerce_to_string_fn coerce_to_string;
} expo_jsi_api;

#ifdef __cplusplus
} // extern "C"
#endif
