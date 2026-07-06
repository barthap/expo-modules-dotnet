# Module Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build production module events by making event-capable generated modules `_expoDotnet.NativeModule` instances and emitting from C# through runtime-scoped services.

**Architecture:** Native C++ owns generic JavaScript class/prototype mechanics behind opaque ABI handles. `Expo.JSI` exposes reusable class/object wrappers. `Expo.ModulesCore` owns the `_expoDotnet.EventEmitter` / `_expoDotnet.NativeModule` installation, managed listener state behind inherited event methods, runtime object factory, module registry integration, event target mapping, and generated event dispatch. Generated providers declare event-capable modules, create native-module-backed JS objects, and attach direct-call functions as own properties.

**Correction note:** Earlier drafts of this plan placed `EventEmitter` listener state and `_expoDotnet` base-class installation in the native `Expo.JSI` bridge. The accepted production boundary keeps those ModulesCore-specific responsibilities in `Expo.ModulesCore`; the ABI exposes only generic class/prototype primitives.

**Tech Stack:** C++ JSI bridge, C ABI, C#/.NET, Roslyn incremental generator, Hermes-backed managed tests, xUnit.

---

## File Structure

- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
  - Add ABI entries for reusable class/prototype operations.
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
  - Implement reusable `createClass`, `createInheritingClass`, and `createObjectWithPrototype`.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
  - Add function pointers, validation, wrappers, and bump expected ABI version.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
  - Expose low-level class/prototype helpers through managed wrappers.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptValue.cs`
  - Ensure constructor-call results can be retained as objects through existing `AsObject` behavior.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptClassTests.cs`
  - Cover class creation, subclass prototype inheritance, and object-with-prototype.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptObjectFactory.cs`
  - Runtime-scoped helper that installs ModulesCore base classes, owns listener state, and constructs named Expo class instances.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
  - Own `JavaScriptObjectFactory` and `ModuleEventEmitter`.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
  - Add `DefineNativeModule` while preserving existing plain-object helpers.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs`
  - Attach C# module instances to JS event targets and emit scheduled payloads.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventsAttribute.cs`
  - Declare supported event names.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStartObservingAttribute.cs`
  - Declare start observing hooks.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStopObservingAttribute.cs`
  - Declare stop observing hooks.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
  - Track event names and observing hooks.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
  - Add diagnostics for invalid event names and invalid observing hooks.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
  - Parse event syntax, emit native-module-backed registration, attach event targets, and emit observing host functions.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Cover generated source shape and diagnostics.
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs`
  - Hermes-backed event behavior.
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
  - Add generated event test modules.
- Modify: `docs/specs/runtime-and-abi.md`
  - Merge accepted ABI class/prototype requirements after implementation.
- Modify: `docs/specs/modules-core-boundary.md`
  - Merge accepted ModulesCore event requirements after implementation.

## Task 1: ABI Class And Base-Class Support

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_jsi.h`
- Modify: `packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptClassTests.cs`

- [ ] **Step 1: Write failing low-level class tests**

Create `JavaScriptClassTests.cs` with tests for:

```csharp
[Fact]
public void CreateObjectWithPrototypeUsesPrototypeMethods()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var global = runtime.Global();
    using var objectConstructorValue = global.GetProperty("Object");
    using var objectConstructor = objectConstructorValue.AsObject();
    using var prototype = runtime.CreateObject();
    using var marker = runtime.CreateString("from prototype");
    prototype.SetProperty("marker", marker);

    using var created = runtime.CreateObjectWithPrototype(prototype);
    using var result = created.GetProperty("marker");

    Assert.Equal("from prototype", result.AsString());
    return true;
  });
}

[Fact]
public void CreateClassWithSuperclassLinksPrototypeChain()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var baseClass = runtime.CreateClass("BridgeBase");
    using var subclass = runtime.CreateClass("BridgeSubclass", baseClass);
    using var global = runtime.Global();
    using var baseValue = baseClass.AsValue();
    using var subclassValue = subclass.AsValue();
    global.SetProperty("__BridgeBase", baseValue);
    global.SetProperty("__BridgeSubclass", subclassValue);

    using var result = fixture.Evaluate(
        "const instance = new globalThis.__BridgeSubclass();" +
        "instance instanceof globalThis.__BridgeSubclass && " +
        "instance instanceof globalThis.__BridgeBase && " +
        "Object.getPrototypeOf(globalThis.__BridgeSubclass) === globalThis.__BridgeBase && " +
        "Object.getPrototypeOf(globalThis.__BridgeSubclass.prototype) === globalThis.__BridgeBase.prototype",
        "create-subclass-check.js"
    );

    Assert.True(result.AsBool());
    return true;
  });
}
```

