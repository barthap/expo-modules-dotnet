# Module Authoring Guide

This guide walks through writing a .NET-backed Expo module in this repo. It
assumes you know C# and the basic Expo Modules API shape (a native module
exposed to JavaScript through a TypeScript facade), but have never seen this
repo's C# module layer before.

The reference implementation used throughout is `packages/example-module`.
Open it alongside this guide; every section below points at the
concrete file that demonstrates it.

## Get started

### Requirements

A module project targets .NET `net10.0`, as the `<TargetFramework>` element in the
project file below shows.

### Project layout

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
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <AssemblyName>ExampleModule</AssemblyName>
    <RootNamespace>ExampleModule</RootNamespace>
  </PropertyGroup>

  <PropertyGroup Condition="'$(PublishAot)' == 'true'">
    <NativeLib>Shared</NativeLib>
  </PropertyGroup>

</Project>
```

The generator reference is conditioned on `PublishAot`: normal builds pull in
the generator as a live analyzer project, while NativeAOT publishes use the
generator's prebuilt DLL so the analyzer itself does not need to be
AOT-compiled. See `docs/specs/dotnet-autolinking.md` for why the aggregator
build (not this csproj) is the actual NativeAOT publish unit.

### Autolinking metadata

`expo-module.config.json` at the package root declares the dotnet platform and
points at the csproj(s):

```json
// packages/example-module/expo-module.config.json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      {
        "path": "dotnet/ExampleModule/ExampleModule.csproj",
        "assemblyName": "ExampleModule"
      }
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

### First module

A module is a class attributed with `[ExpoModule("Name")]`. Methods exposed to
JavaScript are attributed with `[JS]` (optionally `[JS("jsName")]` to rename):

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
using Expo.ModulesCore;

namespace ExampleModule;

[ExpoModule("ExampleModule", Classes = new[] { typeof(ExampleCounter) })]
public sealed partial class ExampleMathModule : Module
{
  public ExampleMathModule(DotnetRuntimeContext context)
      : base(context)
  {
  }
// ...
  [JS]
  public double Add(double a, double b)
  {
    return a + b;
  }

