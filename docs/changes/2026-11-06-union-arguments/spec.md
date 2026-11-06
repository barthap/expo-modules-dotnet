# Two-Member Union Arguments

## Goal

`Either<TFirst, TSecond>` works in every generated codec position for
members whose JavaScript value kinds differ. The codec reads the value's
kind once, decodes through that member's codec, and authored code receives
a typed union. `Either<string, ArrayBuffer>` is the shape roadmap item 038
needs for `write`. This is roadmap item 035.

## Process note

The brief `docs/plans/035-union-arguments.md` was written after the 034
close and the bridge fix (`6fc0e5a`), and this spec follows it. Its "codec
shape" question is settled by decisions 1 to 3 below, and its encode
question by decision 3.

## Design criteria

1. **Clean code.** One carrier struct, one codec, one kind table on each
   side of the boundary (the generator's for diagnostics, the leaf codecs'
   for dispatch), no try-one-then-the-other decoding.
2. **Performance.** One `Kind` read, then the member codec. A union with
   leaf-free members keeps leaf-free generated text; a union with an
   `ArrayBuffer` member releases that one lease, exactly as a direct
   `ArrayBuffer` parameter does.
3. **Correctness.** A leaf-bearing member keeps the 030 ownership rules
   through the same `IJavaScriptCodec<T>.Release` protocol; the carrier holds
   no wrapper of its own.

## Scope

### In scope

- `Expo.ModulesCore/Either.cs` (new).
- `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs` (new interface)
  and its implementation on `StringCodec`, `BoolCodec`, `NumberCodec<T>`,
  `ArrayBufferCodec`, `ByteArrayCodec`, `MemoryByteCodec`,
  `ReadOnlyMemoryByteCodec`.
- `Expo.ModulesCore/Codecs/EitherCodec.cs` (new).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs`.
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`,
  `docs/module-authoring-guide.md`, `docs/roadmap.md`.

### Out of scope

- `packages/expo-file-system-dotnet` `write`: roadmap 038.
- Three or more members.
- Members of kind `Object` or `Function` (records, lists, dictionaries,
  shared objects, callbacks, `JavaScriptValue`, typed arrays): two such
  members cannot be told apart by kind. They report a diagnostic.

## Accepted design

### Carrier

```csharp
public readonly struct Either<TFirst, TSecond>
{
  public static Either<TFirst, TSecond> FromFirst(TFirst value);
  public static Either<TFirst, TSecond> FromSecond(TSecond value);
  public bool IsFirst { get; }
  public TFirst First { get; }    // throws InvalidOperationException when !IsFirst
  public TSecond Second { get; }  // throws InvalidOperationException when IsFirst
  public TResult Match<TResult>(Func<TFirst, TResult> first, Func<TSecond, TResult> second);
}
```

`default(Either<,>)` is `IsFirst == false` with a default `Second`. The
codec never decodes it. Authored code creates a union with `FromFirst` or
`FromSecond` when it returns, stores, or emits one.

### Kind on the leaf codecs

```csharp
public interface IJavaScriptValueKindCodec
{
  static abstract JavaScriptValueKind Kind { get; }
}
```

`StringCodec` → `String`; `BoolCodec` → `Bool`; `NumberCodec<T>` →
`Number`; `ArrayBufferCodec`, `ByteArrayCodec`, `MemoryByteCodec`,
`ReadOnlyMemoryByteCodec` → `ArrayBuffer`. Each is one static property.
Nothing else implements it; a type that does not implement it cannot be a
union member, which the generator enforces first.

### Codec

```csharp
public readonly struct EitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IJavaScriptCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IJavaScriptCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IJavaScriptCodec<TSecond>, IJavaScriptValueKindCodec
```

Decode: read `value.Kind` once; if it equals `TFirstCodec.Kind`, return
`FromFirst(TFirstCodec.Decode(...))`; else if it equals `TSecondCodec.Kind`,
return `FromSecond(...)`; else throw `InvalidOperationException` with the
message
`Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.`
Only one member decodes, so a failed decode has no partial result to
release. `Encode` and `Release` delegate to the active member's codec:
`Release` of a string member does nothing, and `Release` of an
`ArrayBuffer` member disposes that buffer.

