# Module Authoring Guide

This guide walks through writing a .NET-backed Expo module in this repo. It
assumes you know C# and the basic Expo Modules API shape (a native module
exposed to JavaScript through a TypeScript facade), but have never seen this
repo's C# module layer before.

The reference implementation used throughout is `packages/example-module`.
Open it alongside this guide; every numbered section below points at the
concrete file that demonstrates it.

## 1. Project setup

> **Note**: This section describes the current repo-local workflow — module
> packages living inside this monorepo. Authoring modules as standalone
> libraries in separate repos, or as app-local modules, is a planned future
> workflow and is not supported yet.

A dotnet Expo module package lives at `packages/<name>/dotnet/<AssemblyName>/`
next to its JavaScript facade in `packages/<name>/src/`. The module project is
a plain SDK-style csproj that references `Expo.ModulesCore` (which pulls in
`Expo.JSI`) and wires up the Roslyn source generator:

```xml
<!-- packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.JSI/Expo.JSI.csproj" />
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
    <ProjectReference
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
      Condition="'$(PublishAot)' != 'true'"
      OutputItemType="Analyzer"
      ReferenceOutputAssembly="false" />
    <Analyzer
      Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/bin/Debug/netstandard2.0/Expo.ModulesCore.Generator.dll"
      Condition="'$(PublishAot)' == 'true'" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

The generator reference is conditioned on `PublishAot`: normal builds pull in
the generator as a live analyzer project, while NativeAOT publishes use the
generator's prebuilt DLL so the analyzer itself does not need to be
AOT-compiled. See `docs/specs/dotnet-autolinking.md` for why the aggregator
build (not this csproj) is the actual NativeAOT publish unit.

## 2. Autolinking metadata

`expo-module.config.json` at the package root declares the dotnet platform and
points at the csproj(s):

```json
// packages/example-module/expo-module.config.json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      { "path": "dotnet/ExampleModule/ExampleModule.csproj", "assemblyName": "ExampleModule" }
    ]
  }
}
```

The `expo-modules-dotnet-autolinking` CLI's `resolve` command walks app
dependencies for packages that declare `"dotnet"` in `platforms`, and `generate`
turns the resolved manifest into a single app-level `ExpoDotnetHost` aggregator
project referencing every resolved module. A package without `"dotnet"` in
`platforms` is skipped, and a `dotnet.projects[].path` that does not exist on
disk fails `resolve`/`generate` with an error naming the package and path. See
`packages/expo-modules-dotnet-autolinking/README.md` and
`docs/specs/dotnet-autolinking.md` for the full contract.

## 3. Module definition

A module is a class attributed with `[ExpoModule("Name")]`. Methods exposed to
JavaScript are attributed with `[JS]` (optionally `[JS("jsName")]` to rename):

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
[ExpoModule("ExampleModule")]
public sealed partial class ExampleMathModule : Module
{
  public ExampleMathModule(DotnetRuntimeContext context) : base(context) { }

  [JS]
  public double Add(double a, double b) => a + b;

  [JS]
  public ExampleUserSummary DescribeUser(ExampleUser user) =>
      new(user.Name, user.Age, $"{user.Name} is {user.Age}");

  [JS]
  public bool Ready => true;
}

public readonly record struct ExampleUser(string Name, int Age);
public readonly record struct ExampleUserSummary(string Name, int Age, string Summary);
```

The Roslyn generator (`Expo.ModulesCore.Generator`) reads these attributes at
compile time and emits direct-call dispatch glue; there is no runtime
reflection on the hot path. The module class must be `partial` so the
generator can add its registration members. With parameterless `[JS]`, the
JavaScript name lowercases the first C# character: `Add` becomes `add`,
`DescribeUser` becomes `describeUser`, and `Ready` becomes `ready`. Use
`[JS("ExactName")]` only when JavaScript should receive that exact name.

Supported parameter and return types, decoded/encoded through generated
compile-time codecs (`Expo.ModulesCore.Codecs`):

- CLR numeric primitives (`int`, `double`, `float`, unsigned integers, and
  friends), including their nullable forms.
