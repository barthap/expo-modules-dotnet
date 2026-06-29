# JavaScriptArray Generated Conversions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add ABI-backed `JavaScriptArray` support and prove generated-looking `IReadOnlyList<T>` conversions without introducing `Expo.ModulesCore` or a source generator.

**Architecture:** C++ owns `facebook::jsi::Array` and exposes opaque array handles through the C ABI. `Expo.JSI` gets only the low-level `JavaScriptArray` wrapper, while temporary generated-looking codecs and module tests live in `Expo.JSI.Tests/Modules` until `Expo.ModulesCore` exists.

**Tech Stack:** C++ JSI bridge, C ABI function table, C# unsafe function pointers, xUnit, Hermes-backed `scripts/test-jsi.sh`.

---

## File Structure

- Modify: `native/include/expo_jsi.h`
  Adds `expo_jsi_array_handle`, `expo_jsi_array_result`, array function pointer typedefs, and function table entries.
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
  Adds `ArrayHandle`, native array operations, API version bump, release counter increment, and function table wiring.
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
  Adds `ExpoJsiArrayHandle`.
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
  Adds `ExpoJsiArrayResult`.
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
  Adds array function pointers, validation, wrappers, and expected version bump.
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  Adds `CreateArray(uint length = 0)`.
- Modify: `managed/packages/Expo.JSI/JavaScriptValue.cs`
  Adds `AsArray()`.
- Modify: `managed/packages/Expo.JSI/JavaScriptBorrowedValue.cs`
  Adds `AsArray()`.
- Create: `managed/packages/Expo.JSI/JavaScriptArray.cs`
  Owns opaque array handles and exposes `Length`, indexed get/set, `AsObject()`, `AsValue()`, and `Dispose()`.
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`
  Low-level wrapper tests.
- Create: `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`
  Temporary generated-looking codec/module proof. Move this area to `Expo.ModulesCore.Tests` when `Expo.ModulesCore` exists.

## Task 1: Write Low-Level JavaScriptArray Tests

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`
- Modify: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueTests.cs`

- [ ] **Step 1: Add failing low-level array wrapper tests**

Create `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`:

```csharp
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Runtime;

public sealed class JavaScriptArrayTests
{
  [Fact]
  public void CreateArrayCreatesJavaScriptVisibleArrayWithRequestedLength()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var array = fixture.Runtime.CreateArray(3);
    using var arrayValue = array.AsValue();
    global.SetProperty("managedArray", arrayValue);

    using var isArray = fixture.Evaluate("Array.isArray(globalThis.managedArray)", "array-create.js");
    using var length = fixture.Evaluate("globalThis.managedArray.length", "array-create.js");

    Assert.True(isArray.AsBool());
    Assert.Equal(3, length.AsDouble());
  }

  [Fact]
  public void GetAndSetValueRoundTripIndexedElements()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var array = fixture.Runtime.CreateArray(2);
    using var first = fixture.Runtime.CreateNumber(41.5);
    using var second = fixture.Runtime.CreateString("expo");

    array.SetValue(0, first);
    array.SetValue(1, second);

    using var actualFirst = array.GetValue(0);
    using var actualSecond = array.GetValue(1);

    Assert.Equal(JavaScriptValueKind.Number, actualFirst.Kind);
    Assert.Equal(41.5, actualFirst.AsDouble());
    Assert.Equal(JavaScriptValueKind.String, actualSecond.Kind);
    Assert.Equal("expo", actualSecond.AsString());
  }

  [Fact]
  public void LengthObservesJavaScriptSideMutations()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("const xs = [1, 2]; xs.push(3); xs", "array-length.js");
    using var array = value.AsArray();

    Assert.Equal(3u, array.Length);
  }

  [Fact]
  public void JavaScriptValueAsArrayConvertsEvaluatedArray()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var value = fixture.Evaluate("['a', 'b']", "array-as-array.js");
    using var array = value.AsArray();

    Assert.Equal(2u, array.Length);
    using var element = array.GetValue(1);
    Assert.Equal("b", element.AsString());
  }

  [Fact]
  public void JavaScriptBorrowedValueAsArrayWorksInsideHostFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();
    using var global = fixture.Runtime.Global();
    using var function = fixture.Runtime.CreateHostFunction(
        "readArrayLength",
        1,
        (runtime, thisValue, arguments, context) =>
        {
          using var array = arguments.GetBorrowedValue(0).AsArray();
          return runtime.CreateNumber(array.Length);
        },
        new object()
    );
    using var functionValue = function.AsValue();
    global.SetProperty("readArrayLength", functionValue);

    using var result = fixture.Evaluate("globalThis.readArrayLength([1, 2, 3, 4])", "borrowed-array.js");

    Assert.Equal(4, result.AsDouble());
  }

  [Fact]
  public void DisposingArrayIncrementsReleaseCounter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.ResetCounters();

    using (fixture.Runtime.CreateArray(0))
    {
    }

    Assert.True(fixture.Counters.ReleasedObjects >= 1);
  }
}
```

- [ ] **Step 2: Add array wrong-type coverage**

In `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptValueTests.cs`, add this inline data row:

```csharp
[InlineData("42", "array", "Value is not an array.")]
```

and add the switch case:

```csharp
case "array":
  using (value.AsArray())
  {
  }
  break;
