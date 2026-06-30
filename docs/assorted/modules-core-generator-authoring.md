# Library Authoring And Future Autolinking

This note records the intended authoring and autolinking shape for the first
`Expo.ModulesCore` Roslyn generator milestone. It is future-facing design
documentation for the implementation slice; it is not an implemented
autolinking contract yet.

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

For example, a HostFXR proof build may emit a generated provider under:

```text
obj/Debug/net10.0/generated/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.ExpoModulesGenerator/ExpoModulesProvider_HostFxrJSIProof.g.cs
```

This opt-in is useful for debugging generator output. It should not be required
for normal library builds, and generated files should remain untracked build
artifacts.

## Authored Module Syntax

The first generator milestone supports synchronous functions only:

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

The first slice excludes records, async functions, properties, events, shared
objects, optional/default arguments, and platform adapters.

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

The generated provider should be deterministic so an app-level generated file
can call it later. The first implementation may refine the exact namespace,
type-name sanitization rule, and visibility, but the provider must be suitable
for cross-assembly use.

## Two-Stage Generation Model

Stage 1 is library-local generation:

- The module library compiles with the Roslyn generator.
- The generator sees `[ExpoModule]` classes in the current compilation.
- The generator emits direct-call module registration glue for that library.
- The library build can fail early for unsupported signatures.

Stage 2 is future app-level aggregation:

- A future autolinking tool resolves dotnet Expo libraries.
- The tool generates one app-level provider file.
- The app-level provider calls each linked library's generated provider.
- App startup calls the aggregate provider.

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

## Proposed Dotnet Config Shape

This milestone does not parse `expo-module.config.json`. The following shape
is a proposed future input for dotnet autolinking:

```json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      {
        "path": "src/Expo.Example/Expo.Example.csproj",
        "assemblyName": "Expo.Example"
      }
    ]
  }
}
```

`path` identifies the C# project that produces an Expo module assembly.
`assemblyName` is optional if the autolinking tool can read it from the project
file, but it may be useful as an explicit stable override.

The config should not list individual module classes. The `[ExpoModule]`
attributes are the source of truth for module class discovery inside the
library project.

## Non-Goals For The First Generator Milestone

The first generator milestone does not:

- parse `expo-module.config.json`;
- generate an app-level aggregate provider;
- package analyzer assets for external NuGet consumption;
- scan runtime assemblies for module types;
- inspect referenced assemblies for module classes;
- support records, async functions, properties, events, or shared objects.