- `string`, `bool`.
- String-backed convertible types: `Guid`, `Uri`, `DateTimeOffset`,
  `TimeSpan` — encoded/decoded as JavaScript strings; invalid input throws a
  managed exception that surfaces to JavaScript as a catchable `Error`.
- Positional `record` / `record class` / `record struct` types (including
  simple nested records), mapped to/from plain JavaScript objects with
  lower-camel field names. For example, `ExampleUser.Name` and
  `ExampleUser.Age` map to `name` and `age`. Decode reads only those
  lower-camel names; there is no PascalCase compatibility fallback. A missing
  field follows its existing codec's `undefined` behavior.
- `Dictionary<string, T>` / `IReadOnlyDictionary<string, T>` where `T` has a
  generated codec, mapped to/from a plain JavaScript object.
- C# `enum` values — encoded as JavaScript strings by default; annotate with
  `[JSEnum(EnumRepresentation.Number)]` for integer-backed representation.
- `JavaScriptValue` directly, for cases the typed codecs don't cover (see
  `docs/specs/managed-jsi-wrappers.md` and
  `docs/specs/ownership-and-scoped-refs.md` for the ownership rules — argument
  wrappers are owned by generated glue for the call's duration; returned
  wrappers transfer ownership to generated glue, so keep a retained copy if you
  need to hold onto or dispose your own reference afterward).
- `JavaScriptCallback<TResult>` / `JavaScriptCallback<TArgs, TResult>` for
  JS function arguments (see Callbacks below).

Unsupported parameter, return, constructor, method, and property shapes and
duplicate exported names are reported as generator build diagnostics, not
runtime failures.

### Properties

Annotate an instance property with `[JS]` to expose an own JavaScript accessor:

```csharp
[JS]
public bool Ready { get; set; }

[JS]
public bool IsReadOnly => true;

[JS("isReady")]
public bool ReadyWithExplicitName => Ready;
```

JavaScript reads and writes these as properties, not functions:

```ts
nativeModule.ready = true;
console.log(nativeModule.ready);
console.log(nativeModule.isReadOnly);
```

The property must be an instance, non-indexed property with a public or
internal getter and a supported codec. `init` accessors are not supported. A
public or internal ordinary setter makes it writable; no setter or an
inaccessible setter makes it read-only. In strict-mode JavaScript, assigning a
getter-only property throws `TypeError`. Getter exceptions and setter codec
failures surface as catchable JavaScript errors.

`JavaScriptValue` properties use the same explicit ownership rule as methods.
The setter receives an invocation-owned wrapper: do not dispose or store that
wrapper. Call `Retain()` if the module needs a copy after the setter returns,
then dispose the retained copy when the module no longer needs it. A getter
transfers its returned wrapper to generated glue, so return `stored.Retain()`
when the module keeps ownership of `stored`. `JavaScriptObject` may become an
optional advanced module convertible in a separate future change, but it does
not have a generated module codec today.

## 4. Async methods

A `[JS]` method returning `Task` or `Task<T>` is generated as a
promise-returning JavaScript function instead of a direct-call one:

```csharp
[JS]
public async Task<string> GetMessageAsync()
{
  await Task.Yield();
  return "Hello from async C#";
}
```

Arguments are decoded and captured before the authored method is awaited.
Argument validation/decoding failures, an authored method throwing
synchronously, and a faulted or canceled task all reject the returned
JavaScript Promise instead of throwing synchronously.

Threading: a synchronous `[JS]` function runs as a direct JSI host function
inside the current JavaScript call — it does not hop threads. Async work
scheduled through `DotnetRuntimeContext` (for example inside a `Task`-returning
method, or when emitting an event) is routed back onto the owning JavaScript
runtime by the runtime scheduler; see `docs/specs/runtime-scheduling.md` for
how each platform host (React Native, headless Hermes testhost) executes that
scheduled work, and `docs/specs/promises.md` for how the underlying promise
capability is settled.

## 5. Events

Use a typed `[Event]` member for new events. It declares both the JavaScript
event name and its one payload type, then the generator supplies one cached,
awaitable delegate per module instance. Declare a payload-less event as
`Func<Task>` and a one-payload event as `Func<T, Task>`:

```csharp
[ExpoModule("ExampleModule")]
public sealed partial class ExampleMathModule : Module
{
  [Event]
  public partial Func<string, Task> OnStatus { get; }

  [JS]
  public Task EmitStatusAsync(string label) =>
      OnStatus($"C# event: {label}");
}
```

`[Event]` lowercases only the first character of the C# property name, so
`OnStatus` emits `onStatus`; it does not strip the `On` prefix. Use
`[Event("StatusChanged")]` when JavaScript must receive an explicit name
verbatim. The delegate returns the real dispatch task. Await it when the
calling code needs to observe target lookup, payload encoding, scheduling, or
teardown failures; a `[JS]` method returning that task exposes the result as a
JavaScript Promise.

Generated registration initializes typed event members before `[OnCreate]`
runs. Reading one from an authored constructor fails with a clear
`InvalidOperationException`, because registration has not initialized it yet.
`[OnCreate]` can read and invoke the member, although dispatch there can still
fail if JavaScript has not attached the event target yet.

### Event payload ownership

For ordinary payloads, keep mutable payload state stable until the event task
completes. Direct `ArrayBuffer` and `JavaScriptValue` payloads have different
lifetime rules:

- An `ArrayBuffer` original may be disposed after event invocation returns, but
  it must not race that invocation. The dispatcher retains its own lease before
  returning the task.
- A `JavaScriptValue` original stays alive until the event task completes. Its
  invocation copy can only be retained while running on the owning JavaScript
  runtime.

Nested owned wrappers, including `JavaScriptValue` and `ArrayBuffer`, and
callback payloads are rejected for typed events. `JavaScriptObject` is not
currently a generated module codec, but remains a possible future advanced
convertible.

To react when JavaScript adds or removes listeners, declare hook methods with
`[OnStartObserving]` / `[OnStopObserving]` (optionally naming a specific event,
for example `[OnStopObserving("onChange")]`):

```csharp
[OnStartObserving]
public void Start(string eventName) { /* first listener for eventName added */ }

[OnStopObserving("onChange")]
public void Stop() { /* last listener for onChange removed */ }
```

Typed events are registered as `_expoDotnet.NativeModule` instances, so the
inherited `EventEmitter`/`addListener` JavaScript methods work through the
prototype chain. You don't need (and can't declare) `[JS]` members named
`startObserving` or `stopObserving` — those names are reserved for generated
observing hooks.

On the JS facade side, subscribe with `addListener`:

```ts
// packages/example-module/src/index.ts
export function addStatusListener(listener: (payload: string) => void) {
  return nativeModule.addListener('onStatus', listener);
}
```

The JavaScript listener facade and its event map do not change when moving to a
typed C# event member.

### Legacy `[Events]` and `SendEventAsync`

`[Events("name1", "name2", ...)]` and the inherited `SendEventAsync` methods
remain supported for migration and interop. The event list must be non-empty
and non-duplicated:

```csharp
[ExpoModule("ExampleModule")]
[Events("onStatus")]
public sealed partial class ExampleMathModule : Module
{
  [JS]
  public Task EmitStatusAsync(string label) =>
      SendEventAsync<StringCodec, string>("onStatus", $"C# event: {label}");
}
```

Emitting a name outside the declared list fails loudly; an async `[JS]` caller
observes that failure as a rejected Promise. Prefer typed `[Event]` members for
new events so the name and payload type are checked at compile time.

## 6. Lifecycle

`[OnCreate]` and `[OnDestroy]` mark parameterless instance methods called once
per module instance:

```csharp
[OnCreate]
public void OnCreate() { Console.WriteLine("ExampleModule created"); }

[OnDestroy]
public void OnDestroy() { Console.WriteLine("ExampleModule destroyed"); }
```

`OnCreate` runs once, right after the module instance is stored in the owning
`DotnetRuntimeContext`'s module registry. `OnDestroy` runs once when that
runtime context is disposed, before `IDisposable.Dispose`; all modules'
destroy/dispose callbacks run even if one throws, with failures aggregated
into a single `AggregateException` after cleanup finishes. Neither hook is
exposed as a JavaScript-visible module property.

