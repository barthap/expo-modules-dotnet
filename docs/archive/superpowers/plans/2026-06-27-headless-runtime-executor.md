# Headless Runtime Executor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a runtime-access executor for the headless Hermes bridge and expose C# `ScheduleAsync`, `ExecuteAsync<T>`, and `Execute<T>` APIs.

**Architecture:** C++ continues to own all JSI mechanics and grants scoped access to `facebook::jsi::Runtime` through a runtime-bound executor. The C ABI carries only opaque runtime handles, callback function pointers, callback context pointers, and structured errors. C# owns the `Task` surface and managed callback trampoline, while native owns when the callback may touch JSI.

**Tech Stack:** C++20, Hermes JSI, C ABI function table, .NET 10 unsafe interop, xUnit v3, repository scripts `scripts/test-jsi.sh` and `scripts/format.sh`.

---

## Success Criteria

- `JavaScriptRuntime.ScheduleAsync(...)` returns a `Task` that completes only after the headless executor drains.
- `JavaScriptRuntime.ExecuteAsync<T>(...)` returns values produced through real Hermes JSI and faults when the body throws.
- `JavaScriptRuntime.Execute<T>(...)` works on the headless executor and throws before invoking the body when the adapter reports no sync support.
- Queued runtime task contexts are released exactly once.
- Cancellation is best-effort: pre-schedule cancellation returns a canceled task, queued cancellation skips the body, and cancellation after the body starts does not interrupt it.
- Existing synchronous host function tests still pass without scheduling.
- `scripts/test-jsi.sh` and `scripts/format.sh --check --all` pass.

## File Map

- Modify `native/include/expo_jsi.h`: add task priority enum, task callback typedefs, runtime executor function pointer typedefs, and append executor functions to `expo_jsi_api`.
- Modify `native/packages/jsi/include/JsiRuntimeConnector.h`: replace `JsiScheduler` with `JsiRuntimeExecutor` and expose `runtimeExecutor()`.
- Modify `native/packages/jsi/include/HermesConsoleRuntimeConnector.h`: add the headless queued executor owned by the Hermes connector.
- Modify `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`: implement deterministic queueing, sync execution, and drain.
- Modify `native/packages/jsi/src/ExpoJsiBridge.cpp`: bump API version to 5 and implement ABI wrappers for schedule, sync capability, sync execution, and drain.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`: add `ExpoJsiTaskPriority`.
- Modify `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: add function pointers and managed wrappers for executor ABI functions.
- Modify `managed/packages/Expo.JSI/JavaScriptRuntime.cs`: add `CanExecuteSync`, `ScheduleAsync`, `ExecuteAsync<T>`, `Execute<T>`, and unmanaged task trampolines.
- Modify `native/testhost/include/expo_jsi_testhost.h`: add task-context release and sync-call counters plus drain/sync-toggle exports.
- Modify `native/testhost/src/ExpoJsiTestHost.cpp`: count task context releases, expose drain, and allow tests to disable sync capability.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`: bind new testhost exports and counter fields.
- Modify `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`: expose `DrainTasks()` and `DisableSyncExecutionForTesting()`.
- Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`: runtime executor behavior tests.

## Task 1: Add Managed Runtime Executor Tests

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`

- [ ] **Step 1: Add the failing test file**

Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs`:

