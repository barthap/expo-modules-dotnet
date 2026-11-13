# Modules Core Nullable And Collection Codecs

## Purpose

Define codecs for nullable reference values and for collections, and how
they compose.

## Requirements

### Requirement: Nullable Reference Codecs Are Annotation-Driven

Generated binding analysis SHALL activate nullable reference handling only for a
reference type whose Roslyn symbol carries `NullableAnnotation.Annotated`. It
SHALL remove only the top-level annotation before resolving the inner codec, and
it SHALL preserve nested annotations on type arguments and record fields.
Nullish handling SHALL live only in the nullable reference wrapper and the
nullable collection adapters. The generator SHALL NOT make an existing
non-nullable codec accept JavaScript `null` or `undefined`.

Generated codec type arguments SHALL be rendered with the nullable
reference-type modifier. A codec expression is used as a generic type argument
under a `where TCodec : IJavaScriptCodec<T>` constraint, so an
annotation-erased `T` paired with a nullable codec makes generated code report
`CS8631`, and a reproduced authored partial property reports `CS9256`.

#### Scenario: Annotated string selects a nullable wrapper
- **GIVEN** nullable annotations are enabled and a generated boundary declares
  `string?`
- **WHEN** the generator resolves its codec
- **THEN** it SHALL compose `StringCodec` through the nullable reference codec
- **AND** it SHALL resolve the inner `string` from a non-annotated top-level
  symbol

#### Scenario: Non-nullable reference stays strict
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

#### Scenario: Nullable value types keep their own dispatch
- **GIVEN** a generated boundary declares `int?` or another supported nullable
  value type
- **WHEN** the generator resolves its codec
- **THEN** it SHALL use `NullableCodec<T, TCodec>`, which SHALL stay constrained
  with `where T : struct`
- **AND** it SHALL NOT use `NullableReferenceCodec<T, TCodec>`

#### Scenario: Codec type argument carries the annotation
- **GIVEN** the generator pairs a value or event-payload type argument with a
  nullable codec expression
- **WHEN** it renders generated source
- **THEN** the type argument SHALL include the nullable reference-type modifier
- **AND** generated source SHALL compile without `CS8631` or `CS9256`

### Requirement: Supported Nullable Reference Values Use Dedicated Codecs

Generated bindings SHALL support nullable `string`, `Uri`, `byte[]`,
positional reference-record values, and `[Union]` values through
`NullableReferenceCodec<T, TCodec>`, constrained with `where T : class` and
`where TCodec : IJavaScriptCodec<T>`. Both the scoped and the owned decode
overloads SHALL return C# `null` for JavaScript `null` or `undefined` without
calling the inner codec. Encoding C# `null` SHALL produce JavaScript `null`.
Non-null conversion SHALL delegate to the existing codec for the same
non-nullable type, whose behavior SHALL remain unchanged. `ByteArrayCodec`
SHALL implement `IJavaScriptCodec<byte[]>` so it can serve as an inner codec,
keeping its existing copy-in-both-directions behavior.

#### Scenario: Nullish input decodes to null
- **GIVEN** a supported nullable reference codec receives JavaScript `null` or
  `undefined`
- **WHEN** either decode overload runs
- **THEN** it SHALL return C# `null`
- **AND** it SHALL NOT call the inner codec

#### Scenario: Null value encodes as JavaScript null
- **GIVEN** a supported nullable reference value is C# `null`
- **WHEN** generated binding glue encodes it
- **THEN** the result SHALL be JavaScript `null`
- **AND** it SHALL NOT be JavaScript `undefined`

#### Scenario: Non-null value uses the existing codec
- **GIVEN** a supported nullable reference value is not null
- **WHEN** it crosses the generated boundary in either direction
- **THEN** conversion SHALL delegate to the existing non-nullable codec
- **AND** that codec's non-null behavior SHALL remain unchanged

### Requirement: Non-Null Collections Compose Recursively

