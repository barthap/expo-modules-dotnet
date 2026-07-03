# Generator Hardening

## Goal

Harden the `Expo.ModulesCore.Generator` source generator so synchronous module
bindings have a stable generated-provider contract, actionable diagnostics, and
a maintainable emission structure before the supported codec surface grows.

## Scope

This change applies to the Roslyn generator, its tests, and the current
`modules-core-boundary` living spec. It keeps generated bindings direct-call,
library-local, and reflection-free.

This change does not add async methods, events, autolinking, package metadata,
records, dictionaries, enums, integer codecs, platform adapter behavior, or ABI
functions.

The implementation plan may include `void` and nullable primitive codecs only
if generator hardening stays local to model collection, diagnostics, emission,
and tests. If hardening requires broader generator restructuring, codec work
stays out of this slice.

## Accepted Design

### Generator Contract

The generator SHALL continue to emit one deterministic provider for modules in
the current compilation. The provider SHALL expose:

- `Register(DotnetRuntimeContext context)`
- `Register(DotnetRuntimeContext context, JavaScriptObject modules)`

The default overload SHALL use the context-owned default dotnet modules object.
The explicit overload SHALL install generated functions under the supplied
modules object. Generated function bodies SHALL decode arguments through typed
codecs, call authored methods directly, and encode returns through typed codecs.

### Diagnostics

The generator SHALL report compile-time diagnostics for authored shapes that it
does not support. Diagnostics SHALL identify the authored location when Roslyn
provides one and include enough context for the author to fix the signature.

Required unsupported shapes:

- unsupported parameter types
- unsupported return types
- unsupported module constructors
- duplicate module names in one compilation
- duplicate exported JavaScript function names within one module
- static `[JS]` methods
- generic `[JS]` methods
- overloaded exported JavaScript names

Unsupported shapes SHALL fail the consuming compilation instead of silently
skipping affected modules or emitting invalid generated C#.

### Source Emission

The generator MAY replace line-by-line `StringBuilder.AppendLine` emission with
small raw-string-template emitter helpers. This is allowed only when it serves
the generator hardening work and keeps model collection, diagnostics, and source
emission separate.

The emitter cleanup SHALL avoid T4, custom template engines, and
`SyntaxFactory`-heavy generated-source construction for this slice.

Generated source tests SHALL assert semantic contract points rather than every
whitespace detail.

### Generated Output Inspection

The existing generated-output inspection guidance for consuming projects SHALL
remain accurate. If provider shape or generated-file naming changes in a way
that affects authors, update the authoring documentation in the same slice.

## Delta Requirements

### MODIFIED: Generated Providers Are Library-Local

The Roslyn generator SHALL emit one deterministic provider for modules in the
current compilation.

#### Scenario: Provider shape is stable

- **GIVEN** a library project declares at least one `[ExpoModule]`
- **WHEN** the project is compiled with `Expo.ModulesCore.Generator`
- **THEN** generated source SHALL include a deterministic provider name derived
  from the current compilation
- **AND** the provider SHALL expose `Register(DotnetRuntimeContext context)`
- **AND** the provider SHALL expose
  `Register(DotnetRuntimeContext context, JavaScriptObject modules)`
- **AND** generated registration SHALL use `DotnetRuntimeContext` module
  instances
- **AND** generated registration SHALL NOT require runtime reflection.

### ADDED: Unsupported Authored Shapes Fail With Diagnostics

The Roslyn generator SHALL reject unsupported module and function shapes with
diagnostics before generated source reaches runtime behavior.

#### Scenario: Unsupported function signature is compiled

- **GIVEN** a `[JS]` method declares an unsupported parameter or return type
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a compile-time diagnostic
- **AND** the diagnostic SHALL include the method name, authored type, and
  relevant parameter name when applicable.

#### Scenario: Unsupported module shape is compiled

- **GIVEN** a module cannot be constructed by the generated provider
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a compile-time diagnostic
- **AND** generated code SHALL NOT rely on runtime reflection to create the
  module.

#### Scenario: Ambiguous exported names are compiled

- **GIVEN** two generated modules have the same exported module name, or two
  generated functions in one module have the same exported JavaScript name
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a compile-time diagnostic
- **AND** the duplicate names SHALL NOT be resolved by source order.

#### Scenario: Unsupported method shape is compiled

- **GIVEN** a `[JS]` method is static, generic, or otherwise cannot be emitted
  as a direct instance call
- **WHEN** the project is compiled
- **THEN** the generator SHALL report a compile-time diagnostic
- **AND** generated source SHALL NOT emit a fallback dispatch path.

### MODIFIED: Sync Function Generation Uses Direct Calls

Generated sync function glue SHALL decode arguments, call authored methods
directly, and encode return values through typed helpers.

#### Scenario: Generated source is emitted

- **GIVEN** generated sync functions use supported signatures
- **WHEN** the generator emits provider source
- **THEN** the emitted source SHALL remain readable generated C#
- **AND** emission helpers SHALL preserve the direct-call contract
- **AND** the generated source SHALL compile without source-order-dependent
  collision behavior.

## Verification

Implementation SHALL verify this slice with:

- generator unit tests for provider shape and diagnostics
- managed Hermes-backed tests when runtime-visible generated behavior changes
- `scripts/test-managed.sh`
- `scripts/format.sh --check --all`
