# Headless Runtime Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the headless Hermes manual-drain executor with a dedicated runtime loop thread that runs scheduled work, supports sync re-entry, and performs Hermes microtask checkpoints.

**Architecture:** `HermesConsoleRuntimeExecutor` becomes the headless runtime loop: it starts an executor thread, creates and destroys the Hermes runtime on that thread, owns the priority queue, and exposes `executeAsync`, `executeSync`, and `drain` through the existing `JsiRuntimeExecutor` interface. Testhost script evaluation and managed test setup must enter JSI through the runtime executor, preserving the portable rule that C# uses opaque handles and C++ owns JSI mechanics.

**Tech Stack:** C++20 threads, mutexes, condition variables, Hermes JSI, C ABI testhost exports, .NET 10 unsafe interop, xUnit v3, `scripts/test-jsi.sh`, `scripts/format.sh`.

---

## Success Criteria

- `ExecuteAsync` work runs on a dedicated executor thread without the test manually invoking each queued task.
- `Execute<T>` called outside the executor thread posts to the executor and waits.
- Nested `Execute<T>` from executor-owned work runs inline and does not deadlock.
- `DrainTasks()` / `WaitUntilIdle()` waits for all work queued before the call, including work queued by running work.
- Hermes microtasks run after script evaluation and after executor tasks.
- Runtime teardown releases queued tasks and faults pending managed tasks.
- Existing primitive/object/array/host-function/module tests still pass after routing setup through executor-owned runtime access where needed.
- `scripts/test-jsi.sh`, `scripts/format.sh --check --all`, and `git diff --check` pass.

## File Map

- Modify `native/packages/jsi/include/HermesConsoleRuntimeConnector.h`: replace the single-thread queue fields with runtime-loop thread, queue, lifecycle, and idle state.
- Modify `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`: implement thread startup, Hermes runtime creation/destruction on executor thread, async queueing, sync wait, idle wait, shutdown, and microtask checkpoints.
- Modify `native/packages/jsi/src/ExpoJsiBridge.cpp`: route `runtime_drain_tasks` to the new idle wait; avoid touching Hermes runtime from non-executor validation paths.
- Modify `native/testhost/include/expo_jsi_testhost.h`: add `expo_jsi_testhost_wait_until_idle`.
- Modify `native/testhost/src/ExpoJsiTestHost.cpp`: evaluate scripts through `runtimeExecutor().executeSync(...)`, expose wait-until-idle, preserve per-runtime counters.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`: bind the new wait export.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`: expose `WaitUntilIdle()` and keep `DrainTasks()` as compatibility alias.
- Modify `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`: change executor tests from manual-drain assumptions to runtime-loop assumptions.
- Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs`: prove Hermes promise microtasks are checkpointed by the host loop.
- Modify existing Hermes-backed tests that create/read JSI values directly from the xUnit thread, wrapping that work in `fixture.Runtime.Execute(...)` when the operation touches JSI.

## Task 1: Add Runtime-Loop Red Tests

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`

- [ ] **Step 1: Add fixture declarations for idle waiting**

Add these declarations to `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`:

```csharp
  private static delegate* unmanaged[Cdecl]<nint, ExpoJsiError> waitUntilIdle;

  internal static void WaitUntilIdle(nint testHostRuntime)
  {
    EnsureLoaded();
    var error = waitUntilIdle(testHostRuntime);
    if (error.Code != 0)
    {
      ThrowNativeError(error, "Failed to wait for Hermes runtime idle.");
    }
  }
```

Load the export in `EnsureLoaded()` after `drainTasks`:

```csharp
    waitUntilIdle =
      (delegate* unmanaged[Cdecl]<nint, ExpoJsiError>)LoadExport(
          library,
          "expo_jsi_testhost_wait_until_idle"
      );
```

Add these members to `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs` after `DrainTasks()`:

```csharp
  public void WaitUntilIdle()
  {
    NativeTestHost.WaitUntilIdle(testHostRuntime);
  }
```

Keep `DrainTasks()` for compatibility in this red step. It will remain as an alias later.

