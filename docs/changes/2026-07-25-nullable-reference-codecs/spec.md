# Nullable Reference Codecs

## Goal

Add generated codec support for nullable reference-type values without
weakening the existing strict codecs for non-nullable references.

The generator will recognize an annotated nullable reference, select a
nullable wrapper or collection adapter, and preserve the existing codec for
the same non-nullable type. Ownership-bearing references remain excluded, and an
annotated use of one reports the existing diagnostic for the position it appears
in.

## Scope

### In scope

- Nullable `string`, `Uri`, `byte[]`, and positional reference-record values.
- Nullable `IReadOnlyList<T>`, `Dictionary<string, T>`, and
  `IReadOnlyDictionary<string, T>` containers.
- Nullable supported reference types in collection element and dictionary
  value positions.
- Parameters, return values, `Task<T>` results, optional arguments, `[JS]`
  properties, record fields, typed event payloads, and shared-object `[JS]`
  members.
- Reuse of the existing context-specific unsupported-type diagnostics for
  nullable ownership-bearing or runtime-context-bearing reference types.
- Direct codec tests, generator tests, and Hermes-backed generated-binding
  tests.

### Out of scope

- `List<T>`, because its non-nullable form has no generated codec.
- Nullable `JavaScriptValue`, `ArrayBuffer`,
  `JavaScriptCallback<TResult>`,
  `JavaScriptCallback<TArgs, TResult>`, `SharedObject`, `SharedRef<T>`, or a
  concrete `[ExpoSharedObject]` class.
- New nullable semantics for any other owned JSI wrapper.
- A new generator diagnostic code. The exclusions reuse the diagnostics the
  generator already reports for unsupported boundary types.
- Changes to the C ABI, `Expo.JSI`, or native bridge.
- Changes to nullable value-type behavior.

## Accepted design

### Annotation-driven selection

Only a reference type whose Roslyn symbol has
`NullableAnnotation.Annotated` uses nullable reference codec handling. The
generator strips the top-level annotation before resolving the inner codec.
It preserves annotations on nested type arguments and record fields.

A non-nullable reference and a reference from a disabled nullable context do
not use the nullable wrapper. Their current strict codec remains in place.
Nullish handling lives only in separate nullable wrappers and collection
adapters. Existing non-nullable codecs do not become null-tolerant.

### Standard nullable reference codec

`NullableReferenceCodec<T, TCodec>` composes a supported reference type with
its existing non-nullable codec:

```csharp
public readonly struct NullableReferenceCodec<T, TCodec>
    : IJavaScriptCodec<T?>
    where T : class
    where TCodec : IJavaScriptCodec<T>
```

Both decode overloads return C# `null` for JavaScript `null` or `undefined`.
For a non-null value, they delegate to `TCodec`. Encoding C# `null` creates
JavaScript `null`; encoding a non-null value delegates to `TCodec`.

This codec supports `string?`, `Uri?`, `byte[]?`, and nullable positional
reference records. `ByteArrayCodec` implements
`IJavaScriptCodec<byte[]>` so it can serve as an inner codec without changing
its conversion behavior.

### Nullable collection adapters

The existing array and dictionary helpers use special decode methods and do
not all implement `IJavaScriptCodec<T>`. Nullable containers therefore use
dedicated adapters:

- `NullableReadOnlyListCodec<T, TCodec>` for `IReadOnlyList<T>?`;
- `NullableDictionaryCodec<T, TCodec>` for `Dictionary<string, T>?`; and
- `NullableReadOnlyDictionaryCodec<T, TCodec>` for
  `IReadOnlyDictionary<string, T>?`.

Each adapter returns C# `null` for JavaScript `null` or `undefined`, encodes a
null container as JavaScript `null`, and delegates non-null conversion to the
existing collection helper. Element and value codecs resolve recursively, so
supported nullable references compose inside non-null and nullable
containers. Dictionary keys remain exactly `string`.