  [JS]
  public bool Ready => true;
// ...
  [JS]
  public ExampleUserSummary DescribeUser(ExampleUser user)
  {
    return new ExampleUserSummary(user.Name, user.Age, $"{user.Name} is {user.Age}");
  }
// ...
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

### TypeScript facade

The TypeScript side in `packages/<name>/src/` declares the native module shape
and wraps it in an ergonomic public API:

```ts
// packages/example-module/src/index.ts
import {
  DotnetModule,
  DotnetSharedObject,
  requireDotnetModule,
  type EventSubscription,
} from 'expo-modules-dotnet';

export type { EventSubscription } from 'expo-modules-dotnet';

const nativeModule = requireDotnetModule<ExampleModule>('ExampleModule');

export type ExampleUser = {
  age: number;
  name: string;
};

export type ExampleUserSummary = ExampleUser & {
  summary: string;
};

type ExampleModuleEvents = {
  onStatus(payload: string): void;
};

type ExampleCounterEvents = {
  onChange(value: number): void;
};

/**
 * Shared counter handle: the same instance is visible to C# and JavaScript. Call `release()`
 * when done; using a released counter throws a catchable error.
 */
export declare class ExampleCounter extends DotnetSharedObject<ExampleCounterEvents> {
  constructor(start: number);
  readonly count: number;
  increment(by: number): number;
  incrementAndEmitAsync(by: number): Promise<number>;
}

declare class ExampleModuleType extends DotnetModule<ExampleModuleEvents> {
  ExampleCounter: typeof ExampleCounter;
  add(a: number, b: number): number;
  echoCounter(counter: ExampleCounter): ExampleCounter;
  makeCounter(start: number): ExampleCounter;
  describeUser(user: ExampleUser): ExampleUserSummary;
  emitStatusAsync(label: string): Promise<void>;
  getMessageAsync(): Promise<string>;
  readonly ready: boolean;
  transformWithCallback(value: string, callback: (value: string) => string): string;
}

export type ExampleModule = ExampleModuleType;

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

## Modules

### Declare a module

<!-- backed-by: test GeneratorEmitsDeterministicProviderForAssembly; source ExpoModulesGenerator.CreateModuleModel -->
<!-- upstream: expo/expo#53016 docs/pages/modules/v2/reference/modules.mdx -->
`[ExpoModule("Name")]` on a `partial` class declares a module. `[ExpoModule]`
without an argument strips a trailing `Module` from the class name, so
`MathModule` registers as `Math`. Upstream uses the class name verbatim. EMC-75
changes this.

<!-- backed-by: spec "Requirement: Runtime Context Owns Module Instances" (modules-core-boundary.md); source Module; test GeneratorReportsUnsupportedModuleConstructor -->
The `Module` base class is optional. A module without it declares a
parameterless constructor or a constructor that takes `DotnetRuntimeContext`.
Another constructor shape is build error `EXPOJSI003`. `V2MathModule` has no
base class and no constructor.

```csharp
// apps/hermes-console-app/managed/HermesConsoleApp/V2MathModule.cs
using Expo.ModulesCore;

namespace HermesConsoleApp;

[ExpoModule("V2Math")]
internal sealed partial class V2MathModule
{
  [JS("add")]
  public double Add(double a, double b)
  {
    return a + b;
  }
}
```

### Functions

<!-- backed-by: spec "Requirement: Generated JavaScript Member Names Are Lower Camel" (modules-core-boundary.md); test GeneratorEmitsDefaultAndExplicitFunctionNames -->
`[JS]` exports a method. The default JavaScript name lowercases the first
character. `[JS("name")]` sets the name.

<!-- backed-by: test GeneratorReportsDuplicateJavaScriptFunctionName; test GeneratorRejectsPropertyThatCollidesWithDuplicateMethodName -->
Two `[JS]` members that resolve to one JavaScript name are build errors
(`EXPOJSI005` for functions, `EXPOJSI016` for members). A C# overload
therefore cannot export one JavaScript function with several shapes.

<!-- backed-by: spec "Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults" (modules-core-boundary.md); test GeneratedProviderUsesDefaultForMissingOrUndefinedOptionalNullableArguments; test GeneratorPreservesLeafFreeCodecPolicyModuleOutputContract; source ExpoModulesGenerator.GetDefaultValueExpression -->
Omitting a parameter that has a C# default value, or passing `undefined`, uses
the default value. Defaults work for `bool`, `string`, numeric types and
`null`, for nullable and non-nullable parameters.

<!-- backed-by: source ExpoModulesGenerator.GetRequiredParameterCount; source GeneratedFunction.RequireArgumentCount; test GeneratedAsyncArgumentCountFailureRejectsPromise -->
The generated function checks the argument count. The minimum is the number of
parameters without a default. The maximum is the number of parameters. A call
outside that range throws, and an async function rejects its Promise. A
trailing nullable parameter without a default is required (EMC-63).

<!-- backed-by: spec "Requirement: Unsupported Signatures Are Build Diagnostics" (modules-core-boundary.md) -->
Unsupported parameter, return, constructor, method, and property shapes and
duplicate exported names are reported as generator build diagnostics, not
runtime failures.

### Asynchronous functions

<!-- backed-by: spec "Requirement: Async Function Generation Returns Promises" (modules-core-boundary.md); spec "Requirement: Async Function Failures Reject Promises" (modules-core-boundary.md) -->
A `[JS]` method returning `Task` or `Task<T>` is generated as a
promise-returning JavaScript function instead of a direct-call one:

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
  [JS]
  public async Task<string> GetMessageAsync()
  {
    await Task.Yield();
    return "Hello from async C#";
  }
```

<!-- backed-by: spec "Requirement: Async Function Generation Returns Promises" (modules-core-boundary.md); spec "Requirement: Async Function Failures Reject Promises" (modules-core-boundary.md) -->
Arguments are decoded and captured before the authored method is awaited.
Argument validation/decoding failures, an authored method throwing
synchronously, and a faulted or canceled task all reject the returned
JavaScript Promise instead of throwing synchronously.

### Properties

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md) -->
Annotate an instance property with `[JS]` to expose an own JavaScript accessor:

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md) -->
```csharp
[JS]
public bool Ready { get; set; }

[JS]
public bool IsReadOnly => true;

[JS("isReady")]
public bool ReadyWithExplicitName => Ready;
```

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md) -->
JavaScript reads and writes these as properties, not functions:

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md) -->
```ts
nativeModule.ready = true;
console.log(nativeModule.ready);
console.log(nativeModule.isReadOnly);
```

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md) -->
The property must be an instance, non-indexed property with a public or
internal getter and a supported codec. `init` accessors are not supported. A
public or internal ordinary setter makes it writable; no setter or an
inaccessible setter makes it read-only. In strict-mode JavaScript, assigning a
getter-only property throws `TypeError`. Getter exceptions and setter codec
failures surface as catchable JavaScript errors.

