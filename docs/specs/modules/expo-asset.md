# expo-asset-dotnet

## Purpose

Define the behavior of the `packages/expo-asset-dotnet` authored module.

## Requirements

### Requirement: Expo Asset Dotnet Is A Standalone Authored Module

`packages/expo-asset-dotnet` SHALL provide the Windows and macOS authored
module package for asset download and caching. Its TypeScript facade SHALL
export the `expo-asset@57.0.2`-compatible native function signature
`downloadAsync(url: string, md5Hash: string | null, type: string):
Promise<string>`. The generated module SHALL register as `ExpoAsset` only under
`_expoDotnet.modules` through the existing dotnet autolinking path.

The package SHALL NOT resolve or reuse the upstream `Asset` class, install a
package alias, register under `globalThis.expo`, branch on the runtime operating
system, or add a platform-specific dependency to the managed implementation.
Filesystem I/O, HTTP, and hashing SHALL use .NET APIs and SHALL add no C ABI
surface.

#### Scenario: JavaScript downloads through the dotnet module

- **GIVEN** an app has autolinked `expo-asset-dotnet`
- **WHEN** JavaScript imports and calls its `downloadAsync` facade
- **THEN** the facade SHALL resolve `_expoDotnet.modules.ExpoAsset`
- **AND** it SHALL forward `url`, `md5Hash`, and `type` in that order

#### Scenario: The package remains standalone

- **GIVEN** the app does not install the upstream `expo-asset` JavaScript
  package
- **WHEN** the facade evaluates
- **THEN** it SHALL NOT resolve an upstream native module or `Asset` class
- **AND** its public function SHALL remain available through its own package

### Requirement: Expo Asset Dotnet Uses Strict App-Scoped Cache Inputs

The module SHALL accept only a non-empty absolute `file`, `http`, or `https`
URL. `type` SHALL match `^[A-Za-z0-9]{1,16}$`. `md5Hash` SHALL be `null` or 32
hexadecimal characters and SHALL be normalized to lowercase. This stricter
filename-input validation is an intentional security divergence from
`expo-asset@57.0.2`.

A `file:` URL SHALL return byte-for-byte unchanged before any cache access. An
HTTP request SHALL read its cache root lazily and only from
`DotnetRuntimeContext.CacheDirectory`, append the `ExponentAsset`
subdirectory, and reject an invalid or unavailable host root without an
environment or user-directory fallback. The final path SHALL be checked for
containment within that subdirectory.

The cache identifier SHALL be the supplied normalized hash, or the lowercase
MD5 of the original URL when no hash is supplied. The filename SHALL be
`ExponentAsset-<cache-id>.<type>`. An existing no-hash path SHALL be an
unconditional hit. A supplied hash SHALL be compared with the existing file's
MD5; an unreadable or mismatched file SHALL be a cache miss. Freshly downloaded
bytes SHALL NOT be rejected when they differ from the supplied hash.

#### Scenario: File URL bypasses host cache policy

- **GIVEN** `downloadAsync` receives a valid `file:` URL
- **WHEN** the module handles the request
- **THEN** it SHALL return the original URL unchanged
- **AND** it SHALL NOT read `DotnetRuntimeContext.CacheDirectory` or send an
  HTTP request

#### Scenario: Unsafe filename input is rejected

- **GIVEN** `type` or `md5Hash` does not match its strict accepted shape
- **WHEN** the module validates the request
- **THEN** it SHALL reject before creating a cache path
- **AND** arbitrary path syntax SHALL NOT reach filename construction

### Requirement: Expo Asset Dotnet Owns Download Lifetime And Failures

The package SHALL own its `HttpClient`, responses, streams, hash objects, and a
module-scoped cancellation source. HTTP responses SHALL stream into a unique
same-directory `*.download` file and move into place with overwrite only after
the write succeeds. Failure, caller cancellation, and module destruction SHALL
leave no temporary file. A pre-cancelled HTTP request SHALL reject before a
cache hit is returned.

Caller cancellation SHALL remain an `OperationCanceledException`. An
`OperationCanceledException` raised by the HTTP handler or response stream
while the caller token is live SHALL use the controlled download or save error
family. Download errors SHALL start with `Unable to download asset from url:
'<url>'`; save errors SHALL start with `Unable to save asset to directory:
'ExponentAsset'`.

The package SHALL keep its full validation, cache, HTTP, concurrency,
cancellation, and cleanup matrix in pure offline tests with injected cache and
HTTP dependencies. A small Hermes-backed set SHALL verify generated module
visibility, `file:` passthrough, validation rejection, and teardown. Facade and
real metadata tests SHALL run through the package's Vitest suite.

#### Scenario: Download succeeds atomically

- **GIVEN** an HTTP response succeeds for a cache miss
- **WHEN** the response body is saved
- **THEN** it SHALL first be written to a unique same-directory temporary file
- **AND** only the completed file SHALL replace the final cache path

#### Scenario: Timeout-style cancellation uses a controlled error

- **GIVEN** the caller token is still live
- **WHEN** the HTTP handler or response stream throws
  `OperationCanceledException`
- **THEN** the module SHALL reject with the matching controlled outer error
- **AND** it SHALL retain the cancellation as the inner exception
- **AND** no temporary file SHALL remain
