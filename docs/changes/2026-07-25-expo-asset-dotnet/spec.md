# Expo Asset Dotnet

## Goal

Add the repo's first authored .NET Expo module package,
`expo-asset-dotnet`, exposing one native `downloadAsync` method backed by an
HTTP download and a per-OS file cache. This is a full production-shaped slice
built on the existing generated-binding and autolinking contracts, not a
prototype: it fixes the exact JavaScript surface, validation rules, cache
layout, and error messages so the package can ship and be tested end to end.

## Scope

### In scope

- The `expo-asset-dotnet` package identity, private npm publication metadata,
  and registration against `globalThis._expoDotnet.modules`.
- The single `downloadAsync(url, md5Hash, type)` JavaScript surface and its
  argument validation.
- `file`/`http`/`https` URL handling, including passthrough for `file` URLs.
- Cache root resolution per OS, cache identity, cache-hit detection, and
  atomic download-and-move semantics.
- Module-owned cancellation driven from the module's `[OnDestroy]` hook.
- The two reused Windows C++ reference error-message families.
- The constructor constraint imposed by the Roslyn generator and its
  consequence for internal service visibility and test reach.
- The package's test project shape: pure tests, a small Hermes-backed set,
  and Vitest coverage for the TypeScript facade.

### Out of scope

- Registering into `globalThis.expo.modules`, package-specifier aliasing,
  Metro configuration, or any `expo-asset` compatibility claim.
- Android and iOS.
- Asset metadata resolution and reimplementing Expo's `Asset` class.
- Image decoding, cache-management APIs, and URI schemes beyond
  `file`/`http`/`https`.
- Sharing a filesystem API with the planned `expo-file-system-dotnet` work.
- Editing `scripts/test-managed.sh`, `scripts/test-managed.ps1`, or any
  GitHub workflow.

## Accepted design

### Package identity and registration

`expo-asset-dotnet` is a private npm package under `packages/`. Its native
module name is `ExpoAsset`; its managed assembly name and root namespace are
both `ExpoAssetDotnet`. The TypeScript facade calls
`requireDotnetModule<T>('ExpoAsset')`, which resolves against
`globalThis._expoDotnet.modules` per the existing "Managed test code uses
default dotnet namespace" behavior in `docs/specs/modules-core-boundary.md`.
The package declares `"dotnet"` in `expo-module.config.json` `platforms` and a
`dotnet.projects` entry for its module csproj, per the existing "Dotnet
Modules Declare Autolinking Metadata" requirement in
`docs/specs/dotnet-autolinking.md`; this delta does not change that
contract, it only exercises it with a real package.

### JavaScript surface

The module exposes exactly one `[JS]` member:

```
downloadAsync(url: string, md5Hash: string | null, type: string): Promise<string>
```

The argument order matches upstream `expo-asset` exactly. There are no other
`[JS]` members, no `[Events]`, and no shared objects.

### Argument validation

Every validation failure rejects the returned `Promise` with a catchable
`Error` rather than throwing synchronously past the JS boundary:

- `url` must be non-empty after trimming and must parse as an absolute URI.
- `type` must match `^[A-Za-z0-9]{1,16}$`. A path separator, a dot, traversal,
  an empty string, or a string longer than 16 characters all reject. `type`
  is never rewritten, stripped, or sanitized before or after this check.
- `md5Hash` accepts `null`. A non-null value must match
  `^[0-9a-fA-F]{32}$`, is compared case-insensitively, and is normalized to
  lowercase before use as a cache identity. A malformed value rejects.

### URL classes

- `file` (matched case-insensitively): resolves with the input string
  unchanged. No filesystem access, existence check, or canonicalization
  happens. A `file:` URL that does not parse as an absolute URI rejects like
  any other malformed `url`.
- `http` and `https`: the download path described below.
- Any other scheme rejects with a message naming the offending scheme.

### Cache root resolution

Platform support for this module is a link-time concern, resolved by which
platform adapters reference the package, not a runtime concern. The module
therefore never inspects `Environment.OSVersion`, `RuntimeInformation`, or any
other OS check, and never throws `PlatformNotSupportedException`. The cache
root resolves per OS:

- Windows: `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)`.
- macOS: `<UserProfile>/Library/Caches`.
- Any other OS: `$XDG_CACHE_HOME` when set and absolute, otherwise
  `<UserProfile>/.cache`.

An `ExponentAsset` subdirectory is appended and created with
`Directory.CreateDirectory`. The root is never derived from
`Environment.CurrentDirectory`. An empty or whitespace root, or a directory
creation failure, rejects with the underlying cause. The resolved cache
directory is a constructor dependency of the download service specifically so
tests can supply a temporary directory instead of a real per-OS cache
location.

