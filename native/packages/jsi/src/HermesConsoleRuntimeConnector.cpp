#include "HermesConsoleRuntimeConnector.h"

#include <stdexcept>

namespace expo::jsi {

HermesConsoleRuntimeConnector::HermesConsoleRuntimeConnector()
  : runtime_(facebook::hermes::makeHermesRuntime())
{
  if (!runtime_) {
    throw std::runtime_error("Failed to create Hermes runtime.");
  }
}

HermesConsoleRuntimeConnector::~HermesConsoleRuntimeConnector()
{
  invalidate();
}

facebook::jsi::Runtime &HermesConsoleRuntimeConnector::runtime()
{
  if (!runtime_) {
    throw std::runtime_error("Hermes runtime is invalid.");
  }
  return *runtime_;
}

JsiScheduler &HermesConsoleRuntimeConnector::scheduler()
{
  return scheduler_;
}

bool HermesConsoleRuntimeConnector::isRuntimeValid() const
{
  return runtime_ != nullptr;
}

void HermesConsoleRuntimeConnector::invalidate()
{
  runtime_.reset();
}

} // namespace expo::jsi
