# Minimal Codec Expansion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add generated-binding support for enum, simple record, and string-key dictionary conversions, including the `Expo.JSI` own-property-name enumeration needed by dictionary decode.

**Architecture:** Implement the low-level object enumeration ABI first, then layer `Expo.ModulesCore` dictionary and enum codecs on top, then add generated record codecs. Keep all module dispatch direct-call and generated-code-inspectable; unsupported shapes fail at compile time through generator diagnostics.

**Tech Stack:** C++ JSI bridge, C ABI with opaque handles, C# `Expo.JSI`, `Expo.ModulesCore`, Roslyn incremental generator, xUnit, Hermes-backed managed tests.

## File Structure

- Modify `packages/expo-modules-dotnet/native/include/expo_jsi.h`: add property-name result structs, release callback typedef, and `object_get_own_property_names` function pointer.
- Modify `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`: implement `objectGetOwnPropertyNames`, release property-name buffers, bump API version, and wire the function table.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: add the native result structs/function pointer and managed wrapper method.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Internal/JavaScriptObjectInner.cs`: expose `GetOwnPropertyNames()`.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObject.cs`: expose owned-wrapper `GetOwnPropertyNames(): IReadOnlyList<string>`.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObjectRef.cs`: expose scoped-ref `GetOwnPropertyNames(): IReadOnlyList<string>`.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`: add enumeration/prototype tests.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptDictionaryCodec.cs`: object-to-dictionary codec.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/StringEnumCodec.cs`: default enum codec.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NumberEnumCodec.cs`: integer enum codec.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EnumRepresentation.cs`: enum representation enum.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSEnumAttribute.cs`: optional enum representation attribute.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`: compose enum, dictionary, and generated record codecs.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`: add generated codec model records.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`: add record/dictionary diagnostics if existing unsupported-type messages are not precise enough.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`: add generator output and diagnostic tests.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`: add authored sample enum, record, and dictionary modules.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs`: Hermes-backed conversion tests.
- Modify `docs/specs/managed-jsi-wrappers.md`: merge accepted object enumeration behavior after implementation.
- Modify `docs/specs/modules-core-boundary.md`: merge accepted codec behavior after implementation.
- Remove or archive `docs/changes/2026-07-03-minimal-codec-expansion/` after the accepted delta is merged into living specs.

## Task 1: Add Object Own-Property Enumeration To Expo.JSI

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Internal/JavaScriptObjectInner.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObject.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObjectRef.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs`

- [ ] **Step 1: Add failing object enumeration tests**

Add these tests to `JavaScriptObjectTests`:

```csharp
[Fact]
public void GetOwnPropertyNamesReturnsOwnEnumerableAndNonEnumerableNames()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var value = fixture.Evaluate(
        "(() => { const obj = { alpha: 1, zażółć: 2 }; Object.defineProperty(obj, 'hidden', { value: 3, enumerable: false }); return obj; })()",
        "object-property-names.js"
    );
    using var target = value.AsObject();

    var names = target.GetOwnPropertyNames();

    Assert.Contains("alpha", names);
    Assert.Contains("zażółć", names);
    Assert.Contains("hidden", names);
    return true;
  });
}

[Fact]
public void GetOwnPropertyNamesExcludesPrototypeProperties()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var value = fixture.Evaluate(
        "(() => { const proto = { inherited: 1 }; const obj = Object.create(proto); obj.own = 2; return obj; })()",
        "own-property-names.js"
    );
    using var target = value.AsObject();

    var names = target.GetOwnPropertyNames();

    Assert.Contains("own", names);
    Assert.DoesNotContain("inherited", names);
    return true;
  });
}
```

- [ ] **Step 2: Run the focused failing test**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptObjectTests"
```

Expected: FAIL to compile because `JavaScriptObject.GetOwnPropertyNames()` does not exist.

- [ ] **Step 3: Add the C ABI property-name result shape**

In `expo_jsi.h`, add structs near `expo_jsi_string_result`:

```c
typedef void (*expo_jsi_release_property_names_fn)(void *release_context);

typedef struct expo_jsi_property_name {
  const uint8_t *data;
  int32_t length;
} expo_jsi_property_name;

