# HostObject And Lazy Dotnet Modules Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a generic HostObject primitive and use it to make `_expoDotnet.modules` create module objects lazily on first registered module property access.

**Architecture:** `Expo.JSI` owns the generic HostObject ABI and managed wrapper. `Expo.ModulesCore` owns the lazy module registry semantics and keeps module lifetime routed through `DotnetRuntimeContext.ModuleRegistry`. Generated providers register module metadata for the lazy registry while keeping explicit registration into a caller-supplied modules object as the compatibility path.

**Tech Stack:** C++ JSI bridge, C ABI in `expo_jsi.h`, C# `Expo.JSI`, C# `Expo.ModulesCore`, Roslyn incremental generator, TypeScript adapter helpers, Hermes-backed managed tests, pnpm/Vitest autolinking tests.

---

## File Structure

- Modify `packages/expo-modules-dotnet/native/include/expo_jsi.h`: add HostObject callback typedefs, create-host-object ABI function pointer, and API table entry.
- Modify `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`: implement native `jsi::HostObject` wrapper and add it to `kApi`.
- Modify `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`: forward the new API entry through the counted API table.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`: add the managed function pointer and wrapper method.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptHostObject.cs`: public delegate/descriptor types for HostObject callbacks.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/HostObjectContext.cs`: callback context, error capture, and release logic mirroring `HostFunctionContext`.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`: add `CreateHostObject`.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptHostObjectTests.cs`: low-level HostObject behavior tests.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`: add lazy module definition registration and `_expoDotnet.modules` HostObject installation.
- Create `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/LazyModuleDefinition.cs`: immutable module metadata record.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`: default `Register(context)` registers lazy module definitions; explicit `Register(context, modules)` remains eager.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`: assert generated lazy registration source shape.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs`: add lazy registry behavior tests.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`: verify generated default registration is lazy but callable.
- Modify `packages/expo-modules-dotnet/src/index.ts`: change required module error message.
- Add `packages/expo-modules-dotnet/src/index.test.ts`: Vitest coverage for `requireDotnetModule`.
- Modify `docs/specs/runtime-and-abi.md`, `docs/specs/managed-jsi-wrappers.md`, `docs/specs/modules-core-boundary.md`, and `docs/roadmap.md`: merge accepted behavior and future direction.

## Task 1: Low-Level HostObject Tests And ABI

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptHostObjectTests.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptHostObject.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/HostObjectContext.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp`

- [ ] **Step 1: Write failing HostObject wrapper tests**

Add `JavaScriptHostObjectTests` with focused scenarios:

```csharp
[Fact]
public void HostObjectGetterReturnsValuesAndPropertyNames()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
        get: (callbackRuntime, propertyName, _) =>
            propertyName == "answer"
                ? callbackRuntime.CreateNumber(42)
                : callbackRuntime.CreateUndefined(),
        getPropertyNames: _ => new[] { "answer" }
    ));
    using var global = runtime.Global();
    using var hostValue = hostObject.AsValue();
    global.SetProperty("__hostObject", hostValue);

    using var value = fixture.Evaluate(
        "globalThis.__hostObject.answer + ':' + Object.keys(globalThis.__hostObject).join(',')",
        "host-object-getter.js"
    );

    Assert.Equal("42:answer", value.AsString());
    return true;
  });
}

[Fact]
public void HostObjectGetterExceptionIsCatchableInJavaScript()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
        get: (_, propertyName, _) => throw new InvalidOperationException($"boom:{propertyName}")
    ));
    using var global = runtime.Global();
    using var hostValue = hostObject.AsValue();
    global.SetProperty("__hostObject", hostValue);

    using var value = fixture.Evaluate(
        "try { globalThis.__hostObject.fail; 'no error'; } catch (error) { error.message; }",
        "host-object-getter-error.js"
    );

    Assert.Contains("boom:fail", value.AsString());
    return true;
  });
}

