# Memory Byte Codecs Delta Spec

## Goal

Add copy-based `Memory<byte>` and `ReadOnlyMemory<byte>` support to generated
Expo module bindings. Reorganize and document the public `ArrayBuffer` surface
without changing its behavior.

## Scope

### Included

- Public `MemoryByteCodec` and `ReadOnlyMemoryByteCodec` types that implement
  `IJavaScriptCodec<T>`.
- Generated binding support for exactly `System.Memory<byte>` and
  `System.ReadOnlyMemory<byte>` in every ordinary codec position.
- Copy-isolation, sliced-memory, async, and generic-codec coverage.
- XML documentation for the public `ArrayBuffer` surface, its byte-access
  delegates, and the new codecs.
- Reordering the existing `ArrayBuffer` members into discovery-oriented groups.

### Excluded

- Zero-copy managed views over JavaScript storage.
- Managed-array pinning, `IMemoryOwner<byte>`, streams, typed-array views, or
  `SharedArrayBuffer` support.
- `ArrayBuffer.ToMemory()` convenience APIs.
- Native bridge, ABI, scheduler, and platform-adapter changes.

## Accepted Design

`MemoryByteCodec.Decode` and `ReadOnlyMemoryByteCodec.Decode` SHALL decode an
accepted JavaScript `ArrayBuffer` through `ByteArrayCodec` and wrap the fresh
managed array. The resulting memory is independent of both the original
JavaScript buffer and the temporary module-facing `ArrayBuffer` wrapper.

Both codecs SHALL encode by passing `value.Span` to
`ArrayBufferCodec.EncodeCopy`. Encoding copies exactly the logical memory
slice, including a non-zero offset, into native-backed storage. It does not
pin, adopt, or alias the source memory. The source storage must remain valid
until the synchronous encode call completes; for an asynchronous generated
return, encoding occurs during Promise settlement.

The generator SHALL recognize only the exact `byte` specializations as normal
codecs. They are not span passing kinds, so the one-span and synchronous-span
restrictions do not apply. Other `Memory<T>` and `ReadOnlyMemory<T>` types
continue through the normal unsupported-type diagnostic path.

`ArrayBuffer.cs` SHALL keep all existing members and behavior. It SHALL group
factories, ownership, snapshot/copy operations, synchronous access,
asynchronous access, internal encoding, disposal, and private helpers in that
order. XML documentation SHALL describe ownership, copy semantics, scoped span
validity, cancellation, and disposal where those rules apply.

## Delta Requirements

### ADDED Requirement: Memory Byte Values Cross The Module Boundary By Copy

Generated bindings SHALL support `Memory<byte>` and `ReadOnlyMemory<byte>` as
ordinary copy codecs.

#### Scenario: JavaScript buffer decodes into mutable memory
- **GIVEN** a generated method declares a `Memory<byte>` parameter
- **WHEN** JavaScript supplies an ArrayBuffer
- **THEN** generated dispatch SHALL copy its bytes into new managed storage
- **AND** later mutation of either the JavaScript buffer or the managed memory
  SHALL NOT change the other

#### Scenario: Read-only memory encodes an exact slice
- **GIVEN** authored code returns a `ReadOnlyMemory<byte>` over a subrange
- **WHEN** generated dispatch encodes the result
- **THEN** the returned JavaScript ArrayBuffer SHALL contain only that subrange
- **AND** it SHALL not retain, pin, or alias the source storage

#### Scenario: Memory values are valid in asynchronous bindings
- **GIVEN** a generated asynchronous method accepts or returns either memory
  type
- **WHEN** it is invoked from JavaScript
- **THEN** generated dispatch SHALL use the ordinary codec path without span
  diagnostics or scoped byte callbacks
- **AND** Promise settlement SHALL encode the returned value using the current
  logical slice

#### Scenario: Non-byte memory remains unsupported
- **GIVEN** an authored member declares `Memory<T>` or `ReadOnlyMemory<T>`
  where `T` is not `byte`
- **WHEN** the generator analyzes that member
- **THEN** it SHALL report the existing unsupported-type diagnostic

### MODIFIED Requirement: Module-facing ArrayBuffer And Binary Codecs

`ArrayBuffer` documentation and member order SHALL make its existing
explicit-ownership, copy, and scoped-access contract discoverable without
changing the API or behavior.
