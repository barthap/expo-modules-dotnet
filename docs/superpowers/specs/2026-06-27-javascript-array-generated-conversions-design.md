# JavaScriptArray Generated Conversions Design

Date: 2026-06-27
Repo: `<repo>`

## Context

The bridge already proves primitive values, objects, host functions, borrowed
arguments, and generated-looking module dispatch through a Hermes-backed JSI
runtime. The next useful slice is JavaScript array support, but only far enough
to prove the future generated binding shape.

The governing architecture remains:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

This design intentionally does not introduce `Expo.ModulesCore`, a Roslyn
source generator, or a public module DSL. The goal is hand-written
generated-looking code that demonstrates what a future generator should emit.

## Goal

Support a low-level `Expo.JSI.JavaScriptArray` wrapper and prove generated-looking
conversion for:

- JS array argument -> `IReadOnlyList<T>` module parameter;
- `IReadOnlyList<T>` module return -> JS array;
- first required element types: `double` and `string`.

`bool` is not part of first-slice success. If it is added, it must use the same
primitive codec pattern and must not expand the design.

Example authored module shape:

```csharp
internal sealed class ArrayModule
{
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  public IReadOnlyList<string> Labels() => ["one", "two"];
}
```

## Non-Goals

Do not build in this slice:

- `Expo.ModulesCore`;
- actual Roslyn source generator;
- public module DSL;
- runtime converter registry;
- `List<T>` construction policy;
- `IEnumerable<T>` conversion;
- dictionaries, records, nullable values, enums, BigInt, ArrayBuffer, or typed arrays;
- dynamic fallback conversion;
- broad conversion error taxonomy beyond clear native/managed messages.

## Architecture

The data flow is:

```text
JS call
  -> native JSI host function
  -> managed generated-looking callback
  -> generated-looking converter calls
  -> authored C# module method
  -> generated-looking converter calls
  -> owned JavaScriptValue return
```

`Expo.JSI` owns only low-level runtime wrappers:

```text
JavaScriptRuntime
JavaScriptValue / JavaScriptBorrowedValue
JavaScriptObject
JavaScriptFunction
JavaScriptArguments
JavaScriptArray
```

Generated-looking conversion helpers stay in the proof/test layer. They must not
be added to `Expo.JSI` as if they were permanent low-level runtime concepts.

When an `Expo.ModulesCore` package arrives in this repo, move the temporary
generated-looking array/list codecs and module conversion tests there. Until
then, they may live in the temporary module-test/proof area, but their placement
is advisory and not final architecture.

## JavaScriptArray API

Add a distinct owned wrapper:

```csharp
public sealed class JavaScriptArray : IDisposable
{
  public uint Length { get; }

  public JavaScriptValue GetValue(uint index);
  public void SetValue(uint index, JavaScriptValue value);

  public JavaScriptObject AsObject();
  public JavaScriptValue AsValue();
}
```

Add runtime creation:

```csharp
public JavaScriptArray CreateArray(uint length = 0);
```

Add value conversion:

```csharp
public JavaScriptArray AsArray();
```

on both `JavaScriptValue` and `JavaScriptBorrowedValue`.

Rules:

- `JavaScriptArray` owns an opaque native array handle and releases it from
  `Dispose()`.
- `GetValue` returns an owned `JavaScriptValue`.
- `SetValue` accepts an owned `JavaScriptValue` but does not consume it.
- `Length` reads the current JS array length through native JSI so JS-side
  mutations are visible.
- `AsArray()` throws a typed native error when the value is not an array.
- `AsObject()` and `AsValue()` are explicit conversions.

## Native ABI Additions

Mirror the existing object/function handle pattern.

```c
typedef struct expo_jsi_array_t *expo_jsi_array_handle;

typedef struct expo_jsi_array_result {
  int32_t ok;
  expo_jsi_array_handle array;
  expo_jsi_error error;
} expo_jsi_array_result;
```

Required function table additions:

```c
create_array(runtime, length) -> array_result
array_as_value(runtime, array) -> value_result
array_as_object(runtime, array) -> object_result
value_as_array(runtime, value) -> array_result
array_get_length(runtime, array, error*) -> uint32_t
array_get_value_at_index(runtime, array, index) -> value_result
array_set_value_at_index(runtime, array, index, value) -> error
release_array(runtime, array)
```

Native C++ owns all `facebook::jsi::Array` operations. C# receives only opaque
handles and structured results.

## Generated-Looking Conversion Shape

Keep codecs internal to the proof/test layer for now:

```csharp
internal interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
}
```

Primitive codecs are direct and allocation-minimal:

```csharp
internal readonly struct DoubleCodec : IJavaScriptCodec<double>
{
  public static double Decode(JavaScriptBorrowedValue value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(value);
}
```

Array/list codec:

```csharp
internal static class JavaScriptArrayCodec<T, TCodec>
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
```

Generated-looking callbacks decode to arrays because `T[]` already implements
`IReadOnlyList<T>`. Return conversion accepts `IReadOnlyList<T>` directly so
authored module methods can return immutable/list-like shapes without forcing a
specific concrete collection.

Do not add a public `JavaScriptValue.AsBorrowed()` just for converter symmetry.
The temporary codec interface can overload owned and borrowed decode paths until
the real ModulesCore converter design decides whether such an API belongs in
the permanent surface.

## Generated-Looking Module Proof

The proof should register an array module shape through the existing host
function mechanism. The exact JS namespace may reuse the current generated
module proof pattern.

Suggested JS assertions:

```js
globalThis.expo.modules.Array.sum([1, 2, 3.5]) === 6.5
globalThis.expo.modules.Array.labels().join(",") === "one,two"
```

Generated-looking host function shape:

```csharp
private static JavaScriptValue ArraySumHostFunction(
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
```

Return conversion:

```csharp
private static JavaScriptValue ArrayLabelsHostFunction(
    JavaScriptRuntime runtime,
    JavaScriptBorrowedValue thisValue,
    JavaScriptArguments arguments,
    object context)
{
  var module = (ArrayModule)context;
  return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime);
}
```

## Testing

Low-level `Expo.JSI.Tests.Runtime` coverage:

- `CreateArray` creates a JS-visible array with the requested length.
- `SetValue` and `GetValue` round-trip values.
- `Length` observes JS-side length changes.
- `JavaScriptValue.AsArray()` succeeds for JS arrays and fails for non-arrays.
- `JavaScriptBorrowedValue.AsArray()` works inside a host function.
- disposing an array increments the native release counter.

Temporary module/proof coverage:

- JS array argument decodes into `IReadOnlyList<double>`.
- `IReadOnlyList<string>` return encodes into a JS array.
- wrong argument type produces a catchable JS/managed error, not a process crash.

If implementing tests reveals that native/managed failures are swallowed or
expected conversion errors become uncatchable process crashes, stop and surface
the bridge error-boundary flaw before continuing.

## Verification

Before finishing implementation:

```sh
scripts/test-jsi.sh
scripts/run-hermes-experiment.sh
scripts/format.sh --check --all
```