typedef struct expo_jsi_property_names_result {
  const expo_jsi_property_name *names;
  int32_t count;
  void *release_context;
  expo_jsi_release_property_names_fn release;
  expo_jsi_error error;
} expo_jsi_property_names_result;
```

Add a function pointer near the object property functions:

```c
typedef expo_jsi_property_names_result (*expo_jsi_object_get_own_property_names_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle object);
```

Add the field after `object_get_property` in `expo_jsi_api`:

```c
expo_jsi_object_get_own_property_names_fn object_get_own_property_names;
```

- [ ] **Step 4: Implement native own-property enumeration**

In `ExpoJsiBridge.cpp`, add a native buffer type near `StringResultBuffer`:

```cpp
struct PropertyNamesResultBuffer {
  std::vector<std::string> strings;
  std::vector<expo_jsi_property_name> names;
};
```

Add a release function:

```cpp
void releasePropertyNames(void *releaseContext)
{
  delete static_cast<PropertyNamesResultBuffer *>(releaseContext);
}
```

Add `objectGetOwnPropertyNames` near `objectGetProperty`:

```cpp
expo_jsi_property_names_result objectGetOwnPropertyNames(expo_jsi_runtime_handle runtime,
                                                         expo_jsi_value_handle object)
{
  try {
    auto &jsRuntime = runtime->runtime();
    auto propertyNames = object->value().asObject(jsRuntime).getPropertyNames(jsRuntime);
    auto length = propertyNames.size(jsRuntime);
    auto buffer = std::make_unique<PropertyNamesResultBuffer>();
    buffer->strings.reserve(length);
    buffer->names.reserve(length);

    for (size_t index = 0; index < length; index++) {
      auto nameValue = propertyNames.getValueAtIndex(jsRuntime, index);
      auto name = nameValue.asString(jsRuntime).utf8(jsRuntime);
      buffer->strings.push_back(std::move(name));
    }

    for (const auto &name : buffer->strings) {
      buffer->names.push_back(expo_jsi_property_name{
        reinterpret_cast<const uint8_t *>(name.data()),
        static_cast<int32_t>(name.size()),
      });
    }

    auto *releaseContext = buffer.release();
    return expo_jsi_property_names_result{
      releaseContext->names.data(),
      static_cast<int32_t>(releaseContext->names.size()),
      releaseContext,
      releasePropertyNames,
      makeOk(),
    };
  } catch (const std::exception &error) {
    return expo_jsi_property_names_result{nullptr, 0, nullptr, nullptr, makeError(1, error.what())};
  } catch (...) {
    return expo_jsi_property_names_result{nullptr, 0, nullptr, nullptr, makeError(1, "Unknown native exception.")};
  }
}
```

Update the API table and version:

```cpp
constexpr uint32_t kApiVersion = 14;
```

Set the new field:

```cpp
objectGetOwnPropertyNames,
```

- [ ] **Step 5: Add managed interop and wrapper methods**

In `ExpoJsiApi.cs`, add managed structs matching the ABI:

```csharp
[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiPropertyName
{
  public readonly byte* Data;
  public readonly int Length;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct ExpoJsiPropertyNamesResult
{
  public readonly ExpoJsiPropertyName* Names;
  public readonly int Count;
  public readonly nint ReleaseContext;
  public readonly delegate* unmanaged[Cdecl]<nint, void> Release;
  public readonly ExpoJsiError Error;
}
```

Add the function pointer after `ObjectGetProperty` and expose a wrapper method:

```csharp
private readonly delegate* unmanaged[Cdecl]<
  ExpoJsiRuntimeHandle,
  ExpoJsiValueHandle,
  ExpoJsiPropertyNamesResult> ObjectGetOwnPropertyNames;

public IReadOnlyList<string> GetObjectOwnPropertyNames(
    ExpoJsiRuntimeHandle runtime,
    ExpoJsiValueHandle value)
{
  var result = ObjectGetOwnPropertyNames(runtime, value);
  try
  {
    if (result.Error.Code != 0)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to get JavaScript object property names.");
    }

    var names = new string[result.Count];
    for (var index = 0; index < result.Count; index++)
    {
      names[index] = Encoding.UTF8.GetString(result.Names[index].Data, result.Names[index].Length);
    }

    return names;
  }
  finally
  {
    if (result.ReleaseContext != 0 && result.Release != null)
    {
      result.Release(result.ReleaseContext);
    }
  }
}
```

Update the managed expected ABI version wherever `ExpoJsiApi` validates it from `13` to `14`.

In `JavaScriptObjectInner`, add:

```csharp
public IReadOnlyList<string> GetOwnPropertyNames() =>
    Context.Api->GetObjectOwnPropertyNames(Context.RuntimeHandle, Handle);
```

In `JavaScriptObject`, add:

```csharp
public IReadOnlyList<string> GetOwnPropertyNames() => Inner.GetOwnPropertyNames();
```

In `JavaScriptObjectRef`, add the same public method using `Inner.GetOwnPropertyNames()`.

- [ ] **Step 6: Run focused object tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~JavaScriptObjectTests"
```

Expected: PASS.

- [ ] **Step 7: Commit object enumeration**

Run:

```sh
git add packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptObjectTests.cs
git diff --cached --check
rg -n "(/[U]sers/|localhost:[0-9])" -- $(git diff --cached --name-only)
git commit -m "feat: expose JavaScript object property names"
```

Expected: commit succeeds and the `rg` command prints no matches.

## Task 2: Add Dictionary And Enum Runtime Codecs

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/JavaScriptDictionaryCodec.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/StringEnumCodec.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs/NumberEnumCodec.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EnumRepresentation.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JSEnumAttribute.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs`

- [ ] **Step 1: Add failing generated-looking runtime tests**

Create `GeneratedCodecExpansionModuleTests.cs` with manually generated-looking provider code that uses the new codecs:

```csharp
using System.Collections.Generic;
using System.Linq;
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedCodecExpansionModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDecodesAndEncodesDictionary()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
      GeneratedCodecExpansionModuleProvider.Register(runtime, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.CodecExpansion.total({ first: 2, second: 3.5 })",
          "dictionary-total.js"
      );

      Assert.Equal(5.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeEncodesDictionaryAsPlainObject()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
      GeneratedCodecExpansionModuleProvider.Register(runtime, modules);
      using var result = fixture.Evaluate(
          "const value = globalThis._expoDotnet.modules.CodecExpansion.labels(); value.one + ',' + value.two",
          "dictionary-labels.js"
      );

      Assert.Equal("first,second", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodeUsesStringEnumByDefault()
  {
    using var fixture = HermesRuntimeFixture.Create();
    fixture.Runtime.Execute(runtime =>
    {
      using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
      GeneratedCodecExpansionModuleProvider.Register(runtime, modules);
      using var result = fixture.Evaluate(
          "globalThis._expoDotnet.modules.CodecExpansion.describeMode('Fast')",
          "enum-mode.js"
      );

      Assert.Equal("Fast", result.AsString());
      return true;
    });
  }

  private enum Mode
  {
    Slow,
    Fast,
  }

  private sealed class CodecExpansionModule
  {
    public double Total(Dictionary<string, double> values) => values.Values.Sum();

    public IReadOnlyDictionary<string, string> Labels() =>
        new Dictionary<string, string>
        {
          ["one"] = "first",
          ["two"] = "second",
        };

    public Mode DescribeMode(Mode mode) => mode;
  }

  private static class GeneratedCodecExpansionModuleProvider
  {
    public static void Register(JavaScriptRuntime runtime, JavaScriptObject modules)
    {
      using var module = ModuleRegistry.DefineModule(runtime, modules, "CodecExpansion");
      var instance = new CodecExpansionModule();
      GeneratedFunction.DefineSync(runtime, module, "total", 1, TotalHostFunction, instance);
      GeneratedFunction.DefineSync(runtime, module, "labels", 0, LabelsHostFunction, instance);
      GeneratedFunction.DefineSync(runtime, module, "describeMode", 1, DescribeModeHostFunction, instance);
    }

    private static JavaScriptValue TotalHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.total", arguments, 1);
      var module = (CodecExpansionModule)context;
      var values = JavaScriptDictionaryCodec<double, DoubleCodec>.DecodeToDictionary(
          arguments.GetValue(0),
          runtime
      );
      return DoubleCodec.Encode(module.Total(values), runtime);
    }

    private static JavaScriptValue LabelsHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.labels", arguments, 0);
      var module = (CodecExpansionModule)context;
      return JavaScriptDictionaryCodec<string, StringCodec>.Encode(module.Labels(), runtime);
    }

    private static JavaScriptValue DescribeModeHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("CodecExpansion.describeMode", arguments, 1);
      var module = (CodecExpansionModule)context;
      var mode = StringEnumCodec<Mode>.Decode(arguments.GetValue(0), runtime);
      return StringEnumCodec<Mode>.Encode(module.DescribeMode(mode), runtime);
    }
  }
}
```

- [ ] **Step 2: Run the focused failing test**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedCodecExpansionModuleTests"
```