A module instance is scoped to one `DotnetRuntimeContext` (one JavaScript
runtime); a second runtime gets its own instance.

## 7. Callbacks

`JavaScriptCallback<TResult>` (zero arguments) and
`JavaScriptCallback<TArgs, TResult>` (one to eight arguments via `ValueTuple`)
let a `[JS]` method accept a JavaScript function and call back into it:

```csharp
[JS]
public string TransformWithCallback(
    string value,
    JavaScriptCallback<ValueTuple<string>, string> callback)
{
  return callback.Invoke(ValueTuple.Create($"C# sent {value}"));
}
```

The callback is retained and owned by the current `DotnetRuntimeContext`.
Calling `Invoke` while already executing on the owning JavaScript runtime runs
synchronously; `InvokeAsync` schedules the call through the runtime for later,
event-style use. Invoking a callback after its runtime context has torn down
fails loudly instead of touching released native state — don't cache a
callback past the lifetime of the module/runtime that received it.

## 8. Shared objects

A shared object is an expensive native resource (file handle, image, crypto
key) held by JavaScript as a class instance while C# keeps the original
managed object. Both sides always see the same instance: converting the same
managed object twice yields strictly equal JavaScript objects, and passing the
JavaScript object back into C# returns the original managed instance.

### Declaring a class

```csharp
[ExpoSharedObject]
public sealed partial class ExampleCounter : SharedObject
{
  [JS]
  public ExampleCounter(double start)
  {
    Count = start;
  }

  [JS]
  public double Count { get; private set; }

  [JS("increment")]
  public double Increment(double by)
  {
    Count += by;
    return Count;
  }

  protected override void OnRelease()
  {
    // Idempotent resource cleanup. Called exactly once.
  }
}
```

Declaration constraints (violations produce `EXPOJSI021`–`EXPOJSI028`
diagnostics):

- The class must be `sealed`, `partial`, top-level, non-generic, and derive
  from `SharedObject` (directly or through `SharedRef<T>`).
- `[ExpoSharedObject]` takes an optional explicit JavaScript class name;
  otherwise the C# type name is used verbatim.
- At most one accessible `[JS]` constructor, with codec-supported parameters.
  A class with a `[JS]` constructor is constructible from JavaScript; without
  one it is native-created-only and never appears as a module property.
- `[JS]` methods and properties follow the same shape, naming (lower-camel
  defaults), async, and codec rules as module members. `release`,
  `constructor`, `__proto__`, and the event-emitter method names are reserved.

### Per-instance events

Declare shared-object events with the same awaitable typed shape as module
events. The generated delegate becomes available when the object first pairs
with its JavaScript instance:

```csharp
[Event]
public partial Func<double, Task> OnChange { get; }

[JS]
public async Task<double> IncrementAndEmitAsync(double by)
{
  Count += by;
  await OnChange(Count);
  return Count;
}
```

Listeners belong to one JavaScript shared-object instance. A listener on one
counter never receives another counter's event. The returned subscription has
an idempotent `remove()` method; call it when the listener is no longer needed.
Listeners, including a listener that captures its own shared object, stay in
the JavaScript heap and do not prevent the whole unreachable cycle from being
collected.

### Module ownership

Exactly one module owns each shared class through the retained
`[ExpoModule(Classes = ...)]` shape:

```csharp
[ExpoModule("ExampleModule", Classes = new[] { typeof(ExampleCounter) })]
public sealed partial class ExampleMathModule : Module
{
  [JS]
  public ExampleCounter MakeCounter(double start) => new(start);

  [JS]
  public ExampleCounter EchoCounter(ExampleCounter counter) => counter;
}
```

The owning module exposes each constructible class as a class-name property
(`module.ExampleCounter`). Exposed class names must not collide with the
module's functions, properties, observing hooks, or event-runtime members.
Shared-object parameters, returns, and properties must use the exact sealed
authored type directly at the boundary — the `SharedObject`/`SharedRef<T>`
bases and nested composition (records, lists, dictionaries, callbacks,
nullable annotations) are rejected.

### Lifetime and release

