#pragma once

#include <array>
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

namespace expo::dotnet {

#ifndef EXPO_DOTNET_JSI_NAMESPACE_ALIAS
#define EXPO_DOTNET_JSI_NAMESPACE_ALIAS
namespace jsi = facebook::jsi;
#endif

class HermesConsoleRuntimeConnector;
class HermesConsoleRuntimeTestControl;

class HermesConsoleRuntimeExecutor final : public JsiRuntimeExecutor {
public:
  explicit HermesConsoleRuntimeExecutor(HermesConsoleRuntimeConnector &connector);
  ~HermesConsoleRuntimeExecutor() override;

  // Returns the Hermes runtime for code that is already running on the executor
  // thread. This is an assertion boundary: non-executor callers must enter
  // through executeAsync/executeSync instead of borrowing runtime access.
  jsi::Runtime &runtime();

  // Passive lifecycle check used by ABI validation paths that must not touch
  // Hermes from the caller thread.
  bool isRuntimeValid() const noexcept;

  // Thread-affinity predicate for reentrant executor calls. The thread id is
  // published during threadMain startup and remains stable until shutdown.
  bool isOnRuntimeThread() const noexcept;

  // Enqueues work for the executor thread and returns after the queue accepts
  // it. The callback always runs on the executor thread; callers that need
  // completion should wait on the managed Task or, in the Hermes console test
  // host, wait until the loop is idle.
  void executeAsync(JsiRuntimeTaskPriority priority,
                    std::function<void(jsi::Runtime &)> work) noexcept override;

  // A passive capability answer. This must not probe by calling executeSync,
  // because probes can deadlock if the caller already holds a required lock.
  bool canExecuteSync() const noexcept override;

  // Provides synchronous runtime access. Executor-thread callers run inline;
  // other callers enqueue an Immediate task and block until that queued task
  // either finishes, throws, or is cancelled by shutdown.
  void executeSync(std::function<void(jsi::Runtime &)> work) override;

  // Waits for the loop to become idle. It does not manually execute queued
  // work on the caller thread; the executor thread remains the only runner.
  void waitUntilIdle();

  // Transitions the loop to Stopping, releases queued-but-not-running work, and
  // joins the executor thread after any active task finishes.
  void shutdown() noexcept;

private:
  friend class HermesConsoleRuntimeConnector;

  // Test-only controls stay behind the connector's private companion so
  // production users of the executor cannot depend on queue manipulation.
  void pauseForTesting();
  void resumeForTesting();
  void dropNextTaskForTesting(JsiRuntimeTaskPriority priority);
  bool waitUntilTaskQueuedForTesting(JsiRuntimeTaskPriority priority);
  bool waitUntilTaskCountForTesting(JsiRuntimeTaskPriority priority, size_t count);
  bool dropQueuedTaskForTesting(JsiRuntimeTaskPriority priority);

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
    std::function<void(jsi::Runtime &)> work;
    std::shared_ptr<SyncResult> syncResult;
  };

  // Executor thread entry point. It creates and destroys the Hermes runtime on
  // the same thread that runs all queued JSI work.
  void threadMain();

  // Selects the next task by priority, then FIFO sequence. Caller must hold
  // mutex_ and queue_ must be non-empty.
  size_t nextTaskIndexLocked() const;

  // Runs a callback with executor-owned runtime access. This is local-only to
  // the executor thread; external callers must use executeAsync/executeSync.
  void runTask(std::function<void(jsi::Runtime &)> work);

  // Performs the host microtask checkpoint for the current runtime task.
  void drainMicrotasks();

  // Releases queued work that will never run. Caller must hold mutex_.
  void releaseQueuedTasksLocked();

  // Notifies drain waiters only when no queued or active task remains. Caller
  // must hold mutex_.
  void notifyIdleIfNeededLocked();

  HermesConsoleRuntimeConnector *connector_;
  // Protects lifecycle state, queue_, runtime publication, runtimeThreadId_,
  // and activeTasks_. It must never be held while running user/managed code.
  mutable std::mutex mutex_;
  std::condition_variable workAvailable_;
  std::condition_variable idleChanged_;
  std::condition_variable queueChanged_;
  std::deque<QueuedTask> queue_;
  std::thread runtimeThread_;
  std::unique_ptr<jsi::Runtime> runtime_;
  std::thread::id runtimeThreadId_;
  uint64_t nextSequence_ = 0;
  // Counts executor-thread callbacks that have been popped from the queue but
  // have not yet completed their microtask checkpoint.
  uint32_t activeTasks_ = 0;
  bool paused_ = false;
  std::array<uint32_t, 6> dropNextTasks_{};
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

  jsi::Runtime &runtime() override;
  JsiRuntimeExecutor &runtimeExecutor() override;
  bool isRuntimeValid() const override;
  void waitUntilIdle();
  void invalidate() override;

private:
  friend class HermesConsoleRuntimeExecutor;
  friend class HermesConsoleRuntimeTestControl;

  void pauseRuntimeExecutorForTesting();
  void resumeRuntimeExecutorForTesting();
  void dropNextRuntimeTaskForTesting(JsiRuntimeTaskPriority priority);
  bool waitUntilRuntimeTaskQueuedForTesting(JsiRuntimeTaskPriority priority);
  bool waitUntilRuntimeTaskCountForTesting(JsiRuntimeTaskPriority priority, size_t count);
  bool dropQueuedRuntimeTaskForTesting(JsiRuntimeTaskPriority priority);

  HermesConsoleRuntimeExecutor runtimeExecutor_;
};

} // namespace expo::dotnet