```csharp
using System.Threading.Tasks;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptRuntimeExecutorTests
{
  [Fact]
  public async Task ExecuteAsyncRunsOnlyAfterDrain()
  {
    using var fixture = HermesRuntimeFixture.Create();
    var ran = false;

    var task = fixture.Runtime.ExecuteAsync(js =>
    {
      ran = true;
      using var value = js.CreateNumber(42);
      return value.AsDouble();
    });

    Assert.False(ran);
    Assert.False(task.IsCompleted);

    fixture.DrainTasks();

    Assert.True(ran);
    Assert.Equal(42, await task);
  }

  [Fact]
  public async Task ExecuteAsyncPropagatesManagedExceptions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ExecuteAsync<double>(_ =>
    {
      throw new InvalidOperationException("runtime body failed");
    });

    fixture.DrainTasks();

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    Assert.Equal("runtime body failed", error.Message);
  }

  [Fact]
  public async Task ScheduleAsyncReturnsFaultedTaskWhenBodyThrows()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var task = fixture.Runtime.ScheduleAsync(_ =>
    {
      throw new InvalidOperationException("scheduled body failed");
    });

    fixture.DrainTasks();

    var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task);
    Assert.Equal("scheduled body failed", error.Message);
  }

  [Fact]
  public async Task CancellationBeforeSchedulingReturnsCanceledTask()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    var task = fixture.Runtime.ExecuteAsync(
        js =>
        {
          using var value = js.CreateNumber(1);
          return value.AsDouble();
        },
        cancellationToken: cts.Token
    );

    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
  }

  [Fact]
  public async Task CancellationWhileQueuedSkipsBodyAndReleasesContext()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();
    using var cts = new CancellationTokenSource();
    var ran = false;

    var task = fixture.Runtime.ScheduleAsync(
        _ =>
        {
          ran = true;
        },
        cancellationToken: cts.Token
    );

    cts.Cancel();
    fixture.DrainTasks();

    Assert.False(ran);
    Assert.True(task.IsCanceled);
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
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

    fixture.DrainTasks();

    Assert.True(bodyStarted);
    Assert.Equal("finished", await task);
  }

  [Fact]
  public void ExecuteRunsSynchronouslyOnHeadlessRuntime()
  {
    using var fixture = HermesRuntimeFixture.Create();

    var result = fixture.Runtime.Execute(js =>
    {
      using var value = js.CreateNumber(7);
      return value.AsDouble();
    });

    Assert.True(fixture.Runtime.CanExecuteSync);
    Assert.Equal(7, result);
  }

  [Fact]
  public void ExecuteThrowsBeforeBodyWhenSyncUnsupported()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.DisableSyncExecutionForTesting();
    var ran = false;

    var error = Assert.Throws<NotSupportedException>(() =>
      fixture.Runtime.Execute(js =>
      {
        ran = true;
        using var value = js.CreateNumber(1);
        return value.AsDouble();
      }));

    Assert.False(ran);
    Assert.Equal(0u, fixture.Counters.SyncExecuteCalls);
    Assert.Contains("Synchronous JavaScript runtime execution is not supported", error.Message);
  }

  [Fact]
  public async Task ScheduledTaskContextIsReleasedExactlyOnce()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    var task = fixture.Runtime.ScheduleAsync(js =>
    {
      using var value = js.CreateBool(true);
      Assert.True(value.AsBool());
    });

    fixture.DrainTasks();

    await task;
    Assert.Equal(1u, fixture.Counters.ReleasedTaskContexts);
  }
}
```

- [ ] **Step 2: Add fixture method declarations that intentionally fail to compile**

Add these members to `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs` after `ResetCounters()`:

```csharp
  public void DrainTasks()
  {
    NativeTestHost.DrainTasks(testHostRuntime);
  }

  public void DisableSyncExecutionForTesting()
  {
    NativeTestHost.SetSyncExecutionSupported(testHostRuntime, false);
  }
```

Extend `NativeTestHost.Counters` in `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`:

```csharp
  internal readonly struct Counters
  {
    public readonly uint ReleasedValues;
    public readonly uint ReleasedObjects;
    public readonly uint ReleasedFunctions;
    public readonly uint ReleasedStrings;
    public readonly uint ReleasedTaskContexts;
    public readonly uint SyncExecuteCalls;
  }
```

Add failing method declarations to `NativeTestHost`:

```csharp
  private static delegate* unmanaged[Cdecl]<nint, void> drainTasks;
  private static delegate* unmanaged[Cdecl]<nint, byte, void> setSyncExecutionSupported;

  internal static void DrainTasks(nint testHostRuntime)
  {
    EnsureLoaded();
    drainTasks(testHostRuntime);
  }

  internal static void SetSyncExecutionSupported(nint testHostRuntime, bool supported)
  {
    EnsureLoaded();
    setSyncExecutionSupported(testHostRuntime, supported ? (byte)1 : (byte)0);
  }
```

Load the exports in `EnsureLoaded()`:

```csharp
    drainTasks =
      (delegate* unmanaged[Cdecl]<nint, void>)LoadExport(
          library,
          "expo_jsi_testhost_drain_tasks"
      );
    setSyncExecutionSupported =
      (delegate* unmanaged[Cdecl]<nint, byte, void>)LoadExport(
          library,
          "expo_jsi_testhost_set_sync_execution_supported"
      );
```