- [ ] **Step 2: Replace manual-drain executor assumptions with loop assumptions**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`, replace `ExecuteAsyncRunsOnlyAfterDrain` with:

```csharp
  [Fact]
  public async Task ExecuteAsyncRunsOnExecutorThreadWithoutManualDrain()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var callerThread = Environment.CurrentManagedThreadId;

    var executorThread = await fixture.Runtime.ExecuteAsync(
        js =>
        {
          using var value = js.CreateNumber(42);
          Assert.Equal(42, value.AsDouble());
          return Environment.CurrentManagedThreadId;
        },
        cancellationToken: TestContext.Current.CancellationToken
    ).WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.NotEqual(callerThread, executorThread);
  }
```

Add these tests to the same file:

```csharp
  [Fact]
  public void ExecuteFromOutsideRuntimeThreadPostsAndWaits()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var callerThread = Environment.CurrentManagedThreadId;

    var executorThread = fixture.Runtime.Execute(js =>
    {
      using var value = js.CreateNumber(7);
      Assert.Equal(7, value.AsDouble());
      return Environment.CurrentManagedThreadId;
    });

    Assert.NotEqual(callerThread, executorThread);
  }

  [Fact]
  public void NestedExecuteFromExecutorThreadRunsInline()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var result = fixture.Runtime.Execute(js =>
    {
      var outerThread = Environment.CurrentManagedThreadId;
      var innerThread = js.Execute(inner =>
      {
        using var value = inner.CreateBool(true);
        Assert.True(value.AsBool());
        return Environment.CurrentManagedThreadId;
      });

      return (outerThread, innerThread);
    });

    Assert.Equal(result.outerThread, result.innerThread);
  }

  [Fact]
  public async Task WaitUntilIdleIncludesWorkQueuedByRunningWork()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var outerRan = false;
    var innerRan = false;

    var outerTask = fixture.Runtime.ScheduleAsync(
        js =>
        {
          outerRan = true;
          _ = js.ScheduleAsync(
              inner =>
              {
                using var value = inner.CreateString("inner");
                Assert.Equal("inner", value.AsString());
                innerRan = true;
              },
              cancellationToken: TestContext.Current.CancellationToken
          );
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    fixture.WaitUntilIdle();

    await outerTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    Assert.True(outerRan);
    Assert.True(innerRan);
  }
```

Replace the existing tests below with these runtime-loop versions:

```csharp
  [Fact]
  public async Task ExecuteAsyncPropagatesManagedExceptions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ExecuteAsync<double>(
        _ =>
        {
          throw new InvalidOperationException("runtime body failed");
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal("runtime body failed", error.Message);
  }

  [Fact]
  public async Task ScheduleAsyncReturnsFaultedTaskWhenBodyThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          throw new InvalidOperationException("scheduled body failed");
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    var error = await Assert.ThrowsAsync<InvalidOperationException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal("scheduled body failed", error.Message);
  }

  [Fact]
  public async Task CancellationWhileQueuedSkipsBodyAndReleasesContext()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    using var blockerStarted = new ManualResetEventSlim(false);
    using var releaseBlocker = new ManualResetEventSlim(false);
    using var cts = new CancellationTokenSource();
    var ran = false;

    var blockerTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          blockerStarted.Set();
          if (!releaseBlocker.Wait(TimeSpan.FromSeconds(5)))
          {
            throw new TimeoutException("Timed out waiting to release executor blocker.");
          }
        },
        priority: JavaScriptTaskPriority.Immediate,
        cancellationToken: TestContext.Current.CancellationToken
    );

    Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(1)));

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          ran = true;
        },
        priority: JavaScriptTaskPriority.Idle,
        cancellationToken: cts.Token
    );

    cts.Cancel();
    releaseBlocker.Set();
    fixture.WaitUntilIdle();
    await blockerTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.False(ran);
    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
        async () => await task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal(2u, fixture.Counters.ReleasedTaskContexts);
  }

  [Fact]
  public async Task CancellationAfterRuntimeWorkStartsDoesNotInterruptBody()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var cts = new CancellationTokenSource();
    var bodyStarted = false;

    var task = fixture.Runtime.ExecuteAsync(
        js =>
        {
          bodyStarted = true;
          cts.Cancel();
          using var value = js.CreateString("finished");
          return value.AsString();
        },
        cancellationToken: cts.Token
    );

    var result = await task.WaitAsync(
        TimeSpan.FromSeconds(1),
        TestContext.Current.CancellationToken
    );

    Assert.True(bodyStarted);
    Assert.Equal("finished", result);
  }

  [Fact]
  public async Task ScheduledTaskContextIsReleasedExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    var task = fixture.Runtime.ScheduleAsync(
        js =>
        {
          using var value = js.CreateBool(true);
          Assert.True(value.AsBool());
        },
        cancellationToken: TestContext.Current.CancellationToken
    );

    fixture.WaitUntilIdle();

    await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
  }

  [Fact]
  public async Task PendingScheduledTaskFaultsWhenRuntimeIsDisposed()
  {
    var fixture = HermesRuntimeFixture.Create();
    using var blockerStarted = new ManualResetEventSlim(false);
    using var releaseBlocker = new ManualResetEventSlim(false);
    var pendingRan = false;

    var blockerTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          blockerStarted.Set();
          if (!releaseBlocker.Wait(TimeSpan.FromSeconds(5)))
          {
            throw new TimeoutException("Timed out waiting to release executor blocker.");
          }
        },
        priority: JavaScriptTaskPriority.Immediate,
        cancellationToken: TestContext.Current.CancellationToken
    );

    Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(1)));

    var pendingTask = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          pendingRan = true;
        },
        priority: JavaScriptTaskPriority.Idle,
        cancellationToken: TestContext.Current.CancellationToken
    );

    var releaseTask = Task.Run(async () =>
    {
      await Task.Delay(TimeSpan.FromMilliseconds(50));
      releaseBlocker.Set();
    });

    fixture.Dispose();
    await releaseTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    await blockerTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.False(pendingRan);
    var error = await Assert.ThrowsAsync<ObjectDisposedException>(
        async () => await pendingTask.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        )
    );
    Assert.Equal(nameof(JavaScriptRuntime), error.ObjectName);
  }
