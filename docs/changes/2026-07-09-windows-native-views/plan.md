# Windows Native Views Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Windows-only, composition-backed native view support for C# Expo Modules v2 while keeping `Expo.JSI` and `Expo.ModulesCore` universal.

**Architecture:** `Expo.ModulesCore` owns platform-neutral `[View]` / `[Prop]` attributes, generated metadata, and generated direct-call prop dispatch. A Windows sidecar package owns RNW/Fabric registration, Windows composition view lifetime, and native prop delivery. `apps/desktop-app` consumes the sidecar and renders a custom `ExampleColorBox` native view from `packages/example-module`.

**Tech Stack:** C# net10.0, Roslyn incremental generator, React Native Windows 0.81 Fabric view components, C++/WinRT, HostFXR/NativeAOT-compatible unmanaged entry points, TypeScript, pnpm, Vitest, xUnit.

---

## File Structure

- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/`
  - Add `ViewAttribute.cs`, `PropAttribute.cs`, `GeneratedViewDefinition.cs`, `GeneratedViewPropDefinition.cs`, `GeneratedViewPropKind.cs`.
  - These files contain no Windows, RNW, WinUI, XAML, or composition references.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/`
  - Extend `ExpoModuleModel.cs` with view metadata models.
  - Extend `ExpoModulesDiagnostics.cs` with view diagnostics `EXPOJSI012` through `EXPOJSI015`.
  - Extend `ExpoModulesGenerator.cs` to consume `[View]` / `[Prop]`, emit metadata, and emit direct-call prop dispatch.
- Modify `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`
  - Add generator tests for valid view metadata, prop dispatch, duplicate component names, duplicate props, and invalid prop setter shape.
- Modify `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
  - Generate Windows-only view entry points when `platform === "windows"`.
  - Use a Windows target framework for the Windows aggregator and reference the Windows sidecar managed project only for Windows.
- Modify autolinking tests under `packages/expo-modules-dotnet-autolinking/src/__tests__/`
  - Assert Windows aggregation includes view entry points and sidecar references.
  - Assert non-Windows aggregation stays universal.
- Create `packages/expo-modules-dotnet-windows/`
  - `package.json`, `react-native.config.js`, `src/index.tsx`.
  - `managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj`.
  - `managed/Expo.ModulesCore.Windows/WindowsExpoView.cs`.
  - `windows/ExpoModulesDotnetWindows/...` RNW C++ project files.
- Modify `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.*`
  - Expose the current managed runtime context through a small Windows-only native accessor consumed by the sidecar.
- Modify `packages/example-module/`
  - Multi-target `ExampleModule.csproj` for universal and Windows builds.
  - Add Windows-only partial module/view files for `ExampleColorBox`.
  - Export a JS helper that resolves the native view component through the Windows sidecar.
- Modify `apps/desktop-app/`
  - Add `expo-modules-dotnet-windows` dependency.
  - Render `ExampleColorBox` on Windows.
  - Keep non-Windows desktop behavior working.
- Modify `docs/specs/modules-core-boundary.md`, `docs/specs/dotnet-autolinking.md`, and add `docs/specs/windows-native-views.md`.
  - Merge accepted delta requirements after implementation.

---

### Task 1: Core View Attributes And Generated Metadata

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ViewAttribute.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/PropAttribute.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedViewDefinition.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedViewPropDefinition.cs`
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/GeneratedViewPropKind.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModuleModel.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [x] **Step 1: Write the failing generator test**

Add this test to `ExpoModulesGeneratorTests`:

```csharp
[Fact]
public void GeneratorEmitsViewMetadataAndDirectPropDispatch()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class ColorBoxView
      {
        public string? Color { get; set; }
      }

      [ExpoModule("ExampleModule")]
      [View("ExampleColorBox", typeof(ColorBoxView))]
      public sealed partial class ExampleModule
      {
        [Prop("color")]
        public void SetColor(ColorBoxView view, string? color)
        {
          view.Color = color;
        }
      }
      """
  );

  Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
  var source = Assert.Single(result.GeneratedSources).Text;
  Assert.Contains("public static global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.GeneratedViewDefinition> GetViewDefinitions()", source);
  Assert.Contains("new global::Expo.ModulesCore.GeneratedViewDefinition(", source);
  Assert.Contains("\"ExampleModule\"", source);
  Assert.Contains("\"ExampleColorBox\"", source);
  Assert.Contains("typeof(global::Expo.TestModules.ColorBoxView)", source);
  Assert.Contains("new global::Expo.ModulesCore.GeneratedViewPropDefinition(\"color\", global::Expo.ModulesCore.GeneratedViewPropKind.String)", source);
  Assert.Contains("public static object CreateView(", source);
  Assert.Contains("public static void UpdateViewProp(", source);
  Assert.Contains("module.SetColor((global::Expo.TestModules.ColorBoxView)view, value)", source);
  Assert.DoesNotContain("MethodInfo.Invoke", source);
  Assert.DoesNotContain("Delegate.DynamicInvoke", source);
  Assert.DoesNotContain("JsonSerializer", source);
}
```

- [x] **Step 2: Run the failing test**

Run:

```powershell
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter GeneratorEmitsViewMetadataAndDirectPropDispatch
```

Expected: FAIL because `ViewAttribute`, `PropAttribute`, generated view metadata, `CreateView`, and `UpdateViewProp` do not exist yet.

- [x] **Step 3: Add platform-neutral core types**

Create `ViewAttribute.cs`:

```csharp
namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewAttribute : Attribute
{
  public ViewAttribute(string componentName, Type viewType)
  {
    ComponentName = componentName;
    ViewType = viewType;
  }

  public string ComponentName { get; }
  public Type ViewType { get; }
}
```

Create `PropAttribute.cs`:

```csharp
namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PropAttribute : Attribute
{
  public PropAttribute(string name)
  {
    Name = name;
  }

  public string Name { get; }
}
```

Create `GeneratedViewPropKind.cs`:

```csharp
namespace Expo.ModulesCore;

public enum GeneratedViewPropKind
{
  String,
}
```

Create `GeneratedViewPropDefinition.cs`:

```csharp
namespace Expo.ModulesCore;

public sealed record GeneratedViewPropDefinition(
    string Name,
    GeneratedViewPropKind Kind);
```

Create `GeneratedViewDefinition.cs`:

```csharp
namespace Expo.ModulesCore;

public sealed record GeneratedViewDefinition(
    string ModuleName,
    string ComponentName,
    Type ViewType,
    IReadOnlyList<GeneratedViewPropDefinition> Props);
```

- [x] **Step 4: Extend generator models**

Add records to `ExpoModuleModel.cs`:

```csharp
internal sealed record ExpoViewModel(
    string ComponentName,
    string ViewTypeName,
    Location? Location,
    EquatableArray<ExpoViewPropModel> Props);

internal sealed record ExpoViewPropModel(
    string MethodName,
    string PropName,
    string ViewTypeName,
    string ValueTypeName,
    string PropKindExpression,
    Location? Location);
```

Add `ExpoViewModel? View` to `ExpoModuleModel` before `Functions`.

- [x] **Step 5: Extend generator implementation**

In `ExpoModulesGenerator.cs`, add metadata constants:

```csharp
private const string ViewAttributeMetadataName = "Expo.ModulesCore.ViewAttribute";
private const string PropAttributeMetadataName = "Expo.ModulesCore.PropAttribute";
```

In `CreateModuleModel`, collect the view before functions:

```csharp
var view = GetView(typeSymbol, moduleName, diagnostics);
```

Include `view` in the `ExpoModuleModel` constructor call.

Add a helper that supports string and nullable string props only in this first slice:

```csharp
private static ExpoViewModel? GetView(
    INamedTypeSymbol typeSymbol,
    string moduleName,
    List<ExpoDiagnosticModel> diagnostics)
{
  var viewAttribute = typeSymbol.GetAttributes().FirstOrDefault(attribute =>
      attribute.AttributeClass?.ToDisplayString() == ViewAttributeMetadataName);
  if (viewAttribute is null)
  {
    return null;
  }

  var componentName = viewAttribute.ConstructorArguments.ElementAtOrDefault(0).Value as string;
  var viewType = viewAttribute.ConstructorArguments.ElementAtOrDefault(1).Value as INamedTypeSymbol;
  if (string.IsNullOrWhiteSpace(componentName) || viewType is null)
  {
    diagnostics.Add(new ExpoDiagnosticModel(
        ExpoModulesDiagnostics.InvalidViewDeclaration.Id,
        typeSymbol.Locations.FirstOrDefault(),
        new EquatableArray<string>(new[] { moduleName, "view declaration must provide a non-empty component name and view type" })
    ));
    return null;
  }

  var viewTypeName = viewType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
  var props = GetViewProps(typeSymbol, moduleName, viewTypeName, diagnostics).ToArray();
  return new ExpoViewModel(
      componentName!,
      viewTypeName,
      typeSymbol.Locations.FirstOrDefault(),
      new EquatableArray<ExpoViewPropModel>(props)
  );
}
```

Add `GetViewProps`:

```csharp
private static IEnumerable<ExpoViewPropModel> GetViewProps(
    INamedTypeSymbol typeSymbol,
    string moduleName,
    string viewTypeName,
    List<ExpoDiagnosticModel> diagnostics)
{
  var props = new List<ExpoViewPropModel>();
  foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
  {
    var propAttribute = method.GetAttributes().FirstOrDefault(attribute =>
        attribute.AttributeClass?.ToDisplayString() == PropAttributeMetadataName);
    if (propAttribute is null)
    {
      continue;
    }

    var propName = propAttribute.ConstructorArguments.ElementAtOrDefault(0).Value as string;
    if (string.IsNullOrWhiteSpace(propName))
    {
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidViewProp.Id,
          method.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName, method.Name, "prop name must be non-empty" })
      ));
      continue;
    }

    if (method.IsStatic || method.IsGenericMethod || method.Parameters.Length != 2 ||
        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != viewTypeName ||
        !IsStringOrNullableString(method.Parameters[1].Type) ||
        (!method.ReturnsVoid && method.ReturnType.SpecialType != SpecialType.System_Void))
    {
      diagnostics.Add(new ExpoDiagnosticModel(
          ExpoModulesDiagnostics.InvalidViewProp.Id,
          method.Locations.FirstOrDefault(),
          new EquatableArray<string>(new[] { moduleName, method.Name, "prop setter must be an instance void method accepting the view type and string? value" })
      ));
      continue;
    }

    props.Add(new ExpoViewPropModel(
        method.Name,
        propName!,
        viewTypeName,
        method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        "global::Expo.ModulesCore.GeneratedViewPropKind.String",
        method.Locations.FirstOrDefault()
    ));
  }

  foreach (var duplicate in props.GroupBy(prop => prop.PropName, StringComparer.Ordinal).Where(group => group.Count() > 1))
  {
    diagnostics.Add(new ExpoDiagnosticModel(
        ExpoModulesDiagnostics.DuplicateViewPropName.Id,
        duplicate.Skip(1).First().Location,
        new EquatableArray<string>(new[] { moduleName, duplicate.Key })
    ));
  }

  return props;
}
```