- [ ] **Step 2: Run focused test and verify failure**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj --filter JavaScriptClassTests
```

Expected: fails because `CreateObjectWithPrototype` and `CreateClass` do not exist.

- [ ] **Step 3: Add ABI declarations**

Add ABI typedefs and table entries:

```c
typedef expo_jsi_value_result (*expo_jsi_create_object_with_prototype_fn)(
  expo_jsi_runtime_handle runtime,
  expo_jsi_value_handle prototype);

typedef expo_jsi_value_result (*expo_jsi_create_class_fn)(
  expo_jsi_runtime_handle runtime, const char *name, int32_t name_len);

typedef expo_jsi_value_result (*expo_jsi_create_class_with_superclass_fn)(
  expo_jsi_runtime_handle runtime,
  const char *name,
  int32_t name_len,
  expo_jsi_value_handle superclass);
```

Bump the native and managed ABI version from `16` to `17`.

- [ ] **Step 4: Implement native class helpers**

In `ExpoJsiBridge.cpp`, add internal helpers modeled on upstream Expo:

```cpp
jsi::Function createClass(jsi::Runtime &runtime, const char *name, ClassConstructor constructor);
jsi::Function createInheritingClass(jsi::Runtime &runtime, const char *name, jsi::Function &baseClass, ClassConstructor constructor);
jsi::Object createObjectWithPrototype(jsi::Runtime &runtime, jsi::Object &prototype);
```

Expose those helpers through the ABI only as generic class/prototype primitives. Do not install `globalThis._expoDotnet.EventEmitter`, `globalThis._expoDotnet.NativeModule`, or event listener state in `ExpoJsiBridge.cpp`; ModulesCore owns those classes.

- [ ] **Step 5: Add managed interop wrappers**

In `ExpoJsiApi.cs`, add function pointers, validation, and methods:

```csharp
public ExpoJsiValueResult CreateObjectWithPrototypeValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    ExpoJsiValueHandle prototypeHandle);

public ExpoJsiValueResult CreateClassValue(ExpoJsiRuntimeHandle runtimeHandle, string name);

public ExpoJsiValueResult CreateClassWithSuperclassValue(
    ExpoJsiRuntimeHandle runtimeHandle,
    string name,
    ExpoJsiValueHandle superclassHandle);
```

In `JavaScriptRuntime.cs`, add:

```csharp
public JavaScriptObject CreateObjectWithPrototype(JavaScriptObject prototype);

public JavaScriptFunction CreateClass(string name);

public JavaScriptFunction CreateClass(string name, JavaScriptFunction superclass);
```

- [ ] **Step 6: Run focused low-level tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj --filter JavaScriptClassTests
```

Expected: pass.

- [ ] **Step 7: Commit**

```sh
git add packages/expo-modules-dotnet/native/include/expo_jsi.h \
  packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/Interop/ExpoJsiApi.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptClassTests.cs
git diff --cached --check
git commit -m "Add JavaScript class ABI support"
```

## Task 2: Runtime Object Factory And Native Modules

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptObjectFactory.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs`

- [ ] **Step 1: Write failing registry tests**

Add tests that verify:

```csharp
[Fact]
public void DefineNativeModuleCreatesNativeModuleInstance()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    using var context = new DotnetRuntimeContext(runtime);
    using var modules = context.ModuleRegistry.GetOrCreateDotnetModulesObject();
    using var module = context.ModuleRegistry.DefineNativeModule(modules, "Events");

    using var result = fixture.Evaluate(
        "const module = globalThis._expoDotnet.modules.Events;" +
        "module instanceof globalThis._expoDotnet.NativeModule && " +
        "module instanceof globalThis._expoDotnet.EventEmitter && " +
        "typeof module.addListener === 'function'",
        "native-module-registry.js"
    );

    Assert.True(result.AsBool());
    return true;
  });
}
```

- [ ] **Step 2: Run focused test and verify failure**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~ModuleRegistryTests"
```

Expected: fails because `DefineNativeModule` does not exist.

- [ ] **Step 3: Implement `JavaScriptObjectFactory`**

Create a sealed runtime-scoped helper:

```csharp
public sealed class JavaScriptObjectFactory
{
  private readonly JavaScriptRuntime runtime;

  internal JavaScriptObjectFactory(JavaScriptRuntime runtime)
  {
    this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    EnsureBaseClasses();
  }

  public JavaScriptFunction GetExpoClass(string className);

  public JavaScriptObject CreateExpoClassInstance(string className);
}
```

