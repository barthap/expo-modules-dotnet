# Uint8Array Codecs

## Goal

`Uint8Array` crosses the boundary wherever `ArrayBuffer` does: module
parameters, returns, `Task<Uint8Array>`, properties, shared-object members,
`Uint8Array?`, and list and dictionary positions. A JavaScript `Uint8Array`
decodes without a byte copy as a view over its backing buffer, so authored
code can write in place; a managed `Uint8Array` encodes as a new JavaScript
`Uint8Array` over the same bytes. No ABI change. This is roadmap item 036.
Roadmap items 038 (`bytes()`) and 025 (`getRandomValues`, `digest`) need
it.

## Process note

The brief `docs/plans/036-typed-array-codecs.md` was written at the 035
close (`babcba8`) and this spec follows it. Its ABI question is settled:
every operation is a property read or a global constructor call that
`Expo.JSI` already exposes, so no value passes the "ABI Carries Only Host
Knowledge" test.

## Design criteria

1. **Clean code.** One managed type that is a view (`ArrayBuffer` lease,
   offset, length), one codec with the `ArrayBufferCodec` shape, one
   generator row. No typed-array kind enum until a second kind ships.
2. **Performance.** Decode proves the kind with three property reads and
   one call, then retains the backing buffer and reads two numbers; no
   copy. Encode encodes the buffer as today and calls the constructor
   once. The constructor is read from the global object per encode: two
   property reads, no cached handle.
3. **Correctness.** The view's bytes are a slice of the buffer's bytes
   under the buffer's captured-length check, so a detached or resized
   buffer fails loudly. The lease is released once at invocation end, as
   for `ArrayBuffer`.

## Scope

### In scope

