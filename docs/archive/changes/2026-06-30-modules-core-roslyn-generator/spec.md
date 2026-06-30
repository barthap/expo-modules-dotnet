# Expo.ModulesCore Roslyn Generator

## Goal

Introduce the first Roslyn source-generator milestone for `Expo.ModulesCore`.
The generator consumes real authored C# module syntax and emits the
direct-call JSI glue shape already proven by generated-looking tests.

This milestone is intentionally narrow. It proves build-time discovery,
generated provider shape, typed conversion, direct module invocation, and
unsupported-signature diagnostics for synchronous module functions.

## Current Baseline

`Expo.ModulesCore` currently owns generated-binding runtime helpers above
`Expo.JSI`:

- `ModuleRegistry` installs modules under `globalThis.expo.modules`.
- `GeneratedFunction` creates host functions and checks arity.
- `IJavaScriptCodec<T>` and primitive/array codecs perform typed conversion.
- `Expo.ModulesCore.Tests` contains hand-written generated-looking module
  providers that exercise the runtime path under Hermes.

`Expo.ModulesCore` does not yet expose public authored v2 syntax, a source
generator, generated provider code, or app-level autolinking.

## Scope

This milestone adds:

- authored attributes consumed by the generator, such as `[ExpoModule]` and
  `[JS]`;
- a Roslyn generator project for library-local generation;
- one deterministic generated provider per module assembly;
- generator diagnostics for unsupported sync function signatures;
- Hermes-backed tests proving generated code can register and call sync
  modules;
- documentation for library-author requirements, manual analyzer wiring, the
  future two-stage autolinking model, and proposed dotnet
  `expo-module.config.json` shape.

This milestone does not add:

- records;
- async functions or promise-returning generated functions;
- properties;
- events;
- shared objects;
- default or optional arguments;
- real autolinking;
- parsing of `expo-module.config.json`;
- runtime assembly scanning;
- runtime hot-path reflection or dynamic invocation;
- platform adapters.

Existing generated-looking tests may remain beside new generator-backed tests.
If helper behavior conflicts with the generator path, favor the generated path
and keep the helper shape aligned with what generated code needs.

## Design

### Library-Local Generation

Each C# Expo library runs the generator in its own project. The generator
discovers `[ExpoModule]` classes in that compilation and emits a deterministic
provider for that assembly.

Generated code SHALL register only modules owned by the current compilation.
It SHALL NOT scan referenced assemblies for modules.

The intended library-author path is a normal package reference to
`Expo.ModulesCore`, with generator assets supplied by the package. During
repo-local development and tests, projects MAY wire the generator manually as
an analyzer project reference. The companion documentation describes both
paths.

### App-Level Aggregation

Future autolinking will resolve dotnet Expo libraries and generate an app-level
aggregate provider. The aggregate provider will call each linked library's
generated provider.

This milestone documents that two-stage model but does not implement the
autolinking tool or parse package config.

### Generated Provider Shape

The generated provider SHOULD have a stable namespace and type name derived
from the assembly identity, for example:

```csharp
namespace Expo.ModulesCore.Generated;

public static class ExpoModulesProvider_ExpoExample
{
  public static void Register(JavaScriptRuntime runtime)
  {
    // Generated module registrations for the current assembly.
  }
}
```

The exact sanitization rule may be refined during implementation, but the
shape MUST be deterministic and suitable for an app-level generated aggregate
provider to call later.

Generated function bodies SHALL:

- instantiate or otherwise access the authored module directly;
- decode arguments through typed `Expo.ModulesCore` codecs;
- call the authored method directly;
- encode return values through typed `Expo.ModulesCore` codecs;
- install functions through `ModuleRegistry` and `GeneratedFunction`;
- preserve `Expo.JSI` ownership and scoped-ref rules.

Generated function bodies SHALL NOT use `Assembly.GetTypes`,
`MethodInfo.Invoke`, `Delegate.DynamicInvoke`, `object?[]` as the normal
argument container, or JSON serialization for ordinary JSI values.

### Authored Syntax

The first authored syntax is intentionally small:

```csharp
[ExpoModule]
public sealed partial class MathModule
{
  [JS]
  public double Add(double a, double b) => a + b;
}
```

The generator SHOULD support explicit module and function names:

```csharp
[ExpoModule("Math")]
public sealed partial class InternalMathModule
{
  [JS("add")]
  public double AddNumbers(double a, double b) => a + b;
}
```

Module classes in this milestone SHOULD be non-generic, non-abstract, and
constructible by generated code. A public or internal parameterless constructor
is the initial supported construction model.

### Supported Sync Signatures

The first generated functions are synchronous instance methods.

Supported parameter and return types are limited to the types already proven
by generated-looking helpers:

- `bool`;
- `double`;
- `string`;
- `IReadOnlyList<T>` where `T` is a supported element type.

`void` return support is deferred because `Expo.JSI` does not currently expose
an undefined-value factory for generated host-function returns.

Unsupported signatures MUST produce Roslyn diagnostics. They MUST NOT fall
back to runtime reflection or runtime discovery.

## Requirements

### ADDED Requirement: ModulesCore Consumes Authored Syntax Through Roslyn

`Expo.ModulesCore` SHALL expose authored module attributes only when those
attributes are consumed by a Roslyn generator in the same milestone.

#### Scenario: Attribute-backed module is compiled
- **GIVEN** a C# project references `Expo.ModulesCore` and has the generator
  configured
- **WHEN** it declares a class with `[ExpoModule]` and a sync method with
  `[JS]`
- **THEN** the generator SHALL emit direct-call registration glue for that
  module

### ADDED Requirement: Generated Providers Are Library-Local

The generator SHALL emit one deterministic provider for modules in the current
compilation.

#### Scenario: Package-local provider is generated
- **GIVEN** a library project declares module classes
- **WHEN** the project is compiled
- **THEN** generated code SHALL register only modules declared in that library
  project
- **AND** generated code SHALL expose a stable provider that future app-level
  autolinking can call

### ADDED Requirement: App Aggregation Is Future Autolinking Work

The first generator milestone SHALL document app-level aggregation but SHALL
NOT implement real autolinking.

#### Scenario: Multiple libraries are linked into an app
- **GIVEN** future autolinking resolves several dotnet Expo libraries
- **WHEN** an app-level provider is generated
- **THEN** it SHALL call each library-local generated provider
- **AND** module class discovery SHALL remain owned by each library's Roslyn
  generation step

### ADDED Requirement: Sync Function Generation Uses Direct Calls

Generated sync function glue SHALL decode arguments, call authored methods
directly, and encode return values through typed helpers.

#### Scenario: Generated sync module function is called from JavaScript
- **GIVEN** a generated provider registered a module under
  `globalThis.expo.modules`
- **WHEN** JavaScript calls a generated sync function with supported arguments
- **THEN** the generated host function SHALL decode arguments through typed
  codecs
- **AND** call the authored method directly
- **AND** return the encoded result through `Expo.JSI`

### ADDED Requirement: Unsupported Signatures Are Build Diagnostics

Unsupported generated function signatures SHALL fail at build time with
actionable diagnostics.

#### Scenario: Unsupported parameter type is used
- **GIVEN** a `[JS]` method has an unsupported parameter type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported
  parameter or return type
- **AND** generated runtime glue SHALL NOT attempt dynamic invocation

### ADDED Requirement: Documentation Covers Library Authoring

The milestone SHALL document how library authors configure generation today
and how future autolinking is expected to aggregate generated providers.

#### Scenario: Developer wants manual repo-local generator wiring
- **GIVEN** a test or development project cannot consume packaged analyzer
  assets yet
- **WHEN** it needs generator output
- **THEN** documentation SHALL show the manual analyzer `ProjectReference`
  configuration

#### Scenario: Future dotnet package config is designed
- **GIVEN** autolinking is not implemented yet
- **WHEN** this milestone documents future package discovery
- **THEN** documentation SHALL include a proposed dotnet
  `expo-module.config.json` shape
- **AND** state that this milestone does not parse that config

## Testing

Add generator-backed mini modules beside existing generated-looking tests.
Tests SHOULD cover:

- default module name;
- explicit module name;
- default JS function name;
- explicit JS function name;
- supported primitive sync calls;
- supported `IReadOnlyList<T>` sync calls if practical in the first slice;
- unsupported signature diagnostics;
- generated provider registration under Hermes.

Existing generated-looking tests remain useful as helper-runtime baselines.

## Verification

For the implementation slice, run:

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
```

Any match in generated tests, generator implementation, or docs must be
intentional and explained by context.