```

- [ ] **Step 3: Run tests to verify the expected compile failure**

Run:

```sh
scripts/test-jsi.sh
```

Expected: build fails because `CreateArray` and `AsArray` are not defined yet.

## Task 2: Implement ABI-Backed JavaScriptArray

**Files:**
- Modify: `native/include/expo_jsi.h`
- Modify: `native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`
- Modify: `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptValue.cs`
- Modify: `managed/packages/Expo.JSI/JavaScriptBorrowedValue.cs`
- Create: `managed/packages/Expo.JSI/JavaScriptArray.cs`

- [ ] **Step 1: Extend native header types**

In `native/include/expo_jsi.h`, add the C++ forward declaration and typedef beside object/function handles:

```c
class ArrayHandle;
using expo_jsi_array_t = expo::jsi::ArrayHandle;
typedef expo_jsi_array_t *expo_jsi_array_handle;
```

Add the C fallback typedef:

```c
typedef struct expo_jsi_array_t *expo_jsi_array_handle;
```

Add the result struct after `expo_jsi_object_result`:

```c
typedef struct expo_jsi_array_result {
  int32_t ok;
  expo_jsi_array_handle array;
  expo_jsi_error error;
} expo_jsi_array_result;
```

Add function typedefs:

```c
typedef expo_jsi_array_result (*expo_jsi_create_array_fn)(expo_jsi_runtime_handle runtime,
                                                          uint32_t length);
typedef expo_jsi_value_result (*expo_jsi_array_as_value_fn)(expo_jsi_runtime_handle runtime,
                                                            expo_jsi_array_handle array);
typedef expo_jsi_object_result (*expo_jsi_array_as_object_fn)(expo_jsi_runtime_handle runtime,
                                                              expo_jsi_array_handle array);
typedef expo_jsi_array_result (*expo_jsi_value_as_array_fn)(expo_jsi_runtime_handle runtime,
                                                            expo_jsi_value_handle value);
typedef uint32_t (*expo_jsi_array_get_length_fn)(expo_jsi_runtime_handle runtime,
                                                 expo_jsi_array_handle array,
                                                 expo_jsi_error *error);
typedef expo_jsi_value_result (*expo_jsi_array_get_value_at_index_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_array_handle array,
  uint32_t index);
typedef expo_jsi_error (*expo_jsi_array_set_value_at_index_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_array_handle array,
  uint32_t index,
  expo_jsi_value_handle value);
typedef void (*expo_jsi_release_array_fn)(expo_jsi_runtime_handle runtime,
                                          expo_jsi_array_handle array);
