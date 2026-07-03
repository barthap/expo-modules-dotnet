# Minimal Codec Expansion

## Goal

Expand the `Expo.ModulesCore` generated-binding codec surface enough for common
module argument and return shapes: enums, simple C# records, and
`Dictionary<string, T>` / `IReadOnlyDictionary<string, T>`.

This change keeps generated bindings direct-call, generated-code-inspectable,
and reflection-free at runtime. It also pulls the `Expo.JSI` object property
enumeration wrapper forward because dictionary decode needs a concrete
JavaScript object iteration use case.

## Scope

This change applies to:

- the low-level `Expo.JSI` object wrapper and native ABI for object property
  name enumeration
- `Expo.ModulesCore` codecs
- `Expo.ModulesCore.Generator` type modeling and generated codec emission
- generator tests and Hermes-backed module conversion tests
- the `managed-jsi-wrappers` and `modules-core-boundary` living specs

This change does not add ArrayBuffer, SharedObject, NativeState, HostObject,
events, async methods, module autolinking, platform adapter behavior, or
runtime hot-path reflection.

## Accepted Design

### Object Property Enumeration

`Expo.JSI` SHALL expose object own-property-name enumeration through opaque
handles and managed wrappers. Dictionary decode SHALL use own property names of
plain JavaScript objects and SHALL NOT traverse prototypes.

The managed wrapper SHALL materialize property names as managed strings before
returning them from the runtime access frame. The public managed return shape
SHALL be `IReadOnlyList<string>`. The implementation may return an array behind
that interface, but it SHALL preserve wrapper ownership rules and SHALL NOT
expose raw JSI property-name layouts to C#.

### Enum Codecs

The generator SHALL infer enum support from C# enum types. By default, generated
bindings SHALL use a string-backed enum codec that maps JavaScript strings to
C# enum names and encodes enum values back to strings.

An optional authored attribute MAY select string-backed or integer-backed enum
conversion explicitly. Integer-backed enum conversion SHALL map JavaScript
numbers to the enum underlying value and encode enum values back to JavaScript
numbers.

Invalid enum input SHALL fail through the normal managed conversion exception
path and become a catchable JavaScript `Error` at the host-function boundary.

### Record Codecs

The generator SHALL infer simple Expo Record support from C# record types.
Roslyn `record`, `record class`, and `record struct` forms SHALL be supported
when the record shape can be generated without runtime reflection.

Positional records SHALL decode by reading known object properties and invoking
the primary constructor. Record encode SHALL create a plain JavaScript object
and set known properties through their generated field codecs.

Non-positional records MAY be supported only when construction is obvious, such
as a parameterless constructor plus public `init` or `set` properties. Other
record shapes SHALL receive generator diagnostics instead of falling back to
runtime reflection, dynamic invocation, JSON, or `object?[]` dispatch.

Record field names SHALL initially follow C# member names. Naming overrides,
unknown-field validation, custom field converters, inheritance, cyclic record
graphs, and custom constructor selection are out of this slice.

### Dictionary Codecs

`Dictionary<string, T>` and `IReadOnlyDictionary<string, T>` SHALL map to plain
JavaScript objects when `T` has a generated codec.

Dictionary decode SHALL require a JavaScript object, enumerate own property
names, decode each value with `T`'s codec, and return a managed dictionary
shape appropriate for the authored parameter type. Dictionary encode SHALL
create a plain JavaScript object, iterate managed entries, encode each value
with `T`'s codec, and set each string key as a JavaScript property.

Dictionary keys SHALL be strings. `Map`, symbol keys, prototype properties, and
non-string key dictionaries are out of this slice.

### Generator Composition

The generator SHALL compose enum, record, nullable, array, and dictionary
codecs through compile-time codec expressions. If a type cannot be represented
by the supported codec graph, the generator SHALL report an authored-location
diagnostic and SHALL NOT silently skip the module or emit invalid source.

Generated module function bodies SHALL continue to decode arguments through
typed codecs, call authored methods directly, and encode return values through
typed codecs.

## Delta Requirements

### ADDED: Object Own-Property Names Are Exposed

`Expo.JSI` SHALL expose own-property-name enumeration for JavaScript objects
without exposing raw JSI layouts to managed code.

#### Scenario: Managed code enumerates object property names

- **GIVEN** a `JavaScriptObject` has own string-named properties
- **WHEN** managed code asks for own property names
- **THEN** the wrapper SHALL call the native ABI through opaque handles
- **AND** return managed strings that remain valid after the native call
- **AND** preserve object wrapper disposal and scoped-ref lifetime rules.

#### Scenario: Prototype properties exist

- **GIVEN** an object has inherited properties through its prototype chain
- **WHEN** managed code asks for own property names for dictionary conversion
- **THEN** inherited properties SHALL NOT be returned as dictionary keys.

