# Expo.ModulesCore Roslyn Generator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first `Expo.ModulesCore` Roslyn generator milestone for synchronous `[JS]` module functions.

**Architecture:** Authored attributes live in `Expo.ModulesCore`. A separate `Expo.ModulesCore.Generator` analyzer project discovers `[ExpoModule]` classes in the current compilation and emits one deterministic provider for that assembly. `Expo.ModulesCore.Tests` consumes the generator through a manual analyzer project reference and verifies generated mini modules under Hermes, while `Expo.ModulesCore.Generator.Tests` verifies generated source and diagnostics without Hermes.

**Tech Stack:** .NET 10 projects, Roslyn incremental source generator, `Microsoft.CodeAnalysis.CSharp` 5.3.0, xUnit v3, Hermes-backed `scripts/test-managed.sh`.

**Repo Rules:** Do not create a git worktree. Do not commit unless the user explicitly asks. Keep code changes surgical and keep generated-module behavior in `Expo.ModulesCore.Tests`.

---

## File Structure

- Create `managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj`: Roslyn analyzer/source-generator project targeting `netstandard2.0`.
- Create `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`: incremental generator entry point and source emission.
- Create `managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`: diagnostic descriptors.
- Create `managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`: immutable model records used by the generator.
- Create `managed/packages/Expo.ModulesCore.Generator/IsExternalInit.cs`: compatibility shim for records in the `netstandard2.0` generator project.
- Create `managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`: non-Hermes generator unit tests.
- Create `managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`: helper that compiles source with the generator and returns diagnostics/generated source.
- Create `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`: generated-source tests and unsupported-signature diagnostic tests.
- Create `managed/packages/Expo.ModulesCore/ExpoModuleAttribute.cs`: authored module attribute.
- Create `managed/packages/Expo.ModulesCore/JSAttribute.cs`: authored function attribute.
- Modify `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`: add manual analyzer project reference.
- Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`: Hermes-backed tests for real generated modules.
- Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`: authored test modules consumed by the generator.
- Modify `scripts/test-managed.sh`: build generator and run generator tests before Hermes-backed suites.
- Modify `docs/specs/modules-core-boundary.md`: merge accepted generator requirements after implementation.
- Modify `docs/changes/2026-06-30-modules-core-roslyn-generator/library-authoring.md`: update exact provider shape if implementation settles names.
- Archive `docs/changes/2026-06-30-modules-core-roslyn-generator/` after the living spec is updated and verification is green.

## Task 1: Add Authored Attributes

**Files:**
- Create: `managed/packages/Expo.ModulesCore/ExpoModuleAttribute.cs`
- Create: `managed/packages/Expo.ModulesCore/JSAttribute.cs`
- Test indirectly in Task 3 and Task 5.

- [ ] **Step 1: Add `ExpoModuleAttribute`**

Create `managed/packages/Expo.ModulesCore/ExpoModuleAttribute.cs`:

```csharp
namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExpoModuleAttribute : Attribute
{
  public ExpoModuleAttribute()
  {
  }

  public ExpoModuleAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
  }

  public string? Name { get; }
}
```

- [ ] **Step 2: Add `JSAttribute`**

Create `managed/packages/Expo.ModulesCore/JSAttribute.cs`:

```csharp
namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class JSAttribute : Attribute
{
  public JSAttribute()
  {
  }

  public JSAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
  }

  public string? Name { get; }
}
```

- [ ] **Step 3: Build `Expo.ModulesCore`**

Run:

```sh
dotnet build managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj
```

Expected: build succeeds with no warnings introduced by the new attributes.

## Task 2: Scaffold Generator And Test Project

**Files:**
- Create: `managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj`
- Create: `managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Create: `managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Create: `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Create: `managed/packages/Expo.ModulesCore.Generator/IsExternalInit.cs`
- Create: `managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`
- Create: `managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`
- Create: `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add generator project**

Create `managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="5.3.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add record compatibility shim**

Create `managed/packages/Expo.ModulesCore.Generator/IsExternalInit.cs`:

```csharp
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
```

- [ ] **Step 3: Add generator model types**

Create `managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`:

```csharp
namespace Expo.ModulesCore.Generator;

internal sealed record ExpoModuleModel(
    string FullyQualifiedTypeName,
    string ModuleName,
    EquatableArray<ExpoFunctionModel> Functions);

internal sealed record ExpoFunctionModel(
    string MethodName,
    string JavaScriptName,
    string ReturnType,
    EquatableArray<ExpoParameterModel> Parameters);

