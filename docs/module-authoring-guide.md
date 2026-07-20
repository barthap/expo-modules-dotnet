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

  [JS("add")]
  public double Add(double a, double b) => a + b;

  [JS("describeUser")]
  public ExampleUserSummary DescribeUser(ExampleUser user) =>
      new(user.Name, user.Age, $"{user.Name} is {user.Age}");
}

public readonly record struct ExampleUser(string Name, int Age);
public readonly record struct ExampleUserSummary(string Name, int Age, string Summary);
```

The Roslyn generator (`Expo.ModulesCore.Generator`) reads these attributes at
compile time and emits direct-call dispatch glue; there is no runtime
reflection on the hot path. The module class must be `partial` so the
generator can add its registration members.

Supported parameter and return types, decoded/encoded through generated
compile-time codecs (`Expo.ModulesCore.Codecs`):

- CLR numeric primitives (`int`, `double`, `float`, unsigned integers, and
  friends), including their nullable forms.
- `string`, `bool`.
- String-backed convertible types: `Guid`, `Uri`, `DateTimeOffset`,
  `TimeSpan` — encoded/decoded as JavaScript strings; invalid input throws a
  managed exception that surfaces to JavaScript as a catchable `Error`.
- Positional `record` / `record class` / `record struct` types (including
  simple nested records), mapped to/from plain JavaScript objects by field
  name, as `ExampleUser` and `ExampleUserSummary` show above.
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

Unsupported parameter/return/constructor/method shapes and duplicate exported
names are reported as generator build diagnostics, not runtime failures.

## 4. Async methods

A `[JS]` method returning `Task` or `Task<T>` is generated as a
promise-returning JavaScript function instead of a direct-call one:

```csharp
[JS("getMessageAsync")]
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

Declare the events a module can emit with `[Events("name1", "name2", ...)]` on
the module class. The list must be non-empty and non-duplicated. Emit an event
with `SendEventAsync` (inherited from the `Module` base class) or directly
through `RuntimeContext.Events`:

```csharp
[ExpoModule("ExampleModule")]
[Events("onStatus")]
public sealed partial class ExampleMathModule : Module
{
  [JS("emitStatusAsync")]
  public Task EmitStatusAsync(string label) =>
      SendEventAsync<StringCodec, string>("onStatus", $"C# event: {label}");
}
```

Emitting an event name outside the declared list fails loudly; an async `[JS]`
caller observes this as a rejected Promise.

To react when JavaScript adds or removes listeners, declare hook methods with
`[OnStartObserving]` / `[OnStopObserving]` (optionally naming a specific event,
for example `[OnStopObserving("onChange")]`):

```csharp
[OnStartObserving]
public void Start(string eventName) { /* first listener for eventName added */ }

[OnStopObserving("onChange")]
public void Stop() { /* last listener for onChange removed */ }
```

Modules that declare `[Events]` are registered as `_expoDotnet.NativeModule`
instances so the inherited `EventEmitter`/`addListener` JavaScript methods work
through the prototype chain; you don't need (and can't declare) `[JS]`
functions named `startObserving` or `stopObserving` — those names are reserved
for the generated observing hooks.

On the JS facade side, subscribe with `addListener`:

```ts
// packages/example-module/src/index.ts
export function addStatusListener(listener: (payload: string) => void) {
  return nativeModule.addListener('onStatus', listener);
}
```

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
[JS("transformWithCallback")]
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

## 8. JS facade

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
  getMessageAsync(): Promise<string>;
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
`[ExpoModule("...")]`. Record field names cross the boundary as declared in
C# (see `ExampleUser`/`ExampleUserSummary` above, whose JS-facing shape uses
`Age`/`Name`/`Summary` to match the C# record's property names); the facade is
a good place to translate those into idiomatic camelCase JS types.

`DotnetModule` and `DotnetEventEmitter` are type bases, not usable JavaScript
module constructors. Obtain module objects through `requireDotnetModule`, and
do not use `instanceof` with either base because native registry objects do not
inherit their JavaScript prototypes. The event map is explicit until generated
event declarations provide a different authoring input. Release a listener
with the returned subscription's `remove()` method.

## 9. Platform matrix

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

## 10. Verification

Run the Hermes-backed managed test suite from the repo root:

```sh
scripts/test-managed.sh
```

`example-module` and its Hermes-backed dispatch/conversion tests
(`Expo.ModulesCore.Tests`) are good references for testing a new module's
behavior without a full mobile app build.

## 11. Troubleshooting

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
