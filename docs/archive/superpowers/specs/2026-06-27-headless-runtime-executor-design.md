# Headless Runtime Executor Design

Date: 2026-06-27
Repo: `<repo>`

## Context

The Hermes console HostFXR proof now validates synchronous generated-looking
module dispatch through real JSI:

```text
JS -> native JSI host function -> C ABI -> C# generated-looking dispatch
```

That proof covers primitive values, strings, objects, host functions, borrowed
arguments, owned return handles, and managed errors converted back to JavaScript
errors. It deliberately does not cover async functions, promises, events, or
work that needs to touch JSI after the original host-function callback has
returned.

The next slice should add the missing runtime-access capability before adding
promises. Earlier docs called this capability a scheduler. That word is still
useful at the React Native adapter boundary, but the portable core should model
the more precise concept:

```text
runtime executor = a capability that grants scoped, exclusive access to
facebook::jsi::Runtime for scheduled native work.
```

This distinction matters because React Native does not only model "hop to the
JS thread." In React Native 0.86, `RuntimeSchedulerCallInvoker::invokeSync`
forwards to `RuntimeScheduler::executeNowOnTheSameThread(...)`, whose important
semantic is safe runtime access with re-entrancy handling, not a guaranteed
thread hop. React Native 0.81, RNW 0.81, and React Native macOS 0.81 are more
limited for sync access, but they still fit an async runtime-executor shape.

The governing architecture remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## Goal

Design a runtime executor and Task-first managed API that let C# schedule work
onto the JSI runtime safely from async continuations, promise settlement, event
emission, retained JS callbacks, and scoped JS heap access.

The design must fit all of these host shapes without changing managed promise
or module code:

- headless Hermes console runtime owned by this repository;
- RNW / React Native macOS 0.81 async call-invoker-like facilities;
- React Native 0.86 `RuntimeSchedulerCallInvoker` async and sync facilities;
- future expo-desktop or RNW adapter code that supplies the same capability.

## Non-Goals

Do not build in this slice:

- JavaScript promise handles or promise settlement;
- async generated module dispatch;
- source generator changes;
- RNW, React Native macOS, or expo-desktop adapter implementation;
- full React Native `RuntimeScheduler` behavior;
- yielding, rendering updates, expired task processing, microtasks, or task
  cancellation queues;
- public C# module DSL;
- NativeAOT loader changes.

The slice may add enough executor API surface for promises to use next, but it
must not implement promises in the same step.

## Compatibility Observations

React Native 0.81 and 0.86 both expose `CallInvoker` in terms of
`CallFunc = std::function<void(jsi::Runtime&)>`. That is the most important
shape to preserve: scheduled work receives runtime access only for the duration
of the callback.

React Native `RuntimeExecutor` is also runtime-access-shaped:

```cpp
std::function<void(std::function<void(jsi::Runtime& runtime)>&& callback)>
```

It executes the callback when runtime access is safe and may be asynchronous
depending on host implementation.

React Native 0.86 `RuntimeSchedulerCallInvoker` maps:

- `invokeAsync(func)` to `RuntimeScheduler::scheduleWork(...)`;
- `invokeAsync(priority, func)` to `RuntimeScheduler::scheduleTask(...)`;
- `invokeSync(func)` to `RuntimeScheduler::executeNowOnTheSameThread(...)`.

RNW / React Native macOS 0.81 should be treated as async-first. They can map
to the same abstraction through `CallInvoker::invokeAsync`,
`RuntimeExecutor`, or equivalent host-provided scheduling. Sync support can be
reported unsupported unless a host provides a safe implementation.

Current Expo APIs provide useful naming and behavior:

- Swift `runtime.schedule { ... }` schedules fire-and-forget runtime work.
- Swift `runtime.execute { ... }` grants scoped runtime access and propagates
  return values or errors.
- Android `reactContext.runOnJSQueueThread(...)` is a fire-and-forget queue
  primitive; newer experimental JS heap access uses `runOnQueue` and
  `runOnQueueSync`.

The C# API should feel natural with `Task` and `async` / `await`, while keeping
the same schedule-vs-execute split. In the first slice, runtime-access bodies
are synchronous delegates. The API returns `Task` so callers can `await`
scheduled runtime work, but the body itself must not be `async` because C# has
no built-in equivalent of Swift's `@JavaScriptActor` that would keep post-await
continuations under JSI runtime access.

## Native Core Shape

Replace or evolve the current `JsiScheduler` concept into a runtime executor:

```cpp
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
    std::function<void(facebook::jsi::Runtime&)> work) noexcept = 0;

  virtual bool canExecuteSync() const noexcept = 0;

  virtual void executeSync(
    std::function<void(facebook::jsi::Runtime&)> work) = 0;
};

}
```

Priority values intentionally mirror `facebook::react::SchedulerPriority`
without including React Native headers in the portable core.

