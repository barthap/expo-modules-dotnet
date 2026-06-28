#pragma once

#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <exception>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>

#include <hermes/hermes.h>

#include "JsiRuntimeConnector.h"

namespace expo::jsi {

class HermesConsoleRuntimeConnector;

class HermesConsoleRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  explicit HermesConsoleRuntimeExecutor(HermesConsoleRuntimeConnector &connector);
  ~HermesConsoleRuntimeExecutor() override;

  facebook::jsi::Runtime &runtime();
  bool isRuntimeValid() const noexcept;
  bool isOnRuntimeThread() const noexcept;

  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(facebook::jsi::Runtime &)> work) noexcept override;

  bool canExecuteSync() const noexcept override;

  void executeSync(std::function<void(facebook::jsi::Runtime &)> work) override;

  void drain() override;
  void shutdown() noexcept;

private:
  enum class State {
    Created,
    Running,
    Stopping,
    Stopped,
  };

  // Used only by executeSync callers that are not on the runtime thread. This
  // has its own mutex so the caller can block without holding the queue mutex
  // needed by the executor thread.
  struct SyncResult {
    bool finished = false;
    bool cancelled = false;
    std::exception_ptr exception;
    std::mutex mutex;
    std::condition_variable condition;
  };

  struct QueuedTask {
    JsiRuntimeTaskPriority priority;
    uint64_t sequence;
    std::function<void(facebook::jsi::Runtime &)> work;
    std::shared_ptr<SyncResult> syncResult;
  };

  void threadMain();
  size_t nextTaskIndexLocked() const;
  void runTask(std::function<void(facebook::jsi::Runtime &)> work);
  void drainMicrotasks();
  void releaseQueuedTasksLocked();
  void notifyIdleIfNeededLocked();

  HermesConsoleRuntimeConnector *connector_;
  // Protects lifecycle state, queue_, runtime publication, runtimeThreadId_,
  // and activeTasks_. It must never be held while running user/managed code.
  mutable std::mutex mutex_;
  std::condition_variable workAvailable_;
  std::condition_variable idleChanged_;
  std::deque<QueuedTask> queue_;
  std::thread runtimeThread_;
  std::unique_ptr<facebook::jsi::Runtime> runtime_;
  std::thread::id runtimeThreadId_;
  uint64_t nextSequence_ = 0;
  // Counts executor-thread callbacks that have been popped from the queue but
  // have not yet completed their microtask checkpoint.
  uint32_t activeTasks_ = 0;
  // Executor-thread-only reentrancy marker. Nested executeSync calls run inline
  // and defer the microtask checkpoint to the outermost runtime task.
  bool isExecuting_ = false;
  State state_ = State::Created;
  std::exception_ptr startupException_;
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
};

} // namespace expo::jsi