Add `IsStringOrNullableString`:

```csharp
private static bool IsStringOrNullableString(ITypeSymbol typeSymbol) =>
    typeSymbol.SpecialType == SpecialType.System_String;
```

Emit `GetViewDefinitions`, `CreateView`, and `UpdateViewProp` from `EmitProvider`. Use a generated `switch` by component/prop name and direct method calls:

```csharp
public static global::System.Collections.Generic.IReadOnlyList<global::Expo.ModulesCore.GeneratedViewDefinition> GetViewDefinitions()
```

```csharp
public static object CreateView(global::Expo.ModulesCore.DotnetRuntimeContext context, string componentName)
```

```csharp
public static void UpdateViewProp(global::Expo.ModulesCore.DotnetRuntimeContext context, string componentName, object view, string propName, string? value)
```

- [x] **Step 6: Run generator tests**

Run:

```powershell
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter GeneratorEmitsViewMetadataAndDirectPropDispatch
```

Expected: PASS.

- [x] **Step 7: Commit**

Run:

```powershell
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --cached --check
git commit -m "Add generated view metadata syntax"
```

---

### Task 2: View Diagnostics

**Files:**
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/ExpoModulesGeneratorTests.cs`

- [x] **Step 1: Write failing diagnostics tests**

Add these tests:

```csharp
[Fact]
public void GeneratorReportsDuplicateViewComponentName()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class BoxView {}

      [ExpoModule("First")]
      [View("DuplicateBox", typeof(BoxView))]
      public sealed partial class FirstModule {}

      [ExpoModule("Second")]
      [View("DuplicateBox", typeof(BoxView))]
      public sealed partial class SecondModule {}
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI012");
  Assert.Contains("DuplicateBox", diagnostic.GetMessage());
}

[Fact]
public void GeneratorReportsDuplicateViewPropName()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class BoxView {}

      [ExpoModule("Box")]
      [View("BoxView", typeof(BoxView))]
      public sealed partial class BoxModule
      {
        [Prop("color")]
        public void SetColor(BoxView view, string? value) {}

        [Prop("color")]
        public void SetTint(BoxView view, string? value) {}
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI013");
  Assert.Contains("Box", diagnostic.GetMessage());
  Assert.Contains("color", diagnostic.GetMessage());
}

[Fact]
public void GeneratorReportsInvalidViewPropSetterShape()
{
  var result = GeneratorTestHost.Run(
      """
      using Expo.ModulesCore;

      namespace Expo.TestModules;

      public sealed class BoxView {}

      [ExpoModule("Box")]
      [View("BoxView", typeof(BoxView))]
      public sealed partial class BoxModule
      {
        [Prop("opacity")]
        public void SetOpacity(BoxView view, double value) {}
      }
      """
  );

  var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "EXPOJSI014");
  Assert.Contains("SetOpacity", diagnostic.GetMessage());
  Assert.Contains("string", diagnostic.GetMessage());
}
```

- [x] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "DuplicateViewComponentName|DuplicateViewPropName|InvalidViewPropSetterShape"
```

Expected: FAIL because diagnostics do not exist or do not use the expected ids.

- [x] **Step 3: Add diagnostics**

Add to `ExpoModulesDiagnostics.cs`:

```csharp
public static readonly DiagnosticDescriptor DuplicateViewComponentName = new(
    id: "EXPOJSI012",
    title: "Duplicate Expo view component name",
    messageFormat: "Multiple Expo modules export view component name '{0}'",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);

public static readonly DiagnosticDescriptor DuplicateViewPropName = new(
    id: "EXPOJSI013",
    title: "Duplicate Expo view prop name",
    messageFormat: "Module '{0}' exports duplicate view prop name '{1}'",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);

public static readonly DiagnosticDescriptor InvalidViewProp = new(
    id: "EXPOJSI014",
    title: "Invalid Expo view prop",
    messageFormat: "Module '{0}' has invalid view prop setter '{1}': {2}",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);

public static readonly DiagnosticDescriptor InvalidViewDeclaration = new(
    id: "EXPOJSI015",
    title: "Invalid Expo view declaration",
    messageFormat: "Module '{0}' has invalid view declaration: {1}",
    category: "Expo.ModulesCore",
    defaultSeverity: DiagnosticSeverity.Error,
    isEnabledByDefault: true
);
```

