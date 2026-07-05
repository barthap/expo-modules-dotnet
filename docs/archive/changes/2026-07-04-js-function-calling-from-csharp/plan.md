# JS Function Calling From C# Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement C#-initiated JavaScript function calls and retained generated-module callbacks.

**Architecture:** Add function-call slots to the native ABI, expose them through `JavaScriptFunction`, then build one `Expo.ModulesCore` callback wrapper that retains JS functions and invokes them through value-tuple argument codecs plus existing result codecs. Generator support should lower explicit `JavaScriptCallback<TArgs, TResult>` parameter types into codec expressions without reflection, JSON, `object?[]`, or dynamic invocation on the hot path.

**Tech Stack:** C++ JSI bridge, C ABI function table, C# unsafe interop, `Expo.JSI`, `Expo.ModulesCore`, Roslyn incremental generator, xUnit, Hermes testhost.

## File Map

- `packages/expo-modules-dotnet/native/include/expo_jsi.h`: Add function-call ABI typedefs and function-table slots.
- `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`: Implement JSI `Function::call`, `callWithThis`, and `callAsConstructor` bridge entries.
- `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`: Keep copied API table and counters compatible with the new ABI version.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: Add managed function pointers, validation, and public interop helper methods for the new ABI slots.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`: Add any argument-list struct needed to pass handles and counts across the ABI.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptFunction.cs`: Add `Call`, `CallWithThis`, and `CallAsConstructor`.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`: Add `AsFunction`.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueRef.cs`: Add `AsFunction`.
- `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptFunctionTests.cs`: Add low-level function-call tests.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptCallback.cs`: Add retained tuple-argument callback wrapper type.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/ValueTupleCodec.cs`: Add value-tuple argument codecs up to eight callback arguments.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptCallbackCodec.cs`: Add callback codecs.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`: Track retained callback disposables with runtime-context teardown.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`: Recognize supported `JavaScriptCallback` generic forms and emit callback codec expressions.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCallbackModuleTests.cs`: Add generated module callback behavior tests.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`: Add callback fixture module.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`: Add generator diagnostic tests for unsupported callback codec types.
- `docs/specs/runtime-and-abi.md`, `docs/specs/managed-jsi-wrappers.md`, `docs/specs/ownership-and-scoped-refs.md`, `docs/specs/modules-core-boundary.md`, `docs/specs/runtime-scheduling.md`, `docs/roadmap.md`: Merge accepted behavior after implementation.

## Task 1: Low-Level Function Calls

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptFunction.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueRef.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptFunctionTests.cs`

- [ ] **Step 1: Write failing low-level tests**

Create `JavaScriptFunctionTests.cs` with tests for:

```csharp
[Fact]
public void CallInvokesJavaScriptFunctionWithRepresentableArguments()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var value = fixture.Evaluate("(a, b) => a + b", "function-call.js");
    using var function = value.AsFunction();
    using var a = runtime.CreateNumber(20);
    using var b = runtime.CreateNumber(22);

    using var result = function.Call(a, b);

    Assert.Equal(42, result.AsDouble());
    Assert.Equal(20, a.AsDouble());
    Assert.Equal(22, b.AsDouble());
    return true;
  });
}

[Fact]
public void CallWithThisUsesExplicitObjectReceiver()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var value = fixture.Evaluate("function (suffix) { return this.prefix + suffix; }", "function-call-this.js");
    using var function = value.AsFunction();
    using var receiver = fixture.Evaluate("({ prefix: 'hello ' })", "function-this-object.js").AsObject();
    using var suffix = runtime.CreateString("JS");

    using var result = function.CallWithThis(receiver, suffix);

    Assert.Equal("hello JS", result.AsString());
    return true;
  });
}

