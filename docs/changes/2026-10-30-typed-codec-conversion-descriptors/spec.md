# Typed Codec Conversion Descriptors

## Goal

The generator stops reading conversion policy out of codec-expression text.
Codec resolution returns a typed `ExpoCodecDescriptor`, generator models store
it, and analysis and emission switch on its fields. This is roadmap item 029.
Plans 030–033 extend this descriptor instead of adding more string checks.

The typed fields also close one defect class. Today some codec combinations
emit generated C# that does not compile, which the living spec already
forbids ("Unsupported shapes SHALL fail the consuming compilation instead of
... emitting invalid generated C#"). After this change:

- non-null `IReadOnlyList<T>`, `Dictionary<string, T>`, and
  `IReadOnlyDictionary<string, T>` compose inside other collections, nullable
  wrappers, callback types, record fields, and typed event payloads, as the
  module authoring guide already says they do;
- `ArrayBuffer` and `JavaScriptCallback` in a position they cannot occupy
  report a generator diagnostic;
- a span function that returns a shared object compiles.

## Process note

The 029 brief says "do not create a delta spec", because it planned a pure
refactor. The approved scope now includes the fixes above, which change
generator output and two runtime codec types for shapes that do not compile
today. This change therefore follows the normal deliver flow: this spec, a
change-local plan, and a merge into `docs/specs/modules-core-boundary.md`.

## Scope

### In scope

- `Expo.ModulesCore.Generator`: a new `ExpoCodecDescriptor.cs`, the model
  files, codec resolution, module, shared-object, and event analysis, and
  emission.
- `Expo.ModulesCore/Codecs/JavaScriptArrayCodec.cs` and
  `JavaScriptDictionaryCodec.cs`, plus the remarks in
  `NullableReadOnlyListCodec.cs`, `NullableDictionaryCodec.cs`, and
  `NullableReadOnlyDictionaryCodec.cs` that say the collection helpers do not
  implement `IJavaScriptCodec<T>`.
- `Expo.ModulesCore.Generator.Tests`: characterization tests captured before
  the refactor, and one test per fixed shape.
- `Expo.ModulesCore.Tests`: runtime tests for the two collection codecs and
  for generated bindings with nested collections, record collection fields,
  and a typed event with a collection payload.
- `docs/specs/modules-core-boundary.md`, `docs/module-authoring-guide.md`, and
  `docs/roadmap.md`.

### Out of scope

- `IJavaScriptCodec<T>`, and every other runtime codec.
- Support for nested `ArrayBuffer` or nested callbacks (030 adds nested
  `ArrayBuffer` support).
- Ownership of `JavaScriptValue` or `ArrayBuffer` values inside composed codecs
  (030).
- Nullable `ArrayBuffer`, nullable callbacks, nullable shared objects (031–033).
- New diagnostic descriptors or new message text.
- Event payload analysis rules. Events keep `ExpoEventPayloadKind` and their
  current checks.
- Replacing the nullable collection adapters with
  `NullableReferenceCodec<T, TCodec>` over the new struct codecs. That would
  change generated output for shapes that compile today.

## Preservation contract

1. Every shape that compiles today produces byte-identical generated sources,
   an identical generator diagnostic list, and an identical compiler
   diagnostic list.
2. Every shape in the "Fixed shapes" table compiles, or reports the listed
   generator diagnostics with no compiler errors.
3. Existing callers of `JavaScriptArrayCodec<T, TCodec>.DecodeToArray`,
   `JavaScriptArrayCodec<T, TCodec>.Encode`,
   `JavaScriptDictionaryCodec<T, TCodec>.DecodeToDictionary`, and
   `JavaScriptDictionaryCodec<T, TCodec>.Encode` compile unchanged and behave
   the same.

## Accepted design

### Composable collection codecs

`JavaScriptArrayCodec<T, TCodec>` and `JavaScriptDictionaryCodec<T, TCodec>`
change from static classes to readonly structs, like every other generic
codec. Their current static methods stay.

- `JavaScriptArrayCodec<T, TCodec>` implements
  `IJavaScriptCodec<IReadOnlyList<T>>`. It adds `Decode(JavaScriptValueRef)`
  and `Decode(JavaScriptValue)`, which return `IReadOnlyList<T>` backed by the
  decoded array, and a `DecodeToArray(JavaScriptValue)` overload. The owned overloads dispose the
  array wrapper and each owned element wrapper they create, as
  `DecodeToDictionary(JavaScriptValue)` does today.
- `JavaScriptDictionaryCodec<T, TCodec>` implements
  `IJavaScriptCodec<Dictionary<string, T>>` and
  `IJavaScriptCodec<IReadOnlyDictionary<string, T>>`. Its public `Decode`
  overloads return `Dictionary<string, T>`. The `IReadOnlyDictionary` decode
  and the `Dictionary` encode are explicit interface implementations that
  delegate to the public methods.

This makes `IReadOnlyList<IReadOnlyList<int>>`,
`IReadOnlyList<IReadOnlyDictionary<string, int>>`, a callback with a list
result, a record with a collection field, and a typed event with a collection
payload valid generated C#. Boundary
decode keeps calling `DecodeToArray` and `DecodeToDictionary`, so output for
direct collection parameters does not change.

### The descriptor

`ExpoCodecDescriptor` is an internal sealed record in
`ExpoCodecDescriptor.cs`. It has value equality, so it is safe in incremental
generator models. Each policy fact is a closed enum:

| Field | Values | Meaning |
| --- | --- | --- |
| `Expression` | string | The emitted C# codec type. Emission data only. |
| `Shape` | `JavaScriptCodec`, `StaticHelper` | `JavaScriptCodec` implements `IJavaScriptCodec<T>` and can be a generic type argument. `StaticHelper` is a static class that generated glue calls directly. |
| `Capabilities` | `DecodeOnly`, `DecodeAndEncode` | Whether the codec has an `Encode` method. |
| `DecodeOperation` | `Decode`, `DecodeToArray`, `DecodeToDictionary` | The decode method that boundary glue calls. |
| `ContextRequirement` | `None`, `DotnetRuntimeContext` | Whether decode and encode take the current runtime context. |
| `DecodedOwnership` | `Borrowed`, `InvocationOwned`, `ContextRetained` | Who releases a decoded value. Generated glue disposes only `InvocationOwned` values. |
| `EncodeOperation` | `Standard`, `TransferJavaScriptValue`, `EncodeThenDisposeArrayBuffer` | How a returned value is encoded. A `DecodeOnly` codec uses `Standard`, and no emission path reads it. |

The descriptor does not nest element descriptors. Composed codecs embed the
inner `Expression` in their own `Expression`, and the composition rules below
check the inner descriptor at resolution time.

### Where each fact is set

Each resolver branch sets the descriptor where it selects the codec. No shared
factory derives a descriptor from a finished expression.

| Resolver branch | Shape | Capabilities | Decode | Context | Ownership | Encode |
| --- | --- | --- | --- | --- | --- | --- |
| Scalars, enums, convertibles, `byte[]`, memory, `Nullable<T>`, nullable references and nullable collections, records | `JavaScriptCodec` | both | `Decode` | `None` | `Borrowed` | `Standard` |
| `IReadOnlyList<T>` | `JavaScriptCodec` | both | `DecodeToArray` | `None` | `Borrowed` | `Standard` |
| `Dictionary<string, T>`, `IReadOnlyDictionary<string, T>` | `JavaScriptCodec` | both | `DecodeToDictionary` | `None` | `Borrowed` | `Standard` |
| `JavaScriptValue` | `JavaScriptCodec` | both | `Decode` | `None` | `InvocationOwned` | `TransferJavaScriptValue` |
| `ArrayBuffer` | `StaticHelper` | both | `Decode` | `None` | `InvocationOwned` | `EncodeThenDisposeArrayBuffer` |
| `JavaScriptCallback<...>` | `StaticHelper` | `DecodeOnly` | `Decode` | `DotnetRuntimeContext` | `ContextRetained` | `Standard` |
| Direct `[ExpoSharedObject]` class | `StaticHelper` | both | `Decode` | `DotnetRuntimeContext` | `Borrowed` | `Standard` |

The callback-argument `ValueTupleCodec` implements `IJavaScriptArgsCodec<T>`,
not `IJavaScriptCodec<T>`. It is never a boundary codec, so its resolver keeps
returning only an expression for the callback resolver to embed.

### Composition rules

- **Generic position.** A list element, a dictionary value, a `Nullable<T>`
  or nullable-reference inner type, a callback result, a callback-argument
  tuple element, and a typed event payload codec (`EmitAsync<TCodec, T>`)
  appear as generic type arguments. Their descriptor SHALL have
  `Shape == JavaScriptCodec`. Otherwise the composed resolver returns no
  codec, and the calling position reports its existing unsupported-type
  diagnostic.
- **Record field.** Generated record codecs call field codecs directly with
  `Decode` and `Encode`, so a `StaticHelper` field is valid. A field
  descriptor SHALL have `Capabilities == DecodeAndEncode` and
  `ContextRequirement == None`. Otherwise the record resolver reports
  `EXPOJSI007`, as it does for any unsupported field. `ArrayBuffer` and
  `JavaScriptValue` record fields keep their current output.
- **Encode position.** A sync return, an async result, and a span-function
  return SHALL have `Capabilities == DecodeAndEncode`. Otherwise the function
  reports `EXPOJSI002`. Properties and events already reject callbacks before
  codec resolution, so their analysis does not change. Event analysis already
  rejects every static-helper payload before codec resolution, so the event
  generic-position rule changes no output.

### Model changes

- `ExpoParameterModel`: `CodecExpression`, `RequiresRuntimeContext`, and
  `OwnsDecodedValue` become one `ExpoCodecDescriptor? Codec`. It is null only
  for span parameters, which have no codec today (their `CodecExpression` is
  empty).
- `ExpoFunctionModel`: `ReturnCodecExpression` and
  `ReturnRequiresRuntimeContext` become `ExpoCodecDescriptor? ReturnCodec`;
  `AsyncResultCodecExpression` and `AsyncResultRequiresRuntimeContext` become
  `ExpoCodecDescriptor? AsyncResultCodec`. Null means no codec (void, async
  void, the other position, or a span return).
- `ExpoPropertyModel`: `CodecExpression`, `OwnsDecodedValue`, and
  `RequiresRuntimeContext` become `ExpoCodecDescriptor Codec`.
- `ExpoGeneratedRecordFieldModel` and `ExpoEventModel`: `CodecExpression`
  becomes a descriptor (nullable on events whose payload kind is not `Codec`).
  `ExpoEventPayloadKind` stays, because it expresses event scheduling policy.
- `IsSharedObjectCodecExpression` is deleted. `TryAnalyzeSharedObjectBoundaryType`
  returns the shared-object descriptor.

### Emission rules

- Emission writes `Expression` into generated C#. It never compares or parses
  it.
- Boundary decode calls the method named by `DecodeOperation`. Record-codec
  emission keeps calling `Decode`, which the collection structs now provide.
- A context argument is emitted when `ContextRequirement ==
  DotnetRuntimeContext`. Parameters, constructor parameters, and property
  setters pass `GeneratedFunction.CurrentRuntimeContext`. Async results pass
  the captured `__expoRuntimeContext`.
- `using var` and the async dispose list are emitted when `DecodedOwnership ==
  InvocationOwned`.
- Return and getter encoding switches on `EncodeOperation`.
- **Span-function fix.** A span function's return now passes
  `GeneratedFunction.CurrentRuntimeContext` when `ContextRequirement ==
  DotnetRuntimeContext`. This is safe because the `WithBytes` and
  `WithReadOnlyBytes` lambda runs synchronously inside the host-function
  frame, and the generated host function returns the lambda's result.

### Fixed shapes

Each shape below emits invalid C# today. Probes on `main` at `1886561`
showed the listed compiler error for every row, except three cases that
follow the same code path as a probed case: a dictionary record field, an
`ArrayBuffer` dictionary value, and a record with an `IReadOnlyList<ArrayBuffer>`
field. The RED tests in
the plan confirm every row's compiler error and then pin the new result. "Position" means the diagnostic of the member that
uses the shape: `EXPOJSI001` for a method parameter, `EXPOJSI002` for a return
or async result, `EXPOJSI008` for a type inside a callback parameter,
`EXPOJSI015` for a property, and `EXPOJSI023` for any shared-object member or
constructor parameter (the generator translates the other codes to
`EXPOJSI023` on shared-object classes).

| Shape | Today | After |
| --- | --- | --- |
| `IReadOnlyList<IReadOnlyList<T>>`, nullable outer list of non-null lists, `Dictionary<string, IReadOnlyList<T>>`, `IReadOnlyList<Dictionary<string, T>>` | CS0718 | compiles and round-trips |
| Record with an `IReadOnlyList<T>` or dictionary field | CS0117 (no `Decode`) | compiles and round-trips |
| Callback with a collection result or collection argument | CS0718 | compiles and round-trips |
| Module or shared-object `[Event]` with a non-null `IReadOnlyList<T>`, `Dictionary<string, T>`, or `IReadOnlyDictionary<string, T>` payload | CS0718 | compiles and emits |
| `JavaScriptCallback<...>` sync return | CS0117 (no `Encode`) | `EXPOJSI002` |
| `Task<JavaScriptCallback<...>>` async return | CS0117 | `EXPOJSI002` |
| `JavaScriptCallback<...>` return of a span function | CS0117 | `EXPOJSI002` |
| `IReadOnlyList<JavaScriptCallback<...>>`, `Dictionary<string, JavaScriptCallback<...>>` | CS0718 | position diagnostic |
| Record with a `JavaScriptCallback<...>` field | CS7036, CS0117 | `EXPOJSI007` and position diagnostic |
| Callback whose argument tuple or result contains a callback | CS0718 | position diagnostic |
| `IReadOnlyList<ArrayBuffer>`, `IReadOnlyList<ArrayBuffer>?`, `Dictionary<string, ArrayBuffer>`, `IReadOnlyDictionary<string, ArrayBuffer>` | CS0718 | position diagnostic |
| Callback whose argument tuple or result contains `ArrayBuffer` | CS0718 | position diagnostic |
| Record with an `IReadOnlyList<ArrayBuffer>` field | CS0718, CS0117 | `EXPOJSI007` and position diagnostic |
| Span function that returns an `[ExpoSharedObject]` class | CS7036 (no `runtimeContext`) | compiles; `Encode` receives the context |

Properties of a callback type keep `EXPOJSI015` from the existing callback
check. Event payloads keep their existing nested-resource and decode-only
rejections.

## Delta against `docs/specs/modules-core-boundary.md`

### MODIFIED Requirement: Unsupported Signatures Are Build Diagnostics

Add these scenarios:

#### Scenario: Decode-only codec is used where a value is encoded
- **GIVEN** a `[JS]` method returns `JavaScriptCallback<...>` directly, as a
  `Task` result, or from a span function
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI002` naming the method and type,
  or `EXPOJSI023` on a shared-object class
- **AND** generated source SHALL NOT call a missing `Encode` method

#### Scenario: Static helper codec is used as a generic codec argument
- **GIVEN** a list element, dictionary value, nullable inner type, callback
  result, or callback-argument tuple element is `ArrayBuffer`, a
  `JavaScriptCallback`, or a type that contains one
- **WHEN** the project is compiled
- **THEN** the generator SHALL report the using member's unsupported-type
  diagnostic: `EXPOJSI001`, `EXPOJSI002`, `EXPOJSI008`, `EXPOJSI015`, or
  `EXPOJSI023`
- **AND** generated source SHALL NOT pass a static codec class as a type
  argument

#### Scenario: Record field needs a runtime context
- **GIVEN** a generated record has a `JavaScriptCallback` field, or a field
  whose codec the generic-argument rule rejects
- **WHEN** a `[JS]` method parameter, return, async result, or shared-object
  constructor parameter uses the record
- **THEN** the generator SHALL report `EXPOJSI007` naming the record and field
- **AND** the using member SHALL report its unsupported-type diagnostic
- **AND** a `[JS]` property whose type contains a callback SHALL keep
  reporting only `EXPOJSI015`

### ADDED Requirement: Non-Null Collections Compose Recursively

`JavaScriptArrayCodec<T, TCodec>` SHALL implement
`IJavaScriptCodec<IReadOnlyList<T>>`, and `JavaScriptDictionaryCodec<T,
TCodec>` SHALL implement `IJavaScriptCodec<Dictionary<string, T>>` and
`IJavaScriptCodec<IReadOnlyDictionary<string, T>>`. Their existing
`DecodeToArray`, `DecodeToDictionary`, and `Encode` methods SHALL keep their
signatures and behavior. Generated bindings SHALL support a non-null
`IReadOnlyList<T>`, `Dictionary<string, T>`, or `IReadOnlyDictionary<string,
T>` wherever a codec of `T` is supported: as a list element, a dictionary
value, a nullable container's content, a callback result or argument, a
generated record field, and a typed event payload.

#### Scenario: Nested non-null collections round-trip
- **GIVEN** a generated method accepts and returns
  `IReadOnlyList<IReadOnlyList<int>>` or
  `IReadOnlyDictionary<string, IReadOnlyList<string>>`
- **WHEN** JavaScript passes nested arrays and objects
- **THEN** generated dispatch SHALL decode and re-encode every level
- **AND** direct collection parameters SHALL still decode through
  `DecodeToArray` or `DecodeToDictionary`

#### Scenario: Record collection field round-trips
- **GIVEN** a positional record has an `IReadOnlyList<T>` or
  `Dictionary<string, T>` field with a supported `T`
- **WHEN** the record crosses a generated boundary
- **THEN** the field SHALL decode from and encode to its lower-camel
  JavaScript property

#### Scenario: Typed event emits a collection payload
- **GIVEN** a module or shared object declares
  `[Event] Func<IReadOnlyList<T>, Task>` or a string-keyed dictionary payload
  with a supported `T`
- **WHEN** C# raises the event
- **THEN** generated code SHALL compile and listeners SHALL receive a
  JavaScript array or plain object

### MODIFIED Requirement: Nullable Collection Containers And Contents Compose Recursively

Replace "because the existing array and dictionary helpers use special decode
methods and do not all implement `IJavaScriptCodec<T>`" with "because a
nullable container needs nullish handling that the non-null collection codecs
do not have". The adapters, their names, and their behavior do not change.

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

Change the `ArrayBuffer` table row's constraint from "Ordinary codec" to:
"Direct parameters, returns, properties, and record fields; not a list
element, dictionary value, nullable inner type, or callback type argument".

### MODIFIED Requirement: Shared-Object Codecs Preserve Managed And JavaScript Identity

Add this scenario:

#### Scenario: Span function returns a shared object
- **GIVEN** a synchronous `[JS]` method has one `Span<byte>` or
  `ReadOnlySpan<byte>` parameter and returns a valid `[ExpoSharedObject]` class
- **WHEN** the method is generated
- **THEN** the return encode SHALL receive the current `DotnetRuntimeContext`
- **AND** the generated source SHALL compile

### ADDED Requirement: Generator Conversion Policy Is Typed

Generator analysis and emission SHALL read decode operation, runtime-context
requirement, decoded ownership, encode operation, capabilities, and codec
shape from typed `ExpoCodecDescriptor` fields. Production generator code SHALL
NOT compare, prefix-match, or parse emitted codec-expression text to make a
conversion decision.

#### Scenario: Codec text is opaque
- **GIVEN** the generator source under `Expo.ModulesCore.Generator`
- **WHEN** a reviewer runs
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- **THEN** it SHALL print no matches

## Module authoring guide

- State that `ArrayBuffer` is supported directly and as a record field, but
  not as a list element, dictionary value, or callback type argument.
- State that `JavaScriptCallback` is a parameter type only: it cannot be a
  return value, a collection element, or a record field.

## Calls I made

- Name `ExpoCodecDescriptor` in `ExpoCodecDescriptor.cs`: the 030 and 031
  briefs already cite both.
- Added `Shape` to the brief's six fields. Ownership and encode operation do
  not decide it: `JavaScriptValueCodec` and `ArrayBufferCodec` share ownership
  policy but only one can be a type argument.
- `Capabilities` has no encode-only value, because no codec is encode-only.
- No recursive element descriptors: no consumer needs them in this change.
- One dictionary struct implements both dictionary interfaces, so the
  generator keeps one dictionary codec name for both CLR types.
- Events keep their current analysis. The only event shape this change fixes
  is a direct collection payload, and the struct codecs fix it without a
  generator change.
