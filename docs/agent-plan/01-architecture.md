# 01 - Architecture And Boundaries

Last refreshed: 2026-06-29.

## Purpose

This file defines the current architecture for the portable C# / JSI bridge in
this repository. It is no longer only a pre-implementation research plan. Future
agents should read it before changing the ABI, low-level wrappers, generated
module proof code, or platform-adapter direction.

If a proposed implementation violates these boundaries, stop and ask for user
review instead of blending conflicting patterns.

## Current Implementation Baseline

The repo currently has these concrete pieces:

- `native/include/expo_jsi.h`: C ABI function table, opaque runtime/value/
  promise/arguments handles, task scheduling, sync execution, string/error
  result structs, and wrapper operations.
- `native/testhost/`: Hermes-backed native test host for the managed test suite.
- `managed/packages/Expo.JSI/`: low-level managed wrapper package targeting
  `net10.0` with unsafe function-pointer interop.
- `managed/packages/Expo.JSI.Tests/`: Hermes-backed xUnit suite that exercises
  runtime, values, object/array/function wrappers, promises, scoped refs,
  host functions, ABI layout, scheduler/runtime-loop behavior, and temporary
  module conversion proofs.
- `experiments/`: standalone HostFXR, NativeAOT, and Hermes/HostFXR proofs.
- `docs/spike-results/`: proof notes for completed loader and Hermes bridge
  spikes.

The current code package name is `Expo.JSI`, not `Expo.CSharpJsi`. The ABI name
prefix is `expo_jsi_*`, not `expo_csharp_jsi_*`.

## The Architecture Rule

The bridge has one governing rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

Meaning:

- C++ receives or creates the real `facebook::jsi::Runtime`.
- C++ creates, reads, writes, retains, and releases actual JSI values.
- C++ owns host object and host function mechanics.
- C++ catches C++ exceptions and converts them to ABI error results.
- C# implements module classes, typed business logic, generated bindings, and
  managed wrappers around opaque handles.
- C# never interprets the memory layout of `jsi::Runtime`, `jsi::Value`,
  `jsi::Object`, `jsi::Function`, Hermes internals, or React Native internals.

This is the main difference from Swift. Swift can expose C++-reference-like JSI
types through Swift/C++ interop and API notes. C# should expose similar concepts
through a C ABI, not through raw C++ classes.

## Current Layering

The desired layering is:

```text
React Native host platform
  RNW first when adapter work starts
  future React Native macOS proof only after approval

Thin platform adapter
  installs the bridge into the host
  supplies JS scheduler, lifecycle, logging, and platform services
  optionally supplies native view adapter

Portable C++ JSI bridge
  owns jsi::Runtime, jsi::Value, jsi::Object, jsi::Function
  owns HostObject, HostFunction, Promise conversion, and exception boundaries
  exposes opaque handles and function tables through C ABI

C ABI: native/include/expo_jsi.h
  opaque runtime/value/promise/arguments handles
  primitive values, pointers, lengths, and explicit result structs
  explicit release functions, callback context release, runtime-task scheduling

Low-level C# bridge API: managed/packages/Expo.JSI
  JavaScriptRuntime
  JavaScriptValue / JavaScriptValueRef
  JavaScriptObject / JavaScriptObjectRef
  JavaScriptArray / JavaScriptArrayRef
  JavaScriptFunction
  JavaScriptPromise and promise result helpers
  JavaScriptArguments
  JavaScriptErrorObject

Future C# module layer: Expo.ModulesCore
  authored module DSL
  generated v2 providers
  typed converters
  module registry and dispatch

Loader modes and experiments
  HostFXR for early development experiments
  NativeAOT-compatible entry points and ABI constraints for later proof
```

The platform adapter should be thin. It should know how to mount the proven
core into RNW or React Native macOS. It should not contain the portable C#
module system, ordinary type conversion rules, or generated v2 invocation
mechanics.

## Loader Choice Is Not Runtime Design

HostFXR is useful because a native process can load a framework-dependent .NET
assembly during fast local research iteration. The HostFXR experiments prove
loader feasibility and bridge shape, but `Expo.JSI` must remain loader-neutral.

NativeAOT remains a distribution and compatibility target. It changes
deployment constraints:

