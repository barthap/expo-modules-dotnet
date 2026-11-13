# Modules Core Codecs

## Purpose

Define the typed conversion policy of the generator, the leaf release
protocol, binary codecs, and named unions.

## Requirements

### Requirement: Generator Conversion Policy Is Typed

Generator analysis and emission SHALL read decode operation, runtime-context
requirement, resource ownership, capabilities, and codec shape from typed
`ExpoCodecDescriptor` fields. Production generator code SHALL
NOT compare, prefix-match, or parse emitted codec-expression text to make a
conversion decision.

#### Scenario: Codec text is opaque
- **GIVEN** the generator source under `Expo.ModulesCore.Generator`
- **WHEN** a reviewer runs
  `rg -n '\.\w*CodecExpression\b|\.Expression *(==|!=| is )|Expression\.(StartsWith|EndsWith|Contains)|StartsWith\("(JavaScriptArrayCodec|JavaScriptDictionaryCodec|SharedObjectCodec)|is "(JavaScriptValueCodec|ArrayBufferCodec)"' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator`
- **THEN** it SHALL print no matches

### Requirement: Codecs Release Leaves Through One Protocol

The leaf and the codec contract are defined in `### Requirement: Boundary Leaf
Ownership Is Explicit` in `ownership-and-scoped-refs.md`.

`IJavaScriptCodec<T>` SHALL declare `static virtual void Release(T value)`
with an empty default body. There SHALL be no second codec protocol.
`JavaScriptValueCodec`, `ArrayBufferCodec`, and `Uint8ArrayCodec` SHALL
dispose the leaf.
`JavaScriptValueCodec.Encode` SHALL return `value.Retain()`.

Every list, dictionary, nullable, generated union, and generated record codec
whose content holds a leaf SHALL release each child through the child codec. A
generated union codec SHALL release only the payload of the case it holds. A
dictionary codec SHALL do this through both dictionary interfaces. A composite
with more than one child SHALL attempt every child. It SHALL throw one
`AggregateException` with every failure, through the public `CodecRelease`
helper.

A list, dictionary, or record decode that fails at a child SHALL release the
children it already decoded and rethrow. If that cleanup throws, its
`AggregateException` SHALL replace the decode error. A generated union decode
SHALL rely on this rule for each failed attempt, SHALL propagate an
`AggregateException` from an attempt, and SHALL release a decoded payload whose
case construction throws.

No codec SHALL use finalizers, reflection, or ambient state to release.

Every generated record codec SHALL use one `Encode` text with no null guard.
Encoding a null class record SHALL fail with a catchable JavaScript error.
`Release` SHALL skip a null record. `JavaScriptCallbackCodec`,
`NullableJavaScriptCallbackCodec`, `SharedObjectCodec<T>`,
`NullableSharedObjectCodec<T>`, and `SharedObjectBaseCodec<TBase>` are static
classes with no `Release`: the runtime context retains callbacks and the
registry owns shared objects.

#### Scenario: A missing override leaks
- **GIVEN** a composite codec over `ArrayBufferCodec`, holding JS-created
  buffers
- **WHEN** a test calls `Release` through the generic `TCodec.Release`
- **THEN** each buffer entry SHALL release exactly once
- **AND** the test SHALL fail if that composite used the empty default

#### Scenario: Release failure is aggregated
- **GIVEN** a list whose second element's release throws
- **WHEN** the list codec releases it
- **THEN** the first and third elements SHALL still release
- **AND** the codec SHALL throw `AggregateException` with that failure

#### Scenario: Later element decode fails
- **GIVEN** a JavaScript array whose first element is an `ArrayBuffer` and
  whose second element is not
- **WHEN** `JavaScriptArrayCodec<ArrayBuffer, ArrayBufferCodec>` decodes it
- **THEN** the first lease SHALL release exactly once
- **AND** the decode error SHALL propagate

#### Scenario: Encode leaves the source usable
- **GIVEN** an author-owned `JavaScriptValue` inside a list
- **WHEN** `JavaScriptArrayCodec<JavaScriptValue, JavaScriptValueCodec>`
  encodes the list
- **THEN** the author's wrapper SHALL stay usable
- **AND** the encoder SHALL release only its own copy

#### Scenario: Null record in a returned list
- **GIVEN** a returned list holds a null class record and records with leaves
- **WHEN** generated glue encodes and releases it
- **THEN** the call SHALL fail with a catchable JavaScript error
- **AND** the leaves of the other records SHALL release exactly once