[Fact]
public void HostObjectWithoutSetterRejectsAssignment()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var hostObject = runtime.CreateHostObject(new JavaScriptHostObjectDescriptor(
        get: (callbackRuntime, _, _) => callbackRuntime.CreateUndefined()
    ));
    using var global = runtime.Global();
    using var hostValue = hostObject.AsValue();
    global.SetProperty("__hostObject", hostValue);

    using var value = fixture.Evaluate(
        "try { globalThis.__hostObject.name = 1; 'no error'; } catch (error) { error.message; }",
        "host-object-readonly-setter.js"
    );

    Assert.Contains("Cannot set property", value.AsString());
    return true;
  });
}
```

- [ ] **Step 2: Run the low-level test file to verify it fails**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj --filter JavaScriptHostObjectTests
```

Expected: compile failure because `JavaScriptHostObjectDescriptor` and `JavaScriptRuntime.CreateHostObject` do not exist.

- [ ] **Step 3: Add HostObject ABI declarations**

In `expo_jsi.h`, add callback typedefs near `expo_jsi_host_function_callback_fn`:

```c
typedef expo_jsi_value_result (*expo_jsi_host_object_get_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len);

typedef expo_jsi_error (*expo_jsi_host_object_set_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  expo_jsi_value_handle value);

typedef expo_jsi_property_names_result (*expo_jsi_host_object_get_property_names_fn)(
  void *callback_context,
  expo_jsi_runtime_handle runtime);

typedef expo_jsi_value_result (*expo_jsi_create_host_object_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_host_object_get_fn get,
  expo_jsi_host_object_set_fn set,
  expo_jsi_host_object_get_property_names_fn get_property_names,
  void *callback_context,
  expo_jsi_release_callback_context_fn release_callback_context);
```

Add `expo_jsi_create_host_object_fn create_host_object;` at the end of
`expo_jsi_api`, after `object_clear_native_state`, then increment the ABI
version in the C++/managed constants together.

- [ ] **Step 4: Add native HostObject implementation**

In `ExpoJsiBridge.cpp`, add a native wrapper near the host-function code:

```cpp
class ManagedHostObject final : public jsi::HostObject {
public:
  ManagedHostObject(expo_jsi_runtime_handle runtime,
                    expo_jsi_host_object_get_fn get,
                    expo_jsi_host_object_set_fn set,
                    expo_jsi_host_object_get_property_names_fn getPropertyNames,
                    void *callbackContext,
                    expo_jsi_release_callback_context_fn releaseContext)
    : runtime(runtime),
      getCallback(get),
      setCallback(set),
      getPropertyNamesCallback(getPropertyNames),
      callbackContext(callbackContext),
      releaseContext(releaseContext) {}

  ~ManagedHostObject() override
  {
    if (releaseContext != nullptr) {
      releaseContext(callbackContext);
    }
  }

  jsi::Value get(jsi::Runtime &jsRuntime, const jsi::PropNameID &name) override;
  void set(jsi::Runtime &jsRuntime, const jsi::PropNameID &name, const jsi::Value &value) override;
  std::vector<jsi::PropNameID> getPropertyNames(jsi::Runtime &jsRuntime) override;

private:
  expo_jsi_runtime_handle runtime;
  expo_jsi_host_object_get_fn getCallback;
  expo_jsi_host_object_set_fn setCallback;
  expo_jsi_host_object_get_property_names_fn getPropertyNamesCallback;
  void *callbackContext;
  expo_jsi_release_callback_context_fn releaseContext;
};
```

Implement `createHostObject` to validate callbacks, create
`jsi::Object::createFromHostObject`, and return an owned `ValueHandle`.
When callback results contain errors, throw `jsi::JSError` with the copied
message so JavaScript catches an `Error`. For read-only setters, throw a
`jsi::JSError` without crossing managed code.

- [ ] **Step 5: Add managed HostObject types and interop**

Create `JavaScriptHostObject.cs`:

```csharp
namespace Expo.JSI;

public sealed record JavaScriptHostObjectDescriptor(
    JavaScriptHostObjectGetter Get,
    JavaScriptHostObjectSetter? Set = null,
    JavaScriptHostObjectPropertyNamesGetter? GetPropertyNames = null,
    object? State = null);

public delegate JavaScriptValue JavaScriptHostObjectGetter(
    JavaScriptRuntime runtime,
    string propertyName,
    object? state);

public delegate void JavaScriptHostObjectSetter(
    JavaScriptRuntime runtime,
    string propertyName,
    JavaScriptValueRef value,
    object? state);

public delegate IReadOnlyList<string> JavaScriptHostObjectPropertyNamesGetter(object? state);
```

