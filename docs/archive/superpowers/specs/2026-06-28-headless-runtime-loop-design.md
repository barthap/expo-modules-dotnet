# Headless Runtime Loop Design

Date: 2026-06-28
Repo: `<repo>`

## Context

The headless Hermes bridge now has a `JsiRuntimeExecutor` abstraction and a
Task-first managed API:

```text
JavaScriptRuntime.ScheduleAsync(...)
JavaScriptRuntime.ExecuteAsync<T>(...)
JavaScriptRuntime.Execute<T>(...)
```

The current headless implementation is intentionally deterministic and
single-threaded. `executeAsync` enqueues work, and tests run that work by
calling a manual drain hook. That was enough to prove the C ABI callback shape,
managed `Task` completion, cancellation, context release, and sync capability.

It does not yet model a real host event loop. If no one drains the queue,
scheduled tasks remain pending until runtime teardown releases them. That is
acceptable for the first slice, but it is too weak for the next promise slice.
Promise settlement needs a realistic path:

```text
.NET continuation -> schedule runtime work -> resolve/reject JS Promise
  -> host performs microtask checkpoint -> JS .then/.catch handlers run
```

Hermes exposes JSI microtask APIs, but they are still host-driven. The engine
can queue and drain microtasks, but the host must decide when to call
`facebook::jsi::Runtime::drainMicrotasks(...)`.

## Goal

Add a headless runtime loop that more closely resembles React Native runtime
access:

- a dedicated executor thread owns ordinary Hermes runtime access;
- `executeAsync` posts work to that thread;
- `executeSync` runs inline when already on the executor thread, otherwise
  posts and waits;
- the loop performs a Hermes microtask checkpoint after each runtime task;
- tests can wait for the loop to become idle without manually running each
  task.

This slice should make promise support the natural next step without building
promise handles or generated async module dispatch yet.

## Non-Goals

Do not build in this slice:

- JavaScript promise handle creation or promise settlement;
- generated async module dispatch;
- timers, `setTimeout`, `setImmediate`, or a full JavaScript macrotask queue;
- React Native `RuntimeScheduler` expiration, yielding, rendering updates, or
  task priorities beyond the ordering already supported by the headless
  executor;
- RNW, React Native macOS, or expo-desktop adapters;
- public C# async delegate bodies that keep JSI access across `await`;
- Babel, transpilation, or modern-JS compatibility infrastructure.

## Approaches Considered

### Option A: Keep Manual Drain And Add Promises

This is the smallest implementation, but it would make promise tests depend on
manual calls such as `DrainTasks()` and `drainMicrotasks()`. It proves promise
object lifetime, but it does not prove the host behavior we need for RN-like
integration.

### Option B: Dedicated Headless Runtime Loop First

This adds a modest amount of native threading code, but it proves the important
host shape before promises: runtime work happens on a specific executor, async
work can complete from another thread and re-enter runtime access, and Hermes
microtasks are checked after each task.

This is the recommended path.

### Option C: Implement A Full Console Event Loop

This would include timers, console task queues, process lifecycle controls, and
possibly REPL-style behavior. It is too broad for the next slice. Timers are
not necessary to prove native promise settlement because JS `.then(...)`
handlers are microtasks.

## Proposed Architecture

Introduce a headless runtime loop owned by the Hermes console connector:

```text
HermesConsoleRuntimeConnector
  owns Hermes runtime
  owns HermesConsoleRuntimeLoop

HermesConsoleRuntimeLoop
  owns executor thread
  owns task queue, mutex, condition variable
  grants scoped runtime access
  performs microtask checkpoints
  supports shutdown and waitUntilIdle
```

The runtime loop is still a headless test/proof utility. It must not pull React
Native headers into the portable core.

The current `JsiRuntimeExecutor` shape remains valid:

```cpp
executeAsync(priority, work)
canExecuteSync()
executeSync(work)
drain()
```

The implementation behind it changes for the headless Hermes connector. The
manual `drain()` hook becomes a compatibility/test hook that waits until the
runtime loop has processed all currently queued work. It should not be the
ordinary mechanism for running tasks once the executor thread exists.

## Thread Ownership

The executor thread is the only thread that should perform ordinary Hermes
runtime work after startup.

Rules:

- the connector creates the Hermes runtime before starting the executor thread;
- after the loop starts, `executeAsync` runs callbacks on the executor thread;
- `executeSync` checks whether the caller is already on the executor thread;
- if already on the executor thread, `executeSync` invokes inline;
- otherwise `executeSync` enqueues a task and blocks the caller until it
  finishes;
- shutdown stops accepting new work, releases queued work without invoking it,
  wakes waiting sync callers, and joins the executor thread.

This models React Native's important property for our purposes:

```text
scheduled native work receives exclusive scoped runtime access
```

It does not claim that all hosted adapters will literally hop threads. RN 0.86
`RuntimeSchedulerCallInvoker` sync execution is still best understood as
exclusive runtime access with re-entrancy handling, not as a mandatory thread
switch.

## Queue Semantics

The headless loop should keep the current minimal priority semantics:

- lower numeric priority runs first;
- FIFO within the same priority;
- tasks enqueued while a task is running are picked up by the loop after the
  current task completes and the microtask checkpoint runs.

`executeAsync` returns after enqueueing. The returned managed `Task` completes
when the queued callback runs, throws, is canceled at callback entry, or is
released during shutdown.

`executeSync` blocks only callers outside the executor thread. It must not hold
the queue mutex while running user work. It should propagate callback failure
to the caller through the same structured native error boundary used today.

## Microtask Checkpoints

