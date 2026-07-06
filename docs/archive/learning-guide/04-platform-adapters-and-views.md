# 04 - Platform Adapters And Views

## Headless Core First

The portable bridge should first work without a React Native app. This is
called headless in these docs.

Headless means:

- C++ owns a JSI runtime or a clearly documented fake/runtime substitute;
- C# wrappers manipulate values through the C ABI;
- generated-looking module code can run;
- tests prove ownership and conversion behavior;
- no RNW, WinUI, AppKit, XAML, or app packaging is required.

This is valuable because it answers the core question before platform noise:

```text
Can C# module logic talk to JSI through typed wrappers and opaque handles?
```

If the answer is no, RNW integration will not fix it. If the answer is yes,
RNW and React Native macOS adapters can be thinner.

## What A Platform Adapter Does

A platform adapter mounts the proven headless core into a real React Native
host.

Responsibilities:

- find or receive the host JSI runtime;
- install global/module entry points;
- supply JS scheduler callbacks, equivalent in role to React Native's
  call-invoker/runtime-scheduler facilities;
- supply lifecycle hooks;
- route logging and diagnostics;
- connect platform services;
- optionally register native view adapters.

Non-responsibilities:

- owning the C# module registry design;
- decoding ordinary JS arguments;
- deciding v2 type conversion rules;
- implementing generated bindings;
- embedding WinUI/AppKit concepts in the universal core.

Good adapter boundary:

```text
RNW adapter
  gets runtime from RNW
  provides scheduler/lifecycle
  installs portable bridge

portable bridge
  registers generated modules
  invokes C# logic
  manages JSI values through C++ bridge
```

Bad adapter boundary:

```text
RNW adapter
  scans C# modules
  decodes arguments
  invokes module methods
  owns view and non-view module semantics
```

The bad boundary makes the core less portable.

## Adapter Install Walkthrough

This walkthrough is illustrative. It gives future agents a concrete shape for
the "thin adapter" boundary without pretending the final RNW or React Native
macOS code already exists.

The host-specific adapter receives or locates a real `jsi::Runtime&` from the
React Native host. It then constructs a bridge runtime handle, fills a platform
services table, and calls the portable bridge install function.

Native adapter shape:

```cpp
void install_expo_csharp_jsi_for_host(
  facebook::jsi::Runtime &runtime,
  HostScheduler &scheduler,
  HostLogger &logger) {
  expo_js_runtime_handle runtime_handle =
    expo_js_runtime_borrow_from_host(&runtime);

  expo_platform_services services{
    .context = &scheduler,
    .schedule_on_js = [](
      void *context,
      expo_task_priority priority,
      expo_task_callback callback,
      void *task_context) {
      auto *scheduler = static_cast<HostScheduler *>(context);
      scheduler->scheduleOnJs(priority, [callback, task_context]() {
        callback(task_context);
      });
    },
    .log = [](void *context, int32_t level, const char *message, int32_t len) {
      auto *logger = static_cast<HostLogger *>(context);
      logger->log(level, std::string(message, static_cast<size_t>(len)));
    },
    .is_runtime_valid = [](void *context) -> int32_t {
      auto *scheduler = static_cast<HostScheduler *>(context);
      return scheduler->isRuntimeStillValid() ? 1 : 0;
    },
  };

  expo_managed_entrypoints managed{
    .initialize = resolved_managed_initialize,
    .register_modules = resolved_generated_register_modules,
  };

  expo_csharp_jsi_install(runtime_handle, &services, &managed);
}
```

Portable install shape:

```c
int32_t expo_csharp_jsi_install(
  expo_js_runtime_handle runtime,
  const expo_platform_services *services,
  const expo_managed_entrypoints *managed_entrypoints);
```

The adapter's job is narrow:

1. provide the host runtime as an opaque runtime handle;
2. provide scheduler/log/lifecycle services;
3. call the portable install function;
4. let the portable bridge and generated C# bindings register modules.

The adapter should not decode ordinary JS arguments, scan C# modules, invoke
C# module methods, or own the generated v2 binding rules.

## RNW Adapter

RNW is the first real host target because this repository is Windows-focused.
However, RNW work belongs after the headless proof.

RNW adapter likely handles:

- package registration;
- runtime installation;
- integration with expo-desktop runtime ownership;
- scheduler mapping;
- Windows lifecycle;
- optional WinUI view adapter later.

RNW adapter work should happen in this repository after the core proof is
stable. Verification that needs Visual Studio, MSBuild, packaging, or app
screenshots belongs on <windows-test-machine>.

## React Native macOS Adapter

React Native macOS is a future portability proof. The plan should leave a
credible path but must not assume a host app exists.

A future RN macOS proof should:

- reuse the same headless core;
- implement only a macOS adapter layer;
- provide scheduler/lifecycle services for the macOS host;
- avoid AppKit view work unless explicitly requested;
- prove that the core did not accidentally depend on RNW.

Do not create a React Native macOS app without user approval.

## Views Are Platform-Gated

Views are different from headless modules. A headless module can expose a
function like `Math.add`. A view module needs native UI objects, layout,
events, and platform-specific rendering.

The universal core may define metadata:

```text
View name
Prop names and types
Event names and payload types
Command names
```

Actual creation belongs to adapters:

```text
WindowsViewAdapter
  creates WinUI/RNW view objects
  maps props to Windows types
  emits events through RNW

MacOSViewAdapter
  creates AppKit/RN macOS view objects
  maps props to macOS types
  emits events through RN macOS

NoViewAdapter
  used in headless mode
  fails loudly if a view is requested
```

This keeps the core honest. If a headless test accidentally needs WinUI, the
core is no longer universal.

## Platform Services Table

A future adapter may pass a services table into the core:

```c
typedef void (*expo_task_callback)(void *task_context);

typedef enum expo_task_priority {
  EXPO_TASK_IMMEDIATE = 0,
  EXPO_TASK_NORMAL = 1
} expo_task_priority;

typedef struct expo_platform_services {
  void *context;
  void (*schedule_on_js)(
    void *context,
    expo_task_priority priority,
    expo_task_callback callback,
    void *task_context);
  void (*log)(void *context, int32_t level, const char *message, int32_t len);
  int32_t (*is_runtime_valid)(void *context);
} expo_platform_services;
```

This is only illustrative. The important design is explicit dependency
injection. The core should not reach out to RNW or AppKit globals by itself.

## Call Invoker vs Portable Scheduler

React Native uses call-invoker-like objects to run closures on the JS thread.
Depending on React Native version and host, the concrete native mechanism may
be:

- `react::CallInvoker`;
- `react::RuntimeExecutor`;
- `RuntimeScheduler` or `RuntimeSchedulerCallInvoker`;
- a platform wrapper supplied by RNW or React Native macOS.

The portable C# bridge should not expose those concrete C++ types to C#. They
are host integration details. The adapter should translate them into one
portable capability:

```text
schedule_on_js(callback, context, priority)
```

That capability is needed whenever code wants to touch JSI after the current JS
callback frame has ended. It is not needed for the body of a synchronous host
function that is already running on the JS runtime thread.

Use the scheduler for:

- settling promises after a C# `Task` completes;
- emitting events from timers, native callbacks, or background work;
- calling retained JS functions after the original host call has returned;
- releasing retained JSI state if the release operation must run on the JS
  runtime thread.

Do not use the scheduler as a general background executor for C# work. .NET can
run its own CPU or I/O work on .NET threads. The scheduler exists only to cross
back into the JS runtime safely.

Headless tests may implement `schedule_on_js` as immediate execution, but they
should still route through the same abstraction. Otherwise the proof lies about
the seam that real RNW and React Native macOS adapters must implement.

## Async And Scheduling

Async module methods force the adapter boundary to be real.

Example:

```text
C# Task completes on a .NET thread
adapter schedules promise resolution on JS runtime
native bridge resolves JS promise
```

If the adapter cannot schedule back to JS correctly, promise resolution is
unsafe. A headless proof can use a simple single-threaded scheduler, but a real
host adapter must use host-provided scheduling.

Concrete promise path:

```text
generated C# method starts Task<T>
  returns JS promise handle immediately

Task<T> completes on .NET worker thread
  generated continuation packages success/error
  calls platform services schedule_on_js

adapter posts callback to host JS queue
  callback runs while runtime is valid
  C# wrapper creates result JS value through C ABI
  native bridge calls resolve or reject JSI function
```

The important constraint is not the exact API name. It is ownership and thread
discipline: promise handles belong to the JS runtime, and resolve/reject must
happen on the runtime's valid JS execution context.

Bad:

```csharp
task.ContinueWith(t => promise.Resolve(runtime.CreateString(t.Result)));
```

That continuation may run on a .NET thread that is not allowed to touch JSI.
It is also wrong if `runtime` came from a borrowed callback-frame object such as
`args.Runtime`.

Better shape:

```csharp
JavaScriptAsyncRuntime asyncRuntime = runtime.CaptureForAsync();
JavaScriptPromise promise = asyncRuntime.CreatePromise();

task.ContinueWith(t =>
  asyncRuntime.ScheduleOnJs(() =>
  {
    try
    {
      using var value = asyncRuntime.CreateString(t.Result);
      promise.Resolve(value);
    }
    finally
    {
      promise.Dispose();
      asyncRuntime.Dispose();
    }
  }));
```

Here `asyncRuntime` is not the borrowed `args` object and not a borrowed
`JavaScriptUnownedValue`. It represents durable scheduler/runtime access that
was captured before the callback returned and is valid until the scheduled
completion releases it. The real implementation must also check cancellation,
exception state, and whether the runtime is still valid.

## Mapping To The Repo Strategy

Clean research repo:

- headless core;
- wrapper semantics;
- generated-looking proof;
- NativeAOT audit;
- maybe adapter interface sketch.

This repository:

- plans now;
- RNW adapter later;
- expo-desktop connector integration;
- Windows build and packaging.

<windows-test-machine>:

- RNW app proof;
- Visual Studio/MSBuild;
- Windows NativeAOT;
- WinUI views and screenshots.

## Pitfalls

- Starting with RNW before proving the headless core. Packaging failures then
  obscure bridge design failures.
- Letting view APIs force WinUI/AppKit into the core.
- Treating React Native macOS as already available. It is a future proof target,
  not a current dependency.
- Resolving promises from the wrong thread.
- Making platform adapters responsible for generated v2 module invocation.