### Ownership-bearing exclusions

Nullable annotations are not supported on current generated-boundary types
whose conversion carries JSI ownership, retained callback state, shared-object
identity, or runtime-context state. This set is:

- `JavaScriptValue`;
- `ArrayBuffer`;
- `JavaScriptCallback<TResult>` and
  `JavaScriptCallback<TArgs, TResult>`;
- `SharedObject` and `SharedRef<T>`; and
- concrete `[ExpoSharedObject]` classes.

An annotated use of one of these types reports the diagnostic the generator
already uses for the position where the type appears: `EXPOJSI001` or
`EXPOJSI002` for a method parameter or return, `EXPOJSI007` for a record field,
`EXPOJSI008` for a callback, `EXPOJSI015` for a property, `EXPOJSI019` or
`EXPOJSI027` for an event payload, and `EXPOJSI023` for a shared-object
boundary. This change adds no new diagnostic code.

Codec resolution cannot see the member context. A diagnostic raised there could
not tell the author whether the offending nullable type is a parameter, a record
field, a property, or an event payload. The generator already has eight
context-specific codes for exactly that, and each caller already reports the
right one.

The generator emits no binding for the invalid member and does not fall through
to the non-nullable codec. Existing behavior for the same non-nullable type
remains unchanged.

### Required and optional arguments

A required nullable reference parameter decodes explicit JavaScript `null` or
`undefined` as C# `null`.

For an optional nullable reference parameter, omission or explicit JavaScript
`undefined` uses the authored C# default. Explicit JavaScript `null` decodes
as C# `null`. This matches the existing optional nullable value-type behavior.

### Existing nullable value types

`NullableCodec<T, TCodec>` remains constrained with `where T : struct` and
keeps its current decode and encode behavior. The nullable reference feature
does not replace it, widen it, or change its dispatch.

## Delta requirements

### Requirement: Nullable Reference Codec Selection Is Annotation-Driven

Generated binding analysis SHALL activate nullable reference handling only
when a reference type has `NullableAnnotation.Annotated`. It SHALL remove only
the top-level nullable annotation before resolving the existing inner codec.
It SHALL preserve nested nullable annotations.

Nullish handling SHALL exist only in the nullable reference wrapper and
nullable collection adapters. The generator SHALL NOT make an existing
non-nullable codec accept JavaScript `null` or `undefined`.

#### Scenario: Annotated string selects a nullable wrapper

- **GIVEN** nullable annotations are enabled and a generated boundary declares
  `string?`
- **WHEN** the generator resolves its codec
- **THEN** it SHALL compose `StringCodec` through the nullable reference codec
- **AND** it SHALL resolve the inner `string` with a non-annotated top-level
  symbol

#### Scenario: Non-nullable string stays strict

- **GIVEN** a generated method has a non-nullable `string` parameter
- **WHEN** JavaScript passes `null` or `undefined`
- **THEN** `StringCodec` SHALL reject the value before authored code runs
- **AND** generated dispatch SHALL NOT route the parameter through a nullable
  wrapper

#### Scenario: Oblivious reference keeps its current codec

- **GIVEN** nullable annotations are disabled and a generated boundary declares
  an unannotated `string`
- **WHEN** the generator resolves its codec
- **THEN** `NullableAnnotation.None` SHALL NOT activate nullable reference
  handling
- **AND** the generator SHALL use plain `StringCodec`

### Requirement: Supported Nullable Reference Values Use Separate Codecs

Generated bindings SHALL support nullable `string`, `Uri`, `byte[]`, and
positional reference-record values. Both scoped and owned JavaScript value
decode paths SHALL treat JavaScript `null` and `undefined` as C# `null`.
Encoding C# `null` SHALL produce JavaScript `null`. Non-null conversion SHALL
delegate to the existing codec for the same non-nullable type.

#### Scenario: Nullish input decodes to null