[Fact]
public void CallAsConstructorCreatesObject()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var value = fixture.Evaluate("function Box(value) { this.value = value; }", "function-constructor.js");
    using var function = value.AsFunction();
    using var argument = runtime.CreateString("boxed");

    using var constructed = function.CallAsConstructor(argument);
    using var constructedObject = constructed.AsObject();
    using var result = constructedObject.GetProperty("value");

    Assert.Equal("boxed", result.AsString());
    return true;
  });
}
```

- [ ] **Step 2: Run failing low-level tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptFunctionTests"
```

Expected: build fails because `AsFunction`, `Call`, `CallWithThis`, or `CallAsConstructor` does not exist.

- [ ] **Step 3: Add ABI slots and native implementations**

Add C ABI entries named consistently with existing style, such as:

```c
typedef expo_jsi_value_result (*expo_jsi_function_call_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle function,
  const expo_jsi_value_handle *arguments,
  uint32_t argument_count);

typedef expo_jsi_value_result (*expo_jsi_function_call_with_this_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle function,
  expo_jsi_value_handle this_object,
  const expo_jsi_value_handle *arguments,
  uint32_t argument_count);

typedef expo_jsi_value_result (*expo_jsi_function_call_as_constructor_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle function,
  const expo_jsi_value_handle *arguments,
  uint32_t argument_count);
```

Implement the slots in `ExpoJsiBridge.cpp` by validating handles with existing `checkedFunction` / object validation helpers, copying handle values into `std::vector<jsi::Value>`, calling the JSI function API, and returning a new owned `ValueHandle`.

- [ ] **Step 4: Add managed interop and wrappers**

In `ExpoJsiApi.cs`, add function pointers, validation, and helper methods:

```csharp
public ExpoJsiValueResult CallFunction(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle functionHandle,
    ReadOnlySpan<ExpoJsiValueHandle> arguments)
```

Mirror that for `CallFunctionWithThis` and `CallFunctionAsConstructor`.

In `JavaScriptFunction.cs`, add overloads accepting `params IJavaScriptValueRepresentable[]` and `ReadOnlySpan<IJavaScriptValueRepresentable>`. Convert each argument with `AsValue()` into a temporary array, pass native handles, and dispose temporaries in a `finally`.

In `JavaScriptValue.cs` and `JavaScriptValueRef.cs`, add `AsFunction()` using `RetainValueAs(..., ExpoJsiValueExpectation.Function)`.

- [ ] **Step 5: Run low-level tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptFunctionTests"
```

Expected: all `JavaScriptFunctionTests` pass.

- [ ] **Step 6: Commit low-level function calls**

Run:

```sh
git add packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
  packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptFunction.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValueRef.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptFunctionTests.cs
git diff --cached --check
git commit -m "feat: call JavaScript functions from managed JSI"
```

## Task 2: Retained Callback Wrappers

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptCallback.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptCallbackCodec.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCallbackModuleTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`

- [ ] **Step 1: Write failing callback wrapper tests**

Add tests that manually construct `JavaScriptCallback<ValueTuple, string>`,
`JavaScriptCallback<ValueTuple<string>, string>`, and
`JavaScriptCallback<(string first, string second), string>` from JS functions
and verify:

```csharp
var callback = JavaScriptCallback<ValueTuple<string>, string>.FromFunction(
    context,
    function,
    static (args, runtime) => ValueTupleCodec<string, StringCodec>.Encode(args, runtime),
    static (value, runtime) => StringCodec.Decode(value, runtime));
Assert.Equal("Hello JS", callback.Invoke(ValueTuple.Create("JS")));
```

The test should fail at compile time until callback types exist.

- [ ] **Step 2: Implement retained callback types**

Create one callback type with this public shape:

```csharp
public sealed class JavaScriptCallback<TArgs, TResult> : IDisposable
    where TArgs : struct
{
  public TResult Invoke(TArgs args);
  public Task<TResult> InvokeAsync(TArgs args, CancellationToken cancellationToken = default);
  public void Dispose();
}
```

