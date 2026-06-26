# 01 - Architecture And Boundaries

## Purpose

This file defines the target architecture for the portable C# / JSI bridge
research track. Future agents should read it before writing any proof code. If
a proposed implementation violates these boundaries, stop and ask for user
review instead of blending patterns.

## Assumptions

- The current repository is `expo-modules-windows-core`.
- The current Windows proof and repo restructure are separate work streams.
- `docs_old/` is historical reference material and must not be edited by this
  planning track.
- The first implementation should be a small research proof, not a production
  bridge.
- The user wants the C# API to feel similar in spirit to Expo's Swift
  `expo-modules-jsi` wrappers, but C# cannot use Swift-style direct C++ wrapper
  mechanics.

## The Architecture Rule

The bridge has one governing rule:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

Meaning:

- C++ receives or creates the real `jsi::Runtime`.
- C++ creates, reads, writes, retains, and releases actual JSI values.
- C++ owns host object and host function mechanics.
- C++ catches C++ exceptions and converts them to ABI error results.
- C# implements module classes, typed business logic, generated bindings, and
  managed wrappers around opaque handles.
- C# never interprets the memory layout of `jsi::Runtime`, `jsi::Value`,
  `jsi::Object`, `jsi::Function`, Hermes internals, or RNW internals.

This is the main difference from Swift. Swift can expose C++-reference-like JSI
types through Swift/C++ interop and API notes. C# should expose similar concepts
through a C ABI, not through raw C++ classes.

## Target Layering

The desired long-term layering is:

```text
React Native host platform
  RNW first
  future React Native macOS proof
  later hosts only if the bridge remains host-neutral

Thin platform adapter
  installs the bridge into the host
  supplies JS scheduler, lifecycle, logging, and platform services
  optionally supplies native view adapter

Portable C++ JSI bridge
  owns jsi::Runtime, jsi::Value, jsi::Object, jsi::Function
  owns HostObject, HostFunction, Promise conversion, and exception boundaries
  exposes opaque handles and function tables through C ABI

C ABI
  opaque runtime/value/object/function/buffer/callback/promise handles
  primitive values, pointers, lengths, and explicit result structs
  explicit retain/release, callback context disposal, and schedule-on-JS hooks

C# bridge API
  JavaScriptRuntime
  JavaScriptUnownedValue
  JavaScriptValue
  JavaScriptObject
  JavaScriptFunction
  JavaScriptArguments
  JavaScriptArrayBuffer or buffer wrappers
  ModuleRegistry and generated v2 provider

Loader mode
  HostFXR first for development
  NativeAOT-compatible entry points and ABI for later proof
```

The platform adapter should be thin. It should know how to mount the bridge
into RNW or React Native macOS. It should not contain the portable C# module
system, ordinary type conversion rules, or generated v2 invocation mechanics.

## Loader Choice Is Not Runtime Design

HostFXR is the first loader because it makes macOS research fast:

- build C# with normal `dotnet build`;
- load a framework-dependent assembly from a native host;
- iterate without solving NativeAOT packaging on day one.

NativeAOT is a later proof because it changes deployment constraints:

- publish for a specific runtime identifier such as `osx-arm64` or `win-x64`;
- keep entry points blittable;
- avoid dynamic code paths that break trimming or AOT;
- produce Windows artifacts on Windows when testing RNW.

These are loader/deployment choices. They must not leak into the v2 runtime
binding design. In particular, HostFXR does not grant permission to build v2
around runtime reflection. The v2 bridge must be designed as if NativeAOT will
consume the same generated code and C ABI later.

Hard rule for generated v2 runtime code:

- no runtime `Assembly.GetTypes()` module discovery in the normal path;
- no `MethodInfo.Invoke()` for normal module calls;
- no `Delegate.DynamicInvoke()` for normal module calls;
- no JSON serialization for ordinary JSI arguments or return values;
- no `object?[]` as the normal fast-path argument container.

Attributes are compile-time metadata for a Roslyn source generator. Generated
code should use direct calls and typed conversions.

## Universal Headless Core

The universal/headless core is the part that should be provable on macOS
without RNW, WinUI, AppKit, or app packaging.

It includes:

- C++ bridge source that owns JSI interaction;
- a C ABI header and function table;
- handle table and ownership model;
- HostFXR loader proof;
- NativeAOT-compatible entry point shape;
- C# wrapper types;
- generated-looking module provider;
- primitive conversion;
- string conversion;
- object property get/set;
- host function creation;
- callback context lifetime;
- promise resolve/reject model, even if scheduler integration is stubbed;
- structured errors;
- headless tests and proof executables.

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

React Native hosts expose a concept commonly called a JS call invoker: a way to
post work back to the JavaScript runtime thread. In React Native code this may
be a `react::CallInvoker`, `react::RuntimeExecutor`, `RuntimeScheduler`, or a
host-specific wrapper around one of those concepts.

The portable C# bridge needs this capability, but it should not depend on the
React Native C++ type directly. Model it as an adapter-provided scheduler:

```text
portable core
  depends on schedule_on_js callback / JavaScriptScheduler abstraction

RNW adapter
  implements schedule_on_js with CallInvoker, RuntimeExecutor, or RuntimeScheduler

React Native macOS adapter
  implements schedule_on_js with the scheduler exposed by RN macOS

headless proof
  implements schedule_on_js as immediate/same-thread or a tiny event loop
```

Use the scheduler only when work must touch JSI after the current host-function
callback has returned or from a non-JS thread. A synchronous host function is
already running in a valid JS callback frame, so it should decode arguments and
return directly without scheduling. Async continuations, promise settlement,
event emission, retained-handle cleanup that must touch JSI, and platform
callbacks must go through the scheduler.

The scheduler is a runtime capability, not a C# business-logic facility. C#
generated bindings may call a managed wrapper such as `JavaScriptAsyncRuntime`
or `JavaScriptScheduler`, but the actual posting to the JS queue is implemented
by the platform adapter.

## C ABI Shape

The ABI should be small and explicit at first. Use opaque handles, primitive
types, pointer + length pairs, result structs, and explicit release functions.

Example shape:

```c
typedef struct expo_js_runtime_t *expo_js_runtime_handle;
typedef struct expo_js_value_t *expo_js_value_handle;
typedef struct expo_js_object_t *expo_js_object_handle;
typedef struct expo_js_function_t *expo_js_function_handle;
typedef struct expo_js_buffer_t *expo_js_buffer_handle;

typedef void (*expo_js_task_callback)(
  void *task_context);

typedef enum expo_js_task_priority {
  EXPO_JS_TASK_IMMEDIATE = 0,
  EXPO_JS_TASK_NORMAL = 1
} expo_js_task_priority;

typedef enum expo_js_value_kind {
  EXPO_JS_UNDEFINED = 0,
  EXPO_JS_NULL = 1,
  EXPO_JS_BOOL = 2,
  EXPO_JS_NUMBER = 3,
  EXPO_JS_STRING = 4,
  EXPO_JS_OBJECT = 5,
  EXPO_JS_FUNCTION = 6,
  EXPO_JS_ARRAY_BUFFER = 7
} expo_js_value_kind;

typedef struct expo_js_error {
  int32_t code;
  const char *message;
  int32_t message_len;
} expo_js_error;

typedef struct expo_js_value_result {
  int32_t ok;
  expo_js_value_handle value;
  expo_js_error error;
} expo_js_value_result;

typedef struct expo_js_object_result {
  int32_t ok;
  expo_js_object_handle object;
  expo_js_error error;
} expo_js_object_result;

typedef struct expo_js_function_result {
  int32_t ok;
  expo_js_function_handle function;
  expo_js_error error;
} expo_js_function_result;

typedef struct expo_js_string_result {
  int32_t ok;
  const uint8_t *utf8;
  int32_t len;
  void *release_context;
  void (*release)(void *release_context);
  expo_js_error error;
} expo_js_string_result;

typedef struct expo_js_scheduler {
  void *context;
  void (*schedule_on_js)(
    void *context,
    expo_js_task_priority priority,
    expo_js_task_callback callback,
    void *task_context);
  int32_t (*is_runtime_valid)(void *context);
} expo_js_scheduler;
```

Representative functions:

```c
expo_js_value_result expo_js_runtime_create_number(
  expo_js_runtime_handle runtime,
  double value);

expo_js_value_result expo_js_runtime_create_bool(
  expo_js_runtime_handle runtime,
  int32_t value);

expo_js_value_result expo_js_runtime_create_string_utf8(
  expo_js_runtime_handle runtime,
  const uint8_t *value,
  int32_t value_len);

expo_js_object_result expo_js_runtime_create_object(
  expo_js_runtime_handle runtime);

expo_js_value_kind expo_js_value_get_kind(
  expo_js_runtime_handle runtime,
  expo_js_value_handle value);

double expo_js_value_get_double(
  expo_js_runtime_handle runtime,
  expo_js_value_handle value,
  expo_js_error *error);

expo_js_string_result expo_js_value_get_string_utf8(
  expo_js_runtime_handle runtime,
  expo_js_value_handle value);

expo_js_value_result expo_js_object_get_property_utf8(
  expo_js_runtime_handle runtime,
  expo_js_object_handle object,
  const uint8_t *name,
  int32_t name_len);

int32_t expo_js_object_set_property_utf8(
  expo_js_runtime_handle runtime,
  expo_js_object_handle object,
  const uint8_t *name,
  int32_t name_len,
  expo_js_value_handle value,
  expo_js_error *error);

typedef expo_js_value_result (*expo_js_host_function_callback)(
  expo_js_runtime_handle runtime,
  expo_js_value_handle this_value,
  const expo_js_value_handle *args,
  int32_t arg_count,
  void *callback_context);

expo_js_function_result expo_js_runtime_create_host_function_utf8(
  expo_js_runtime_handle runtime,
  const uint8_t *name,
  int32_t name_len,
  int32_t param_count,
  expo_js_host_function_callback callback,
  void *callback_context,
  void (*release_callback_context)(void *callback_context));

void expo_js_value_retain(expo_js_value_handle value);
void expo_js_value_release(expo_js_value_handle value);

expo_js_value_result expo_js_object_as_value(
  expo_js_runtime_handle runtime,
  expo_js_object_handle object);

expo_js_value_result expo_js_function_as_value(
  expo_js_runtime_handle runtime,
  expo_js_function_handle function);
```

These functions are representative, not final. They are included because the
tutorial examples use runtime-backed factories such as `CreateNumber`,
`CreateBool`, `CreateObject`, and `CreateHostFunction`. Do not widen the ABI
speculatively beyond the proof. Add functions only when a spike needs them, and
document the proof that forced the addition.

This draft deliberately keeps object/function handles distinct from generic
value handles. `expo_js_runtime_create_object` returns an
`expo_js_object_handle`, because property APIs require an object handle. When an
object or function must cross an API that expects a JS value, use an explicit
conversion such as `expo_js_object_as_value` or `expo_js_function_as_value`.
Future proofs may choose a single tagged value-handle representation instead,
but they must update this section and the wrapper tutorial together; do not
leave object/value mapping implicit.

## C# Wrapper Shape

The C# public surface should look familiar to an Expo/JSI reader:

```csharp
public readonly ref struct JavaScriptUnownedValue
{
  public JavaScriptValueKind Kind { get; }
  public double AsDouble();
  public string AsString();
  public JavaScriptObject AsObject();
}

public sealed class JavaScriptValue : IDisposable
{
  public JavaScriptValueKind Kind { get; }
  public void Dispose();
}

public sealed class JavaScriptObject : IDisposable
{
  public JavaScriptValue GetProperty(string name);
  public void SetProperty(string name, JavaScriptValue value);
  public JavaScriptValue AsValue();
}

public sealed class JavaScriptFunction : IDisposable
{
  public JavaScriptValue AsValue();
}

public sealed class JavaScriptRuntime
{
  public JavaScriptValue CreateNumber(double value);
  public JavaScriptValue CreateBool(bool value);
  public JavaScriptObject CreateObject();
  public JavaScriptValue CreateString(string value);
  public JavaScriptFunction CreateHostFunction(...);
}
```

This is illustrative, not final API law. The important semantics are:

- `JavaScriptUnownedValue` is borrowed and cannot escape the current call.
- factory methods such as `CreateNumber` and `CreateBool` call the C ABI to ask
  the native bridge to create a real JSI value and return an owned retained
  handle.
- `JavaScriptValue` owns a retained native handle and must release it unless
  ownership is explicitly transferred to the native bridge.
