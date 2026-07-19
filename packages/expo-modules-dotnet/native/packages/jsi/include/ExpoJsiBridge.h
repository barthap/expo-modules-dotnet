#pragma once

#include <cstdint>

#include <expo_jsi.h>
#include <jsi/jsi.h>

namespace expo::dotnet {

#ifndef EXPO_DOTNET_JSI_NAMESPACE_ALIAS
#define EXPO_DOTNET_JSI_NAMESPACE_ALIAS
namespace jsi = facebook::jsi;
#endif

class JsiRuntimeConnector;

expo_jsi_runtime_handle createRuntimeHandle(JsiRuntimeConnector &connector);
void prepareRuntimeHandleForInvalidation(expo_jsi_runtime_handle runtime);
void releaseRuntimeHandle(expo_jsi_runtime_handle runtime);
expo_jsi_value_handle createOwnedValueHandle(jsi::Value value);
const expo_jsi_api *api();

} // namespace expo::dotnet