<!-- backed-by: spec "Requirement: Generated Module Value Ownership" (managed-jsi-wrappers.md) -->
`JavaScriptValue` properties use the same explicit ownership rule as methods.
The setter receives an invocation-owned wrapper: do not dispose or store that
wrapper. Call `Retain()` if the module needs a copy after the setter returns,
then dispose the retained copy when the module no longer needs it. Disposing a
retained copy after the runtime is torn down is safe and frees only native
memory, so module cleanup code does not need to check whether the runtime is
still alive. Any other use of the copy after teardown fails with an error. A
getter transfers its returned wrapper to generated glue, so return
`stored.Retain()` when the module keeps ownership of `stored`. A getter that
returns a record, list, or dictionary holding a stored wrapper returns a new
value built from `Retain()` copies. `JavaScriptObject` may become an optional
advanced module convertible in a separate future change, but it does not have a
generated module codec today.

### Constants

<!-- backed-by: spec "Requirement: `[JS]` Instance Properties Are JavaScript Accessors" (modules-core-boundary.md); spec "Requirement: Expo Constants Dotnet Exposes Typed Host Values" (modules-core-boundary.md) -->
<!-- upstream: expo/expo#53016 docs/pages/modules/v2/reference/modules.mdx -->
There is no `[Constant]` attribute. A constant is a getter-only `[JS]` property,
and its getter runs on every JavaScript read. Upstream reads a constant once:
Kotlin `@Constant` and a Swift `lazy var` store the value on the JavaScript
object on the first read. EMC-62 changes this. `ExpoConstantsModule` exposes
its values this way:

```csharp
// packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet/ExpoConstantsModule.cs
  [JS]
  public string Platform => RequireSupportedPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

  [JS]
  public string ExecutionEnvironment => RequireSupportedHostValue("bare");

  [JS]
  public string SessionId => RequireSupportedHostValue(sessionId);

  [JS]
  public string? NativeAppVersion => RequireSupportedHostValue(metadata.NativeAppVersion);

  [JS]
  public string? NativeBuildVersion => RequireSupportedHostValue(metadata.NativeBuildVersion);
```

### Naming conventions

<!-- upstream: expo/expo packages/expo-sqlite/ios/SQLiteModule.swift; expo/expo packages/expo-crypto/ios/CryptoModule.swift; expo/expo#53016 docs/pages/modules/v2/reference/concurrency.mdx -->
<!-- backed-by: spec "Requirement: Generated JavaScript Member Names Are Lower Camel" (modules-core-boundary.md); test GeneratorEmitsDefaultAndExplicitFunctionNames -->
Upstream names an asynchronous function with an Async suffix
(`deleteDatabaseAsync`) and its synchronous twin with a `Sync` suffix
(`deleteDatabaseSync`). Upstream module names carry an `Expo` prefix
(`ExpoCrypto`). In C#, a `Task` method named `GetMessageAsync` already crosses
as `getMessageAsync`, so the .NET and upstream conventions give the same
JavaScript name. New modules follow both conventions. Existing repo modules
keep their names: `ExpoAsset`, `ExponentConstants`, `FileSystem` and
`ExampleModule`.

## Types

### Supported types

Supported parameter and return types, decoded/encoded through generated
compile-time codecs (`Expo.ModulesCore.Codecs`):

- CLR numeric primitives (`int`, `double`, `float`, unsigned integers, and
  friends), including their nullable forms.
- `string`, `bool`.
- String-backed convertible types: `Guid`, `Uri`, `DateTimeOffset`,
  `TimeSpan` — encoded/decoded as JavaScript strings; invalid input throws a
  managed exception that surfaces to JavaScript as a catchable `Error`.