Create `Interop/HostObjectContext.cs` modeled after `HostFunctionContext`, with
`CaptureException`, `ToIntPtr`, `FromIntPtr`, and `Release`. Add
`JavaScriptRuntime.CreateHostObject(JavaScriptHostObjectDescriptor descriptor)`
that calls `ExpoJsiApi.CreateHostObjectValue`.

- [ ] **Step 6: Update testhost API forwarding**

In `ExpoJsiTestHost.cpp`, set `runtime.countedApi.create_host_object` to the
inner API function so all low-level tests use the counted API table.

- [ ] **Step 7: Run HostObject tests until they pass**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj --filter JavaScriptHostObjectTests
```

Expected: all `JavaScriptHostObjectTests` pass.

- [ ] **Step 8: Commit HostObject primitive**

```sh
git add packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
  packages/expo-modules-dotnet/native/testhost/src/ExpoJsiTestHost.cpp \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptHostObjectTests.cs
git diff --cached --check
git commit -m "feat(jsi): add managed host objects"
```

## Task 2: One-Stage Lazy Module Registry

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/LazyModuleDefinition.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs`

- [ ] **Step 1: Add failing lazy registry tests**

Add tests to `ModuleRegistryTests`:

```csharp
[Fact]
public void LazyDotnetModulesObjectEnumeratesNamesWithoutCreatingModules()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var context = new DotnetRuntimeContext(runtime);
    var createCount = 0;
    context.ModuleRegistry.RegisterLazyModule(new LazyModuleDefinition(
        "Camera",
        (moduleContext, modules) =>
        {
          createCount++;
          return moduleContext.ModuleRegistry.DefineModule(modules, "Camera");
        }
    ));

    using var names = fixture.Evaluate(
        "Object.keys(globalThis._expoDotnet.modules).join(',')",
        "lazy-modules-keys.js"
    );

    Assert.Equal("Camera", names.AsString());
    Assert.Equal(0, createCount);
    return true;
  });
}

[Fact]
public void LazyDotnetModulesObjectCreatesKnownModuleOnFirstRead()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var context = new DotnetRuntimeContext(runtime);
    var createCount = 0;
    context.ModuleRegistry.RegisterLazyModule(new LazyModuleDefinition(
        "Camera",
        (moduleContext, modules) =>
        {
          createCount++;
          var module = moduleContext.ModuleRegistry.DefineModule(modules, "Camera");
          using var value = moduleContext.Runtime.CreateString("ready");
          module.SetProperty("status", value);
          return module;
        }
    ));

    using var result = fixture.Evaluate(
        "const first = globalThis._expoDotnet.modules.Camera;" +
        "const second = globalThis._expoDotnet.modules.Camera;" +
        "first === second && first.status",
        "lazy-modules-first-read.js"
    );

    Assert.Equal("ready", result.AsString());
    Assert.Equal(1, createCount);
    return true;
  });
}

[Fact]
public void LazyDotnetModulesObjectReturnsUndefinedForUnknownAndProbeProperties()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var context = new DotnetRuntimeContext(runtime);
    context.ModuleRegistry.RegisterLazyModule(new LazyModuleDefinition(
        "Camera",
        (moduleContext, modules) => moduleContext.ModuleRegistry.DefineModule(modules, "Camera")
    ));

    using var result = fixture.Evaluate(
        "String(globalThis._expoDotnet.modules.Unknown) + ':' + " +
        "String(globalThis._expoDotnet.modules.$$typeof)",
        "lazy-modules-unknown.js"
    );

    Assert.Equal("undefined:undefined", result.AsString());
    return true;
  });
}
```

- [ ] **Step 2: Run the module registry tests to verify they fail**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter ModuleRegistryTests
```

Expected: compile failure because `LazyModuleDefinition` and `RegisterLazyModule` do not exist.

- [ ] **Step 3: Add lazy module definition type**

Create `LazyModuleDefinition.cs`:

```csharp
using Expo.JSI;