`JavaScriptArrayCodec<T, TCodec>` SHALL implement
`IJavaScriptCodec<IReadOnlyList<T>>`, and `JavaScriptDictionaryCodec<T,
TCodec>` SHALL implement `IJavaScriptCodec<Dictionary<string, T>>` and
`IJavaScriptCodec<IReadOnlyDictionary<string, T>>`. Their existing
`DecodeToArray`, `DecodeToDictionary`, and `Encode` methods SHALL keep their
signatures and behavior. Generated bindings SHALL support a non-null
`IReadOnlyList<T>`, `Dictionary<string, T>`, or `IReadOnlyDictionary<string,
T>` wherever a codec of `T` is supported: as a list element, a dictionary
value, a nullable container's content, a callback result or argument, a
generated record field, and a typed event payload. A list element or
dictionary value MAY be `ArrayBuffer`, `JavaScriptValue`, or any type that
holds a leaf. The same `JavaScriptArrayCodec<T, TCodec>` and
`JavaScriptDictionaryCodec<T, TCodec>` SHALL serve it.

#### Scenario: Nested non-null collections round-trip
- **GIVEN** a generated method accepts and returns
  `IReadOnlyList<IReadOnlyList<int>>` or
  `IReadOnlyDictionary<string, IReadOnlyList<string>>`
- **WHEN** JavaScript passes nested arrays and objects
- **THEN** generated dispatch SHALL decode and re-encode every level
- **AND** direct collection parameters SHALL still decode through
  `DecodeToArray` or `DecodeToDictionary`

#### Scenario: Collections of buffers round-trip
- **GIVEN** a method accepts and returns `IReadOnlyList<ArrayBuffer>`,
  `Dictionary<string, ArrayBuffer>`, or a record containing a list containing
  a dictionary of `ArrayBuffer`
- **WHEN** JavaScript passes JS-created buffers
- **THEN** each decoded lease and each returned source SHALL release exactly
  once
- **AND** the JavaScript result SHALL stay usable after the call releases its
  leaves

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

### Requirement: Nullable Collection Containers And Contents Compose Recursively

Generated bindings SHALL support nullable `IReadOnlyList<T>`,
`Dictionary<string, T>`, and `IReadOnlyDictionary<string, T>` containers through
dedicated adapters, because a nullable container needs nullish handling that
the non-null collection codecs do not have. The adapters SHALL be
`NullableReadOnlyListCodec<T, TCodec>`, `NullableDictionaryCodec<T, TCodec>`,
and `NullableReadOnlyDictionaryCodec<T, TCodec>`. Each SHALL return C# `null`
for JavaScript `null` or `undefined`, SHALL encode a null container as
JavaScript `null`, and SHALL delegate non-null conversion to the existing
collection helper. Element and value codecs SHALL resolve recursively, and
dictionary keys SHALL remain exactly `string`. `List<T>` SHALL remain
unsupported with and without a nullable annotation, because its non-nullable
form has no generated codec. These three adapters SHALL serve both leaf-free
and leaf-bearing contents. A null container releases nothing.

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
- **THEN** it SHALL preserve each container and element annotation
- **AND** it SHALL emit compile-time codec composition without reflection

#### Scenario: List stays unsupported with a nullable annotation
- **GIVEN** a generated boundary declares `List<T>` or `List<T>?`
- **WHEN** the generator analyzes the type
- **THEN** it SHALL report the existing unsupported-type diagnostic for that
  position
- **AND** it SHALL NOT emit a `List<T>` codec

### Requirement: Ownership-Bearing Nullable References Are Build Diagnostics

