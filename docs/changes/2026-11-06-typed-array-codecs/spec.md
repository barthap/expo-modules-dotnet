# Uint8Array Codecs

## Goal

`Uint8Array` crosses the boundary in every codec position where
`ArrayBuffer` does: module parameters, returns, `Task<Uint8Array>`,
properties, shared-object members, `Uint8Array?`, list and dictionary
positions, record fields, callback type arguments, and typed-event payloads.
It is also a union member. A JavaScript `Uint8Array` decodes without a byte
copy as a view over its backing buffer, so authored code can write in
place; a managed `Uint8Array` encodes as a new JavaScript `Uint8Array` over
the same bytes. No ABI change. This is roadmap item 036. Roadmap items 038
(`bytes()`) and 025 (`getRandomValues`, `digest`) need it.

## Process note

The brief `docs/plans/036-typed-array-codecs.md` was written at the 035
close (`b7966f3`) and this spec follows it. Its ABI question is settled:
every operation is a property read or a global constructor call that
`Expo.JSI` already exposes, so no value passes the "ABI Carries Only Host
Knowledge" test.

## Design criteria

1. **Clean code.** One managed type that is a view (`ArrayBuffer` lease,
   offset, length), one leaf codec with the `ArrayBufferCodec` shape, one
   generator row, and one row in the union kind table. No typed-array kind
   enum until a second kind ships.
2. **Performance.** Decode proves the kind with three property reads and
   one call, then retains the backing buffer and reads two numbers; no
   copy. Encode encodes the buffer as today and calls the constructor
   once. The constructor is read from the global object per encode: two
   property reads, no cached handle. A leaf-free member keeps its generated
   text.
3. **Correctness.** The view's bytes are a slice of the buffer's bytes
   under the buffer's captured-length check, so a detached or resized
   buffer fails loudly. The view is a leaf: generated glue releases it
   exactly once through `Uint8ArrayCodec.Release`, as for `ArrayBuffer`.

## Scope

### In scope

- `Expo.ModulesCore/Uint8Array.cs` (new) and
  `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs` (new).
- `Expo.ModulesCore/Codecs/IJavaScriptCodec.cs` (the doc comment that
  defines a leaf).
- `Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs` (one
  `HoldsLeaves` row next to `ArrayBufferCodec`, one row in
  `GetUnionMemberKind`).
- `Expo.ModulesCore.Generator.Tests` and `Expo.ModulesCore.Tests`.
- `docs/specs/modules-core-boundary.md`,
  `docs/specs/ownership-and-scoped-refs.md`,
  `docs/module-authoring-guide.md`, `docs/roadmap.md`, and the
  `expo-jsi-managed-handle-lifetime` skill.

### Out of scope

- Other typed-array kinds. 025 decides whether `getRandomValues` needs a
  kind-generic view.
- Accepting a `Uint8Array` where `ArrayBuffer`, `byte[]`, `Memory<byte>`,
  or `ReadOnlyMemory<byte>` is declared. Those stay exact.
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

`Uint8ArrayCodec` is an ordinary `IJavaScriptCodec<Uint8Array>` with the
`ArrayBufferCodec` shape: `Decode(ref, runtime)`, `Decode(owned, runtime)`,
`Encode`, `Release`, and `EncodeCopy(ReadOnlySpan<byte>)`. It implements
`IJavaScriptValueKindCodec` with `Kind == Object`.

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
the caller, and the view stays usable. `EncodeCopy` copies bytes into a
native buffer and encodes a view with offset 0.

Release: `value?.Dispose()`. A null view releases nothing, and a second
release does nothing.

### Generator

`Uint8Array` resolves to `OrdinaryCodec("Uint8ArrayCodec")` with
`ResourceOwnership = HoldsLeaves`, next to `ArrayBufferCodec`. Every other
position follows from the existing descriptors with no new rule:
`Uint8Array?` is `NullableReferenceCodec<Uint8Array, Uint8ArrayCodec>`;
lists, dictionaries, records, and unions hold leaves through
`CompositeCodec`; callback type arguments take any `IJavaScriptCodec<T>`;
and a direct `Func<Uint8Array, Task>` or `Func<Uint8Array?, Task>` event
payload takes the `Codec` payload kind and encodes through the codec, like
a nested leaf. `GetUnionMemberKind` maps `Uint8Array` to `Object`.

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
- Lifetimes follow the four leaf rules: a decoded view is released once at
  invocation end (async: at settlement); a `Retain()` copy outlives the
  call; a returned view belongs to generated glue; a view passed to a
  callback or an event stays with the author; a view returned by a
  callback belongs to the author.
- `Either<string, Uint8Array>` decodes a string as its first member and a
  `Uint8Array` as its second. An `ArrayBuffer` fails with the union's kind
  message, and another object fails with `Value is not a Uint8Array.`.

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
6. **The view is a leaf, in every position.** `Uint8ArrayCodec` overrides
   `Release` and the generator marks it `HoldsLeaves`, so it composes like
   `ArrayBuffer` everywhere, callback type arguments and typed-event
   payloads included. A direct event payload encodes through the codec on
   the owning runtime and stays with the author, as a nested leaf does; it
   does not get the direct `ArrayBuffer` scheduling lease.