### Requirement: Generated Bindings Release Leaves After Each Call

Generated bindings with no `HoldsLeaves` argument or result SHALL keep their
generated text. The generator SHALL emit `Release` only for a `HoldsLeaves`
descriptor.

Sync: a leaf-bearing sync function, property getter or setter, shared-object
member, shared-object constructor factory, or span function SHALL declare its
leaf-bearing arguments and result before one `try`. Its `finally` SHALL release
each of them once. An argument that never decoded is still default and
releases nothing. The span lease SHALL keep its own `using`.

Async: the outer `catch` SHALL release leaf-bearing arguments until
`CreatePromise` returns. After that, only the Promise lambda SHALL release
them. With a leaf-free result, the lambda SHALL release them when the `Task`
completes. A leaf-bearing result SHALL travel to the factory in a `StrongBox`
with the leaf-bearing arguments. The factory SHALL release them after it
encodes, and abandon SHALL release them without encoding. A fault or cancel
SHALL release them in the lambda's `catch`.

A `Release` failure SHALL replace the original outcome. In a sync `finally` it
replaces the result or the authored exception. In the async outer `catch` the
host function throws it instead of returning a rejected Promise.

`ExpoResourceOwnership` is `Borrowed`, `HoldsLeaves`, or `ContextRetained`.
`ExpoCodecShape` is `JavaScriptCodec` or `StaticHelper`. `ExpoCodecDescriptor`
SHALL carry `ValueTypeName`, the codec's `T` with its nullable annotation, for
local declarations and helper type arguments.

| Branch | Shape | ResourceOwnership |
| --- | --- | --- |
| Leaf-free types | as before | `Borrowed` |
| `JavaScriptValue`, `ArrayBuffer`, `Uint8Array` | `JavaScriptCodec` | `HoldsLeaves` |
| List, dictionary, or nullable over a `HoldsLeaves` child | `JavaScriptCodec` | `HoldsLeaves` |
| `[Union]` type with any `HoldsLeaves` payload | `JavaScriptCodec` | `HoldsLeaves` |
| Record with any `HoldsLeaves` field | `JavaScriptCodec` | `HoldsLeaves` |
| `JavaScriptCallback<...>` and `JavaScriptCallback<...>?` | `StaticHelper` | `ContextRetained` |
| Direct `[ExpoSharedObject]` class `T` and `T?` | `StaticHelper` | `Borrowed` |
| Authored abstract base `TBase` (parameter only; decode-only `SharedObjectBaseCodec<TBase>`) | `StaticHelper` | `Borrowed` |

A record codec model SHALL store whether the record holds leaves and whether it
is a value type, and a union codec model SHALL store whether the union holds
leaves, so a cache hit rebuilds the same descriptor.

#### Scenario: Leaf-free member text is unchanged
- **GIVEN** the leaf-free output contract sources
- **WHEN** the generator runs
- **THEN** the output hashes SHALL equal the hashes from before leaf release

#### Scenario: Later field decode fails
- **GIVEN** a record parameter whose first field is an `ArrayBuffer` and whose
  second field fails to decode
- **WHEN** JavaScript calls the method
- **THEN** the first field's lease SHALL release exactly once
- **AND** the call SHALL fail through the catchable JavaScript error path

#### Scenario: Alias in two returned leaves
- **GIVEN** a method returns a record whose two fields hold the same
  `JavaScriptValue`
- **WHEN** generated glue encodes it
- **THEN** both JavaScript properties SHALL hold the value
- **AND** the source's native handle SHALL release exactly once

#### Scenario: Encode failure releases returned leaves
- **GIVEN** a returned value whose encode throws
- **WHEN** generated glue handles the failure
- **THEN** every leaf of the returned value SHALL release exactly once

#### Scenario: Abandoned async result
- **GIVEN** an async result with leaves whose settlement never reaches the
  runtime
- **WHEN** the scheduler abandons it
- **THEN** every input and result leaf SHALL release exactly once

#### Scenario: Async method throws before returning its task
- **GIVEN** an async method with leaf-bearing arguments throws before it
  returns its `Task`
- **WHEN** JavaScript calls it
- **THEN** the outer `catch` SHALL release each argument exactly once
- **AND** the Promise SHALL reject

### Requirement: Module-facing ArrayBuffer And Binary Codecs

