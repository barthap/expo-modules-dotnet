# Host-Supplied App Directories

## Goal

Let an authored module read the app-scoped directories it may write to, taken
from the platform host instead of resolved inside managed code.

`DotnetRuntimeContext` gains a cache directory and a persistent-files
directory. The native host supplies both across the C ABI at context creation.
The managed core stores and validates the strings and never touches a disk. An
unconfigured directory throws a specific exception rather than falling back to a
user-wide path.

## Why this needs new ABI surface

`### Requirement: ABI Carries Only Host Knowledge` in
`docs/specs/runtime-and-abi.md` requires any delta that adds ABI surface to name
the host-knowledge category and say why portable .NET cannot answer it. For
these two strings:

- **Category**: host identity, plus host-supplied policy.
- **Why .NET cannot answer it**: .NET's path APIs are all user-wide.
  `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` and
  `Path.GetTempPath()` do not know which app is asking, so two apps on one
  machine resolve the same root and clobber each other's files. Making any of
  them app-scoped needs an app identity: a bundle identifier on macOS, a package
  family name on Windows MSIX. Getting either from managed code means
  P/Invoking CoreFoundation or kernel32, or guessing from
  `__CFBundleIdentifier` and `Info.plist`. That is a platform-specific native
  dependency inside a package that must stay portable.
- **Why the host supplies the finished path, not just the identity**: both cost
  one UTF-8 string. Supplying the identity alone would force the portable core
  to hard-code each OS's cache-root convention, which is more managed code for
  less capability. It would also remove the host's ability to override the
  directory, which upstream deliberately preserves for scoped hosts such as
  Expo Go.

Everything a consuming module does with the path stays in .NET: `HttpClient`
for downloads, `System.Security.Cryptography` for hashing, and
`File`/`Directory`/`Path` for the write. Two strings of host knowledge cross the
ABI; the rest is the base class library.

## Upstream evidence

Neither official platform lets a module resolve its own paths.

- **Android** — `AppContext.cacheDirectory` delegates to
  `AppDirectoriesService.cacheDirectory`, which returns `context.cacheDir`
  (`expo/packages/expo-modules-core/android/src/main/java/expo/modules/kotlin/services/AppDirectoriesService.kt:16`).
  The service is `open`, so a scoped host can override it. When the service is
  not registered the accessor throws.
- **iOS** — `AppContextConfig` accepts host-injected directory URLs and falls
  back to `FileManager`'s platform directories when either argument is nil
  (`expo/packages/expo-modules-core/ios/Core/AppContextConfig.swift:3-14`).
  `expo-asset` reads `appContext.fileSystem.cachesDirectory` and rejects the
  operation when that service is absent
  (`expo/packages/expo-asset/ios/AssetModule.swift:27-34`).
- **iOS, at use time** — `FileSystemUtilities.generatePathInCache` calls
  `ensureDirExists` when the path is used, not when it is configured
  (`expo/packages/expo-modules-core/ios/FileSystemUtilities/FileSystemUtilities.swift:29-38`).

Android exposes the pair `cacheDirectory` + `persistentFilesDirectory`
(`AppDirectoriesService.kt:16-22`); iOS exposes `cacheDirectory` +
`documentDirectory`. This delta ships both, under Android's names.

## Scope

### In scope

- A versioned, size-checked app-directories struct on the runtime-context create
  ABI.
- A versioned create symbol and HostFXR method name for the changed signature.
- Strict UTF-8 decoding of borrowed directory strings in the generated host.
- An immutable public `AppDirectories` model and platform-neutral path
  validation in `Expo.ModulesCore`.
- `DotnetRuntimeContext.CacheDirectory` and
  `DotnetRuntimeContext.PersistentFilesDirectory`, plus
  `AppDirectoryNotConfiguredException`.
- A directory-aware `ExpoModuleTestHost.Create` overload that passes values
  through.
- Per-adapter policy for which hosts supply a real path.
- A compiled generated-host harness that executes the ABI boundary.

### Out of scope

- App-group shared directories, upstream iOS's third directory concept.
- Any filesystem operation in the managed core.
- Creating, cleaning, or lifetime-managing temp directories in
  `ExpoModuleTestHost`.
