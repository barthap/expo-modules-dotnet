# Windows Native Views Delta Spec

## Goal

Add a Windows-only native view slice for Expo Modules v2 authored in C# while
preserving the portable core boundary:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

The first proof renders a composition-backed React Native Windows Fabric view in
`apps/desktop-app`. XAML control hosting, cross-platform view adapters, commands,
and native view events are out of scope for this slice.

## Scope

This change adds:

- generated v2 view authoring syntax such as `[View]` and `[Prop]`;
- platform-neutral view metadata emitted by the existing library-local generator;
- a Windows sidecar package that consumes generated metadata and registers RNW
  Fabric view components;
- a composition-backed example native view in `packages/example-module`;
- a desktop app screen that renders the custom view on Windows.

This change does not add RNW, WinUI, AppKit, XAML, or host packaging references
to `Expo.JSI` or `Expo.ModulesCore`. The universal managed packages remain
headless and reusable by non-Windows hosts.

## Accepted Design

### Architecture

`Expo.ModulesCore` owns only the platform-neutral declaration and metadata
surface:

- `[View]` marks one generated module as exposing a native view component.
- `[Prop]` marks authored prop setter methods for that view.
- generated provider code registers ordinary module functions as it does today
  and also exposes view metadata through a generated, platform-neutral contract.

The Windows implementation lives behind a sidecar boundary. The sidecar owns:

- RNW package registration;
- Fabric component registration;
- composition visual creation;
- prop delivery from RNW to managed view instances;
- layout updates;
- deterministic view teardown.

The first view host is composition-backed. Managed Windows view classes create
`Microsoft.UI.Composition.Visual` instances from a RNW-provided compositor. This
matches the previous working proof while avoiding XAML-root hosting complexity.

### Authoring Shape

The intended authoring shape is attribute-first and generator-backed:

```csharp
[ExpoModule("ExampleModule")]
[View("ExampleColorBox")]
public sealed partial class ExampleMathModule : Module
{
  [Prop("color")]
  public void SetColor(ExampleColorBoxView view, string? color)
  {
    view.Color = color;
  }
}
```

Prop setters are direct generated calls. They SHALL NOT use
`MethodInfo.Invoke`, `Delegate.DynamicInvoke`, `object?[]`, JSON, or runtime
module scanning as the ordinary v2 dispatch path.

### Sidecar Boundary

The preferred package boundary is `packages/expo-modules-dotnet-windows`.
It may depend on `expo-modules-dotnet`, React Native Windows, WinRT projection
packages, and Windows composition APIs. The portable core packages SHALL NOT
depend on the sidecar package.

The sidecar may be feature-flagged for view registration so existing Windows
module installation can remain stable while the native-view slice matures. The
flag must live in the Windows package or Windows native project, not in
`Expo.JSI` or `Expo.ModulesCore`.

### Data Flow

1. A C# module project declares `[ExpoModule]`, `[View]`, and `[Prop]`.
2. The Roslyn generator emits module registration plus view metadata for the
   current compilation.
3. Dotnet autolinking generates the app-level aggregator that links module
   providers.
4. On Windows, the sidecar loads the generated aggregator and reads the
   platform-neutral view metadata.
5. The sidecar registers each view as a RNW Fabric component.
6. React Native creates the component from JS.
7. RNW passes props and layout to the sidecar.
8. The sidecar creates or updates the managed Windows view instance and its
   composition visual.
9. RNW destroys the component; the sidecar tears down the managed view instance
   and releases retained Windows composition state.

### Error Handling

Unsupported authored view shapes fail at build time with generator diagnostics.
Examples include duplicate view component names, duplicate prop names on the
same view, prop setters with unsupported value types, static or generic prop
setters, and prop setters that do not accept the generated view instance plus
one prop value.

Runtime errors in the Windows sidecar SHALL be surfaced through Windows debug
logging and native installer diagnostics. Missing managed view metadata SHALL
not crash the app; it should skip view registration or report an actionable
installer error depending on where the failure occurs.

### Testing And Verification

Generator tests SHALL cover:

- generated view metadata for `[View]` and `[Prop]`;
- invalid view and prop shapes;
- no runtime reflection or dynamic invocation in generated prop dispatch.

Core boundary checks SHALL cover:

- no Windows, RNW, WinUI, AppKit, or XAML references in `Expo.JSI`;
- no Windows, RNW, WinUI, AppKit, or XAML references in `Expo.ModulesCore`;
- no hot-path reflection or JSON dispatch in generated v2 module/view code.

Windows proof verification SHOULD include:

- `pnpm --filter desktop-app typecheck`;
- RNW autolinking check for `apps/desktop-app`;
- a Windows desktop build path such as `react-native run-windows --no-packager
  --no-launch` or MSBuild when the local environment has the required Windows
  toolchain;
- manual or automated confirmation that `apps/desktop-app` renders the custom
  native view on Windows.

## Delta Requirements

### ADDED Requirement: Generated View Syntax Is Non-Inert

`Expo.ModulesCore` SHALL expose view authoring attributes only when the Roslyn
generator consumes them.

#### Scenario: View-backed module is compiled
- **GIVEN** a C# module declares `[ExpoModule]`, `[View]`, and one or more
  `[Prop]` methods
- **WHEN** the project is compiled
- **THEN** the generator SHALL emit platform-neutral view metadata
- **AND** the generated provider SHALL expose that metadata without runtime
  module scanning

#### Scenario: Invalid view syntax is compiled
- **GIVEN** a module declares a duplicate component name, duplicate prop name,
  unsupported prop type, or unsupported prop method shape
- **WHEN** the project is compiled
- **THEN** the generator SHALL report an actionable diagnostic
- **AND** generated code SHALL NOT silently skip the invalid view declaration

### ADDED Requirement: Windows Sidecar Owns Native View Hosting

Windows native view hosting SHALL live in a Windows-only sidecar package or
behind a clearly Windows-scoped compilation boundary.

#### Scenario: Windows sidecar registers generated views
- **GIVEN** the generated app-level aggregator exposes view metadata
- **WHEN** the Windows package creates its RNW package
- **THEN** it SHALL register generated view components with React Native Windows
  Fabric
- **AND** it SHALL create composition-backed managed view instances for the
  first proof

#### Scenario: Portable core is built
- **GIVEN** `Expo.JSI` and `Expo.ModulesCore` are compiled outside Windows
- **WHEN** view syntax support is present
- **THEN** neither package SHALL require RNW, WinUI, AppKit, XAML, or Windows
  composition references

### ADDED Requirement: Desktop App Renders A Custom Windows View

The Windows desktop app SHALL render a custom native view backed by authored C#
module code.

#### Scenario: Desktop app renders the example view
- **GIVEN** `apps/desktop-app` runs on Windows with the dotnet aggregator staged
- **WHEN** React renders the example view component
- **THEN** RNW SHALL host a native composition visual created by managed C# code
- **AND** changing the React prop SHALL update the native visual through the
  generated `[Prop]` dispatch path

## Self-Review

- No placeholders or unresolved requirements remain.
- The spec keeps Windows composition and RNW dependencies in the sidecar
  boundary.
- The first proof is intentionally composition-backed; XAML control hosting,
  commands, and view events are excluded to keep the slice focused.
- The generator may own platform-neutral metadata, but runtime native view
  hosting is explicitly Windows-sidecar owned.