In `EmitProvider`, add duplicate component detection next to duplicate module detection:

```csharp
foreach (var duplicateView in modules
    .Where(module => module.View is not null)
    .GroupBy(module => module.View!.ComponentName, StringComparer.Ordinal)
    .Where(group => group.Count() > 1))
{
  var duplicateModules = duplicateView.ToArray();
  sourceContext.ReportDiagnostic(Diagnostic.Create(
      ExpoModulesDiagnostics.DuplicateViewComponentName,
      duplicateModules[1].View?.Location ?? duplicateModules[1].Location,
      duplicateView.Key
  ));
  hasDuplicateExports = true;
}
```

- [x] **Step 4: Verify diagnostics pass**

Run:

```powershell
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj --filter "DuplicateViewComponentName|DuplicateViewPropName|InvalidViewPropSetterShape"
```

Expected: PASS.

- [x] **Step 5: Commit**

Run:

```powershell
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --cached --check
git commit -m "Validate generated view declarations"
```

---

### Task 3: Windows Aggregator View Entry Points

**Files:**
- Modify: `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/types.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/commands/generateCommand.ts`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`

- [x] **Step 1: Write failing autolinking tests**

Add tests:

```ts
it('generates Windows view entry points and sidecar reference for Windows aggregation', () => {
  const result = generateAggregator(manifestWithExampleModule, {
    outputDir,
    adapterPackageRoot,
    platform: 'windows',
  });

  const csproj = readFileSync(join(outputDir, 'ExpoDotnetHost.csproj'), 'utf8');
  const entryPoints = readFileSync(join(outputDir, 'EntryPoints.g.cs'), 'utf8');

  expect(csproj).toContain('<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>');
  expect(csproj).toContain('managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj');
  expect(csproj).toContain('expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj');
  expect(entryPoints).toContain('expo_dotnet_windows_get_view_metadata');
  expect(entryPoints).toContain('expo_dotnet_windows_create_view');
  expect(entryPoints).toContain('expo_dotnet_windows_update_string_prop');
  expect(entryPoints).toContain('LinkedExpoModulesProvider.GetViewDefinitions()');
});