- object/function wrappers are typed views over handles with clear ownership.
- `JavaScriptObject.AsValue()` and `JavaScriptFunction.AsValue()` are explicit
  wrapper conversions that call `expo_js_object_as_value` and
  `expo_js_function_as_value`; examples should not pass object/function handles
  where a `JavaScriptValue` is required without such a conversion.
- all wrapper methods call the C ABI; none interpret native class layouts.

## Memory And Lifetime Rules

Future agents must not guess at ownership. Use these rules until a later proof
updates them explicitly.

Runtime handles:

- Borrowed from the host or bridge initialization.
- Valid only on the JS runtime/thread rules supplied by the host.
- Not retained or released by ordinary C# wrappers.
- C# may hold a runtime wrapper only as long as the bridge lifetime permits.

Borrowed value handles:

- Used for arguments and temporary values during a native call.
- Valid only until the host function callback returns.
- Must not be stored in fields, captured by async continuations, or returned as
  owned values without an explicit retain/copy operation.

Owned value handles:

- Created by native bridge factory functions or by retaining borrowed values.
- Must be released exactly once by the owning wrapper.
- May escape the current call if thread/runtime rules are respected.

Strings:

- ABI returns string data as UTF-8 pointer + byte length.
- Native bridge must specify whether the bytes are borrowed or owned.
- Owned string buffers must include a release callback or release function.
- C# should copy to managed `string` unless a short-lived span API is explicitly
  proven safe.

Buffers:

- Borrowed buffers expose pointer + length only during the valid borrow window.
- Owned buffers need explicit retain/release or copy semantics.
- C# must not keep a raw pointer beyond its documented lifetime.
- Mutability must be explicit: read-only borrowed bytes are not mutable JS
  ArrayBuffers.

Callbacks:

- Host functions receive a function pointer plus opaque context.
- Any context allocated by C# must have an explicit release callback.
- The native bridge must call the release callback exactly once when the host
  function is destroyed.
- Do not use `Marshal.GetFunctionPointerForDelegate` as the core v2 model
  unless a proof documents why it is safe for lifetime and NativeAOT.

Promises:

- JS promises are native-owned JS objects.
- C# may receive resolve/reject handles or wrapper functions.
- Resolution must happen on the correct JS runtime/thread according to host
  scheduler rules.
- A headless proof may use a simple scheduler, but the adapter boundary must
  make the real host scheduler explicit.
- A real React Native adapter should map the scheduler to the host's
  `CallInvoker`, `RuntimeExecutor`, or `RuntimeScheduler` equivalent; do not
  make the portable core include React Native headers just to schedule async
  continuations.

JS scheduler:

- The scheduler is adapter-owned and runtime-bound.
- It is required for async `Task` continuations, promise resolve/reject, events
  emitted after the original JS call, and any retained JSI cleanup that must run
  on the JS runtime thread.
- It is not required for the body of a synchronous host function that is already
  executing on the JS runtime thread.
- C# must not assume `.NET Task` continuations resume on the JS thread.
- Headless tests may run scheduled work immediately, but the proof must still
  pass through the scheduler abstraction so the real adapter seam is exercised.

Errors:

- C++ exceptions do not cross the C ABI.
- Managed exceptions do not cross unmanaged frames.
- Convert native failures into structured `ok/error` results.
- Convert managed failures into structured rejection/throw results at the ABI
  boundary.
- Include at least code, message, and optional native detail in proof artifacts.

## Updated Decisions And Open Questions

Updated decisions from the old research note:

- A clean separate research repo is now the recommended phase 1 location, not
  just one option. This repo remains the planning source and later RNW adapter
  home.
- Views are explicitly outside the first universal headless proof.
- The generated-looking C# module proof is mandatory before building a source
  generator.
- The proof plan must stop for user review before creating a new repo, changing
  production code, or adding a real host app.

Open questions to preserve as decision points:

- Should owned wrappers derive from `SafeHandle`, implement `IDisposable`
  directly, or use specialized structs for hot paths?
- What minimal C ABI is enough for useful modules before arrays, typed arrays,
  and records are added?
- How should JS-thread confinement be represented in C# type names or runtime
  guards?
- What v1 reflection compatibility must remain, and how is it isolated from v2?
- Should v1 and v2 share one registry or merge generated providers at startup?
- How much existing Expo C++ JSI utility code can be reused directly across RNW
  and React Native macOS?