Back them with an internal generic implementation that owns one retained
`JavaScriptFunction`, has a `DotnetRuntimeContext`, and throws
`ObjectDisposedException` after the callback is disposed or
`InvalidOperationException` after context teardown. `Invoke` SHALL require
`context.Runtime.CanExecuteSync` and call through `context.Runtime.Execute`.
`InvokeAsync` SHALL call through `context.Runtime.ExecuteAsync`.

- [ ] **Step 3: Add callback codecs**

Add one context-aware callback codec helper:

```csharp
public static class JavaScriptCallbackCodec<TArgs, TArgsCodec, TResult, TResultCodec>
    where TArgs : struct
    where TArgsCodec : IJavaScriptArgsCodec<TArgs>
    where TResultCodec : IJavaScriptCodec<TResult>
{
  public static JavaScriptCallback<TArgs, TResult> Decode(
      JavaScriptValueRef value,
      JavaScriptRuntime runtime,
      DotnetRuntimeContext context);
}
```

Decode by validating `JavaScriptValueRef.AsFunction()`, retaining the function,
and registering the callback as context-owned disposable state. Do not implement
callback encoding in this slice.

Add `IJavaScriptArgsCodec<TArgs>` and `ValueTupleCodec` forms for `ValueTuple`
and one through eight tuple elements. Support tuple syntax for two through eight
arguments, `ValueTuple<T>` for one argument, flat explicit `ValueTuple` forms
through seven arguments, and C#'s nested-rest `ValueTuple<T1, ..., T7,
ValueTuple<T8>>` shape for explicit eight-argument spelling. Tuple codecs SHALL
dispose already-encoded argument values if a later argument codec throws.

- [ ] **Step 4: Track callback disposables in runtime context**

Extend `DotnetRuntimeContext` with an internal registration method for retained
callbacks, following `RegisterHostFunction` lifetime style. Dispose registered
callbacks during context teardown after invalidating the context and before
module disposals complete.

- [ ] **Step 5: Run callback wrapper tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptCallback"
```

Expected: callback wrapper tests pass.

- [ ] **Step 6: Commit callback wrappers**

Run:

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptCallback.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptCallbackCodec.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCallbackModuleTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs
git diff --cached --check
git commit -m "feat: add retained JavaScript callbacks"
```

## Task 3: Generator Support For Callback Parameters

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Create or modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCallbackModuleTests.cs`

- [ ] **Step 1: Add generator tests**

Add a generator snapshot/assertion test that verifies a method like:

```csharp
[JS]
public string UseCallback(string value, JavaScriptCallback<ValueTuple<string>, string> callback) =>
    callback.Invoke(ValueTuple.Create(value));
```

generates a callback codec expression with concrete argument and result codecs.

Add a diagnostic test for:

```csharp
[JS]
public void Bad(JavaScriptCallback<UnsupportedType, string> callback) {}
```

Expected diagnostic: unsupported parameter type names the callback argument type.

- [ ] **Step 2: Add callback type recognition**

In `GetCodecExpression`, detect named types whose constructed-from definition
is one of:

```text
global::Expo.ModulesCore.JavaScriptCallback<TResult>
global::Expo.ModulesCore.JavaScriptCallback<T1, TResult>
global::Expo.ModulesCore.JavaScriptCallback<T1, T2, TResult>
```

Resolve codecs recursively for each generic argument. Return a direct
context-aware callback codec expression such as:

```csharp
global::Expo.ModulesCore.Codecs.JavaScriptCallbackCodec<string, StringCodec>
```

- [ ] **Step 3: Thread runtime context into parameter decoding**

Add an `ExpoParameterModel.RequiresRuntimeContext` boolean. Update
`GetParameterExpression` so normal parameters still emit:

```csharp
StringCodec.Decode(arguments.GetValue(0), runtime)
```

and callback parameters emit:

```csharp
JavaScriptCallbackCodec<string, StringCodec>.Decode(arguments.GetValue(0), runtime, context)
```

