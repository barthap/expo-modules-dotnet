#pragma once

#include <stdint.h>

#ifdef __cplusplus
namespace expo::jsi {
class RuntimeHandle;
class ValueHandle;
} // namespace expo::jsi

using expo_jsi_runtime_t = expo::jsi::RuntimeHandle;
using expo_jsi_value_t = expo::jsi::ValueHandle;

extern "C" {

typedef expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef expo_jsi_value_t *expo_jsi_value_handle;
#else
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
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

typedef expo_jsi_value_result (*expo_jsi_create_number_fn)(expo_jsi_runtime_handle runtime,
                                                           double value);

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

typedef void (*expo_jsi_release_value_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_value_handle value);

typedef struct expo_jsi_api {
  uint32_t size;
  uint32_t version;

  expo_jsi_create_number_fn create_number;
  expo_jsi_get_value_kind_fn get_value_kind;
  expo_jsi_get_bool_fn get_bool;
  expo_jsi_get_double_fn get_double;
  expo_jsi_release_value_fn release_value;
} expo_jsi_api;

#ifdef __cplusplus
} // extern "C"
#endif
