# Windows Native Views

## Purpose

This spec defines the Windows-only native view sidecar for C# Expo Modules v2.
The sidecar consumes platform-neutral generated view metadata and registers
React Native Windows Fabric view components while preserving the portable core
boundary:

```text
C++ owns JSI mechanics.
C# owns module logic.
A C ABI with opaque handles connects them.
```

The first supported view host is composition-backed. XAML control hosting,
cross-platform view adapters, commands, and native view events are out of scope
for this slice.

The current authored C# syntax is provisional. Upstream Expo Modules has not
settled a final C# view-authoring shape, so this spec documents the Windows
proof contract rather than a permanent public API. The initial proof attaches
`[View]` to an `[ExpoModule]` class and discovers `[Prop]` methods from that
same class. That shape is acceptable for the first end-to-end Windows view
proof, but it intentionally leaves room for a future design where view
definitions or view managers are separate classes registered by, or next to,
Expo modules.

## Requirements

### Requirement: Windows Sidecar Owns Native View Hosting

Windows native view hosting SHALL live in a Windows-only sidecar package or
behind a clearly Windows-scoped compilation boundary. The sidecar MAY depend on
React Native Windows, WinRT projection packages, and Windows composition APIs.
Portable core packages SHALL NOT depend on the sidecar package.

#### Scenario: Windows sidecar registers generated views
- **GIVEN** the generated app-level aggregator exposes view metadata
- **WHEN** the Windows package creates its RNW package
- **THEN** it SHALL register generated view components with React Native
  Windows Fabric
- **AND** it SHALL create composition-backed managed view instances for the
  first proof

#### Scenario: Portable core is reused outside Windows
- **GIVEN** an app builds `Expo.JSI` and `Expo.ModulesCore` for a non-Windows
  target
- **WHEN** Windows native view support exists in the repository
- **THEN** the portable managed packages SHALL remain headless and reusable
  without RNW, WinUI, XAML, or Windows composition dependencies

### Requirement: Generated Metadata Drives Managed Views

The Windows sidecar SHALL consume generated metadata for `[View]` and `[Prop]`
declarations. It SHALL NOT discover authored view modules through runtime
module scanning as the ordinary dispatch path. Generated Windows metadata
entry points SHALL expose metadata through typed native calls such as indexed
view and prop counts, caller-owned string buffers, and numeric prop kinds. The
sidecar SHALL NOT consume generated view metadata through JSON strings,
serialized anonymous objects, or dynamic payloads.

#### Scenario: React creates a generated view component
- **GIVEN** a C# module declares a generated view component and prop setters
- **WHEN** React Native Windows creates that component
- **THEN** the sidecar SHALL create the authored managed view instance
- **AND** RNW prop updates SHALL call the generated prop dispatch path
- **AND** generated prop dispatch SHALL call the authored prop setter directly

#### Scenario: View metadata is unavailable
- **GIVEN** the generated aggregator has no view metadata
- **WHEN** the Windows sidecar registers native components
- **THEN** it SHALL skip view registration or report an actionable installer
  error
- **AND** it SHALL NOT crash because metadata is absent

#### Scenario: Windows sidecar loads view metadata
- **GIVEN** a Windows aggregator exposes generated view definitions
- **WHEN** the sidecar loads metadata
- **THEN** it SHALL call typed metadata entry points
- **AND** generated code SHALL NOT call JSON serializers for view metadata

### Requirement: Prop Conversion Reuses Shared Codec Semantics

View prop conversion SHOULD reuse the existing generated type-conversion model
where practical instead of inventing an unrelated prop-only conversion system.
The initial Windows proof supports only string props, but that limitation is a
temporary proof boundary. Future prop support SHOULD expand through a deliberate
subset of the same codec families used by generated module functions, such as
primitive values, nullable values, string-backed or number-backed enums, record
types, arrays, and dictionaries when those shapes are representable as native
view props.

The prop input layer MAY expose a dedicated prop codec interface because RNW
and Fabric view props arrive as native prop values rather than
`JavaScriptValue` function arguments. Existing codecs SHOULD be able to opt into
that prop interface alongside the ordinary JavaScript codec interface when the
conversion semantics are the same. The adapter boundary SHALL stay narrow:
transport differences between JSI function calls and Fabric prop updates may be
handled separately, but semantic conversion rules SHOULD remain shared.

Prop conversion SHALL NOT fall back to a JSON or string tunnel for structured
props. Rich view libraries such as `@expo/ui` need prop shapes including
colors, dates or date ranges, enum-like style values, arrays, nested records,
modifier/config objects, picker values, and icon or asset references. Those
use cases are a reason to design a typed prop-conversion subset, not a reason
to add per-prop ad hoc parsing.

#### Scenario: A prop type is also supported by function codecs
- **GIVEN** a view prop uses a type already supported by generated module
  function codecs