```

Append matching fields to `expo_jsi_api` after the object operations and before host-function creation.

- [ ] **Step 2: Add native array handle and operations**

In `native/packages/jsi/src/ExpoJsiBridge.cpp`, add an `ArrayHandle` next to `ObjectHandle`:

```cpp
class ArrayHandle final {
public:
  static std::unique_ptr<ArrayHandle> owned(facebook::jsi::Array array)
  {
    return std::unique_ptr<ArrayHandle>(
      new ArrayHandle(std::make_unique<facebook::jsi::Array>(std::move(array))));
  }

  facebook::jsi::Array &array()
  {
    return *array_;
  }

private:
  explicit ArrayHandle(std::unique_ptr<facebook::jsi::Array> array)
    : array_(std::move(array))
  {
  }

  std::unique_ptr<facebook::jsi::Array> array_;
};
```

Add `makeArrayResult` and `makeArrayErrorResult` beside object/function result helpers:

```cpp
expo_jsi_array_result makeArrayResult(std::unique_ptr<expo::jsi::ArrayHandle> array)
{
  return expo_jsi_array_result{1, array.release(), expo_jsi_error{0, nullptr, 0}};
}

expo_jsi_array_result makeArrayErrorResult(int32_t code, const char *message)
{
  return expo_jsi_array_result{0, nullptr, makeError(code, message)};
}
```

Add operations:

```cpp
expo_jsi_array_result createArray(expo_jsi_runtime_handle runtime, uint32_t length);
expo_jsi_value_result arrayAsValue(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array);
expo_jsi_object_result arrayAsObject(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array);
expo_jsi_array_result valueAsArray(expo_jsi_runtime_handle runtime, expo_jsi_value_handle value);
uint32_t arrayGetLength(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array, expo_jsi_error *error);
expo_jsi_value_result arrayGetValueAtIndex(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array, uint32_t index);
expo_jsi_error arraySetValueAtIndex(expo_jsi_runtime_handle runtime, expo_jsi_array_handle array, uint32_t index, expo_jsi_value_handle value);
void releaseArray(expo_jsi_runtime_handle, expo_jsi_array_handle array);
```

Use these JSI calls inside the implementations:

```cpp
facebook::jsi::Array(jsRuntime, length)
facebook::jsi::Value(jsRuntime, array->array())
facebook::jsi::Object(jsRuntime, array->array())
value->value().asObject(jsRuntime).asArray(jsRuntime)
array->array().length(jsRuntime)
array->array().getValueAtIndex(jsRuntime, index)
array->array().setValueAtIndex(jsRuntime, index, value->value())
```

For `valueAsArray`, first check `value->value().isObject()` and then
`value->value().asObject(jsRuntime).isArray(jsRuntime)`. Return error message
`Value is not an array.` for non-arrays.

Bump `kApiVersion` from `5` to `6`, wire the functions into `kApi`, and make
`releaseArray` increment the released-object counter by using the same counter
increment helper as `releaseObject`.

- [ ] **Step 3: Extend managed interop types**

In `managed/packages/Expo.JSI/Interop/ExpoJsiHandles.cs`, add:

```csharp
global using ExpoJsiArrayHandle = System.IntPtr;
```

In `managed/packages/Expo.JSI/Interop/ExpoJsiTypes.cs`, add:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal readonly struct ExpoJsiArrayResult
{
  public readonly int Ok;
  public readonly ExpoJsiArrayHandle Array;
  public readonly ExpoJsiError Error;
}
```

- [ ] **Step 4: Extend managed API table**

In `managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`, add function pointer fields matching the C header, validate they are non-null, add wrapper methods named:

```csharp
CreateArrayValue
ConvertArrayToValue
ConvertArrayToObject
ConvertValueToArray
GetArrayLength
GetArrayValueAtIndex
SetArrayValueAtIndex
ReleaseArrayHandle
```

Bump:

```csharp
public const uint ExpectedVersion = 6;
```

- [ ] **Step 5: Add managed wrapper**

Create `managed/packages/Expo.JSI/JavaScriptArray.cs`:

```csharp
using Expo.JSI.Interop;

namespace Expo.JSI;

public sealed class JavaScriptArray : IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiArrayHandle handle;

  internal JavaScriptArray(JsiContext context, ExpoJsiArrayHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  public uint Length
  {
    get
    {
      ThrowIfDisposed();
      unsafe
      {
        ExpoJsiError error;
        var length = context.Api->GetArrayLength(context.RuntimeHandle, handle, &error);
        context.ThrowIfError(error, "Failed to read JavaScript array length.");
        return length;
      }
    }
  }

  public JavaScriptValue GetValue(uint index)
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->GetArrayValueAtIndex(context.RuntimeHandle, handle, index);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript array value.");
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  public void SetValue(uint index, JavaScriptValue value)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(value);
    unsafe
    {
      var error = context.Api->SetArrayValueAtIndex(
          context.RuntimeHandle,
          handle,
          index,
          value.Handle
      );
      context.ThrowIfError(error, "Failed to set JavaScript array value.");
    }
  }

  public JavaScriptObject AsObject()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertArrayToObject(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Object == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to object.");
      }
      return new JavaScriptObject(context, result.Object);
    }
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertArrayToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript array to value.");
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseArrayHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
```

- [ ] **Step 6: Add runtime/value entry points**

In `JavaScriptRuntime.cs`, add:

```csharp
public JavaScriptArray CreateArray(uint length = 0)
{
  var result = context.Api->CreateArrayValue(context.RuntimeHandle, length);
  if (result.Ok == 0 || result.Array == 0)
  {
    JsiContext.ThrowNativeError(result.Error, "Failed to create JavaScript array.");
  }
  return new JavaScriptArray(context, result.Array);
}
```

In `JavaScriptValue.cs` and `JavaScriptBorrowedValue.cs`, add:

```csharp
public JavaScriptArray AsArray()
{
  ThrowIfDisposed(); // JavaScriptValue
  unsafe
  {
    var result = context.Api->ConvertValueToArray(context.RuntimeHandle, handle);
    if (result.Ok == 0 || result.Array == 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript value to array.");
    }
    return new JavaScriptArray(context, result.Array);
  }
}
```

For `JavaScriptBorrowedValue`, call `ThrowIfNull()` instead of `ThrowIfDisposed()`.

- [ ] **Step 7: Run low-level tests**

Run:

```sh
scripts/test-jsi.sh
```

Expected: all tests pass, including `JavaScriptArrayTests`.

- [ ] **Step 8: Commit low-level array support**

```sh
git add native/include/expo_jsi.h native/packages/jsi/src/ExpoJsiBridge.cpp managed/packages/Expo.JSI managed/packages/Expo.JSI.Tests/Runtime
git commit -m "Add JavaScriptArray JSI wrapper"
```

## Task 3: Add Generated-Looking Array Conversion Proof

**Files:**
- Create: `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`

- [ ] **Step 1: Add failing temporary module conversion tests**