`canExecuteSync()` is an adapter-declared capability. It must not actively
probe sync support by calling a host sync API. The Android Expo
`supportsSyncExecution()` hazard is the cautionary example: an active sync
probe can enter deadlock-sensitive runtime scheduler paths.

The existing connector becomes:

```cpp
class JsiRuntimeConnector {
public:
  virtual facebook::jsi::Runtime& runtime() = 0;
  virtual JsiRuntimeExecutor& runtimeExecutor() = 0;
  virtual bool isRuntimeValid() const = 0;
  virtual void invalidate() = 0;
};
```

The executor is runtime-bound and adapter-owned. The portable bridge must not
store raw `facebook::react::CallInvoker`, `RuntimeExecutor`, or
`RuntimeScheduler` types in reusable core headers.

## Headless Hermes Executor

The headless Hermes connector owns the Hermes runtime, so it can implement a
deterministic executor without React Native.

The first implementation should use a small queue instead of only immediate
execution. Immediate execution is too weak to prove promise and event paths
later because it hides the fact that work may run after the initiating call
returns.

Recommended modes:

- `executeAsync(priority, work)` enqueues work and returns immediately.
- `drain()` runs queued tasks in deterministic order while holding runtime
  access.
- `executeSync(work)` runs inline when already executing runtime work; otherwise
  it runs the work synchronously with exclusive runtime access.
- `canExecuteSync()` returns true for the headless Hermes executor.

For the first slice, priority ordering may be minimal:

- immediate before normal;
- otherwise FIFO within the same priority.

Full React Native task expiration, yielding, rendering updates, and microtasks
are intentionally out of scope.

## Hosted Adapter Mapping

Future hosted adapters should wrap host facilities behind `JsiRuntimeExecutor`.

RNW / React Native macOS 0.81 async mapping:

```text
JsiRuntimeExecutor.executeAsync(priority, work)
  -> CallInvoker.invokeAsync([work](jsi::Runtime& runtime) { work(runtime); })

JsiRuntimeExecutor.canExecuteSync()
  -> false unless the host has a known safe sync path

JsiRuntimeExecutor.executeSync(work)
  -> throw unsupported sync runtime access
```

React Native 0.86 mapping:

```text
executeAsync(priority, work)
  -> RuntimeSchedulerCallInvoker.invokeAsync(priority, work)

canExecuteSync()
  -> true when backed by RuntimeSchedulerCallInvoker invokeSync

executeSync(work)
  -> RuntimeSchedulerCallInvoker.invokeSync(work)
```

The 0.86 sync path must be documented as exclusive runtime access. It is not a
promise that work switched to a different thread.

## C ABI Additions

Managed code cannot receive a C++ lambda or a raw `jsi::Runtime&`. The native
bridge should expose executor capability through a narrow C ABI callback shape.

Conceptual additions:

```c
typedef enum expo_jsi_task_priority {
  EXPO_JSI_TASK_IMMEDIATE = 1,
  EXPO_JSI_TASK_USER_BLOCKING = 2,
  EXPO_JSI_TASK_NORMAL = 3,
  EXPO_JSI_TASK_LOW = 4,
  EXPO_JSI_TASK_IDLE = 5
} expo_jsi_task_priority;

typedef void (*expo_jsi_task_callback_fn)(void *task_context);

typedef void (*expo_jsi_release_task_context_fn)(void *task_context);

typedef expo_jsi_error (*expo_jsi_runtime_schedule_task_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_priority priority,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef uint8_t (*expo_jsi_runtime_can_execute_sync_fn)(
  expo_jsi_runtime_handle runtime);

typedef expo_jsi_error (*expo_jsi_runtime_execute_sync_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_task_callback_fn callback,
  void *task_context,
  expo_jsi_release_task_context_fn release_task_context);

typedef expo_jsi_error (*expo_jsi_runtime_drain_tasks_fn)(
  expo_jsi_runtime_handle runtime);
```

`drain_tasks` is for the headless testhost and console proof. Hosted adapters
may expose it as unsupported or no-op, but the managed test fixture needs a
deterministic way to make scheduled work run.

The scheduled callback executes with runtime access already granted by native.
Inside that callback, managed code may call ordinary `Expo.JSI` APIs using the
same runtime handle. The callback must not escape borrowed handles from any
outer host-function frame.

Errors:

- scheduling failure returns `expo_jsi_error`;
- callback exceptions are caught by the managed trampoline and stored on the
  managed `Task`;
- C++ exceptions do not cross the ABI;
- C# exceptions do not cross unmanaged frames.

## Managed API

Add a Task-first managed runtime-access API to `Expo.JSI.JavaScriptRuntime`.

Recommended surface:

```csharp
public enum JavaScriptTaskPriority
{
  Immediate = 1,
  UserBlocking = 2,
  Normal = 3,
  Low = 4,
  Idle = 5,
}

public sealed unsafe class JavaScriptRuntime
{
  public bool CanExecuteSync { get; }

  public Task ScheduleAsync(
      Action<JavaScriptRuntime> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Normal,
      CancellationToken cancellationToken = default);

  public Task<T> ExecuteAsync<T>(
      Func<JavaScriptRuntime, T> body,
      JavaScriptTaskPriority priority = JavaScriptTaskPriority.Immediate,
      CancellationToken cancellationToken = default);

  public T Execute<T>(Func<JavaScriptRuntime, T> body);
}
```

