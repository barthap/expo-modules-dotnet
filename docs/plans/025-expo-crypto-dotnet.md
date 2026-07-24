# Plan 025: `expo-crypto-dotnet` for Windows and macOS

> **Executor instructions**: Start with the drift check and living-spec
> workflow. The public JS facade may accept TypedArray inputs, but the native
> binding must take an `ArrayBuffer` plus `byteOffset` and `byteLength`. Do not
> add a typed-array codegen codec for this plan.
>
> **Drift check**: `git diff --stat 9247d75d..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet docs/specs/`

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (cryptographic API semantics and byte-range correctness)
- **Depends on**: plan 006 ArrayBuffer work (complete); execute after plan 024
- **Category**: authored module
- **Planned at**: `9247d75d`, 2026-07-24

## Why this matters

Crypto is a clean all-platform-shaped module, though this initial package only
supports Windows and macOS. It shows a place where .NET's standard crypto APIs
are a strong authored-module fit, and it verifies the bridge's binary contract
without making the native layer understand every TypedArray kind. The key
correctness property is that subviews affect only their selected byte range.

## Current state

- `Expo.ModulesCore.ArrayBuffer` supports owned JavaScript-backed and native
  MutableBuffer-backed storage, scoped readable/writable byte callbacks, and
  `ArrayBuffer.CopyFrom` for native-owned results.
- Generated tests in `BinaryModuleTests.cs` prove `ArrayBuffer`, byte[], span,
  and memory bindings. There is no generated TypedArray wrapper or codec.
- `docs/specs/ownership-and-scoped-refs.md` requires scoped byte borrowing and
  explicit ownership. Crypto must not hold a JS-backed buffer after its access
  callback returns.
- `Task<T>` generated bindings map to promises and surface faults as catchable
  JavaScript errors.

## Scope

**In scope**

- Create `packages/expo-crypto-dotnet` with native module name `ExpoCrypto`.
- Windows/macOS support only in this release, with clear unsupported-platform
  behavior elsewhere.
- A real, approved subset covering random bytes/fill, UUID generation, and
  digest operations whose algorithms are available through supported .NET
  cryptography APIs.
- Typed-array-friendly JS facade normalization to the native ArrayBuffer range
  contract, plus tests for offsets, views, and detached/invalid input errors.

**Out of scope**

- Android/iOS registration, upstream package aliasing, Expo global mutation,
  WebCrypto, encryption/key storage, streaming, and adding a typed-array JSI
  codec.
- Advertising unsupported algorithms such as MD2/MD4. Do not emulate them or
  add an unreviewed crypto dependency merely to match an enum.

## Required binary boundary

Native methods must use the equivalent of:

```text
fillRandom(buffer: ArrayBuffer, byteOffset: int, byteLength: int): void
digestAsync(algorithm, buffer: ArrayBuffer, byteOffset: int, byteLength: int): Promise<ArrayBuffer>
```

The JavaScript facade accepts an `ArrayBuffer` or ArrayBuffer view, validates
that offset/length are non-negative safe integers and contained by
`buffer.byteLength`, and passes the underlying `ArrayBuffer` plus exact range.
For `getRandomValues`, it returns the original view after native mutation. A
subview must never cause random fill or hashing to use the full backing buffer.

## Steps

### Step 1: Approve cryptographic surface and error model

Create `docs/changes/2026-<mm-dd>-expo-crypto-dotnet/spec.md` and matching
`plan.md`. The specification must decide:

1. Exact package/native names and Windows/macOS initial platform matrix.
2. The supported digest enum/list and canonical output representation. Limit it
   to algorithms provided and supported by the target .NET runtime. State why
   any upstream-only algorithm is absent.
3. Which string APIs, encodings, UUID APIs, and random APIs are exposed. Every
   exported API must be implemented, not a placeholder.
4. Input classes accepted by JS (`ArrayBuffer`, `Uint8Array`, other views),
   exact range extraction, DataView behavior, and rejection for detached or
   out-of-bounds data.
5. Random fill size limits and error behavior, including the zero-length range.
6. Digest scheduling: snapshot/copy range bytes before background hashing, then
   create an independent native-backed `ArrayBuffer` for the promise result.
7. FIPS/platform availability behavior, resource disposal, and error messages
   that do not expose sensitive data.

Get approval and commit the change artifacts before implementation.

### Step 2: Add package and native range methods

Create:

- `packages/expo-crypto-dotnet/package.json`
- `packages/expo-crypto-dotnet/expo-module.config.json`
- `packages/expo-crypto-dotnet/src/index.ts`
- `packages/expo-crypto-dotnet/dotnet/ExpoCryptoDotnet/`
- `packages/expo-crypto-dotnet/dotnet/ExpoCryptoDotnet.Tests/`

Follow the existing module-project/analyzer shape. Implement `ExpoCryptoModule`
with `[JS]` members whose signatures take `ArrayBuffer`, `int byteOffset`, and
`int byteLength`. Centralize range validation in an internal helper and slice
only within an active scoped byte callback.

### Step 3: Implement random, digest, and UUID operations

Use `RandomNumberGenerator` for random filling and approved `System.Security.Cryptography`
implementations for digests. Generate UUIDs through a cryptographically sound
approved platform API. For async digest, copy exactly the requested range to
managed memory before `Task.Run`/async hashing, dispose any input lease at the
end of scoped access, and return `ArrayBuffer.CopyFrom` over the digest bytes.
Never retain a JavaScript-backed ArrayBuffer into background work.

The JS facade must normalize a view using its `.buffer`, `.byteOffset`, and
`.byteLength`; direct `ArrayBuffer` input maps to offset zero and full length.
Do not rely on a view's constructor type or construct a replacement view around
the whole buffer. Preserve the caller's view identity for random filling.

### Step 4: Add range and algorithm tests

Add pure tests for range validation and algorithm selection. Add Hermes-backed
tests that verify:

1. A `Uint8Array` subview receives random bytes only inside its range.
2. Digesting a subview equals the digest of the selected bytes, not the backing
   buffer.
3. `DataView` and direct ArrayBuffer behavior follow the approved facade
   contract.
4. Zero-length input, invalid offsets/lengths, unsupported algorithms, and
   native exceptions become catchable JS errors.
5. Async digest returns a distinct native-backed ArrayBuffer and remains correct
   after the caller mutates the original backing buffer.
6. UUID and random output satisfy structural/length expectations without
   asserting deterministic random values.

### Step 5: Merge docs and verify

Merge accepted requirements into the living specs, archive the change folder,
and update plan 025 only after all verification passes.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package JS tests | `pnpm --filter expo-crypto-dotnet test` | exit 0 |
| Managed package tests | `dotnet test packages/expo-crypto-dotnet/dotnet/ExpoCryptoDotnet.Tests/ExpoCryptoDotnet.Tests.csproj` | exit 0 |
| Full managed regression | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |

## Done criteria

- The package exposes only real approved crypto operations on Windows/macOS.
- Native APIs receive ArrayBuffer plus offset/length; JS view ergonomics are
  implemented in the facade and subviews are correct.
- Async work copies its byte range before leaving the runtime scope.
- Unsupported algorithms/platforms fail clearly, with no fake upstream parity.
- Tests, living-spec merge, and required verification pass.

## STOP conditions

- Full upstream algorithm parity requires unsupported algorithms or an
  unapproved cryptographic dependency.
- Correct view-range behavior needs a new TypedArray native codec.
- The implementation would retain JS-backed buffer storage across background
  work or runtime teardown.
- Compatibility aliases or mobile support become necessary to ship the defined
  package.