```

- [ ] **Step 3: Add microtask checkpoint tests with conservative JS**

Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptMicrotaskTests
{
  [Fact]
  public void PromiseThenRunsAfterScriptEvaluationCheckpoint()
  {
    using var fixture = HermesRuntimeFixture.Create();

    using (fixture.Evaluate(
        """
        globalThis.done = false;
        globalThis.promiseValue = 0;
        Promise.resolve(42).then(function (value) {
          globalThis.done = true;
          globalThis.promiseValue = value;
        });
        0;
        """,
        "promise-microtask.js"
    ))
    {
    }

    fixture.WaitUntilIdle();

    using var done = fixture.Evaluate("globalThis.done", "promise-microtask-done.js");
    using var value = fixture.Evaluate("globalThis.promiseValue", "promise-microtask-value.js");
    fixture.Runtime.Execute(_ =>
    {
      Assert.True(done.AsBool());
      Assert.Equal(42, value.AsDouble());
      return true;
    });
  }

}
```

- [ ] **Step 4: Run red tests**

Run:

```sh
scripts/test-jsi.sh --filter "JavaScriptRuntimeExecutorTests|JavaScriptMicrotaskTests"
```

Expected:

- build fails because `expo_jsi_testhost_wait_until_idle` is not exported; or
- tests time out/fail because `ExecuteAsync` does not run until manual drain and `Execute` still runs inline on the caller thread.

- [ ] **Step 5: Commit red tests**

```sh
git add managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs \
  managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs
git commit -m "test: define headless runtime loop behavior"
```

## Task 2: Implement Threaded Headless Runtime Loop

