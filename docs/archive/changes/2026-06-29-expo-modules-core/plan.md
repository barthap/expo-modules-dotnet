# Expo.ModulesCore Generated-Building-Blocks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `Expo.ModulesCore` and `Expo.ModulesCore.Tests` as the real home for generated-looking module dispatch and conversion behavior above `Expo.JSI`.

**Architecture:** `Expo.ModulesCore` depends on `Expo.JSI` and exposes generated-binding helpers for module registration, sync host-function installation, arity checks, primitive codecs, and `IReadOnlyList<T>` array conversion. `Expo.ModulesCore.Tests` contains hand-written generated-looking providers that exercise those helpers through the Hermes-backed runtime, while `Expo.JSI.Tests` keeps direct low-level coverage.

**Tech Stack:** .NET 10, C# static abstract interface members, xUnit v3, Hermes-backed native testhost, Bash test scripts.

---

## File Structure

- Create `managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`
  - Package project above `Expo.JSI`.
- Create `managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
  - Creates or reuses `globalThis.expo.modules` and installs module objects by name.
- Create `managed/packages/Expo.ModulesCore/GeneratedFunction.cs`
  - Defines sync host functions on module objects and checks exact arity.
- Create `managed/packages/Expo.ModulesCore/Codecs/IJavaScriptCodec.cs`
  - Common generated-binding codec interface.
- Create `managed/packages/Expo.ModulesCore/Codecs/BoolCodec.cs`
  - Boolean decode/encode helper.
- Create `managed/packages/Expo.ModulesCore/Codecs/DoubleCodec.cs`
  - Number decode/encode helper.
- Create `managed/packages/Expo.ModulesCore/Codecs/StringCodec.cs`
  - String decode/encode helper.
- Create `managed/packages/Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs`
  - `IReadOnlyList<T>` conversion helper backed by `JavaScriptArray`.
- Create `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
  - Test project referencing `Expo.ModulesCore`.
- Create `managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
  - Copy/adapt the JSI testhost loader with the new script name in errors.
- Create `managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs`
  - Copy/adapt runtime fixture.
- Create `managed/packages/Expo.ModulesCore.Tests/Fixtures/JavaScriptTestRuntime.cs`
  - Copy/adapt JavaScript evaluation helper.
- Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`
  - Generated-looking module dispatch tests copied/adapted from the HostFXR proof.
- Move `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`
  - New destination: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedArrayModuleTests.cs`.
- Remove `managed/packages/Expo.JSI.Tests/Modules/` after the move leaves it empty.
- Create `scripts/test-managed.sh`
  - Build `Expo.JSI`, build the Hermes testhost, run both managed test projects.
- Modify `scripts/test-managed.sh`
  - Compatibility wrapper that delegates to `scripts/test-managed.sh`.
- Modify `docs/README.md`, `docs/specs/README.md`, `docs/specs/modules-core-boundary.md`, `docs/specs/managed-jsi-wrappers.md`, `docs/specs/hermes-testhost.md`, and `AGENTS.md`
  - Merge accepted implementation facts into living docs/specs after code passes.

## Task 1: Add Failing ModulesCore Dispatch Tests

**Files:**
- Create: `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
- Create: `managed/packages/Expo.ModulesCore.Tests/Fixtures/NativeTestHost.cs`
- Create: `managed/packages/Expo.ModulesCore.Tests/Fixtures/HermesRuntimeFixture.cs`
- Create: `managed/packages/Expo.ModulesCore.Tests/Fixtures/JavaScriptTestRuntime.cs`
- Create: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`

- [ ] **Step 1: Create the test project**

Create `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Expo.ModulesCore/Expo.ModulesCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Copy and adapt testhost fixtures**

Copy these files from `managed/packages/Expo.JSI.Tests/Fixtures/` into
`managed/packages/Expo.ModulesCore.Tests/Fixtures/`:

```text
HermesRuntimeFixture.cs
JavaScriptTestRuntime.cs
NativeTestHost.cs
```

Then change their namespace from:

```csharp
namespace Expo.JSI.Tests.Fixtures;
```

to:

```csharp
namespace Expo.ModulesCore.Tests.Fixtures;
```

Add this import to the copied `HermesRuntimeFixture.cs` and
`JavaScriptTestRuntime.cs` files because they no longer live under the
`Expo.JSI.*` namespace tree:

```csharp
using Expo.JSI;
```

Add this import to the copied `NativeTestHost.cs` file for the same reason:

```csharp
using Expo.JSI;
```

In the copied `NativeTestHost.cs`, change the missing environment variable
message to:

```csharp
throw new InvalidOperationException($"{LibraryEnvVar} is not set. Run scripts/test-managed.sh.");
```

- [ ] **Step 3: Write generated-looking dispatch tests**

Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`:

```csharp
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedMathAndTextModuleTests
{
  [Fact]
  public void GeneratedLookingCodeDispatchesSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.Math.add(41.5, true)",
          "modules-core-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingCodePreservesStringValuesThroughModuleDispatch()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.Text.greet('Zoë\\u0000JS')",
          "modules-core-text-greet.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("Hello, Zoë\0JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedLookingTypeFailureIsCatchableInJavaScript()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      GeneratedMathAndTextModuleProvider.Register(runtime);

      using var result = fixture.Evaluate(
          "try { globalThis.expo.modules.Text.greet(42); 'no error'; } catch (e) { e.message; }",
          "modules-core-text-error.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Contains("string", result.AsString(), StringComparison.OrdinalIgnoreCase);
      return true;
    });
  }

  private sealed class MathModule
  {
    public double Add(double value, bool shouldAddOne) =>
        shouldAddOne ? value + 1.0 : value;
  }

  private sealed class TextModule
  {
    public string Greet(string name) => $"Hello, {name}";
  }

  private static class GeneratedMathAndTextModuleProvider
  {
    public static void Register(JavaScriptRuntime runtime)
    {
      using var math = ModuleRegistry.DefineModule(runtime, "Math");
      using var text = ModuleRegistry.DefineModule(runtime, "Text");

      GeneratedFunction.DefineSync(
          runtime,
          math,
          "add",
          2,
          MathAddHostFunction,
          new MathModule()
      );
      GeneratedFunction.DefineSync(
          runtime,
          text,
          "greet",
          1,
          TextGreetHostFunction,
          new TextModule()
      );
    }

    private static JavaScriptValue MathAddHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Math.add", arguments, 2);

      var module = (MathModule)context;
      var value = DoubleCodec.Decode(arguments.GetValue(0), runtime);
      var shouldAddOne = BoolCodec.Decode(arguments.GetValue(1), runtime);
      return DoubleCodec.Encode(module.Add(value, shouldAddOne), runtime);
    }

    private static JavaScriptValue TextGreetHostFunction(
        JavaScriptRuntime runtime,
        JavaScriptValueRef thisValue,
        JavaScriptArguments arguments,
        object context)
    {
      GeneratedFunction.RequireArgumentCount("Text.greet", arguments, 1);

      var module = (TextModule)context;
      var name = StringCodec.Decode(arguments.GetValue(0), runtime);
      return StringCodec.Encode(module.Greet(name), runtime);
    }
  }
}
```

- [ ] **Step 4: Run the new tests and confirm they fail because the package is missing**

Run:

```sh
EXPO_JSI_TESTHOST_LIBRARY=build/jsi-testhost/libexpo_jsi_testhost.dylib \
  dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  -c Debug \
  --filter GeneratedMathAndTextModuleTests
```

Expected: restore/build fails because `../Expo.ModulesCore/Expo.ModulesCore.csproj` does not exist.

## Task 2: Add ModulesCore Project And Registration Helpers

**Files:**
- Create: `managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`
- Create: `managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
- Create: `managed/packages/Expo.ModulesCore/GeneratedFunction.cs`

- [ ] **Step 1: Create the package project**

Create `managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../Expo.JSI/Expo.JSI.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add module namespace registration helper**

Create `managed/packages/Expo.ModulesCore/ModuleRegistry.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore;