7. **Union member of kind `Object`.** The union reads the value kind once
   and then calls the member codec, which runs its own prototype test. So
   `Uint8Array` maps to `Object` in the kind table, and
   `Either<string, Uint8Array>` and `Either<ArrayBuffer, Uint8Array>` are
   valid unions. A second view shares the kind and stays rejected, and
   records and other objects stay outside the table. A view passed to
   `Either<string, ArrayBuffer>` keeps reporting
   `Value kind 'Object' is not one of 'String' or 'ArrayBuffer'.`.

## Delta against `docs/specs/modules-core-boundary.md`

### ADDED Requirement: Uint8Array Is A Byte View

`Uint8Array` SHALL be accepted in every codec position where `ArrayBuffer`
is, each also as `Uint8Array?`, and as a union member of kind `Object`,
through `Uint8ArrayCodec`, an ordinary `IJavaScriptCodec<Uint8Array>` that
holds a leaf. Decoding SHALL prove the value is a `Uint8Array` by
prototype-chain membership (`Uint8Array.prototype.isPrototypeOf`), accepting
a subclass instance and rejecting every other shape, SHALL retain the
backing buffer without copying bytes, and SHALL keep the offset and length.
Encoding SHALL produce a JavaScript `Uint8Array` over the encoded buffer
with the same offset and length through the global constructor, and SHALL
leave the view usable. `Release` SHALL dispose the view. The view SHALL fail
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
- **THEN** each view SHALL release its lease exactly once through
  `Release`, as for `ArrayBuffer`

#### Scenario: Callback, event, and union

- **GIVEN** a callback whose argument and result are `Uint8Array?`, a
  `Func<Uint8Array?, Task>` event, and an `Either<string, Uint8Array>`
  parameter
- **WHEN** JavaScript passes a view or `null`
- **THEN** a view or `null` passed to the callback or the event SHALL stay
  with the author, and a view returned by the callback SHALL belong to the
  author
- **AND** the union SHALL decode a string as its first member and a view as
  its second, SHALL throw the kind message for an `ArrayBuffer`, and SHALL
  throw `Value is not a Uint8Array.` for another object

### Delta against other requirements

- "Module-facing ArrayBuffer And Binary Codecs": add a `Uint8Array` row to
  the binary codec table.
- "Codecs Release Leaves Through One Protocol": `Uint8ArrayCodec` disposes
  the leaf, and it implements `IJavaScriptValueKindCodec` with `Object`.
- "Generated Bindings Release Leaves After Each Call": the descriptor table
  row for leaves names `Uint8Array`.
- "Typed Event Payload Ownership Is Explicit": a payload that holds a
  `Uint8Array`, and a direct `Uint8Array` or `Uint8Array?`, is valid and
  stays with the author.
- "Two-Member Unions Decode By Value Kind": the kinds gain `Object`, and
  `Uint8Array` maps to it.
- "Nullable Reference Codecs Are Annotation-Driven": `Uint8Array?` is
  supported through `NullableReferenceCodec`.

## Delta against `docs/specs/ownership-and-scoped-refs.md`

- "Binary Borrowing And Ownership": a decoded `Uint8Array` holds one
  retained lease on the JavaScript buffer, with no byte copy, and its byte
  callbacks borrow only its slice.
- "Boundary Leaf Ownership Is Explicit": a leaf is a `JavaScriptValue`, an
  `ArrayBuffer`, or a `Uint8Array`; a `Uint8Array` owns one buffer lease; a
  null `Uint8Array?` holds no leaf; a `Uint8Array` union member is a leaf.

## Delta against `docs/specs/runtime-and-abi.md`

None. Record in the 036 roadmap note that typed arrays need no ABI entry.

## Module authoring guide

Binary data bullet: add `Uint8Array` (view, no copy, offset and length;
every position; any `Uint8Array` instance, including a subclass such as a
`Buffer` polyfill, is accepted); note that `byte[]` and `ArrayBuffer` do not
accept one. The union bullet, the callback bullet, the leaf rules, the
nullable reference paragraph, and the event payload section name it next
to `ArrayBuffer`.

## Roadmap

Row 036 `IN PROGRESS` with this spec, `DONE` at close with a new 036 note.

## Verification

```sh
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
git diff --stat b7966f3..HEAD -- packages/expo-modules-dotnet/native
rg -n 'Uint8Array' packages/expo-modules-dotnet/native
git diff --unified=0 b7966f3..HEAD -- docs packages | rg -n '/[U]sers/[A-Za-z0-9._-]+/|[A-Za-z]:\\\\[U]sers\\\\'
```

The native diff and both scans print nothing.
