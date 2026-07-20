#pragma once

#include <memory>
#include <optional>
#include <stdexcept>
#include <string_view>

#include <jsi/jsi.h>

#ifndef EXPO_JSI_HAS_WEAK_OBJECT
#define EXPO_JSI_HAS_WEAK_OBJECT 1
#endif

namespace expo::dotnet::detail {

#if EXPO_JSI_HAS_WEAK_OBJECT

class WeakObjectPayload final {
public:
  explicit WeakObjectPayload(facebook::jsi::WeakObject value)
    : value_(std::move(value))
  {
  }

  facebook::jsi::WeakObject value_;
};

inline std::unique_ptr<WeakObjectPayload> createWeakObjectPayload(facebook::jsi::Runtime &runtime,
                                                                  facebook::jsi::Object object)
{
  return std::make_unique<WeakObjectPayload>(facebook::jsi::WeakObject(runtime, object));
}

inline std::optional<facebook::jsi::Object> lockWeakObjectPayload(facebook::jsi::Runtime &runtime,
                                                                  WeakObjectPayload &payload)
{
  auto value = payload.value_.lock(runtime);
  if (value.isUndefined()) {
    return std::nullopt;
  }
  return value.asObject(runtime);
}

inline void releaseWeakObjectPayloadOnRuntime(facebook::jsi::Runtime &,
                                              std::unique_ptr<WeakObjectPayload> payload) noexcept
{
  payload.reset();
}

inline void abandonWeakObjectPayload(std::unique_ptr<WeakObjectPayload> payload) noexcept
{
  // WeakObject destruction is runtime-affine. Intentionally leak if runtime access is gone.
  (void)payload.release();
}

#else

class WeakObjectPayload final {};

inline std::unique_ptr<WeakObjectPayload> createWeakObjectPayload(facebook::jsi::Runtime &,
                                                                  facebook::jsi::Object)
{
  throw std::runtime_error("WeakObject is unsupported by this JSI capability.");
}

inline std::optional<facebook::jsi::Object> lockWeakObjectPayload(facebook::jsi::Runtime &,
                                                                  WeakObjectPayload &)
{
  throw std::runtime_error("WeakObject is unsupported by this JSI capability.");
}

inline void releaseWeakObjectPayloadOnRuntime(facebook::jsi::Runtime &,
                                              std::unique_ptr<WeakObjectPayload>) noexcept
{
}

inline void abandonWeakObjectPayload(std::unique_ptr<WeakObjectPayload>) noexcept {}

#endif

} // namespace expo::dotnet::detail