`Expo.ModulesCore.ArrayBuffer` SHALL be the universal module-facing binary
abstraction with exactly two private backing forms: JavaScript-owned storage
and native MutableBuffer-owned storage. `byte[]`, `Memory<byte>`,
`ReadOnlyMemory<byte>`, and span support SHALL be convenience codecs, not
additional backing kinds.

| CLR type | Boundary behavior | Generated-binding constraints |
| --- | --- | --- |
| `ArrayBuffer` | Preserves wrapper ownership semantics | Every codec position, including list elements, dictionary values, callback types, and nested typed-event payloads, each also as `ArrayBuffer?` |
| `Uint8Array` | A view over one retained buffer lease with its offset and length; no byte copy in either direction | Every `ArrayBuffer` position, each also as `Uint8Array?`, and a union payload |
| `byte[]` | Copies in both directions | Rank-one arrays only |
| `Memory<byte>` / `ReadOnlyMemory<byte>` | Copies in both directions, encoding exactly the current logical slice | Ordinary codecs |
| `Span<byte>` / `ReadOnlySpan<byte>` | Borrows an input slice or copies a return value | One synchronous parameter only |

#### Scenario: Module ArrayBuffer owns storage
- **GIVEN** a module receives or creates an `ArrayBuffer`
- **WHEN** it retains, copies, accesses, or disposes the value
- **THEN** ownership SHALL remain explicit, disposal SHALL atomically relinquish
  the backing lease, and duplicate disposal SHALL be idempotent
- **AND** the wrapper SHALL remain single-owner: concurrent use and disposal
  are unsupported, and a concurrent consumer SHALL receive a retained copy
- **AND** `ByteLength` SHALL be available without entering JSI
- **AND** `Copy`/`ToArray` operations SHALL produce independent storage

#### Scenario: JavaScript and native backing are encoded
- **GIVEN** a JavaScript-backed value is encoded into its originating runtime
- **WHEN** the module returns it
- **THEN** the original JavaScript object identity SHALL be preserved
- **WHEN** a native-backed value is encoded into any live runtime
- **THEN** the returned JavaScript object SHALL be distinct while sharing bytes

#### Scenario: Byte arrays, memory, and spans cross the module boundary
- **GIVEN** a generated method uses `byte[]`, `Memory<byte>`,
  `ReadOnlyMemory<byte>`, `Span<byte>`, or `ReadOnlySpan<byte>`
- **WHEN** the method is generated
- **THEN** byte arrays SHALL copy in both directions
- **AND** `Memory<byte>` and `ReadOnlyMemory<byte>` SHALL be ordinary codecs
  that copy in both directions
- **AND** memory codecs SHALL be available in ordinary synchronous and
  asynchronous generated-binding positions
- **AND** memory encoding SHALL copy exactly the current logical slice into a
  distinct JavaScript `ArrayBuffer`
- **AND** memory codecs SHALL NOT pin, retain, or alias managed storage as
  JavaScript storage
- **AND** one synchronous scoped span parameter MAY borrow bytes
- **AND** asynchronous or multiple span parameters, and asynchronous span
  results, SHALL produce diagnostics
- **AND** span return values SHALL be copied immediately
- **AND** only rank-one `byte[]` parameters and returns SHALL use the byte-array
  codec; multidimensional byte arrays SHALL produce unsupported-type diagnostics
- **AND** only exact `Memory<byte>` and `ReadOnlyMemory<byte>` specializations
  SHALL use the memory codecs; other `Memory<T>` and `ReadOnlyMemory<T>` types
  SHALL produce unsupported-type diagnostics
- **AND** the one-span limit SHALL not restrict `ArrayBuffer`, `byte[]`,
  `Memory<byte>`, or `ReadOnlyMemory<byte>` arity

### Requirement: Named Unions Decode Cases In Declaration Order

A type with `[Union]` SHALL be a non-generic `abstract record` class with no
parameter list, accessible to generated code, with no generic containing type.
Its cases SHALL be the nested `sealed record` types that derive from it
directly, each with exactly one positional parameter, the payload, which SHALL
have no default value and SHALL NOT be nullable. Case order SHALL be the source
order of the case declarations, which SHALL all be in one partial declaration.
A payload SHALL be a type whose codec is an ordinary `JavaScriptCodec` with
`DecodeAndEncode` and no runtime context, or another `[Union]` declared in the
same assembly. A payload type MAY be declared in a referenced assembly, and
then SHALL decode and encode through the same codec as when that type is a
method parameter: a positional record, an enum, or a list or dictionary of
them. A union SHALL be accepted in every codec position: parameters, returns,
async results, properties, record fields, list elements, dictionary values,
nullable references, callback arguments and results, typed event payloads, and
shared-object constructors and members.