- [ ] **Step 3: Run the focused test command and verify failure**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptRuntimeExecutorTests
```

Expected: build fails because `JavaScriptRuntime` does not contain `ExecuteAsync`, `ScheduleAsync`, `Execute`, or `CanExecuteSync`, and native testhost exports do not exist.

- [ ] **Step 4: Commit the failing tests**

```sh
git add managed/packages/Expo.JSI.Tests/Runtime/JavaScriptRuntimeExecutorTests.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs
git commit -m "test: define runtime executor behavior"
```

## Task 2: Implement Native Runtime Executor ABI

**Files:**
- Modify: `native/include/expo_jsi.h`
- Modify: `native/packages/jsi/include/JsiRuntimeConnector.h`
- Modify: `native/packages/jsi/include/HermesConsoleRuntimeConnector.h`
- Modify: `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`

- [ ] **Step 1: Extend `native/include/expo_jsi.h`**

Insert after `expo_jsi_value_kind`:

```c
typedef enum expo_jsi_task_priority {
  EXPO_JSI_TASK_IMMEDIATE = 1,
  EXPO_JSI_TASK_USER_BLOCKING = 2,
  EXPO_JSI_TASK_NORMAL = 3,
  EXPO_JSI_TASK_LOW = 4,
  EXPO_JSI_TASK_IDLE = 5
} expo_jsi_task_priority;
```

Insert after `expo_jsi_release_callback_context_fn`:

```c
typedef void (*expo_jsi_task_callback_fn)(void *task_context);

typedef void (*expo_jsi_release_task_context_fn)(void *task_context);
```

Insert after `expo_jsi_release_function_fn`:

```c
typedef expo_jsi_error (*expo_jsi_runtime_schedule_task_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_priority priority,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef uint8_t (*expo_jsi_runtime_can_execute_sync_fn)(expo_jsi_runtime_handle runtime);

typedef expo_jsi_error (*expo_jsi_runtime_execute_sync_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef expo_jsi_error (*expo_jsi_runtime_drain_tasks_fn)(expo_jsi_runtime_handle runtime);
```

Append these fields to `expo_jsi_api` after `get_string`:

```c
  expo_jsi_runtime_schedule_task_fn runtime_schedule_task;
  expo_jsi_runtime_can_execute_sync_fn runtime_can_execute_sync;
  expo_jsi_runtime_execute_sync_fn runtime_execute_sync;
  expo_jsi_runtime_drain_tasks_fn runtime_drain_tasks;
```

- [ ] **Step 2: Replace the connector scheduler shape**

Replace the contents of `native/packages/jsi/include/JsiRuntimeConnector.h` with:

```cpp
#pragma once

#include <functional>

#include <jsi/jsi.h>

namespace expo::jsi {

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

  virtual void executeAsync(
    JsiRuntimeTaskPriority priority,
    std::function<void(facebook::jsi::Runtime &)> work) noexcept = 0;

  virtual bool canExecuteSync() const noexcept = 0;

  virtual void executeSync(std::function<void(facebook::jsi::Runtime &)> work) = 0;

  virtual void drain() = 0;
};

class JsiRuntimeConnector {
public:
  virtual ~JsiRuntimeConnector() = default;

  virtual facebook::jsi::Runtime &runtime() = 0;
  virtual JsiRuntimeExecutor &runtimeExecutor() = 0;
  virtual bool isRuntimeValid() const = 0;
  virtual void invalidate() = 0;
};

} // namespace expo::jsi
```

- [ ] **Step 3: Add the headless executor class declaration**

Replace `native/packages/jsi/include/HermesConsoleRuntimeConnector.h` with:

```cpp
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

  void executeAsync(
    JsiRuntimeTaskPriority priority,
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
```

- [ ] **Step 4: Implement the headless executor**

Replace `native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp` with:

```cpp
#include "HermesConsoleRuntimeConnector.h"

#include <algorithm>
#include <stdexcept>
#include <utility>

namespace expo::jsi {

HermesConsoleRuntimeExecutor::HermesConsoleRuntimeExecutor(
  HermesConsoleRuntimeConnector &connector)
  : connector_(&connector)
{
}

void HermesConsoleRuntimeExecutor::executeAsync(
  JsiRuntimeTaskPriority priority,
  std::function<void(facebook::jsi::Runtime &)> work) noexcept
{
  queue_.push_back(QueuedTask{priority, nextSequence_++, std::move(work)});
}

bool HermesConsoleRuntimeExecutor::canExecuteSync() const noexcept
{
  return true;
}

void HermesConsoleRuntimeExecutor::executeSync(
  std::function<void(facebook::jsi::Runtime &)> work)
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
```

- [ ] **Step 5: Add executor access to the runtime handle**

In `native/packages/jsi/src/ExpoJsiBridge.cpp`, add this method to `RuntimeHandle` after `runtime()`:

```cpp
  JsiRuntimeExecutor &runtimeExecutor()
  {
    if (connector_ == nullptr || !connector_->isRuntimeValid()) {
      throw std::runtime_error("Runtime connector is invalid.");
    }
    return connector_->runtimeExecutor();
  }
```

Change:

```cpp
constexpr uint32_t kApiVersion = 4;
```

to:

```cpp
constexpr uint32_t kApiVersion = 5;
```

- [ ] **Step 6: Implement ABI executor functions**

Add these helper functions near the other anonymous-namespace helpers in `ExpoJsiBridge.cpp`:

```cpp
expo_jsi_error makeOk()
{
  return expo_jsi_error{0, nullptr, 0};
}

expo::jsi::JsiRuntimeTaskPriority toRuntimeTaskPriority(expo_jsi_task_priority priority)
{
  switch (priority) {
    case EXPO_JSI_TASK_IMMEDIATE:
      return expo::jsi::JsiRuntimeTaskPriority::Immediate;
    case EXPO_JSI_TASK_USER_BLOCKING:
      return expo::jsi::JsiRuntimeTaskPriority::UserBlocking;
    case EXPO_JSI_TASK_LOW:
      return expo::jsi::JsiRuntimeTaskPriority::Low;
    case EXPO_JSI_TASK_IDLE:
      return expo::jsi::JsiRuntimeTaskPriority::Idle;
    case EXPO_JSI_TASK_NORMAL:
    default:
      return expo::jsi::JsiRuntimeTaskPriority::Normal;
  }
}
```

Add these functions before `const expo_jsi_api kApi`:

```cpp
expo_jsi_error scheduleTask(expo_jsi_runtime_handle runtime,
                            expo_jsi_task_priority priority,
                            expo_jsi_task_callback_fn callback,
                            void *taskContext,
                            expo_jsi_release_task_context_fn releaseTaskContext)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }
  if (callback == nullptr) {
    return makeError(38, "Task callback is null.");
  }

  try {
    runtimeHandle->runtimeExecutor().executeAsync(
      toRuntimeTaskPriority(priority),
      [callback, taskContext, releaseTaskContext](facebook::jsi::Runtime &) {
        callback(taskContext);
        if (releaseTaskContext != nullptr) {
          releaseTaskContext(taskContext);
        }
      });
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(39, ex.what());
  } catch (...) {
    return makeError(40, "Unknown native exception while scheduling runtime task.");
  }
}

uint8_t canExecuteSync(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return 0;
  }
  return runtimeHandle->runtimeExecutor().canExecuteSync() ? 1 : 0;
}