- **GIVEN** a supported nullable reference codec receives JavaScript `null` or
  `undefined`
- **WHEN** either decode overload runs
- **THEN** it SHALL return C# `null`
- **AND** it SHALL NOT call the inner codec

#### Scenario: Null output encodes as JavaScript null

- **GIVEN** a supported nullable reference value is C# `null`
- **WHEN** generated binding glue encodes it
- **THEN** the result SHALL be JavaScript `null`
- **AND** it SHALL NOT be JavaScript `undefined`

#### Scenario: Non-null value uses the existing codec

- **GIVEN** a supported nullable reference value is not null
- **WHEN** it crosses the generated boundary in either direction
- **THEN** conversion SHALL delegate to the existing non-nullable codec
- **AND** that codec's non-null behavior SHALL remain unchanged

### Requirement: Nullable Collection Containers And Contents Compose Recursively

Generated bindings SHALL support nullable
`IReadOnlyList<T>`, `Dictionary<string, T>`, and
`IReadOnlyDictionary<string, T>` containers when the nested type has a
supported codec. They SHALL also support nullable reference elements and
values wherever the nested reference type is supported. Dictionary keys SHALL
remain exactly `string`.

`List<T>` SHALL remain unsupported because its non-nullable form has no
generated codec.

#### Scenario: Nullable collection container round-trips null

- **GIVEN** a generated boundary declares one of the supported nullable
  collection containers
- **WHEN** JavaScript supplies `null` or `undefined`
- **THEN** decoding SHALL return C# `null`
- **WHEN** C# supplies a null container
- **THEN** encoding SHALL return JavaScript `null`

#### Scenario: Nullable collection contents preserve null

- **GIVEN** a supported list element or dictionary value type is an annotated
  nullable reference
- **WHEN** a non-null container crosses the generated boundary
- **THEN** nullish nested values SHALL decode as C# `null`
- **AND** C# null elements or values SHALL encode as JavaScript `null`
- **AND** non-null elements or values SHALL use their existing inner codec

#### Scenario: Nested nullable container composes

- **GIVEN** a supported collection contains another supported nullable
  collection
- **WHEN** the generator resolves the nested codecs
- **THEN** it SHALL preserve each nullable container and element annotation
- **AND** it SHALL emit compile-time codec composition without reflection

#### Scenario: List remains unsupported

- **GIVEN** a generated boundary declares `List<T>` or `List<T>?`
- **WHEN** the generator analyzes the type
- **THEN** it SHALL report the existing unsupported-type diagnostic
- **AND** this change SHALL NOT add a `List<T>` codec

### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

The generator SHALL reject a nullable annotation applied to `JavaScriptValue`,
`ArrayBuffer`, a `JavaScriptCallback<...>`, a `SharedObject` or `SharedRef<T>`
boundary, or a concrete `[ExpoSharedObject]` class. It SHALL report the
diagnostic it already uses for the position where the annotated type appears.
It SHALL NOT add a new diagnostic code, and it SHALL NOT raise a context-free
diagnostic from codec resolution.

Codec resolution does not know the member context, so a diagnostic raised there
could not name the position of the offending type. The generator already has
eight context-specific codes for that, and each caller already reports the right
one.

The generator SHALL emit no binding for the invalid member and SHALL NOT fall
through to that type's non-nullable codec. It SHALL NOT add a second,
context-free diagnostic beside the context-specific one. Existing layered
reporting stays as it is: a rejected record field already reports both its own
`EXPOJSI007` and the containing member's diagnostic.

The same non-nullable types SHALL retain their current supported or diagnostic
behavior. Existing translation SHALL also stay as it is: a member declared on a
shared-object class already reports its `EXPOJSI001`, `EXPOJSI002`,
`EXPOJSI008`, and `EXPOJSI015` results as `EXPOJSI023`, and this change SHALL
NOT alter that.

#### Scenario: Excluded nullable parameter or return reports the method codes