public static class ModuleRegistry
{
  public static JavaScriptObject DefineModule(JavaScriptRuntime runtime, string moduleName)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);

    using var global = runtime.Global();
    using var expo = GetOrCreateObject(runtime, global, "expo");
    using var modules = GetOrCreateObject(runtime, expo, "modules");

    var module = runtime.CreateObject();
    using var moduleValue = module.AsValue();
    modules.SetProperty(moduleName, moduleValue);
    return module;
  }

  private static JavaScriptObject GetOrCreateObject(
      JavaScriptRuntime runtime,
      JavaScriptObject owner,
      string propertyName)
  {
    using var existingValue = owner.GetProperty(propertyName);
    if (existingValue.IsObject)
    {
      return existingValue.AsObject();
    }

    var created = runtime.CreateObject();
    using var createdValue = created.AsValue();
    owner.SetProperty(propertyName, createdValue);
    return created;
  }
}
```

- [ ] **Step 3: Add generated function helper**

Create `managed/packages/Expo.ModulesCore/GeneratedFunction.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore;

public static class GeneratedFunction
{
  public static void DefineSync(
      JavaScriptRuntime runtime,
      JavaScriptObject module,
      string name,
      uint parameterCount,
      JavaScriptHostFunction callback,
      object context)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    ArgumentNullException.ThrowIfNull(callback);
    ArgumentNullException.ThrowIfNull(context);