The generator SHALL reject a nullable annotation on `JavaScriptValue`,
`SharedObject`, and `SharedRef<T>`. `JavaScriptValue` already represents
JavaScript `null` and `undefined`. The others carry shared-object identity,
and a null axis multiplies those states. The generator
SHALL report an existing context-specific diagnostic at error severity. It SHALL
NOT add a new diagnostic code, SHALL NOT raise a context-free diagnostic from
codec resolution, SHALL NOT emit a binding for the rejected member, and SHALL
NOT fall through to that type's non-nullable codec. The same non-nullable types
SHALL keep their current supported or diagnostic behavior. `ArrayBuffer?` and
`Uint8Array?` are supported through `NullableReferenceCodec` (see "Nullable
ArrayBuffer Is A Nullable Reference" and "Uint8Array Is A Byte View"). A
nullable callback is supported as a method or constructor parameter (see
"Nullish Callback Parameters Decode To Null Without Retention") and keeps the
non-null rejection on every other position.

Codec resolution does not know the member context, so the codes come from the
callers: `EXPOJSI001` for a parameter, `EXPOJSI002` for a return, `EXPOJSI007`
for a record field, `EXPOJSI008` for a callback, `EXPOJSI015` for a property,
`EXPOJSI019` for a module typed event payload, `EXPOJSI027` for a shared-object
typed event payload, and `EXPOJSI023` for a shared-object boundary. Existing
layered reporting stays as it is: a rejected record field reports both its own
`EXPOJSI007` and the containing member's diagnostic, and a member declared on a
shared-object class reports its `EXPOJSI001`, `EXPOJSI002`, `EXPOJSI008`, and
`EXPOJSI015` results as `EXPOJSI023`.

Typed event payload analysis SHALL check the exclusion itself. A
`Func<JavaScriptValue?, Task>` payload bypasses codec resolution, so only the
event payload check can report `EXPOJSI019` or `EXPOJSI027` for it. A
`Func<ArrayBuffer?, Task>` payload is the direct `ArrayBuffer` payload kind plus
nullability.

A nullable annotation SHALL NOT change shared-object type identity.
`SharedObject?` and `SharedRef<T>?` SHALL report `EXPOJSI023` with the same
reason as the non-null form. A valid concrete class annotated as nullable SHALL
select `NullableSharedObjectCodec<T>` (see "Nullable Concrete Shared Objects
Bypass The Registry On Null"). A rejected member is rejected at error severity,
no binding is emitted, and no shared-object codec is selected.

#### Scenario: Excluded nullable parameter or return reports the method codes
- **GIVEN** a generated method declares `JavaScriptValue?` as a parameter or as
  its return type
- **WHEN** the generator analyzes the method
- **THEN** it SHALL report `EXPOJSI001` for the parameter position and
  `EXPOJSI002` for the return position
- **AND** it SHALL NOT emit a binding or select `JavaScriptValueCodec`

#### Scenario: Excluded nullable record field reports the record code
- **GIVEN** a generated positional record declares a field whose type is an
  excluded nullable reference
- **WHEN** the generator analyzes the record
- **THEN** it SHALL report `EXPOJSI007` naming the record and the field
- **AND** it SHALL NOT emit a record codec that treats the field as
  non-nullable

#### Scenario: Nullable callback outside a parameter keeps its diagnostic
- **GIVEN** a generated return, async result, `[JS]` property, record
  field, list element, dictionary value, or typed event payload uses an
  annotated `JavaScriptCallback<...>?`
- **WHEN** the generator analyzes the member
- **THEN** it SHALL report the same diagnostic as the non-null form
- **AND** it SHALL NOT emit the member

#### Scenario: Excluded nullable property reports the property code
- **GIVEN** a `[JS]` property declares an excluded nullable reference type
- **WHEN** the generator analyzes the property
- **THEN** it SHALL report `EXPOJSI015`
- **AND** it SHALL emit neither accessor

#### Scenario: Excluded nullable event payload reports the event codes
- **GIVEN** a module typed event or a shared-object typed event declares an
  excluded nullable payload type
- **WHEN** the generator analyzes the event property
- **THEN** typed event payload analysis SHALL report `EXPOJSI019` for a module
  event and `EXPOJSI027` for a shared-object event
- **AND** it SHALL NOT emit event glue for that property

#### Scenario: Nullable SharedRef reports the shared-object code
- **GIVEN** a generated boundary declares an annotated `SharedRef<T>`
- **WHEN** shared-object boundary analysis reaches the annotated type
- **THEN** it SHALL report `EXPOJSI023`
- **AND** it SHALL NOT select a shared-object codec or emit the member

#### Scenario: Nullable polymorphic SharedObject reports the shared-object code
- **GIVEN** a generated boundary declares `SharedObject?` as a method
  parameter, a return type, an async result type, a property type, or a
  shared-object `[JS]` constructor parameter, on a module or on a
  shared-object class
- **WHEN** the generator analyzes the boundary
- **THEN** it SHALL report `EXPOJSI023` naming the member and the reason
  "which is the polymorphic SharedObject base"
- **AND** it SHALL NOT report `EXPOJSI001`, `EXPOJSI002`, or `EXPOJSI015` for
  that member
- **AND** it SHALL NOT select a shared-object codec or emit the member

#### Scenario: Nullable shared object inside a composed codec
- **GIVEN** a generated method parameter declares a composed type whose
  element is `SharedObject?`, such as `IReadOnlyList<SharedObject?>`
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL report `EXPOJSI023` with the composed-codec reason, as it
  does for `IReadOnlyList<SharedObject>`
- **AND** it SHALL NOT emit the member

#### Scenario: Valid concrete shared object stays accepted
- **GIVEN** a generated method parameter declares a non-null top-level, sealed,
  non-generic `[ExpoSharedObject]` class
- **WHEN** the generator analyzes the parameter
- **THEN** it SHALL select `SharedObjectCodec<T>` and emit the binding with no
  diagnostic
- **AND** the same class annotated as nullable SHALL select
  `NullableSharedObjectCodec<T>`

#### Scenario: Rejected nullable type does not fall back
- **GIVEN** codec resolution has handled an annotated excluded reference
- **WHEN** control returns to the analysis that asked for the codec
- **THEN** dispatch SHALL stop instead of continuing to that type's
  non-nullable codec branch
- **AND** the generator SHALL report only the existing context-specific
  diagnostics and SHALL NOT emit a binding

### Requirement: Nullable ArrayBuffer Is A Nullable Reference

`ArrayBuffer?` SHALL resolve to
`NullableReferenceCodec<ArrayBuffer, ArrayBufferCodec>` on every position
where `ArrayBuffer` is accepted. JavaScript `null` and `undefined` SHALL
decode to C# `null` without a lease. C# `null` SHALL encode as JavaScript
`null`. A non-null value SHALL follow the `ArrayBuffer` rules unchanged:
decode gives the leaf to generated glue, which releases it once through
`Release`, and `Encode` leaves the source usable. The generator SHALL NOT add
a nullable-specific cleanup path.

#### Scenario: Nullish parameter holds no leaf
- **GIVEN** a generated method declares an `ArrayBuffer?` parameter
- **WHEN** JavaScript passes `null` or `undefined`
- **THEN** authored code SHALL receive C# `null`
- **AND** `LongLivedArrayBuffersReleased` SHALL NOT change

#### Scenario: Non-null parameter releases once
- **GIVEN** the same method
- **WHEN** JavaScript passes a JavaScript-created buffer to a sync method,
  and to an async method that awaits before reading it
- **THEN** authored code SHALL read the buffer, after the await on the async
  path
- **AND** each call SHALL release the lease exactly once

#### Scenario: Optional nullable buffer uses the authored default
- **GIVEN** a generated method declares `ArrayBuffer? value = null`
- **WHEN** JavaScript omits the argument, passes `undefined`, or passes
  `null`
- **THEN** authored code SHALL receive C# `null` in all three cases
- **AND** no lease SHALL be retained

#### Scenario: Nullable return and property encode null or the buffer
- **GIVEN** a sync method, a `Task<ArrayBuffer?>` method, and a readable and
  writable `ArrayBuffer?` property
- **WHEN** authored code returns or stores `null`
- **THEN** JavaScript SHALL receive `null`
- **WHEN** authored code returns a buffer
- **THEN** JavaScript SHALL read its bytes
- **AND** the returned leaf SHALL release exactly once after encode
- **WHEN** JavaScript assigns a buffer to the property and authored code
  stores a `Retain()` copy
- **THEN** the setter's decoded leaf SHALL release once and the stored copy
  SHALL stay readable until the module releases it

#### Scenario: Nullable buffer inside a record and a collection
- **GIVEN** a record with an `ArrayBuffer?` field and an
  `IReadOnlyList<ArrayBuffer?>` parameter
- **WHEN** JavaScript passes `null` leaves next to buffer leaves
- **THEN** null leaves SHALL release nothing and buffer leaves SHALL
  release exactly once each
- **WHEN** a later field decode fails after a non-null buffer field decoded
- **THEN** the earlier lease SHALL release exactly once

#### Scenario: Shared-object members accept nullable buffers
- **GIVEN** a shared-object `[JS]` constructor parameter, method parameter,
  and property of type `ArrayBuffer?`
- **WHEN** JavaScript passes `null` and a buffer
- **THEN** they SHALL behave as the module positions above

#### Scenario: Nullable buffer in a callback and a nested event payload
- **GIVEN** a callback whose argument and result are `ArrayBuffer?`, and a
  typed event whose payload is a list of `ArrayBuffer?`
- **WHEN** authored code passes `null` and a buffer
- **THEN** JavaScript SHALL receive `null` and the buffer
- **AND** the author's buffer SHALL stay usable, and the author SHALL own a
  non-null callback result

#### Scenario: JavaScriptValue stays excluded
- **GIVEN** a generated member declares `JavaScriptValue?` next to an
  `ArrayBuffer?` member
- **WHEN** the generator runs
- **THEN** it SHALL report the member's existing diagnostic for
  `JavaScriptValue?` and no diagnostic for `ArrayBuffer?`

### Requirement: Nullish Callback Parameters Decode To Null Without Retention

`JavaScriptCallback<TResult>?` and `JavaScriptCallback<TArgs, TResult>?`
SHALL be accepted as parameters of generated module methods, sync and async,
and of supported shared-object constructors and methods, including with an
authored `= null` default. They SHALL resolve to
`NullableJavaScriptCallbackCodec<...>` with the same type arguments as the
non-null codec, `DecodeOnly`, requiring `DotnetRuntimeContext`, and
`ContextRetained`. JavaScript `null` and `undefined` SHALL decode to C#
`null` without calling `AsFunction` and without registering a callback with
the context. A non-null function SHALL decode through the non-null codec
unchanged. A nullable callback is never a leaf holder: generated glue SHALL
NOT release or dispose it at invocation end. Its argument and result types
follow the non-null rule, so they can hold leaves at any depth.

#### Scenario: Nullish callback reaches authored code as null
- **GIVEN** a method with a required `JavaScriptCallback<int>?` parameter
  and a method with an optional
  `JavaScriptCallback<ValueTuple<string>, string>? cb = null` parameter
- **WHEN** JavaScript passes `null`, passes `undefined`, or omits the
  optional argument
- **THEN** authored code SHALL receive C# `null`
- **AND** the context's retained callback count SHALL NOT change

#### Scenario: Non-function non-null value is rejected
- **GIVEN** the required nullable callback parameter
- **WHEN** JavaScript passes a number
- **THEN** the call SHALL throw a JavaScript error from the function check
- **AND** the retained callback count SHALL NOT change

#### Scenario: Non-null callback keeps context-retained lifetime
- **GIVEN** the same parameters
- **WHEN** JavaScript passes a function
- **THEN** authored code SHALL invoke it synchronously and after the method
  returns, including after an `await` on an async method
- **AND** the retained callback count SHALL increase by one per decode
- **AND** explicit `Dispose` SHALL release once, and context teardown SHALL
  release an undisposed callback once

#### Scenario: Leaf-bearing nullable callback follows the leaf rules
- **GIVEN** a `JavaScriptCallback<ValueTuple<ArrayBuffer>, ArrayBuffer>?`
  parameter
- **WHEN** JavaScript passes `null`, or a function that returns another
  buffer that JavaScript already holds
- **THEN** authored code SHALL receive C# `null` for `null`
- **AND** for a function, the buffer the author passes SHALL stay the
  author's, and the returned buffer SHALL be the author's to dispose

#### Scenario: Shared-object parameters accept nullable callbacks
- **GIVEN** a shared-object `[JS]` constructor and method with a nullable
  callback parameter
- **WHEN** JavaScript passes `null` and a function
- **THEN** they SHALL behave as the module positions

### Requirement: Nullable Concrete Shared Objects Bypass The Registry On Null

A top-level, non-generic, sealed `[ExpoSharedObject]` class annotated as
nullable, including a concrete subclass of `SharedRef<T>`, SHALL be accepted
on every boundary that accepts the non-null class and SHALL select
`NullableSharedObjectCodec<T>` with the non-annotated exact type. JavaScript
`null` and `undefined` SHALL decode to C# `null` without resolving registry
identity. C# `null` SHALL encode as JavaScript `null` without class
registration or pairing. A non-null value SHALL use `SharedObjectCodec<T>`
unchanged. A shared object is registry-owned, never a leaf holder: generated
glue SHALL NOT release or dispose it at invocation end.

#### Scenario: Null parameter, return, and property
- **GIVEN** a module with a `T?` parameter, a `T?` return, a `Task<T?>`
  method, and a readable and writable `T?` property
- **WHEN** JavaScript passes `null` or `undefined`, and authored code
  returns or stores `null`
- **THEN** authored code SHALL receive `null`, and JavaScript SHALL receive
  `null`
- **AND** no pairing step SHALL be observed

#### Scenario: Non-null keeps identity
- **GIVEN** the same members
- **WHEN** JavaScript passes a live instance and authored code returns the
  same instance twice
- **THEN** authored code SHALL receive the original managed instance
- **AND** both JavaScript results SHALL be strictly equal to the input

#### Scenario: Existing failures stay
- **GIVEN** the `T?` parameter
- **WHEN** JavaScript passes a plain object, an instance of another shared
  class, an instance from another context, or a released instance
- **THEN** the call SHALL fail as it does for `T`

#### Scenario: Concrete SharedRef subclass
- **GIVEN** a sealed `[ExpoSharedObject]` class that derives from
  `SharedRef<X>` used as `T?`
- **WHEN** it crosses the boundary as `null` and as an instance
- **THEN** it SHALL behave as the scenarios above and SHALL NOT dispose its
  carried value

#### Scenario: Bases and nested shapes stay rejected
- **GIVEN** `SharedObject?`, `SharedRef<X>?`, `IReadOnlyList<T?>`, a record
  with a `T?` field, and `Func<T?, Task>`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI023` (or the event code) with the
  existing reasons

### Requirement: Optional Nullable Reference Arguments Preserve Authored Defaults

A generated optional nullable reference parameter SHALL use the authored C#
default for omission or explicit JavaScript `undefined`. Explicit JavaScript
`null` SHALL decode as C# `null`. A required nullable reference parameter SHALL
decode explicit JavaScript `undefined` as C# `null`. This matches the existing
optional nullable value-type behavior.

#### Scenario: Optional nullable reference is omitted
- **GIVEN** a generated method declares an optional nullable reference parameter
  with a C# default
- **WHEN** JavaScript omits the argument or passes explicit `undefined`
- **THEN** generated dispatch SHALL pass the authored C# default

#### Scenario: Optional nullable reference receives explicit null
- **GIVEN** a generated method declares an optional nullable reference parameter
  with a non-null C# default
- **WHEN** JavaScript passes explicit `null`
- **THEN** generated dispatch SHALL pass C# `null`
- **AND** it SHALL NOT substitute the authored default

#### Scenario: Required nullable reference receives undefined
- **GIVEN** a generated method declares a required nullable reference parameter
- **WHEN** JavaScript passes explicit `undefined`
- **THEN** generated dispatch SHALL pass C# `null`

### Requirement: Nullable Reference Codecs Apply Across Generated Binding Surfaces

Supported nullable reference codecs SHALL apply to method parameters,
synchronous returns, `Task<T?>` results, optional arguments, readable and
writable `[JS]` properties, positional record fields, supported collection
container and nested positions, typed event payloads, and shared-object
constructors and `[JS]` members. Classification and composition SHALL happen at
build time, and generated glue SHALL stay NativeAOT-compatible without runtime
reflection, dynamic invocation, JSON conversion, a new C ABI entry, or a
platform-specific dependency.

This requirement covers safe nullable reference values used by a shared-object
member. A nullable shared-object instance type follows "Nullable Concrete Shared
Objects Bypass The Registry On Null" instead and stays rejected in records,
collections, callbacks, and typed event payloads.

#### Scenario: Generated return paths encode null
- **GIVEN** a synchronous generated method or a `Task<T?>` result returns C#
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
- **GIVEN** a typed event payload, a shared-object constructor parameter, or a
  shared-object `[JS]` member uses a supported nullable reference value
- **WHEN** generated binding or event glue converts the value
- **THEN** it SHALL use the same nullable codec composition as module methods
- **AND** C# `null` SHALL encode as JavaScript `null`