it('keeps non-Windows aggregation universal', () => {
  generateAggregator(manifestWithExampleModule, {
    outputDir,
    adapterPackageRoot,
    platform: 'macos',
  });

  const csproj = readFileSync(join(outputDir, 'ExpoDotnetHost.csproj'), 'utf8');
  const entryPoints = readFileSync(join(outputDir, 'EntryPoints.g.cs'), 'utf8');

  expect(csproj).toContain('<TargetFramework>net10.0</TargetFramework>');
  expect(csproj).not.toContain('Expo.ModulesCore.Windows.csproj');
  expect(entryPoints).not.toContain('expo_dotnet_windows_create_view');
});
```

- [x] **Step 2: Run failing tests**

Run:

```powershell
pnpm --filter expo-modules-dotnet-autolinking test -- generateAggregator
```

Expected: FAIL because `GenerateOptions` does not include `platform`, Windows target framework is not generated, and Windows view entry points do not exist.

- [x] **Step 3: Add platform-aware generation**

Extend `GenerateOptions`:

```ts
export interface GenerateOptions {
  outputDir: string;
  adapterPackageRoot: string;
  platform?: string;
}
```

In `generateCsproj`, derive:

```ts
const isWindows = options.platform === 'windows';
const targetFramework = isWindows ? 'net10.0-windows10.0.19041.0' : 'net10.0';
const coreReferences = [
  path.join(options.adapterPackageRoot, 'managed/packages/Expo.JSI/Expo.JSI.csproj'),
  path.join(options.adapterPackageRoot, 'managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj'),
  ...(isWindows
    ? [path.join(options.adapterPackageRoot, '../expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj')]
    : []),
];
```

Use `${targetFramework}` in the generated project file.

- [x] **Step 4: Generate view entry points only for Windows**

Change `generateEntryPoints()` to accept `options.platform` and append Windows-only methods when `platform === "windows"`. The generated methods should include:

```csharp
[UnmanagedCallersOnly(EntryPoint = "expo_dotnet_windows_get_view_metadata", CallConvs = new[] { typeof(CallConvCdecl) })]
public static int GetWindowsViewMetadata(nint outJson, nint outLength)
```

```csharp
[UnmanagedCallersOnly(EntryPoint = "expo_dotnet_windows_create_view", CallConvs = new[] { typeof(CallConvCdecl) })]
public static nint CreateWindowsView(nint runtimeContext, nint componentNameUtf8, int componentNameLength, nint compositor)
```

```csharp
[UnmanagedCallersOnly(EntryPoint = "expo_dotnet_windows_update_string_prop", CallConvs = new[] { typeof(CallConvCdecl) })]
public static int UpdateWindowsStringProp(nint runtimeContext, nint viewHandle, nint componentNameUtf8, int componentNameLength, nint propNameUtf8, int propNameLength, nint valueUtf8, int valueLength)
```

Use generated direct-call helpers:

```csharp
LinkedExpoModulesProvider.GetViewDefinitions()
LinkedExpoModulesProvider.CreateView(context, componentName)
LinkedExpoModulesProvider.UpdateViewProp(context, componentName, view, propName, value)
```

Metadata JSON is allowed here because it is startup metadata, not the prop dispatch path.

- [x] **Step 5: Verify tests pass**

Run:

```powershell
pnpm --filter expo-modules-dotnet-autolinking test -- generateAggregator
```

Expected: PASS.

- [x] **Step 6: Commit**

Run:

```powershell
git add packages/expo-modules-dotnet-autolinking
git diff --cached --check
git commit -m "Generate Windows view entry points"
```

---

### Task 4: Windows Managed Sidecar

**Files:**
- Create: `packages/expo-modules-dotnet-windows/package.json`
- Create: `packages/expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj`
- Create: `packages/expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/WindowsExpoView.cs`
- Modify: `pnpm-workspace.yaml` only if package discovery needs an explicit workspace entry beyond `packages/*`.

- [x] **Step 1: Create sidecar package manifest**

Create `package.json`:

```json
{
  "name": "expo-modules-dotnet-windows",
  "version": "0.1.0",
  "description": "Windows native view support for .NET-backed Expo modules",
  "main": "src/index.tsx",
  "types": "src/index.tsx",
  "license": "MIT",
  "peerDependencies": {
    "expo-modules-dotnet": "*",
    "react": "*",
    "react-native": "*",
    "react-native-windows": "*"
  },
  "devDependencies": {
    "typescript": "catalog:react-native-81"
  }
}
```

- [x] **Step 2: Create managed Windows project**

Create `Expo.ModulesCore.Windows.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 3: Add composition-backed base view**

Create `WindowsExpoView.cs`:

```csharp
using System.Numerics;
using Microsoft.UI.Composition;
using WinRT;

namespace Expo.ModulesCore.Windows;

public abstract class WindowsExpoView
{
  public Compositor? Compositor { get; private set; }
  public Visual? CompositionVisual { get; private set; }

  protected abstract Visual CreateCompositionVisual(Compositor compositor);

  protected virtual void OnLayout(float width, float height)
  {
    if (CompositionVisual is not null)
    {
      CompositionVisual.Size = new Vector2(width, height);
    }
  }

  protected virtual void OnDisposeComposition()
  {
  }

  public nint InitializeComposition(nint compositorPtr)
  {
    Compositor = MarshalInterface<Compositor>.FromAbi(compositorPtr);
    CompositionVisual = CreateCompositionVisual(Compositor);
    return MarshalInspectable<object>.FromManaged(CompositionVisual);
  }

  public void UpdateLayout(float width, float height)
  {
    OnLayout(width, height);
  }

  public void DisposeComposition()
  {
    OnDisposeComposition();
    CompositionVisual = null;
    Compositor = null;
  }
}
```

- [x] **Step 4: Verify managed sidecar builds**

Run:

```powershell
dotnet build packages/expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj
```

Expected: PASS on Windows with the Windows SDK available. If this fails because package references are required for `Microsoft.UI.Composition` or `WinRT`, add the minimum package references and rerun.

- [x] **Step 5: Commit**

Run:

```powershell
git add packages/expo-modules-dotnet-windows
git diff --cached --check
git commit -m "Add Windows managed view sidecar"
```

---

### Task 5: Windows Native Sidecar And Runtime Junction

**Files:**
- Create: `packages/expo-modules-dotnet-windows/react-native.config.js`
- Create: `packages/expo-modules-dotnet-windows/src/index.tsx`
- Create: `packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/*`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.h`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.def`

- [x] **Step 1: Add runtime context accessor to base Windows package**

In `ExpoModulesDotnetInstaller.h`, add a Windows-only accessor:

```cpp
void *CurrentManagedRuntimeContext() noexcept;
```

In `ExpoModulesDotnetInstaller.cpp`, implement by returning the installed runtime state under the existing mutex. Return `nullptr` when no runtime is installed.

In `ExpoModulesDotnet.def`, export:

```text
expo_modules_dotnet_current_runtime_context
```

This is the only native junction point from the view sidecar back into the base Windows installer. The sidecar resolves generated managed view entrypoints through the shared Windows loader instead of exporting C++ config structs across DLL boundaries.

- [x] **Step 2: Add sidecar React Native config**

Create `react-native.config.js`:

```js
module.exports = {
  dependency: {
    platforms: {
      android: null,
      ios: null,
      macos: null,
      windows: {
        sourceDir: './windows',
        solutionFile: 'ExpoModulesDotnetWindows.sln',
        projects: [
          {
            projectFile: 'ExpoModulesDotnetWindows\\ExpoModulesDotnetWindows.vcxproj',
            directDependency: true,
          },
        ],
      },
    },
  },
};
```

- [x] **Step 3: Add JS native view helper**

Create `src/index.tsx`:

```tsx
import * as NativeComponentRegistry from 'react-native-windows/Libraries/NativeComponent/NativeComponentRegistry';

export function requireDotnetNativeView<Props extends object>(
  name: string,
  propNames: readonly string[]
) {
  const validAttributes: Record<string, true> = {};
  for (const propName of propNames) {
    validAttributes[propName] = true;
  }

  return NativeComponentRegistry.get<Props>(name, () => ({
    uiViewClassName: name,
    validAttributes,
  }));
}
```

- [x] **Step 4: Create native sidecar project from the existing Windows package pattern**

Copy the minimal RNW C++/WinRT project shape from `packages/expo-modules-dotnet/windows/ExpoModulesDotnet` and rename it to `ExpoModulesDotnetWindows`. Keep these files focused:

```text
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ReactPackageProvider.cpp
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ReactPackageProvider.h
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ReactPackageProvider.idl
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ExpoDotnetViewManager.cpp
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ExpoDotnetViewManager.h
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ManagedViewHost.cpp
packages/expo-modules-dotnet-windows/windows/ExpoModulesDotnetWindows/ManagedViewHost.h
```

`ReactPackageProvider::CreatePackage` must call:

```cpp
RegisterDotnetViewComponents(packageBuilder);
```

`ExpoDotnetViewManager.cpp` must use RNW Fabric APIs:

```cpp
auto fabricBuilder = packageBuilder.try_as<IReactPackageBuilderFabric>();
if (!fabricBuilder) {
  return;
}
```

For each metadata entry, call:

```cpp
fabricBuilder.AddViewComponent(to_hstring(componentName), ...);
```

Use the previous Windows proof's `ExpoViewManager.cpp` only as a reference for
RNW Fabric callback names and lifetime ordering. Do not copy its JSON prop
dispatch.

- [x] **Step 5: Verify RNW autolinking sees the sidecar**

Run:

```powershell
pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

Expected before app dependency update: no sidecar project appears. After Task 7 adds the app dependency and runs autolink, the generated `AutolinkedNativeModules.g.*` files include `ExpoModulesDotnetWindows`.

Current pre-app-dependency check used the app-local CLI because `pnpm exec`
attempted a dependency-status install:

```powershell
apps/desktop-app/node_modules/.bin/react-native.CMD autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

Actual: PASS, no auto-linking changes necessary.

- [x] **Step 6: Commit**

Run:

```powershell
git add packages/expo-modules-dotnet/windows/ExpoModulesDotnet packages/expo-modules-dotnet-windows
git diff --cached --check
git commit -m "Register Windows dotnet view components"
```

---

### Task 6: Example Module Windows View

**Files:**
- Modify: `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj`
- Create: `packages/example-module/dotnet/ExampleModule/ExampleMathModule.Windows.cs`
- Create: `packages/example-module/dotnet/ExampleModule/ExampleColorBoxView.Windows.cs`
- Modify: `packages/example-module/src/index.ts`

- [x] **Step 1: Multi-target the example module**

Change the project file to:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../../../expo-modules-dotnet/managed/packages/Expo.ModulesCore/Expo.ModulesCore.csproj" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'">
    <ProjectReference Include="../../../expo-modules-dotnet-windows/managed/Expo.ModulesCore.Windows/Expo.ModulesCore.Windows.csproj" />
  </ItemGroup>

  <ItemGroup Condition="'$(TargetFramework)' != 'net10.0-windows10.0.19041.0'">
    <Compile Remove="**/*.Windows.cs" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Add Windows partial module syntax**