expo_jsi_error executeSync(expo_jsi_runtime_handle runtime,
                           expo_jsi_task_callback_fn callback,
                           void *taskContext,
                           expo_jsi_release_task_context_fn releaseTaskContext)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }
  if (callback == nullptr) {
    return makeError(41, "Task callback is null.");
  }
  if (!runtimeHandle->runtimeExecutor().canExecuteSync()) {
    return makeError(42, "Synchronous runtime execution is not supported.");
  }

  try {
    runtimeHandle->runtimeExecutor().executeSync(
      [callback, taskContext, releaseTaskContext](facebook::jsi::Runtime &) {
        callback(taskContext);
        if (releaseTaskContext != nullptr) {
          releaseTaskContext(taskContext);
        }
      });
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(43, ex.what());
  } catch (...) {
    return makeError(44, "Unknown native exception while executing runtime task.");
  }
}

expo_jsi_error drainTasks(expo_jsi_runtime_handle runtime)
{
  expo_jsi_error error{};
  auto *runtimeHandle = tryRuntimeHandle(runtime, &error);
  if (runtimeHandle == nullptr) {
    return error;
  }

  try {
    runtimeHandle->runtimeExecutor().drain();
    return makeOk();
  } catch (const std::exception &ex) {
    return makeError(45, ex.what());
  } catch (...) {
    return makeError(46, "Unknown native exception while draining runtime tasks.");
  }
}
```

Append the functions to `kApi`:

```cpp
  createString,
  getString,
  scheduleTask,
  canExecuteSync,
  executeSync,
  drainTasks,
```

- [ ] **Step 7: Run focused native build through the test script**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptRuntimeExecutorTests
```

Expected: native code builds farther than Task 1, then managed build still fails because `ExpoJsiApi` and `JavaScriptRuntime` do not expose the new ABI yet.

- [ ] **Step 8: Commit native executor ABI**

```sh
git add native/include/expo_jsi.h \
  native/packages/jsi/include/JsiRuntimeConnector.h \
  native/packages/jsi/include/HermesConsoleRuntimeConnector.h \
  native/packages/jsi/src/HermesConsoleRuntimeConnector.cpp \
  native/packages/jsi/src/ExpoJsiBridge.cpp
git commit -m "feat: add native runtime executor abi"
```

## Task 3: Implement Managed Task API

