#pragma once

#include <cstdint>

#include <expo_jsi.h>
#include <jsi/jsi.h>

namespace expo::jsi {

class JsiRuntimeConnector;

expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector);
void releaseRuntimeHandle(expo_jsi_runtime_handle runtime);
expo_jsi_value_handle createOwnedValueHandle(facebook::jsi::Value value);
const expo_jsi_api *api();

} // namespace expo::jsi