The generator SHALL emit one private codec per used union in each provider. Its
`Decode` SHALL read the value kind once and visit the cases in declaration
order. It SHALL skip a case whose payload kinds do not contain the kind. The
payload kinds are: `String` for `string`, `Guid`, `Uri`, `DateTimeOffset`,
`TimeSpan`, and string-represented enums; `Bool` for `bool`; `Number` for the
`NumberCodec<T>` types and number-represented enums; `ArrayBuffer` for
`ArrayBuffer`, `byte[]`, `Memory<byte>`, and `ReadOnlyMemory<byte>`; `Object`
for records, lists, dictionaries, and `Uint8Array`; every kind for
`JavaScriptValue`; and the kinds of all cases for a nested union. An integer
case that is not the last case whose kinds contain `Number` SHALL take only a
whole number that its type holds exactly. Any other case SHALL be attempted
through its payload codec. The first successful attempt SHALL win. An
`AggregateException` from an attempt SHALL propagate. Any other exception SHALL
fail the attempt and move to the next case. If constructing the case throws,
the codec SHALL release the decoded payload and rethrow. A typed event payload
is only encoded, so it attempts no case.

When no case takes the value, `Decode` SHALL throw
`UnionCaseMismatchException` with the message
`'<Union>' expected <T1> or <T2> or ..., but received <kind>`: the union's
simple name, the payload types in declaration order in C# minimal form, and one
of `undefined`, `null`, `boolean`, `number`, `string`, `object`, `function`, or
`ArrayBuffer`. `UnionCaseMismatchException` SHALL derive from
`CodedException`, and its `Code` SHALL be the derived `ERR_UNION_CASE_MISMATCH`,
the code of upstream iOS `UnionCaseMismatch`. JavaScript SHALL receive it as an
`Error` whose `message` is that text followed by the cause chain of
`### Requirement: Module Failures Become JavaScript Errors`. It SHALL expose
`UnionName`, `Received`, and `Expected`, and its `InnerException` SHALL be the
failure of the last attempted case, or `null` when no case was attempted.

`Encode` SHALL encode the payload of the case it holds through the payload
codec. A `null` union SHALL throw `ArgumentNullException`, and a type that is
not a case SHALL throw `InvalidOperationException` with the message
`Type '<type>' is not a case of union '<Union>'.`, built by
`UnionCodecErrors.NotACase`, so generated union text uses no `GetType` or
`typeof`. A union SHALL hold leaves when a payload does. Its `Release` SHALL
release only the payload of the case it holds and SHALL skip `null`. A
leaf-free union SHALL keep leaf-free generated text.

The generator SHALL report, once per declaration and at error severity:
`EXPOJSI029` for an invalid union declaration, a recursive union, or a union
declared in a referenced assembly (at the using parameter, member, or record
field); `EXPOJSI030` for an invalid case, a type that derives from a union and
is not a valid case, a nullable or defaulted payload, a payload that contains
itself through records, lists, dictionaries, callbacks, or unions, or a payload
with no union codec; `EXPOJSI031` for two cases with the same payload type; and
`EXPOJSI032` for a case that an earlier case always takes first. A case type
used directly, and every invalid union, SHALL make the using position report
its existing unsupported-type diagnostic. No Roslyn dependency on source file
order, no reflection, and no ABI surface SHALL be added.

A later case is unreachable when:

1. An earlier case has a `JavaScriptValue` payload.
2. Its kinds are only `Number`, and an earlier case has a `double` or `float`
   payload.
3. It is an integer case that is not the last case whose kinds contain
   `Number`, and an earlier integer case's type range contains its type range.
4. Its kinds are only `String`, and an earlier case has a `string` payload.
5. Its kinds are only `ArrayBuffer`, and an earlier case has an `ArrayBuffer`,
   `byte[]`, `Memory<byte>`, or `ReadOnlyMemory<byte>` payload.
6. Its kinds are only `Object`, and an earlier case has a
   `Dictionary<string, JavaScriptValue>` or
   `IReadOnlyDictionary<string, JavaScriptValue>` payload, or a record payload
   whose every field is `JavaScriptValue`.