Create `ExampleMathModule.Windows.cs`:

```csharp
using Expo.ModulesCore;

namespace ExampleModule;

[View("ExampleColorBox", typeof(ExampleColorBoxView))]
public sealed partial class ExampleMathModule
{
  [Prop("color")]
  public void SetColor(ExampleColorBoxView view, string? color)
  {
    view.Color = color;
    view.CommitProps();
  }
}
```

- [x] **Step 3: Add composition view**

Create `ExampleColorBoxView.Windows.cs`:

```csharp
using System.Numerics;
using Expo.ModulesCore.Windows;
using Microsoft.UI.Composition;
using Windows.UI;

namespace ExampleModule;

public sealed class ExampleColorBoxView : WindowsExpoView
{
  private SpriteVisual? visual;
  private CompositionColorBrush? brush;

  public string? Color { get; set; }

  public void CommitProps()
  {
    if (brush is not null)
    {
      brush.Color = ParseColor(Color);
    }
  }

  protected override Visual CreateCompositionVisual(Compositor compositor)
  {
    brush = compositor.CreateColorBrush(ParseColor(Color));
    visual = compositor.CreateSpriteVisual();
    visual.Brush = brush;
    return visual;
  }

  protected override void OnLayout(float width, float height)
  {
    if (visual is not null)
    {
      visual.Size = new Vector2(width, height);
    }
  }

  protected override void OnDisposeComposition()
  {
    if (visual is not null)
    {
      visual.Brush = null;
    }
    visual = null;
    brush = null;
  }

  private static Color ParseColor(string? value) =>
      value?.ToLowerInvariant() switch
      {
        "red" => Color.FromArgb(0xff, 0xcd, 0x5c, 0x5c),
        "green" => Color.FromArgb(0xff, 0x2e, 0x8b, 0x57),
        "orange" => Color.FromArgb(0xff, 0xff, 0x8c, 0x00),
        "purple" => Color.FromArgb(0xff, 0x93, 0x70, 0xdb),
        _ => Color.FromArgb(0xff, 0x46, 0x82, 0xb4),
      };
}
```

