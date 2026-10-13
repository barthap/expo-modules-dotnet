# Expo Modules C#

Write Expo native modules in C# and call them from React Native JavaScript.
Modules use the [Expo Modules 2.0 syntax](https://expo.dev/blog/an-early-look-at-expo-modules-2-0):
you write an ordinary class and mark the members you want to expose to
JavaScript. Modules run on Windows, macOS, Android and iOS. They reach
JavaScript through JSI, so calls do not go through a serialization bridge.

This repository replaces the earlier
[`expo-modules-windows`](https://github.com/barthap/expo-modules-windows)
prototype, which supported Windows only.

## What it looks like

A module is a C# class, in the same annotated style as Expo Modules 2.0. The
`[JS]` attribute exposes a member to JavaScript:

```csharp
[ExpoModule("ExampleModule")]
public sealed partial class ExampleMathModule : Module
{
  public ExampleMathModule(DotnetRuntimeContext context) : base(context) { }

  [JS]
  public double Add(double a, double b) => a + b;
}
```

A small TypeScript facade loads it from JavaScript:

```ts
import { requireDotnetModule } from 'expo-modules-dotnet';

const ExampleModule = requireDotnetModule<{ add(a: number, b: number): number }>(
  'ExampleModule'
);

ExampleModule.add(20, 22); // 42
```

A Roslyn source generator reads the attributes at build time and emits the
dispatch code. There is no runtime reflection on the call path, so modules
compile with NativeAOT. Besides numbers and strings, modules can use records,
dictionaries, enums, callbacks, promises, events, `ArrayBuffer` and shared
objects. See the [module authoring guide](docs/module-authoring-guide.md) for the
full list and the ownership rules.

## Supported features

- Methods and properties (read-only and writable)
- Async methods that return JavaScript promises
- Events, on modules and on shared objects
- Module lifecycle hooks
- JavaScript callbacks as method arguments
- Shared objects (`SharedObject`, `SharedRef<T>`) with JavaScript classes
- `ArrayBuffer`
- Types: numbers, `string`, `bool`, enums, positional records, dictionaries,
  `Guid`, `Uri`, `DateTimeOffset`, `TimeSpan`, and many nullable forms
- Build-time generated bindings with NativeAOT support
- Autolinking of modules on all four platforms

See the [roadmap](docs/roadmap.md) for planned features and open work.

## How it works

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

C# code never sees raw `jsi::Runtime`, `jsi::Value` or `jsi::Object`. It works
with opaque handles and managed wrappers.

## Platform support

| Platform | HostFXR (dev loader) | NativeAOT | Mono AOT |
|---|---|---|---|
| Windows | yes | yes | no |
| macOS | yes | yes | no |
| Android | no | yes | planned |
| iOS | no | yes | planned |

HostFXR is a development-time loader. Production builds on every platform use
NativeAOT. Mono AOT is a planned option for the mobile platforms.

## Using it in an app

The packages are consumed from this pnpm workspace. The sample apps show the
setup:

1. Add `expo-modules-dotnet`, `expo-modules-dotnet-autolinking` and your
   module packages as dependencies.
2. Declare each module package in `expo-module.config.json` with the `dotnet`
   platform and the path to its `.csproj`.
3. Build the app. The native build hooks run
   `expo-modules-dotnet-autolinking link`. It finds the modules, generates one
   `ExpoDotnetHost` project that references all of them, builds it, and stages
   the result into the app.

On Windows, `sync-windows` also adds the managed projects to the React Native
Windows solution. Each app README lists the exact steps and prerequisites:

- [`apps/mobile-app`](apps/mobile-app/README.md) for iOS and Android.
- [`apps/desktop-app`](apps/desktop-app/README.md) for macOS and Windows.

Standalone module libraries in separate repositories and app-local modules are
not supported yet.

## Packages

| Package | Purpose |
|---|---|
| [`expo-modules-dotnet`](packages/expo-modules-dotnet) | Public Expo adapter. Owns the JavaScript API, the TurboModule installer, the platform glue, the reusable C++ JSI bridge, the managed core packages and the Hermes-backed testhost. |
| [`expo-modules-dotnet-autolinking`](packages/expo-modules-dotnet-autolinking/README.md) | CLI that discovers .NET modules, generates the `ExpoDotnetHost` aggregator, and builds and stages the native artifacts. |
| [`example-module`](packages/example-module) | Reference module. Start here when you write your own. |
| [`expo-asset-dotnet`](packages/expo-asset-dotnet) | Asset download and cache module. |
| [`expo-constants-dotnet`](packages/expo-constants-dotnet) | Typed app constants for desktop hosts. |
| [`expo-file-system-dotnet`](packages/expo-file-system-dotnet) | Local file system module. Work in progress. |

The sample apps are in `apps/`: `mobile-app`, `desktop-app` and
`hermes-console-app` (a headless Hermes app for bridge-level checks).
`experiments/` holds small HostFXR and NativeAOT loader checks.

## Development

Install dependencies and run the managed test suite:

```bash
pnpm install
scripts/test-managed.sh
```

Type-check the mobile app:

```bash
pnpm --filter mobile-app typecheck
```

Before you finish a code change, check formatting:

```bash
scripts/format.sh --check --all
```

If the check fails, run `scripts/format.sh` and check again.

### Documentation

- `docs/README.md` is the map of all documentation.
- `docs/specs/` holds the living specs. They describe current behavior.
- `docs/roadmap.md` lists open work and decisions.
- `docs/references/previous-windows-prototype.md` gives history from the
  earlier Windows prototype.