- `Dictionary<string, T>` / `IReadOnlyDictionary<string, T>` where `T` has a
  generated codec, mapped to/from a plain JavaScript object.
- `IReadOnlyList<T>` where `T` has a generated codec, mapped to/from a
  JavaScript array. `List<T>` is not supported. Lists and dictionaries nest in
  each other, in records, in callback types, and in typed event payloads. They
  can hold `JavaScriptValue`, `ArrayBuffer`, and `Uint8Array`.
- Shared objects, declared as described in `## Shared objects`.
- C# `enum` values — encoded as JavaScript strings by default; annotate with
  `[JSEnum(EnumRepresentation.Number)]` for integer-backed representation.
- `JavaScriptValue` directly, for cases the typed codecs don't cover. See
  "Leaf ownership" below and `docs/specs/managed-jsi-wrappers.md`.
- `JavaScriptCallback<TResult>` / `JavaScriptCallback<TArgs, TResult>` for
  JS function arguments (see Closure arguments below). A callback is a parameter type
  only: it cannot be a return value, a collection element, or a record field.
  `JavaScriptCallback<...>?` is accepted as a method or shared-object
  constructor parameter, including with `= null`. JavaScript `null` and
  `undefined` arrive as C# `null`, and a function is retained like a non-null
  callback. Its result and argument types can hold `JavaScriptValue`,
  `ArrayBuffer`, and `Uint8Array` at any depth.

### Leaf ownership

**Leaf ownership.** A `JavaScriptValue`, `ArrayBuffer`, or `Uint8Array` is a
leaf. These rules apply at any depth, also inside records, lists, and
dictionaries:

1. A leaf parameter is valid until the call ends (async: until the returned
   `Task` completes). To keep it, call `Retain()`.
2. A leaf you return is no longer yours.
3. A leaf you pass to a callback or an event stays yours. Keep it alive until
   the call or the returned `Task` completes.
4. A leaf you get back from a callback is yours. Dispose it.

Call `JavaScriptValue.Retain()` on the JavaScript runtime thread. In an async
method, call it before the first `await`. See
`docs/specs/ownership-and-scoped-refs.md`.

### Nullable values

Nullable reference types work for `ArrayBuffer?`, `Uint8Array?`, and for the
codecs that carry no JSI ownership: `string?`, `Uri?`, `byte[]?`, positional
reference records, and the `IReadOnlyList<T>?`, `Dictionary<string, T>?`, and
`IReadOnlyDictionary<string, T>?` containers. Nullable elements and dictionary
values work wherever the nested type is supported, so `IReadOnlyList<string?>?`
composes. `List<T>` stays unsupported in both forms, because its non-nullable
form has no codec either.