- Scoped or multi-app hosts and Expo Go-style directory overriding.
- Reshaping `RuntimeContextError` or `RuntimeContextResult` beyond moving them
  verbatim into a shared header.
- Exposing either directory to JavaScript.

## Accepted design

### The host supplies the directories at context creation

`LinkedExpoModulesProvider.Register(context)` runs inside the generated
`CreateRuntimeContextCore`, so a module constructor can observe the context
before any post-creation setter would run. A setter would leave a window where
the directories are silently absent. The directories therefore arrive as an
argument to the create entry point.

The create entry point's signature changes, so its names change with it. The
NativeAOT symbol becomes `expo_dotnet_create_runtime_context_result_v2` and the
HostFXR method becomes `CreateRuntimeContextResultV2`. No alias stays behind
under the old names. The version field inside the struct protects the contents
of an already signature-safe call; it cannot protect a call made through the
wrong function type.

### One shared native declaration

The runtime-context declarations exist in four hand-maintained native copies
today: `packages/expo-modules-dotnet/macos/ManagedLoader.h:14-30`,
`packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h:14-30`,
`packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm:20-35`, and
`packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp:15-31`.
Everything crosses through a function pointer, so drift between copies is
invisible at compile time and corrupts memory at runtime. The declarations move
verbatim into one shared header before the new struct is added.

### Borrowed strings, versioned struct

The struct follows the existing versioned-struct shape from
`expo_jsi_api` (`size`, then `version`, then payload) and the existing borrowed
string convention from `expo_jsi_create_string_fn`. Managed code decodes to a
`string` before the create call returns, so no release callback is needed.

The whole struct pointer may be null, meaning both directories are
unconfigured. At field level only `(null, 0)` means unconfigured. A non-null
pointer with zero length is a supplied empty string, which then fails path
validation; a null pointer with a nonzero length is an invalid ABI pair. That
distinction stops an empty host value from silently becoming "not configured".

### Unconfigured throws; the core never falls back

The accessors' type is non-nullable `string`. Each throws
`AppDirectoryNotConfiguredException` when the host supplied nothing. This
matches Android's behavior for a missing `AppDirectoriesService` and removes any
portable fallback to the user-wide paths this delta exists to avoid. Module code
then reads like upstream Android's `appContext.cacheDirectory`.

### The core validates strings only

`Expo.ModulesCore` checks that a supplied path is non-empty, has no NUL, and is
fully qualified. It does not create the directory, probe it for writability, or
canonicalize it. `Path.IsPathFullyQualified` is a pure string check.
Creating a subdirectory before writing belongs to the consuming module, matching
upstream iOS.

### Ownership split

The generated host owns the private native mirror, the pointer arithmetic, and
strict UTF-8 decoding, because the generated host csproj compiles only two
files. `Expo.ModulesCore` owns the immutable public `AppDirectories` record and
platform-neutral validation, so both the ABI path and a direct managed caller
flow through the same checks.

## Delta requirements

### Requirement: The Create ABI Carries A Versioned App-Directories Struct

The runtime-context create entry point SHALL accept a pointer to an
`expo_dotnet_app_directories` struct between the runtime handle and the
`RuntimeContextResult` out parameter. The struct SHALL begin with a `size`
field and then a `version` field, matching the existing `expo_jsi_api` shape
required by `### Requirement: ABI Version And Size Validation`.

The struct, both runtime-context result types, and both function-pointer
typedefs SHALL be declared exactly once, in a shared native header included by
every platform adapter. No adapter SHALL redeclare them locally.

The shared header SHALL assert standard layout, every field offset, and the
total struct size for 32-bit and 64-bit pointer targets. The generated managed
mirror SHALL use sequential layout in the same field order and SHALL validate
`size` before reading `version` or either pointer field.

Managed decoding SHALL reject a struct smaller than the managed expected size,
and SHALL require exact version equality. Both mismatch messages SHALL name the
native and the managed value, following the existing format
`"Expo JSI ABI version mismatch: native={0} managed={1}."`

#### Scenario: Host passes a valid struct

- **GIVEN** a native adapter fills `size` and `version` and supplies both
  directories
- **WHEN** it calls the create entry point
- **THEN** managed code SHALL decode both strings before constructing the
  runtime context
- **AND** module registration SHALL observe both configured directories

#### Scenario: Undersized struct is rejected

