# Modules Core Records And Enums

## Purpose

Define how authored records and string enums cross between C# and
JavaScript.

## Requirements

### Requirement: Generated Record Codecs Use Lower-Camel JavaScript Fields

Generated record codecs SHALL model the authored C# property name separately
from its JavaScript field name. Encoding and decoding SHALL use only the
lower-camel JavaScript name, while generated direct C# access SHALL continue to
use the authored C# name. Decoding SHALL NOT probe or fall back to a PascalCase
JavaScript field. A missing lower-camel field SHALL read as JavaScript
`undefined`. Generated decode SHALL then apply "Record Fields With Defaults Are
Optional In JavaScript" and "Required Record Fields Name The Missing Field".

#### Scenario: Record is encoded for JavaScript
- **GIVEN** a supported C# record has `Name`, `Age`, and `Summary` properties
- **WHEN** generated glue encodes the record
- **THEN** the JavaScript object SHALL have `name`, `age`, and `summary` own
  properties
- **AND** generated C# access SHALL still use `Name`, `Age`, and `Summary`

#### Scenario: Record is decoded from JavaScript
- **GIVEN** a supported record expects a `Name` property in C#
- **WHEN** generated glue decodes a JavaScript object
- **THEN** it SHALL read only the `name` JavaScript property
- **AND** it SHALL NOT read or fall back to `Name`

#### Scenario: Lower-camel record field is missing
- **GIVEN** a JavaScript object supplies only a stale PascalCase field
- **WHEN** generated glue reads the absent lower-camel field as `undefined`
- **THEN** a required field SHALL fail with the required-field error that names
  the lower-camel field, through the normal catchable JavaScript error path
- **AND** a nullable field without a default SHALL decode as `null`
- **AND** a field with a C# default SHALL decode as that default

### Requirement: Record Fields With Defaults Are Optional In JavaScript

A generated record codec SHALL treat a positional record constructor parameter
that has a C# default value as an optional JavaScript field. When the field
reads as JavaScript `undefined`, because the key is absent or holds explicit
`undefined`, generated decode SHALL use the authored default and SHALL NOT call
the field codec. Any other value, including JavaScript `null`, SHALL decode
through the field codec. The generator SHALL inline the default as a
compile-time C# expression, without reflection, boxing or dynamic invocation.
Both generated `Decode` overloads SHALL behave the same. Encoding SHALL write
every field, including a field whose value equals its default. A record field
that has a default and whose type is `JavaScriptValue`, or a `record struct`
that holds a `JavaScriptValue`, SHALL be a build error: the generator SHALL
report `EXPOJSI007` with a message that says the field cannot have a default
value, and SHALL NOT emit a codec for the record. A method parameter of type
`JavaScriptValue` with a default SHALL keep its current behavior.

#### Scenario: Omitted or undefined defaulted field uses the default
- **GIVEN** a record `Options(string Name, int Count = 5)` is a `[JS]` method
  parameter
- **WHEN** JavaScript passes `{ name: 'a' }` or `{ name: 'a', count: undefined }`
- **THEN** authored code SHALL receive `Count == 5`

#### Scenario: Supplied value wins over the default
- **GIVEN** the same record
- **WHEN** JavaScript passes `{ name: 'a', count: 41 }`
- **THEN** authored code SHALL receive `Count == 41`

#### Scenario: Explicit null on a nullable defaulted field stays null
- **GIVEN** a record field `string? Label = "x"`
- **WHEN** JavaScript passes `{ label: null }`, and then an object without
  `label`
- **THEN** authored code SHALL receive `null` for the first call
- **AND** it SHALL receive `"x"` for the second call

#### Scenario: Explicit null on a non-nullable defaulted field fails
- **GIVEN** a record field `int Count = 5`
- **WHEN** JavaScript passes `{ count: null }`
- **THEN** the call SHALL fail through the field codec with a catchable
  JavaScript `Error`
- **AND** authored code SHALL NOT run

#### Scenario: Enum and value-type defaults
- **GIVEN** a `readonly record struct Settings(Mode Mode = Mode.Fast, TimeSpan
  Delay = default, Mode? Fallback = Mode.Slow)`
- **WHEN** JavaScript passes `{}`
- **THEN** authored code SHALL receive `Mode.Fast`, `TimeSpan.Zero` and
  `Mode.Slow`

