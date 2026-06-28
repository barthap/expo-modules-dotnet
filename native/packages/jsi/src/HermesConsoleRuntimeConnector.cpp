#include "HermesConsoleRuntimeConnector.h"

#include <cstddef>
#include <stdexcept>
#include <thread>
#include <utility>

namespace expo::jsi {

HermesConsoleRuntimeExecutor::HermesConsoleRuntimeExecutor(HermesConsoleRuntimeConnector &connector)
  : connector_(&connector)
{
  runtimeThread_ = std::thread([this]() { threadMain(); });

  std::unique_lock<std::mutex> lock(mutex_);
  idleChanged_.wait(lock, [this]() {
    return state_ == State::Running || state_ == State::Stopped || startupException_ != nullptr;
  });

  if (startupException_ != nullptr) {
    lock.unlock();
    shutdown();
    std::rethrow_exception(startupException_);
  }
  if (state_ != State::Running) {
    throw std::runtime_error("Hermes runtime loop failed to start.");
  }
}

HermesConsoleRuntimeExecutor::~HermesConsoleRuntimeExecutor()
{
  shutdown();
}

facebook::jsi::Runtime &HermesConsoleRuntimeExecutor::runtime()
{
  if (!isOnRuntimeThread()) {
    throw std::runtime_error("Hermes runtime access is not on the executor thread.");
  }
  if (runtime_ == nullptr) {
    throw std::runtime_error("Hermes runtime is invalid.");
  }
  return *runtime_;
}

bool HermesConsoleRuntimeExecutor::isRuntimeValid() const noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  return state_ == State::Running && runtime_ != nullptr;
}

bool HermesConsoleRuntimeExecutor::isOnRuntimeThread() const noexcept
{
  std::lock_guard<std::mutex> lock(mutex_);
  return runtimeThreadId_ != std::thread::id{} && std::this_thread::get_id() == runtimeThreadId_;
}

void HermesConsoleRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority priority, std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  try {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (state_ != State::Running) {
        return;
      }
      // The queue stores owning callables. Dropping a callable during shutdown
      // must release any captured managed task context without invoking it.
      queue_.push_back(QueuedTask{priority, nextSequence_++, std::move(work), nullptr});
    }
    workAvailable_.notify_one();
  } catch (...) {
    // executeAsync is noexcept; dropping the work releases captured managed state.
  }
}

bool HermesConsoleRuntimeExecutor::canExecuteSync() const noexcept
{
  return true;
}

void HermesConsoleRuntimeExecutor::executeSync(std::function<void(facebook::jsi::Runtime &)> work)
{
  if (isOnRuntimeThread()) {
    runTask(std::move(work));
    return;
  }

  auto result = std::make_shared<SyncResult>();
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (state_ != State::Running) {
      throw std::runtime_error("Hermes runtime loop is not running.");
    }
    queue_.push_back(QueuedTask{
      JsiRuntimeTaskPriority::Immediate,
      nextSequence_++,
      [work = std::move(work)](facebook::jsi::Runtime &runtime) mutable { work(runtime); },
      result,
    });
  }
  workAvailable_.notify_one();

  // Wait on SyncResult rather than mutex_ so the executor can acquire mutex_,
  // pop this task, run it, and publish completion without a lock cycle.
  std::unique_lock<std::mutex> resultLock(result->mutex);
  result->condition.wait(resultLock, [result]() { return result->finished; });
  if (result->cancelled) {
    throw std::runtime_error("Hermes runtime loop stopped before sync work ran.");
  }
  if (result->exception != nullptr) {
    std::rethrow_exception(result->exception);
  }
}

void HermesConsoleRuntimeExecutor::drain()
{
  if (isOnRuntimeThread()) {
    drainMicrotasks();
    return;
  }

  std::unique_lock<std::mutex> lock(mutex_);
  idleChanged_.wait(
    lock, [this]() { return state_ == State::Stopped || (queue_.empty() && activeTasks_ == 0); });
}

void HermesConsoleRuntimeExecutor::shutdown() noexcept
{
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (state_ != State::Stopped) {
      state_ = State::Stopping;
      // Queued work has not started, so it is released rather than invoked.
      // A running task is allowed to finish on the executor thread below.
      releaseQueuedTasksLocked();
    }
  }
  workAvailable_.notify_all();
  idleChanged_.notify_all();

  if (runtimeThread_.joinable()) {
    runtimeThread_.join();
  }
}