internal sealed record ExpoParameterModel(
    string Name,
    string TypeName,
    string CodecExpression);

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
  private readonly T[] _values;

  public EquatableArray(IEnumerable<T> values)
  {
    _values = values.ToArray();
  }

  public IReadOnlyList<T> Values => _values;

  public bool Equals(EquatableArray<T> other) => _values.SequenceEqual(other._values);

  public override bool Equals(object? obj) =>
      obj is EquatableArray<T> other && Equals(other);

  public override int GetHashCode()
  {
    unchecked
    {
      var hash = 17;
      foreach (var value in _values)
      {
        hash = (hash * 31) + value.GetHashCode();
      }
      return hash;
    }
  }
}
```

- [ ] **Step 4: Add diagnostics**

Create `managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace Expo.ModulesCore.Generator;

internal static class ExpoModulesDiagnostics
{
  public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
      id: "EXPOJSI001",
      title: "Unsupported Expo module parameter type",
      messageFormat: "Parameter '{0}' on '{1}' uses unsupported type '{2}'.",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
      id: "EXPOJSI002",
      title: "Unsupported Expo module return type",
      messageFormat: "Method '{0}' uses unsupported return type '{1}'.",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );

  public static readonly DiagnosticDescriptor UnsupportedModuleConstructor = new(
      id: "EXPOJSI003",
      title: "Unsupported Expo module constructor",
      messageFormat: "Module '{0}' must have a public or internal parameterless constructor.",
      category: "Expo.ModulesCore",
      defaultSeverity: DiagnosticSeverity.Error,
      isEnabledByDefault: true
  );
}
```

- [ ] **Step 5: Add minimal generator that emits an empty provider**

Create `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`:

```csharp
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Expo.ModulesCore.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class ExpoModulesGenerator : IIncrementalGenerator
{
  private const string ExpoModuleAttributeMetadataName = "Expo.ModulesCore.ExpoModuleAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context)
  {
    var modules = context.SyntaxProvider.ForAttributeWithMetadataName(
        ExpoModuleAttributeMetadataName,
        static (node, _) => node is ClassDeclarationSyntax,
        static (syntaxContext, cancellationToken) =>
            CreateModuleModel(syntaxContext, cancellationToken)
    );

    var compilationAndModules = context.CompilationProvider.Combine(modules.Collect());

    context.RegisterSourceOutput(
        compilationAndModules,
        static (sourceContext, value) =>
        {
          var assemblyName = value.Left.AssemblyName ?? "ExpoModules";
          EmitProvider(
              sourceContext,
              assemblyName,
              value.Right.Where(module => module is not null).Select(module => module!)
          );
        }
    );
  }

  private static ExpoModuleModel? CreateModuleModel(
      GeneratorAttributeSyntaxContext context,
      CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
    {
      return null;
    }

    var moduleName = typeSymbol.Name.EndsWith("Module", StringComparison.Ordinal)
        ? typeSymbol.Name.Substring(0, typeSymbol.Name.Length - "Module".Length)
        : typeSymbol.Name;

    foreach (var attribute in context.Attributes)
    {
      if (attribute.ConstructorArguments is [{ Value: string explicitName }])
      {
        moduleName = explicitName;
      }
    }

    return new ExpoModuleModel(
        typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        moduleName,
        new EquatableArray<ExpoFunctionModel>(Array.Empty<ExpoFunctionModel>())
    );
  }

  private static void EmitProvider(
      SourceProductionContext context,
      string assemblyName,
      IEnumerable<ExpoModuleModel> modules)
  {
    var providerTypeName = $"ExpoModulesProvider_{SanitizeIdentifier(assemblyName)}";
    var builder = new StringBuilder();
    builder.AppendLine("// <auto-generated />");
    builder.AppendLine("#nullable enable");
    builder.AppendLine("namespace Expo.ModulesCore.Generated;");
    builder.AppendLine();
    builder.AppendLine($"public static class {providerTypeName}");
    builder.AppendLine("{");
    builder.AppendLine("  public static void Register(global::Expo.JSI.JavaScriptRuntime runtime)");
    builder.AppendLine("  {");
    builder.AppendLine("    global::System.ArgumentNullException.ThrowIfNull(runtime);");
    foreach (var module in modules)
    {
      builder.AppendLine($"    // Module discovered: {module.ModuleName}");
    }
    builder.AppendLine("  }");
    builder.AppendLine("}");

    context.AddSource($"{providerTypeName}.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
  }

  private static string SanitizeIdentifier(string value)
  {
    var builder = new StringBuilder(value.Length);
    foreach (var character in value)
    {
      builder.Append(char.IsLetterOrDigit(character) ? character : '_');
    }
    return builder.Length == 0 ? "ExpoModules" : builder.ToString();
  }
}
```

- [ ] **Step 6: Add generator test project**

Create `managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.0.0" />
    <PackageReference Include="xunit.v3" Version="3.2.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" />
    <ProjectReference Include="../Expo.ModulesCore/Expo.ModulesCore.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Add generator test host**

Create `managed/packages/Expo.ModulesCore.Generator.Tests/GeneratorTestHost.cs`:

```csharp
using System.Reflection;
using Expo.ModulesCore.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Expo.ModulesCore.Generator.Tests;

internal static class GeneratorTestHost
{
  public static GeneratorRunResult Run(string source, string assemblyName = "Expo.TestModules")
  {
    var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
    var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
    var references = new[]
    {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ExpoModuleAttribute).Assembly.Location),
    };

    var compilation = CSharpCompilation.Create(
        assemblyName,
        new[] { syntaxTree },
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
    );

    var generator = new ExpoModulesGenerator();
    GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
    driver = driver.RunGeneratorsAndUpdateCompilation(
        compilation,
        out var outputCompilation,
        out var generatorDiagnostics
    );

    var runResult = driver.GetRunResult().Results.Single();
    return new GeneratorRunResult(
        outputCompilation.GetDiagnostics().Concat(generatorDiagnostics).ToArray(),
        runResult.GeneratedSources.Select(sourceResult => sourceResult.SourceText.ToString()).ToArray()
    );
  }
}

internal sealed record GeneratorRunResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> GeneratedSources);
```

- [ ] **Step 8: Add initial generated-provider test**

Create `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Xunit;

namespace Expo.ModulesCore.Generator.Tests;

public sealed class ExpoModulesGeneratorTests
{
  [Fact]
  public void GeneratorEmitsDeterministicProviderForAssembly()
  {
    var result = GeneratorTestHost.Run(
        """
        using Expo.ModulesCore;

        namespace Expo.TestModules;

        [ExpoModule]
        public sealed partial class MathModule
        {
        }
        """
    );

    Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    var source = Assert.Single(result.GeneratedSources);
    Assert.Contains("public static class ExpoModulesProvider_Expo_TestModules", source);
    Assert.Contains("public static void Register(global::Expo.JSI.JavaScriptRuntime runtime)", source);
    Assert.Contains("// Module discovered: Math", source);
  }
}
```

- [ ] **Step 9: Run generator unit tests**

Run:

```sh
dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected: `GeneratorEmitsDeterministicProviderForAssembly` passes.

## Task 3: Generate Sync Function Glue

**Files:**
- Modify: `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add failing generated-source tests for sync functions**

Append these tests to `ExpoModulesGeneratorTests`:

```csharp
[Fact]
public void GeneratorEmitsDefaultAndExplicitFunctionNames()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoModule("Math")]
      public sealed partial class InternalMathModule
      {
        [JS]
        public double Add(double a, double b) => a + b;

        [JS("addOne")]
        public double Increment(double value) => value + 1.0;
      }
      """
  );

  Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  var source = Assert.Single(result.GeneratedSources);
  Assert.Contains("ModuleRegistry.DefineModule(runtime, \"Math\")", source);
  Assert.Contains("GeneratedFunction.DefineSync(runtime, module_Math, \"Add\", 2", source);
  Assert.Contains("GeneratedFunction.DefineSync(runtime, module_Math, \"addOne\", 1", source);
  Assert.Contains("module.Add(a, b)", source);
  Assert.Contains("module.Increment(value)", source);
}
```

Run:

```sh
dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter GeneratorEmitsDefaultAndExplicitFunctionNames
```

Expected: fail because the generator does not emit function glue yet.

- [ ] **Step 2: Extend model to carry functions**

Update `ExpoFunctionModel` and `ExpoParameterModel` usage so `CreateModuleModel`
collects instance methods with `JSAttribute`. For each supported method, store:

```csharp
new ExpoFunctionModel(
    method.Name,
    javaScriptName,
    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
    new EquatableArray<ExpoParameterModel>(
        method.Parameters.Select(parameter => new ExpoParameterModel(
            parameter.Name,
            parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            GetCodecExpression(parameter.Type)
        ))
    )
)
```

Use attribute metadata name `Expo.ModulesCore.JSAttribute`. For explicit names,
read the first constructor argument when it is a string.

- [ ] **Step 3: Add supported type mapping**

Add this helper to `ExpoModulesGenerator.cs`:

```csharp
private static string? GetCodecExpression(ITypeSymbol typeSymbol)
{
  var typeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
  return typeName switch
  {
      "global::System.Boolean" => "global::Expo.ModulesCore.Codecs.BoolCodec",
      "global::System.Double" => "global::Expo.ModulesCore.Codecs.DoubleCodec",
      "global::System.String" => "global::Expo.ModulesCore.Codecs.StringCodec",
      _ => TryGetReadOnlyListCodec(typeSymbol),
  };
}

private static string? TryGetReadOnlyListCodec(ITypeSymbol typeSymbol)
{
  if (typeSymbol is not INamedTypeSymbol namedType ||
      namedType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
      "global::System.Collections.Generic.IReadOnlyList<T>")
  {
    return null;
  }

  var elementType = namedType.TypeArguments.Single();
  var elementCodec = GetCodecExpression(elementType);
  if (elementCodec is null)
  {
    return null;
  }

  var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
  return $"global::Expo.ModulesCore.Codecs.JavaScriptArrayCodec<{elementTypeName}, {elementCodec}>";
}
```

- [ ] **Step 4: Emit direct-call function bodies**

Update `EmitProvider` so each generated module writes this shape:

```csharp
using var module_Math = global::Expo.ModulesCore.ModuleRegistry.DefineModule(runtime, "Math");
global::Expo.ModulesCore.GeneratedFunction.DefineSync(
    runtime,
    module_Math,
    "add",
    2,
    Math_add_HostFunction,
    new global::Expo.TestModules.InternalMathModule()
);
```

For each generated host function, emit:

```csharp
private static global::Expo.JSI.JavaScriptValue Math_add_HostFunction(
    global::Expo.JSI.JavaScriptRuntime runtime,
    global::Expo.JSI.JavaScriptValueRef thisValue,
    global::Expo.JSI.JavaScriptArguments arguments,
    object context)
{
  global::Expo.ModulesCore.GeneratedFunction.RequireArgumentCount("Math.add", arguments, 2);

  var module = (global::Expo.TestModules.InternalMathModule)context;
  var a = global::Expo.ModulesCore.Codecs.DoubleCodec.Decode(arguments.GetValue(0), runtime);
  var b = global::Expo.ModulesCore.Codecs.DoubleCodec.Decode(arguments.GetValue(1), runtime);
  return global::Expo.ModulesCore.Codecs.DoubleCodec.Encode(module.Add(a, b), runtime);
}
```

Do not support `void` returns in this slice. `Expo.JSI` does not currently
expose a `JavaScriptRuntime.CreateUndefined()` helper, so `void` would pull in
adjacent wrapper/runtime work.

- [ ] **Step 5: Run generated-source tests**

Run:

```sh
dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected: all generator unit tests pass.

## Task 4: Add Unsupported Signature Diagnostics

**Files:**
- Modify: `managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Modify: `managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [ ] **Step 1: Add failing diagnostic tests**

Append these tests:

```csharp
[Fact]
public void GeneratorReportsUnsupportedParameterType()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoModule]
      public sealed partial class BadModule
      {
        [JS]
        public double Bad(decimal value) => 0.0;
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI001");
  Assert.Contains("value", diagnostic.GetMessage());
  Assert.Contains("System.Decimal", diagnostic.GetMessage());
}