`JavaScriptValue?`, `SharedObject?`, and `SharedRef<T>?` are build
diagnostics. `JavaScriptValue` already represents JavaScript `null` and
`undefined`. The others carry shared-object identity, and a null axis
multiplies those states. A nullable concrete `[ExpoSharedObject]` class is
supported; see [Shared objects](#shared-objects).

Decoding treats JavaScript `null` and `undefined` alike as C# `null`, and C#
`null` encodes as JavaScript `null`, never `undefined`. A non-nullable reference
stays strict: JavaScript `null` fails the argument decode before your code runs.
For an optional nullable parameter, omission or explicit `undefined` uses your
C# default, while explicit `null` arrives as `null`. Only an explicit `?`
annotation turns any of this on. In a file with `#nullable disable`, an
unannotated reference keeps its strict codec.

### Closure arguments

`JavaScriptCallback<TResult>` (zero arguments) and
`JavaScriptCallback<TArgs, TResult>` (one to eight arguments via `ValueTuple`)
let a `[JS]` method accept a JavaScript function and call back into it:

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
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

Leaves follow rules 3 and 4 in Leaf ownership above. A leaf you pass as an
argument stays yours. With `InvokeAsync`, keep it alive until the returned task
completes. A leaf in the callback result is yours, so dispose it.

## Shared objects

A shared object is an expensive native resource (file handle, image, crypto
key) held by JavaScript as a class instance while C# keeps the original
managed object. Both sides always see the same instance: converting the same
managed object twice yields strictly equal JavaScript objects, and passing the
JavaScript object back into C# returns the original managed instance.

### Declaring a class

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleCounter.cs
[ExpoSharedObject]
public sealed partial class ExampleCounter : SharedObject
{
  private bool resourcesReleased;

  [JS]
  public ExampleCounter(double start)
  {
    Count = start;
  }

  [JS]
  public double Count { get; private set; }
// ...
  [JS("increment")]
  public double Increment(double by)
  {
    Count += by;
    return Count;
  }
// ...
  protected override void OnRelease()
  {
    if (resourcesReleased)
    {
      return;
    }

    resourcesReleased = true;
    Count = 0;
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
// packages/example-module/dotnet/ExampleModule/ExampleCounter.cs
  [Event]
  public partial Func<double, Task> OnChange { get; }
// ...
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
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
[ExpoModule("ExampleModule", Classes = new[] { typeof(ExampleCounter) })]
public sealed partial class ExampleMathModule : Module
{
// ...
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
authored type directly at the boundary, optionally annotated as nullable
(`ExampleCounter?`). JavaScript `null` and `undefined` arrive as C# `null`, and
C# `null` returns JavaScript `null`. A parameter may instead use an authored
abstract base of several shared classes: a top-level, non-generic
`abstract class` that derives from `SharedObject` and has no
`[ExpoSharedObject]` attribute. Authored code receives the concrete instance
JavaScript passed. A base parameter cannot be nullable, and returns and
properties still use the exact sealed class. The `SharedObject`/`SharedRef<T>`
bases, with or without `?`, and nested composition (records, lists,
dictionaries, callbacks) are rejected. `[JS]` members must be declared on the
sealed class; a `[JS]` member inherited from the abstract base is not exported.

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

The facade class extends `DotnetSharedObject` and types the module's class
property, as the `ExampleCounter` declaration in `## Get started` shows.

Release deterministically with `try`/`finally`:

<!-- backed-by: spec "Requirement: TypeScript Facades Expose Explicit Release" (modules-core-boundary.md) -->
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

## Records

Positional `record`, `record class` and `record struct` types (including simple
nested records) map to and from plain JavaScript objects with lower-camel field
names. For example, `ExampleUser.Name` and `ExampleUser.Age` map to `name` and
`age`. Decode reads only those lower-camel names; there is no PascalCase
compatibility fallback. A missing field follows its existing codec's
`undefined` behavior.

## Unions

`Either<TFirst, TSecond>` is for a value that JavaScript passes as one of
two kinds, such as `string | ArrayBuffer`. Each member is `string`, `bool`,
a numeric type, a binary type (`ArrayBuffer`, `byte[]`, `Memory<byte>`,
`ReadOnlyMemory<byte>`), or `Uint8Array` (an object), and the two members
must have different JavaScript kinds: `Either<double, bool>` works,
`Either<double, int>` does not. Read
`IsFirst`, then `First` or `Second`, or call `Match`. Create one with
`Either<TFirst, TSecond>.FromFirst` or `FromSecond`. A union works in every
position. JavaScript `null`, `undefined`, and any other kind fail with a
JavaScript error: a parameter fails before your code runs, and a callback
result fails inside `Invoke()`. `Either<TFirst, TSecond>?` turns `null` and
`undefined` into C# `null`. An `ArrayBuffer` or `Uint8Array` member is a leaf
and follows the leaf rules in `## Types`.

## Binary data

The binary types are `ArrayBuffer`, plus `byte[]`, `Memory<byte>` and
`ReadOnlyMemory<byte>` as convenience types. These copy in both directions.
`Span<byte>` and `ReadOnlySpan<byte>` borrow one synchronous input parameter,
and a returned span is copied. `ArrayBuffer` and `ArrayBuffer?` work in every
position. `Uint8Array` is a view over a JavaScript buffer with its offset and
length, and nothing is copied: your writes are visible to JavaScript after
the call. `Uint8Array` and `Uint8Array?` work in every position too, and any
`Uint8Array` instance, including a subclass such as a `Buffer` polyfill, is
accepted. `byte[]` and `ArrayBuffer` parameters do not accept a
`Uint8Array`; declare `Uint8Array` when JavaScript sends one. See the
"Module-facing ArrayBuffer And Binary Codecs" requirement in
`docs/specs/modules-core-boundary.md`.

## Events

Use a typed `[Event]` member for new events. It declares both the JavaScript
event name and its one payload type, then the generator supplies one cached,
awaitable delegate per module instance. Declare a payload-less event as
`Func<Task>` and a one-payload event as `Func<T, Task>`:

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
[ExpoModule("ExampleModule", Classes = new[] { typeof(ExampleCounter) })]
public sealed partial class ExampleMathModule : Module
{
// ...
  [JS]
  public Task EmitStatusAsync(string label) => OnStatus($"C# event: {label}");

  [Event]
  public partial Func<string, Task> OnStatus { get; }
// ...
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
- A `Func<ArrayBuffer?, Task>` event delivers C# `null` as one JavaScript `null`
  argument and retains no lease. A non-null buffer follows the `ArrayBuffer`
  rule.
- A direct `Uint8Array` or `Uint8Array?` gets no lease; keep it alive until the
  task completes.

Records, lists, and dictionaries that hold `JavaScriptValue`, `ArrayBuffer`,
`ArrayBuffer?`, or `Uint8Array` are valid payloads, and so is a direct
`Uint8Array` or `Uint8Array?`. These leaves stay yours (rule 3 in Leaf ownership under `## Types`):
keep them alive until the event task completes. Callback payloads
and a nullable `JavaScriptValue` are build diagnostics. `JavaScriptObject` is
not currently a generated module codec, but remains a possible future advanced
convertible.

### Observing hooks

To react when JavaScript adds or removes listeners, declare hook methods with
`[OnStartObserving]` / `[OnStopObserving]` (optionally naming a specific event,
for example `[OnStopObserving("onChange")]`):

<!-- backed-by: spec "Requirement: Event Observing Hooks Use Module Object Functions" (modules-core-boundary.md) -->
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
export function addStatusListener(listener: (payload: string) => void): EventSubscription {
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
// apps/hermes-console-app/managed/HermesConsoleApp/ShowcaseModule.cs
[ExpoModule("Showcase")]
[Events("onStatus")]
internal sealed partial class ShowcaseModule : Module
{
  public ShowcaseModule(DotnetRuntimeContext context)
      : base(context)
  {
  }
// ...
  [JS("emitStatusAsync")]
  public Task EmitStatusAsync(string label)
  {
    return SendEventAsync<StringCodec, string>("onStatus", $"C# event: {label}");
  }
}
```

Emitting a name outside the declared list fails loudly; an async `[JS]` caller
observes that failure as a rejected Promise. Prefer typed `[Event]` members for
new events so the name and payload type are checked at compile time.

## Lifecycle and runtime context

### Lifecycle hooks

`[OnCreate]` and `[OnDestroy]` mark parameterless instance methods called once
per module instance:

```csharp
// packages/example-module/dotnet/ExampleModule/ExampleMathModule.cs
  [OnCreate]
  public void OnCreate()
  {
    Console.WriteLine("ExampleModule created");
  }

  [OnDestroy]
  public void OnDestroy()
  {
    Console.WriteLine("ExampleModule destroyed");
  }
```

`OnCreate` runs once, right after the module instance is stored in the owning
`DotnetRuntimeContext`'s module registry. `OnDestroy` runs once when that
runtime context is disposed, before `IDisposable.Dispose`; all modules'
destroy/dispose callbacks run even if one throws, with failures aggregated
into a single `AggregateException` after cleanup finishes. Neither hook is
exposed as a JavaScript-visible module property.

A module instance is scoped to one `DotnetRuntimeContext` (one JavaScript
runtime); a second runtime gets its own instance.

## Threading

A synchronous `[JS]` function runs as a direct JSI host function
inside the current JavaScript call — it does not hop threads. Async work
scheduled through `DotnetRuntimeContext` (for example inside a `Task`-returning
method, or when emitting an event) is routed back onto the owning JavaScript
runtime by the runtime scheduler; see `docs/specs/runtime-scheduling.md` for
how each platform host (React Native, headless Hermes testhost) executes that
scheduled work, and `docs/specs/promises.md` for how the underlying promise
capability is settled.

## Platform matrix

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
  NativeAOT builds (see the csproj snippet in `## Get started`); only its generator
  output participates in the AOT-published binary.

See the root `README.md`'s "Platform support" section and
`docs/specs/runtime-and-abi.md` for how HostFXR and NativeAOT loaders differ.

## Verification

Each repo-local authored package owns a non-packable `.Tests` project. Follow
the `ExampleModule.Tests.csproj` reference shape:

```xml
<!-- packages/example-module/dotnet/ExampleModule.Tests/ExampleModule.Tests.csproj -->
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
// packages/example-module/dotnet/ExampleModule.Tests/AssemblyInfo.cs
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

Keep direct C# unit tests in the same project for behavior that does not need
JavaScript or Hermes. Use `Expo.ModulesCore.Testing` only when testing the
generated module surface. Register the generated provider explicitly; TestCore
does not scan assemblies for providers:

```csharp
// packages/example-module/dotnet/ExampleModule.Tests/ExampleModuleShowcaseTests.cs
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
// packages/example-module/dotnet/ExampleModule.Tests/ExampleModuleShowcaseTests.cs
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExampleModule.Register
    );
    var result = host.Runtime.Execute(_ =>
    {
      using var value = host.Evaluate(
          "const module = globalThis._expoDotnet.modules.ExampleModule; " +
          "const owned = new module.ExampleCounter(10); " +
          "const fromNative = module.makeCounter(5); " +
          "const echoed = module.echoCounter(fromNative); " +
          "const identity = fromNative === echoed && Object.getPrototypeOf(owned) === module.ExampleCounter.prototype; " +
          "const values = [owned.increment(2), owned.count, fromNative.increment(1), identity]; " +
          "owned.release(); fromNative.release(); " +
          "const afterRelease = (() => { try { owned.increment(1); return 'no error'; } " +
          "catch (error) { return error.message.includes('not an active shared object'); } })(); " +
          "values.concat([afterRelease]).join(':')",
          "example-counter-shared-object.js"
      );
      return value.AsString();
    });
    Assert.Equal("12:12:6:true:true", result);
```

For a Promise-returning method, await settlement first, then consume and
dispose the owned fulfillment value on the executor:

```csharp
// packages/example-module/dotnet/ExampleModule.Tests/ExampleModuleShowcaseTests.cs
    var value = await host.EvaluatePromiseAsync(
        """
        Promise.resolve().then(function () {
// ...
        })
        """,
        TestContext.Current.CancellationToken
    );
    var outcome = host.Runtime.Execute(_ =>
    {
      using (value)
      {
        using var obj = value.AsObject();
        return ReadOutcome(obj);
      }
    });
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

Direct `dotnet test` is valid for a project containing only pure C# tests, or
for a pure-test filter in a project that also contains Hermes-backed tests. An
unfiltered mixed project requires the canonical runner so it can build and
provide the native testhost.

## Troubleshooting

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
  Mobile NativeAOT staging does not stage `nethost` or managed `.dll` files.
- **Desktop staging**: `stage --platform macos` and `stage --platform windows`
  copy into `<appRoot>/macos/Managed` and `<appRoot>/windows/Managed`. A HostFXR
  build stages the managed assemblies, the runtime config, the deps file and
  the platform `nethost` library. A NativeAOT build stages the single native
  library. Both also write an `ExpoDotnetHost.loader` file that records which
  loader the packaged app uses.
- **Android NativeAOT publish fails looking for the NDK**: set
  `ANDROID_NDK_HOME` (or ensure an NDK is discoverable under `ANDROID_HOME`/
  `ANDROID_SDK_ROOT`).
- For the full command reference, see
  `packages/expo-modules-dotnet-autolinking/README.md`.

## Further reading

- `docs/specs/modules-core-boundary.md` — the normative spec behind
  `## Modules` to `## Lifecycle and runtime context` above (attributes, codecs,
  lifecycle, events, callbacks).
- `docs/specs/dotnet-autolinking.md` — the normative spec behind
  `## Get started`, `## Platform matrix` and `## Troubleshooting` above.
- `docs/specs/promises.md`, `docs/specs/runtime-scheduling.md` — the async/
  threading model behind `## Threading`.
- `docs/roadmap.md` — direction and what's still planned.