- [x] **Step 4: Export JS view helper**

Update `packages/example-module/src/index.ts`:

```ts
import { requireDotnetNativeView } from 'expo-modules-dotnet-windows';
```

Add:

```ts
export type ExampleColorBoxProps = {
  color?: string;
  style?: import('react-native').StyleProp<import('react-native').ViewStyle>;
};

export const ExampleColorBox = requireDotnetNativeView<ExampleColorBoxProps>(
  'ExampleColorBox',
  ['color']
);
```

Current implementation keeps `expo-modules-dotnet-windows` as an optional peer and exports a null fallback component on non-Windows platforms.

- [x] **Step 5: Verify builds**

Run:

```powershell
dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -f net10.0
dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -f net10.0-windows10.0.19041.0
pnpm --filter desktop-app typecheck
```

Expected: universal target builds without Windows files; Windows target builds with `ExampleColorBoxView`; TypeScript sees the exported view.

Actual managed builds:

```powershell
dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -f net10.0
dotnet build packages/example-module/dotnet/ExampleModule/ExampleModule.csproj -f net10.0-windows10.0.19041.0
```

Both passed with zero warnings and zero errors after `dotnet build-server shutdown` released a stale compiler lock from an earlier parallel build attempt.

TypeScript verification:

```powershell
pnpm --filter desktop-app typecheck
```

Actual: PASS. The first install check required refreshing `pnpm-lock.yaml` because the new sidecar package and example module dev dependencies changed workspace package metadata.

- [x] **Step 6: Commit**

Run:

```powershell
git add packages/example-module
git diff --cached --check
git commit -m "Add example Windows native view"
```

---

### Task 7: Desktop App Windows Rendering

**Files:**
- Modify: `apps/desktop-app/package.json`
- Modify: `apps/desktop-app/App.tsx`
- Modify: `apps/desktop-app/windows/DesktopApp/AutolinkedNativeModules.g.*`
- Modify: `apps/desktop-app/windows/DesktopApp/AutolinkedNativeModules.g.props`
- Modify: `apps/desktop-app/windows/DesktopApp/AutolinkedNativeModules.g.targets`

- [ ] **Step 1: Add sidecar dependency**

In `apps/desktop-app/package.json`, add:

```json
"expo-modules-dotnet-windows": "workspace:*"
```

- [ ] **Step 2: Render the native view on Windows**

Import:

```tsx
import { ExampleColorBox } from 'example-module';
```

Add a color state and render block:

```tsx
const [boxColor, setBoxColor] = useState('green');
```

Inside the screen content, add:

```tsx
{Platform.OS === 'windows' ? (
  <View style={styles.nativeViewSection}>
    <ExampleColorBox color={boxColor} style={styles.nativeColorBox} />
    <Pressable
      accessibilityRole="button"
      onPress={() => setBoxColor(previous => (previous === 'green' ? 'purple' : 'green'))}
      style={({ pressed }) => [styles.button, pressed ? styles.buttonPressed : null]}>
      <Text style={styles.buttonText}>Toggle native color</Text>
    </Pressable>
  </View>
) : null}
```

Add styles:

```tsx
nativeViewSection: {
  gap: 12,
  marginBottom: 20,
},
nativeColorBox: {
  height: 96,
  width: '100%',
},
```

- [ ] **Step 3: Run Windows autolinking**

Run:

```powershell
pnpm --filter desktop-app exec react-native autolink-windows --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

Expected: generated Windows autolinking files include the sidecar native package.

- [ ] **Step 4: Verify app typecheck**

Run:

```powershell
pnpm --filter desktop-app typecheck
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git add apps/desktop-app
git diff --cached --check
git commit -m "Render example Windows native view"
```

---

### Task 8: End-To-End Windows Build Verification

**Files:**
- Modify only if verification reveals a defect.

- [ ] **Step 1: Install workspace dependencies**

Run:

```powershell
pnpm install --frozen-lockfile
```

Expected: PASS and lockfile unchanged unless package graph changes require an intentional lockfile update.

- [ ] **Step 2: Run managed tests**

Run:

```powershell
scripts/test-managed.sh
```

Expected: PASS.

- [ ] **Step 3: Run generator tests**

Run:

```powershell
dotnet test packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests/Expo.ModulesCore.Generator.Tests.csproj
```

Expected: PASS.

- [ ] **Step 4: Run autolinking tests**

Run:

```powershell
pnpm --filter expo-modules-dotnet-autolinking test
```

Expected: PASS.

- [ ] **Step 5: Run desktop checks**

Run:

```powershell
pnpm --filter desktop-app typecheck
pnpm --filter desktop-app exec react-native autolink-windows --check --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
pnpm --filter desktop-app exec react-native run-windows --no-packager --no-launch
```

Expected: typecheck passes, autolinking check passes, Windows build succeeds without launching.

- [ ] **Step 6: Verify boundary scans**

Run:

```powershell
rg "Microsoft\\.UI|ReactNative|WinUI|Xaml|Windows\\.UI|react-native-windows" packages/expo-modules-dotnet/managed/packages/Expo.JSI packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/expo-modules-dotnet/managed packages/example-module/dotnet
scripts/format.sh --check --all
git diff --check
```

Expected: first two `rg` commands produce no matches in universal core and no hot-path reflection/JSON dispatch in generated/view code; format and diff checks pass.

- [ ] **Step 7: Commit fixes if verification required changes**

Run only if files changed during verification:

```powershell
git add <changed-files>
git diff --cached --check
git commit -m "Fix Windows native view verification"
```

---

### Task 9: Merge Delta Into Living Specs And Clean Transient Artifacts

**Files:**
- Create: `docs/specs/windows-native-views.md`
- Modify: `docs/specs/README.md`
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/dotnet-autolinking.md`
- Remove or archive: `docs/changes/2026-07-09-windows-native-views/plan.md`
- Remove or archive: `docs/changes/2026-07-09-windows-native-views/spec.md`