### Cache identity and file naming

`cacheId` is the normalized lowercase `md5Hash` when the caller supplied one,
otherwise the lowercase hex MD5 of the URL's UTF-8 bytes. MD5 is used here
only as a cache key, not as a security primitive; the module makes no
integrity or tamper-resistance claim from it. The cached file name is
`ExponentAsset-<cacheId>.<type>`. Because `cacheId` is always 32 hex
characters and `type` always matches `^[A-Za-z0-9]{1,16}$`, the resulting name
cannot contain a path separator by construction. The implementation still
asserts that the combined path stays inside the cache directory, as a defense
against a future change to either constraint.

### Cache hit behavior

When the target file already exists:

- With no `md5Hash` supplied, the file is treated as a hit unconditionally.
- With an `md5Hash` supplied, the file's content MD5 is computed. An equal
  hash is a hit. An unequal hash, or a file that cannot be read, is treated as
  a miss that triggers a re-download.

A hash mismatch is deliberately not an error, matching upstream `expo-asset`
behavior on iOS and Android.

### Download and temp-file handling

The HTTP GET goes through an injected `HttpMessageHandler` (or an injected
`HttpClient` wrapping one), so tests fully control response status, content,
and cancellation without a live network. A non-2xx response rejects with the
status code in the message. The response body is written to a temporary file
in the same directory as the final file, named
`<filename>.<8 hex characters>.download`; the random suffix keeps two
concurrent downloads of the same asset from clobbering each other's temp
file. The temp file is then moved into place with
`File.Move(temp, final, overwrite: true)`. Freshly downloaded bytes are never
hash-verified against a caller-supplied `md5Hash` — verification only ever
happens against an existing cached file, never against a fresh download.
Every failure path deletes the temp file, swallowing any delete error, and
disposes responses, streams, and hashing objects. On success the promise
resolves with `new Uri(finalPath).AbsoluteUri`.

### Cancellation

No `[JS]` method can accept an inbound `CancellationToken`; the generator
does not support that parameter shape. The module therefore owns a single
`CancellationTokenSource`, cancels it from its `[OnDestroy]` hook per the
existing "Module destroy hook runs during teardown" behavior in
`docs/specs/modules-core-boundary.md`, and passes its token into every
service call. Service methods accept a `CancellationToken` parameter. A
cancelled operation rejects and leaves no temp file behind.

### Error messages

Two message families are reused from the Windows C++ reference implementation
of this module:

- `Unable to download asset from url: '<url>'`, optionally followed by
  `: <detail>`.
- `Unable to save asset to directory: '<dir>'`, optionally followed by
  `: <detail>`.

Validation failures get their own explicit messages rather than reusing these
two families. No error message embeds a local absolute path; messages name
the cache subdirectory (`ExponentAsset`) and the file name instead.

### Constructor constraint and internal service visibility

The Roslyn generator permits an authored module class to declare only a
parameterless constructor or one accepting `DotnetRuntimeContext`
(`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Generator/ExpoModulesDiagnostics.cs:28`),
per the existing "Simple module is constructed" and "Context-backed module is
constructed" behavior in `docs/specs/modules-core-boundary.md`.
`DotnetRuntimeContext` is not a service container, so this module cannot
receive its validation, cache-path, or download services through constructor
injection. The module therefore constructs its own default services
directly. Those services are `internal`, exposed to the package's own test
assembly only through `[assembly: InternalsVisibleTo("ExpoAssetDotnet.Tests")]`
in `dotnet/ExpoAssetDotnet/AssemblyInfo.cs`. Because of this, Hermes-backed
tests that exercise the real module (rather than its internal services
directly) are limited to code paths that need neither live network access nor
a writable cache root.

### Testing shape

The package owns one mixed test project at
`packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`,
discovered automatically by the canonical managed runners' existing
`packages/*/dotnet/*.Tests/*.Tests.csproj` convention. The full validation,
cache, and download behavior matrix lives in pure tests that supply an
injected cache root and an injected fake `HttpMessageHandler`. A small
Hermes-backed set proves binding-level behavior only — module visibility,
`file:` URL passthrough, and validation rejections through the real
generated bindings — and does not duplicate the pure service matrix. No test
in either set requires public network access.

## Delta requirements

### ADDED: Package identity and dotnet-only registration

