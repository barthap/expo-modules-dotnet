# 036: Carry `Uint8Array` across the boundary without a byte copy

> **Deliver with `living-spec-workflow`**: brainstorm, delta spec, plan,
> implement, verify, merge into `docs/specs/`. Set this item's row in
> `docs/roadmap.md` to `IN PROGRESS` when you start and `DONE` when the
> slice closes, then delete this brief (git history keeps it).
>
> **Drift check (run first)**: `git diff --stat b7966f3..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/ArrayBuffer.cs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/Codecs packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesGenerator.Codecs.cs packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptRuntime.cs packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptFunction.cs packages/expo-modules-dotnet/managed/packages/Expo.JSI/JavaScriptObjectRef.cs packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp docs/specs/modules-core-boundary.md docs/specs/ownership-and-scoped-refs.md docs/specs/runtime-and-abi.md`
> If an in-scope file changed, compare "Current state" with the live code.
> A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED. A typed array is a view with an offset and a length over a
  buffer the managed side already knows how to lease; the view must stay
  inside the buffer and must be released exactly once, as a buffer is.
- **Depends on**: 030
- **Category**: direction
- **Planned at**: `b7966f3` (the 035 close), 2026-11-06

## Why this matters

Upstream `expo-file-system` returns `Uint8Array` from `bytes()`, and
upstream `expo-crypto` fills the caller's typed array in
`getRandomValues` and reads one in `digest`. The bridge classifies a
`Uint8Array` as a plain `Object`, so today every binary codec throws on it
and no module can return one. Roadmap 038 and 025 need it. This item adds
one managed `Uint8Array` type and its codec, built from JavaScript features
the managed side can already reach, with no new ABI surface.

## Current state

- `ExpoJsiBridge.cpp`, `getValueKind`: an object is `Function`,
  `ArrayBuffer`, or `Object`. A `Uint8Array` is `Object`. There is no
  typed-array ABI entry and no `isTypedArray` check.
- `Expo.ModulesCore/ArrayBuffer.cs`: owned lease over JavaScript-backed or
  native-backed storage; `Allocate`, `CopyFrom`, `Retain`, `WithBytes`,
  `WithReadOnlyBytes`, async variants, `ByteLength` validated against the
  captured length. `Codecs/ArrayBufferCodec.cs` is the leaf codec
  exemplar: `Decode`, `Encode`, `Release` (disposes the buffer), `EncodeCopy`,
  and `Kind` from `IJavaScriptValueKindCodec`.
- `Codecs/ByteArrayCodec.cs`: `byte[]` decodes through `ArrayBufferCodec`
  and copies, so `byte[]` also rejects a `Uint8Array`.
- `Expo.JSI`: `JavaScriptObjectRef.GetProperty(string)` returns a
  `JavaScriptValueRef`; `JavaScriptValueRef.AsArrayBuffer()`;
  `JavaScriptRuntime.Global()`; `JavaScriptFunction.CallAsConstructor` and
  `CallWithThis`.
- `ExpoModulesGenerator.Codecs.cs` (`ResolveCodec`): `ArrayBufferCodec` is
  an ordinary descriptor with `HoldsLeaves`. `CompositeCodec` makes a list,
  dictionary, nullable, or union hold leaves when its child does, and
  `GetUnionMemberKind` maps a union member type to a value kind.
- `docs/specs/ownership-and-scoped-refs.md`, "Boundary Leaf Ownership Is
  Explicit", and `docs/specs/modules-core-boundary.md`, "Codecs Release
  Leaves Through One Protocol": a leaf codec overrides `Release`, and every
  composite releases its leaves through the child codec.
- `docs/specs/runtime-and-abi.md`, "ABI Carries Only Host Knowledge": a
  value that portable .NET can compute from inputs it already holds stays in
  managed code.
- Upstream reference: `expo-modules-core/common/cpp/JSI/TypedArray.h` reads
  `byteOffset`, `byteLength`, and `.buffer` through JSI, and
  `ios/Core/TypedArrays/TypedArray.swift` keeps a `kind` enum. The C# port
  can do the same through property reads.

## Constraints

- No ABI change. Reading `.buffer`, `.byteOffset`, and `.byteLength`, and
  calling the global `Uint8Array` constructor, are portable JSI operations
  the managed side already exposes. Any proposal for a typed-array ABI entry
  must name which of host identity, host policy, or host-owned handle it
  carries; none applies, so it is a STOP condition.
- Zero copy on decode. The view leases the backing `ArrayBuffer` and keeps
  the offset and length; authored code sees the same bytes JavaScript
  sees, so `getRandomValues` can write in place.