- `Expo.ModulesCore/Uint8Array.cs` (new) and
  `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs` (new).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (one scoped
  row next to `ArrayBufferCodec`; typed-event payload classification if the
  031 path needs a name).
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`,
  `docs/module-authoring-guide.md`, `docs/roadmap.md`.

### Out of scope

- Other typed-array kinds. 025 decides whether `getRandomValues` needs a
  kind-generic view.
- Accepting a `Uint8Array` where `ArrayBuffer`, `byte[]`, `Memory<byte>`,
  or `ReadOnlyMemory<byte>` is declared. Those stay exact.
- A union member of type `Uint8Array` (035's discriminator is the value
  kind; a typed array is `Object`).
- `packages/expo-file-system-dotnet` and `packages/expo-crypto-dotnet`
  members: 038 and 025.
- Any native or ABI change.

## Accepted design

### Type

```csharp
public sealed class Uint8Array : IDisposable
{
  public static Uint8Array Allocate(int length);              // native-backed buffer, offset 0
  public static Uint8Array CopyFrom(ReadOnlySpan<byte> bytes);
  public ArrayBuffer Buffer { get; }                          // the backing lease (whole buffer)
  public int ByteOffset { get; }
  public int Length { get; }
  public Uint8Array Retain();
  public byte[] ToArray();
  public void WithBytes(ArrayBufferBytesAction action);
  public TResult WithBytes<TResult>(ArrayBufferBytesFunc<TResult> action);
  public void WithReadOnlyBytes(ArrayBufferReadOnlyBytesAction action);
  public TResult WithReadOnlyBytes<TResult>(ArrayBufferReadOnlyBytesFunc<TResult> action);
  public void Dispose();                                      // disposes the lease; idempotent
}
```

The internal constructor takes `(ArrayBuffer buffer, int byteOffset, int
length)` and requires `0 <= byteOffset`, `0 <= length`, and
`byteOffset + length <= buffer.ByteLength`. Every byte access slices the
buffer's span by offset and length after the buffer's own captured-length
check. `Buffer` is the view's own lease; `Dispose` disposes it. `Retain`
retains the buffer and returns an independent view with the same offset
and length. Async variants mirror `ArrayBuffer`'s (same delegate types).

### Codec

`Uint8ArrayCodec` has the `ArrayBufferCodec` shape: `Decode(ref, runtime)`,
`Decode(owned, runtime)`, `Encode`, `EncodeCopy(ReadOnlySpan<byte>)`,
scoped `Decode(ref, runtime, scope)`, `Transfer`, `EncodeBorrowed`, and
implements `IGeneratedScopedCodec<Uint8Array>`.

Decode: the value SHALL be an object whose prototype chain contains the
global `Uint8Array.prototype`, tested by calling
`Uint8Array.prototype.isPrototypeOf` with the value through
`JavaScriptFunction.CallWithThis` (the `instanceof` test); otherwise throw
`InvalidOperationException("Value is not a Uint8Array.")`. Then read
`buffer` (must be `IsArrayBuffer`), `byteOffset`, and `byteLength` through
property reads, decode the buffer exactly as `ArrayBufferCodec.Decode`
does, and construct the view. A JavaScript subclass instance, such as a
`Buffer` polyfill, passes the prototype test and decodes like a plain
`Uint8Array`; an `ArrayBuffer`, another typed array, a `DataView`, or a
plain object does not.

Encode: `value.Buffer.Encode(runtime)` gives the buffer value as today;
then `runtime.Global().GetProperty("Uint8Array").AsFunction().CallAsConstructor(bufferValue, runtime.CreateNumber(byteOffset), runtime.CreateNumber(length))`.
Every intermediate wrapper is disposed; the constructed value is owned by
the caller. `EncodeCopy` copies bytes into a native buffer and encodes a
view with offset 0.

### Generator

`Uint8Array` resolves to `ScopedCodec("Uint8ArrayCodec")` next to
`ArrayBufferCodec`. Nullable, list, dictionary, record-field, callback, and
typed-event positions follow 030 and 031 with no new rule: `Uint8Array?`
is `GeneratedScopedNullableReferenceCodec<Uint8Array, Uint8ArrayCodec>`; a
direct `Func<Uint8Array, Task>` or `Func<Uint8Array?, Task>` event payload
is accepted only if `TryGetEventDelegatePayload` classifies it through the
same descriptor row as `ArrayBuffer` without an `ArrayBuffer`-specific
branch; if the 031 path names `ArrayBuffer` by type, typed-event payloads
stay out of scope and the plan says so.

### Semantics

- JS `new Uint8Array(16)` → `Length 16`, `ByteOffset 0`, bytes shared:
  authored writes are visible to JavaScript after the call.
- JS `new Uint8Array(buffer, 4, 8)` → `ByteOffset 4`, `Length 8`, bytes are
  the slice.
- `ArrayBuffer`, `Int32Array`, `DataView`, a plain object, a primitive,
  `null`, and `undefined` throw `Value is not a Uint8Array.` on a
  non-nullable parameter; `Uint8Array?` passes `null` for nullish input.
- Return and property: JavaScript receives a `Uint8Array` whose
  `byteOffset`, `length`, and bytes match; `instanceof Uint8Array` holds.
- Lifetimes: a decoded view is scope-owned and released once at invocation
  end; a `Retain()` copy outlives the call; a returned managed view is
  transferred like a returned `ArrayBuffer`.

## Decisions made by the controller

1. **No ABI entry.** Kind proof, buffer, offset, length, and construction
   are portable JSI property reads and one constructor call the managed
   side already has. Cost if wrong: an `isTypedArray` ABI call later would
   save two property reads per decode.
2. **Prototype-chain membership, the `instanceof` test.** `Expo.JSI` has
   no `instanceof` operator, but it can call
   `Uint8Array.prototype.isPrototypeOf` with a `this` value, which is the
   same test. It accepts a subclass instance such as React Native's
   `Buffer` polyfill, which upstream accepts too and which a 038 `write`
   caller will pass. Constructor identity was considered and dropped
   because it would refuse exactly those callers. Both tests can be
   spoofed by an object built on `Uint8Array.prototype` with its own
   `buffer`, `byteOffset`, and `byteLength`; such an object decodes as a
   bounds-checked view, so this is a fidelity limit, not a safety hole.
   The only unspoofable test is a native `isTypedArray` ABI entry, which
   decision 1 rejects. Cost if wrong: one ABI entry later.
3. **Constructor read per encode.** No cached function on
   `DotnetRuntimeContext`: no new owned handle, no teardown rule. Cost if
   wrong: one cached handle later.
4. **One kind.** `Uint8Array` is its own sealed type; a `TypedArray<T>` or
   kind enum is additive if 025 needs it.
5. **Exact binary codecs stay exact.** `byte[]` and `ArrayBuffer` do not
   accept a `Uint8Array`; authors declare `Uint8Array` when JavaScript
   sends one.
6. **No union membership and no union-message change.** A `Uint8Array`
   is kind `Object`, so it cannot be a 035 union member, and a view passed
   to `Either<string, ArrayBuffer>` keeps reporting
   `Value kind 'Object' is not one of 'String' or 'ArrayBuffer'.` (the
   035 final review suggested a typed-array-aware message; it needs a
   constructor test inside the union codec and is deferred to the 038
   `write` brief, which decides whether `write` accepts a view at all).

## Delta against `docs/specs/modules-core-boundary.md`

### ADDED Requirement: Uint8Array Is A Scoped Byte View

`Uint8Array` SHALL be accepted wherever `ArrayBuffer` is, through
`Uint8ArrayCodec`, a `ScopedOnly`, `ScopedResource` codec. Decoding SHALL
prove the value is a `Uint8Array` by prototype-chain membership
(`Uint8Array.prototype.isPrototypeOf`), accepting a subclass instance and
rejecting every other shape, SHALL retain the backing buffer
without copying bytes, and SHALL keep the offset and length. Encoding SHALL
produce a JavaScript `Uint8Array` over the encoded buffer with the same
offset and length through the global constructor. The view SHALL fail
loudly when the buffer no longer covers it. No native or ABI surface SHALL
be added for typed arrays.

#### Scenario: In-place write

- **GIVEN** a method `void Fill(Uint8Array bytes)` that writes `0x42` to
  every byte
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

- **GIVEN** a method `Uint8Array Make()`, a `Task<Uint8Array>` method, and
  a `Uint8Array?` property
- **WHEN** JavaScript reads them
- **THEN** each SHALL be a `Uint8Array` with matching bytes, `byteOffset`,
  and `length`, and `null` for a null property

#### Scenario: Wrong shapes are rejected

- **GIVEN** the `Fill` parameter and a `Uint8Array?` parameter
- **WHEN** JavaScript passes an `ArrayBuffer`, an `Int32Array`, a
  `DataView`, a plain object, `5`, `null`, or `undefined`
- **THEN** `Fill` SHALL throw `Value is not a Uint8Array.` for every input
- **AND** the nullable parameter SHALL pass `null` for `null` and
  `undefined` and throw for the rest

#### Scenario: Composition follows ArrayBuffer

- **GIVEN** `IReadOnlyList<Uint8Array>` and `Dictionary<string, Uint8Array>`
  parameters and returns, and a record with a `Uint8Array` field
- **WHEN** JavaScript passes and receives them
- **THEN** each leaf SHALL be registered once and released once, as the
  030 ownership matrix states for `ArrayBuffer`

### Delta against "Module-facing ArrayBuffer And Binary Codecs"

Add `Uint8Array` to the binary codec list as the typed-array view with
offset and length.

## Delta against `docs/specs/ownership-and-scoped-refs.md`

"Binary Borrowing And Ownership" and "Conversion Scope Owns Boundary
Wrapper Leaves": a `Uint8Array` view is a leaf that holds one buffer lease;
the leaf list in the scope requirement names it next to `ArrayBuffer`.

## Delta against `docs/specs/runtime-and-abi.md`

None. Record in the 036 roadmap note that typed arrays need no ABI entry.

## Module authoring guide

Binary data bullet: add `Uint8Array` (view, no copy, offset and length;
any `Uint8Array` instance, including a subclass such as a `Buffer`
polyfill, is accepted); note that `byte[]` and `ArrayBuffer` do not accept
one.

## Roadmap

Row 036 `IN PROGRESS` with this spec, `DONE` at close with a new 036 note.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
git diff --stat babcba8..HEAD -- packages/expo-modules-dotnet/native
rg -n 'Uint8Array' packages/expo-modules-dotnet/native
git diff --unified=0 babcba8..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

The native diff and both scans print nothing.