Keep emitted code direct and reflection-free.

- [ ] **Step 4: Add generated module behavior tests**

Add a fixture module with synchronous and retained callback usage:

```csharp
[ExpoModule("GeneratedCallbacks")]
public sealed partial class GeneratedCallbacksModule
{
  [JS("callNow")]
  public string CallNow(string value, JavaScriptCallback<ValueTuple<string>, string> callback) =>
      callback.Invoke(ValueTuple.Create(value));

  [JS("store")]
  public void Store(JavaScriptCallback<ValueTuple<string>, string> callback) => stored = callback;

  [JS("callStored")]
  public Task<string> CallStored(string value) =>
      stored!.InvokeAsync(value);
}
```

In tests, register the generated provider, pass JS callbacks, assert immediate
return values, drain the Hermes loop for retained async use, and assert teardown
failure after disposing `DotnetRuntimeContext`.

- [ ] **Step 5: Run module and generator tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedCallback|FullyQualifiedName~ExpoModulesGeneratorTests"
```

Expected: callback generator tests and generated module callback tests pass.

- [ ] **Step 6: Commit generator support**

Run:

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCallbackModuleTests.cs
git diff --cached --check
git commit -m "feat: generate JavaScript callback parameters"
```

## Task 4: Living Specs And Roadmap Merge

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/ownership-and-scoped-refs.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/runtime-scheduling.md`
- Modify: `docs/roadmap.md`
- Remove or archive: `docs/changes/2026-07-04-js-function-calling-from-csharp/spec.md`
- Remove: `docs/changes/2026-07-04-js-function-calling-from-csharp/plan.md`

- [ ] **Step 1: Merge accepted behavior into living specs**

Update current-state specs with implemented behavior only:

- `runtime-and-abi.md`: function-call ABI slots and result ownership.
- `managed-jsi-wrappers.md`: `JavaScriptFunction.Call`, `CallWithThis`,
  `CallAsConstructor`, and `AsFunction`.
- `ownership-and-scoped-refs.md`: argument temporaries, retained callbacks, and
  context teardown disposal.
- `modules-core-boundary.md`: explicit callback parameter support and codec
  composition.
- `runtime-scheduling.md`: retained callback `InvokeAsync` scheduling semantics.

- [ ] **Step 2: Update roadmap**

Mark P2 function calling from C# complete if all function-call and retained
callback tests pass. Leave Events/EventEmitter open.

- [ ] **Step 3: Remove transient change artifacts**

Remove `docs/changes/2026-07-04-js-function-calling-from-csharp/spec.md` and
`plan.md` after the accepted delta has been merged into `docs/specs/`.

- [ ] **Step 4: Run docs checks**

Run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: no whitespace errors; any `rg` matches are pre-existing or intentionally explained.

- [ ] **Step 5: Commit living-spec merge**

Run:

```sh
git add docs/specs/runtime-and-abi.md docs/specs/managed-jsi-wrappers.md \
  docs/specs/ownership-and-scoped-refs.md docs/specs/modules-core-boundary.md \
  docs/specs/runtime-scheduling.md docs/roadmap.md \
  docs/changes/2026-07-04-js-function-calling-from-csharp
git diff --cached --check
git commit -m "docs: merge JS callback function calling specs"
```

## Task 5: Final Verification

**Files:**
- No new files.

- [ ] **Step 1: Run managed suite**

Run:

```sh
scripts/test-managed.sh
```

Expected: all Hermes-backed managed tests pass.

- [ ] **Step 2: Run format check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: formatting passes. If it reports fixable formatting, run `scripts/format.sh`, then rerun this check.

- [ ] **Step 3: Run hot-path reflection scan**

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected: no new generated-binding hot-path reflection, dynamic invocation, object-array dispatch, or JSON conversion introduced by callback support.

- [ ] **Step 4: Run final diff check**

Run:

```sh
git diff --check
```

Expected: no whitespace errors.