- **GIVEN** a struct whose `size` is smaller than the managed expected size
- **WHEN** managed decoding runs
- **THEN** it SHALL fail with a structured error naming both sizes
- **AND** it SHALL NOT read either directory pointer

#### Scenario: Version mismatch is rejected

- **GIVEN** a struct whose `size` is acceptable but whose `version` differs from
  the managed expected version
- **WHEN** managed decoding runs
- **THEN** it SHALL fail with a structured error naming the native and managed
  version
- **AND** it SHALL NOT construct a runtime context

#### Scenario: Native layout is locked at compile time

- **GIVEN** the shared native header is compiled for a 32-bit or 64-bit pointer
  target
- **WHEN** the target builds
- **THEN** static assertions SHALL fix standard layout, each field offset, and
  the total size
- **AND** a field reorder or type change SHALL fail the build instead of
  corrupting memory at runtime

### Requirement: The Signature Change Uses Versioned Entry-Point Names

The create entry point SHALL be exported as the NativeAOT symbol
`expo_dotnet_create_runtime_context_result_v2` and resolved through HostFXR as
the managed method `CreateRuntimeContextResultV2`. The native function-pointer
typedef and the loader entry-point field SHALL carry matching v2 names.

Neither loader SHALL probe the old create name, and the generated host SHALL
NOT export an alias under it. Teardown SHALL keep its current name because its
signature does not change.

A native adapter and a generated host from different sides of this change SHALL
fail symbol or method resolution before any invocation.

#### Scenario: A stale adapter meets a new host

- **GIVEN** a native adapter built before this change resolves the old create
  symbol or method name
- **WHEN** it loads a generated host built after this change
- **THEN** resolution SHALL fail
- **AND** the adapter SHALL NOT call any create function pointer

#### Scenario: A new adapter meets a stale host

- **GIVEN** a native adapter built after this change resolves the v2 symbol or
  method name
- **WHEN** it loads a generated host built before this change
- **THEN** resolution SHALL fail
- **AND** the adapter SHALL NOT call the old three-argument export through the
  four-argument typedef

#### Scenario: Built artifacts carry only the v2 symbol

- **GIVEN** a generated host is published for NativeAOT
- **WHEN** its exported symbols are inspected with the platform symbol tool
- **THEN** the v2 create symbol SHALL be present
- **AND** the old create symbol SHALL be absent

### Requirement: Directory Strings Are Borrowed Strict UTF-8

Each directory field SHALL be a UTF-8 byte pointer plus an `int32_t` byte
length, not NUL-terminated, following
`### Requirement: UTF-8 String Contract`. The host SHALL keep both buffers
valid for the duration of the create call only. Managed code SHALL copy each
value into a `string` before returning, so no release callback is required.

Managed decoding SHALL use strict UTF-8 that throws on invalid bytes, matching
the existing `new UTF8Encoding(false, throwOnInvalidBytes: true)` convention.
It SHALL NOT silently repair invalid input. It SHALL reject a negative byte
length.

#### Scenario: Valid UTF-8 decodes verbatim

- **GIVEN** a host supplies a fully qualified path containing non-ASCII UTF-8
  bytes
- **WHEN** managed decoding runs
- **THEN** the decoded string SHALL match the host's bytes exactly
- **AND** the runtime context SHALL retain it after the create call returns

#### Scenario: Invalid UTF-8 fails loudly

- **GIVEN** a directory field holds bytes that are not valid UTF-8
- **WHEN** managed decoding runs
- **THEN** decoding SHALL fail with a structured error
- **AND** it SHALL NOT substitute a replacement character

#### Scenario: Negative length is rejected

- **GIVEN** a directory field has a negative byte length
- **WHEN** managed decoding runs
- **THEN** it SHALL fail with a structured error naming the field
- **AND** it SHALL NOT dereference the pointer

### Requirement: Unconfigured Directories Have An Exact ABI Encoding

A null `expo_dotnet_app_directories` pointer SHALL mean both directories are
unconfigured.

At field level:

- `(null pointer, zero length)` SHALL mean that directory is unconfigured;
- `(null pointer, nonzero length)` SHALL be rejected as an invalid ABI pair; and
- `(non-null pointer, zero length)` SHALL decode as a supplied empty string and
  SHALL then fail path validation.