### Generator

`ResolveCodec` resolves `global::Expo.ModulesCore.Either<TFirst, TSecond>`
by `ConstructedFrom`, like `IReadOnlyList<T>`, before the nullable and
record branches. Each member type SHALL map to a kind through a
generator-side table keyed on the type symbol: `string` → `String`; `bool`
→ `Bool`; the types that resolve to `NumberCodec<T>` → `Number`;
`ArrayBuffer`, `byte[]`, `Memory<byte>`, `ReadOnlyMemory<byte>` →
`ArrayBuffer`. An annotated member is outside the table. A member outside
the table, or two members with the same kind, SHALL report the position's
existing diagnostic (`EXPOJSI001`, or `EXPOJSI023` inside a shared-object
class), which names the full union type; no new diagnostic ID and no new
message text, because those diagnostics carry no reason slot. The kinds are
checked before any member codec resolves, so a rejected union adds no
record codec and no diagnostic as a side effect.

Descriptor: `OrdinaryCodec("EitherCodec<...>")`, made `HoldsLeaves` through
the existing `CompositeCodec` rule when either member holds leaves. The
union is `DecodeAndEncode`, so it composes everywhere an ordinary codec
does: `Either<,>?` through `NullableCodec`, lists and dictionaries, record
fields, callback type arguments, and typed event payloads. Type-argument
text uses `CodecTypeArgumentFormat` as every other composite does. Emission
does not change.

### Semantics

- JavaScript passes a string to `Either<string, ArrayBuffer>`: authored
  code receives `IsFirst` with the string. It passes an `ArrayBuffer`: the
  second member with an `ArrayBuffer` that the glue releases once at
  invocation end, or at settlement for an async method.
- `null`, `undefined`, and any value of a third kind fail with the union
  codec's message before authored code runs. `Either<,>?` turns `null` and
  `undefined` into C# `null` and still rejects a third kind.
- `Either<double, bool>`: `0`, `false`, `''` are decoded by kind, not by
  truthiness (`''` is a third kind and fails).
- A returned union belongs to generated glue, which encodes it and releases
  its leaf once. A union passed to a callback or an event stays with the
  author. A union returned by a callback belongs to the author.

## Decisions made by the controller

1. **Kind lives on the leaf codecs** (`IJavaScriptValueKindCodec`), read
   by the union codec, and the generator keeps its own type-keyed table
   for build diagnostics. The alternatives: a codec-string table in the
   generator (forbidden since 029); `typeof` comparisons in a static
   initializer (a second runtime table); try-first-then-second decoding
   (exception-driven, ambiguous errors). Cost if wrong: seven one-line
   properties to remove.
2. **One union codec.** `EitherCodec` is an ordinary `IJavaScriptCodec<T>`
   whose `Release` releases the member that is set. A union with leaf-free
   members has a `Borrowed` descriptor, so its glue stays leaf-free; one
   with an `ArrayBuffer` member is `HoldsLeaves` through the existing
   composite rule. No second codec and no adapter.
3. **Every position, not decode-only.** The union always knows which
   member it holds, so `Encode` is one line per member with no ambiguity,
   and the 030 rules already say who owns a returned, stored, emitted, or
   callback-passed leaf. Returns, async results, properties, record fields,
   callback type arguments, and event payloads therefore need no rule of
   their own, and no generator gate is added to reject them. Cost if
   wrong: one gate and the tests that pin it.
4. **Member kinds limited to `String`, `Bool`, `Number`, `ArrayBuffer`.**
   `Object`-kind members need a second discriminator and are deferred with
   036's typed arrays.
5. **Name `Either`**, matching upstream Swift.

## Delta against `docs/specs/modules-core-boundary.md`

### ADDED Requirement: Two-Member Unions Decode By Value Kind

