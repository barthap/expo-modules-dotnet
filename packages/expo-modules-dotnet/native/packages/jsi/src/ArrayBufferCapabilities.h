#pragma once

#include <exception>
#include <memory>
#include <string_view>

#include <jsi/jsi.h>

namespace expo::dotnet::detail {

template <typename Buffer>
bool isArrayBufferDetached(facebook::jsi::Runtime &runtime, const Buffer &arrayBuffer)
{
  if constexpr (requires(const Buffer &candidate, facebook::jsi::Runtime &candidateRuntime) {
                  candidate.detached(candidateRuntime);
                }) {
    try {
      return arrayBuffer.detached(runtime);
    } catch (const std::exception &error) {
      // The pinned Hermes test runtime exposes the JSI method but reports that detachment is not
      // implemented. Treat that capability gap as "not detached"; engines that support the check
      // still get the strict validation required by the bridge contract.
      if (std::string_view(error.what()).find("not supported") != std::string_view::npos) {
        return false;
      }
      throw;
    }
  }
  (void)runtime;
  (void)arrayBuffer;
  return false;
}

template <typename Buffer>
std::shared_ptr<facebook::jsi::MutableBuffer> tryGetArrayBufferMutableBuffer(
  facebook::jsi::Runtime &runtime, Buffer &arrayBuffer)
{
  if constexpr (requires(Buffer &candidate, facebook::jsi::Runtime &candidateRuntime) {
                  candidate.tryGetMutableBuffer(candidateRuntime);
                }) {
    return arrayBuffer.tryGetMutableBuffer(runtime);
  }
  (void)runtime;
  (void)arrayBuffer;
  return nullptr;
}

template <typename Buffer>
bool isArrayBufferMutableBufferBacked(facebook::jsi::Runtime &runtime, Buffer &arrayBuffer)
{
  return tryGetArrayBufferMutableBuffer(runtime, arrayBuffer) != nullptr;
}

} // namespace expo::dotnet::detail