The loop should call:

```cpp
runtime.drainMicrotasks(-1)
```

after:

- initial script evaluation in headless tests or console proof code;
- each runtime task executed by the loop;
- each synchronous task executed inline on the executor thread, after the
  outermost runtime task completes.

The checkpoint should happen outside managed callback frames but while runtime
access is still owned by the executor thread.

If `drainMicrotasks` throws a `facebook::jsi::JSError`, the headless proof
should convert it into a structured native error for the task that triggered
the checkpoint. Promise job exceptions are expected to be handled internally by
Hermes, but explicit `queueMicrotask` callbacks can throw.

The first implementation should use an unbounded checkpoint (`-1`) rather than
introducing yielding or bounded draining.

## Startup And Shutdown

The runtime loop needs explicit lifecycle states:

```text
Created -> Running -> Stopping -> Stopped
```

Behavior:

- `Created`: runtime exists, executor thread not yet accepting work;
- `Running`: accepts async and sync work;
- `Stopping`: rejects new work, releases queued work, wakes waiters;
- `Stopped`: all queued work has been released and executor thread has joined.

The connector destructor and `invalidate()` must drive shutdown before the
Hermes runtime is destroyed.

Pending queued work must not leak managed `GCHandle`s. The existing scheduled
task context release behavior should continue to fault pending managed tasks
when work is released before invocation.

## Testhost API

Keep deterministic test control, but shift its meaning:

```text
expo_jsi_testhost_drain_tasks(...)
```

should wait until all tasks queued before the call have completed. It may also
perform a final microtask checkpoint if called from a non-executor thread.

Add a stronger helper if the implementation needs a distinct name:

```text
expo_jsi_testhost_wait_until_idle(...)
```

The managed fixture may expose:

```csharp
fixture.WaitUntilIdle();
```

`DrainTasks()` can remain as a compatibility alias in tests during the
transition.

## JavaScript Test Style

Hermes in this repository is embedded directly. Tests should avoid relying on a
Babel transform or on modern syntax that may not be enabled in the local
runtime build.

Use conservative JavaScript snippets:

```js
var done = false;
var value = null;

nativeAsync().then(function (result) {
  done = true;
  value = result;
});
```

Avoid in this slice:

- top-level `await`;
- optional chaining;
- nullish coalescing;
- class fields;
- async-heavy arrow syntax;
- timer-based assertions.

Promise-oriented tests should use `Promise.prototype.then(function (...) { ...
})` and should validate state after the host reaches idle.

Implementation finding: this bare Hermes embedding does not provide the React
Native `setImmediate` layer that this Hermes build's Promise bytecode uses
when the host microtask queue is not enabled/configured. This slice should
validate host microtask checkpoints with `queueMicrotask(function () { ... })`
backed by `facebook::jsi::Runtime::queueMicrotask`, with the Hermes runtime
created using `RuntimeConfig::Builder().withMicrotaskQueue(true)`. Promise
settlement remains the next slice and should not smuggle in timers or a full
macrotask queue here.

## Promise Follow-Up Shape

Promise support should be implemented after the runtime loop exists.

The intended flow is:

```text
JS calls native async host function
  native/C# creates and returns JS Promise
  C# async operation runs on .NET thread pool
  C# continuation calls runtime.ExecuteAsync(...)
  executor thread resolves or rejects the JS Promise
  runtime loop drains Hermes microtasks
  JS .then/.catch handler runs
```

The promise slice should not need to redesign the executor API.

Rules preserved from the prior executor design:

- decode host-function arguments synchronously;
- never let borrowed `JavaScriptArguments` or borrowed values escape;
- capture only durable runtime, promise, resolve, and reject handles;
- touch JSI only inside executor-provided runtime access.

## Testing Strategy

Add Hermes-backed tests for the loop before promise support:

- `ExecuteAsync` runs on the executor thread and completes without manual task
  invocation;
- `Execute<T>` called from outside the executor thread posts and waits;
- nested `Execute<T>` called from within executor work runs inline;
- `WaitUntilIdle` returns after all work queued before the call has run;
- work enqueued by running work is processed before idle is reported;
- shutdown releases queued work and faults pending managed tasks;
- microtasks queued by JS `queueMicrotask(function () { ... })` run after
  script evaluation when the host reaches idle;
- microtasks queued by executor work run before idle is reported;
- tests use conservative JavaScript syntax.

Do not test with timers in this slice. Timers require a macrotask queue and are
a separate host feature.

## Verification Commands

After implementation:

```sh
scripts/test-jsi.sh
scripts/format.sh --check --all
git diff --check
```

## Stop Conditions

Stop and review if:

- the portable core needs React Native headers to express the runtime loop;
- the executor thread must expose raw `facebook::jsi::Runtime` or raw JSI
  layouts to C#;
- `executeSync` can deadlock when called from executor-owned runtime work;
- queued work can be dropped without releasing managed task contexts;
- promise tests require timers or a full macrotask queue;
- tests only pass by adding Babel or modern-JS transforms;
- microtasks only run when tests call a private Hermes API directly instead of
  going through the host loop policy.

## Acceptance Criteria

The implementation plan for this design is acceptable when:

- it preserves the existing `JsiRuntimeExecutor` abstraction;
- the headless Hermes connector has a dedicated executor thread and clear
  lifecycle;
- async scheduled work runs without manual per-task draining;
- sync execution is safe from inside and outside the executor thread;
- Hermes microtasks are checkpointed by the host loop;
- promise support can be implemented next without changing the executor API;
- tests avoid modern JavaScript syntax assumptions.