The `expo-asset-dotnet` package SHALL be a private npm package under
`packages/` with native module name `ExpoAsset` and managed assembly name and
root namespace `ExpoAssetDotnet`. Its JavaScript facade SHALL register
through `requireDotnetModule<T>('ExpoAsset')` against
`globalThis._expoDotnet.modules` only.

#### Scenario: Module registers only under the dotnet modules object
- **GIVEN** the package's JS facade calls `requireDotnetModule<ExpoAssetModule>('ExpoAsset')`
- **WHEN** the generated provider registers the module
- **THEN** it SHALL install under `globalThis._expoDotnet.modules` only
- **AND** it SHALL NOT register into `globalThis.expo.modules`
- **AND** it SHALL NOT alias the `expo-asset` package specifier
- **AND** it SHALL NOT claim `expo-asset` drop-in compatibility

### ADDED: Single downloadAsync surface

The module SHALL expose exactly one JavaScript-visible member,
`downloadAsync(url: string, md5Hash: string | null, type: string): Promise<string>`,
with argument order matching upstream `expo-asset`.

#### Scenario: Only downloadAsync is visible
- **GIVEN** generated registration installs the `ExpoAsset` module
- **WHEN** JavaScript inspects the module's own properties
- **THEN** `downloadAsync` SHALL be the only `[JS]` member
- **AND** the module SHALL declare no `[Events]` and no shared objects

### ADDED: Argument validation rejects the promise

`downloadAsync` SHALL validate `url`, `type`, and `md5Hash` and SHALL reject
the returned promise with a catchable `Error` for any failure instead of
throwing synchronously.

#### Scenario: Empty or non-absolute url rejects
- **GIVEN** a `url` that is empty after trimming, or does not parse as an
  absolute URI
- **WHEN** `downloadAsync` is called
- **THEN** the returned promise SHALL reject with a catchable `Error`

#### Scenario: type outside the allowed pattern rejects unmodified
- **GIVEN** a `type` value containing a path separator, a dot, traversal, an
  empty string, or more than 16 characters
- **WHEN** `downloadAsync` validates `type`
- **THEN** it SHALL reject
- **AND** it SHALL NOT rewrite, strip, or otherwise sanitize `type` before
  rejecting

#### Scenario: Valid md5Hash normalizes to lowercase
- **GIVEN** a `md5Hash` matching `^[0-9a-fA-F]{32}$` with mixed case
- **WHEN** `downloadAsync` validates it
- **THEN** validation SHALL accept it
- **AND** the normalized lowercase form SHALL be used as the cache identity

#### Scenario: Malformed md5Hash rejects
- **GIVEN** a non-null `md5Hash` that does not match `^[0-9a-fA-F]{32}$`
- **WHEN** `downloadAsync` validates it
- **THEN** it SHALL reject with a catchable `Error`

#### Scenario: Null md5Hash is accepted
- **GIVEN** `md5Hash` is `null`
- **WHEN** `downloadAsync` validates it
- **THEN** it SHALL accept the null value and proceed without hash comparison

### ADDED: URL scheme classification

`downloadAsync` SHALL classify `url` by scheme before validating further, per
the three URL classes below.

#### Scenario: file scheme resolves without filesystem access
- **GIVEN** a `url` with scheme `file` (matched case-insensitively) that
  parses as an absolute URI
- **WHEN** `downloadAsync` handles it
- **THEN** it SHALL resolve with the input string unchanged
- **AND** it SHALL NOT touch the filesystem or canonicalize the path

#### Scenario: Malformed file URL rejects
- **GIVEN** a `url` with scheme `file` that does not parse as an absolute URI
- **WHEN** `downloadAsync` handles it
- **THEN** it SHALL reject

#### Scenario: Unsupported scheme rejects naming the scheme
- **GIVEN** a `url` whose scheme is not `file`, `http`, or `https`
- **WHEN** `downloadAsync` handles it
- **THEN** it SHALL reject with a message naming the offending scheme

### ADDED: Cache root resolution has no runtime OS check

The cache root SHALL resolve per OS as specified without any runtime
operating-system check, and SHALL be a constructor dependency of the download
service.

#### Scenario: Cache root resolution never inspects the running OS
- **GIVEN** the download service resolves its cache root
- **WHEN** resolution runs
- **THEN** it SHALL NOT branch on a runtime operating-system check
- **AND** it SHALL NOT throw `PlatformNotSupportedException`

#### Scenario: Cache root is an injectable constructor dependency
- **GIVEN** a test constructs the download service
- **WHEN** it supplies a temporary directory as the cache root
- **THEN** the service SHALL use that directory instead of computing a
  per-OS default

#### Scenario: Cache directory creation failure rejects with cause
- **GIVEN** the resolved cache root is empty or whitespace, or
  `Directory.CreateDirectory` fails