Expected: FAIL to compile because `JavaScriptDictionaryCodec`, `StringEnumCodec`, and enum metadata types do not exist.

- [ ] **Step 3: Implement dictionary and enum codecs**

Create `JavaScriptDictionaryCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class JavaScriptDictionaryCodec<T, TCodec>
    where TCodec : IJavaScriptCodec<T>
{
  public static Dictionary<string, T> DecodeToDictionary(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);
    foreach (var name in obj.GetOwnPropertyNames())
    {
      var property = obj.GetProperty(name);
      result[name] = TCodec.Decode(property, runtime);
    }

    return result;
  }

  public static Dictionary<string, T> DecodeToDictionary(JavaScriptValue value, JavaScriptRuntime runtime)
  {
    using var obj = value.AsObject();
    var result = new Dictionary<string, T>(StringComparer.Ordinal);
    foreach (var name in obj.GetOwnPropertyNames())
    {
      using var property = obj.GetProperty(name);
      result[name] = TCodec.Decode(property, runtime);
    }

    return result;
  }

  public static JavaScriptValue Encode(IReadOnlyDictionary<string, T> values, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(values);

    using var obj = runtime.CreateObject();
    foreach (var pair in values)
    {
      using var value = TCodec.Encode(pair.Value, runtime);
      obj.SetProperty(pair.Key, value);
    }

    return obj.AsValue();
  }
}
```

