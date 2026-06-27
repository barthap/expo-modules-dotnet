#pragma once

#include <stdint.h>

#include <expo_jsi.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct expo_jsi_testhost_runtime_t *expo_jsi_testhost_runtime_handle;

typedef struct expo_jsi_testhost_create_result {
  int32_t ok;
  const expo_jsi_api *api;
  expo_jsi_runtime_handle runtime;
  expo_jsi_testhost_runtime_handle testhost_runtime;
  expo_jsi_error error;
} expo_jsi_testhost_create_result;

typedef struct expo_jsi_testhost_counters {
  uint32_t released_values;
  uint32_t released_objects;
  uint32_t released_functions;
  uint32_t released_strings;
} expo_jsi_testhost_counters;

expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void);

expo_jsi_value_result expo_jsi_testhost_evaluate_script(
  expo_jsi_testhost_runtime_handle testhost_runtime,
  const uint8_t *source,
  int32_t source_len,
  const uint8_t *source_url,
  int32_t source_url_len);

expo_jsi_testhost_counters expo_jsi_testhost_get_counters(
  expo_jsi_testhost_runtime_handle testhost_runtime);

void expo_jsi_testhost_reset_counters(expo_jsi_testhost_runtime_handle testhost_runtime);

void expo_jsi_testhost_release_runtime(expo_jsi_testhost_runtime_handle testhost_runtime);

#ifdef __cplusplus
} // extern "C"
#endif