- **WHEN** `downloadAsync` needs the cache directory
- **THEN** it SHALL reject, and the rejection SHALL carry the underlying
  cause

### ADDED: Cache identity and file naming stay inside the cache directory

`cacheId` SHALL be the normalized lowercase `md5Hash` when supplied, otherwise
the lowercase hex MD5 of the URL's UTF-8 bytes, used only as a cache key and
not as a security primitive. The cached file name SHALL be
`ExponentAsset-<cacheId>.<type>`.

#### Scenario: cacheId derives from a supplied hash
- **GIVEN** `downloadAsync` receives a valid `md5Hash`
- **WHEN** it computes `cacheId`
- **THEN** `cacheId` SHALL be that hash normalized to lowercase

#### Scenario: cacheId derives from the URL when no hash is supplied
- **GIVEN** `downloadAsync` receives `md5Hash = null`
- **WHEN** it computes `cacheId`
- **THEN** `cacheId` SHALL be the lowercase hex MD5 of the URL's UTF-8 bytes

#### Scenario: Combined cache path is asserted inside the cache directory
- **GIVEN** the implementation builds the cached file path from the cache
  directory, `cacheId`, and `type`
- **WHEN** it constructs the final path
- **THEN** it SHALL assert the resulting path stays inside the cache
  directory

### ADDED: Cache hit tolerates a hash mismatch

A hash mismatch on an existing cached file SHALL be treated as a miss that
triggers a re-download, never as an error.

#### Scenario: Cache hit without a supplied hash
- **GIVEN** the cached file for a `url`/`type` pair exists
- **AND** `md5Hash` is `null`
- **WHEN** `downloadAsync` checks for a cache hit
- **THEN** it SHALL treat the existing file as a hit without downloading

#### Scenario: Cache hit with a matching hash
- **GIVEN** the cached file exists and its content MD5 equals the supplied
  `md5Hash`
- **WHEN** `downloadAsync` checks for a cache hit
- **THEN** it SHALL treat the existing file as a hit without downloading

#### Scenario: Hash mismatch is a silent miss, not an error
- **GIVEN** the cached file exists and its content MD5 does not equal the
  supplied `md5Hash`, or the file cannot be read
- **WHEN** `downloadAsync` checks for a cache hit
- **THEN** it SHALL treat this as a miss and re-download
- **AND** it SHALL NOT reject or raise an error for the mismatch itself

### ADDED: Download uses an injected handler and an atomic temp-file move

The HTTP GET SHALL go through an injected `HttpMessageHandler` or
`HttpClient`. The response body SHALL be written to a same-directory temp
file and moved into place atomically. Freshly downloaded bytes SHALL NOT be
hash-verified.

#### Scenario: Successful download resolves with an absolute file URI
- **GIVEN** the injected handler returns a 2xx response for a `http`/`https`
  `url`
- **WHEN** the download completes
- **THEN** `downloadAsync` SHALL move the temp file into place with
  `File.Move(temp, final, overwrite: true)`
- **AND** it SHALL resolve with `new Uri(finalPath).AbsoluteUri`

#### Scenario: Non-2xx response rejects with the status code
- **GIVEN** the injected handler returns a non-2xx response
- **WHEN** `downloadAsync` reads the response status
- **THEN** it SHALL reject with the status code included in the message

#### Scenario: Concurrent downloads use unique temp file names
- **GIVEN** two downloads of the same `url`/`type` pair run concurrently
- **WHEN** each writes its response body to a temp file
- **THEN** each temp file name SHALL include its own random 8 hex character
  suffix
- **AND** neither download SHALL clobber the other's temp file

#### Scenario: Downloaded bytes are not hash-verified
- **GIVEN** a fresh download completes with a caller-supplied `md5Hash`
- **WHEN** `downloadAsync` moves the temp file into place
- **THEN** it SHALL NOT compute or compare a hash of the freshly downloaded
  bytes before resolving

#### Scenario: Failure cleans up the temp file and disposables
- **GIVEN** a download fails after the temp file was created
- **WHEN** the failure path runs
- **THEN** it SHALL delete the temp file, swallowing any delete error
- **AND** it SHALL dispose the response, its streams, and any hashing object

### ADDED: Module-owned cancellation

The module SHALL own a `CancellationTokenSource`, cancel it from
`[OnDestroy]`, and pass its token into every service call.

#### Scenario: Module cancels in-flight operations on destroy
- **GIVEN** the module's `[OnDestroy]` hook runs
- **WHEN** teardown executes
- **THEN** it SHALL cancel the module-owned `CancellationTokenSource`
- **AND** every in-flight service call SHALL observe the cancellation through
  its `CancellationToken` parameter