**Files:**
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`

- [ ] **Step 1: Add interop task priority**

Add to `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs` after `ExpoJsiValueKind`:

```csharp
internal enum ExpoJsiTaskPriority : int
{
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
}
```

- [ ] **Step 2: Add public task priority**

Add to `managed/packages/Expo.JSI/JavaScriptRuntime.cs` before `JavaScriptRuntime`:

```csharp
public enum JavaScriptTaskPriority
{
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
}
```

- [ ] **Step 3: Extend the API table**

Add these fields to `ExpoJsiApi` after `GetString`:

```csharp
  private readonly delegate* unmanaged[Cdecl]<
      ExpoJsiRuntimeHandle,
      ExpoJsiTaskPriority,
      delegate* unmanaged[Cdecl]<nint, void>,
      nint,
      delegate* unmanaged[Cdecl]<nint, void>,
      ExpoJsiError> RuntimeScheduleTask;

  private readonly delegate* unmanaged[Cdecl]<
      ExpoJsiRuntimeHandle,
      byte> RuntimeCanExecuteSync;

  private readonly delegate* unmanaged[Cdecl]<
      ExpoJsiRuntimeHandle,
      delegate* unmanaged[Cdecl]<nint, void>,
      nint,
      delegate* unmanaged[Cdecl]<nint, void>,
      ExpoJsiError> RuntimeExecuteSync;

  private readonly delegate* unmanaged[Cdecl]<
      ExpoJsiRuntimeHandle,
      ExpoJsiError> RuntimeDrainTasks;
```

Add these fields to the required-function validation:

```csharp
        || this.RuntimeScheduleTask is null
        || this.RuntimeCanExecuteSync is null
        || this.RuntimeExecuteSync is null
        || this.RuntimeDrainTasks is null
```

Change:

```csharp
  public const uint ExpectedVersion = 4;
```

to:

```csharp
  public const uint ExpectedVersion = 5;
```

- [ ] **Step 4: Add managed wrappers for executor functions**

Add these methods to `ExpoJsiApi` before `ExpectedSize`:

```csharp
  public ExpoJsiError ScheduleRuntimeTask(
      ExpoJsiRuntimeHandle runtimeHandle,
      ExpoJsiTaskPriority priority,
      delegate* unmanaged[Cdecl]<nint, void> callback,
      nint taskContext,
      delegate* unmanaged[Cdecl]<nint, void> releaseTaskContext
  )
  {
    return RuntimeScheduleTask(runtimeHandle, priority, callback, taskContext, releaseTaskContext);
  }

  public bool CanExecuteSync(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return RuntimeCanExecuteSync(runtimeHandle) != 0;
  }

  public ExpoJsiError ExecuteRuntimeTaskSync(
      ExpoJsiRuntimeHandle runtimeHandle,
      delegate* unmanaged[Cdecl]<nint, void> callback,
      nint taskContext,
      delegate* unmanaged[Cdecl]<nint, void> releaseTaskContext
  )
  {
    return RuntimeExecuteSync(runtimeHandle, callback, taskContext, releaseTaskContext);
  }

  public ExpoJsiError DrainRuntimeTasks(ExpoJsiRuntimeHandle runtimeHandle)
  {
    return RuntimeDrainTasks(runtimeHandle);
  }
```

- [ ] **Step 5: Add runtime scheduling methods**

Add `using System.Threading.Tasks;` to `managed/packages/Expo.JSI/JavaScriptRuntime.cs`.

Add this property to `JavaScriptRuntime` after `FromNative(...)`:

```csharp
  public bool CanExecuteSync => context.Api->CanExecuteSync(context.RuntimeHandle);