**Files:**
- Modify: `native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
- Modify: `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`

- [ ] **Step 1: Replace the executor declaration**

Replace `HermesConsoleRuntimeExecutor` in `native/packages/jsi/include/HermesConsoleRuntimeConnector.h` with:

```cpp
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
  mutable std::mutex mutex_;
  std::condition_variable workAvailable_;
  std::condition_variable idleChanged_;
  std::deque<QueuedTask> queue_;
  std::thread runtimeThread_;
  std::unique_ptr<facebook::jsi::Runtime> runtime_;
  std::thread::id runtimeThreadId_;
  uint64_t nextSequence_ = 0;
  uint32_t activeTasks_ = 0;
  bool isExecuting_ = false;
  State state_ = State::Created;
  std::exception_ptr startupException_;
};
```

Add includes:

```cpp
#include <condition_variable>
#include <exception>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <thread>
```

Remove `runtime_` from `HermesConsoleRuntimeConnector`; the executor now owns
the runtime so it can create and destroy it on the executor thread:

```cpp
  HermesConsoleRuntimeExecutor runtimeExecutor_;
```

- [ ] **Step 2: Implement runtime thread startup and shutdown**

In `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`, implement constructor/destructor behavior:

```cpp
HermesConsoleRuntimeExecutor::HermesConsoleRuntimeExecutor(
  HermesConsoleRuntimeConnector &connector)
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