    using var function = runtime.CreateHostFunction(name, parameterCount, callback, context);
    using var functionValue = function.AsValue();
    module.SetProperty(name, functionValue);
  }

  public static void RequireArgumentCount(
      string functionName,
      JavaScriptArguments arguments,
      uint expected)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
    if (arguments.Count != expected)
    {
      throw new ArgumentException(
          $"{functionName} expects {expected} arguments, got {arguments.Count}."
      );
    }
  }
}
```

- [ ] **Step 4: Run focused build and expect codec errors next**

Run:

```sh
dotnet build managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj -c Debug
```

Expected: build fails because `Expo.ModulesCore.Codecs` and codec types do not exist yet.

## Task 3: Add Primitive And Array Codecs

**Files:**
- Create: `managed/packages/Expo.ModulesCore/Codecs/IJavaScriptCodec.cs`
- Create: `managed/packages/Expo.ModulesCore/Codecs/BoolCodec.cs`
- Create: `managed/packages/Expo.ModulesCore/Codecs/DoubleCodec.cs`
- Create: `managed/packages/Expo.ModulesCore/Codecs/StringCodec.cs`
- Create: `managed/packages/Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs`

- [ ] **Step 1: Add common codec interface**

Create `managed/packages/Expo.ModulesCore/Codecs/IJavaScriptCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public interface IJavaScriptCodec<T>
{
  static abstract T Decode(JavaScriptValueRef value, JavaScriptRuntime runtime);
  static abstract T Decode(JavaScriptValue value, JavaScriptRuntime runtime);
  static abstract JavaScriptValue Encode(T value, JavaScriptRuntime runtime);
}
```

- [ ] **Step 2: Add primitive codecs**

Create `managed/packages/Expo.ModulesCore/Codecs/BoolCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct BoolCodec : IJavaScriptCodec<bool>
{
  public static bool Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsBool();

  public static bool Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsBool();

  public static JavaScriptValue Encode(bool value, JavaScriptRuntime runtime) =>
      runtime.CreateBool(value);
}
```

Create `managed/packages/Expo.ModulesCore/Codecs/DoubleCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct DoubleCodec : IJavaScriptCodec<double>
{
  public static double Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static double Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsDouble();

  public static JavaScriptValue Encode(double value, JavaScriptRuntime runtime) =>
      runtime.CreateNumber(value);
}
```

Create `managed/packages/Expo.ModulesCore/Codecs/StringCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public readonly struct StringCodec : IJavaScriptCodec<string>
{
  public static string Decode(JavaScriptValueRef value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static string Decode(JavaScriptValue value, JavaScriptRuntime runtime) =>
      value.AsString();

  public static JavaScriptValue Encode(string value, JavaScriptRuntime runtime) =>
      runtime.CreateString(value);
}
```

- [ ] **Step 3: Add array codec**

Create `managed/packages/Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore.Codecs;

public static class JavaScriptArrayCodec<T, TCodec>
    where TCodec : IJavaScriptCodec<T>
{
  public static T[] DecodeToArray(JavaScriptValueRef value, JavaScriptRuntime runtime)
  {
    var array = value.AsArray();
    var length = checked((int)array.Length);
    var result = new T[length];

    for (var index = 0; index < length; index++)
    {
      var element = array.GetValue((uint)index);
      result[index] = TCodec.Decode(element, runtime);
    }

    return result;
  }

  public static JavaScriptValue Encode(IReadOnlyList<T> values, JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(values);

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

- [ ] **Step 4: Run focused dispatch tests and expect them to pass**

Run:

```sh
scripts/test-managed.sh --filter GeneratedMathAndTextModuleTests
```

Expected: the script still runs only `Expo.JSI.Tests`, so this filter produces no `Expo.ModulesCore.Tests` execution. This confirms the package compiles but the runner still needs broadening.

Then run:

```sh
EXPO_JSI_TESTHOST_LIBRARY=build/jsi-testhost/libexpo_jsi_testhost.dylib \
  dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  -c Debug \
  --filter GeneratedMathAndTextModuleTests
```

Expected: pass, with 3 matching tests.

## Task 4: Move Array Module Tests To ModulesCore

**Files:**
- Move: `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs`
- Create: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedArrayModuleTests.cs`
- Delete empty directory: `managed/packages/Expo.JSI.Tests/Modules/`

- [ ] **Step 1: Move and rename the array module test**

Move `managed/packages/Expo.JSI.Tests/Modules/ArrayConversionTests.cs` to
`managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedArrayModuleTests.cs`.

- [ ] **Step 2: Update namespace and imports**

At the top of the moved file, replace the old imports and namespace with:

```csharp
using Expo.JSI;
using Expo.ModulesCore;
using Expo.ModulesCore.Codecs;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;
```

- [ ] **Step 3: Remove local codec definitions from the moved test**

Delete these private definitions from the moved file:

```text
IJavaScriptCodec<T>
DoubleCodec
StringCodec
JavaScriptArrayCodec<T, TCodec>
```

Keep `ArrayModule` and the generated-looking provider.

- [ ] **Step 4: Update the generated array provider to use ModulesCore helpers**

In the moved test, replace `GeneratedArrayModuleProvider.Register` and its helper calls with:

```csharp
private static class GeneratedArrayModuleProvider
{
  public static void Register(JavaScriptRuntime runtime)
  {
    using var array = ModuleRegistry.DefineModule(runtime, "Array");

    GeneratedFunction.DefineSync(
        runtime,
        array,
        "sum",
        1,
        SumHostFunction,
        new ArrayModule()
    );
    GeneratedFunction.DefineSync(
        runtime,
        array,
        "labels",
        0,
        LabelsHostFunction,
        new ArrayModule()
    );
  }

  private static JavaScriptValue SumHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    GeneratedFunction.RequireArgumentCount("Array.sum", arguments, 1);

    var module = (ArrayModule)context;
    var values = JavaScriptArrayCodec<double, DoubleCodec>.DecodeToArray(
        arguments.GetValue(0),
        runtime
    );
    return DoubleCodec.Encode(module.Sum(values), runtime);
  }

  private static JavaScriptValue LabelsHostFunction(
      JavaScriptRuntime runtime,
      JavaScriptValueRef thisValue,
      JavaScriptArguments arguments,
      object context)
  {
    GeneratedFunction.RequireArgumentCount("Array.labels", arguments, 0);

    var module = (ArrayModule)context;
    return JavaScriptArrayCodec<string, StringCodec>.Encode(module.Labels(), runtime);
  }
}
```

- [ ] **Step 5: Remove the empty old Modules directory**

Run:

```sh
rmdir managed/packages/Expo.JSI.Tests/Modules
```

Expected: succeeds. If it fails, inspect the remaining file and move or delete only module-layer test leftovers.

- [ ] **Step 6: Run moved array tests**

Run:

```sh
EXPO_JSI_TESTHOST_LIBRARY=build/jsi-testhost/libexpo_jsi_testhost.dylib \
  dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  -c Debug \
  --filter GeneratedArrayModuleTests
```

Expected: pass, including the `IReadOnlyList<double>` parameter and `IReadOnlyList<string>` return coverage.

## Task 5: Broaden Managed Test Script

**Files:**
- Create: `scripts/test-managed.sh`
- Modify: `scripts/test-managed.sh`

- [ ] **Step 1: Create the canonical managed test script**

Create `scripts/test-managed.sh`:

```bash
#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
build_dir="$repo_root/build/jsi-testhost"
configuration="${CONFIGURATION:-Debug}"
hermes_root="${HERMES_PREBUILT_ROOT:-$repo_root/build/hermes/source/destroot}"
testhost_library="$build_dir/libexpo_jsi_testhost.dylib"

run_in_repo_env() {
  if command -v direnv >/dev/null 2>&1; then
    direnv exec "$repo_root" "$@"
  else
    "$@"
  fi
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  cat <<'EOF'
Usage: scripts/test-managed.sh [dotnet test args...]

Builds the Hermes-backed native JSI testhost and runs managed test projects.

Environment:
  CONFIGURATION           .NET configuration. Default: Debug
  HERMES_PREBUILT_ROOT    Hermes destroot. Default: <repo>/build/hermes/source/destroot
EOF
  exit 0
fi

if [[ ! -d "$hermes_root/include" ]]; then
  cat >&2 <<EOF
Hermes prebuilt was not found at:
  $hermes_root

Run:
  scripts/build-hermes-macos.sh
EOF
  exit 1
fi

echo "==> Building Expo.JSI"
dotnet build "$repo_root/managed/packages/Expo.JSI/Expo.JSI.csproj" -c "$configuration"

echo
echo "==> Building Expo.ModulesCore"
dotnet build "$repo_root/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" -c "$configuration"

echo
echo "==> Configuring native testhost"
run_in_repo_env cmake \
  -S "$repo_root/native/testhost" \
  -B "$build_dir" \
  -DHERMES_PREBUILT_ROOT="$hermes_root"

echo
echo "==> Building native testhost"
run_in_repo_env cmake --build "$build_dir" --target expo_jsi_testhost

echo
echo "==> Running Expo.JSI.Tests"
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
  dotnet test "$repo_root/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj" \
  -c "$configuration" \
  "$@"

echo
echo "==> Running Expo.ModulesCore.Tests"
EXPO_JSI_TESTHOST_LIBRARY="$testhost_library" \
  dotnet test "$repo_root/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj" \
  -c "$configuration" \
  "$@"
```

- [ ] **Step 2: Make the new script executable**

Run:

```sh
chmod +x scripts/test-managed.sh
```

Expected: `scripts/test-managed.sh` has executable mode in `git diff --summary`.

- [ ] **Step 3: Replace test-managed with compatibility wrapper**

Replace `scripts/test-managed.sh` with:

```bash
#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
exec "$repo_root/scripts/test-managed.sh" "$@"
```

- [ ] **Step 4: Run both script entry points**

Run:

```sh
scripts/test-managed.sh --filter "GeneratedMathAndTextModuleTests|GeneratedArrayModuleTests"
```

Expected: builds the testhost, runs `Expo.JSI.Tests` with no matching tests, then runs `Expo.ModulesCore.Tests` with matching generated module tests passing.

Run:

```sh
scripts/test-managed.sh --filter GeneratedArrayModuleTests
```

Expected: delegates to `scripts/test-managed.sh` and runs the same broader managed test flow.

## Task 6: Validate JSI Test Ownership After Move

**Files:**
- Inspect: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionTests.cs`
- Inspect: `managed/packages/Expo.JSI.Tests/HostFunctions/HostFunctionErrorTests.cs`
- Inspect: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptPrimitiveTests.cs`
- Inspect: `managed/packages/Expo.JSI.Tests/Runtime/JavaScriptArrayTests.cs`

- [ ] **Step 1: Confirm direct low-level coverage still exists**

Run:

```sh
rg -n "Zoë|a\\\\0b|HostFunction|JavaScriptValueRefAsArray|CreateStringRoundTripsStrictUtf8|ManagedExceptionIsCatchable" managed/packages/Expo.JSI.Tests
```

Expected: matches direct low-level tests for host functions, errors, strings, embedded NUL strings, and array refs.

- [ ] **Step 2: Run focused low-level JSI tests**

Run:

```sh
scripts/test-managed.sh --filter "HostFunctionTests|HostFunctionErrorTests|JavaScriptPrimitiveTests|JavaScriptArrayTests"
```

Expected: matching `Expo.JSI.Tests` cases pass. `Expo.ModulesCore.Tests` may report no matching tests for this filter.

- [ ] **Step 3: Add direct JSI tests only if a gap is found**

If Step 1 does not find a direct low-level assertion for a behavior moved out of
module tests, add the direct test in the relevant existing JSI test file before
continuing. For the current repo state, expected coverage already exists:

```text
Host function mechanics: HostFunctionTests.cs
Host function managed errors: HostFunctionErrorTests.cs
UTF-8 and embedded NUL strings: JavaScriptPrimitiveTests.cs
Array refs and array wrappers: JavaScriptArrayTests.cs
```

## Task 7: Update Living Docs And Specs

**Files:**
- Modify: `docs/README.md`
- Modify: `docs/specs/README.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/hermes-testhost.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Update docs front door**

In `docs/README.md`, replace the current state bullets for managed packages
with wording equivalent to:

```markdown
- `managed/packages/Expo.JSI/` contains the low-level C# wrapper package.
- `managed/packages/Expo.JSI.Tests/` contains low-level Hermes-backed wrapper tests.
- `managed/packages/Expo.ModulesCore/` contains generated-binding runtime helpers above `Expo.JSI`.
- `managed/packages/Expo.ModulesCore.Tests/` contains Hermes-backed module dispatch and conversion tests.
```

Replace the paragraph that says `Expo.ModulesCore` does not exist yet with:

```markdown
`Expo.ModulesCore` is the first higher-level package above `Expo.JSI`. It owns
generated-binding runtime helpers and typed conversion helpers that future
Roslyn-generated code can call. It does not yet expose the public v2 authored
API syntax or a source generator.
```

- [ ] **Step 2: Update specs index**

In `docs/specs/README.md`, keep `modules-core-boundary.md` listed and adjust
its description to:

```markdown
- `modules-core-boundary.md`: `Expo.ModulesCore` package boundary, generated-binding helpers, and module test ownership.
```

- [ ] **Step 3: Merge the accepted boundary into modules-core spec**

Rewrite `docs/specs/modules-core-boundary.md` so it states current behavior:

```markdown
# Modules Core Boundary

## Purpose

Define the boundary between low-level `Expo.JSI` wrappers and the
`Expo.ModulesCore` generated-binding helper package.

## Requirements

### Requirement: ModulesCore Owns Generated-Binding Helpers

`Expo.ModulesCore` SHALL own module registration helpers, generated dispatch
helpers, and typed conversion helpers above `Expo.JSI`.

#### Scenario: Generated-looking provider registers a module
- **GIVEN** generated-looking provider code has a `JavaScriptRuntime`
- **WHEN** it installs a module under `globalThis.expo.modules`
- **THEN** it SHALL use `Expo.ModulesCore` helpers instead of placing
  module-layer abstractions in `Expo.JSI`

### Requirement: ModulesCore Avoids Inert Authored Syntax

`Expo.ModulesCore` SHALL NOT expose public v2 authored API syntax before the
Roslyn generator milestone.

#### Scenario: Authored syntax is proposed
- **GIVEN** references describe future `[ExpoModule]`, `[JS]`, `[Record]`, or
  `[Event]` syntax
- **WHEN** no Roslyn generator consumes that syntax
- **THEN** the package SHALL keep that syntax out of production API

### Requirement: Generated Bindings Avoid Hot-Path Reflection

Generated v2 runtime bindings SHALL avoid runtime hot-path reflection and
dynamic invocation.

#### Scenario: Module provider invokes a method
- **GIVEN** generated provider code handles a JavaScript call
- **WHEN** it invokes the authored module method
- **THEN** it SHALL NOT use `Assembly.GetTypes`, `MethodInfo.Invoke`,
  `Delegate.DynamicInvoke`, `object?[]` as the normal argument container, or
  JSON serialization for ordinary JSI values

### Requirement: ModulesCore Owns Module Tests

`Expo.ModulesCore.Tests` SHALL own module dispatch and conversion behavior.

#### Scenario: Module conversion behavior is tested
- **GIVEN** a test proves generated-looking module conversion behavior
- **WHEN** the behavior is above low-level `Expo.JSI`
- **THEN** the test SHALL live in `Expo.ModulesCore.Tests`
```

- [ ] **Step 4: Update JSI wrapper and testhost specs**

In `docs/specs/managed-jsi-wrappers.md`, change the low-level package boundary
scenario to say module behavior has moved to `Expo.ModulesCore.Tests` and
`Expo.JSI.Tests` stays focused on low-level wrappers.

In `docs/specs/hermes-testhost.md`, replace the temporary module-test
requirement with a requirement that the canonical managed runner builds the
Hermes testhost and runs both `Expo.JSI.Tests` and `Expo.ModulesCore.Tests`.

- [ ] **Step 5: Update AGENTS verification command**

In `AGENTS.md`, replace:

```markdown
Run the Hermes-backed JSI test suite with `scripts/test-managed.sh`.
```

with:

```markdown
Run the Hermes-backed managed test suite with `scripts/test-managed.sh`.
`scripts/test-managed.sh` remains as a compatibility wrapper.
```

Also replace the code-change verification command:

```sh
scripts/test-managed.sh
```

with:

```sh
scripts/test-managed.sh
```

- [ ] **Step 6: Run docs checks**

Run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: `git diff --check` exits 0. The `rg` command exits 1 with no matches.

## Task 8: Full Verification

**Files:**
- Inspect all touched files.

- [ ] **Step 1: Run canonical managed tests**

Run:

```sh
scripts/test-managed.sh
```

Expected: native testhost builds, `Expo.JSI.Tests` passes, and `Expo.ModulesCore.Tests` passes.

- [ ] **Step 2: Run compatibility wrapper**

Run:

```sh
scripts/test-managed.sh --filter GeneratedArrayModuleTests
```

Expected: delegates to `scripts/test-managed.sh` and runs without script errors.

- [ ] **Step 3: Run formatting check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: exits 0.

If it fails because files need formatting, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

- [ ] **Step 4: Run whitespace check**

Run:

```sh
git diff --check
```

Expected: exits 0.

- [ ] **Step 5: Scan forbidden generated-binding patterns**

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
```

Expected: exits 1 with no matches.

- [ ] **Step 6: Confirm experiments were preserved**

Run:

```sh
test -f experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/GeneratedModuleProvider.cs
test -f experiments/hermes-console-hostfxr/managed/HostFxrJSIProof/MathModule.cs
```

Expected: both commands exit 0.

- [ ] **Step 7: Review final diff**

Run:

```sh
git status --short
git diff --stat
git diff -- managed/packages/Expo.JSI.Tests managed/packages/Expo.ModulesCore managed/packages/Expo.ModulesCore.Tests scripts docs AGENTS.md
```

Expected: diff shows the new package/test package, moved module tests, script updates, and living spec/doc updates. It must not show edits under `experiments/`.

## Task 9: Merge Delta Into Living Specs And Archive Planning Artifacts

**Files:**
- Move or remove: `docs/changes/2026-06-29-expo-modules-core/spec.md`
- Move or remove: `docs/changes/2026-06-29-expo-modules-core/plan.md`

- [ ] **Step 1: Confirm living specs contain the accepted current state**

Run:

```sh
rg -n "Expo.ModulesCore|test-managed|Generated Bindings Avoid|inert authored syntax|ModulesCore.Tests" docs/README.md docs/specs AGENTS.md
```

Expected: matches in front-door docs, living specs, and verification guidance.

- [ ] **Step 2: Archive or remove transient change artifacts**

After the implementation is verified and the accepted deltas are merged into
`docs/specs/`, move the change directory to archive:

```sh
mkdir -p docs/archive/changes
mv docs/changes/2026-06-29-expo-modules-core docs/archive/changes/2026-06-29-expo-modules-core
```

Expected: no current-state requirement remains only in `docs/archive/changes`.

- [ ] **Step 3: Final docs check**

Run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: `git diff --check` exits 0. The `rg` command exits 1 with no matches.

## Plan Self-Review

- Spec coverage: Tasks 1-4 implement package/test package, dispatch helpers, codecs, moved module tests, and copied/adapted experiment behavior. Task 5 implements the broader managed test script and compatibility wrapper. Task 6 preserves direct low-level JSI test ownership. Task 7 merges accepted deltas into living docs/specs. Task 8 verifies code and scripts. Task 9 archives transient artifacts after living specs are current.
- Red-flag scan: no unresolved marker words or unspecified implementation steps are intentionally left in the plan.
- Type consistency: helper names used by tests match the planned files: `ModuleRegistry`, `GeneratedFunction`, `IJavaScriptCodec<T>`, `BoolCodec`, `DoubleCodec`, `StringCodec`, and `JavaScriptArrayCodec<T, TCodec>`.