#### Scenario: Owned decode path applies the default
- **GIVEN** a `[JS]` method takes `JavaScriptCallback<Options>`
- **WHEN** the JavaScript callback returns `{ name: 'a' }` and authored code
  calls `Invoke()`
- **THEN** `Invoke()` SHALL return a record with `Count == 5`

#### Scenario: Encoding writes every field
- **GIVEN** an authored method returns `new Options("a")`
- **WHEN** JavaScript reads the result
- **THEN** the object SHALL have own properties `name` and `count`, with
  `count === 5`

#### Scenario: Defaulted leaf field is released exactly once
- **GIVEN** a record `Blob(string Name, ArrayBuffer? Data = null)` is a `[JS]`
  method parameter
- **WHEN** JavaScript passes `{ name: 'a' }`, and then
  `{ name: 'a', data: buffer }`
- **THEN** authored code SHALL receive `null` for the first call, and no leaf
  SHALL be released
- **AND** for the second call the decoded buffer SHALL be released exactly once
  after the call

#### Scenario: Default on a JavaScriptValue record field is a build error
- **GIVEN** a record `Holder(string Name, JavaScriptValue V = null)`, and a
  record `Wrapper(Slot Slot = default)` where `Slot` is a `record struct` that
  holds a `JavaScriptValue`
- **WHEN** the generator runs on a module with a `[JS]` method that takes each
  record
- **THEN** it SHALL report `EXPOJSI007` for `V` with the message
  `Record 'Holder' field 'V' of type 'global::Expo.JSI.JavaScriptValue' cannot have a default value`
- **AND** it SHALL report `EXPOJSI007` for `Slot` with the message
  `Record 'Wrapper' field 'Slot' of type '<Slot type>' cannot have a default value because the type holds a JavaScriptValue`,
  where `<Slot type>` is the `global::`-qualified name of `Slot`
- **AND** it SHALL NOT emit a record codec for either record

#### Scenario: Unsupported field type message does not change
- **GIVEN** a record `Bad(decimal Value)` used by a `[JS]` method
- **WHEN** the generator runs
- **THEN** it SHALL report `EXPOJSI007` with the message
  `Record 'Bad' field 'Value' uses unsupported type 'System.Decimal'`

#### Scenario: JavaScriptValue field without a default and method parameter default stay accepted
- **GIVEN** a record `Holder(string Name, JavaScriptValue V)` and a `[JS]`
  method `Run(JavaScriptValue value = null)`
- **WHEN** the generator runs
- **THEN** it SHALL report no diagnostic
- **AND** an omitted `v` field SHALL hold a JavaScript `undefined` value

#### Scenario: Generated decode inlines the default
- **GIVEN** a record field `int Count = 5`
- **WHEN** the generator emits the record codec
- **THEN** both `Decode` overloads SHALL contain a `JavaScriptValueKind.Undefined`
  check that yields the literal `5`
- **AND** the generated source SHALL NOT use reflection to read the default

### Requirement: Required Record Fields Name The Missing Field

A generated record field SHALL be required when it has no C# default and its C#
type is not a nullable value type, not an annotated nullable reference type,
and not `JavaScriptValue`. When a required field reads as JavaScript
`undefined`, generated decode SHALL throw before it calls the field codec. The
message SHALL be `Value for property '<field>' is required, got undefined`,
where `<field>` is the lower-camel JavaScript name. The failure SHALL reach
JavaScript through the normal path for its position: a sync `Error`, an async
rejection, or a failed callback `Invoke()`. Leaves that earlier fields already
decoded SHALL be released once. Explicit `null` on a required field SHALL decode
through the field codec. A field that is not required and has no default SHALL
keep its codec's behavior for `undefined`.

#### Scenario: Missing required field is named
- **GIVEN** a record `CodecUser(string Name, int Age)` is a `[JS]` method
  parameter
- **WHEN** JavaScript passes `{ age: 37 }`
- **THEN** JavaScript SHALL catch an `Error` whose message contains
  `Value for property 'name' is required, got undefined`
- **AND** authored code SHALL NOT run

#### Scenario: Async method rejects on a missing required field
- **GIVEN** an async `[JS]` method takes the same record
- **WHEN** JavaScript passes `{ age: 37 }`
- **THEN** the call SHALL return a promise that rejects with an `Error` whose
  message contains `Value for property 'name' is required`