- publish for a specific runtime identifier;
- keep entry points blittable;
- avoid dynamic code paths that break trimming or AOT;
- produce Windows artifacts on Windows when testing RNW.

These are loader/deployment choices. They must not leak into the v2 runtime
binding design. In particular, HostFXR does not grant permission to build v2
around runtime reflection.

Hard rule for generated v2 runtime code:

- no runtime `Assembly.GetTypes()` module discovery in the normal path;
- no `MethodInfo.Invoke()` for normal module calls;
- no `Delegate.DynamicInvoke()` for normal module calls;
- no JSON serialization for ordinary JSI arguments or return values;
- no `object?[]` as the normal fast-path argument container.

Attributes are compile-time metadata for a Roslyn source generator. Generated
code should use direct calls and typed conversions.

## Universal Headless Core

The universal/headless core is the part that should work without RNW, WinUI,
AppKit, or app packaging.

It includes:

- C++ bridge source that owns JSI interaction;
- the `expo_jsi` C ABI and function table;
- handle ownership and release model;
- C# wrapper types in `Expo.JSI`;
- generated-looking module proofs until `Expo.ModulesCore` exists;
- primitive, string, object, array, function, promise, and error conversion;
- host function creation and callback context lifetime;
- runtime task scheduling and sync execution hooks;
- structured errors;
- headless Hermes-backed tests and proof executables.

The headless core may include an interface for platform services, but it must
not depend on RNW types, WinUI types, AppKit types, Windows App SDK packages,
or React Native macOS project files.

## Platform-Gated Adapters

Platform-gated code is everything that depends on a real host:

- RNW package registration;
- expo-desktop runtime installation;
- React Native macOS package installation;
- JS thread scheduling supplied by a host;
- lifecycle hooks;
- host logging;
- native view manager registration;
- WinUI/XAML/composition objects;
- AppKit objects;
- Windows packaging and Visual Studio/MSBuild project files.

Adapters can call into the universal core. The universal core should not call
adapter-specific APIs directly. If a core operation needs scheduling, logging,
or platform services, pass an explicit service table or adapter interface.

## JS Scheduling Capability

React Native hosts expose a way to post work back to the JavaScript runtime
thread. In React Native code this may be a `react::CallInvoker`,
`react::RuntimeExecutor`, `RuntimeScheduler`, or a host-specific wrapper around
one of those concepts.

The portable bridge models this as an adapter-provided scheduler. The ABI
already has runtime task operations such as `runtime_schedule_task`,
`runtime_can_execute_sync`, `runtime_execute_sync`, and `runtime_drain_tasks`.

Use scheduling only when work must touch JSI after the current host-function
callback has returned or from a non-JS thread. A synchronous host function is
already running in a valid JS callback frame, so it should decode arguments and
return directly without scheduling. Async continuations, promise settlement,
event emission, retained-handle cleanup that must touch JSI, and platform
callbacks must go through the runtime scheduler.

Important naming rule: a sync capability check must not hide an active sync
probe. If native code would need to enqueue work onto the runtime thread and
wait for it, calling that probe from the runtime thread can deadlock. Keep the
passive capability check distinct from any active execution/probe operation.

## C ABI Shape

The current ABI lives in `native/include/expo_jsi.h`. Its direction is:

- one runtime handle type;
- one ordinary value handle type for values, objects, arrays, and functions;
- a separate promise capability handle;
- a separate arguments handle for host-function arguments;
- explicit value and promise release functions;
- UTF-8 string result buffers with explicit release callbacks;
- structured error results;
- function pointers grouped in `expo_jsi_api`.

Representative current concepts:

```c
typedef struct expo_jsi_runtime_t *expo_jsi_runtime_handle;
typedef struct expo_jsi_value_t *expo_jsi_value_handle;
typedef struct expo_jsi_promise_t *expo_jsi_promise_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;

typedef struct expo_jsi_value_result {
  int32_t ok;
  expo_jsi_value_handle value;
  expo_jsi_error error;
} expo_jsi_value_result;

typedef struct expo_jsi_api {
  uint32_t size;
  uint32_t version;
  expo_jsi_create_number_fn create_number;
  expo_jsi_create_object_fn create_object;
  expo_jsi_create_array_fn create_array;
  expo_jsi_create_host_function_fn create_host_function;
  expo_jsi_create_promise_fn create_promise;
  expo_jsi_runtime_schedule_task_fn runtime_schedule_task;
  expo_jsi_runtime_execute_sync_fn runtime_execute_sync;
} expo_jsi_api;
```