namespace Expo.ModulesCore;

public sealed record LazyModuleDefinition(
    string Name,
    Func<DotnetRuntimeContext, JavaScriptObject, JavaScriptObject> CreateModule);
```

- [ ] **Step 4: Implement lazy registry in ModuleRegistry**

In `ModuleRegistry`, add:

```csharp
private readonly DotnetRuntimeContext context;
private readonly Dictionary<string, LazyModuleDefinition> lazyModules = new(StringComparer.Ordinal);
private readonly Dictionary<string, JavaScriptObject> lazyModuleObjects = new(StringComparer.Ordinal);

public void RegisterLazyModule(LazyModuleDefinition definition)
{
  ArgumentNullException.ThrowIfNull(definition);
  ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);

  lock (gate)
  {
    ThrowIfDisposedLocked();
    lazyModules[definition.Name] = definition;
  }

  EnsureLazyDotnetModulesObject();
}
```

Add `EnsureLazyDotnetModulesObject` to create/reuse `globalThis._expoDotnet`
and set `modules` to a HostObject. The HostObject getter should:

```csharp
private JavaScriptValue GetLazyModuleProperty(string name)
{
  if (name == "$$typeof")
  {
    return runtime.CreateUndefined();
  }

  LazyModuleDefinition definition;
  lock (gate)
  {
    ThrowIfDisposedLocked();
    if (!lazyModules.TryGetValue(name, out definition!))
    {
      return runtime.CreateUndefined();
    }
    if (lazyModuleObjects.TryGetValue(name, out var cached))
    {
      return cached.AsValue();
    }
  }

  using var modules = GetOrCreateDotnetModulesObject();
  var created = definition.CreateModule(context, modules);
  lock (gate)
  {
    lazyModuleObjects[name] = created;
  }
  return created.AsValue();
}
```

Change `DotnetRuntimeContext` construction from `new ModuleRegistry(Runtime, objects)`
to an internal `new ModuleRegistry(this, objects)` constructor so the registry
has the owning context for lazy module creation callbacks.

- [ ] **Step 5: Run lazy registry tests until they pass**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter "ModuleRegistryTests"
```

Expected: all `ModuleRegistryTests` pass.

- [ ] **Step 6: Commit lazy registry**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs
git diff --cached --check
git commit -m "feat(modules-core): add lazy dotnet modules registry"
```

## Task 3: Generator Uses Lazy Default Registration

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs`
- Verify: `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- Verify: `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`

- [ ] **Step 1: Update generator source-shape tests first**

In `ExpoModulesGeneratorTests.GeneratorEmitsDeterministicProviderForAssembly`,
replace the default registration assertions with lazy registration assertions:

```csharp
Assert.Contains("context.ModuleRegistry.RegisterLazyModule(", source);
Assert.Contains("new global::Expo.ModulesCore.LazyModuleDefinition(", source);
Assert.Contains("\"Math\"", source);
Assert.DoesNotContain("using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();", source);
Assert.Contains("public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context, global::Expo.JSI.JavaScriptObject modules)", source);
Assert.Contains("context.ModuleRegistry.DefineModule(modules, \"Math\")", source);
```

- [ ] **Step 2: Add generated runtime behavior test**

In `GeneratedMathAndTextModuleTests`, add:

```csharp
[Fact]
public void DefaultGeneratedRegistrationCreatesModulesLazily()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var context = new DotnetRuntimeContext(runtime);
    GeneratedMathAndTextModuleProvider.Register(context);

    using var beforeAccess = fixture.Evaluate(
        "Object.keys(globalThis._expoDotnet.modules).join(',') + ':' + " +
        "String(globalThis._expoDotnet.modules.Unknown)",
        "generated-lazy-before-access.js"
    );
    Assert.Equal("Math,Text:undefined", beforeAccess.AsString());

    using var result = fixture.Evaluate(
        "globalThis._expoDotnet.modules.Math.add(41.5, true)",
        "generated-lazy-call.js"
    );

    Assert.Equal(42.5, result.AsDouble());
    return true;
  });
}
```

- [ ] **Step 3: Run generator and generated module tests to verify failure**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter GeneratedMathAndTextModuleTests
```