- `OnRelease()` runs exactly once per instance — on explicit JavaScript
  `release()`, deterministic garbage collection, or context teardown,
  whichever comes first. It runs synchronously and must be thread-agnostic:
  keep it short, idempotent, and free of JavaScript/runtime calls.
- JavaScript `release()` is idempotent; repeated calls are no-ops.
- Using a released instance from JavaScript throws a catchable error before
  any authored code runs.

### `SharedRef<T>`

`SharedRef<T>` is a non-owning shared wrapper: releasing the shared object
does NOT dispose or clean up `Ref`. Derive a sealed class and add explicit
cleanup in `OnRelease` only if your subclass owns the resource.

### TypeScript facade and cleanup recipe

Extend `DotnetSharedObject` for the facade class and type the module's class
property:

```ts
import { DotnetSharedObject } from 'expo-modules-dotnet';

type ExampleCounterEvents = {
  onChange(value: number): void;
};

export declare class ExampleCounter extends DotnetSharedObject<ExampleCounterEvents> {
  constructor(start: number);
  readonly count: number;
  increment(by: number): number;
  incrementAndEmitAsync(by: number): Promise<number>;
}
```

Release deterministically with `try`/`finally`:

```ts
const counter = new module.ExampleCounter(40);
const subscription = counter.addListener('onChange', value => {
  console.log(value);
});
try {
  await counter.incrementAndEmitAsync(2);
} finally {
  subscription.remove();
  counter.release();
}
```

`Symbol.dispose` / `using` support is deferred pending a TypeScript/runtime
compatibility review; use explicit `release()`.

## 9. JS facade

The TypeScript side in `packages/<name>/src/` declares the native module shape
and wraps it in an ergonomic public API:

```ts
// packages/example-module/src/index.ts
import {
  DotnetModule,
  requireDotnetModule,
  type EventSubscription,
} from 'expo-modules-dotnet';

type ExampleModuleEvents = {
  onStatus(payload: string): void;
};

declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  add(a: number, b: number): number;
  describeUser(user: ExampleUser): ExampleUserSummary;
  getMessageAsync(): Promise<string>;
  readonly ready: boolean;
}

export type ExampleModule = ExampleModuleType;
export type { EventSubscription } from 'expo-modules-dotnet';

const nativeModule = requireDotnetModule<ExampleModuleType>('ExampleModule');

export function add(a: number, b: number): number {
  return nativeModule.add(a, b);
}

export function addStatusListener(listener: (payload: string) => void): EventSubscription {
  return nativeModule.addListener('onStatus', listener);
}
```

`requireDotnetModule` is the dotnet-backed counterpart of Expo's
`requireNativeModule` — it looks up the module by the name passed to
`[ExpoModule("...")]`. Record fields already cross the boundary with
lower-camel JavaScript names, so the facade should declare `name`, `age`, and
`summary` directly. Do not add a PascalCase translation object or rely on a
PascalCase decode fallback.

`DotnetModule` and `DotnetEventEmitter` are type bases, not usable JavaScript
module constructors. Obtain module objects through `requireDotnetModule`, and
do not use `instanceof` with either base because native registry objects do not
inherit their JavaScript prototypes. The event map is explicit until generated
event declarations provide a different authoring input. Release a listener
with the returned subscription's `remove()` method.

## 10. Platform matrix

| Platform | HostFXR (dev loader) | NativeAOT | Mono AOT |
|---|---|---|---|
| Windows | yes | yes | no |
| macOS | yes | yes | no |
| Android | no | yes | planned |
| iOS | no | yes | planned |

HostFXR is a development-time loader; every platform's production build path
currently uses NativeAOT, with Mono AOT planned as a future option for the
mobile platforms. NativeAOT constraints that affect module authors:

- No runtime reflection in generated bindings or module code on the hot
  path — the generator and codecs are all compile-time.
- The `Expo.ModulesCore.Generator` project reference itself is excluded from
  NativeAOT builds (see the csproj snippet in section 1); only its generator
  output participates in the AOT-published binary.

See the root `README.md`'s "Platform support" section and
`docs/specs/runtime-and-abi.md` for how HostFXR and NativeAOT loaders differ.

## 11. Verification

