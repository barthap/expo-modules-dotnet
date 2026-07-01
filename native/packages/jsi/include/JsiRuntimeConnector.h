#pragma once

#include <functional>

#include <jsi/jsi.h>

namespace expo::dotnet {

#ifndef EXPO_DOTNET_JSI_NAMESPACE_ALIAS
#define EXPO_DOTNET_JSI_NAMESPACE_ALIAS
namespace jsi = facebook::jsi;
#endif

enum class JsiRuntimeTaskPriority : int {
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
};

class JsiRuntimeExecutor {
public:
  virtual ~JsiRuntimeExecutor() = default;

  virtual void executeAsync(JsiRuntimeTaskPriority priority,
                            std::function<void(jsi::Runtime &)> work) noexcept = 0;

  virtual bool canExecuteSync() const noexcept = 0;

  virtual void executeSync(std::function<void(jsi::Runtime &)> work) = 0;
};

class JsiRuntimeConnector {
public:
  virtual ~JsiRuntimeConnector() = default;

  virtual jsi::Runtime &runtime() = 0;
  virtual JsiRuntimeExecutor &runtimeExecutor() = 0;
  virtual bool isRuntimeValid() const = 0;
  virtual void invalidate() = 0;
};

} // namespace expo::dotnet