7. It has an `IReadOnlyList<T>` payload, and an earlier case has an
   `IReadOnlyList<JavaScriptValue>` payload.
8. It has a record payload, an earlier case has a record payload, and every
   field of the earlier record that is not `JavaScriptValue` has a field in the
   later record with the same JavaScript name and either the same C# type or
   the C# type of which the earlier field's type is the nullable form, and,
   when the earlier field is required, the later field is required too.

#### Scenario: Three cases decode by kind
- **GIVEN** `[Union] abstract record NonceInput` with cases `Base64(string)`,
  `Bytes(byte[])`, and `Length(int)`, and a method
  `string Describe(NonceInput nonce)`
- **WHEN** JavaScript passes `'AAEC'`, an `ArrayBuffer` of 3 bytes, and `12`
- **THEN** authored code SHALL receive `Base64("AAEC")`, `Bytes` with the 3
  bytes, and `Length(12)`

#### Scenario: Integer before double
- **GIVEN** a union with cases `Whole(int)` and then `Fraction(double)`
- **WHEN** JavaScript passes `3`, `2.5`, and `3e9`
- **THEN** `3` SHALL decode as `Whole(3)`, and `2.5` and `3e9` SHALL decode as
  `Fraction`

#### Scenario: A lone integer case decodes like its codec
- **GIVEN** a union with cases `Text(string)` and `Count(int)`
- **WHEN** JavaScript passes `2.5`
- **THEN** it SHALL decode as `Count(2)`, as `NumberCodec<int>` decodes `2.5`
  outside a union

#### Scenario: A failed attempt moves to the next case
- **GIVEN** a union with cases `Address(Uri)` and then `Text(string)`
- **WHEN** JavaScript passes `'https://expo.dev/x'` and `'hello'`
- **THEN** the first SHALL decode as `Address` and the second as `Text`

#### Scenario: Records are picked by their fields
- **GIVEN** a union with cases `Round(Circle)` and then `Dot(Point)`, where
  `record Circle(double X, double Y, double Radius)` and
  `record Point(double X, double Y)` are declared outside the union
- **WHEN** JavaScript passes `{ x: 1, y: 1, radius: 2 }`, `{ x: 1, y: 2 }`, and
  `{ x: 1 }`
- **THEN** the first SHALL decode as `Round`, the second as `Dot`
- **AND** the third SHALL throw `UnionCaseMismatchException` whose
  `InnerException` is the `Point` decode failure
- **AND** the mismatch message SHALL end with
  `\n→ Caused by: Value for property 'y' is required, got undefined`

#### Scenario: A list and a record in one union
- **GIVEN** a union with cases `Items(IReadOnlyList<int>)` and `Named(Entry)`,
  where `record Entry(string Name)`
- **WHEN** JavaScript passes `[1, 2]` and `{ name: 'a' }`
- **THEN** the first SHALL decode as `Items` and the second as `Named`

#### Scenario: Nested union
- **GIVEN** a union `Outer` with cases `Flag(bool)` and `Nested(Inner)`, where
  `Inner` is a union declared outside `Outer` with cases `Text(string)` and
  `Count(int)`
- **WHEN** JavaScript passes `true`, `'a'`, `3`, and `{}`
- **THEN** they SHALL decode as `Flag`, `Nested(Text)`, `Nested(Count)`, and
  throw `'Outer' expected bool or Inner, but received object`

#### Scenario: Kind gating skips without decoding
- **GIVEN** a union with cases `Text(string)` and `View(Uint8Array)`
- **WHEN** JavaScript passes an `ArrayBuffer`
- **THEN** it SHALL throw
  `'<Union>' expected string or Uint8Array, but received ArrayBuffer`
- **AND** `InnerException` SHALL be `null`, because no case was attempted

#### Scenario: Mismatch message and nullish input
- **GIVEN** `[Union] abstract record UnionSource` with cases `Text(string)` and
  `Options(UnionSourceOptions)` and a non-nullable parameter
- **WHEN** JavaScript passes `42`, `null`, and `undefined`
- **THEN** the call SHALL fail with a catchable JavaScript `Error` whose
  message contains
  `'UnionSource' expected string or UnionSourceOptions, but received number`,
  then `received null`, then `received undefined`
- **AND** authored code SHALL NOT run

#### Scenario: Mismatch has the upstream code
- **GIVEN** any union
- **WHEN** no case takes the value
- **THEN** JavaScript SHALL catch an `Error` with
  `code === 'ERR_UNION_CASE_MISMATCH'`