- [ ] **Step 1: Add living spec**

Create `docs/specs/windows-native-views.md` with requirements from the accepted, implemented behavior:

```markdown
# Windows Native Views

## Purpose

Define Windows-only native view support for C# Expo Modules v2 while preserving
the universal `Expo.JSI` and `Expo.ModulesCore` package boundaries.

## Requirements

### Requirement: Generated View Syntax Is Platform-Neutral

`Expo.ModulesCore` SHALL expose `[View]` and `[Prop]` only as
generator-consumed, platform-neutral authoring syntax.

#### Scenario: Windows view module is compiled
- **GIVEN** a Windows-targeted module declares `[View]` and `[Prop]`
- **WHEN** the Roslyn generator runs
- **THEN** it SHALL emit platform-neutral view metadata
- **AND** it SHALL emit direct prop dispatch without runtime reflection,
  dynamic invocation, or JSON prop dispatch.

### Requirement: Windows Sidecar Owns Native View Hosting

The Windows sidecar SHALL own RNW/Fabric registration, composition visual
creation, prop delivery, layout, and teardown.

#### Scenario: Windows component is rendered
- **GIVEN** the desktop app renders the generated component
- **WHEN** RNW creates the native view
- **THEN** managed C# code SHALL create a Windows composition visual
- **AND** React prop changes SHALL update the visual through generated prop
  dispatch.
```

- [ ] **Step 2: Link spec from `docs/specs/README.md`**

Add:

```markdown
- `windows-native-views.md`: Windows-only generated native view metadata,
  RNW sidecar hosting, and desktop example proof.
```

- [ ] **Step 3: Merge module boundary requirements**

In `docs/specs/modules-core-boundary.md`, add scenarios under generated-binding helpers stating:

```markdown
#### Scenario: View syntax is generator-consumed
- **GIVEN** a module declares `[View]` or `[Prop]`
- **WHEN** the generator is configured
- **THEN** generated code SHALL consume the syntax into platform-neutral
  metadata and direct prop dispatch
- **AND** unconsumed view syntax SHALL NOT be shipped as inert API
```

- [ ] **Step 4: Merge autolinking requirements**

In `docs/specs/dotnet-autolinking.md`, add a Windows-specific scenario:

```markdown
#### Scenario: Windows aggregation includes view entry points
- **GIVEN** `link --platform windows` generates the app-level aggregator
- **WHEN** Windows native views are present
- **THEN** the generated host SHALL expose Windows view metadata and view
  lifecycle entry points
- **AND** non-Windows aggregators SHALL remain on the universal target
  framework without the Windows sidecar reference
```

- [ ] **Step 5: Remove transient change artifacts**

After requirements are merged into living specs, remove:

```powershell
git rm docs/changes/2026-07-09-windows-native-views/spec.md docs/changes/2026-07-09-windows-native-views/plan.md
```

- [ ] **Step 6: Verify docs and commit**

Run:

```powershell
git diff --check
rg "private-hostname|machine-specific|local absolute path|<windows-user>" docs/specs docs/README.md AGENTS.md
git add docs/specs docs/changes/2026-07-09-windows-native-views
git diff --cached --check
git commit -m "Document Windows native view support"
```

Expected: docs checks pass and transient change artifacts are removed from current-state docs.

---

## Self-Review Notes

- Spec coverage: Tasks 1-2 cover generated syntax and diagnostics; Tasks 3-5 cover Windows sidecar hosting and boundary; Tasks 6-7 cover desktop rendering; Task 8 covers verification; Task 9 merges the delta into living specs.
- Placeholder scan: no task uses `TBD`, `TODO`, or unspecified implementation steps. Task 4 explicitly says to add package references only if the Windows SDK build proves they are required; that is verification-driven, not an undefined requirement.
- Type consistency: `ExampleColorBox`, `ExampleColorBoxView`, `GeneratedViewDefinition`, `GeneratedViewPropDefinition`, `GeneratedViewPropKind.String`, `CreateView`, and `UpdateViewProp` are used consistently across generator, autolinking, sidecar, and example tasks.
- Boundary check: Windows-specific managed APIs live under `Expo.ModulesCore.Windows`; universal core only contains platform-neutral attributes and metadata records.