```

Add these methods to `JavaScriptRuntime` after `CreateHostFunction(...)`:

```csharp
  public Task ScheduleAsync(
      Action<JavaScriptRuntime> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Normal,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(body);
    return ScheduleCore(
        js =>
        {
          body(js);
          return null;
        },
        priority,
        cancellationToken
    );
  }

  public async Task<T> ExecuteAsync<T>(
      Func<JavaScriptRuntime, T> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Immediate,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(body);
    var result = await ScheduleCore(js => body(js), priority, cancellationToken)
      .ConfigureAwait(false);
    return (T)result!;
  }

  public T Execute<T>(Func<JavaScriptRuntime, T> body)
  {
    ArgumentNullException.ThrowIfNull(body);
    if (!CanExecuteSync)
    {
      throw new NotSupportedException(
          "Synchronous JavaScript runtime execution is not supported by this host."
      );
    }

    var taskContext = RuntimeTaskContext.Allocate(context, js => body(js), CancellationToken.None);
    var task = RuntimeTaskContext.TaskFor(taskContext);
    var error = context.Api->ExecuteRuntimeTaskSync(
        context.RuntimeHandle,
        &InvokeScheduledRuntimeTask,
        taskContext,
        &ReleaseScheduledRuntimeTaskContext
    );
    if (error.Code != 0)
    {
      RuntimeTaskContext.Release(taskContext);
      JsiContext.ThrowNativeError(error, "Failed to execute JavaScript runtime task.");
    }

    var result = task.GetAwaiter().GetResult();
    return (T)result!;
  }

  private Task<object?> ScheduleCore(
      Func<JavaScriptRuntime, object?> body,
      JavaScriptTaskPriority priority,
      CancellationToken cancellationToken
  )
  {
    if (cancellationToken.IsCancellationRequested)
    {
      return Task.FromCanceled<object?>(cancellationToken);
    }

    var taskContext = RuntimeTaskContext.Allocate(context, body, cancellationToken);
    var task = RuntimeTaskContext.TaskFor(taskContext);
    var error = context.Api->ScheduleRuntimeTask(
        context.RuntimeHandle,
        ToNativePriority(priority),
        &InvokeScheduledRuntimeTask,
        taskContext,
        &ReleaseScheduledRuntimeTaskContext
    );
    if (error.Code != 0)
    {
      RuntimeTaskContext.Release(taskContext);
      JsiContext.ThrowNativeError(error, "Failed to schedule JavaScript runtime task.");
      return task;
    }

    return task;
  }

  private static ExpoJsiTaskPriority ToNativePriority(JavaScriptTaskPriority priority)
  {
    return priority switch
    {
      JavaScriptTaskPriority.Immediate => ExpoJsiTaskPriority.Immediate,
      JavaScriptTaskPriority.UserBlocking => ExpoJsiTaskPriority.UserBlocking,
      JavaScriptTaskPriority.Low => ExpoJsiTaskPriority.Low,
      JavaScriptTaskPriority.Idle => ExpoJsiTaskPriority.Idle,
      _ => ExpoJsiTaskPriority.Normal,
    };
  }
