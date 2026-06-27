#pragma once

#include <cstdint>
#include <deque>
#include <functional>
#include <memory>

#include <hermes/hermes.h>

#include "JsiRuntimeConnector.h"

namespace expo::jsi {

class HermesConsoleRuntimeConnector;

class HermesConsoleRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  explicit HermesConsoleRuntimeExecutor(HermesConsoleRuntimeConnector &connector);

  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(facebook::jsi::Runtime &)> work) noexcept override;

  bool canExecuteSync() const noexcept override;

  void executeSync(std::function<void(facebook::jsi::Runtime &)> work) override;

  void drain() override;

private:
  struct QueuedTask {
    JsiRuntimeTaskPriority priority;
    uint64_t sequence;
    std::function<void(facebook::jsi::Runtime &)> work;
  };

  size_t nextTaskIndex() const;
  void runWithRuntime(std::function<void(facebook::jsi::Runtime &)> work);

  HermesConsoleRuntimeConnector *connector_;
  std::deque<QueuedTask> queue_;
  uint64_t nextSequence_ = 0;
  bool isExecuting_ = false;
};

class HermesConsoleRuntimeConnector final : public JsiRuntimeConnector {
public:
  HermesConsoleRuntimeConnector();
  ~HermesConsoleRuntimeConnector() override;

  facebook::jsi::Runtime &runtime() override;
  JsiRuntimeExecutor &runtimeExecutor() override;
  bool isRuntimeValid() const override;
  void invalidate() override;

private:
  friend class HermesConsoleRuntimeExecutor;

  HermesConsoleRuntimeExecutor runtimeExecutor_;
  std::unique_ptr<facebook::jsi::Runtime> runtime_;
};

} // namespace expo::jsi