void HermesConsoleRuntimeExecutor::threadMain()
{
  // This method is the only place that owns ordinary Hermes execution. The
  // runtime is created here, all queued callbacks run here, and the runtime is
  // destroyed here so JSI never migrates between host threads.
  {
    std::lock_guard<std::mutex> lock(mutex_);
    runtimeThreadId_ = std::this_thread::get_id();
  }

  try {
    auto runtime = facebook::hermes::makeHermesRuntime(
      ::hermes::vm::RuntimeConfig::Builder().withMicrotaskQueue(true).build());
    if (!runtime) {
      throw std::runtime_error("Failed to create Hermes runtime.");
    }

    {
      std::lock_guard<std::mutex> lock(mutex_);
      runtime_ = std::move(runtime);
      state_ = State::Running;
    }
    idleChanged_.notify_all();
    workAvailable_.notify_all();

    while (true) {
      QueuedTask task{};
      {
        std::unique_lock<std::mutex> lock(mutex_);
        workAvailable_.wait(lock,
                            [this]() { return state_ == State::Stopping || !queue_.empty(); });

        // Shutdown drains the queue by release, not execution. Once the current
        // task is done and the queue is empty, the runtime can be destroyed on
        // the executor thread that owns it.
        if (state_ == State::Stopping && queue_.empty()) {
          state_ = State::Stopped;
          runtime_.reset();
          notifyIdleIfNeededLocked();
          idleChanged_.notify_all();
          return;
        }

        auto index = nextTaskIndexLocked();
        task = std::move(queue_[index]);
        queue_.erase(queue_.begin() + static_cast<std::ptrdiff_t>(index));
        // activeTasks_ keeps drain()/WaitUntilIdle from observing idle between
        // queue pop and the task's post-callback microtask checkpoint.
        activeTasks_++;
      }

      try {
        runTask(std::move(task.work));
      } catch (...) {
        if (task.syncResult != nullptr) {
          task.syncResult->exception = std::current_exception();
        }
      }

      if (task.syncResult != nullptr) {
        {
          std::lock_guard<std::mutex> resultLock(task.syncResult->mutex);
          task.syncResult->finished = true;
        }
        task.syncResult->condition.notify_one();
      }

      {
        std::lock_guard<std::mutex> lock(mutex_);
        activeTasks_--;
        notifyIdleIfNeededLocked();
      }
    }
  } catch (...) {
    std::lock_guard<std::mutex> lock(mutex_);
    startupException_ = std::current_exception();
    state_ = State::Stopped;
    runtime_.reset();
    releaseQueuedTasksLocked();
    notifyIdleIfNeededLocked();
  }
  idleChanged_.notify_all();
}

size_t HermesConsoleRuntimeExecutor::nextTaskIndexLocked() const
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

void HermesConsoleRuntimeExecutor::runTask(std::function<void(facebook::jsi::Runtime &)> work)
{
  // runTask is intentionally local to the executor thread. It assumes runtime
  // ownership has already been established by the executor loop; reentrant
  // sync execution reuses the active runtime task.
  if (isExecuting_) {
    // Reentrant sync execution is already inside an executor-owned runtime
    // task. Running inline avoids deadlock and preserves one outer checkpoint.
    work(runtime());
    return;
  }

  isExecuting_ = true;
  std::exception_ptr exception;
  try {
    work(runtime());
  } catch (...) {
    exception = std::current_exception();
  }

  try {
    drainMicrotasks();
  } catch (...) {
    if (exception == nullptr) {
      exception = std::current_exception();
    }
  }

  isExecuting_ = false;
  if (exception != nullptr) {
    std::rethrow_exception(exception);
  }
}

void HermesConsoleRuntimeExecutor::drainMicrotasks()
{
  if (runtime_ != nullptr) {
    runtime_->drainMicrotasks(-1);
  }
}

void HermesConsoleRuntimeExecutor::releaseQueuedTasksLocked()
{
  for (auto &task : queue_) {
    if (task.syncResult != nullptr) {
      // Wake sync callers whose work was accepted but never ran. Async callers
      // are notified by destruction of their captured ScheduledTaskContext.
      {
        std::lock_guard<std::mutex> resultLock(task.syncResult->mutex);
        task.syncResult->cancelled = true;
        task.syncResult->finished = true;
      }
      task.syncResult->condition.notify_one();
    }
  }
  queue_.clear();
}

void HermesConsoleRuntimeExecutor::notifyIdleIfNeededLocked()
{
  if (queue_.empty() && activeTasks_ == 0) {
    idleChanged_.notify_all();
  }
}

HermesConsoleRuntimeConnector::HermesConsoleRuntimeConnector()
  : runtimeExecutor_(*this)
{
}

HermesConsoleRuntimeConnector::~HermesConsoleRuntimeConnector()
{
  invalidate();
}

facebook::jsi::Runtime &HermesConsoleRuntimeConnector::runtime()
{
  return runtimeExecutor_.runtime();
}

JsiRuntimeExecutor &HermesConsoleRuntimeConnector::runtimeExecutor()
{
  return runtimeExecutor_;
}

bool HermesConsoleRuntimeConnector::isRuntimeValid() const
{
  return runtimeExecutor_.isRuntimeValid();
}

void HermesConsoleRuntimeConnector::invalidate()
{
  runtimeExecutor_.shutdown();
}

} // namespace expo::jsi