- **GIVEN** a generated method declares `JavaScriptValue?` or `ArrayBuffer?` as
  a parameter or as its return type
- **WHEN** the generator analyzes the method
- **THEN** it SHALL report `EXPOJSI001` for the parameter position and
  `EXPOJSI002` for the return position
- **AND** it SHALL NOT emit a binding or select `JavaScriptValueCodec` or
  `ArrayBufferCodec`

#### Scenario: Excluded nullable record field reports the record code

- **GIVEN** a generated positional record declares a field whose type is an
  excluded nullable reference
- **WHEN** the generator analyzes the record
- **THEN** it SHALL report `EXPOJSI007` naming the record and the field
- **AND** it SHALL NOT emit a record codec that treats the field as
  non-nullable

#### Scenario: Nullable callback reports the callback code

- **GIVEN** a generated method declares an annotated
  `JavaScriptCallback<...>?` parameter
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL report `EXPOJSI008`
- **AND** it SHALL NOT generate retained callback conversion

#### Scenario: Excluded nullable property reports the property code

- **GIVEN** a `[JS]` property declares an excluded nullable reference type
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI015`
- **AND** it SHALL emit neither accessor

#### Scenario: Excluded nullable event payload reports the event codes

- **GIVEN** a module typed event or a shared-object typed event declares an
  excluded nullable payload type
- **WHEN** the generator analyzes the event property
- **THEN** it SHALL report `EXPOJSI019` for a module event and `EXPOJSI027` for
  a shared-object event
- **AND** it SHALL NOT emit event glue for that property

#### Scenario: Nullable shared-object boundary keeps its existing code

- **GIVEN** a generated boundary declares an annotated `SharedObject`,
  `SharedRef<T>`, or concrete `[ExpoSharedObject]` type
- **WHEN** shared-object boundary analysis reaches the annotated type
- **THEN** it SHALL report `EXPOJSI023`, the code existing tests already fix
- **AND** it SHALL NOT select a polymorphic, base-type, or concrete
  shared-object codec
- **AND** it SHALL NOT weaken the existing non-nullable shared-object identity
  or lifetime rules

#### Scenario: Excluded nullable type is nested

- **GIVEN** an excluded nullable reference appears in a record field,
  collection element, or dictionary value
- **WHEN** codec analysis reaches that nested annotation
- **THEN** the containing member SHALL report the diagnostic for its own
  position
- **AND** the containing binding SHALL not be emitted

#### Scenario: Rejected nullable type does not fall back

- **GIVEN** codec resolution has handled an annotated excluded reference
- **WHEN** control returns to the analysis that asked for the codec
- **THEN** dispatch SHALL stop instead of continuing to that type's
  non-nullable codec branch
- **AND** the generator SHALL report only the existing context-specific
  diagnostics and SHALL NOT emit a binding

### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults

Generated optional nullable reference parameters SHALL use the authored C#
default for omission or explicit JavaScript `undefined`. Explicit JavaScript
`null` SHALL decode as C# `null`. Required nullable reference parameters SHALL
decode explicit JavaScript `undefined` as C# `null`.

#### Scenario: Optional nullable reference is omitted

- **GIVEN** a generated method declares an optional nullable reference
  parameter with a C# default
- **WHEN** JavaScript omits the argument or passes explicit `undefined`
- **THEN** generated dispatch SHALL pass the authored C# default

#### Scenario: Optional nullable reference receives explicit null

- **GIVEN** a generated method declares an optional nullable reference
  parameter with a non-null C# default
- **WHEN** JavaScript passes explicit `null`
- **THEN** generated dispatch SHALL pass C# `null`
- **AND** it SHALL NOT substitute the authored default

#### Scenario: Required nullable reference receives undefined

- **GIVEN** a generated method declares a required nullable reference
  parameter
- **WHEN** JavaScript passes explicit `undefined`
- **THEN** generated dispatch SHALL pass C# `null`

### Requirement: Nullable Reference Codecs Apply Across Generated Binding Surfaces

Supported nullable reference codecs SHALL apply to method parameters,
synchronous returns, `Task<T?>` results, optional arguments, readable and
writable `[JS]` properties, positional record fields, supported collection
container and nested positions, typed event payloads, and shared-object
constructors and `[JS]` members.

This requirement applies to safe nullable reference values used by a
shared-object member. It does not make a shared-object instance type nullable.

#### Scenario: Generated return paths encode null

- **GIVEN** a synchronous generated method or `Task<T?>` result returns C#
  `null` for a supported nullable reference type
- **WHEN** generated glue encodes the result
- **THEN** JavaScript SHALL receive `null`
- **AND** an async method's promise SHALL resolve with `null`

#### Scenario: Nullable property round-trips

- **GIVEN** a readable and writable `[JS]` property uses a supported nullable
  reference type
- **WHEN** JavaScript assigns and reads null and non-null values
- **THEN** the setter and getter SHALL use the same nullable codec
- **AND** null SHALL round-trip as JavaScript `null`

#### Scenario: Nullable record field round-trips

- **GIVEN** a generated positional record has a supported nullable reference
  field
- **WHEN** the record crosses the generated boundary
- **THEN** the field SHALL preserve null and non-null values through its
  generated field codec

#### Scenario: Event and shared-object members use safe nullable values

- **GIVEN** a typed event payload or shared-object `[JS]` member uses a
  supported nullable reference value
- **WHEN** generated binding or event glue converts the value
- **THEN** it SHALL use the same nullable codec composition as module methods
- **AND** C# `null` SHALL encode as JavaScript `null`

### Requirement: Nullable Value-Type Codec Behavior Remains Unchanged

`NullableCodec<T, TCodec>` SHALL remain constrained with `where T : struct`.
It SHALL keep its current scoped decode, owned decode, non-null delegation,
and JavaScript `null` encoding behavior. Nullable reference dispatch SHALL not
replace or alter nullable value-type dispatch.

#### Scenario: Nullable value type still uses NullableCodec

- **GIVEN** a generated boundary declares `int?` or another supported nullable
  value type
- **WHEN** the generator resolves its codec
- **THEN** it SHALL use `NullableCodec<T, TCodec>`
- **AND** it SHALL NOT use `NullableReferenceCodec<T, TCodec>`

#### Scenario: Existing nullable value conversion is unchanged

- **GIVEN** a supported nullable value type crosses the generated boundary
- **WHEN** its existing codec decodes nullish input or encodes a null value
- **THEN** it SHALL preserve the behavior that existed before this change

### Requirement: Nullable Reference Bindings Remain Generated And Portable

Nullable reference classification and codec composition SHALL happen at build
time. Generated glue SHALL remain NativeAOT-compatible and SHALL not use
runtime reflection, dynamic invocation, JSON conversion, a new C ABI entry,
or a platform-specific dependency.

#### Scenario: Nullable binding is generated

- **GIVEN** a supported nullable reference appears in an authored generated
  boundary
- **WHEN** the project compiles for HostFXR or NativeAOT loading
- **THEN** the generator SHALL emit direct typed codec calls
- **AND** runtime dispatch SHALL not inspect nullable metadata through
  reflection

## Verification

Implementation verification SHALL cover:

- direct codec tests for both decode overloads, null encoding, non-null
  delegation, `byte[]`, and all three nullable collection adapters;
- generator source tests for supported types, recursive collection
  composition, every generated binding surface, strict non-nullable and
  oblivious references, existing nullable value types, and the diagnostic
  reported for an excluded nullable type in each position;
- Hermes-backed tests for required and optional arguments, strict
  non-nullable rejection, sync and async returns, properties, records,
  collections, `byte[]`, typed events, and shared-object members;
- the generator test project;
- the full Hermes-backed managed suite; and
- `scripts/format.sh --check --all`.