- **AND** the call SHALL NOT throw synchronously

#### Scenario: Nullable and JavaScriptValue fields stay optional
- **GIVEN** records with an `int? LuckyNumber` field and a `JavaScriptValue
  Value` field, neither with a default
- **WHEN** JavaScript omits each field
- **THEN** `LuckyNumber` SHALL decode as `null`
- **AND** `Value` SHALL hold a JavaScript `undefined` value

#### Scenario: Earlier leaf is released when a later required field is missing
- **GIVEN** a record `CountedBuffer(ArrayBuffer Buffer, int Count)` is a `[JS]`
  method parameter
- **WHEN** JavaScript passes `{ buffer: b }`
- **THEN** the call SHALL fail with the required-field error naming `count`
- **AND** the decoded buffer SHALL be released exactly once

### Requirement: String Enums Cross As Declared JavaScript Values

The effective representation of an enum at a codec position SHALL be the
usage-level `[JSEnum]` on the parameter or return value when present, else the
enum type's `[JSEnum]` when present, else `String`. The usage-level attribute
SHALL apply to the enum inside `Nullable<TEnum>` at that position, and a
return attribute SHALL apply to the result of a `Task<T>` return value, with or
without `Nullable<TEnum>` inside it. It SHALL NOT reach into a collection,
record, callback, event, or union.

Each member of a string enum SHALL have one JavaScript string: the `Value` of
its `System.Runtime.Serialization.EnumMemberAttribute` when the attribute sets
`Value`, otherwise its C# member name verbatim. The generator SHALL read these
strings at build time, from source or from metadata, and SHALL emit one private
codec per string enum in each provider. The codec SHALL use constant `switch`
expressions only, with no runtime reflection, `Enum.TryParse`,
`Enum.IsDefined`, or `Enum.ToString`.

Decode SHALL accept exactly the declared strings, compared by ordinal and
case-sensitively. Any other string SHALL throw `ArgumentException` with the
message below, where the received and declared strings appear verbatim, the
declared strings are in declaration order, and `<Enum>` is the enum's full C#
name without `global::`, with `.` before a nested enum:

`'<value>' is not a valid <Enum> value. It must be one of: '<s1>', '<s2>', ...`

A value that is not a string SHALL fail as it does for `string`. Encode SHALL
produce the member's declared string. A value that names no member, including a
`[Flags]` combination, SHALL throw `InvalidOperationException` with the message
`<number> is not a member of <Enum>, so it has no JavaScript value.`, where the
number is the underlying value in the invariant culture. These errors SHALL have
no code: JavaScript SHALL receive them as catchable `Error`s with no `code`
property from sync members, and as rejected promises from async members.

The generated enum codec SHALL compose in every codec position where an enum is
accepted, SHALL hold no leaves, and SHALL ignore the runtime context. As a union
payload, a string enum SHALL have the kind `String`; a string it does not
declare SHALL fail that case's attempt, so decoding moves to the next case.

#### Scenario: Declared strings cross both ways
- **GIVEN** `enum DigestAlgorithm { MD5, [EnumMember(Value = "SHA-1")] SHA1 }`
  and `[JS] DigestAlgorithm Echo(DigestAlgorithm algorithm)`
- **WHEN** JavaScript calls `echo('SHA-1')` and `echo('MD5')`
- **THEN** authored code SHALL receive `SHA1` and `MD5`
- **AND** JavaScript SHALL receive `'SHA-1'` and `'MD5'`

#### Scenario: Only declared strings decode
- **GIVEN** the same enum
- **WHEN** JavaScript passes `'SHA1'`, `'sha-1'`, `' SHA-1'`, `'1'`, or `1`
- **THEN** the call SHALL throw a catchable `Error` with no `code` before
  authored code runs
- **AND** for each string, the message SHALL contain the received string,
  `DigestAlgorithm`, `'MD5'`, and `'SHA-1'`

#### Scenario: Number strings no longer decode plain enums
- **GIVEN** `enum Mode { Slow, Fast }` with no attributes
- **WHEN** JavaScript passes `'Fast'` and then `'0'`
- **THEN** `'Fast'` SHALL decode to `Mode.Fast`
- **AND** `'0'` SHALL throw a catchable `Error`

#### Scenario: A value with no member does not encode
- **GIVEN** a method that returns `(DigestAlgorithm)42`, and a method that
  returns the `[Flags]` combination `Upper | Spaced` of a string enum
