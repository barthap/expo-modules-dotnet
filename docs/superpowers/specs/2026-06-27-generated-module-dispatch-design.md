# Generated Module Dispatch Design

Date: 2026-06-27
Repo: `<repo>`

## Context

The Hermes console HostFXR proof now validates the first real JSI bridge path:
native C++ owns a Hermes-backed `facebook::jsi::Runtime`, passes a C ABI
function table and opaque runtime handle into C#, and C# can create/read/return
JavaScript values through opaque handles.

The current JS -> C# callback is still hardcoded as `AddOne`. That proves the
JSI bridge can pass value handles, but it does not prove the shape future
generated module bindings need.

This milestone replaces the hardcoded callback with one generated-looking
module dispatch slice:

```js
global.expo.modules.Math.add(41.5, true) // 42.5
```

The governing architecture remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

## Goal

Prove that a JavaScript call can flow through native JSI host-function plumbing
into hand-written generated-looking C# dispatch code, call an authored C#
module method directly, and return an owned JavaScript value handle back to JS.

The proof should exercise the same boundaries a future source generator will
use, while staying small enough to keep every ownership and error rule visible.

## Concrete Target

Native setup installs a module-shaped object graph:

```text
global.expo.modules.Math.add
```

JavaScript evaluation calls:

```js
global.expo.modules.Math.add(41.5, true)
```

The call returns `42.5`.

The authored C# module should look plain and direct:

```csharp
internal sealed class MathModule
{
    public double Add(double value, bool shouldAddOne)
    {
        return shouldAddOne ? value + 1.0 : value;
    }
}
```

The generated-looking C# code should decode `JavaScriptArguments`, call
`MathModule.Add(...)` directly, encode the return value, and avoid reflection,
`object?[]`, JSON, and hardcoded native knowledge of `MathModule`.

## Non-Goals

Do not build in this slice:

- source generator;
- `Expo.ModulesCore` public module DSL;
- rn-macos, RNW, or expo-desktop adapter integration;
- async functions, promises, or scheduler behavior;
- arrays beyond argument-buffer access;
- records, dictionaries, shared objects, events, or views;
- broad converter registry;
- NativeAOT publishing for this path;
- string conversion unless the implementation needs a module/property name ABI
  helper that cannot reasonably stay native-only.

## Design Summary

The slice has four layers:

```text
Hermes console experiment
  evaluates JS and owns this proof's host setup

Native JSI bridge
  creates objects/functions
  owns createFromHostFunction
  passes borrowed this/arguments into managed callbacks

Managed Expo.JSI wrappers
  JavaScriptRuntime
  JavaScriptObject
  JavaScriptArguments
  JavaScriptValue / borrowed value wrapper

Generated-looking C# proof code
  registers Math module shape
  decodes arguments
  calls MathModule.Add directly
  returns JavaScriptValue
```

Native C++ remains responsible for all real JSI mechanics. C# sees only opaque
runtime, value, object, function, and argument handles through the function
table.

## Native ABI Additions

Keep the ABI explicit and narrow. Add only the handles and function pointers the
proof forces.

Likely new opaque handles:

```c
typedef struct expo_jsi_object_t *expo_jsi_object_handle;
typedef struct expo_jsi_function_t *expo_jsi_function_handle;
typedef struct expo_jsi_arguments_t *expo_jsi_arguments_handle;
```

Required operations:

- get the runtime global object;
- create a plain object;
- get and set object properties by UTF-8 property name;
- convert object to value;
- create a host function from a callback pointer plus callback context;
- release object/function/argument-owned handles where ownership exists;
- read argument count;
- borrow an argument value by index;
- promote a borrowed value to an owned value if needed;
- create/read bool and number values;
- return structured errors from every fallible operation.

The host-function callback shape should be generated-code friendly. Conceptual
shape:

```c
typedef expo_jsi_value_result (*expo_jsi_host_function_callback_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle this_value,
  expo_jsi_arguments_handle arguments);
```

The exact C signature can change during implementation if C# function-pointer
rules require a flatter shape, but it must preserve these semantics:

- callback context is managed-owned or proof-owned and has a release callback;
- `this_value` and `arguments` are borrowed and call-scoped;
- the callback returns an owned value handle or structured error;
- native converts the owned value back to JSI and releases it exactly once.

## Managed API Additions

Add only the wrapper surface needed by the proof.

`JavaScriptRuntime`:

- `Global()`;
- `CreateNumber(double value)`;
- `CreateBool(bool value)`;
- `CreateObject()`;
- `CreateHostFunction(string name, JavaScriptHostFunction callback, object? context)`;

`JavaScriptObject`:

- `GetProperty(string name)`;
- `SetProperty(string name, JavaScriptValue value)`;
- `AsValue()`;
- `Dispose()`;

`JavaScriptArguments`:

- `Count`;
- `GetBorrowedValue(int index)`;
- optional `GetValue(int index)` only if an owned copy is needed.