- **AND** when a case was attempted, its failure SHALL follow the message as
  `\n→ Caused by: <failure>`

#### Scenario: Nullable union
- **GIVEN** a `UnionSource?` parameter and a `UnionSource?` return
- **WHEN** JavaScript passes `null` and `undefined`, and authored code returns
  `null`
- **THEN** authored code SHALL receive C# `null` for both inputs, and
  JavaScript SHALL receive `null`

#### Scenario: Encode each case
- **GIVEN** methods that return `Base64("a")`, `Bytes([1, 2])`, `Length(3)`,
  and a `null` non-nullable union
- **WHEN** JavaScript calls them
- **THEN** JavaScript SHALL receive `'a'`, an `ArrayBuffer` with bytes `1, 2`,
  and `3`
- **AND** the `null` call SHALL fail with a catchable JavaScript error
- **AND** the generated `Encode` SHALL end with the `InvalidOperationException`
  throw for a type that is not a case

#### Scenario: Union in every position
- **GIVEN** a union with cases `Text(string)` and `Buffer(ArrayBuffer)`, used
  as a record field, a list element, a dictionary value, a sync return, a
  `Task` result, a property, a typed event payload on a module and on a shared
  object, a callback argument, a callback result, a shared-object constructor
  parameter, and a shared-object method parameter
- **WHEN** JavaScript passes and receives values through each position
- **THEN** each SHALL decode and encode the case that matches the value
- **AND** a returned buffer SHALL keep its JavaScript identity and release
  exactly once
- **AND** an event payload or callback argument SHALL stay with the author, and
  a callback result SHALL belong to the author

#### Scenario: Leaf payload releases once
- **GIVEN** `string Write(Content content)` where `Content` has cases
  `Text(string)` and `Buffer(ArrayBuffer)`, on a module and on a shared object
- **WHEN** JavaScript passes a string and then an `ArrayBuffer`
- **THEN** the buffer lease SHALL release exactly once at invocation end
- **AND** the string call SHALL hold no lease

#### Scenario: A failed attempt releases what it decoded
- **GIVEN** a union with cases `Packed(Pair)` and then `Raw(JavaScriptValue)`,
  where `record Pair(ArrayBuffer Data, int Count)` is declared outside the
  union
- **WHEN** JavaScript passes `{ data: buffer, count: 'x' }`
- **THEN** the `Packed` attempt SHALL release its `Data` lease exactly once
- **AND** the value SHALL decode as `Raw`, whose leaf SHALL release exactly
  once at invocation end

#### Scenario: Case construction fails after its payload decoded
- **GIVEN** a case whose constructor throws for a decoded `ArrayBuffer`, before
  a `Raw(JavaScriptValue)` case
- **WHEN** JavaScript passes a buffer that the constructor rejects
- **THEN** the lease SHALL release exactly once
- **AND** the call SHALL fail with the constructor's error, and the `Raw` case
  SHALL NOT be attempted

#### Scenario: A release failure in an attempt propagates
- **GIVEN** the generated codec of a union whose first case has a leaf-bearing
  record payload
- **WHEN** the generator runs
- **THEN** each attempt SHALL catch `AggregateException` and rethrow it before
  the catch that moves to the next case
- **AND** a release failure during a failed attempt SHALL therefore propagate,
  and the later case SHALL NOT be attempted

#### Scenario: Leaf-free union keeps leaf-free glue
- **GIVEN** `int Pick(Choice value)` where `Choice` has cases `Number(double)`
  and `Flag(bool)`
- **WHEN** the generator runs
- **THEN** the generated text SHALL NOT contain `CodecRelease`, and the union
  codec SHALL have no `Release`

#### Scenario: Invalid declarations report EXPOJSI029
- **GIVEN** `[Union]` on a non-record class, on a generic abstract record, on
  an abstract record with a parameter list, on a private nested union, on a
  union with no cases, on a union whose cases are in two partial declarations,
  and on a union with a case `Many(IReadOnlyList<Self>)` that contains the
  union itself
- **WHEN** the generator runs, with or without a member that uses each union
- **THEN** it SHALL report `EXPOJSI029` once per declaration, naming the union
  and the reason

#### Scenario: Payload record from a referenced assembly
- **GIVEN** an assembly that declares
  `public record ExternalRecord(string Name, int Count)`, and a project that
  references it and declares `[Union] abstract record Sth` with cases
  `Foo(ExternalRecord Bar)` and `Text(string Value)`, and a method
  `string Describe(Sth value)`