Create `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Expo.JSI.Tests.Fixtures;
using Xunit;

namespace Expo.JSI.Tests.Modules;

public sealed class ArrayConversionTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesJavaScriptArrayIntoReadOnlyListParameter()
  {
    using var fixture = HermesRuntimeFixture.Create();
    GeneratedArrayModuleProvider.Register(fixture.Runtime);

    using var result = fixture.Evaluate(
        "globalThis.expo.modules.Array.sum([1, 2, 3.5])",
        "array-sum.js"
    );

    Assert.Equal(JavaScriptValueKind.Number, result.Kind);
    Assert.Equal(6.5, result.AsDouble());
  }

  [Fact]
  public void GeneratedLookingCodeEncodesReadOnlyListReturnAsJavaScriptArray()
  {
    using var fixture = HermesRuntimeFixture.Create();
    GeneratedArrayModuleProvider.Register(fixture.Runtime);

    using var result = fixture.Evaluate(
        "const labels = globalThis.expo.modules.Array.labels(); Array.isArray(labels) && labels.join(',')",
        "array-labels.js"
    );

    Assert.Equal(JavaScriptValueKind.String, result.Kind);
    Assert.Equal("one,two", result.AsString());
  }

  private sealed class ArrayModule
  {
    public double Sum(IReadOnlyList<double> values) => values.Sum();

    public IReadOnlyList<string> Labels() => ["one", "two"];
  }

  private interface IJavaScriptCodec<T>
  {
    static abstract T Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime);
    static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
    static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
  }

  private readonly struct DoubleCodec : IJavaScriptCodec<double>
  {
    public static double Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime) =>
        value.AsDouble();

    public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        value.AsDouble();

    public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime) =>
        runtime.CreateNumber(value);
  }

  private readonly struct StringCodec : IJavaScriptCodec<string>
  {
    public static string Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime) =>
        value.AsString();

    public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
        value.AsString();

    public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
        runtime.CreateString(value);
  }

  private static class JavaScriptArrayCodec<T, TCodec>
      where TCodec : IJavaScriptCodec<T>
  {
    public static T[] DecodeToArray(JavaScriptBorrowedValue value, JavaScriptRuntime runtime)
    {
      using var array = value.AsArray();
      var length = checked((int)array.Length);
      var result = new T[length];

      for (var index = 0; index < length; index++)
      {
        using var element = array.GetValue((uint)index);
        result[index] = TCodec.Decode(element, runtime);
      }

      return result;
    }

    public static JavaScriptValue Encode(IReadOnlyList<T> values, JavaScriptRuntime runtime)
    {
      using var array = runtime.CreateArray((uint)values.Count);
      for (var index = 0; index < values.Count; index++)
      {
        using var element = TCodec.Encode(values[index], runtime);
        array.SetValue((uint)index, element);
      }
      return array.AsValue();
    }
  }

  private static class GeneratedArrayModuleProvider
  {
    public static void Register(JavaScriptRuntime runtime)
    {
      using var global = runtime.Global();
      using var expo = runtime.CreateObject();
      using var modules = runtime.CreateObject();
      using var array = runtime.CreateObject();

      var module = new ArrayModule();
      using var sum = runtime.CreateHostFunction("sum", 1, SumHostFunction, module);
      using var labels = runtime.CreateHostFunction("labels", 0, LabelsHostFunction, module);
      using var sumValue = sum.AsValue();
      using var labelsValue = labels.AsValue();
      array.SetProperty("sum", sumValue);
      array.SetProperty("labels", labelsValue);

      using var arrayValue = array.AsValue();
      modules.SetProperty("Array", arrayValue);
      using var modulesValue = modules.AsValue();
      expo.SetProperty("modules", modulesValue);
      using var expoValue = expo.AsValue();
      global.SetProperty("expo", expoValue);
    }

    private static JavaScriptValue SumHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      var module = (ArrayModule)context;
      var values = JavaScriptArrayCodec<double, DoubleCodec>.DecodeToArray(
          arguments.GetBorrowedValue(0),
          runtime
      );
      return DoubleCodec.Encode(module.Sum(values), runtime);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptBorrowedValue thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      var module = (ArrayModule)context;
      return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime);
    }
  }
}
```

- [ ] **Step 2: Run tests**

Run:

```sh
scripts/test-jsi.sh
```

Expected: tests pass. If a conversion failure becomes an uncatchable process
crash or a swallowed failure, stop and fix the bridge error boundary explicitly
before continuing.

- [ ] **Step 3: Commit generated-looking conversion proof**

```sh
git add managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs
git commit -m "Prove generated array conversions"
```

## Task 4: Final Verification

**Files:**
- No code files unless verification exposes a defect.

- [ ] **Step 1: Run canonical JSI suite**

```sh
scripts/test-jsi.sh
```

Expected: pass.

- [ ] **Step 2: Run Hermes experiment smoke test**

```sh
scripts/run-hermes-experiment.sh
```

Expected: pass.

- [ ] **Step 3: Run formatting check**

```sh
scripts/format.sh --check --all
```

Expected: pass. If it reports formatting changes are needed, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

- [ ] **Step 4: Check final diff hygiene**

```sh
git diff --check
git status --short
```

Expected: no whitespace errors and a clean working tree after the final commit.