```

- [ ] **Step 6: Add the managed task trampoline**

Add this nested class and unmanaged callbacks inside `JavaScriptRuntime` after the existing host-function unmanaged callbacks:

```csharp
  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void InvokeScheduledRuntimeTask(nint taskContext)
  {
    RuntimeTaskContext.FromIntPtr(taskContext).Invoke();
  }

  [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
  private static void ReleaseScheduledRuntimeTaskContext(nint taskContext)
  {
    RuntimeTaskContext.Release(taskContext);
  }

  private sealed class RuntimeTaskContext
  {
    private readonly JsiContext context;
    private readonly Func<JavaScriptRuntime, object?> body;
    private readonly CancellationToken cancellationToken;
    private readonly TaskCompletionSource<object?> completion =
      new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RuntimeTaskContext(
        JsiContext context,
        Func<JavaScriptRuntime, object?> body,
        CancellationToken cancellationToken
    )
    {
      this.context = context;
      this.body = body;
      this.cancellationToken = cancellationToken;
    }

    public Task<object?> Task => completion.Task;

    public static nint Allocate(
        JsiContext context,
        Func<JavaScriptRuntime, object?> body,
        CancellationToken cancellationToken
    )
    {
      var handle = GCHandle.Alloc(new RuntimeTaskContext(context, body, cancellationToken));
      return GCHandle.ToIntPtr(handle);
    }

    public static RuntimeTaskContext FromIntPtr(nint pointer)
    {
      return (RuntimeTaskContext)GCHandle.FromIntPtr(pointer).Target!;
    }

    public static Task<object?> TaskFor(nint pointer)
    {
      return FromIntPtr(pointer).Task;
    }

    public static void Release(nint pointer)
    {
      if (pointer == 0)
      {
        return;
      }

      GCHandle.FromIntPtr(pointer).Free();
    }

    public void Invoke()
    {
      if (cancellationToken.IsCancellationRequested)
      {
        completion.TrySetCanceled(cancellationToken);
        return;
      }

      try
      {
        completion.TrySetResult(body(new JavaScriptRuntime(context)));
      }
      catch (Exception ex)
      {
        completion.TrySetException(ex);
      }
    }
  }
```

- [ ] **Step 7: Run the focused test command**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptRuntimeExecutorTests
```

Expected: managed API compiles. Tests still fail because the testhost has not wired `DrainTasks`, sync support toggling, or task-context release counters.

- [ ] **Step 8: Commit managed runtime API**

```sh
git add managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs \
  managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs \
  managed/packages/Expo.JSI/JavaScriptRuntime.cs
git commit -m "feat: expose managed runtime executor tasks"
```

## Task 4: Wire Testhost Drain, Counters, And Sync Toggle

**Files:**
- Modify: `native/testhost/include/expo_jsi_testhost.h`
- Modify: `native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs`

- [ ] **Step 1: Extend the testhost C header**

Change `expo_jsi_testhost_counters` in `native/testhost/include/expo_jsi_testhost.h` to:

```c
typedef struct expo_jsi_testhost_counters {
  uint32_t released_values;
  uint32_t released_objects;
  uint32_t released_functions;
  uint32_t released_strings;
  uint32_t released_task_contexts;
  uint32_t sync_execute_calls;
} expo_jsi_testhost_counters;
```

Add declarations before `expo_jsi_testhost_release_runtime`:

```c
void expo_jsi_testhost_drain_tasks(expo_jsi_testhost_runtime_handle testhost_runtime);

void expo_jsi_testhost_set_sync_execution_supported(
  expo_jsi_testhost_runtime_handle testhost_runtime,
  uint8_t supported);
```

- [ ] **Step 2: Add sync-toggle state**

Add this field to `expo_jsi_testhost_runtime_t` in `native/testhost/src/ExpoJsiTestHost.cpp`:

```cpp
  bool syncExecutionSupported = true;
```

- [ ] **Step 3: Count task context releases and sync execution calls**

Add this helper context near `CountedStringReleaseContext`:

```cpp
struct CountedTaskContext {
  expo_jsi_testhost_runtime_t *testhost;
  expo_jsi_task_callback_fn callback;
  void *taskContext;
  expo_jsi_release_task_context_fn release;
};

void countedTaskCallback(void *taskContext)
{
  auto *context = static_cast<CountedTaskContext *>(taskContext);
  if (context->callback != nullptr) {
    context->callback(context->taskContext);
  }
}

void countedReleaseTaskContext(void *taskContext)
{
  auto *context = static_cast<CountedTaskContext *>(taskContext);
  if (context->testhost != nullptr) {
    context->testhost->counters.released_task_contexts++;
  }
  if (context->release != nullptr) {
    context->release(context->taskContext);
  }
  delete context;
}

expo_jsi_error countedScheduleTask(expo_jsi_runtime_handle runtime,
                                   expo_jsi_task_priority priority,
                                   expo_jsi_task_callback_fn callback,
                                   void *taskContext,
                                   expo_jsi_release_task_context_fn releaseTaskContext)
{
  auto *testhost = runtimeFor(runtime);
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  auto *countedContext =
    new CountedTaskContext{testhost, callback, taskContext, releaseTaskContext};
  auto error = api->runtime_schedule_task(
    runtime,
    priority,
    countedTaskCallback,
    countedContext,
    countedReleaseTaskContext);
  if (error.code != 0) {
    delete countedContext;
  }
  return error;
}

uint8_t countedCanExecuteSync(expo_jsi_runtime_handle runtime)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr && !testhost->syncExecutionSupported) {
    return 0;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  return api->runtime_can_execute_sync(runtime);
}

expo_jsi_error countedExecuteSync(expo_jsi_runtime_handle runtime,
                                  expo_jsi_task_callback_fn callback,
                                  void *taskContext,
                                  expo_jsi_release_task_context_fn releaseTaskContext)
{
  auto *testhost = runtimeFor(runtime);
  if (testhost != nullptr) {
    testhost->counters.sync_execute_calls++;
  }
  const auto *api = testhost != nullptr ? testhost->innerApi : expo::jsi::api();
  auto *countedContext =
    new CountedTaskContext{testhost, callback, taskContext, releaseTaskContext};
  auto error =
    api->runtime_execute_sync(runtime, countedTaskCallback, countedContext, countedReleaseTaskContext);
  if (error.code != 0) {
    delete countedContext;
  }
  return error;
}
```

Update `makeCountedApi(...)`:

```cpp
  runtime.countedApi.runtime_schedule_task = countedScheduleTask;
  runtime.countedApi.runtime_can_execute_sync = countedCanExecuteSync;
  runtime.countedApi.runtime_execute_sync = countedExecuteSync;
```

- [ ] **Step 4: Add drain and sync-toggle exports**

Add these functions before `expo_jsi_testhost_release_runtime`:

```cpp
extern "C" void expo_jsi_testhost_drain_tasks(
  expo_jsi_testhost_runtime_handle testhostRuntime)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost == nullptr) {
    return;
  }
  auto error = testhost->innerApi->runtime_drain_tasks(testhost->runtime);
  if (error.code != 0) {
    lastErrorMessage = error.message != nullptr
      ? std::string(error.message, static_cast<size_t>(error.message_len))
      : "Failed to drain runtime tasks.";
  }
}

extern "C" void expo_jsi_testhost_set_sync_execution_supported(
  expo_jsi_testhost_runtime_handle testhostRuntime,
  uint8_t supported)
{
  auto *testhost = static_cast<expo_jsi_testhost_runtime_t *>(testhostRuntime);
  if (testhost != nullptr) {
    testhost->syncExecutionSupported = supported != 0;
  }
}
```

- [ ] **Step 5: Finish managed fixture binding**

Verify `NativeTestHost` contains the new delegates, `EnsureLoaded()` loads both exports, and `HermesRuntimeFixture` exposes:

```csharp
  public void DrainTasks()
  {
    NativeTestHost.DrainTasks(testHostRuntime);
  }

  public void DisableSyncExecutionForTesting()
  {
    NativeTestHost.SetSyncExecutionSupported(testHostRuntime, false);
  }
```

- [ ] **Step 6: Run focused executor tests**

Run:

```sh
scripts/test-jsi.sh --filter JavaScriptRuntimeExecutorTests
```

Expected: all `JavaScriptRuntimeExecutorTests` pass.

- [ ] **Step 7: Commit testhost executor support**

```sh
git add native/testhost/include/expo_jsi_testhost.h \
  native/testhost/src/ExpoJsiTestHost.cpp \
  managed/packages/Expo.JSI.Tests/Fixtures/NativeTestHost.cs \
  managed/packages/Expo.JSI.Tests/Fixtures/HermesRuntimeFixture.cs
git commit -m "test: wire hermes runtime executor testhost"
```

## Task 5: Verify Existing Host Function Path And Full Suite

**Files:**
- Modify if required by verification only: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`

- [ ] **Step 1: Add a regression test for direct host-function execution if missing**

If no existing test proves host functions return directly, add this test to `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`:

```csharp
  [Fact]
  public void HostFunctionReturnsWithoutDrainingRuntimeTasks()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "directReturn",
        0,
        (runtime, thisValue, arguments, context) => runtime.CreateString("direct"),
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("directReturn", functionValue);

    using var result = fixture.Evaluate("globalThis.directReturn()", "host-function-direct.js");

    Assert.Equal(JavaScriptValueKind.String, result.Kind);
    Assert.Equal("direct", result.AsString());
  }
