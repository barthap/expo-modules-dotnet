#include "ArrayBufferCapabilities.h"

namespace {

void compileAgainstLegacyArrayBuffer(facebook::jsi::Runtime &runtime,
                                     facebook::jsi::ArrayBuffer &arrayBuffer)
{
  [[maybe_unused]] const auto detached =
    expo::dotnet::detail::isArrayBufferDetached(runtime, arrayBuffer);
  [[maybe_unused]] auto mutableBuffer =
    expo::dotnet::detail::tryGetArrayBufferMutableBuffer(runtime, arrayBuffer);
}

} // namespace
