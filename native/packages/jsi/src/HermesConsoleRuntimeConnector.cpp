#include "HermesConsoleRuntimeConnector.h"

#include <cstddef>
#include <stdexcept>
#include <utility>

namespace expo::jsi {

HermesConsoleRuntimeExecutor::HermesConsoleRuntimeExecutor(HermesConsoleRuntimeConnector &connector)
  : connector_(&connector)
{
}

void HermesConsoleRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority priority, std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  queue_.push_back(QueuedTask{priority, nextSequence_++, std::move(work)});
}

bool HermesConsoleRuntimeExecutor::canExecuteSync() const noexcept
{
  return true;
}

void HermesConsoleRuntimeExecutor::executeSync(std::function<void(facebook::jsi::Runtime &)> work)
{
  runWithRuntime(std::move(work));
}

void HermesConsoleRuntimeExecutor::drain()
{
  while (!queue_.empty()) {
    auto index = nextTaskIndex();
    auto task = std::move(queue_[index]);
    queue_.erase(queue_.begin() + static_cast<std::ptrdiff_t>(index));
    runWithRuntime(std::move(task.work));
  }
}

size_t HermesConsoleRuntimeExecutor::nextTaskIndex() const
{
  size_t bestIndex = 0;
  for (size_t index = 1; index < queue_.size(); index++) {
    const auto &candidate = queue_[index];
    const auto &best = queue_[bestIndex];
    if (static_cast<int>(candidate.priority) < static_cast<int>(best.priority) ||
        (candidate.priority == best.priority && candidate.sequence < best.sequence)) {
      bestIndex = index;
    }
  }
  return bestIndex;
}

void HermesConsoleRuntimeExecutor::runWithRuntime(
  std::function<void(facebook::jsi::Runtime &)> work)
{
  if (connector_ == nullptr) {
    throw std::runtime_error("Hermes runtime connector is missing.");
  }

  if (isExecuting_) {
    work(connector_->runtime());
    return;
  }

  isExecuting_ = true;
  try {
    work(connector_->runtime());
    isExecuting_ = false;
  } catch (...) {
    isExecuting_ = false;
    throw;
  }
}

HermesConsoleRuntimeConnector::HermesConsoleRuntimeConnector()
  : runtimeExecutor_(*this),
    runtime_(facebook::hermes::makeHermesRuntime())
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

JsiRuntimeExecutor &HermesConsoleRuntimeConnector::runtimeExecutor()
{
  return runtimeExecutor_;
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
