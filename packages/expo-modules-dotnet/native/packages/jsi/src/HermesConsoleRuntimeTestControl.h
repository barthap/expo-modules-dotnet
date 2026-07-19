#pragma once

#include <cstddef>

#include "JsiRuntimeConnector.h"

namespace expo::dotnet {

class HermesConsoleRuntimeConnector;

// Private testhost companion for deterministic executor queue controls.
class HermesConsoleRuntimeTestControl final {
public:
  static void pause(HermesConsoleRuntimeConnector &connector);
  static void resume(HermesConsoleRuntimeConnector &connector);
  static void dropNextTask(HermesConsoleRuntimeConnector &connector,
                           JsiRuntimeTaskPriority priority);
  static bool waitUntilTaskQueued(HermesConsoleRuntimeConnector &connector,
                                  JsiRuntimeTaskPriority priority);
  static bool waitUntilTaskCount(HermesConsoleRuntimeConnector &connector,
                                 JsiRuntimeTaskPriority priority,
                                 size_t count);
  static bool dropQueuedTask(HermesConsoleRuntimeConnector &connector,
                             JsiRuntimeTaskPriority priority);
};

} // namespace expo::dotnet