Do not widen the ABI speculatively. Add functions only when a proof or test
needs them, and update the relevant spec or plan.

## C# Wrapper Shape

The C# public surface should feel familiar to an Expo/JSI reader while staying
honest about C# and ABI constraints:

```csharp
public sealed class JavaScriptRuntime
{
  public JavaScriptValue CreateNumber(double value);
  public JavaScriptValue CreateBool(bool value);
  public JavaScriptValue CreateString(string value);
  public JavaScriptObject Global();
  public JavaScriptObject CreateObject();
  public JavaScriptArray CreateArray(uint length = 0);
  public JavaScriptFunction CreateHostFunction(...);
  public JavaScriptPromise CreatePromise();
  public Task<T> ExecuteAsync<T>(Func<JavaScriptRuntime, T> body, ...);
  public T Execute<T>(Func<JavaScriptRuntime, T> body);
}

public sealed class JavaScriptValue : IDisposable
{
  public JavaScriptObject AsObject();
  public JavaScriptArray AsArray();
  public JavaScriptValue AsValue();
  public JavaScriptValueRef Ref { get; }
}

public readonly ref struct JavaScriptValueRef
{
  public JavaScriptObjectRef AsObject();
  public JavaScriptArrayRef AsArray();
  public JavaScriptValue Retain();
}
```

The important semantics are:

- owned wrappers dispose native handles;
- scoped refs are temporary inspection views and cannot escape their execution
  frame in ordinary C# code;
- `AsObject()`, `AsArray()`, `AsValue()`, and `Retain()` are explicit ownership
  transitions;
- all wrapper methods call the C ABI; none interpret native class layouts;
- module-facing generated code should consume these wrappers rather than
  adding module DSL behavior to `Expo.JSI`.

## Memory And Lifetime Rules

Future agents must not guess at ownership.

Runtime handles:

- Borrowed from the host or test host.
- Valid only according to the runtime/thread rules supplied by the host.
- Not retained or released by ordinary C# wrappers.

Scoped refs:

- Used for arguments and temporary traversal during a host-function callback or
  runtime execution frame.
- Must not be stored in fields, captured by async continuations, or returned as
  owned values without `Retain()` or another explicit ownership transition.

Owned value wrappers:

- Created by native bridge factory functions or by retaining scoped refs.
- Must be disposed exactly once by the owning wrapper unless ownership is
  detached/transferred to native return handling.

Strings:

- ABI strings are UTF-8 pointer + byte length.
- Owned string buffers include a release callback.
- C# copies them to managed `string`.

Callbacks:

- Host functions receive a function pointer plus opaque context.
- Any context allocated by C# must have an explicit release callback.
- Native must call the release callback exactly once when the host function is
  destroyed.

Promises:

- `JavaScriptPromise` wraps a native promise capability, not merely a JS value.
- `JavaScriptPromiseValue` owns the JS promise value.
- Resolve/reject must run on the correct runtime path.

Errors:

- C++ exceptions do not cross the C ABI.
- Managed exceptions do not cross unmanaged frames.
- Convert failures into structured `ok/error` results or JS errors.

## Current Decisions And Open Questions

Current decisions:

- This repository is now the implementation home for the portable core.
- `Expo.JSI` stays low-level.
- `Expo.ModulesCore` is the next higher-level package boundary.
- Temporary module conversion proofs may live in `Expo.JSI.Tests/Modules` only
  until `Expo.ModulesCore.Tests` exists.
- The ABI should keep moving toward the slim value-handle model documented by
  the latest specs.

Open questions to preserve as decision points:

- What exact public DSL shape should `Expo.ModulesCore` expose?
- Which converters belong in `Expo.ModulesCore` first?
- How should source-generator diagnostics describe unsupported parameter and
  return types?
- What NativeAOT proof is required before treating the generated module path as
  production-ready?
- Which platform adapter should be implemented first after the portable module
  layer is stable?