- The view is a leaf. Its codec is one ordinary `IJavaScriptCodec<T>` with a
  `Release` override, and the generator marks it `HoldsLeaves`, so it
  composes through the existing descriptors with no special case.
- No reflection, no `object` boxing, no JSON.

## Scope

**In scope**:

- `Expo.ModulesCore/Uint8Array.cs` (new): sealed, `IDisposable`, owns one
  `ArrayBuffer` lease plus `ByteOffset` and `Length`; `Allocate(length)`,
  `CopyFrom(span)`, `Retain`, `WithBytes`, `WithReadOnlyBytes`, `ToArray`,
  and `Buffer` access.
- `Expo.ModulesCore/Codecs/Uint8ArrayCodec.cs` (new): the `ArrayBufferCodec`
  shape.
- `ExpoModulesGenerator.Codecs.cs`: resolve `Uint8Array` as a leaf-holding
  ordinary codec, so `Uint8Array?`, lists, dictionaries, records, callbacks,
  and events compose with no new rule.
- Generator tests, Hermes-backed runtime tests, including an in-place write
  observed from JavaScript and a sub-view with a non-zero offset.
- `docs/specs/modules-core-boundary.md`, `docs/specs/ownership-and-scoped-refs.md`,
  `docs/module-authoring-guide.md`, `docs/roadmap.md`.

**Out of scope**:

- Other typed-array kinds (`Int32Array`, `Float64Array`, ...). 025 revisits
  whether `getRandomValues` needs a kind-generic view.
- Accepting a `Uint8Array` where `ArrayBuffer` or `byte[]` is declared.
  Those codecs stay exact.
- `packages/expo-file-system-dotnet` and `packages/expo-crypto-dotnet`
  members: roadmap 038 and 025.

## Open design questions

1. **How decode proves the object is a `Uint8Array`.** Recommended: read
   the global `Uint8Array` constructor and compare it with the value's
   `constructor` property through `StrictEquals`. A JavaScript subclass of
   `Uint8Array` is then rejected; that matches "exact" elsewhere in the
   boundary. Reading `.buffer` and checking `IsArrayBuffer` is not enough,
   because a `DataView` or an `Int32Array` passes that test. If a cheaper
   exact test exists in `Expo.JSI`, the spec names it.
2. **Encode mechanism.** Recommended: encode the backing `ArrayBuffer` as
   today, then call the global `Uint8Array` constructor with `(buffer,
byteOffset, length)` through `CallAsConstructor`. The constructor is read
   from the global object per encode; it is two property reads and keeps no
   cached handle in `DotnetRuntimeContext`. Cost if wrong: one cached
   function per context, added later.
3. **View bounds.** Recommended: `Uint8Array` validates `byteOffset + length
<= buffer.ByteLength` at construction and again before each byte access,
   reusing the buffer's captured-length check, so a detached or resized
   buffer fails loudly.
4. **Managed-created arrays.** Recommended: `Uint8Array.Allocate(length)` and
   `CopyFrom(span)` wrap a native-backed `ArrayBuffer` with offset zero.
   `bytes()` in 038 returns `Uint8Array.CopyFrom(fileBytes)`.
5. **Union membership.** A `Uint8Array` is kind `Object`. The spec decides
   whether the 035 kind table gains an `Object` row for it.

## Done criteria

- [ ] `scripts/test-managed.sh` passes with no skips; new tests cover a
      `Uint8Array` parameter, return, `Task<Uint8Array>`, `Uint8Array?`,
      `IReadOnlyList<Uint8Array>`, a sub-view with offset, an in-place write
      visible to JavaScript, rejection of `ArrayBuffer`, `Int32Array`, and a
      plain object, and release of the lease at invocation end.
- [ ] `scripts/format.sh --check --all` passes.
- [ ] `git diff --stat b7966f3..HEAD -- packages/expo-modules-dotnet/native` prints nothing: no ABI or native change.
- [ ] Accepted behavior is merged into `docs/specs/modules-core-boundary.md`
      and `docs/specs/ownership-and-scoped-refs.md`.
- [ ] Roadmap row is `DONE`, and this brief is deleted.

## STOP conditions

Stop and report instead of improvising if:

- Current state does not match the excerpts.
- `Expo.JSI` cannot read `.buffer`, `.byteOffset`, and `.byteLength` or call
  the global constructor without a new ABI entry.
- A zero-copy view cannot share the `ArrayBuffer` lease.
- The exact-kind test needs symbol-keyed property access that `Expo.JSI`
  does not expose.

## Maintenance notes

025 may need other kinds. The recommended design keeps the kind in the
type name, so a later `TypedArray<T>` or a kind enum is additive.
