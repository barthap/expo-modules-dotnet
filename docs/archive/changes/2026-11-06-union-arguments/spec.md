# Two-Member Union Arguments

## Goal

`Either<TFirst, TSecond>` works as a parameter type of generated module
methods and of supported shared-object constructors and methods, for
members whose JavaScript value kinds differ. The codec reads the value's
kind once, decodes through that member's codec, and authored code receives
a typed union. `Either<string, ArrayBuffer>` is the shape roadmap item 038
needs for `write`. This is roadmap item 035.

## Process note

The brief `docs/plans/035-union-arguments.md` was written after the 034
close and the bridge fix (`fbb3459`), and this spec follows it. Its "codec
shape" question is settled by decisions 1 to 3 below.

## Design criteria

1. **Clean code.** One carrier struct, one kind table on each side of the
   boundary (the generator's for diagnostics, the leaf codecs' for
   dispatch), no try-one-then-the-other decoding.
2. **Performance.** One `Kind` read, then the member codec. An ordinary
   union allocates no scope; a union with an `ArrayBuffer` member registers
   that one lease, exactly as a direct `ArrayBuffer` parameter does.
3. **Correctness.** A scoped member keeps the 030 ownership rules through
   the same `IGeneratedScopedCodec<T>` protocol; the carrier holds no
   wrapper of its own.

## Scope

### In scope

- `Expo.ModulesCore/Either.cs` (new).
- `Expo.ModulesCore/Codecs/IJavaScriptValueKindCodec.cs` (new interface)
  and its implementation on `StringCodec`, `BoolCodec`, `NumberCodec<T>`,
  `ArrayBufferCodec`, `ByteArrayCodec`, `MemoryByteCodec`,
  `ReadOnlyMemoryByteCodec`.
- `Expo.ModulesCore/Codecs/EitherCodec.cs`,
  `GeneratedScopedEitherCodec.cs`, `GeneratedScopedOrdinaryCodec.cs` (new).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` and the
  property path in `ExpoModulesGenerator.ModuleAnalysis.cs`.
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
- Union returns, async results, properties, record fields, and typed event
  payloads: decode-only. A union inside a list or dictionary composes
  through the scoped protocol with no new rule and is covered by one test
  each; it is not a design goal.

## Accepted design

### Carrier

```csharp
public readonly struct Either<TFirst, TSecond>
{
  public static Either<TFirst, TSecond> FromFirst(TFirst value);
  public static Either<TFirst, TSecond> FromSecond(TSecond value);
  public bool IsFirst { get; }
  public TFirst First { get; }    // throws InvalidOperationException when IsSecond
  public TSecond Second { get; }  // throws InvalidOperationException when IsFirst
  public TResult Match<TResult>(Func<TFirst, TResult> first, Func<TSecond, TResult> second);
}
```

`default(Either<,>)` is `IsFirst == false` with a default `Second`; the
codecs never produce it, and authored code is not expected to construct
unions.

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

### Codecs

```csharp
public readonly struct EitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IJavaScriptCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IJavaScriptCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IJavaScriptCodec<TSecond>, IJavaScriptValueKindCodec

public readonly struct GeneratedScopedEitherCodec<TFirst, TFirstCodec, TSecond, TSecondCodec>
    : IGeneratedScopedCodec<Either<TFirst, TSecond>>
    where TFirstCodec : IGeneratedScopedCodec<TFirst>, IJavaScriptValueKindCodec
    where TSecondCodec : IGeneratedScopedCodec<TSecond>, IJavaScriptValueKindCodec

public readonly struct GeneratedScopedOrdinaryCodec<T, TCodec>
    : IGeneratedScopedCodec<T>, IJavaScriptValueKindCodec
    where TCodec : IJavaScriptCodec<T>, IJavaScriptValueKindCodec