- **WHEN** the prop input can represent that type without losing information
- **THEN** generated prop dispatch SHOULD reuse the same conversion semantics
- **AND** the implementation MAY do so through a prop-facing codec interface
  implemented by the existing codec type

#### Scenario: A prop requires a view-specific transport adapter
- **GIVEN** RNW provides a view prop as a native Fabric prop value
- **AND** the equivalent module function codec expects a `JavaScriptValue`
- **WHEN** the generator emits prop conversion
- **THEN** the transport adapter MAY differ from function-argument decoding
- **AND** the supported .NET type, nullability, enum, and record semantics
  SHOULD remain aligned with the shared codec model

#### Scenario: A rich UI package passes structured props
- **GIVEN** a native view package uses props for colors, dates, enum-like style
  values, arrays, nested records, picker values, or icon and asset references
- **WHEN** those prop shapes are added to Windows native view support
- **THEN** they SHOULD be added as typed prop codec support
- **AND** the implementation SHALL NOT require authors to serialize those
  values to JSON strings before passing them to managed prop setters

### Requirement: Authored View Syntax Is Provisional

The current `[View]` and `[Prop]` authoring surface SHOULD be treated as a
temporary proof shape. Documentation, tests, and examples MAY use the current
module-attached form while Windows native views are still experimental, but
they SHALL NOT imply that this shape is the final cross-platform Expo Modules
C# view API.

Future syntax SHOULD evaluate separating view registration from Expo module
instances. A future design MAY support separate view-definition or view-manager
classes, MAY allow a module to own or register multiple views, and MAY dispatch
props without instantiating the Expo module solely to update a view. Until that
design is accepted, the generator and sidecar SHALL keep the junction points
small so this syntax can change without rewriting the Windows host boundary.

#### Scenario: Example uses the temporary module-attached shape
- **GIVEN** an example module declares `[View]` on the `[ExpoModule]` class
- **AND** declares `[Prop]` methods on that same class
- **WHEN** developers read the Windows native view docs
- **THEN** the docs SHALL identify this as the initial proof shape
- **AND** the docs SHALL note that separate view definition or view manager
  classes remain a likely future direction

#### Scenario: A module needs multiple views
- **GIVEN** an Expo module needs to expose two or more native view components
- **WHEN** the current proof syntax feels awkward or ambiguous
- **THEN** that pressure SHALL be treated as evidence for revisiting the
  authoring API
- **AND** the Windows sidecar metadata boundary SHOULD remain compatible with a
  future generator model that emits multiple view definitions per module or
  emits view definitions independently from modules

### Requirement: View Operations Use Runtime-Scoped Context

Windows view operations SHALL use the managed runtime context associated with
the component's RNW React context. The installer SHALL store the active managed
runtime context in the RNW context property bag using an ABI-safe value, and the
sidecar SHALL read that property from the component context when creating views
or dispatching props.

#### Scenario: Multiple runtime contexts exist in one process
- **GIVEN** two RNW contexts have separate managed runtime contexts
- **WHEN** a view in one context receives a prop update
- **THEN** the sidecar SHALL dispatch the prop with that view's context handle
- **AND** it SHALL NOT use a process-global current-context export

### Requirement: Desktop App Renders A Custom Windows View

The Windows desktop app SHALL render a custom native view backed by authored C#
module code.

#### Scenario: Desktop app renders the example view
- **GIVEN** `apps/desktop-app` runs on Windows with the dotnet aggregator staged
- **WHEN** React renders the example view component
- **THEN** RNW SHALL host a native composition visual created by managed C#
  code
- **AND** changing the React prop SHALL update the native visual through the
  generated `[Prop]` dispatch path

### Requirement: Windows HostFXR Staging Includes Sidecar Dependencies

When Windows native view support is linked through HostFXR, the generated host
build and staging path SHALL include the transitive managed dependency closure
required by the Windows sidecar.

#### Scenario: Windows sidecar is staged for hostfxr
- **GIVEN** the generated Windows aggregator references the Windows sidecar
- **WHEN** the autolinking CLI builds and stages the aggregator for
  `--platform windows --mode hostfxr`
- **THEN** the staged `windows/Managed` directory SHALL include the generated
  host assembly, runtime configuration, deps file, transitive managed
  dependency assemblies, and platform `nethost` runtime library
- **AND** WinRT and Windows App SDK projection assemblies needed by the sidecar
  SHALL be present in the staged dependency closure

## Verification

Windows native view changes SHOULD be verified with:

- generator tests for generated view metadata and invalid view declarations;
- autolinking tests for Windows aggregator generation and dependency staging;
- `pnpm --filter desktop-app typecheck`;
- React Native Windows autolinking check for `apps/desktop-app`;
- a Windows desktop build and launch path;
- runtime confirmation that the desktop app renders the custom native view and
  prop updates reach the managed `[Prop]` dispatch path.