Each repo-local authored package owns a non-packable `.Tests` project. Follow
the `ExampleModule.Tests.csproj` reference shape:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
  <PackageReference Include="xunit.v3" Version="3.2.0" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="../ExampleModule/ExampleModule.csproj" />
  <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/Expo.ModulesCore.Testing.csproj" />
</ItemGroup>
```

Disable xUnit parallel execution for an authored module test project in v1:

```csharp
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Keep direct C# unit tests in the same project for behavior that does not need
JavaScript or Hermes. Use `Expo.ModulesCore.Testing` only when testing the
generated module surface. Register the generated provider explicitly; TestCore
does not scan assemblies for providers:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
```

`ExpoModuleTestHost` owns the Hermes runtime and module context. Keep it in a
`using` scope. Read and dispose `JavaScriptValue` instances inside
`host.Runtime.Execute(...)`, including values fulfilled by
`EvaluatePromiseAsync`, so all JSI access and release occurs on the runtime
executor.

For synchronous evaluation:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
var sum = host.Runtime.Execute(_ =>
{
  using var value = host.Evaluate(
      "globalThis._expoDotnet.modules.ExampleModule.add(20, 22)",
      "example-module-test.js"
  );
  return checked((int)value.AsDouble());
});

Assert.Equal(42, sum);
```

For a Promise-returning method, await settlement first, then consume and
dispose the owned fulfillment value on the executor:

```csharp
using var host = ExpoModuleTestHost.Create(
    ExpoModulesProvider_ExampleModule.Register
);
var fulfilled = await host.EvaluatePromiseAsync(
    "globalThis._expoDotnet.modules.ExampleModule.getMessageAsync()",
    TestContext.Current.CancellationToken
);
var message = host.Runtime.Execute(_ =>
{
  using (fulfilled)
  {
    return fulfilled.AsString();
  }
});

Assert.Equal("Hello from async C#", message);
```

Run the canonical runners for the full suite or a selected mixed project:

```sh
scripts/test-managed.sh
scripts/test-managed.sh --project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
```

```powershell
scripts/test-managed.ps1
scripts/test-managed.ps1 -Project packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj
```

Direct `dotnet test` is valid for a project containing only pure C# tests. An
unfiltered project that mixes pure tests with Hermes-backed tests requires the
canonical runner so it can build and provide the native testhost.

## 12. Troubleshooting

- **A new module doesn't show up in the app**: confirm
  `expo-module.config.json` lists `"dotnet"` in `platforms` and the
  `dotnet.projects[].path` is correct; run the CLI's `resolve` command to see
  the manifest the aggregator will be generated from.
- **Aggregator output looks stale**: `generate` writes the `ExpoDotnetHost`
  aggregator to `<appRoot>/.expo/dotnet/` by default (this is the current, not
  a permanent, location — see the output-directory migration note in
  `docs/specs/dotnet-autolinking.md`); it only rewrites files whose content
  changed.
- **Duplicate assembly name error**: two resolved dotnet projects share the
  same effective `assemblyName`; make each module's `assemblyName` unique.
- **iOS staging**: `stage --platform ios` copies
  `<appRoot>/ios/Managed/libExpoDotnetHost.dylib`. **Android staging**:
  `stage --platform android` copies to
  `<appRoot>/android/app/src/main/jniLibs/arm64-v8a/libExpoDotnetHost.so`.
  NativeAOT staging does not stage `nethost` or managed `.dll` files.
- **Android NativeAOT publish fails looking for the NDK**: set
  `ANDROID_NDK_HOME` (or ensure an NDK is discoverable under `ANDROID_HOME`/
  `ANDROID_SDK_ROOT`).
- For the full command reference, see
  `packages/expo-modules-dotnet-autolinking/README.md`.

## Further reading

- `docs/specs/modules-core-boundary.md` — the normative spec behind sections
  3–7 above (attributes, codecs, lifecycle, events, callbacks).
- `docs/specs/dotnet-autolinking.md` — the normative spec behind sections 2
  and 9–11 above.
- `docs/specs/promises.md`, `docs/specs/runtime-scheduling.md` — the async/
  threading model behind section 4.
- `docs/roadmap.md` — what's shipped and what's still planned.
