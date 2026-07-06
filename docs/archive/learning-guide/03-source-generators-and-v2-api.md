# 03 - Source Generators And The v2 API

## Why A Source Generator

Expo module APIs should be pleasant to author:

```csharp
[ExpoModule]
public sealed partial class MathModule
{
  [JS]
  public double Add(double a, double b) => a + b;
}
```

The runtime bridge, however, needs direct and predictable code:

- decode argument 0 as double;
- decode argument 1 as double;
- call `MathModule.Add`;
- convert the double return value to a JS value;
- report unsupported signatures at build time.

A source generator connects those goals. Authors write attributes. The build
produces explicit C# code. The runtime does not need to discover and invoke
methods reflectively for v2.

## Attribute Metadata vs Runtime Reflection

Attributes are metadata. They can be read in two very different places:

Build time:

- Roslyn source generator inspects syntax and symbols.
- Generator emits normal C# source.
- Errors become compiler diagnostics.
- Runtime cost is low.

Runtime:

- code scans assemblies;
- code reflects over types/methods;
- code calls `MethodInfo.Invoke`;
- code often boxes arguments into `object?[]`;
- NativeAOT and trimming become harder.

The v2 direction is build-time generation. HostFXR makes runtime reflection
available, but that is not a reason to use it for v2 invocation.

Forbidden in generated v2 hot path:

```csharp
Assembly.GetTypes();
methodInfo.Invoke(instance, args);
delegate.DynamicInvoke(args);
JsonSerializer.Serialize(jsValue);
```

Acceptable in the generator:

```csharp
context.SyntaxProvider
  .ForAttributeWithMetadataName("ExpoModuleAttribute", ...)
```

The generator runs at build time. The emitted code runs at runtime.

## Generated Code Shape

Authored module:

```csharp
[ExpoModule(Name = "Math")]
public sealed partial class MathModule
{
  [JS(Name = "add")]
  public double Add(double a, double b) => a + b;

  [JS(Name = "describe")]
  public MathResult Describe(double value) => new(value, value >= 0);
}

[Record]
public readonly partial record struct MathResult(double Value, bool IsPositive);
```

Generated registration should look like ordinary code:

```csharp
public static partial class GeneratedExpoModulesProvider
{
  public static void Register(JavaScriptRuntime runtime, ModuleRegistry registry)
  {
    var math = new MathModule();
    using var exports = runtime.CreateObject();

    using var addFunction = JavaScriptFunction.Create(
      runtime,
      "add",
      static (thisValue, args, context) =>
      {
        var module = (MathModule)context;
        var a = ExpoArgumentConverters.GetDouble(args, 0, "a");
        var b = ExpoArgumentConverters.GetDouble(args, 1, "b");
        return args.Runtime.CreateNumber(module.Add(a, b));
      },
      math);
    using var addValue = addFunction.AsValue();
    exports.SetProperty("add", addValue);

    using var describeFunction = JavaScriptFunction.Create(
      runtime,
      "describe",
      static (thisValue, args, context) =>
      {
        var module = (MathModule)context;
        var value = ExpoArgumentConverters.GetDouble(args, 0, "value");
        return GeneratedRecordConverters.MathResultToJs(
          args.Runtime,
          module.Describe(value));
      },
      math);
    using var describeValue = describeFunction.AsValue();
    exports.SetProperty("describe", describeValue);

    using var exportsValue = exports.AsValue();
    registry.RegisterModule("Math", exportsValue.RetainForRegistry());
  }
}
```

Generated record conversion should also be explicit:

```csharp
internal static partial class GeneratedRecordConverters
{
  public static JavaScriptValue MathResultToJs(JavaScriptRuntime runtime, MathResult value)
  {
    using var obj = runtime.CreateObject();
    using var jsValue = runtime.CreateNumber(value.Value);
    using var jsIsPositive = runtime.CreateBool(value.IsPositive);
    obj.SetProperty("value", jsValue);
    obj.SetProperty("isPositive", jsIsPositive);
    using var objValue = obj.AsValue();
    return objValue.RetainForReturn();
  }
}
```

This code is not the final API. It shows the intended runtime qualities:

- module construction is explicit;
- argument conversion is typed;
- method call is direct;
- return conversion is typed and runtime-backed. For example,
  `args.Runtime.CreateNumber(...)` calls the C ABI to create a real JSI number
  in the native bridge and returns an owned `JavaScriptValue` handle. Boolean,
  string, object, and record conversions should follow the same pattern through
  runtime wrapper factories such as `CreateBool`; object/function wrappers use
  explicit `AsValue()` conversions that call `expo_js_object_as_value` or
  `expo_js_function_as_value`. Returning that wrapper transfers ownership to
  the host-function return path.
- generated code can be inspected and tested.

## Argument Converter Internals

`ExpoArgumentConverters.GetDouble(args, 0, "a")` is a small wrapper around the
borrowed-value rules. It should not be magic and it should not use JSON.

Illustrative implementation:

```csharp
public static double GetDouble(JavaScriptArguments args, int index, string name)
{
  if (index >= args.Count)
  {
    throw JavaScriptArgumentException.Missing(name, index);
  }

  JavaScriptUnownedValue value = args.UnownedValueAt(index);
  if (value.Kind != JavaScriptValueKind.Number)
  {
    throw JavaScriptArgumentException.WrongType(
      name,
      expected: JavaScriptValueKind.Number,
      actual: value.Kind);
  }

  return value.AsDouble();
}
```