[Fact]
public void GeneratorReportsUnsupportedReturnType()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      [ExpoModule]
      public sealed partial class BadModule
      {
        [JS]
        public decimal Bad(double value) => 0m;
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI002");
  Assert.Contains("Bad", diagnostic.GetMessage());
  Assert.Contains("System.Decimal", diagnostic.GetMessage());
}
```

Run:

```sh
dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter Unsupported
```

Expected: fail because diagnostics are not reported yet.

- [ ] **Step 2: Report diagnostics during model creation**

Change the model creation path to return both `ExpoModuleModel?` and diagnostics,
or report diagnostics during source output from collected method metadata. Use
the descriptors from `ExpoModulesDiagnostics`.

When a parameter codec is `null`, report:

```csharp
Diagnostic.Create(
    ExpoModulesDiagnostics.UnsupportedParameterType,
    parameter.Locations.FirstOrDefault(),
    parameter.Name,
    method.Name,
    parameter.Type.ToDisplayString()
)
```

When the return codec is `null`, report:

```csharp
Diagnostic.Create(
    ExpoModulesDiagnostics.UnsupportedReturnType,
    method.ReturnType.Locations.FirstOrDefault(),
    method.Name,
    method.ReturnType.ToDisplayString()
)
```

Do not emit host functions for methods with unsupported signatures.

- [ ] **Step 3: Run diagnostic tests**

Run:

```sh
dotnet test managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter Unsupported
```

Expected: unsupported parameter and return diagnostics pass.

## Task 5: Add Hermes-Backed Generated Module Tests

**Files:**
- Modify: `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`
- Create: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`
- Create: `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`