Create `StringEnumCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct StringEnumCodec<TEnum> : IJavaScriptCodec<TEnum>
    where TEnum : struct, Enum
{
  public static TEnum Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      Parse(value.AsString());

  public static TEnum Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      Parse(value.AsString());

  public static JavaScriptValue Encode(TEnum value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value.ToString());

  private static TEnum Parse(string value)
  {
    if (Enum.TryParse<TEnum>(value, ignoreCase: false, out var result) &&
        Enum.IsDefined(result))
    {
      return result;
    }

    throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).FullName} value.");
  }
}
```

Create `NumberEnumCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct NumberEnumCodec<TEnum> : IJavaScriptCodec<TEnum>
    where TEnum : struct, Enum
{
  public static TEnum Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      FromNumber(value.AsDouble());

  public static TEnum Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      FromNumber(value.AsDouble());

  public static JavaScriptValue Encode(TEnum value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(Convert.ToDouble(value));

  private static TEnum FromNumber(double value)
  {
    var converted = (TEnum)Enum.ToObject(typeof(TEnum), value);
    if (Enum.IsDefined(converted))
    {
      return converted;
    }

    throw new ArgumentException($"'{value}' is not a valid {typeof(TEnum).FullName} value.");
  }
}
```

Create `EnumRepresentation.cs`:

```csharp
namespace Expo.ModulesCore;

public enum EnumRepresentation
{
  String,
  Number,
}
```

Create `JSEnumAttribute.cs`:

```csharp
namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Enum | AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
public sealed class JSEnumAttribute : Attribute
{
  public JSEnumAttribute(EnumRepresentation representation)
  {
    Representation = representation;
  }

  public EnumRepresentation Representation { get; }
}
```

- [ ] **Step 4: Run focused codec tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedCodecExpansionModuleTests"
```

Expected: PASS.

- [ ] **Step 5: Commit runtime codecs**

Run:

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs
git diff --cached --check
rg -n "(/[U]sers/|localhost:[0-9])" -- $(git diff --cached --name-only)
git commit -m "feat: add dictionary and enum codecs"
```

Expected: commit succeeds and the `rg` command prints no matches.

## Task 3: Teach The Generator Enum And Dictionary Codec Expressions

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add failing generator tests**

Add tests to `ExpoModulesGeneratorTests`:

```csharp
[Fact]
public void GeneratorEmitsEnumAndDictionaryCodecs()
{
  var result = GeneratorTestHost.Run(
      """
      using System.Collections.Generic;
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public enum Mode
      {
        Slow,
        Fast,
      }

      [ExpoModule("Codec")]
      public sealed partial class CodecModule
      {
        [JS]
        public Mode RoundTripMode(Mode mode) => mode;

        [JS]
        public double Total(Dictionary<string, double> values) => 0.0;

        [JS]
        public IReadOnlyDictionary<string, string> Labels() => new Dictionary<string, string>();
      }
      """
  );

  Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  var source = Assert.Single(result.GeneratedSources).Text;
  Assert.Contains("StringEnumCodec<global::Expo.TestModules.Mode>.Decode(arguments.GetValue(0), runtime)", source);
  Assert.Contains("StringEnumCodec<global::Expo.TestModules.Mode>.Encode(module.RoundTripMode(mode), runtime)", source);
  Assert.Contains("JavaScriptDictionaryCodec<double, NumberCodec<double>>.DecodeToDictionary(arguments.GetValue(0), runtime)", source);
  Assert.Contains("JavaScriptDictionaryCodec<string, StringCodec>.Encode(module.Labels(), runtime)", source);
}

[Fact]
public void GeneratorReportsUnsupportedDictionaryKeyType()
{
  var result = GeneratorTestHost.Run(
      """
      using System.Collections.Generic;
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoModule("Bad")]
      public sealed partial class BadModule
      {
        [JS]
        public double Bad(Dictionary<int, double> values) => 0.0;
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI001");
  Assert.Contains("values", diagnostic.GetMessage());
  Assert.Contains("Dictionary", diagnostic.GetMessage());
}
```