### ADDED: Enums Convert Through Generated Codecs

`Expo.ModulesCore` SHALL support C# enum parameters and return values through
generated codec expressions.

#### Scenario: String-backed enum is used by default

- **GIVEN** a generated sync function accepts or returns a C# enum type
- **WHEN** no enum representation attribute is present
- **THEN** generated dispatch SHALL decode from JavaScript strings by enum name
- **AND** encode enum return values as JavaScript strings
- **AND** generated dispatch SHALL NOT use runtime reflection.

#### Scenario: Integer-backed enum is requested

- **GIVEN** an authored enum usage explicitly requests integer-backed
  conversion
- **WHEN** generated dispatch decodes or encodes that enum value
- **THEN** JavaScript numbers SHALL map to and from the enum underlying value.

#### Scenario: Invalid enum input is passed

- **GIVEN** a generated sync function expects an enum value
- **WHEN** JavaScript passes a string or number that cannot be converted to the
  target enum
- **THEN** the codec SHALL throw a managed conversion exception
- **AND** the host-function boundary SHALL expose it to JavaScript as a
  catchable `Error`.

### ADDED: Simple Records Convert Through Generated Codecs

`Expo.ModulesCore.Generator` SHALL infer simple C# records and emit
record-specific codecs.

#### Scenario: Positional record is decoded

- **GIVEN** a generated sync function accepts a positional `record`,
  `record class`, or `record struct`
- **WHEN** JavaScript passes a plain object with supported fields
- **THEN** generated dispatch SHALL read known properties by name
- **AND** decode each field through its generated codec
- **AND** invoke the record constructor directly.

#### Scenario: Record return value is encoded

- **GIVEN** a generated sync function returns a supported record type
- **WHEN** the authored method returns a record value
- **THEN** generated dispatch SHALL create a plain JavaScript object
- **AND** encode each supported record field through its generated codec
- **AND** set each generated field value by property name.

#### Scenario: Unsupported record shape is compiled

- **GIVEN** a record requires unsupported construction, unsupported fields,
  inheritance, cycles, custom field naming, or custom converter behavior
- **WHEN** the consuming project is compiled
- **THEN** the generator SHALL report a diagnostic at the authored type or
  member location when Roslyn provides one
- **AND** generated source SHALL NOT fall back to runtime reflection, dynamic
  invocation, JSON, or `object?[]` dispatch.

### ADDED: String-Key Dictionaries Convert Through JavaScript Objects

`Expo.ModulesCore` SHALL support string-key dictionary shapes through plain
JavaScript object conversion when the value type has a codec.

#### Scenario: Dictionary parameter is decoded

- **GIVEN** a generated sync function accepts `Dictionary<string, T>` or
  `IReadOnlyDictionary<string, T>`
- **AND** `T` has a generated codec
- **WHEN** JavaScript passes a plain object
- **THEN** generated dispatch SHALL enumerate the object's own property names
- **AND** decode each property value through `T`'s codec
- **AND** pass a managed dictionary value to the authored method.

#### Scenario: Dictionary return value is encoded

- **GIVEN** a generated sync function returns `Dictionary<string, T>` or
  `IReadOnlyDictionary<string, T>`
- **AND** `T` has a generated codec
- **WHEN** the authored method returns a dictionary value
- **THEN** generated dispatch SHALL create a plain JavaScript object
- **AND** encode each managed dictionary value through `T`'s codec
- **AND** set each managed string key as a JavaScript property.

#### Scenario: Unsupported dictionary shape is compiled

- **GIVEN** a generated sync function uses a dictionary with a non-string key
  type or an unsupported value type
- **WHEN** the consuming project is compiled
- **THEN** the generator SHALL report a compile-time diagnostic
- **AND** generated source SHALL NOT emit a fallback conversion path.

### MODIFIED: Sync Function Generation Uses Direct Calls

Generated sync function glue SHALL decode arguments, call authored methods
directly, and encode return values through typed helpers.

#### Scenario: Generated source uses expanded codecs

- **GIVEN** generated sync functions use supported enum, record, or dictionary
  signatures
- **WHEN** the generator emits provider and codec source
- **THEN** generated source SHALL remain readable generated C#
- **AND** generated dispatch SHALL call authored methods directly
- **AND** generated dispatch SHALL NOT use runtime reflection, dynamic
  invocation, JSON, or `object?[]` dispatch.

## Verification

Implementation SHALL verify this slice with:

- low-level `Expo.JSI` tests for object own-property-name enumeration and
  prototype exclusion
- generator tests for enum, record, dictionary, and unsupported-shape
  diagnostics
- Hermes-backed `Expo.ModulesCore.Tests` coverage for generated module
  conversion behavior
- `scripts/test-managed.sh`
- `scripts/format.sh --check --all`