- [ ] **Step 1: Wire generator into `Expo.ModulesCore.Tests`**

Modify `managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../Expo.ModulesCore/Expo.ModulesCore.csproj" />
  <ProjectReference
    Include="../Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj"
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
</ItemGroup>
```

- [ ] **Step 2: Add authored generated test modules**

Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModules.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Expo.ModulesCore.Tests.Generated;

[ExpoModule("GeneratedMath")]
public sealed partial class GeneratedMathModule
{
  [JS("add")]
  public double Add(double a, double b) => a + b;

  [JS]
  public double AddOneWhen(double value, bool shouldAddOne) =>
      shouldAddOne ? value + 1.0 : value;
}

[ExpoModule]
public sealed partial class GeneratedTextModule
{
  [JS("greet")]
  public string Greet(string name) => $"Hello, {name}";
}

[ExpoModule("GeneratedArray")]
public sealed partial class GeneratedArrayModule
{
  [JS("sum")]
  public double Sum(IReadOnlyList<double> values) => values.Sum();

  [JS("labels")]
  public IReadOnlyList<string> Labels() => ["one", "two"];
}
```

- [ ] **Step 3: Add failing Hermes tests**

Create `managed/packages/Expo.ModulesCore.Tests/Generated/GeneratedAttributeModuleTests.cs`:

```csharp
using Expo.JSI;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Tests.Fixtures;
using Xunit;

