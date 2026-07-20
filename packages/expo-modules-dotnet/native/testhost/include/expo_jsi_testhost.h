#pragma once

#include <stdint.h>

#include <expo_jsi.h>

#if defined(_WIN32)
#define EXPO_JSI_TESTHOST_EXPORT __declspec(dllexport)
#else
#define EXPO_JSI_TESTHOST_EXPORT
#endif

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
  uint32_t released_promises;
  uint32_t released_strings;
  uint32_t released_errors;
  uint32_t released_task_contexts;
  uint32_t sync_execute_calls;
  uint32_t primitive_value_creates;
  uint32_t deprecated_number_creates;
  uint32_t deprecated_bool_creates;
  uint32_t released_native_states;
  uint32_t released_promises_off_runtime_thread;
  uint32_t long_lived_array_buffers_released;
  uint32_t long_lived_array_buffers_abandoned;
  uint32_t long_lived_weak_objects_released;
  uint32_t long_lived_weak_objects_abandoned;
  uint32_t long_lived_objects_remaining;
} expo_jsi_testhost_counters;

EXPO_JSI_TESTHOST_EXPORT expo_jsi_testhost_create_result expo_jsi_testhost_create_runtime(void);

EXPO_JSI_TESTHOST_EXPORT expo_jsi_value_result
expo_jsi_testhost_evaluate_script(expo_jsi_testhost_runtime_handle testhost_runtime,
                                  const uint8_t *source,
                                  int32_t source_len,
                                  const uint8_t *source_url,
                                  int32_t source_url_len);

EXPO_JSI_TESTHOST_EXPORT expo_jsi_testhost_counters
expo_jsi_testhost_get_counters(expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_reset_counters(
  expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_drain_tasks(
  expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT expo_jsi_error
expo_jsi_testhost_wait_until_idle(expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT expo_jsi_error
expo_jsi_testhost_collect_garbage(expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_pause_runtime_executor(
  expo_jsi_testhost_runtime_handle testhost_runtime);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_resume_runtime_executor(
  expo_jsi_testhost_runtime_handle testhost_runtime);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_drop_next_runtime_task(
  expo_jsi_testhost_runtime_handle testhost_runtime, int32_t priority);
EXPO_JSI_TESTHOST_EXPORT expo_jsi_error expo_jsi_testhost_wait_until_runtime_task_queued(
  expo_jsi_testhost_runtime_handle testhost_runtime, int32_t priority);
EXPO_JSI_TESTHOST_EXPORT expo_jsi_error expo_jsi_testhost_wait_until_runtime_tasks_queued(
  expo_jsi_testhost_runtime_handle testhost_runtime, int32_t priority, int32_t count);
EXPO_JSI_TESTHOST_EXPORT expo_jsi_error expo_jsi_testhost_drop_queued_runtime_task(
  expo_jsi_testhost_runtime_handle testhost_runtime, int32_t priority);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_release_bridge_runtime_handle(
  expo_jsi_testhost_runtime_handle testhost_runtime);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_poison_mutable_buffer_dispatch(
  expo_jsi_testhost_runtime_handle testhost_runtime);

EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_set_sync_execution_supported(
  expo_jsi_testhost_runtime_handle testhost_runtime, uint8_t supported);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_invalidate_runtime(
  expo_jsi_testhost_runtime_handle testhost_runtime);
EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_prepare_runtime_for_invalidation(
  expo_jsi_testhost_runtime_handle testhost_runtime);
EXPO_JSI_TESTHOST_EXPORT expo_jsi_error
expo_jsi_testhost_validate_array_buffer_snapshot(expo_jsi_testhost_runtime_handle testhost_runtime,
                                                 uint8_t detached,
                                                 int32_t current_length,
                                                 int32_t captured_length);
EXPO_JSI_TESTHOST_EXPORT expo_jsi_error expo_jsi_testhost_validate_array_buffer_length(
  expo_jsi_testhost_runtime_handle testhost_runtime, uint64_t length);

EXPO_JSI_TESTHOST_EXPORT void expo_jsi_testhost_release_runtime(
  expo_jsi_testhost_runtime_handle testhost_runtime);

#ifdef __cplusplus
} // extern "C"
#endif