`Either<TFirst, TSecond>` SHALL be accepted in every codec position when
each member type maps to one of the JavaScript value kinds `String`,
`Bool`, `Number`, or `ArrayBuffer`, and the two kinds differ.
`EitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>` SHALL read the
value kind once and decode through that member's codec; it SHALL NOT try
one member and fall back to the other. A value of any other kind SHALL
throw the kind message. `Encode` and `Release` SHALL use the member that
the union holds. The union holds leaves when a member does. A member of any
other kind, an annotated member, or two members of the same kind SHALL
report the position's existing diagnostic.

#### Scenario: String or ArrayBuffer parameter

- **GIVEN** a method `string Write(Either<string, ArrayBuffer> content)`
  on a module and on a shared object
- **WHEN** JavaScript passes a string and then an `ArrayBuffer`
- **THEN** authored code SHALL receive `IsFirst` with the string, then
  the second member with a buffer whose bytes match
- **AND** the buffer lease SHALL release once at invocation end, and the
  string call SHALL hold no lease

#### Scenario: Third kind and nullish are rejected

- **GIVEN** the same parameter and an `Either<string, ArrayBuffer>?` parameter
- **WHEN** JavaScript passes `5`, `{}`, `null`, or `undefined`
- **THEN** the non-nullable parameter SHALL throw the union codec's kind
  message for every input
- **AND** the nullable parameter SHALL pass C# `null` for `null` and
  `undefined` and SHALL throw for `5` and `{}`

#### Scenario: Leaf-free union keeps leaf-free glue

- **GIVEN** `int Pick(Either<double, bool> value)`
- **WHEN** the generator runs and JavaScript passes `0`, `1.5`, `false`, `true`, `''`
- **THEN** the generated text SHALL NOT contain `CodecRelease`
- **AND** `0` and `1.5` SHALL arrive as `IsFirst`, `false` and `true` as
  the second member, and `''` SHALL throw

#### Scenario: Union in encode positions

- **GIVEN** a sync or async method that returns its
  `Either<string, ArrayBuffer>` input, a record field, a property that
  stores `Retain()` copies, an event payload, a callback argument, and a
  callback result of that type
- **WHEN** JavaScript passes an `ArrayBuffer`
- **THEN** a returned buffer SHALL keep its JavaScript identity and release
  exactly once
- **AND** an event payload or callback argument SHALL stay with the author,
  and a callback result SHALL belong to the author

#### Scenario: Unsupported members report a diagnostic

- **GIVEN** `Either<string, Uri>`, `Either<string, Holder>` for a record,
  `Either<string, Entry>` for a shared-object class, `Either<string?,
double>`, and `Either<double, int>`
- **WHEN** the generator runs
- **THEN** it SHALL report the position's existing diagnostic, naming the
  union type, and SHALL NOT emit the member

### Codec protocol and descriptor table

In "Codecs Release Leaves Through One Protocol", the union joins the
composites that release each child through the child codec, releasing only
the member it holds, and `IJavaScriptValueKindCodec` is named with its
seven implementations. In the descriptor table of "Generated Bindings
Release Leaves After Each Call", the composite row becomes "List,
dictionary, nullable, or `Either<TFirst, TSecond>` over a `HoldsLeaves`
child".

## Delta against `docs/specs/ownership-and-scoped-refs.md`

Under "Boundary Leaf Ownership Is Explicit", add: a union holds only the
leaves of the member it holds; an `ArrayBuffer` member is a leaf, and a
string, bool, or number member holds none.

## Module authoring guide

Supported types: `Either<TFirst, TSecond>` for `string | ArrayBuffer`
style values; members limited to string, bool, numbers, and binary; works
in every position; an `ArrayBuffer` member follows the leaf rules.

## Roadmap

Row 035 `IN PROGRESS` with this spec, `DONE` at close with a new 035 note.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs
rg -n 'EitherCodec<[^>]*(SharedObject|JavaScriptValue|JavaScriptCallback)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --unified=0 6fc0e5a..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

All scans print nothing.