namespace Expo.ModulesCore.Tests.Generated;

public sealed class GeneratedAttributeModuleTests
{
  [Fact]
  public void GeneratedProviderDispatchesExplicitNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedMath.add(20.25, 22.25)",
          "generated-attribute-math-add.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderDispatchesDefaultNamedSyncFunction()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedMath.AddOneWhen(41.5, true)",
          "generated-attribute-math-default-name.js"
      );

      Assert.Equal(JavaScriptValueKind.Number, result.Kind);
      Assert.Equal(42.5, result.AsDouble());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderPreservesStrings()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "globalThis.expo.modules.GeneratedText.greet('Zoë\\u0000JS')",
          "generated-attribute-text-greet.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("Hello, Zoë\0JS", result.AsString());
      return true;
    });
  }

  [Fact]
  public void GeneratedProviderSupportsReadOnlyListConversions()
  {
    using var fixture = HermesRuntimeFixture.Create();

    fixture.Runtime.Execute(runtime =>
    {
      ExpoModulesProvider_Expo_ModulesCore_Tests.Register(runtime);

      using var result = fixture.Evaluate(
          "const labels = globalThis.expo.modules.GeneratedArray.labels(); " +
          "globalThis.expo.modules.GeneratedArray.sum([1, 2, 3.5]) + ':' + labels.join(',')",
          "generated-attribute-array.js"
      );

      Assert.Equal(JavaScriptValueKind.String, result.Kind);
      Assert.Equal("6.5:one,two", result.AsString());
      return true;
    });
  }
}
```

Run:

```sh
EXPO_JSI_TESTHOST_LIBRARY="$(pwd)/build/jsi-testhost/libexpo_jsi_testhost.dylib" \
  dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  --filter GeneratedAttributeModuleTests
```

Expected before final generator implementation: tests fail to compile or fail
because the generated provider does not register functions correctly.

- [ ] **Step 4: Fix provider type naming if needed**

If the actual sanitized test assembly name differs from
`ExpoModulesProvider_Expo_ModulesCore_Tests`, update the test and
`library-authoring.md` to the implemented deterministic sanitization rule.

- [ ] **Step 5: Run Hermes generated module tests**

Run:

```sh
EXPO_JSI_TESTHOST_LIBRARY="$(pwd)/build/jsi-testhost/libexpo_jsi_testhost.dylib" \
  dotnet test managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj \
  --filter GeneratedAttributeModuleTests