Expected: source-shape assertions fail until generator output changes.

- [ ] **Step 4: Update generated provider output**

In `ExpoModulesGenerator.EmitProvider`, keep `Register(context, modules)` eager.
Change default `Register(context)` to emit one `RegisterLazyModule` call per
module:

```csharp
public static void Register(global::Expo.ModulesCore.DotnetRuntimeContext context)
{
  global::System.ArgumentNullException.ThrowIfNull(context);
  context.ModuleRegistry.RegisterLazyModule(new global::Expo.ModulesCore.LazyModuleDefinition(
      "Math",
      static (context, modules) => RegisterMath(context, modules)
  ));
}

private static global::Expo.JSI.JavaScriptObject RegisterMath(
    global::Expo.ModulesCore.DotnetRuntimeContext context,
    global::Expo.JSI.JavaScriptObject modules)
{
  var module_Math = context.ModuleRegistry.DefineModule(modules, "Math");
  var instance_Math = context.ModuleRegistry.GetOrCreateModule(
      "Math",
      static () => new global::Expo.TestModules.MathModule()
  );
  return module_Math;
}
```

Refactor the generator so `Register(context, modules)` calls per-module helper
methods, and each lazy callback calls only the helper for its own module. Do not
generate one callback that registers every module when a single module is read.

- [ ] **Step 5: Check autolinking aggregator remains valid**

Run:

```sh
pnpm --filter expo-modules-dotnet-autolinking test -- generateAggregator
```

Expected: aggregator tests still pass because `LinkedExpoModulesProvider`
continues to call each provider's `Register(context)`.

- [ ] **Step 6: Run generator and generated module tests until they pass**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj --filter GeneratedMathAndTextModuleTests
```

Expected: both commands pass.

- [ ] **Step 7: Commit generator lazy registration**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedMathAndTextModuleTests.cs
git diff --cached --check
git commit -m "feat(generator): register modules lazily by default"
```

## Task 4: JavaScript Required Module Error

**Files:**
- Modify: `packages/expo-modules-dotnet/src/index.ts`
- Create: `packages/expo-modules-dotnet/src/index.test.ts`

- [ ] **Step 1: Add failing TypeScript helper tests**

Create `index.test.ts`:

```ts
import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('./NativeExpoModulesDotnetInstaller', () => ({
  default: {
    installModules: () => true,
  },
}));

describe('requireDotnetModule', () => {
  afterEach(() => {
    delete globalThis._expoDotnet;
  });

  it('throws an autolinking hint for missing required modules', async () => {
    globalThis._expoDotnet = { modules: {} };
    const { requireDotnetModule } = await import('./index');

    expect(() => requireDotnetModule('Camera')).toThrow(
      "Module 'Camera' is not registered. Check that it is autolinked correctly."
    );
  });

  it('returns registered modules', async () => {
    const camera = { takePicture: () => 'ok' };
    globalThis._expoDotnet = { modules: { Camera: camera } };
    const { requireDotnetModule } = await import('./index');

    expect(requireDotnetModule('Camera')).toBe(camera);
  });
});
```

- [ ] **Step 2: Run the package test to verify it fails**

Run:

```sh
pnpm exec vitest run packages/expo-modules-dotnet/src/index.test.ts
```

Expected: failure with the old `.NET module 'Camera' is not installed.` message.

- [ ] **Step 3: Update `requireDotnetModule`**

Change the missing-module branch in `index.ts`:

```ts
if (module == null) {
  throw new Error(
    `Module '${name}' is not registered. Check that it is autolinked correctly.`
  );
}
```

- [ ] **Step 4: Run the helper test until it passes**

Run:

```sh
pnpm exec vitest run packages/expo-modules-dotnet/src/index.test.ts
```

Expected: `index.test.ts` passes.

- [ ] **Step 5: Commit JS required module error**

```sh
git add packages/expo-modules-dotnet/src/index.ts \
  packages/expo-modules-dotnet/src/index.test.ts
git diff --cached --check
git commit -m "fix(js): clarify missing dotnet module errors"
```

