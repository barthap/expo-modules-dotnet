# Library Authoring And Autolinking

This note records the authoring shape for the `Expo.ModulesCore` Roslyn
generator. The current autolinking contract is specified in
`docs/specs/dotnet-autolinking.md`.

## Intended Library Author Experience

The long-term author experience should be a normal package reference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Expo.ModulesCore" />
  </ItemGroup>
</Project>
```

`Expo.ModulesCore` should provide:

- the authored attributes such as `[ExpoModule]` and `[JS]`;
- runtime helpers used by generated glue;
- analyzer/source-generator assets that run during compilation.

Library authors should not need to reference the generator project manually
once packaging is in place.

## Manual Development Wiring

Before package analyzer assets are finalized, repo-local tests and experiments
may wire the generator as an analyzer project reference:

```xml
<ItemGroup>
  <ProjectReference Include="../Expo.ModulesCore/Expo.ModulesCore.csproj" />
  <ProjectReference
    Include="../Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
</ItemGroup>
```

This configuration is a development path, not the desired final library
contract. It lets test projects consume generated source without requiring a
NuGet package.

## Inspecting Generated Output

Roslyn source generators usually pass generated source to the compiler in
memory. The generated provider is not normally written as a stable source file
in the repository.

To inspect generated output on disk, opt in from the project that consumes the
generator. This means the module library project, test project, or experiment
project that declares `[ExpoModule]` classes. It does not mean
`Expo.ModulesCore.csproj` or `Expo.ModulesCore.Generator.csproj`.

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
  <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

The usual SDK-style output location is under the consuming project's
intermediate directory:

```text
obj/Debug/<target-framework>/generated/
```

For example, the Hermes console app's HostFXR build (`apps/hermes-console-app`,
assembly name `HermesConsoleApp`) may emit a generated provider under:

```text
obj/Debug/net10.0/generated/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.ExpoModulesGenerator/ExpoModulesProvider_HermesConsoleApp.g.cs
```

This opt-in is useful for debugging generator output. It should not be required
for normal library builds, and generated files should remain untracked build
artifacts.

## Authored Module Syntax

Synchronous `[JS]` functions are the baseline authoring shape:

```csharp
using Expo.ModulesCore;

namespace Expo.Example;

[ExpoModule]
public sealed partial class MathModule
{
  [JS]
  public double Add(double a, double b) => a + b;
}
```

Explicit JavaScript names are supported through attributes:

```csharp
[ExpoModule("Math")]
public sealed partial class InternalMathModule
{
  [JS("add")]
  public double AddNumbers(double a, double b) => a + b;
}
```

See `docs/roadmap.md` for the current authoring scope and what remains in the
backlog; that list is maintained there rather than duplicated here.

## Library-Local Generated Provider

Each C# Expo library should generate one deterministic provider for modules in
that library's compilation.

Illustrative generated shape:

```csharp
namespace Expo.ModulesCore.Generated;

public static class ExpoModulesProvider_ExpoExample
{
  public static void Register(JavaScriptRuntime runtime)
  {
    using var math = ModuleRegistry.DefineModule(runtime, "Math");
    GeneratedFunction.DefineSync(
        runtime,
        math,
        "add",
        2,
        MathAddHostFunction,
        new Expo.Example.InternalMathModule()
    );
  }
}
```

The generated provider is deterministic so an app-level generated file can call
it later; the `ExpoModulesProvider_{assemblyName}` naming contract is defined
below in Dotnet Config Shape, and the provider must remain suitable for
cross-assembly use.

## Two-Stage Generation Model

Stage 1 is library-local generation:

- The module library compiles with the Roslyn generator.
- The generator sees `[ExpoModule]` classes in the current compilation.
- The generator emits direct-call module registration glue for that library.
- The library build can fail early for unsupported signatures.

Stage 2 is app-level aggregation implemented by
`packages/expo-modules-dotnet-autolinking`:

- The autolinking CLI resolves dotnet Expo libraries from
  `expo-module.config.json`.
- The tool generates one app-level `ExpoDotnetHost` project.
- The app-level provider calls each linked library's generated provider.
- App startup calls the aggregate provider through the generated
  `expo_dotnet_create_runtime_context_result_v2` entry point.

Illustrative app-level output:

```csharp
public static class LinkedExpoModulesProvider
{
  public static void Register(JavaScriptRuntime runtime)
  {
    Expo.ModulesCore.Generated.ExpoModulesProvider_ExpoClipboard.Register(runtime);
    Expo.ModulesCore.Generated.ExpoModulesProvider_ExpoFileSystem.Register(runtime);
  }
}
```

The app-level provider should not discover individual module classes. Module
class discovery belongs to each library's generator run.

## Dotnet Config Shape

The autolinking CLI parses `expo-module.config.json` entries with this shape:

```json
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

`path` identifies the C# project that produces an Expo module assembly.
`assemblyName` is optional and defaults to the csproj file basename. When
provided, it must match the assembly name used by the Roslyn generator to name
`ExpoModulesProvider_{assemblyName}`.

The config should not list individual module classes. The `[ExpoModule]`
attributes are the source of truth for module class discovery inside the
library project.

## Generator Non-Goals

The generator does not:

- package analyzer assets for external NuGet consumption (see Manual
  Development Wiring above);
- scan runtime assemblies for module types;
- inspect referenced assemblies for module classes.

For authoring surfaces still outstanding, see `docs/roadmap.md`.