#### Scenario: Cancelled operation leaves no temp file behind
- **GIVEN** a download is cancelled mid-flight
- **WHEN** the cancellation is observed
- **THEN** `downloadAsync` SHALL reject
- **AND** no temp file SHALL remain in the cache directory

### ADDED: Reused error message families

Download and save failures SHALL reuse the two message families from the
Windows C++ reference implementation. No error message SHALL embed a local
absolute path.

#### Scenario: Download failure uses the download message family
- **GIVEN** a download fails for any reason other than validation
- **WHEN** `downloadAsync` rejects
- **THEN** the message SHALL start with
  `Unable to download asset from url: '<url>'`
- **AND** MAY be followed by `: <detail>`

#### Scenario: Save failure uses the save message family
- **GIVEN** writing or moving the downloaded file into the cache directory
  fails
- **WHEN** `downloadAsync` rejects
- **THEN** the message SHALL start with
  `Unable to save asset to directory: '<dir>'`
- **AND** MAY be followed by `: <detail>`

#### Scenario: Error messages omit local absolute paths
- **GIVEN** any rejection message produced by the module
- **WHEN** the message is constructed
- **THEN** it SHALL NOT embed a local absolute path
- **AND** it SHALL name the `ExponentAsset` cache subdirectory or the file
  name instead

### ADDED: Constructor constraint forces internal default services

Because the Roslyn generator only supports a parameterless constructor or one
accepting `DotnetRuntimeContext`, the module SHALL construct its own default
services rather than receiving them through constructor injection. Those
services SHALL be `internal` and exposed to the package's own test assembly
through `InternalsVisibleTo`.

#### Scenario: Module constructs its own default services
- **GIVEN** the generator instantiates the `ExpoAsset` module class
- **WHEN** construction runs
- **THEN** the module SHALL construct its own validation, cache-path, and
  download services directly
- **AND** it SHALL NOT require an unsupported constructor shape to receive
  them

#### Scenario: Test assembly can reach internal services
- **GIVEN** `ExpoAssetDotnet.Tests` needs to exercise internal services
  directly
- **WHEN** it references the module assembly
- **THEN** `dotnet/ExpoAssetDotnet/AssemblyInfo.cs` SHALL declare
  `[assembly: InternalsVisibleTo("ExpoAssetDotnet.Tests")]`
- **AND** no other assembly SHALL gain that visibility

### ADDED: Testing shape splits pure, Hermes, and JS coverage

The package SHALL own one mixed test project discovered by the canonical
managed runners' existing project convention. The full behavior matrix SHALL
live in pure tests; a small Hermes-backed set SHALL prove binding-level
behavior only.

#### Scenario: Canonical runners discover the package test project
- **GIVEN** `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`
  exists
- **WHEN** `scripts/test-managed.sh` runs without project selection
- **THEN** it SHALL discover and run that project through the existing
  `packages/*/dotnet/*.Tests/*.Tests.csproj` convention
- **AND** neither runner script SHALL require an edit to discover it

#### Scenario: Pure tests own the full behavior matrix
- **GIVEN** validation, cache, and download behavior needs coverage
- **WHEN** tests are written for that behavior
- **THEN** they SHALL be pure tests with an injected cache root and an
  injected fake `HttpMessageHandler`
- **AND** they SHALL NOT require public network access

#### Scenario: Hermes-backed tests stay small and non-duplicative
- **GIVEN** a Hermes-backed test exercises the real module through generated
  bindings
- **WHEN** it is added to the Hermes-backed set
- **THEN** it SHALL cover only module visibility, `file:` passthrough, or
  validation rejections
- **AND** it SHALL NOT duplicate the pure service behavior matrix
- **AND** it SHALL NOT require public network access or a writable cache root

## Verification

Verification for this delta covers both the pure and Hermes-backed managed
test matrix and the TypeScript facade, with no step requiring public network
access:

- the pure test matrix for validation, cache identity, cache-hit, and
  download behavior, using an injected temporary cache root and an injected
  fake `HttpMessageHandler`;
- the small Hermes-backed set for module visibility, `file:` passthrough, and
  validation rejections;
- Vitest coverage for the TypeScript facade;
- `scripts/test-managed.sh --project packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj`
  for the package's managed tests, and `scripts/test-managed.sh` for the full
  managed suite once the package is wired into the workspace;
- `scripts/format.sh --check --all`; and
- `git diff --check`.

No verification step may require public network access.
