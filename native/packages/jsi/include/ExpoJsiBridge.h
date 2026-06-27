#pragma once

#include <cstdint>

#include <expo_jsi.h>
#include <jsi/jsi.h>

namespace expo::jsi {

class JsiRuntimeConnector;

expo_jsi_runtime_handle create_runtime_handle(JsiRuntimeConnector *connector);
void release_runtime_handle(expo_jsi_runtime_handle runtime);
uint32_t released_value_count(expo_jsi_runtime_handle runtime);
expo_jsi_value_handle create_borrowed_value_handle(const facebook::jsi::Value *value);
void release_borrowed_value_handle(expo_jsi_value_handle value);
facebook::jsi::Value copy_value_to_jsi(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle value);
const expo_jsi_api *api();

} // namespace expo::jsi