Borrowed value wrapper:

- use a separate type from owned `JavaScriptValue`;
- expose `Kind`, `AsBool()`, and `AsDouble()`;
- make the call-scoped nature clear in type name and docs;
- do not allow `Dispose()` to release borrowed JSI values;
- provide explicit promotion to owned value only if needed.

Generated-looking helper methods may live in the experiment assembly at first.
Do not move module DSL or registration concepts into the reusable package until
the shape proves itself.

## Generated-Looking Dispatch Shape

The generated-looking proof should split authored code from generated code.

Authored module:

```csharp
internal sealed class MathModule
{
    public double Add(double value, bool shouldAddOne) =>
        shouldAddOne ? value + 1.0 : value;
}
```

Generated-looking provider:

```csharp
internal static class GeneratedModuleProvider
{
    public static void Register(JavaScriptRuntime runtime)
    {
        using var global = runtime.Global();
        using var expo = EnsureObject(global, "expo");
        using var modules = EnsureObject(expo, "modules");
        using var math = runtime.CreateObject();

        var module = new MathModule();
        using var add = runtime.CreateHostFunction(
            "add",
            MathAddHostFunction,
            module);

        math.SetProperty("add", add.AsValue());
        modules.SetProperty("Math", math.AsValue());
    }

    private static JavaScriptValue MathAddHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
        var module = (MathModule)context;
        var value = arguments.GetBorrowedValue(0).AsDouble();
        var shouldAddOne = arguments.GetBorrowedValue(1).AsBool();
        return runtime.CreateNumber(module.Add(value, shouldAddOne));
    }
}
```

This is illustrative, not exact final syntax. The important constraints are:

- module lookup is static and explicit;
- the target method call is direct;
- argument decoding uses typed wrapper methods;
- return encoding uses typed runtime creation methods;
- no runtime module scan, `MethodInfo.Invoke`, `Delegate.DynamicInvoke`,
  `object?[]`, or JSON.

## Error Handling

Generated-looking dispatch must fail loudly and with useful messages:

- wrong argument count;
- wrong argument type;
- null or invalid runtime/value/object/function/arguments handle;
- native exception while touching JSI;
- managed exception while decoding arguments or calling the module.

Native C++ catches native exceptions and converts them to JS errors. Managed
callbacks should convert managed exceptions into structured ABI errors so native
can throw a JS error from the host function.

For this proof, it is acceptable to fail the executable on unexpected errors.
The reusable ABI should still model structured errors rather than relying on
process-level failure.

## Ownership And Lifetime

Required rules:

- runtime handle is borrowed from the connector;
- global object is an owned wrapper handle returned to C# and released by C#;
- created objects/functions are owned handles and released by C# wrappers;
- object property assignment copies/moves into JSI according to native rules and
  must not let C# release a value still needed by JS;
- host-function callback context has exactly one release path;
- `this_value` and `arguments` are borrowed and valid only during the callback;
- an owned return value from C# is copied back into JSI by native and then
  released exactly once.

This slice should keep the existing release-count proof or equivalent
experiment-only assertion outside reusable bridge code.

## Verification

The milestone is complete only with fresh evidence:

```sh
dotnet build experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/HostFxrJSIProof.csproj -c Debug
cmake --build build/hermes-console-hostfxr --target hermes_console_hostfxr
./build/hermes-console-hostfxr/hermes_console_hostfxr
```

Expected meaningful output should include:

```text
Created Hermes-backed JSI runtime
registered generated-looking Math module
JS called generated-looking C# module: 42.5
managed JSI proof: number kind=Number value=42.5
hermes console hostfxr proof: ok
```

Also run scans proving the generated-looking path did not regress into forbidden
dispatch mechanisms:

```sh
rg -n "Assembly\\.GetTypes|MethodInfo\\.Invoke|DynamicInvoke|object\\?\\[\\]|JsonSerializer|JsonConvert" \
  managed experiments/hermes-console-hostfxr
```

Result notes should record:

- commands run;
- expected and actual output;
- ownership/lifetime findings;
- scheduler findings;
- stop/go decision for the next slice.

## Stop Conditions

Stop and review if:

- installing `global.expo.modules.Math.add` requires broad object/function APIs
  unrelated to this proof;
- C# needs raw JSI pointers or C++ object layouts;
- borrowed arguments cannot be made call-scoped and understandable;
- callback context lifetime becomes unclear;
- generated-looking dispatch requires reflection or `object?[]`;
- ownership of function return values cannot be proven by release assertions.

## Next Slice After This

If this proof succeeds, the next slice should be one of:

- add string conversion and a second generated-looking method using `string`;
- add `JavaScriptArray` only if argument/return conversion needs it;
- run a NativeAOT compatibility audit against the generated-looking callback
  shape;
- write the implementation plan for a source-generator prototype.

Do not start rn-macos, RNW, or expo-desktop adapter work until this headless
dispatch shape is proven.