```

Decode in both union codecs: read `value.Kind` once; if it equals
`TFirstCodec.Kind`, return `FromFirst(TFirstCodec.Decode(...))`; else if it
equals `TSecondCodec.Kind`, return `FromSecond(...)`; else throw
`InvalidOperationException` with the message
`Value kind '{kind}' is not one of '{TFirstCodec.Kind}' or '{TSecondCodec.Kind}'.`
`Transfer` and `EncodeBorrowed` / `Encode` delegate to the active member.
`GeneratedScopedOrdinaryCodec<T, TCodec>` lifts an ordinary leaf codec into
the scoped protocol: `Decode` ignores the scope, `Transfer` is a no-op,
`EncodeBorrowed` is `Encode`, and `Kind` forwards. It is what lets
`Either<string, ArrayBuffer>` pair an ordinary member with a scoped one.

### Generator

`GetCodec` resolves `global::Expo.ModulesCore.Either<TFirst, TSecond>` by
`ConstructedFrom`, like `IReadOnlyList<T>`. Each member type SHALL map to a
kind through a generator-side table keyed on the type symbol: `string` →
`String`; `bool` → `Bool`; the types that resolve to `NumberCodec<T>` →
`Number`; `ArrayBuffer`, `byte[]`, `Memory<byte>`, `ReadOnlyMemory<byte>`
→ `ArrayBuffer`. A member outside the table, or two members with the same
kind, SHALL report the parameter's existing diagnostic (`EXPOJSI001`, or
`EXPOJSI023` inside a shared-object class), which names the member and the
full union type; no new diagnostic ID and no new message text, because
those diagnostics carry no reason slot.

Descriptor:

- both members ordinary: `OrdinaryCodec("EitherCodec<...>") with { Capabilities = DecodeOnly }`;
- any member scoped: `ScopedCodec("GeneratedScopedEitherCodec<...>") with { Capabilities = DecodeOnly }`,
  where an ordinary member codec is wrapped as
  `GeneratedScopedOrdinaryCodec<T, TCodec>`.

`DecodeOnly` keeps returns and async results rejected through the existing
checks, and record fields through the record codec's `DecodeAndEncode`
requirement. Three gates did not exist before this item and are added so
that "decode-only" holds everywhere: a property rejects a decode-only
codec as a return does; a callback type argument or typed event payload
requires an encode-capable `JavaScriptCodec`, not only the shape; and a
nullable, list, or dictionary composite takes its child's capabilities, so
`Either<,>?` and `IReadOnlyList<Either<,>>` stay decode-only. No earlier
descriptor was decode-only with the `JavaScriptCodec` or `ScopedOnly`
shape, so these gates change no existing output. `Either<,>?` composes
through `GeneratedScopedNullableCodec` (scoped) or `NullableCodec`
(ordinary).

Type-argument text uses `CodecTypeArgumentFormat` as every other composite
does. Emission does not change.

### Semantics

- JavaScript passes a string to `Either<string, ArrayBuffer>`: authored
  code receives `IsFirst` with the string. It passes an `ArrayBuffer`:
  `IsSecond` with a scoped `ArrayBuffer` released once at invocation end.
- `null`, `undefined`, and any value of a third kind fail with the union
  codec's message before authored code runs. `Either<,>?` turns `null` and
  `undefined` into C# `null` and still rejects a third kind.
- `Either<double, bool>`: `0`, `false`, `''` are decoded by kind, not by
  truthiness (`''` is a third kind and fails).

## Decisions made by the controller

1. **Kind lives on the leaf codecs** (`IJavaScriptValueKindCodec`), read
   by the union codec, and the generator keeps its own type-keyed table
   for build diagnostics. The alternatives: a codec-string table in the
   generator (forbidden since 029); `typeof` comparisons in a static
   initializer (a second runtime table); try-first-then-second decoding
   (exception-driven, ambiguous errors). Cost if wrong: seven one-line
   properties to remove.
2. **Two union codecs plus one lifting adapter** instead of one. The
   ordinary one keeps leaf-free bindings scope-free (a spec rule); the
   scoped one needs both children on the scoped protocol, and the adapter
   is the missing general piece that record codecs today inline. Cost if
   wrong: one struct.
3. **Decode-only.** Encode of a union is one line per member, but no
   roadmap item returns one. Cost if wrong: flip one capability and add a
   test.
4. **Member kinds limited to `String`, `Bool`, `Number`, `ArrayBuffer`.**
   `Object`-kind members need a second discriminator and are deferred with
   036's typed arrays.
5. **Name `Either`**, matching upstream Swift.

## Delta against `docs/specs/modules-core-boundary.md`

### ADDED Requirement: Two-Member Unions Decode By Value Kind

`Either<TFirst, TSecond>` SHALL be accepted as a parameter type of
generated module methods and of supported shared-object constructors and
methods when each member type maps to one of the JavaScript value kinds
`String`, `Bool`, `Number`, or `ArrayBuffer`, and the two kinds differ. The
codec SHALL read the value kind once and decode through that member's
codec; it SHALL NOT try one member and fall back to the other. A member of
any other kind, or two members of the same kind, SHALL report the
position's existing diagnostic. A scoped member SHALL keep the 030
ownership rules; an ordinary union SHALL NOT allocate a scope. Unions
SHALL be decode-only.

#### Scenario: String or ArrayBuffer parameter

- **GIVEN** a method `string Write(Either<string, ArrayBuffer> content)`
  on a module and on a shared object
- **WHEN** JavaScript passes a string and then an `ArrayBuffer`
- **THEN** authored code SHALL receive `IsFirst` with the string, then
  `IsSecond` with a buffer whose bytes match
- **AND** the buffer lease SHALL be released once at invocation end, and no
  lease SHALL be held after the string call

#### Scenario: Third kind and nullish are rejected

- **GIVEN** the same parameter and an `Either<string, ArrayBuffer>?` parameter
- **WHEN** JavaScript passes `5`, `{}`, `null`, or `undefined`
- **THEN** the non-nullable parameter SHALL throw the union codec's kind
  message for every input
- **AND** the nullable parameter SHALL pass C# `null` for `null` and
  `undefined` and SHALL throw for `5` and `{}`

#### Scenario: Ordinary union keeps the binding scope-free

- **GIVEN** `int Pick(Either<double, bool> value)`
- **WHEN** the generator runs and JavaScript passes `0`, `1.5`, `false`, `true`, `''`
- **THEN** the generated text SHALL NOT contain `GeneratedConversionScope`
- **AND** `0` and `1.5` SHALL arrive as `IsFirst`, `false` and `true` as
  `IsSecond`, and `''` SHALL throw

#### Scenario: Unsupported members report a diagnostic

- **GIVEN** `Either<string, Uri>`, `Either<string, SharedObjectClass>`,
  `Either<Holder, int>`, `Either<double, int>`, and `Either<,>` as a return,
  async result, property, or record field
- **WHEN** the generator runs
- **THEN** it SHALL report the position's existing diagnostic, naming the
  member and the union type, and SHALL NOT emit the member

### Position and descriptor tables

Add rows for `Either<,>` (parameter only): ordinary `EitherCodec`
(`JavaScriptCodec`, `Borrowed`, decode-only) and scoped
`GeneratedScopedEitherCodec` (`ScopedOnly`, `ScopedResource`, decode-only).

## Delta against `docs/specs/ownership-and-scoped-refs.md`

Under "Boundary Leaf Ownership Is Explicit", add: a union member that is an
`ArrayBuffer` is a scoped leaf registered once; the other member holds no
leaf.

## Module authoring guide

Parameters section: `Either<TFirst, TSecond>` for `string | ArrayBuffer`
style arguments; members limited to string, bool, numbers, and binary;
decode-only.

## Roadmap

Row 035 `IN PROGRESS` with this spec, `DONE` at close with a new 035 note.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
rg -n 'catch \(InvalidOperationException\)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs
rg -n 'EitherCodec<[^>]*(SharedObject|JavaScriptValue|JavaScriptCallback)' packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator.Tests
git diff --unified=0 fbb3459..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

All scans print nothing.