- **WHEN** JavaScript passes `{ name: 'a', count: 2 }` and `'x'`
- **THEN** the generator SHALL report no diagnostic
- **AND** the first value SHALL decode as `Foo` whose `Bar` is
  `ExternalRecord("a", 2)`, and the second as `Text("x")`
- **AND** a method that returns `new Sth.Foo(new ExternalRecord("a", 2))` SHALL
  give JavaScript the object `{ name: 'a', count: 2 }`

#### Scenario: A union from a referenced assembly reports EXPOJSI029
- **GIVEN** a `[Union]` declared in a referenced assembly and a method
  parameter of that type
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI029` at the parameter, naming the union and
  its assembly
- **AND** the parameter SHALL report `EXPOJSI001`, and no binding SHALL be
  emitted for the method

#### Scenario: Invalid cases report EXPOJSI030
- **GIVEN** a union with a case that has no positional parameter, a case with
  two, a non-sealed case, a case with `string Value = ""`, a case with
  `string? Value`, a case with a shared-object payload, a case with a
  `JavaScriptCallback<int>` payload, a case `Address(Uri Value)` declared next
  to a case named `Uri`, and a top-level record that derives from the union
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI030` for each, naming the case or type, the
  union, and the reason
- **AND** the no-parameter message SHALL point to an `enum` payload, the
  two-parameter message SHALL point to a record payload, and the nullable
  message SHALL point to a nullable union at the use site
- **AND** a case declared as `Uri(Uri Value)` SHALL also report `EXPOJSI030`,
  next to the C# error CS8910

#### Scenario: A payload that contains itself is rejected
- **GIVEN** a case `Tree(Node)` where
  `record Node(string Name, IReadOnlyList<Node> Children)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI030` for the case before any payload codec
  resolves, and the build SHALL NOT crash

#### Scenario: Duplicate payloads report EXPOJSI031
- **GIVEN** a union with cases `A(string)` and `B(string)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI031` naming the union, the payload type, and
  both cases

#### Scenario: Unreachable cases report EXPOJSI032
- **GIVEN** unions where a case follows a `JavaScriptValue` case; an `int` case
  follows a `double` case; an `int` case followed by a `double` case follows a
  `long` case; a `Uri` case follows a `string` case; a `byte[]` case follows an
  `ArrayBuffer` case; a record case follows a
  `Dictionary<string, JavaScriptValue>` case; an `IReadOnlyList<int>` case
  follows an `IReadOnlyList<JavaScriptValue>` case; and a case with record
  payload `Circle(double X, double Y, double Radius)` follows a case with
  record payload `Point(double X, double Y)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI032` for each later case, naming it, the
  union, and the earlier case
- **AND** it SHALL NOT report `EXPOJSI032` for `int` then `double`, `Uri` then
  `string`, payload `Circle` then payload `Point`, `Dictionary<string, int>`
  then `IReadOnlyList<int>`, or payload `Point` then a record payload
  `Line(string X, double Y)`

#### Scenario: Reachable through record defaults
- **GIVEN** a union with cases `Dot(Point)` and then `Relaxed(Partial)`, where
  `record Point(double X, double Y)` and
  `record Partial(double X, double Y = 0, double Z = 0)`
- **WHEN** the generator runs and JavaScript passes `{ x: 1 }`
- **THEN** it SHALL NOT report `EXPOJSI032`
- **AND** the value SHALL decode as `Relaxed`

#### Scenario: A JavaScriptValue dictionary takes every object
- **GIVEN** a union with cases `Bag(Dictionary<string, JavaScriptValue>)` and
  then `Text(string)`
- **WHEN** JavaScript passes a plain object, an array, a `Uint8Array`, an
  object with a null prototype, a `Map`, a `Date`, and an object with a
  non-enumerable property
- **THEN** every value SHALL decode as `Bag`