`GetExpoClass` reads `globalThis._expoDotnet[className]` and retains it as a function. `CreateExpoClassInstance` calls that function as a constructor and returns the object result.

- [ ] **Step 4: Wire context and registry**

Add `DotnetRuntimeContext.Objects` and construct `ModuleRegistry` with the factory. Add:

```csharp
public JavaScriptObject DefineNativeModule(JavaScriptObject modules, string moduleName)
{
  using var existingModuleValue = modules.GetProperty(moduleName);
  if (existingModuleValue.IsObject)
  {
    return existingModuleValue.AsObject();
  }

  var module = objectFactory.CreateExpoClassInstance("NativeModule");
  using var moduleValue = module.AsValue();
  modules.SetProperty(moduleName, moduleValue);
  return module;
}
```

- [ ] **Step 5: Run focused registry tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~ModuleRegistryTests"
```

Expected: pass.

- [ ] **Step 6: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/JavaScriptObjectFactory.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/ModuleRegistryTests.cs
git diff --cached --check
git commit -m "Create native module objects through context"
```

## Task 3: Event Syntax And Generated NativeModule Registration

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventsAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Write failing generator tests**

Add tests that assert this input:

```csharp
[ExpoModule("Device")]
[Events("onChange", "onReady")]
public sealed partial class DeviceModule
{
  public DeviceModule(DotnetRuntimeContext context) {}
}
```

emits:

```csharp
using var module_Device = context.ModuleRegistry.DefineNativeModule(modules, "Device");
context.Events.Attach(instance_Device, module_Device, "Device", new[] { "onChange", "onReady" });
```

Also assert duplicate or blank event names produce a new diagnostic, e.g. `EXPOJSI009`.

- [ ] **Step 2: Run generator tests and verify failure**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
```

Expected: new assertions fail.

- [ ] **Step 3: Implement attribute and model**

Create:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventsAttribute : Attribute
{
  public EventsAttribute(params string[] names) => Names = names;
  public IReadOnlyList<string> Names { get; }
}
```

Track `EquatableArray<string> EventNames` in `ExpoModuleModel`.

- [ ] **Step 4: Emit native-module registration**

When a module has events, emit `DefineNativeModule` and `context.Events.Attach`. Modules without events keep the existing `DefineModule` path.

- [ ] **Step 5: Run generator tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
```

Expected: pass.

- [ ] **Step 6: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/EventsAttribute.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs
git diff --cached --check
git commit -m "Generate native module event metadata"
```

## Task 4: Module Event Emission

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs`

- [ ] **Step 1: Write failing Hermes event tests**

Add an event module:

```csharp
[ExpoModule("GeneratedEvents")]
[Events("onChange", "onReady")]
public sealed partial class GeneratedEventsModule : Module
{
  public GeneratedEventsModule(DotnetRuntimeContext context) : base(context) {}

  [JS]
  public Task EmitChangeAsync(string value) =>
      RuntimeContext.Events.EmitAsync(this, "onChange", value);

  [JS]
  public Task EmitReadyAsync() =>
      RuntimeContext.Events.EmitAsync(this, "onReady");
}
```

Test listener delivery:

```csharp
using var result = fixture.Evaluate(
    "const events = globalThis._expoDotnet.modules.GeneratedEvents;" +
    "let seen = '';" +
    "events.addListener('onChange', value => { seen = value; });" +
    "events.EmitChangeAsync('payload').then(() => seen)",
    "generated-events-delivery.js"
);
fixture.WaitUntilIdle();
```

Assert the promise settles to `"payload"` using the existing async test pattern.

- [ ] **Step 2: Run focused tests and verify failure**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedEventModuleTests"
```

Expected: fails because `RuntimeContext.Events` and event dispatch do not exist.

- [ ] **Step 3: Implement `ModuleEventEmitter`**

Implement:

```csharp
public sealed class ModuleEventEmitter
{
  public void Attach(object module, JavaScriptObject target, string moduleName, IReadOnlyList<string> eventNames);
  public Task EmitAsync<T>(object module, string eventName, T payload, CancellationToken cancellationToken = default);
  public Task EmitAsync(object module, string eventName, CancellationToken cancellationToken = default);
}
```

Store retained event target handles per module instance, validate declared event names, and dispose retained targets when `DotnetRuntimeContext` is disposed.

- [ ] **Step 4: Dispatch through inherited `emit`**

In scheduled runtime work:

```csharp
using var emitValue = target.GetProperty("emit");
using var emit = emitValue.AsFunction();
using var eventNameValue = runtime.CreateString(eventName);
using var payloadValue = codec.Encode(payload, runtime);
using var result = emit.CallWithThis(target, eventNameValue, payloadValue);
```

For payload-less events, call with only the event name.

- [ ] **Step 5: Add module convenience methods**

Add protected helpers to `Module`:

```csharp
protected Task SendEventAsync(string eventName, CancellationToken cancellationToken = default);
protected Task SendEventAsync<T>(string eventName, T payload, CancellationToken cancellationToken = default);
```

- [ ] **Step 6: Run focused event tests**

Run:

```sh
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedEventModuleTests"
```

Expected: pass.

- [ ] **Step 7: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleEventEmitter.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Module.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs
git diff --cached --check
git commit -m "Emit module events through native module objects"
```

## Task 5: Observing Hooks

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStartObservingAttribute.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStopObservingAttribute.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs`

- [ ] **Step 1: Write failing observing source tests**

Add generator input:

```csharp
[ExpoModule("Device")]
[Events("onChange")]
public sealed partial class DeviceModule
{
  [OnStartObserving]
  public void Start(string eventName) {}

  [OnStopObserving("onChange")]
  public void Stop() {}
}
```

Assert generated code defines `startObserving` and `stopObserving` host functions on the module object.

- [ ] **Step 2: Write failing Hermes observing tests**

Assert adding the first listener invokes start and removing the last listener invokes stop:

```js
const events = globalThis._expoDotnet.modules.GeneratedEvents;
const sub = events.addListener('onChange', () => {});
const started = events.ReadStarted();
sub.remove();
const stopped = events.ReadStopped();
started + ':' + stopped;
```

Expected result: `"onChange:onChange"`.

- [ ] **Step 3: Implement attributes and generator model**

Support method shapes:

```csharp
[OnStartObserving]
public void Start(string eventName) {}

[OnStartObserving("onChange")]
public void StartSpecific() {}
```

Reject static, generic, unsupported parameter counts, and hooks on modules without `[Events]`.

- [ ] **Step 4: Emit observing host functions**

Generated functions should be own properties named `startObserving` and `stopObserving`. Each accepts `eventName`, filters by specific event when configured, and invokes matching authored hooks.

- [ ] **Step 5: Run focused generator and Hermes tests**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedEventModuleTests"
```

Expected: pass.

- [ ] **Step 6: Commit**

```sh
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStartObservingAttribute.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/OnStopObservingAttribute.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs \
  packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedEventModuleTests.cs
git diff --cached --check
git commit -m "Add module event observing hooks"
```

## Task 6: Living Specs And Final Verification

**Files:**
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Move or remove: `docs/changes/2026-07-05-module-events/`

- [ ] **Step 1: Merge accepted ABI behavior into `runtime-and-abi.md`**

Add requirements for class/subclass/object-with-prototype ABI and opaque ownership boundaries.

- [ ] **Step 2: Merge accepted module behavior into `modules-core-boundary.md`**

Add requirements for `[Events]`, native-module-backed generated objects, event emission through `DotnetRuntimeContext.Events`, listener semantics, and observing hooks.

- [ ] **Step 3: Archive or remove transient change artifacts**

Follow the current repo convention for completed changes. If completed specs are archived, move `docs/changes/2026-07-05-module-events/` to `docs/archive/changes/2026-07-05-module-events/`; if the repo has shifted to removal, remove the transient directory after living specs contain the accepted behavior.

- [ ] **Step 4: Run focused checks**

Run:

```sh
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Expo.JSI.Tests.csproj --filter JavaScriptClassTests
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter ExpoModulesGeneratorTests
scripts/test-managed.sh --filter "FullyQualifiedName~GeneratedEventModuleTests|FullyQualifiedName~ModuleRegistryTests"
```

Expected: all pass.

- [ ] **Step 5: Run final verification**

Run:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed/packages
rg "(Users/[^/]+|home/[^/]+|~[^[:space:]]|machine-specific|private hostname)" docs packages apps
```

Expected: tests and format pass; `git diff --check` has no output; hot-path reflection scan has no new generated/module event matches; privacy scan has no newly introduced local path or username matches.

- [ ] **Step 6: Commit closeout**

```sh
git add docs/specs/runtime-and-abi.md docs/specs/modules-core-boundary.md docs/archive/changes/2026-07-05-module-events
git diff --cached --check
git commit -m "Update specs for module events"
```

If the transient change directory is removed instead of archived, stage the removal path instead of `docs/archive/changes/2026-07-05-module-events`.
