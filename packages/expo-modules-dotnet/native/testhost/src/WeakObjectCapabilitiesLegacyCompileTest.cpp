#define EXPO_JSI_HAS_WEAK_OBJECT 0
#include "WeakObjectHandles.h"

namespace {

void compileAgainstLegacyWeakObject(facebook::jsi::Runtime &runtime, facebook::jsi::Object object)
{
  try {
    auto payload = expo::dotnet::detail::createWeakObjectPayload(runtime, object);
    auto entry = std::make_shared<expo::dotnet::WeakObjectEntry>(nullptr, std::move(payload));
    [[maybe_unused]] auto value = entry->lock(runtime);
    [[maybe_unused]] auto handle =
      expo::dotnet::createWeakObjectHandle(runtime, nullptr, std::move(object));
  } catch (const std::runtime_error &) {
  }
}

} // namespace