`ScheduleAsync` returns a `Task` so failures are observable. It is not a raw
fire-and-forget API. Higher-level event helpers may choose to log and discard
that task later, but the primitive should preserve errors.

`ExecuteAsync` is the primary API for generated async/promise code and scoped
JS heap access. It completes when scheduled runtime work has run and the body
has returned or thrown. The body must be synchronous; async work should happen
before calling `ExecuteAsync`, and any JS access after that async work should
be done inside a new `ExecuteAsync` call.

`Execute` is optional at the adapter level. If `CanExecuteSync` is false,
`Execute` throws a clear unsupported-sync exception before invoking `body`.

The managed API passes a `JavaScriptRuntime` instance into each body to make the
runtime access scope explicit and avoid capturing callback-frame-only objects.

Async-body overloads such as `Func<JavaScriptRuntime, Task<T>>` are deferred.
They would require a runtime-affine continuation model or repeated re-entry
into the executor after each `await`. Adding those overloads without that model
would let code touch JSI after runtime access has ended.

## Cancellation

Cancellation is best-effort in this slice.

Semantics:

- if the token is canceled before scheduling, return a canceled `Task`;
- if the token is canceled while the task is queued in the headless executor,
  the managed callback may skip the body and complete as canceled;
- if the token is canceled after runtime work starts, do not interrupt JSI work;
- hosted adapters are not required to remove already-scheduled work from React
  Native queues;
- cancellation must not leave retained callback contexts unreleased.

This gives C# callers a natural `CancellationToken` parameter without forcing a
full cancelable priority queue into the first implementation.

## Promise Follow-Up Shape

Promises are not implemented in this slice, but the runtime executor API should
make the next slice straightforward.

Generated async module code should look conceptually like this:

```csharp
var promise = runtime.CreatePromise();
var promiseValue = promise.RetainPromiseValueForReturn();

_ = module.ReadTextAsync(path).ContinueWith(async task =>
{
  await runtime.ExecuteAsync(js =>
  {
    if (task.IsCompletedSuccessfully)
    {
      using var value = js.CreateString(task.Result);
      promise.Resolve(value);
    }
    else
    {
      promise.Reject(task.Exception);
    }
  });
});

return promiseValue;
```

The important rules:

- arguments are decoded synchronously during the original host-function call;
- `JavaScriptArguments` and `JavaScriptBorrowedValue` never escape;
- durable promise/runtime state is captured before returning to JS;
- the `Task` continuation never touches JSI directly;
- promise resolve/reject happens inside `ExecuteAsync`.

## Testing Strategy

Add tests to the Hermes-backed `Expo.JSI.Tests` suite.

Required tests:

- `ExecuteAsync` runs scheduled work only after the headless executor drains.
- `ExecuteAsync<T>` returns a value created/read through real Hermes JSI.
- `ExecuteAsync<T>` propagates managed exceptions through the returned `Task`.
- `ScheduleAsync` returns a faulted `Task` when scheduled work throws.
- cancellation before scheduling returns a canceled `Task`.
- cancellation after runtime work starts does not interrupt the body.
- `Execute<T>` succeeds on the headless executor.
- an unsupported-sync testhost or fake API makes `Execute<T>` throw before
  invoking `body`.
- queued tasks release managed callback contexts exactly once.
- synchronous host-function callbacks still return directly and do not require
  scheduling.

Headless tests should expose a drain function through the native testhost so
managed tests can verify queued behavior deterministically.

## Verification Commands

After implementation:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
```

Before finishing the design-only step:

```sh
git diff --check
```

## Stop Conditions

Stop and review if:

- the portable core needs React Native headers to express executor semantics;
- C# receives or stores raw `facebook::jsi::Runtime`, `jsi::Value`, or
  `jsi::Object` layouts;
- `CanExecuteSync` becomes an active sync probe instead of a passive adapter
  property;
- `ScheduleAsync` swallows exceptions;
- cancellation requires removing tasks from a hosted React Native queue;
- promise settlement can touch JSI without going through `ExecuteAsync`;
- borrowed host-function arguments can escape into scheduled work;
- headless Hermes immediate execution makes tests unable to prove post-callback
  scheduling behavior.

## Acceptance Criteria

The implementation plan for this design is acceptable when:

- the core abstraction is named and documented as runtime access, not only
  thread hopping;
- headless Hermes, RNW / React Native macOS 0.81, and React Native 0.86 all
  have clear adapter mappings;
- C# callers get natural `Task` / `async` / `await` APIs;
- sync support is available where adapters support it but is not required for
  promises;
- cancellation has explicit best-effort semantics;
- the next promise slice can be implemented without redesigning executor APIs.