#### Scenario: A case type outside its union is rejected
- **GIVEN** a method `int M(NonceInput.Bytes bytes)`
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI001` naming `NonceInput.Bytes`, and SHALL NOT
  generate a record codec for it

#### Scenario: Generated union codecs avoid reflection
- **GIVEN** the generated provider of a project that uses unions
- **WHEN** a reviewer searches it for `GetType(`, `typeof(`, `MethodInfo`,
  `Activator`, `DynamicInvoke`, and `object?[]`
- **THEN** the union codecs SHALL contain none of them

#### Scenario: Either is gone
- **GIVEN** the repository after this change
- **WHEN** a reviewer runs
  `git grep -n -e "Either[<]" -e "Either[C]odec" -e "IJavaScriptValueKind[C]odec" -- packages apps docs/specs docs/module-authoring-guide.md`
- **THEN** it SHALL print nothing

### Requirement: Uint8Array Is A Byte View

`Uint8Array` SHALL be accepted in every codec position where `ArrayBuffer` is,
each also as `Uint8Array?`, and as a union payload, through
`Uint8ArrayCodec`, an ordinary `IJavaScriptCodec<Uint8Array>` that holds a
leaf. A `Uint8Array` owns one `ArrayBuffer` lease plus an offset and a length.
Decoding SHALL prove the value is a `Uint8Array` by prototype-chain membership
(`Uint8Array.prototype.isPrototypeOf`), accepting a subclass instance and
rejecting every other shape with `InvalidOperationException` and the message
`Value is not a Uint8Array.`. It SHALL retain the backing buffer without
copying bytes, and SHALL keep the offset and length. Encoding SHALL produce a
JavaScript `Uint8Array` over the encoded buffer with the same offset and
length through the global constructor, and SHALL leave the view usable.
`Release` SHALL dispose the view, which releases its lease once; `Dispose` is
idempotent. The view SHALL fail loudly when the buffer no longer covers it.
`byte[]`, `ArrayBuffer`, `Memory<byte>`, and `ReadOnlyMemory<byte>` SHALL NOT
accept a `Uint8Array`. No native or ABI surface SHALL be added for typed
arrays.

#### Scenario: In-place write
- **GIVEN** a method `void Fill(Uint8Array bytes)` that writes `0x42` to every
  byte
- **WHEN** JavaScript passes `new Uint8Array(4)`, and then an instance of a
  `Uint8Array` subclass, and reads each after its call
- **THEN** every element SHALL be `0x42`
- **AND** the lease SHALL be released once at invocation end

#### Scenario: Sub-view
- **GIVEN** a method `string Hex(Uint8Array bytes)`
- **WHEN** JavaScript passes `new Uint8Array(buffer, 4, 8)` over a 16-byte
  buffer
- **THEN** authored code SHALL see `ByteOffset 4`, `Length 8`, and exactly
  those bytes

#### Scenario: Return and property
- **GIVEN** a method `Uint8Array Make()`, a `Task<Uint8Array>` method, and a
  `Uint8Array?` property
- **WHEN** JavaScript reads them
- **THEN** each SHALL be a `Uint8Array` with matching bytes, `byteOffset`, and
  `length`, and `null` for a null property

#### Scenario: Wrong shapes are rejected
- **GIVEN** the `Fill` parameter and a `Uint8Array?` parameter
- **WHEN** JavaScript passes an `ArrayBuffer`, an `Int32Array`, a `DataView`, a
  plain object, `5`, `null`, or `undefined`
- **THEN** `Fill` SHALL throw `Value is not a Uint8Array.` for every input
- **AND** the nullable parameter SHALL pass `null` for `null` and `undefined`
  and throw for the rest

#### Scenario: Composition follows ArrayBuffer
- **GIVEN** `IReadOnlyList<Uint8Array>` and `Dictionary<string, Uint8Array>`
  parameters and returns, and a record with a `Uint8Array` field
- **WHEN** JavaScript passes and receives them
- **THEN** each view SHALL release its lease exactly once through `Release`,
  as for `ArrayBuffer`

#### Scenario: Callback, event, and union
- **GIVEN** a callback whose argument and result are `Uint8Array?`, a
  `Func<Uint8Array?, Task>` event, and a `StringOrView` union parameter with
  cases `Text(string)` and `View(Uint8Array)`
- **WHEN** JavaScript passes a view or `null`
- **THEN** a view or `null` passed to the callback or the event SHALL stay with
  the author, and a view returned by the callback SHALL belong to the author
- **AND** the union SHALL decode a string as `Text` and a view as `View`
- **AND** it SHALL throw
  `'StringOrView' expected string or Uint8Array, but received ArrayBuffer` for
  an `ArrayBuffer`, and
  `'StringOrView' expected string or Uint8Array, but received object` for
  another object, whose `InnerException` is `Value is not a Uint8Array.`, so
  the JavaScript message ends with `\n→ Caused by: Value is not a Uint8Array.`