The two fields SHALL be independent. A host MAY supply one directory and leave
the other unconfigured.

#### Scenario: Null struct pointer means both unconfigured

- **GIVEN** a native adapter passes a null struct pointer
- **WHEN** managed decoding runs
- **THEN** it SHALL produce an unconfigured value for both directories
- **AND** the runtime context SHALL still be created

#### Scenario: Null pointer with a length is an ABI error

- **GIVEN** a directory field has a null pointer and a nonzero length
- **WHEN** managed decoding runs
- **THEN** it SHALL fail with a structured error naming the field
- **AND** it SHALL NOT treat the field as unconfigured

#### Scenario: Empty supplied string is not unconfigured

- **GIVEN** a directory field has a non-null pointer and a zero length
- **WHEN** managed decoding runs
- **THEN** it SHALL decode an empty string rather than an unconfigured value
- **AND** path validation SHALL reject that empty string

#### Scenario: One directory configured, the other not

- **GIVEN** a host supplies only a cache directory
- **WHEN** the runtime context is created
- **THEN** the cache accessor SHALL return the supplied path
- **AND** the persistent-files accessor SHALL report the unconfigured state

### Requirement: The Runtime Context Exposes Both App Directories

`DotnetRuntimeContext` SHALL expose a `CacheDirectory` accessor for temporary
files the operating system may remove, and a `PersistentFilesDirectory`
accessor for app files that must survive cache eviction. Both SHALL be
non-nullable `string`.

Each accessor SHALL throw `AppDirectoryNotConfiguredException` when the host
supplied no value for it. That exception SHALL be publicly catchable, SHALL
derive from `InvalidOperationException`, and SHALL name which accessor was
unconfigured so the two are distinguishable.

Each accessor SHALL take the context's existing lock and run the existing
active-state check first, so a disposed context SHALL throw
`ObjectDisposedException` before configuration is consulted.

The one-argument `DotnetRuntimeContext` constructor SHALL remain, meaning both
directories are unconfigured. A second constructor SHALL accept the directory
model and SHALL throw `ArgumentNullException` for a null argument.

Neither directory SHALL be exposed to JavaScript by this delta.

#### Scenario: Configured directory is returned verbatim

- **GIVEN** a host supplied a fully qualified path for a directory
- **WHEN** module code reads that accessor
- **THEN** it SHALL return the supplied path unchanged
- **AND** it SHALL NOT canonicalize or rewrite it

#### Scenario: Unconfigured directory throws a specific exception

- **GIVEN** a runtime context whose host supplied no value for a directory
- **WHEN** module code reads that accessor
- **THEN** it SHALL throw `AppDirectoryNotConfiguredException`
- **AND** the exception SHALL identify which accessor was unconfigured
- **AND** it SHALL NOT return a user-wide or process-wide path

#### Scenario: Disposed context throws before checking configuration

- **GIVEN** a runtime context has been disposed
- **WHEN** either directory accessor is read
- **THEN** it SHALL throw `ObjectDisposedException`
- **AND** it SHALL NOT throw `AppDirectoryNotConfiguredException` instead

#### Scenario: Existing single-argument construction still compiles

- **GIVEN** an existing caller constructs a runtime context with only a runtime
- **WHEN** the code compiles and runs
- **THEN** construction SHALL succeed
- **AND** both directories SHALL report the unconfigured state

### Requirement: The Managed Core Validates Paths And Never Touches The Filesystem

`Expo.ModulesCore` SHALL NOT resolve, create, canonicalize, or probe a
directory. It SHALL NOT call `Environment.GetFolderPath`, `Path.GetTempPath`,
`Path.GetFullPath`, `Directory.*`, or `File.*`.

It SHALL reject a supplied path that is empty, whitespace-only, contains a NUL
character, or is not fully qualified. Validation SHALL use a pure string check.
Both the ABI-decoded value and a direct managed caller's value SHALL flow
through the same validation.

The public directory model SHALL be immutable after construction, so a caller
cannot change the context's directory policy after module registration.

#### Scenario: Invalid supplied path is rejected at construction

- **GIVEN** a supplied path is empty, whitespace-only, contains NUL, or is
  relative
- **WHEN** the directory model is constructed
- **THEN** construction SHALL throw and SHALL name the offending parameter
- **AND** no runtime context SHALL be created with that value