```

Expected: generated attribute module tests pass.

## Task 6: Update Managed Test Runner

**Files:**
- Modify: `scripts/test-managed.sh`

- [ ] **Step 1: Add generator build and tests**

Modify `scripts/test-managed.sh` after the `Expo.ModulesCore` build block:

```sh
echo
echo "==> Building Expo.ModulesCore.Generator"
dotnet build "$repo_root/managed/packages/Expo.ModulesCore.Generator/Expo.ModulesCore.Generator.csproj" -c "$configuration"

echo
echo "==> Running Expo.ModulesCore.Generator.Tests"
dotnet test "$repo_root/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj" \
  -c "$configuration" \
  "$@"
```

- [ ] **Step 2: Run managed suite**

Run:

```sh
scripts/test-managed.sh
```

Expected: generator tests, `Expo.JSI.Tests`, and `Expo.ModulesCore.Tests` all
pass. If Hermes prebuilt is missing, run the repo-suggested
`scripts/build-hermes-macos.sh` first and rerun `scripts/test-managed.sh`.

## Task 7: Documentation Merge And Cleanup

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/changes/2026-06-30-modules-core-roslyn-generator/library-authoring.md`
- Move or archive: `docs/changes/2026-06-30-modules-core-roslyn-generator/`

- [ ] **Step 1: Merge accepted requirements into living spec**

Update `docs/specs/modules-core-boundary.md` with current-state requirements:

```markdown
### Requirement: ModulesCore Consumes Authored Syntax Through Roslyn

`Expo.ModulesCore` SHALL expose authored module attributes only when those
attributes are consumed by the Roslyn generator.

#### Scenario: Attribute-backed module is compiled
- **GIVEN** a C# project references `Expo.ModulesCore` and has the generator configured
- **WHEN** it declares a class with `[ExpoModule]` and a sync method with `[JS]`
- **THEN** the generator SHALL emit direct-call registration glue for that module

### Requirement: Generated Providers Are Library-Local

The generator SHALL emit one deterministic provider for modules in the current
compilation.

#### Scenario: Package-local provider is generated
- **GIVEN** a library project declares module classes
- **WHEN** the project is compiled
- **THEN** generated code SHALL register only modules declared in that library project
- **AND** generated code SHALL expose a stable provider that future app-level autolinking can call

### Requirement: Unsupported Signatures Are Build Diagnostics

Unsupported generated function signatures SHALL fail at build time with
actionable diagnostics.

#### Scenario: Unsupported parameter type is used
- **GIVEN** a `[JS]` method has an unsupported parameter type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a diagnostic naming the unsupported type
- **AND** generated runtime glue SHALL NOT attempt dynamic invocation
```

Also update the existing "ModulesCore Avoids Inert Authored Syntax" requirement
so it says authored syntax is allowed only when consumed by the generator.

- [ ] **Step 2: Keep companion documentation current**

Review `docs/changes/2026-06-30-modules-core-roslyn-generator/library-authoring.md`
against the implemented provider namespace/type name and manual analyzer
reference. Update examples so they match code exactly.

- [ ] **Step 3: Archive transient plan artifacts**

After code, tests, and living specs are complete, move the change directory:

```sh
mkdir -p docs/archive/changes
mv docs/changes/2026-06-30-modules-core-roslyn-generator docs/archive/changes/
```

If the user wants to keep the companion authoring doc as durable front-door
documentation instead, move it to `docs/` before archiving the change directory
and update `docs/README.md` to link it.

## Task 8: Final Verification

**Files:**
- Verify all modified files.

- [ ] **Step 1: Run managed tests**

Run:

```sh
scripts/test-managed.sh
```

Expected: all managed tests pass, including generator unit tests and
Hermes-backed generated module tests.

- [ ] **Step 2: Run formatting check**

Run:

```sh
scripts/format.sh --check --all
```

Expected: `Formatting check passed.`

If formatting fails because C# files need whitespace changes, run:

```sh
scripts/format.sh
scripts/format.sh --check --all
```

- [ ] **Step 3: Run forbidden hot-path search**

Run:

```sh
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" managed/packages
```

Expected: no matches in production/generated runtime paths. Matches inside
docs or tests must be intentional references to forbidden patterns.

- [ ] **Step 4: Run diff whitespace check**

Run:

```sh
git diff --check
```

Expected: no output.

- [ ] **Step 5: Summarize final state**

Report:

- generated provider type name;
- supported first-slice signatures;
- diagnostics added;
- exact tests run and pass/fail result;
- any docs moved to `docs/archive/changes/` or durable `docs/`.
