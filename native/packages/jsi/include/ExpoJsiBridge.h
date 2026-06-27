#pragma once

#include <cstdint>

#include <expo_jsi.h>

namespace expo::jsi {

class JsiRuntimeConnector;

expo_jsi_runtime_handle create_runtime_handle(JsiRuntimeConnector *connector);
void release_runtime_handle(expo_jsi_runtime_handle runtime);
uint32_t released_value_count(expo_jsi_runtime_handle runtime);
const expo_jsi_api *api();

} // namespace expo::jsi