void HermesConsoleRuntimeExecutor::threadMain()
{
  {
    std::lock_guard<std::mutex> lock(mutex_);
    runtimeThreadId_ = std::this_thread::get_id();
  }

  try {
    auto runtime = facebook::hermes::makeHermesRuntime();
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
        workAvailable_.wait(lock, [this]() {
          return state_ == State::Stopping || !queue_.empty();
        });

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
        activeTasks_++;
      }

      try {
        runTask(std::move(task.work));
      } catch (...) {
        // Async task failures are reported through their managed callback task.
        // Microtask failures during explicit idle waits are surfaced by tests through executeSync.
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

void HermesConsoleRuntimeExecutor::shutdown() noexcept
{
  {
    std::lock_guard<std::mutex> lock(mutex_);
    if (state_ == State::Stopped) {
      // Continue to join below.
    } else {
      state_ = State::Stopping;
      releaseQueuedTasksLocked();
    }
  }
  workAvailable_.notify_all();
  idleChanged_.notify_all();

  if (runtimeThread_.joinable()) {
    runtimeThread_.join();
  }
}
```

- [ ] **Step 3: Implement runtime access helpers**

Add:

```cpp
facebook::jsi::Runtime &HermesConsoleRuntimeExecutor::runtime()
{
  if (!isOnRuntimeThread()) {
    throw std::runtime_error("Hermes runtime access is not on the executor thread.");
  }
  if (!runtime_) {
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
  return runtimeThreadId_ != std::thread::id{} &&
         std::this_thread::get_id() == runtimeThreadId_;
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

void HermesConsoleRuntimeExecutor::runTask(
  std::function<void(facebook::jsi::Runtime &)> work)
{
  if (isExecuting_) {
    work(runtime());
    return;
  }

  isExecuting_ = true;
  try {
    work(runtime());
    drainMicrotasks();
    isExecuting_ = false;
  } catch (...) {
    drainMicrotasks();
    isExecuting_ = false;
    throw;
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
```

- [ ] **Step 4: Implement async, sync, and idle wait**

Add:

```cpp
void HermesConsoleRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority priority,
  std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  try {
    {
      std::lock_guard<std::mutex> lock(mutex_);
      if (state_ != State::Running) {
        return;
      }
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

void HermesConsoleRuntimeExecutor::executeSync(
  std::function<void(facebook::jsi::Runtime &)> work)
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
      [work = std::move(work), result](facebook::jsi::Runtime &runtime) mutable {
        try {
          work(runtime);
        } catch (...) {
          result->exception = std::current_exception();
        }
        {
          std::lock_guard<std::mutex> resultLock(result->mutex);
          result->finished = true;
        }
        result->condition.notify_one();
      },
      result,
    });
  }
  workAvailable_.notify_one();

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
  idleChanged_.wait(lock, [this]() {
    return state_ != State::Running || (queue_.empty() && activeTasks_ == 0);
  });
}
```

This first version intentionally treats `drain()` as `waitUntilIdle()`. It does
not manually execute tasks on the caller thread.

- [ ] **Step 5: Update connector methods**

In the same `.cpp`, connector methods become:

```cpp
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
```

- [ ] **Step 6: Avoid non-executor runtime validation in ABI scheduling paths**

In `native/packages/jsi/src/ExpoJsiBridge.cpp`, add this method to `RuntimeHandle`:

```cpp
  bool isRuntimeValid() const
  {
    return connector_ != nullptr && connector_->isRuntimeValid();
  }
```

Add this helper near `tryRuntimeHandle`:

```cpp
expo::jsi::RuntimeHandle *tryRuntimeHandleWithoutAccess(
  expo_jsi_runtime_handle runtime,
  expo_jsi_error *error)
{
  auto *handle = runtime;
  if (handle == nullptr) {
    writeError(error, 1, "Runtime handle is null.");
    return nullptr;
  }
  if (!handle->isRuntimeValid()) {
    writeError(error, 2, "Runtime connector is invalid.");
    return nullptr;
  }
  return handle;
}
```

Change `scheduleTask`, `canExecuteSync`, `executeSync`, and `drainTasks` to call
`tryRuntimeHandleWithoutAccess(...)` instead of `tryRuntimeHandle(...)`.

Do not change JSI value/object/array/function APIs in this step; those APIs
should still require actual runtime access and therefore continue using
`tryRuntimeHandle(...)`.

- [ ] **Step 7: Run focused tests**

Run:

```sh
scripts/test-jsi.sh --filter "JavaScriptRuntimeExecutorTests|JavaScriptMicrotaskTests"
```

Expected at this point:

- executor tests may pass;
- microtask tests may still fail until testhost script evaluation is routed
  through `executeSync`;
- existing full suite may fail because some tests still touch JSI from the
  xUnit thread.

- [ ] **Step 8: Commit native runtime loop**

```sh
git add native/packages/jsi/include/HermesConsoleRuntimeConnector.h \
  native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp \
  native/packages/jsi/src/ExpoJsiBridge.cpp
git commit -m "feat: run headless hermes work on executor thread"
```

## Task 3: Route Testhost Through Runtime Executor

**Files:**
- Modify: `native/testhost/include/expo_jsi_testhost.h`
- Modify: `native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`

- [ ] **Step 1: Add wait export to C header**

Add to `native/testhost/include/expo_jsi_testhost.h` after `expo_jsi_testhost_drain_tasks`:

```c
expo_jsi_error expo_jsi_testhost_wait_until_idle(
  expo_jsi_testhost_runtime_handle testhost_runtime);
```

- [ ] **Step 2: Route script evaluation through `executeSync`**

Replace the body of `expo_jsi_testhost_evaluate_script` in
`native/testhost/src/ExpoJsiTestHost.cpp` after argument validation with:

```cpp
  expo_jsi_value_result result{};
  try {
    auto script =
      std::string(reinterpret_cast<const char *>(source), static_cast<size_t>(sourceLength));
    auto url = sourceUrl == nullptr || sourceUrlLength == 0
                 ? std::string("expo-jsi-test.js")
                 : std::string(reinterpret_cast<const char *>(sourceUrl),
                               static_cast<size_t>(sourceUrlLength));

    testhost->connector.runtimeExecutor().executeSync(
      [&](facebook::jsi::Runtime &runtime) {
        auto value =
          runtime.evaluateJavaScript(std::make_unique<facebook::jsi::StringBuffer>(script), url);
        result = expo_jsi_value_result{
          1,
          expo::jsi::createOwnedValueHandle(std::move(value)),
          expo_jsi_error{0, nullptr, 0},
        };
      });
    return result;
  } catch (const facebook::jsi::JSError &error) {
    return makeErrorResult(6, error.what());
  } catch (const std::exception &error) {
    return makeErrorResult(7, error.what());
  } catch (...) {
    return makeErrorResult(8, "Unknown native exception while evaluating script.");
  }
```

- [ ] **Step 3: Add wait-until-idle export**

Add to `native/testhost/src/ExpoJsiTestHost.cpp` after `expo_jsi_testhost_drain_tasks`:

```cpp
extern "C" expo_jsi_error expo_jsi_testhost_wait_until_idle(
  expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr) {
    return makeError(9, "Testhost runtime is null.");
  }
  return testhost->innerApi->runtime_drain_tasks(testhost->runtime);
}
```

Update `expo_jsi_testhost_drain_tasks` to call the new helper and preserve
current void behavior:

```cpp
extern "C" void expo_jsi_testhost_drain_tasks(expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto error = expo_jsi_testhost_wait_until_idle(testhostRuntime);
  if (error.code != 0) {
    lastErrorMessage = error.message != nullptr
                         ? std::string(error.message, static_cast<size_t>(error.message_len))
                         : "Failed to wait for Hermes runtime idle.";
  }
}
```

- [ ] **Step 4: Bind managed wait helper**

Finish the `NativeTestHost.WaitUntilIdle(...)` and `HermesRuntimeFixture.WaitUntilIdle()`
declarations added in Task 1.

Update `HermesRuntimeFixture.DrainTasks()` to call `WaitUntilIdle()`:

```csharp
  public void DrainTasks()
  {
    WaitUntilIdle();
  }

  public void WaitUntilIdle()
  {
    NativeTestHost.WaitUntilIdle(testHostRuntime);
  }
```

- [ ] **Step 5: Run focused tests**

Run:

```sh
scripts/test-jsi.sh --filter "JavaScriptRuntimeExecutorTests|JavaScriptMicrotaskTests"
```

Expected: focused runtime-loop and microtask tests pass. The microtask test
proves the script-evaluation task checkpoint. A later function-call or promise
slice can add a microtask test initiated by a retained JS function call.

- [ ] **Step 6: Commit testhost runtime-loop routing**

```sh
git add native/testhost/include/expo_jsi_testhost.h \
  native/testhost/src/ExpoJsiTestHost.cpp \
  managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs \
  managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs
git commit -m "test: route hermes testhost through runtime loop"
```

## Task 4: Convert Existing Tests To Runtime-Scoped JSI Access

**Files:**
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`

- [ ] **Step 1: Find direct JSI runtime calls from tests**

Run:

```sh
rg -n "fixture\\.Runtime\\.(Create|Global|ExecuteAsync|ScheduleAsync)|fixture\\.Evaluate|\\.As(Double|Bool|String|Object|Array|Function)\\(" managed/packages/Expo.JSI.Tests -S
```

Expected: matches in these files:

```text
managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptMicrotaskTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueTests.cs
managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs
managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs
managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs
```

- [ ] **Step 2: Convert setup and assertions that touch JSI into `Runtime.Execute`**

For tests that create/read JSI values directly from xUnit thread, wrap the
whole JSI interaction in `fixture.Runtime.Execute(...)`.

Use this pattern:

```csharp
[Fact]
public void CreateNumberRoundTrips()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var value = runtime.CreateNumber(42.5);
    Assert.Equal(JavaScriptValueKind.Number, value.Kind);
    Assert.Equal(42.5, value.AsDouble());
    return true;
  });
}
```

For tests that need to evaluate JS and then inspect the result, either keep
the full interaction inside one `Execute` body if all handles are created
there, or use `fixture.Evaluate(...)` followed by `fixture.Runtime.Execute(...)`
only for handle inspection:

```csharp
using var value = fixture.Evaluate("21 + 21", "value.js");
fixture.Runtime.Execute(_ =>
{
  Assert.Equal(JavaScriptValueKind.Number, value.Kind);
  Assert.Equal(42, value.AsDouble());
  return true;
});
```

Host function callbacks do not need wrapping inside the callback body because
they are already invoked with runtime access.

- [ ] **Step 3: Convert host function installation setup**

For tests that install host functions before `Evaluate`, wrap object/function
creation and property setup:

```csharp
fixture.Runtime.Execute(runtime =>
{
  using var global = runtime.Global();
  using var function = runtime.CreateHostFunction(
      "addOne",
      1,
      (callbackRuntime, thisValue, arguments, context) =>
      {
        var input = arguments.GetBorrowedValue(0);
        return callbackRuntime.CreateNumber(input.AsDouble() + 1);
      },
      new object()
  );
  using var functionValue = function.AsValue();
  global.SetProperty("addOne", functionValue);
  return true;
});
```

Then evaluate normally:

```csharp
using var result = fixture.Evaluate("globalThis.addOne(41.5)", "host-function-success.js");
fixture.Runtime.Execute(_ =>
{
  Assert.Equal(JavaScriptValueKind.Number, result.Kind);
  Assert.Equal(42.5, result.AsDouble());
  return true;
});
```

- [ ] **Step 4: Keep array conversion module setup scoped**

In `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`, wrap
module installation in `Runtime.Execute(...)` and keep callback bodies unchanged.

Use this shape:

```csharp
fixture.Runtime.Execute(runtime =>
{
  using var global = runtime.Global();
  using var expo = runtime.CreateObject();
  using var modules = runtime.CreateObject();
  using var array = runtime.CreateObject();
  using var sum = runtime.CreateHostFunction("sum", 1, SumHostFunction, module);
  using var labels = runtime.CreateHostFunction("labels", 0, LabelsHostFunction, module);
  using var sumValue = sum.AsValue();
  using var labelsValue = labels.AsValue();
  array.SetProperty("sum", sumValue);
  array.SetProperty("labels", labelsValue);
  using var arrayValue = array.AsValue();
  modules.SetProperty("Array", arrayValue);
  using var modulesValue = modules.AsValue();
  expo.SetProperty("modules", modulesValue);
  using var expoValue = expo.AsValue();
  global.SetProperty("expo", expoValue);
  return true;
});
```

- [ ] **Step 5: Run full test suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all tests pass. If failures report "Hermes runtime access is not on
the executor thread", wrap the failing JSI access in `Runtime.Execute(...)`
rather than weakening the native runtime-thread check.

- [ ] **Step 6: Commit test conversions**

```sh
git add managed/packages/Expo.JSI.Tests/Runtime \
  managed/packages/Expo.JSI.Tests/HostFunctions \
  managed/packages/Expo.JSI.Tests/Modules
git commit -m "test: scope hermes jsi access to runtime executor"
```

## Task 5: Verification And Cleanup

**Files:**
- Formatter-managed files reported by `scripts/format.sh`; no predetermined file list.

- [ ] **Step 1: Run the focused runtime-loop tests**

Run:

```sh
scripts/test-jsi.sh --filter "JavaScriptRuntimeExecutorTests|JavaScriptMicrotaskTests"
```

Expected: runtime-loop and microtask tests pass.

- [ ] **Step 2: Run full Hermes-backed suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all tests pass, including array conversion tests from the current branch.

- [ ] **Step 3: Run format check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: exits 0. If it reports formatting changes, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

Expected after formatting: exits 0.

- [ ] **Step 4: Run whitespace check**

Run:

```sh
git diff --check
```

Expected: no output.

- [ ] **Step 5: Commit formatter cleanup when formatter changed files**

If `scripts/format.sh` changed files:

```sh
git add native managed
git commit -m "style: format headless runtime loop changes"
```

If no files changed, do not create an empty commit.

## Implementation Notes

- Do not create a git worktree; this repo's local instructions prohibit worktrees unless the user explicitly asks.
- Keep `JsiRuntimeExecutor` host-neutral. Do not include React Native headers.
- Keep `CanExecuteSync` passive. Do not probe sync support by calling sync execution.
- Do not add promise handles in this slice.
- Do not add timers or `setTimeout` test dependencies in this slice.
- Use conservative JavaScript in new snippets. Prefer `function (...) { ... }` and `var` over modern syntax.
- Treat thread-affinity failures as useful. Fix tests or call sites to enter through the executor rather than making `runtime()` silently cross-thread again.

## Self-Review Checklist

- Spec coverage: Tasks 1-3 implement the executor thread, idle wait, sync behavior, shutdown, and microtask checkpoints; Task 4 updates existing tests to respect runtime access; Task 5 verifies the full branch.
- Placeholder scan: no placeholders or open-ended error-handling tasks remain.
- Type consistency: public names are `WaitUntilIdle`, `DrainTasks`, `HermesConsoleRuntimeExecutor`, and existing `JsiRuntimeExecutor` methods.
- Scope check: promise handles, timers, generated async modules, and hosted adapters remain out of scope.