#### Scenario: Validation performs no disk access

- **GIVEN** a supplied path is fully qualified but does not exist on disk
- **WHEN** the directory model is constructed
- **THEN** construction SHALL succeed
- **AND** validation SHALL NOT create, stat, or probe the path

#### Scenario: The core holds no filesystem API

- **GIVEN** `Expo.ModulesCore` sources are scanned for filesystem and
  special-folder APIs
- **WHEN** the scan runs
- **THEN** it SHALL report no matches
- **AND** a new filesystem call in the core SHALL be treated as a defect

#### Scenario: The model cannot be mutated after registration

- **GIVEN** a runtime context was created with a directory model
- **WHEN** module registration has run
- **THEN** no public member SHALL change either configured value
- **AND** the context SHALL keep serving the values it was constructed with

### Requirement: Ownership Of Marshalling And The Public Model Is Split

The generated aggregator host SHALL own the private native struct mirror, the
pointer decoding, and strict UTF-8 conversion. Its public unmanaged create entry
point SHALL receive the struct pointer as an untyped native integer, so no
private mirror type leaks into the generated host's public surface.

`Expo.ModulesCore` SHALL own the immutable public directory model and its
platform-neutral validation. The generated host SHALL construct that model and
pass it into the runtime context before module registration runs.

The generated host SHALL NOT reuse the `Expo.JSI` UTF-8 decoder through a new
`InternalsVisibleTo` edge. It SHALL emit its own private decoder.

All decoding SHALL be NativeAOT-safe and SHALL NOT use runtime reflection or
dynamic invocation.

#### Scenario: Decoding happens before context construction

- **GIVEN** a native adapter supplies a valid struct
- **WHEN** the generated create entry point runs
- **THEN** it SHALL decode both directories, construct the public model, and
  pass it to the runtime context constructor
- **AND** module registration SHALL run after that

#### Scenario: Decoding failure returns a structured error

- **GIVEN** any struct, version, pointer, length, or UTF-8 rule is violated
- **WHEN** the generated create entry point runs
- **THEN** it SHALL report failure through the existing structured
  `RuntimeContextResult` error channel
- **AND** it SHALL NOT create a partially configured runtime context

#### Scenario: The private mirror stays private

- **GIVEN** the generated host is compiled
- **WHEN** its public API surface is examined
- **THEN** the native struct mirror SHALL NOT be public
- **AND** the create entry point SHALL declare the struct parameter as a native
  integer

### Requirement: Test Hosts Pass Directories Through Without Managing Them

`ExpoModuleTestHost` SHALL offer a factory overload that accepts a directory
model and makes it observable through the runtime context inside the existing
registration callback. The existing factory overload SHALL remain source- and
binary-compatible and SHALL mean both directories are unconfigured.

The test host SHALL NOT create, clean, or lifetime-manage a directory. A test
that needs a real directory owns that fixture itself.

#### Scenario: A test supplies directories

- **GIVEN** a test calls the directory-aware factory with configured values
- **WHEN** the registration callback runs
- **THEN** the runtime context SHALL return those values
- **AND** the test host SHALL NOT have created either directory

#### Scenario: The existing factory keeps working

- **GIVEN** an existing test calls the current factory overload
- **WHEN** it reads either directory accessor
- **THEN** it SHALL observe the unconfigured state
- **AND** the existing overload's signature SHALL be unchanged

### Requirement: Platform Adapters Follow A Defined Directory Policy

The packaged Windows example adapter and the macOS example adapter SHALL supply
real app-scoped paths for both directories.

An unpackaged Windows process with no app-model identity SHALL pass the
unconfigured value for both. An executable-name fallback SHALL NOT be
introduced: two unrelated apps can share an executable name, which reproduces
the collision this delta removes. A `%LOCALAPPDATA%` fallback SHALL NOT be
introduced either.

The iOS adapter, the Android adapter, and the development console app SHALL pass
the unconfigured value. No module on those hosts consumes app-scoped storage
through this bridge yet, and upstream's own `expo-asset` serves iOS and Android.
Passing a guessed path there would invent a contract with no consumer to
validate it, and the development console app has no app identity at all.