- **WHEN** JavaScript calls them
- **THEN** each call SHALL throw a catchable `Error` whose message contains the
  number (`42`, `3`) and the enum's name

#### Scenario: Async functions reject for a bad string
- **GIVEN** `[JS] Task<string> HashAsync(DigestAlgorithm algorithm)`
- **WHEN** JavaScript calls it with `'MD2'`
- **THEN** it SHALL return a rejected promise and SHALL NOT throw synchronously

#### Scenario: Declared strings in composite positions
- **GIVEN** echo methods for `record Options(DigestAlgorithm Algorithm)`,
  `IReadOnlyList<DigestAlgorithm>`, `DigestAlgorithm?`, and
  `Dictionary<string, DigestAlgorithm>`
- **WHEN** JavaScript passes `{ algorithm: 'SHA-1' }`, `['SHA-1', 'MD5']`,
  `'SHA-1'`, `null`, and `{ a: 'SHA-1' }`
- **THEN** each call SHALL return the same JavaScript value it received, with
  the declared strings
- **AND** `null` SHALL reach authored code as `null`

#### Scenario: Declared strings in other positions
- **GIVEN** a `DigestAlgorithm` property, a getter-only constant, an async
  method returning `Task<DigestAlgorithm>`, a callback that receives a
  `(DigestAlgorithm, int)` argument tuple and returns a `DigestAlgorithm`, a
  typed event whose payload is a `DigestAlgorithm` and one whose payload record
  has a `DigestAlgorithm` field, and a shared object whose constructor, method,
  and property take or return `DigestAlgorithm`
- **WHEN** each crosses with the member `SHA1`
- **THEN** JavaScript SHALL see `'SHA-1'` in each, and `'SHA-1'` passed from
  JavaScript SHALL reach authored code as `SHA1`

#### Scenario: Defaults are members
- **GIVEN** `[JS] DigestAlgorithm Pick(DigestAlgorithm algorithm = DigestAlgorithm.SHA1)`
  and `record Settings(string Label, DigestAlgorithm Algorithm = DigestAlgorithm.SHA1)`
- **WHEN** JavaScript omits the argument and the field
- **THEN** authored code SHALL receive `SHA1`, rendered as a cast of its
  underlying value and never decoded from a string
- **AND** JavaScript SHALL receive `'SHA-1'` back

#### Scenario: String enums as union payloads
- **GIVEN** a union with cases `Algorithm(DigestAlgorithm)`,
  `Output(DigestEncoding)` where `DigestEncoding` declares `'hex'` and
  `'base64'`, and `Text(string)`
- **WHEN** JavaScript passes `'SHA-1'`, `'hex'`, and `'SHA1'`
- **THEN** they SHALL decode as `Algorithm(SHA1)`, `Output(Hex)`, and
  `Text("SHA1")`
- **AND** a union with only the two enum cases SHALL reject `'nope'` with
  `UnionCaseMismatchException` whose cause is the `DigestEncoding` decode error

#### Scenario: Number enums do not change
- **GIVEN** `[JSEnum(EnumRepresentation.Number)] enum Level { [EnumMember] Low = 1, High = 2 }`
- **WHEN** JavaScript passes `2`
- **THEN** authored code SHALL receive `Level.High` and JavaScript SHALL receive
  `2` back

#### Scenario: One codec per enum per provider
- **GIVEN** two modules and a shared object in one assembly that all use
  `DigestAlgorithm` in several positions
- **WHEN** the project is compiled
- **THEN** the provider SHALL contain exactly one generated codec for
  `DigestAlgorithm`
- **AND** it SHALL contain no `Enum.TryParse` or `Enum.IsDefined`

#### Scenario: An enum from a referenced assembly
- **GIVEN** a string enum with `[EnumMember(Value = "SHA-1")]` declared in a
  referenced assembly
- **WHEN** a module in the consuming project uses it
- **THEN** the generated codec SHALL use `'SHA-1'`, read from metadata

### Requirement: Invalid String Enum Declarations Are Build Diagnostics

