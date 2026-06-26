# 02 - JSI Wrapper Model And Lifetime Rules

## Why Wrappers Exist

JSI is a C++ API. It exposes concepts such as runtime, value, object, function,
host object, and host function. Expo's Swift bridge gives Swift developers
typed wrappers around those concepts. The C# bridge should provide the same
kind of comfort, but through different mechanics.

Swift can use Swift/C++ interop. C# should not directly wrap C++ JSI classes.
Instead:

```text
C# wrapper -> C ABI function -> C++ bridge -> JSI object
```

The wrapper should make module code pleasant while preserving the real ownership
rules.

## Core Terms

Runtime:

The JS runtime where values live. A JSI value from one runtime cannot be freely
used in another runtime. The runtime also implies thread-affinity rules.

Borrowed value:

A handle that is valid only for a short window, usually during one host
function call. Borrowed values are fast, but they cannot be stored.

Owned value:

A retained handle with a release responsibility. Owned values can escape a call
when runtime/thread rules allow.

Wrapper:

A C# type that stores a runtime handle plus a value/object/function handle and
knows whether it owns the handle.

## Proposed Wrapper Family

Illustrative shape:

```csharp
public sealed class JavaScriptRuntime
{
  private readonly IntPtr _runtime;

  public JavaScriptValue CreateNumber(double value);
  public JavaScriptValue CreateBool(bool value);
  public JavaScriptObject CreateObject();
  public JavaScriptValue CreateString(string value);
  public JavaScriptFunction CreateHostFunction(string name, HostFunction callback);
}

public sealed class JavaScriptObject : IDisposable
{
  public JavaScriptValue AsValue();
}

public sealed class JavaScriptFunction : IDisposable
{
  public JavaScriptValue AsValue();
}

public readonly ref struct JavaScriptUnownedValue
{
  private readonly IntPtr _runtime;
  private readonly IntPtr _value;

  public JavaScriptValueKind Kind { get; }
  public double AsDouble();
  public string AsString();
  public JavaScriptObject AsObjectBorrowed();
}

public sealed class JavaScriptValue : IDisposable
{
  private readonly IntPtr _runtime;
  private IntPtr _value;

  public void Dispose();
}
```

The exact API may change. The distinction must not: borrowed wrappers are
temporary; owned wrappers release.

## What Happens Across The Bridge

When C# module code calls a wrapper method, it is not calling JSI directly. It is
calling a managed method that immediately delegates to a C ABI function table.
That C ABI function runs in native code, recovers the bridge-owned C++ wrapper
state from opaque handles, and then calls real `facebook::jsi` APIs.

For example, this C# call:

```csharp
using var obj = runtime.CreateObject();
```

should mean roughly:

1. `JavaScriptRuntime.CreateObject()` passes its opaque runtime handle to a C ABI
   function such as `expo_js_runtime_create_object`.
2. The C ABI function casts the opaque handle back to an internal C++ bridge
   struct.
3. The C++ bridge reads the real `facebook::jsi::Runtime&` from that struct.
4. The C++ bridge calls JSI, for example `facebook::jsi::Object(runtime)`.
5. The C++ bridge stores the resulting C++ JSI object inside a bridge-owned
   handle record.
6. The C ABI returns that opaque object handle to C#.
7. C# wraps the returned handle in `JavaScriptObject` and becomes responsible
   for releasing it unless ownership is transferred.

This tutorial uses distinct object and value handles. A `JavaScriptObject`
contains an `expo_js_object_handle`, not a generic `expo_js_value_handle`.
Property APIs therefore take object handles directly. If an object needs to be
returned where a normal JS value is expected, the wrapper must explicitly call a
C ABI conversion such as `expo_js_object_as_value`; it should not pretend that
the handle types are interchangeable.

An illustrative C# wrapper implementation could look like:

```csharp
public sealed unsafe class JavaScriptRuntime
{
  private readonly IntPtr _runtime;
  private readonly ExpoJsiApi* _api;

  public JavaScriptObject CreateObject()
  {
    ExpoJsObjectResult result = _api->CreateObject(_runtime);
    if (result.Ok == 0)
    {
      throw JavaScriptBridgeException.From(result.Error);
    }

    return new JavaScriptObject(this, result.ObjectHandle, ownsHandle: true);
  }
}

public unsafe struct ExpoJsiApi
{
  public delegate* unmanaged<IntPtr, ExpoJsObjectResult> CreateObject;
  public delegate* unmanaged<IntPtr, IntPtr, ExpoJsError*, int> ReleaseObject;
}

public readonly struct ExpoJsObjectResult
{
  public readonly int Ok;
  public readonly IntPtr ObjectHandle;
  public readonly ExpoJsError Error;
}
```

The matching C ABI declaration might look like:

```c
typedef struct expo_js_runtime_t *expo_js_runtime_handle;
typedef struct expo_js_object_t *expo_js_object_handle;

typedef struct expo_js_object_result {
  int32_t ok;
  expo_js_object_handle object;
  expo_js_error error;
} expo_js_object_result;

expo_js_object_result expo_js_runtime_create_object(
  expo_js_runtime_handle runtime);

expo_js_value_result expo_js_object_as_value(
  expo_js_runtime_handle runtime,
  expo_js_object_handle object);

void expo_js_object_release(expo_js_object_handle object);
```

The native implementation is where JSI is actually touched:

```cpp
#include <jsi/jsi.h>

using namespace facebook;

struct expo_js_runtime_t {
  jsi::Runtime *runtime;
};

struct expo_js_object_t {
  jsi::Runtime *runtime;
  jsi::Object object;
  uint32_t ref_count;
};

extern "C" expo_js_object_result expo_js_runtime_create_object(
  expo_js_runtime_handle runtime_handle) {
  try {
    auto *runtime_state =
      reinterpret_cast<expo_js_runtime_t *>(runtime_handle);
    jsi::Runtime &runtime = *runtime_state->runtime;

    auto *object_state = new expo_js_object_t{
      &runtime,
      jsi::Object(runtime),
      1,
    };

    return expo_js_object_result{
      1,
      object_state,
      expo_js_error{},
    };
  } catch (const std::exception &error) {
    return expo_js_object_result{
      0,
      nullptr,
      expo_js_error_from_exception(error),
    };
  }
}

extern "C" void expo_js_object_release(expo_js_object_handle object_handle) {
  auto *object_state = reinterpret_cast<expo_js_object_t *>(object_handle);
  if (object_state == nullptr) {
    return;
  }
  if (--object_state->ref_count == 0) {
    delete object_state;
  }
}
```

The exact structs may change. The important lesson is that C# never casts the
handle to `jsi::Object`. C# only stores an opaque pointer. Native C functions
cast that pointer back to bridge-owned C++ state, and that state owns or borrows
the real JSI object according to the lifetime rules.

A property set follows the same path. C# might call:

```csharp
using var obj = runtime.CreateObject();
using var ok = runtime.CreateBool(true);
obj.SetProperty("ok", ok);
```

Internally, `SetProperty` calls a C ABI function:

```csharp
public unsafe void SetProperty(string name, JavaScriptValue value)
{
  byte[] nameBytes = Encoding.UTF8.GetBytes(name);
  fixed (byte* nameUtf8 = nameBytes)
  {
    ExpoJsError error;
    int ok = _api->SetPropertyUtf8(
      _runtime.Handle,
      _objectHandle,
      nameUtf8,
      Encoding.UTF8.GetByteCount(name),
      value.Handle,
      &error);

    if (ok == 0)
    {
      throw JavaScriptBridgeException.From(error);
    }
  }
}
```

The native function again recovers C++ bridge structs and calls JSI:

```cpp
extern "C" int32_t expo_js_object_set_property_utf8(
  expo_js_runtime_handle runtime_handle,
  expo_js_object_handle object_handle,
  const uint8_t *name,
  int32_t name_len,
  expo_js_value_handle value_handle,
  expo_js_error *error) {
  try {
    auto &runtime =
      *reinterpret_cast<expo_js_runtime_t *>(runtime_handle)->runtime;
    auto *object_state =
      reinterpret_cast<expo_js_object_t *>(object_handle);
    auto *value_state =
      reinterpret_cast<expo_js_value_t *>(value_handle);

    std::string property_name(
      reinterpret_cast<const char *>(name),
      static_cast<size_t>(name_len));

    object_state->object.setProperty(
      runtime,
      property_name.c_str(),
      value_state->value);

    return 1;
  } catch (const std::exception &ex) {
    *error = expo_js_error_from_exception(ex);
    return 0;
  }
}
```

