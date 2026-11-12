# expo-crypto-dotnet

## Purpose

Define the behavior of the `packages/expo-crypto-dotnet` authored module.

## Requirements

### Requirement: Expo Crypto Dotnet Ports The Upstream ExpoCrypto Module

`packages/expo-crypto-dotnet` SHALL register a native module named
`ExpoCrypto` only under `_expoDotnet.modules`, through the existing dotnet
autolinking path. It SHALL export exactly the native members `digestString`,
`digestStringAsync`, `getRandomValues`, `digest`, and `randomUUID`, with the
upstream `expo-crypto@58.0.7` names, argument order, and sync or async form.
Its TypeScript facade SHALL export `getRandomBytes`, `getRandomBytesAsync`,
`digestStringAsync`, `getRandomValues`, `randomUUID`, `digest`,
`CryptoDigestAlgorithm`, `CryptoEncoding`, `CryptoDigestOptions`, and `Digest`
with upstream signatures. The package SHALL NOT export any AES name, a stub, or
a throwing placeholder. It SHALL NOT install an upstream package alias,
register under `globalThis.expo`, or branch on the operating system. Hashing,
random bytes, and UUIDs SHALL use .NET APIs and SHALL add no C ABI surface.

#### Scenario: Native surface matches upstream
- **GIVEN** an app has autolinked `expo-crypto-dotnet`
- **WHEN** JavaScript reads the own property names of
  `_expoDotnet.modules.ExpoCrypto`
- **THEN** the native functions SHALL be exactly `digest`, `digestString`,
  `digestStringAsync`, `getRandomValues`, and `randomUUID`
- **AND** `digestStringAsync` SHALL return a `Promise` and the others SHALL
  return synchronously

#### Scenario: AES is absent
- **GIVEN** the facade and the native module are inspected
- **WHEN** a test looks for `AESEncryptionKey`, `AESSealedData`,
  `aesEncryptAsync`, `aesDecryptAsync`, `AESKeySize`, or an `ExpoCryptoAES`
  module
- **THEN** none of them SHALL exist

### Requirement: Expo Crypto Dotnet Digests Match Upstream

The native digest enum `ExpoCryptoDotnet.DigestAlgorithm` SHALL declare `MD5`,
`SHA-1`, `SHA-256`, `SHA-384`, and `SHA-512` with `[EnumMember(Value = ...)]`,
and the native encoding enum `ExpoCryptoDotnet.CryptoEncoding` SHALL declare
`hex` and `base64` the same way. Decoding SHALL accept exactly those strings,
matched exactly and case-sensitively, and SHALL reject any other string with
the `ArgumentException` of "String Enums Cross As Declared JavaScript Values",
which lists the accepted strings. Each algorithm SHALL hash with the matching
.NET `HashData` method. `digestString(algorithm, data, options)` SHALL hash the
UTF-8 bytes of `data` and SHALL return lowercase hex for `hex` and padded
standard base64 without line breaks for `base64`. `options.encoding` SHALL
default to `hex` when it is absent or `undefined`, through the record field
default of "Record Fields With Defaults Are Optional In JavaScript".
`digestStringAsync` SHALL decode its arguments on the JavaScript thread,
compute the same value on the .NET thread pool, and resolve with it.
`digest(algorithm, output, data)` SHALL hash exactly the bytes of the `data`
view, compute the full hash before it writes, and copy the first
`min(hash length, output length)` bytes to the start of the `output` view,
leaving the other output bytes unchanged.

#### Scenario: Known-answer vectors
- **GIVEN** the input string `Expo`
- **WHEN** JavaScript calls `digestString` and `digestStringAsync` for each
  accepted algorithm with `{ encoding: 'hex' }` and with
  `{ encoding: 'base64' }`
- **THEN** each result SHALL equal the upstream vector in
  `android/src/test/java/expo/modules/crypto/ExpectedEncodingResults.kt`
- **AND** the sync and async results SHALL be equal

#### Scenario: Non-ASCII input
- **GIVEN** a string with characters outside ASCII, including one outside the
  Basic Multilingual Plane
- **WHEN** JavaScript digests it with `SHA-256`
- **THEN** the result SHALL equal the SHA-256 digest of the string's UTF-8
  bytes

#### Scenario: Encoding defaults to hex
- **GIVEN** the `DigestOptions` record, whose `Encoding` field defaults to
  `Hex`
- **WHEN** JavaScript calls `digestString('SHA-256', 'Expo', {})`
- **THEN** the result SHALL equal the hex vector for SHA-256

#### Scenario: Unsupported or misspelled algorithm
- **GIVEN** the native module
- **WHEN** JavaScript passes `MD2`, `MD4`, `SHA-224`, `SHA1`, `sha-256`, or
  `1` as the algorithm
- **THEN** `digestString` and `digest` SHALL throw a catchable `Error` before
  hashing
- **AND** for each string, the message SHALL contain the received string,
  `DigestAlgorithm`, `'MD5'`, and `'SHA-1'`, and the call for `1` SHALL fail
  as a non-string value
- **AND** `digestStringAsync` SHALL return a rejected `Promise` and SHALL NOT
  throw synchronously

#### Scenario: Digest of a subview
- **GIVEN** a 16-byte buffer with distinct bytes and
  `data = new Uint8Array(buffer, 4, 8)`