The calls inside that helper still go through the bridge:

```text
args.UnownedValueAt(0)
  returns a borrowed handle from the callback frame

value.Kind
  calls expo_js_value_get_kind(runtime, borrowed_handle)

value.AsDouble()
  calls expo_js_value_get_double(runtime, borrowed_handle, &error)

wrong type or native conversion failure
  becomes a structured managed exception/result
  generated callback catches it
  native converts it to JS throw or promise rejection
```

The converter may allocate a managed exception on failure. It should not
allocate for the normal successful primitive path beyond what the wrapper model
requires. It must never store `JavaScriptUnownedValue` beyond the callback.

## Diagnostics Instead Of Runtime Guessing

A source generator should fail early when a type is unsupported:

```csharp
[JS]
public SomeUnsupportedType Bad(SomeOtherUnsupportedType input) => ...
```

Desired diagnostic:

```text
EXPOJSI001: Parameter 'input' on 'Bad' uses unsupported type
'SomeOtherUnsupportedType'. Add a generated converter or change the signature.
```

This is better than runtime discovery because:

- the author sees the issue during build;
- the runtime bridge stays small;
- NativeAOT compatibility is easier;
- generated code stays predictable.

## Async Methods And Promises

Async authored API might look like:

```csharp
[JS]
public async Task<string> ReadTextAsync(string path)
{
  return await File.ReadAllTextAsync(path);
}
```

Generated code should:

1. decode arguments synchronously;
2. create a JS promise through the runtime wrapper, receiving a promise value
   plus native-owned resolve/reject handles;
3. run the managed task;
4. when the task completes, schedule resolve/reject through the adapter's JS
   scheduler;
5. on the JS thread, convert result or error through wrapper rules and call the
   native resolve/reject JSI functions.

The generator should not hide scheduler semantics. If a platform adapter is
needed to get back to the JS thread, the generated code should call an explicit
bridge service. In React Native terms, that service is backed by a
call-invoker-like mechanism, but generated C# should not receive
`react::CallInvoker`, `RuntimeExecutor`, or `RuntimeScheduler` directly. It
should receive a managed wrapper such as `JavaScriptAsyncRuntime` or
`JavaScriptScheduler` that the adapter created from those native facilities.

Illustrative generated flow:

```csharp
using var readTextAsyncFunction = JavaScriptFunction.Create(
  runtime,
  "readTextAsync",
  static (thisValue, args, context) =>
  {
    var module = (FileModule)context;
    var path = ExpoArgumentConverters.GetString(args, 0, "path");

    JavaScriptAsyncRuntime asyncRuntime = args.Runtime.CaptureForAsync();
    JavaScriptPromise promise = asyncRuntime.CreatePromise();
    JavaScriptValue promiseValue = promise.RetainPromiseValueForReturn();

    _ = module.ReadTextAsync(path).ContinueWith(task =>
    {
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
  },
  module);
using var readTextAsyncValue = readTextAsyncFunction.AsValue();
exports.SetProperty("readTextAsync", readTextAsyncValue);
```

This is not final API. It teaches the ownership and threading path:

```text
host-function callback frame
  -> decode path synchronously from borrowed args
  -> capture only durable async state:
       JavaScriptAsyncRuntime
       JavaScriptPromise / resolve / reject handles
       managed string path
  -> return promise JS value

Task<T> completes on a .NET thread
  -> generated continuation cannot touch JSI directly
  -> adapter schedule_on_js posts work to JS runtime
  -> JS-thread callback creates JS value or error
  -> native resolve/reject function is called on the right runtime
```

C# must not resolve or reject a JS promise directly from an arbitrary .NET
thread. The platform adapter owns the scheduler bridge. Generated async code
must not close over `JavaScriptArguments`, `JavaScriptUnownedValue`, or borrowed
argument handles. Inputs must be decoded synchronously during the callback, and
any promise, resolve/reject, runtime, or scheduler state that survives past the
callback must be explicitly retained or represented by a durable async wrapper
such as the illustrative `JavaScriptAsyncRuntime`.

The generator should also avoid scheduling when it is not needed. A synchronous
`[JS]` method runs inside a JSI host-function callback and should return
directly. Scheduling every call would add latency, hide thread assumptions, and
make the headless proof less representative of real JSI execution.

## v1 Compatibility

Existing v1-style DSL or reflection-based code may remain during migration.
That does not invalidate the v2 goal if it is isolated:

```text
v1 compatibility path
  may use existing reflection where required
  not the target for NativeAOT fast path

v2 generated path
  static registration
  direct invocation
  typed wrappers
  NativeAOT-compatible design
```

Future agents should avoid "temporarily" making v2 call into v1 reflection
helpers unless the result note explicitly marks it as a blocker.

## Testing The Generator Shape

Before writing the generator, test the generated-looking code by hand:

```sh
dotnet test
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed
```

Useful tests:

- module is registered under expected JS name;
- function property exists;
- `add(2, 3)` returns `5`;
- unsupported argument count produces structured JS error;
- borrowed values are not stored;
- owned return values are released by the receiver;
- generated-looking code contains no forbidden reflection calls.

## Mapping To This Project

The source generator is not phase 1. Phase 1 proves the loader and ABI. Phase 2
proves wrappers. Phase 3 hand-writes generated-looking code. Only after that
should a generator be built.

The reason is practical: if the generated-looking code is awkward, unsafe, or
requires runtime reflection, a generator would only automate the wrong design.