## Task 5: Merge Delta Into Living Specs And Roadmap

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/managed-jsi-wrappers.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/roadmap.md`

- [ ] **Step 1: Update runtime and ABI spec**

Add a `HostObject ABI` requirement to `runtime-and-abi.md` covering:

```markdown
### Requirement: HostObject ABI

The ABI SHALL expose generic JavaScript HostObjects through opaque handles.
Managed callbacks SHALL provide string property get, optional set, and property
name enumeration without exposing raw JSI layouts to C#.
```

Include scenarios for getter success, getter error propagation, read-only set,
and callback-context release without touching JSI.

- [ ] **Step 2: Update managed wrapper spec**

Add a `HostObject Wrapper` requirement to `managed-jsi-wrappers.md` covering:

```markdown
### Requirement: HostObject Wrapper

`JavaScriptRuntime` SHALL create HostObject-backed `JavaScriptObject` wrappers
from managed callbacks. Setter values SHALL be invocation-scoped and must be
retained before storage beyond the callback.
```

- [ ] **Step 3: Update ModulesCore boundary spec**

Add lazy module registry requirements to `modules-core-boundary.md`:

```markdown
### Requirement: Lazy Dotnet Module Registry

`Expo.ModulesCore` SHALL install `_expoDotnet.modules` as a one-stage lazy
HostObject backed by generated module metadata.
```

Include scenarios for registered read, unknown read returning `undefined`,
probe read returning `undefined`, enumeration without module creation, root
mutation error, and post-teardown catchable JS error.

- [ ] **Step 4: Update roadmap**

In `docs/roadmap.md`, mark HostObject/lazy module access as current or complete
according to the implemented result. Add this future direction under richer
runtime surface:

```markdown
- **Two-stage lazy modules**: if root module property access becomes measurable
  overhead, replace one-stage lazy module creation with a two-stage lazy shell
  similar to upstream `LazyObject`.
```

Keep SharedObject/SharedRef as future work and mention class/prototype plus
NativeState direction without claiming implementation.

- [ ] **Step 5: Run docs checks**

Run:

```sh
git diff --check
rg "self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository" docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
```

Expected: `git diff --check` passes; `rg` returns no matches.

- [ ] **Step 6: Commit living spec merge**

```sh
git add docs/specs/runtime-and-abi.md \
  docs/specs/managed-jsi-wrappers.md \
  docs/specs/modules-core-boundary.md \
  docs/roadmap.md
git diff --cached --check
git commit -m "docs(hostobject): update living specs"
```

## Task 6: Final Verification And Transient Artifact Cleanup

**Files:**
- Delete after implementation is accepted: `docs/changes/2026-07-08-hostobject-lazy-modules/spec.md`
- Delete after implementation is accepted: `docs/changes/2026-07-08-hostobject-lazy-modules/plan.md`

- [ ] **Step 1: Run managed verification**

Run:

```sh
scripts/test-managed.sh
```

Expected: Hermes-backed managed suite passes.

- [ ] **Step 2: Run JS/package verification**

Run:

```sh
pnpm exec vitest run packages/expo-modules-dotnet/src/index.test.ts
pnpm --filter expo-modules-dotnet-autolinking test -- generateAggregator
```

Expected: both commands pass.

- [ ] **Step 3: Run format and hot-path scans**

Run:

```sh
scripts/format.sh --check --all
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
git diff --check
```

Expected: formatter check passes; hot-path scan has no new generated-binding hot-path reflection/dynamic invocation; diff check passes.

- [ ] **Step 4: Remove transient change artifacts**

After code, tests, docs, and roadmap reflect the accepted behavior, remove the
change package:

```sh
git rm docs/changes/2026-07-08-hostobject-lazy-modules/spec.md \
  docs/changes/2026-07-08-hostobject-lazy-modules/plan.md
```

- [ ] **Step 5: Commit cleanup**

```sh
git add docs/specs docs/roadmap.md
git diff --cached --check
git commit -m "docs(hostobject): archive completed change plan"
```

- [ ] **Step 6: Final status**

Run:

```sh
git status --short
```

Expected: no uncommitted files from this slice unless the user has made
unrelated changes.