- **WHEN** JavaScript calls `digest('SHA-256', new Uint8Array(32), data)`
- **THEN** the output SHALL equal the SHA-256 digest of bytes 4 to 11 only

#### Scenario: Short, long, and overlapping output
- **GIVEN** an `output` view of 8 bytes, then one of 40 bytes prefilled with
  `0xAA`, then an `output` view over the same bytes as `data`
- **WHEN** JavaScript calls `digest('SHA-256', output, data)`
- **THEN** the 8-byte output SHALL hold the first 8 hash bytes
- **AND** the 40-byte output SHALL hold the 32 hash bytes followed by 8 bytes
  of `0xAA`
- **AND** the overlapping output SHALL hold the hash of the original `data`
  bytes

### Requirement: Expo Crypto Dotnet Random Values And UUIDs

`getRandomValues(view)` SHALL fill exactly the bytes of the `Uint8Array` view
with `RandomNumberGenerator`, with no size limit, and SHALL return nothing. A
zero-length view SHALL succeed and change nothing. `randomUUID()` SHALL return
`Guid.NewGuid()` in the 36-character lowercase hyphenated form, which is a
version 4 UUID with the RFC 4122 variant.

#### Scenario: Random fill stays inside the view
- **GIVEN** a 32-byte buffer filled with `0x00` and
  `view = new Uint8Array(buffer, 8, 16)`
- **WHEN** JavaScript calls `getRandomValues(view)`
- **THEN** bytes 0 to 7 and 24 to 31 of the buffer SHALL still be `0x00`
- **AND** bytes 8 to 23 SHALL NOT all be `0x00`

#### Scenario: Zero-length and large views
- **GIVEN** `new Uint8Array(0)` and `new Uint8Array(70000)`
- **WHEN** JavaScript calls `getRandomValues` on each
- **THEN** both calls SHALL succeed

#### Scenario: Wrong value kind
- **GIVEN** the native module
- **WHEN** JavaScript passes an `Int32Array`, a `DataView`, an `ArrayBuffer`,
  or `5` to `getRandomValues`
- **THEN** the call SHALL throw a catchable `Error` whose message contains
  `Value is not a Uint8Array.`

#### Scenario: UUID shape
- **GIVEN** the native module
- **WHEN** JavaScript calls `randomUUID()` 100 times
- **THEN** each result SHALL match
  `^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$`
- **AND** the 100 results SHALL be distinct

### Requirement: Expo Crypto Dotnet Facade Normalizes Byte Views

The facade SHALL pass a `Uint8Array` to native code unchanged. It SHALL wrap
any other `ArrayBuffer` view as
`new Uint8Array(view.buffer, view.byteOffset, view.byteLength)`, and for
`digest` it SHALL wrap a whole `ArrayBuffer` as `new Uint8Array(buffer)`.
`getRandomValues` SHALL pass a `DataView` or a non-view value to native code
unchanged, so native decoding rejects it, and SHALL return the caller's
original object. `digest(algorithm, data)` SHALL allocate the output as
upstream does (`SHA-1` 20, `SHA-256` 32, `SHA-384` 48, `SHA-512` 64, `MD2`,
`MD4`, `MD5` 16 bytes), call native `digest`, and resolve with the output's
`ArrayBuffer`. It SHALL
reject, and not throw, when native code throws. `getRandomBytes` and
`getRandomBytesAsync` SHALL validate and floor `byteCount` as upstream does and
SHALL always use native random values. `digestStringAsync` SHALL validate
`algorithm`, `data`, and `options.encoding` as upstream does before it calls
native code, with the upstream `TypeError` and `CryptoError` (`code`
`ERR_CRYPTO`) messages.

#### Scenario: Integer view keeps its identity and range
- **GIVEN** `const a = new Uint32Array(8)` and
  `const sub = new Uint16Array(a.buffer, 4, 4)`
- **WHEN** JavaScript calls `getRandomValues(a)` and `getRandomValues(sub)`
- **THEN** each call SHALL return the same object it received
- **AND** native code SHALL receive a `Uint8Array` with the byte offset and
  byte length of that view

#### Scenario: Digest accepts every buffer source
- **GIVEN** the same 8 bytes as an `ArrayBuffer`, a `Uint8Array` subview of a
  larger buffer, an `Int16Array`, and a `DataView`
- **WHEN** JavaScript calls `digest('SHA-256', source)` for each
- **THEN** every result SHALL be a new 32-byte `ArrayBuffer` with the same
  bytes
- **AND** no result SHALL be the input's buffer, and changing the input
  afterwards SHALL NOT change a result

#### Scenario: Byte count validation
- **GIVEN** the facade
- **WHEN** JavaScript calls `getRandomBytes` with `-1`, `1025`, `NaN`, or `'4'`
- **THEN** it SHALL throw a `TypeError` with the upstream message, and
  `getRandomBytesAsync` SHALL reject with it
- **AND** `getRandomBytes(0)` SHALL return an empty `Uint8Array`,
  `getRandomBytes(1024.9)` SHALL return 1024 bytes, and no call SHALL use
  `Math.random`

#### Scenario: String digest validation
- **GIVEN** the facade
- **WHEN** JavaScript calls `digestStringAsync` with an algorithm outside
  `CryptoDigestAlgorithm`, a non-string `data`, or `{ encoding: 'utf8' }`
- **THEN** it SHALL reject with a `CryptoError` whose `code` is `ERR_CRYPTO`,
  before native code runs
