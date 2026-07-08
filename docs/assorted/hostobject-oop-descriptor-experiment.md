# HostObject OOP Descriptor Experiment

This is a side-experiment guide for replacing or supplementing delegate-based
HostObject descriptors with an object-oriented descriptor shape.

The experiment goal is not "more abstraction." The only success criterion is
simpler code at real call sites, especially `ModuleRegistry`.

## Current Shape

The low-level public API has a delegate descriptor:

```csharp
public sealed record JavaScriptHostObjectDescriptor(
    JavaScriptHostObjectGetter Get,
    JavaScriptHostObjectSetter? Set = null,
    JavaScriptHostObjectPropertyNamesGetter? GetPropertyNames = null,
    object? State = null);
```

It also has a typed convenience descriptor:

```csharp
public sealed record JavaScriptHostObjectDescriptor<TState>(
    JavaScriptHostObjectGetter<TState> Get,
    JavaScriptHostObjectSetter<TState>? Set = null,
    JavaScriptHostObjectPropertyNamesGetter<TState>? GetPropertyNames = null)
    where TState : class;
```

This is best for small HostObjects and tests:

```csharp
using var hostObject = runtime.CreateHostObject(
    state,
    new JavaScriptHostObjectDescriptor<MyState>(
        Get: (runtime, name, state) =>
            name == "answer" ? runtime.CreateNumber(state.Answer) : runtime.CreateUndefined()
    )
);
```

The downside is that complex behavior can become scattered across lambdas or
private methods on another owner.

## Proposed OOP Shape

Add an interface that describes HostObject behavior as an object:

```csharp
public interface IJavaScriptHostObjectDescriptor
{
  JavaScriptValue Get(JavaScriptRuntime runtime, string propertyName);

  void Set(JavaScriptRuntime runtime, string propertyName, JavaScriptValueRef value);

  IReadOnlyList<string> GetPropertyNames();
}
```

Then add a runtime overload:

```csharp
public JavaScriptObject CreateHostObject(IJavaScriptHostObjectDescriptor descriptor)
{
  ArgumentNullException.ThrowIfNull(descriptor);

  return CreateHostObject(new JavaScriptHostObjectDescriptor(
      Get: (runtime, propertyName, state) =>
          ((IJavaScriptHostObjectDescriptor)state!).Get(runtime, propertyName),
      Set: (runtime, propertyName, value, state) =>
          ((IJavaScriptHostObjectDescriptor)state!).Set(runtime, propertyName, value),
      GetPropertyNames: state =>
          ((IJavaScriptHostObjectDescriptor)state!).GetPropertyNames(),
      State: descriptor
  ));
}
```

This keeps the ABI path unchanged. The interface is only a managed adapter over
the existing delegate descriptor.

## Optional Read-Only Variant

The interface above forces all descriptors to implement `Set`. If that feels
noisy, use an abstract base class instead:

```csharp
public abstract class JavaScriptHostObjectDescriptorBase
{
  public abstract JavaScriptValue Get(JavaScriptRuntime runtime, string propertyName);

  public virtual void Set(
      JavaScriptRuntime runtime,
      string propertyName,
      JavaScriptValueRef value)
  {
    throw new InvalidOperationException(
        $"Cannot set property '{propertyName}' on a read-only host object."
    );
  }

  public virtual IReadOnlyList<string> GetPropertyNames() => [];
}
```

This is more ergonomic for read-only HostObjects, but it is a class hierarchy
instead of a pure interface. Prefer the interface first if the experiment is
about proving composition, not designing an inheritance model.

## Test-First Plan

Start with low-level tests in
`packages/expo-modules-dotnet/managed/packages/Expo.JSI.Tests/Runtime/JavaScriptHostObjectTests.cs`.

Add a private descriptor:

```csharp
private sealed class CounterHostObjectDescriptor : IJavaScriptHostObjectDescriptor
{
  public int GetCount { get; private set; }

  public double Assigned { get; private set; }

  public JavaScriptValue Get(JavaScriptRuntime runtime, string propertyName)
  {
    GetCount++;
    return propertyName == "count"
        ? runtime.CreateNumber(GetCount)
        : runtime.CreateUndefined();
  }

  public void Set(JavaScriptRuntime runtime, string propertyName, JavaScriptValueRef value)
  {
    if (propertyName == "assigned")
    {
      Assigned = value.AsDouble();
    }
  }

  public IReadOnlyList<string> GetPropertyNames() => new[] { "count", "assigned" };
}
```

Then test creation and callback dispatch:

```csharp
[Fact]
public void OopHostObjectDescriptorDispatchesCallbacks()
{
  using var fixture = HermesRuntimeFixture.Create();

  fixture.Runtime.Execute(runtime =>
  {
    var descriptor = new CounterHostObjectDescriptor();
    using var hostObject = runtime.CreateHostObject(descriptor);
    using var global = runtime.Global();
    using var hostValue = hostObject.AsValue();
    global.SetProperty("__hostObject", hostValue);

    using var value = fixture.Evaluate(
        "globalThis.__hostObject.assigned = 42; " +
        "globalThis.__hostObject.count + ':' + Object.keys(globalThis.__hostObject).join(',')",
        "oop-host-object.js"
    );

    Assert.Equal("1:count,assigned", value.AsString());
    Assert.Equal(42.0, descriptor.Assigned);
    return true;
  });
}
```

Run the red test:

```sh
scripts/test-managed.sh --filter JavaScriptHostObjectTests
```

Expected failure: `IJavaScriptHostObjectDescriptor` or the runtime overload does
not exist.

## Implementation Steps

1. Add `IJavaScriptHostObjectDescriptor` to
   `packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptHostObject.cs`.
2. Add `JavaScriptRuntime.CreateHostObject(IJavaScriptHostObjectDescriptor)`.
3. Implement the overload as a thin adapter to the existing
   `JavaScriptHostObjectDescriptor`.
4. Keep ownership identical to the delegate path:
   - `CreateHostObject` returns an owned `JavaScriptObject`.
   - `AsValue()` results are still owned by the caller.
   - Setter inputs are scoped refs and must be retained before storage.
   - The descriptor object is held as callback state until the HostObject is
     released by JS/native teardown.
5. Run the HostObject test filter.
6. Run `scripts/format.sh --check --all`.
7. Run `scripts/test-managed.sh`.

## ModuleRegistry Trial

Only try this after the low-level API is green.

The current lazy registry HostObject setup lives in
`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ModuleRegistry.cs`.

The experiment is to move root lazy-module behavior into a private nested
descriptor:

```csharp
private sealed class LazyDotnetModulesHostObjectDescriptor : IJavaScriptHostObjectDescriptor
{
  private readonly ModuleRegistry registry;

  public LazyDotnetModulesHostObjectDescriptor(ModuleRegistry registry)
  {
    this.registry = registry;
  }

  public JavaScriptValue Get(JavaScriptRuntime runtime, string propertyName) =>
      registry.GetLazyModuleProperty(runtime, propertyName);

  public void Set(JavaScriptRuntime runtime, string propertyName, JavaScriptValueRef value)
  {
    throw new InvalidOperationException(
        $"Cannot set property '{propertyName}' on _expoDotnet.modules."
    );
  }

  public IReadOnlyList<string> GetPropertyNames() => registry.GetLazyModuleNames();
}
```

Then `EnsureLazyDotnetModulesObject` becomes:

```csharp
var hostObject = runtime.CreateHostObject(
    new LazyDotnetModulesHostObjectDescriptor(this)
);
```

To make that compile, remove the unused `state` parameter from
`GetLazyModuleProperty` or add a small forwarding method. Do not move the
registry dictionaries into the descriptor unless that deletes code. If the
descriptor becomes a second `ModuleRegistry`, the refactor failed.

## Simplification Criteria

Keep the OOP refactor only if it improves at least one of these:

- `EnsureLazyDotnetModulesObject` is easier to read.
- HostObject callback behavior is grouped in one cohesive private type.
- There are fewer casts or fewer delegate adapters at the call site.
- Disposal and backing-object ownership become clearer.
- Tests become more intention-revealing.

Reject or revert the refactor if it only:

- moves existing `ModuleRegistry` methods into a nested class,
- adds a descriptor type without deleting complexity,
- hides ownership of `lazyModulesBackingObject`,
- makes teardown behavior harder to audit,
- creates two owners for lazy module caches.

## Ownership Traps

- Do not dispose `lazyModulesBackingObject` from the descriptor. The registry
  currently owns it.
- Do not store `JavaScriptValueRef` from `Set`; retain an owned value first if
  storage is needed.
- Do not let descriptor cleanup touch JSI. HostObject callback context release
  can happen during teardown.
- Do not assume disposing a managed wrapper immediately frees callback state if
  JavaScript still retains the HostObject.
- Avoid `.AsValue().AsObject()` without disposing the intermediate value.

## Expected Outcome

The interface path is likely valuable for complex HostObjects. It is not
automatically better than delegates for simple HostObjects. For `ModuleRegistry`,
the side experiment should be judged by the diff: if the private descriptor
groups behavior without duplicating registry ownership, keep it. Otherwise,
leave the delegate descriptor in place.