The generator SHALL report, at error severity, for each enum that reaches a
codec position, including a union payload and a typed event payload:
`EXPOJSI033` for an `EnumMember` `Value` that is `null`, empty, or holds a lone
surrogate, for an `EnumMember` `Value` on an enum whose own `[JSEnum]` is
`Number`, and, at the parameter or return value, for a usage-level
`[JSEnum(EnumRepresentation.Number)]` on an enum where a member sets `Value`;
`EXPOJSI034` for two members of a string enum that cross as the same string by
ordinal comparison; `EXPOJSI035` for two members of a string enum with the same
underlying value. `[EnumMember]` without `Value` SHALL NOT be reported. The
string checks (an invalid `Value`, `EXPOJSI034`, and `EXPOJSI035`) SHALL run
only where the enum crosses as a string, so a number enum MAY alias members.

A diagnostic about a member of an enum declared in the compiling assembly SHALL
be reported at the member. For an enum from a referenced assembly it SHALL be
reported at the using parameter, return, property, record field, or event, and
SHALL name the enum's assembly. A pair diagnostic SHALL be reported at the later
member and name the earlier one, and a pair with the same underlying value SHALL
report only `EXPOJSI035`. The provider SHALL report equal diagnostics with these
IDs (same ID, location, and arguments) once, so each is reported once per
declaration however many positions use the enum, and once per using position
for an enum from a referenced assembly. The `[Union]` declaration pipeline SHALL
NOT report them; a union case whose payload is such an enum SHALL report
`EXPOJSI030` with a reason that names the enum and these IDs. An enum with any
of these errors SHALL resolve to no codec where the error applies, so the using
position SHALL also report its existing unsupported-type diagnostic, and no
binding SHALL be emitted for it.

#### Scenario: Invalid values report EXPOJSI033
- **GIVEN** string enum members with `[EnumMember(Value = "")]`,
  `[EnumMember(Value = null)]`, and `[EnumMember(Value = "\uD800")]`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI033` once for each member, naming
  the enum, the member, and the reason

#### Scenario: Values on a number enum report EXPOJSI033
- **GIVEN** `[JSEnum(EnumRepresentation.Number)] enum Level { [EnumMember(Value = "low")] Low }`
  used by a method
- **AND** a string enum with `[EnumMember(Value = "SHA-1")]` used by a parameter
  with `[JSEnum(EnumRepresentation.Number)]`
- **WHEN** the project is compiled
- **THEN** the generator SHALL report `EXPOJSI033` at `Low` and at the parameter
- **AND** a parameter of `Level` with `[JSEnum(EnumRepresentation.String)]`
  SHALL still have no codec, and a string parameter of the string enum SHALL
  still bind

#### Scenario: Duplicate strings report EXPOJSI034
- **GIVEN** `enum E { [EnumMember(Value = "B")] A, B }`
- **WHEN** a method uses `E`
- **THEN** the generator SHALL report `EXPOJSI034` once, naming `A`, `B`, and
  `"B"`

#### Scenario: Aliased members report EXPOJSI035
- **GIVEN** `enum Mode { Slow, Fast, Default = Fast }` as a string enum
- **WHEN** a method uses `Mode`
- **THEN** the generator SHALL report `EXPOJSI035` once, naming `Fast`,
  `Default`, and the value `1`
- **AND** the same enum with `[JSEnum(EnumRepresentation.Number)]` SHALL report
  nothing, unless a parameter uses it with
  `[JSEnum(EnumRepresentation.String)]`

#### Scenario: One report for many uses
- **GIVEN** an enum with a duplicate string, used by two modules, a record
  field, a list, a union payload, and a shared-object method
- **WHEN** the project is compiled
- **THEN** `EXPOJSI034` SHALL be reported exactly once

#### Scenario: A bad enum from a referenced assembly
- **GIVEN** a referenced assembly declares a string enum with two members that
  share one string
- **WHEN** a consuming module uses it as a parameter
- **THEN** the generator SHALL report `EXPOJSI034` at the parameter, naming the
  enum and its assembly
- **AND** the parameter SHALL report `EXPOJSI001`, and no binding SHALL be
  emitted for the method

#### Scenario: A bad enum as a union payload
- **GIVEN** a union case `Pick(Bad Value)` where `Bad` has a duplicate string
- **WHEN** a method uses the union
- **THEN** the case SHALL report `EXPOJSI030` naming `Bad` and `EXPOJSI034`,
  `EXPOJSI034` SHALL be reported once, and the method SHALL report `EXPOJSI001`
- **AND** when no member uses the union, only `EXPOJSI030` SHALL be reported