This example is deliberately simple. Real code must also handle runtime/thread
checks, null handles, string encoding helpers, exception conversion, and whether
`expo_js_value_t` stores a `jsi::Value`, `jsi::Object`, or a tagged bridge-owned
variant. But the direction is fixed: C# wrapper methods call C ABI functions;
C ABI functions recover native bridge state; native bridge state calls JSI.

## Borrowed Values

Borrowed values are expected for arguments:

```csharp
static JavaScriptValue Add(JavaScriptUnownedValue thisValue, JavaScriptArguments args, object context)
{
  var a = args.UnownedValueAt(0).AsDouble();
  var b = args.UnownedValueAt(1).AsDouble();
  return args.Runtime.CreateNumber(a + b);
}
```

Rules:

- valid only during the callback;
- safe for immediate primitive reads;
- safe for immediate property reads if the property result has clear ownership;
- not safe to store in a field;
- not safe to capture into `Task`, lambda, or async continuation;
- not safe to return as owned without an explicit retain/copy.

Bad:

```csharp
private JavaScriptUnownedValue _lastArgument; // do not do this
```

Better:

```csharp
using var retained = args.UnownedValueAt(0).Retain();
_lastValue = retained.TransferOwnership();
```

The exact method names are illustrative. The important operation is explicit
retain or copy.

## Owned Values

Owned values are handles C# must release:

```csharp
using var result = runtime.CreateObject();
using var ok = runtime.CreateBool(true);
result.SetProperty("ok", ok);
using var resultValue = result.AsValue();
return resultValue.RetainForReturn();
```

Rules:

- release exactly once;
- do not use after release;
- do not release a borrowed handle;
- make transfer operations explicit;
- prefer `using` or `IDisposable` for normal owned wrappers;
- consider `SafeHandle` only after measuring overhead and ergonomics.

When an owned object or function must be returned as a generic JS value, convert
it explicitly. In this tutorial model, `JavaScriptObject.AsValue()` calls
`expo_js_object_as_value`, and `JavaScriptFunction.AsValue()` calls
`expo_js_function_as_value`. This keeps object/function handles distinct while
still allowing them to flow through APIs that expect `JavaScriptValue`.

Open decision:

Owned wrappers may be implemented as `SafeHandle`, normal `IDisposable` classes,
or specialized structs. Do not choose by taste. Choose after a proof compares
lifetime correctness, NativeAOT compatibility, and hot-path cost.

## Objects And Functions

`JavaScriptObject` should expose property operations:

```csharp
using var options = args.UnownedValueAt(0).AsObjectBorrowed().Retain();
using var path = options.GetProperty("path");
var pathString = path.AsString();
```

`JavaScriptFunction` should expose calls or host-function creation:

```csharp
using var fn = runtime.CreateHostFunction(
  "add",
  static (thisValue, args, context) =>
  {
    var module = (MathModule)context;
    return args.Runtime.CreateNumber(module.Add(
      args.UnownedValueAt(0).AsDouble(),
      args.UnownedValueAt(1).AsDouble()));
  },
  module);
```

The native bridge owns the actual JSI host function. If C# passes a context
object, the bridge needs a release path so the context is not leaked.

Primitive return values should still be created through the runtime wrapper.
For example, `args.Runtime.CreateNumber(value)` calls the C ABI and asks the
native bridge to create the actual JSI number. The returned `JavaScriptValue`
is an owned handle until the callback return path transfers it back to native.

## Strings

JSI strings live in the runtime. C# wants managed `string`.

Safe default:

1. C# asks native for UTF-8 bytes.
2. Native returns pointer + length + release callback.
3. C# copies bytes into managed `string`.
4. C# calls release callback.

This copy is acceptable for correctness in early proofs. Avoid borrowed string
spans until a proof defines the lifetime precisely.

Pitfall:

Do not return `const char*` without length and ownership. Null-terminated
strings are not enough for arbitrary JS strings and make release rules vague.

## Buffers And ArrayBuffers

Buffers are trickier because avoiding copies can matter later.

Borrowed buffer:

- pointer + length valid only during a callback;
- read-only unless explicitly marked mutable;
- cannot be stored.

Owned buffer:

- retained native buffer handle or managed copy;
- explicit release;
- can escape according to documented rules.

Project rule:

The first proof should prefer explicit correctness over zero-copy cleverness.
If a future optimization avoids a copy, document exactly who owns the bytes and
how mutation is synchronized.

## Callbacks

Callbacks cross in both directions:

- JS calls a host function implemented by C#.
- C# may hold or call a JS function handle.

For host functions:

```text
native JSI host function
  stores unmanaged callback pointer
  stores opaque context
  calls managed entry point
  releases context when host function is destroyed
```

Rules:

- callback pointer lifetime must outlive the native host function;
- context release must happen exactly once;
- managed exceptions must be caught before returning to native;
- callbacks must run on the correct JS runtime/thread.

## Host Function Call Walkthrough

This is the reverse direction from `runtime.CreateObject()`. Instead of C#
calling native to create a value, JS calls a JSI host function and native calls
managed code.

The flow should look like this:

```text
JS: Math.add(2, 3)
  -> C++ JSI HostFunction operator()
  -> C++ creates borrowed handles for thisValue and args
  -> C++ calls managed callback entry point
  -> C# generated callback decodes args and calls MathModule.Add
  -> C# creates owned return handle with args.Runtime.CreateNumber(5)
  -> managed callback returns expo_js_value_result
  -> C++ converts returned handle back to jsi::Value
  -> C++ releases callback-frame borrowed handles
```

Illustrative native host-function body:

```cpp
jsi::Function::createFromHostFunction(
  runtime,
  jsi::PropNameID::forAscii(runtime, "add"),
  2,
  [bridge, callback, context](
    jsi::Runtime &runtime,
    const jsi::Value &this_value,
    const jsi::Value *args,
    size_t count) -> jsi::Value {
      expo_callback_frame frame =
        bridge->borrow_callback_frame(runtime, this_value, args, count);

      expo_js_value_result result = callback(
        frame.runtime,
        frame.this_value,
        frame.args,
        static_cast<int32_t>(frame.arg_count),
        context);

      bridge->release_callback_frame(frame);

      if (!result.ok) {
        bridge->throw_js_error(runtime, result.error);
      }

      return bridge->take_value_for_js_return(runtime, result.value);
    });
```

Illustrative managed callback entry:

```csharp
private static unsafe ExpoJsValueResult InvokeAdd(
  IntPtr runtimeHandle,
  IntPtr thisValueHandle,
  IntPtr* argHandles,
  int argCount,
  IntPtr context)
{
  try
  {
    var runtime = JavaScriptRuntime.FromBorrowedHandle(runtimeHandle);
    var args = JavaScriptArguments.FromBorrowedHandles(
      runtime,
      argHandles,
      argCount);

    var module = (MathModule)GCHandle.FromIntPtr(context).Target!;
    double a = ExpoArgumentConverters.GetDouble(args, 0, "a");
    double b = ExpoArgumentConverters.GetDouble(args, 1, "b");

    return ExpoJsValueResult.Ok(
      runtime.CreateNumber(module.Add(a, b)).TransferToNative());
  }
  catch (Exception error)
  {
    return ExpoJsValueResult.Error(ExpoJsError.FromException(error));
  }
}
```

The important ownership details:

- `thisValueHandle` and `argHandles` are borrowed for this callback only;
- `JavaScriptArguments` must not store those handles after the callback;
- `runtime.CreateNumber(...)` returns an owned handle;
- `TransferToNative()` is illustrative shorthand for "C# no longer releases
  this handle; native will consume it as the JS return value";
- if C# returns an error result, native throws into JS or rejects a promise.

## Promises

Async Expo modules usually map to JS promises.

The important point is that C# should not implement JavaScript promises. The
real JS `Promise`, `resolve`, and `reject` functions are JSI values owned by
the native bridge. C# receives a managed wrapper around a native promise
capability:

```text
Promise capability =
  JS Promise object returned to JS
  + native-retained resolve function
  + native-retained reject function
  + scheduler needed to settle the promise later
```

That scheduler is the portable version of React Native's call-invoker concept.
The RNW adapter may implement it with `react::CallInvoker`,
`RuntimeExecutor`, or `RuntimeScheduler`; a React Native macOS adapter may use
the equivalent scheduler available there; a headless proof may run it
immediately. C# code should see only the managed wrapper around this capability,
not the React Native C++ type.

The end-to-end flow is:

```text
JS calls C# async module method
  -> native/C++ creates a real JS Promise object
  -> native/C++ retains resolve and reject functions
  -> C# receives an opaque promise handle
  -> C# immediately returns the Promise value to JS
  -> C# starts or awaits a .NET Task
  -> when the Task completes, C# schedules resolve/reject onto the JS runtime
  -> C++ performs the actual JSI resolve/reject call
```

An illustrative generated C# callback:

```csharp
static JavaScriptValue InvokeReadText(
  JavaScriptUnownedValue thisValue,
  JavaScriptArguments args,
  object context)
{
  var module = (FileModule)context;

  // Borrowed arguments are decoded synchronously during this callback.
  // Do not capture args or JavaScriptUnownedValue into the continuation.
  string path = ExpoArgumentConverters.GetString(args, 0, "path");

  // Capture durable async state before returning to JS.
  JavaScriptAsyncRuntime asyncRuntime = args.Runtime.CaptureForAsync();
  JavaScriptPromise promise = asyncRuntime.CreatePromise();
  JavaScriptValue promiseValue = promise.RetainPromiseValueForReturn();

  _ = module.ReadTextAsync(path).ContinueWith(task =>
  {
    // The task may complete on a .NET thread. JSI must be touched on the
    // JS runtime thread, so resolve/reject is scheduled through the adapter.
    asyncRuntime.ScheduleOnJs(() =>
    {
      try
      {
        if (task.IsCompletedSuccessfully)
        {
          using var value = asyncRuntime.CreateString(task.Result);
          promise.Resolve(value);
        }
        else
        {
          promise.Reject(ExpoError.FromException(task.Exception));
        }
      }
      finally
      {
        promise.Dispose();
        asyncRuntime.Dispose();
      }
    });
  });

  return promiseValue;
}
```

Conceptual C ABI functions:

```c
typedef struct expo_js_promise_t *expo_js_promise_handle;

typedef struct expo_js_promise_result {
  int32_t ok;
  expo_js_value_handle promise_value;
  expo_js_promise_handle promise;
  expo_js_error error;
} expo_js_promise_result;

expo_js_promise_result expo_js_runtime_create_promise(
  expo_js_runtime_handle runtime);

int32_t expo_js_promise_resolve(
  expo_js_runtime_handle runtime,
  expo_js_promise_handle promise,
  expo_js_value_handle value,
  expo_js_error *error);

int32_t expo_js_promise_reject(
  expo_js_runtime_handle runtime,
  expo_js_promise_handle promise,
  expo_js_error error);

void expo_js_promise_release(expo_js_promise_handle promise);
```

The C# wrapper methods are thin calls into that ABI:

```csharp
public sealed class JavaScriptPromise : IDisposable
{
  private readonly JavaScriptAsyncRuntime _runtime;
  private IntPtr _promise;
  private IntPtr _promiseValue;

  public JavaScriptValue RetainPromiseValueForReturn()
  {
    return JavaScriptValue.FromOwnedHandle(_runtime.RuntimeHandle, _promiseValue);
  }

  public void Resolve(JavaScriptValue value)
  {
    NativeAbi.ThrowIfFailed(NativeAbi.PromiseResolve(
      _runtime.RuntimeHandle,
      _promise,
      value.Handle));
  }

  public void Reject(ExpoError error)
  {
    NativeAbi.ThrowIfFailed(NativeAbi.PromiseReject(
      _runtime.RuntimeHandle,
      _promise,
      error.ToAbi()));
  }

  public void Dispose()
  {
    NativeAbi.PromiseRelease(_promise);
    _promise = IntPtr.Zero;
  }
}
```