- [ ] **Step 2: Run failing generator tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorEmitsEnumAndDictionaryCodecs|FullyQualifiedName~GeneratorReportsUnsupportedDictionaryKeyType"
```

Expected: FAIL because enum and dictionary codec expressions are unsupported.

- [ ] **Step 3: Implement generator enum and dictionary matching**

In `GetCodecExpression`, check enum types before primitive switch:

```csharp
if (typeSymbol.TypeKind == TypeKind.Enum)
{
  return $"StringEnumCodec<{typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>";
}
```

Add `TryGetDictionaryCodec` and include it after read-only list matching:

```csharp
private static string? TryGetDictionaryCodec(ITypeSymbol typeSymbol)
{
  if (typeSymbol is not INamedTypeSymbol namedType)
  {
    return null;
  }

  var constructedType = namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
  if (constructedType is not "global::System.Collections.Generic.Dictionary<TKey, TValue>" and
      not "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
  {
    return null;
  }

  var keyType = namedType.TypeArguments[0];
  if (keyType.SpecialType != SpecialType.System_String)
  {
    return null;
  }

  var valueType = namedType.TypeArguments[1];
  var valueCodec = GetCodecExpression(valueType);
  if (valueCodec is null)
  {
    return null;
  }

  var valueTypeName = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
  return $"JavaScriptDictionaryCodec<{valueTypeName}, {valueCodec}>";
}
```

Update `GetParameterExpression` so dictionary parameters use `DecodeToDictionary`, matching the existing array special case:

```csharp
var methodName = parameter.CodecExpression.StartsWith("JavaScriptArrayCodec", StringComparison.Ordinal)
    ? "DecodeToArray"
    : parameter.CodecExpression.StartsWith("JavaScriptDictionaryCodec", StringComparison.Ordinal)
        ? "DecodeToDictionary"
        : "Decode";
```

- [ ] **Step 4: Run generator tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit generator enum/dictionary support**

Run:

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git diff --cached --check
rg -n "(/[U]sers/|localhost:[0-9])" -- $(git diff --cached --name-only)
git commit -m "feat: generate enum and dictionary codecs"
```

Expected: commit succeeds and the `rg` command prints no matches.

## Task 4: Generate Simple Record Codecs

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedCodecExpansionModuleTests.cs`

- [ ] **Step 1: Add failing generator tests for record forms**

Add this test:

```csharp
[Fact]
public void GeneratorEmitsSimpleRecordCodecs()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public record User(string Name, int Age);
      public record class UserClass(string Name, int Age);
      public readonly record struct UserStruct(string Name, int Age);

      [ExpoModule("Records")]
      public sealed partial class RecordsModule
      {
        [JS]
        public User RoundTripUser(User user) => user;

        [JS]
        public UserClass RoundTripUserClass(UserClass user) => user;

        [JS]
        public UserStruct RoundTripUserStruct(UserStruct user) => user;
      }
      """
  );

  Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  var source = Assert.Single(result.GeneratedSources).Text;
  Assert.Contains("private readonly struct UserCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.User>", source);
  Assert.Contains("private readonly struct UserClassCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.UserClass>", source);
  Assert.Contains("private readonly struct UserStructCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.UserStruct>", source);
  Assert.Contains("return new global::Expo.TestModules.User(name, age);", source);
  Assert.Contains("return new global::Expo.TestModules.UserClass(name, age);", source);
  Assert.Contains("return new global::Expo.TestModules.UserStruct(name, age);", source);
}
```

Add a diagnostic test:

```csharp
[Fact]
public void GeneratorReportsUnsupportedRecordFieldType()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public record Bad(decimal Value);

      [ExpoModule("BadRecords")]
      public sealed partial class BadRecordsModule
      {
        [JS]
        public Bad RoundTrip(Bad value) => value;
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI007");
  Assert.Contains("Bad", diagnostic.GetMessage());
  Assert.Contains("Value", diagnostic.GetMessage());
  Assert.Contains("System.Decimal", diagnostic.GetMessage());
}
```

- [ ] **Step 2: Run failing record generator tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "FullyQualifiedName~GeneratorEmitsSimpleRecordCodecs|FullyQualifiedName~GeneratorReportsUnsupportedRecordFieldType"
```

Expected: FAIL because record codec generation does not exist.

- [ ] **Step 3: Add generator model records**

In `ExpoModuleModel.cs`, add generated codec records:

```csharp
internal sealed record ExpoGeneratedRecordCodecModel(
    string CodecTypeName,
    string RecordTypeName,
    EquatableArray<ExpoGeneratedRecordFieldModel> Fields,
    Location? Location);

internal sealed record ExpoGeneratedRecordFieldModel(
    string ParameterName,
    string PropertyName,
    string PropertyAccessName,
    string TypeName,
    string CodecExpression,
    Location? Location);
```

Add an `EquatableArray<ExpoGeneratedRecordCodecModel> RecordCodecs` property to the provider-level model. If adding a new provider model is cleaner than threading codecs through modules, introduce an internal `ExpoGeneratorModel` containing modules, diagnostics, and record codecs.

- [ ] **Step 4: Implement record discovery and codec naming**

In `ExpoModulesGenerator.cs`, when `GetCodecExpression` sees `typeSymbol is INamedTypeSymbol { IsRecord: true }`, collect a record codec model and return its generated codec type name.

Use this first-pass rule:

```csharp
private static bool TryCreateRecordCodec(
    INamedTypeSymbol typeSymbol,
    List<ExpoDiagnosticModel> diagnostics,
    out ExpoGeneratedRecordCodecModel? codec)
{
  var primaryConstructor = typeSymbol.InstanceConstructors
      .Where(constructor => constructor.Parameters.Length > 0)
      .OrderByDescending(constructor => constructor.Parameters.Length)
      .FirstOrDefault();

  if (primaryConstructor is null)
  {
    codec = null;
    diagnostics.Add(new ExpoDiagnosticModel(
        ExpoModulesDiagnostics.UnsupportedRecordShape.Id,
        typeSymbol.Locations.FirstOrDefault(),
        new EquatableArray<string>(new[] { typeSymbol.Name, "record does not expose a positional constructor" })
    ));
    return false;
  }

  var fields = new List<ExpoGeneratedRecordFieldModel>();
  foreach (var parameter in primaryConstructor.Parameters)
  {
    var property = typeSymbol.GetMembers()
        .OfType<IPropertySymbol>()
        .FirstOrDefault(item => string.Equals(item.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
    var codecExpression = GetCodecExpression(parameter.Type);
    if (property is null || codecExpression is null)
    {
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.UnsupportedRecordField.Id,
          parameter.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[]
          {
              typeSymbol.Name,
              parameter.Name,
              GetDiagnosticTypeName(parameter.Type),
          })
      ));
      codec = null;
      return false;
    }

    fields.Add(new ExpoGeneratedRecordFieldModel(
        LowerCamel(parameter.Name),
        property.Name,
        property.Name,
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        codecExpression,
        parameter.Locations.FirstOrDefault()
    ));
  }

  codec = new ExpoGeneratedRecordCodecModel(
      $"{SanitizeIdentifier(typeSymbol.Name)}Codec",
      typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
      new EquatableArray<ExpoGeneratedRecordFieldModel>(fields),
      typeSymbol.Locations.FirstOrDefault()
  );
  return true;
}
```

Keep the implementation exact to current Roslyn APIs; adjust the helper names if the existing generator structure needs a smaller insertion point.

- [ ] **Step 5: Emit generated record codec structs**

Emit record codecs into the generated provider source before the provider class:

```csharp
private readonly struct UserCodec : global::Expo.ModulesCore.Codecs.IJavaScriptCodec<global::Expo.TestModules.User>
{
  public static global::Expo.TestModules.User Decode(global::Expo.JSI.JavaScriptValueRef value, global::Expo.JSI.JavaScriptRuntime runtime)
  {
    var obj = value.AsObject();
    var name = global::Expo.ModulesCore.Codecs.StringCodec.Decode(obj.GetProperty("Name"), runtime);
    var age = global::Expo.ModulesCore.Codecs.NumberCodec<int>.Decode(obj.GetProperty("Age"), runtime);
    return new global::Expo.TestModules.User(name, age);
  }

  public static global::Expo.TestModules.User Decode(global::Expo.JSI.JavaScriptValue value, global::Expo.JSI.JavaScriptRuntime runtime)
  {
    using var obj = value.AsObject();
    using var nameValue = obj.GetProperty("Name");
    using var ageValue = obj.GetProperty("Age");
    var name = global::Expo.ModulesCore.Codecs.StringCodec.Decode(nameValue, runtime);
    var age = global::Expo.ModulesCore.Codecs.NumberCodec<int>.Decode(ageValue, runtime);
    return new global::Expo.TestModules.User(name, age);
  }

  public static global::Expo.JSI.JavaScriptValue Encode(global::Expo.TestModules.User value, global::Expo.JSI.JavaScriptRuntime runtime)
  {
    using var obj = runtime.CreateObject();
    using var name = global::Expo.ModulesCore.Codecs.StringCodec.Encode(value.Name, runtime);
    using var age = global::Expo.ModulesCore.Codecs.NumberCodec<int>.Encode(value.Age, runtime);
    obj.SetProperty("Name", name);
    obj.SetProperty("Age", age);
    return obj.AsValue();
  }
}
```

Generated field names stay equal to C# property names for this slice.

- [ ] **Step 6: Run generator tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Add Hermes-backed generated record tests**

Extend `GeneratedAttributeModules.cs` with:

```csharp
public record CodecUser(string Name, int Age);
public record class CodecUserClass(string Name, int Age);
public readonly record struct CodecUserStruct(string Name, int Age);

[ExpoModule("GeneratedRecords")]
public sealed partial class GeneratedRecordsModule
{
  [JS]
  public CodecUser Rename(CodecUser user) => user with { Name = user.Name + "!" };

  [JS]
  public CodecUserClass RenameClass(CodecUserClass user) => user with { Name = user.Name + "!" };

  [JS]
  public CodecUserStruct RenameStruct(CodecUserStruct user) => user with { Name = user.Name + "!" };
}
```

Add tests to `GeneratedCodecExpansionModuleTests.cs`:

```csharp
[Fact]
public void GeneratedProviderDecodesAndEncodesPositionalRecord()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
    Expo.ModulesCore.Generated.ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime, modules);
    using var result = fixture.Evaluate(
        "globalThis._expoDotnet.modules.GeneratedRecords.rename({ Name: 'Ada', Age: 37 }).Name",
        "record-user.js"
    );

    Assert.Equal("Ada!", result.AsString());
    return true;
  });
}

[Fact]
public void GeneratedProviderDecodesAndEncodesRecordClassAndStruct()
{
  using var fixture = HermesRuntimeFixture.Create();
  fixture.Runtime.Execute(runtime =>
  {
    using var modules = ModuleRegistry.GetOrCreateDotnetModulesObject(runtime);
    Expo.ModulesCore.Generated.ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime, modules);
    using var classResult = fixture.Evaluate(
        "globalThis._expoDotnet.modules.GeneratedRecords.renameClass({ Name: 'Grace', Age: 40 }).Name",
        "record-class.js"
    );
    using var structResult = fixture.Evaluate(
        "globalThis._expoDotnet.modules.GeneratedRecords.renameStruct({ Name: 'Katherine', Age: 42 }).Name",
        "record-struct.js"
    );

    Assert.Equal("Grace!", classResult.AsString());
    Assert.Equal("Katherine!", structResult.AsString());
    return true;
  });
}
```

- [ ] **Step 8: Run ModulesCore tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedProviderDecodesAndEncodesPositionalRecord|FullyQualifiedName~GeneratedProviderDecodesAndEncodesRecordClassAndStruct"
```

Expected: PASS.

- [ ] **Step 9: Commit record codec generation**

Run:

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated
git diff --cached --check
rg -n "(/[U]sers/|localhost:[0-9])" -- $(git diff --cached --name-only)
git commit -m "feat: generate simple record codecs"
```

Expected: commit succeeds and the `rg` command prints no matches.

## Task 5: Merge Accepted Behavior Into Living Specs

**Files:**
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/roadmap.md`
- Remove or archive: `docs/changes/2026-07-03-minimal-codec-expansion/spec.md`
- Remove or archive: `docs/changes/2026-07-03-minimal-codec-expansion/plan.md`

- [ ] **Step 1: Update managed JSI wrapper spec**

Add an object enumeration scenario under `Typed Owned Wrappers`:

```markdown
#### Scenario: Object wrapper enumerates own property names

- **GIVEN** a `JavaScriptObject`
- **WHEN** managed code asks for own property names
- **THEN** the wrapper SHALL call the object property-name ABI using opaque
  handles
- **AND** return managed strings that remain valid after the native call
- **AND** inherited prototype properties SHALL NOT be returned.
```

- [ ] **Step 2: Update ModulesCore boundary spec**

Add scenarios for enum, record, and dictionary conversions under generated sync function generation:

```markdown
#### Scenario: Enum values use generated codecs

- **GIVEN** a generated sync function accepts or returns a C# enum
- **WHEN** no explicit enum representation is requested
- **THEN** generated dispatch SHALL decode and encode the enum as JavaScript
  strings
- **AND** integer-backed enum conversion SHALL be available through explicit
  authored metadata.

#### Scenario: Simple records use generated codecs

- **GIVEN** a generated sync function accepts or returns a positional C#
  `record`, `record class`, or `record struct`
- **WHEN** JavaScript passes or receives a plain object
- **THEN** generated dispatch SHALL convert known fields through generated
  field codecs
- **AND** construct records through direct constructor calls.

#### Scenario: String-key dictionaries use JavaScript objects

- **GIVEN** a generated sync function accepts or returns
  `Dictionary<string, T>` or `IReadOnlyDictionary<string, T>`
- **WHEN** `T` has a generated codec
- **THEN** generated dispatch SHALL map the dictionary to a plain JavaScript
  object using own property names.
```

- [ ] **Step 3: Update roadmap**

In `docs/roadmap.md`, mark minimal codec expansion items complete:

```markdown
2. **Minimal codec expansion** (complete)
   - Complete: null / undefined / void return semantics, nullable value types,
     generic numeric primitive codecs, enums, simple records, and
     `Dictionary<string, T>` / `IReadOnlyDictionary<string, T>`.
   - Keep ArrayBuffer, SharedObject, and NativeState out of this slice.
```

Move or remove completed backlog entries for property enumeration, enums, record types, and `Dictionary<string, T>` so the roadmap does not list completed work as open.

- [ ] **Step 4: Archive or remove transient change artifacts**

Move the transient change directory under `docs/archive/changes` for provenance:

```sh
mkdir -p docs/archive/changes
git mv docs/changes/2026-07-03-minimal-codec-expansion docs/archive/changes/2026-07-03-minimal-codec-expansion
```

Do not leave a duplicate copy under `docs/changes/`.

- [ ] **Step 5: Run docs verification**

Run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: `git diff --check` passes; `rg` prints no matches unless a match is intentional and documented in the final handoff.

- [ ] **Step 6: Commit living spec merge**

Run:

```sh
git add docs/specs/managed-jsi-wrappers.md docs/specs/modules-core-boundary.md docs/roadmap.md docs/archive/changes/2026-07-03-minimal-codec-expansion
git diff --cached --check
rg -n "(/[U]sers/|localhost:[0-9])" -- $(git diff --cached --name-only)
git commit -m "docs: merge minimal codec expansion specs"
```

Expected: commit succeeds and the `rg` command prints no matches.

## Task 6: Final Verification

**Files:**
- Verify all changed code and docs.

- [ ] **Step 1: Run canonical managed tests**

Run:

```sh
scripts/test-managed.sh
```

Expected: PASS.

- [ ] **Step 2: Run formatter check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: PASS. If it fails because files need formatting, run `scripts/format.sh`, then repeat `scripts/format.sh --check --all`.

- [ ] **Step 3: Check generated-binding hot path stays reflection-free**

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
```

Expected: no matches in generated-binding runtime or generated source paths unless an existing unrelated test fixture match is explicitly explained.

- [ ] **Step 4: Confirm git state and summarize evidence**

Run:

```sh
git status --short
git log --oneline --max-count=8
```

Expected: working tree is clean except for intentional untracked local files. Final handoff lists commits, verification commands, and any skipped checks.
