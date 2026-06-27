#pragma once

#include <stdint.h>

#ifdef __cplusplus
namespace expo::jsi {
class RuntimeHandle;
class ValueHandle;
class ObjectHandle;
class FunctionHandle;
class ArgumentsHandle;
} // namespace expo::jsi

using expo_jsi_runtime_t = expo::jsi::RuntimeHandle;
using expo_jsi_value_t = expo::jsi::ValueHandle;
using expo_jsi_object_t = expo::jsi::ObjectHandle;
using expo_jsi_function_t = expo::jsi::FunctionHandle;
using expo_jsi_arguments_t = expo::jsi::ArgumentsHandle;

extern "C" {

typedef expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef expo_jsi_value_t *expo_jsi_value_handle;
typedef expo_jsi_object_t *expo_jsi_object_handle;
typedef expo_jsi_function_t *expo_jsi_function_handle;
typedef expo_jsi_arguments_t *expo_jsi_arguments_handle;
#else
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
typedef struct expo_jsi_object_t *expo_jsi_object_handle;
typedef struct expo_jsi_function_t *expo_jsi_function_handle;
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

typedef struct expo_jsi_object_result {
  int32_t ok;
  expo_jsi_object_handle object;
  expo_jsi_error error;
} expo_jsi_object_result;

typedef struct expo_jsi_function_result {
  int32_t ok;
  expo_jsi_function_handle function;
  expo_jsi_error error;
} expo_jsi_function_result;

typedef expo_jsi_value_result (*expo_jsi_host_function_callback_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle this_value,
  expo_jsi_arguments_handle arguments);

typedef void (*expo_jsi_release_callback_context_fn)(void *callback_context);

typedef expo_jsi_value_result (*expo_jsi_create_number_fn)(expo_jsi_runtime_handle runtime,
                                                           double value);

typedef expo_jsi_value_result (*expo_jsi_create_bool_fn)(expo_jsi_runtime_handle runtime,
                                                         uint8_t value);

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

typedef expo_jsi_object_result (*expo_jsi_get_global_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_object_result (*expo_jsi_create_object_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_object_as_value_fn)(expo_jsi_runtime_handle runtime,
                                                             expo_jsi_object_handle object);

typedef expo_jsi_error (*expo_jsi_object_set_property_fn)(expo_jsi_runtime_handle runtime,
                                                          expo_jsi_object_handle object,
                                                          const char *name,
                                                          int32_t name_len,
                                                          expo_jsi_value_handle value);

typedef expo_jsi_function_result (*expo_jsi_create_host_function_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  uint32_t parameter_count,
  expo_jsi_host_function_callback_fn callback,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);

typedef expo_jsi_value_result (*expo_jsi_function_as_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_function_handle function);

typedef uint32_t (*expo_jsi_get_arguments_count_fn)(expo_jsi_runtime_handle runtime,
                                                    expo_jsi_arguments_handle arguments,
                                                    expo_jsi_error *error);

typedef expo_jsi_value_result (*expo_jsi_get_argument_value_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_arguments_handle arguments,
  uint32_t index);

typedef void (*expo_jsi_release_value_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value);

typedef void (*expo_jsi_release_object_fn)(expo_jsi_runtime_handle runtime,
                                           expo_jsi_object_handle object);

typedef void (*expo_jsi_release_function_fn)(expo_jsi_runtime_handle runtime,
                                             expo_jsi_function_handle function);

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
  expo_jsi_object_as_value_fn object_as_value;
  expo_jsi_object_set_property_fn object_set_property;
  expo_jsi_create_host_function_fn create_host_function;
  expo_jsi_function_as_value_fn function_as_value;
  expo_jsi_get_arguments_count_fn get_arguments_count;
  expo_jsi_get_argument_value_fn get_argument_value;
  expo_jsi_release_object_fn release_object;
  expo_jsi_release_function_fn release_function;
  expo_jsi_release_value_fn release_value;
} expo_jsi_api;

#ifdef __cplusplus
} // extern "C"
#endif