Inside the C ABI implementation, C++ casts the opaque handles back to native
bridge structures and calls JSI. The exact helper names will differ, but the
mechanics should look like this:

```cpp
struct PromiseState {
  jsi::Value promise;
  jsi::Function resolve;
  jsi::Function reject;
};

struct expo_js_promise_t {
  std::shared_ptr<PromiseState> state;
};

int32_t expo_js_promise_resolve(
  expo_js_runtime_handle runtime_handle,
  expo_js_promise_handle promise_handle,
  expo_js_value_handle value_handle,
  expo_js_error *error) {
  try {
    auto &runtime = *reinterpret_cast<jsi::Runtime *>(runtime_handle);
    auto *promise = reinterpret_cast<expo_js_promise_t *>(promise_handle);
    auto *value = reinterpret_cast<expo_js_value_t *>(value_handle);

    promise->state->resolve.call(runtime, value->asJsiValue(runtime));
    return 1;
  } catch (const std::exception &ex) {
    *error = expo_js_error_from_exception(ex);
    return 0;
  }
}
```

Ownership rules:

- `promise_value` is the JS `Promise` object returned to JavaScript;
- `promise` retains the native resolve/reject capability until settled or
  disposed;
- C# may keep `JavaScriptPromise` past the original callback only because it is
  explicitly retained async state;
- C# must not keep `JavaScriptArguments`, `JavaScriptUnownedValue`, borrowed
  strings, or borrowed buffers past the original callback;
- `Resolve` and `Reject` must run on the JS runtime thread;
- the platform adapter supplies the real scheduler in RNW or RN macOS;
- a headless proof may use a simple scheduler, but it still must model the
  thread hop explicitly.

Pitfall:

Do not resolve a JS promise from an arbitrary .NET thread. A .NET `Task`
continuation is not automatically on the JS runtime. It must schedule back
through `JavaScriptAsyncRuntime` or an equivalent adapter-owned scheduler before
calling the C ABI resolve/reject functions.

Equally, do not schedule synchronous host-function work just because a
scheduler exists. The host-function callback itself is already executing in the
JS runtime context; scheduling is for work that happens later or from another
thread.

## Errors

Errors need two boundaries:

- native/JSI errors to C#;
- managed exceptions back to native/JS.

Rules:

- C++ exceptions must not cross the C ABI.
- C# exceptions must not cross unmanaged frames.
- ABI calls return `ok/error` result structs.
- Generated bindings catch managed exceptions and convert them to JS throws or
  promise rejections through native bridge functions.

Example shape:

```csharp
try
{
  return InvokeModule(args);
}
catch (Exception ex)
{
  return JavaScriptValue.Throw(runtime, ExpoError.FromException(ex));
}
```

`Throw` here is conceptual. The real implementation may return a structured
error result for native to throw into JS.

End-to-end error example:

```text
C# generated callback
  catches ArgumentException("Expected number for 'a'")
  returns ExpoJsValueResult.Error({ code, message })

C++ host-function body
  sees ok == 0
  converts expo_js_error to jsi::JSError
  throws into JS

JS caller
  sees Math.add("x", 3) throw a TypeError-like bridge error
```

Native errors travel the other direction through the same idea. If
`expo_js_object_set_property_utf8` catches a C++ exception, it fills
`expo_js_error` and returns `0`. The C# wrapper then turns that structured
error into a managed exception or managed result object:

```csharp
int ok = api->SetPropertyUtf8(..., &error);
if (ok == 0)
{
  throw JavaScriptBridgeException.From(error);
}
```

The invariant is the same both ways: C++ exceptions do not cross into C#, and
C# exceptions do not cross into C++. They become structured error values at the
boundary.

## Mapping To This Project

The wrapper model supports the architecture rule:

- C++ owns JSI runtime/value mechanics.
- C# owns typed module logic.
- wrappers make C ABI calls feel like a managed API.
- generated v2 code decodes arguments through wrappers.

A future implementation agent should start with wrapper lifetime tests before
adding broad type coverage. If a value can be borrowed, retained, returned,
released, and rejected incorrectly, write tests for those cases first.