A supplying adapter SHALL construct the host strings so they outlive the
synchronous create call. It SHALL verify that both resolved paths are distinct,
fully qualified, and app-scoped before it emits a durable, path-free marker
recording that app directories were configured. A resolved path that is a bare
user-wide root SHALL be treated as a defect, not accepted.

Committed artifacts SHALL record only sanitized path shapes, never a real user
profile, machine path, or package identity.

#### Scenario: Packaged host supplies app-scoped paths

- **GIVEN** the packaged Windows example or the macOS example starts
- **WHEN** the adapter resolves its directories
- **THEN** both SHALL be app-scoped and distinct from each other
- **AND** the adapter SHALL emit the path-free configured marker

#### Scenario: Unpackaged Windows has no app identity

- **GIVEN** the Windows adapter cannot obtain app-model identity
- **WHEN** it prepares the create call
- **THEN** it SHALL log the missing host identity and pass the unconfigured
  value for both directories
- **AND** it SHALL NOT derive a path from an executable name or a user-wide root

#### Scenario: Mobile and console hosts are unconfigured

- **GIVEN** the iOS adapter, the Android adapter, or the development console app
  creates a runtime context
- **WHEN** it prepares the create call
- **THEN** it SHALL pass the defined unconfigured value
- **AND** module registration SHALL still succeed

#### Scenario: A user-wide root is a defect

- **GIVEN** a supplying adapter resolves a directory to a bare user-wide root
- **WHEN** it checks the resolved values
- **THEN** that SHALL be reported as a defect
- **AND** the adapter SHALL NOT pass the path as app-scoped

### Requirement: Generated-Host Verification Executes The ABI Boundary

Verification of the generated host SHALL compile and run the generated entry
points, not only assert on emitted source text. A checked-in harness compiled
into the generated host assembly SHALL:

- call the v2 create entry point through its unmanaged function pointer for
  every invalid input and check the structured error result and its release
  callback;
- call the private decoder for valid inputs and check that both directories
  reach the public model independently;
- assert the managed struct size and every field offset for the running pointer
  width; and
- cover a null struct pointer, an undersized size, a wrong version, a negative
  length, a null pointer with a nonzero length, a non-null pointer with a zero
  length, invalid UTF-8, and valid UTF-8.

The generated entry-point type SHALL be emitted as `partial` so the harness can
reach the private decoder without widening production visibility.

Source-text assertions SHALL remain for the exact emitted contract, but they
SHALL NOT be the only check. The native layout assertions SHALL also be compiled
for the Android adapter's configured 32-bit and 64-bit ABIs.

#### Scenario: The harness exercises the unmanaged failure boundary

- **GIVEN** the generated host is compiled with the harness
- **WHEN** the harness invokes the create entry point with each invalid input
- **THEN** each call SHALL return a structured failure
- **AND** the harness SHALL release every structured error buffer

#### Scenario: The harness checks valid decoding

- **GIVEN** the harness supplies valid UTF-8 for one or both directories
- **WHEN** it calls the private decoder
- **THEN** each supplied value SHALL reach the public model independently
- **AND** an unconfigured field SHALL stay unconfigured

#### Scenario: Managed and native layouts are both checked

- **GIVEN** the harness runs on the current pointer width and the Android
  adapter builds for its configured ABIs
- **WHEN** both verifications run
- **THEN** the managed struct size and field offsets SHALL match the native
  assertions
- **AND** a layout divergence SHALL fail verification rather than surface at
  runtime

## Verification

Implementation verification SHALL cover:

- managed tests for the directory model's validation, both context
  constructors, both accessors, independence of the two directories, the
  unconfigured exception's identity, and the disposed-context guard;
- a test-host test proving the pass-through overload and the unchanged existing
  overload;
- generator source assertions for the v2 names, the absence of the old names,
  the private mirror's field order, size-before-version validation, strict
  UTF-8, and the exact pointer/length rules;
- the compiled generated-host ABI harness;
- regeneration of both example app hosts, byte-identical to each other;
- the declaration scan proving the native types are declared exactly once;
- the scan proving no filesystem API in `Expo.ModulesCore`;
- symbol inspection of the built NativeAOT library for the v2 symbol's presence
  and the old symbol's absence;
- Android, iOS, macOS, and Windows adapter builds, with HostFXR and NativeAOT
  runs on both desktop targets reporting the path-free configured marker; and
- `scripts/format.sh --check --all`.