```

- [ ] **Step 2: Run the full Hermes-backed suite**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all tests pass, including the existing host-function tests and the new executor tests.

- [ ] **Step 3: Run formatting check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: command exits 0. If it reports formatting changes, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

Expected after formatting: command exits 0.

- [ ] **Step 4: Run whitespace check**

Run:

```sh
git diff --check
```

Expected: no output.

- [ ] **Step 5: Commit final verification cleanup**

If Step 1 added the regression test or Step 3 changed formatting, commit those files:

```sh
git add managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs
git commit -m "test: keep host functions on direct runtime path"
```

If Step 1 and Step 3 changed no files, do not create an empty commit.

## Implementation Notes

- `CanExecuteSync` is a passive property. Do not call `runtime_execute_sync` or schedule a probe to compute it.
- The native executor callback receives `facebook::jsi::Runtime&`, but the C ABI callback remains `void (*)(void*)`; managed code uses the captured opaque runtime handle to call the existing API table.
- The first implementation does not add async delegate overloads such as `Func<JavaScriptRuntime, Task<T>>`. `Task` is the returned observation/completion mechanism, not the runtime-access body type.
- On scheduling failure, native does not consume the task context. Managed code releases the context after converting the returned `expo_jsi_error` into an exception.
- On successful scheduling or sync execution, native consumes the context and calls the release callback exactly once after the managed callback returns.
- Cancellation does not remove work from the native queue. The managed callback observes cancellation when it starts and completes the `Task` as canceled without invoking the user body.
- Do not add React Native, RNW, WinUI, AppKit, or expo-desktop dependencies to the portable core in this slice.

## Self-Review Checklist

- Spec coverage: Task 2 implements the runtime-access abstraction and ABI; Task 3 implements the Task-first managed API; Task 4 implements deterministic headless drain and context-release checks; Task 5 verifies the direct host-function path remains unchanged.
- Placeholder scan: no task relies on open-ended phrases for required code; each code-changing step includes concrete snippets and exact commands.
- Type consistency: public names are `JavaScriptTaskPriority`, `ScheduleAsync`, `ExecuteAsync<T>`, `Execute<T>`, and `CanExecuteSync`; native names are `JsiRuntimeExecutor`, `JsiRuntimeTaskPriority`, and `runtime_*` ABI fields.
- Boundary check: no raw JSI layouts cross into C#; no React Native headers are introduced into portable core; sync support remains adapter-declared.
