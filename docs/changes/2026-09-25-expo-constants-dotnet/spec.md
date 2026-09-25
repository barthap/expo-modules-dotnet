# Expo Constants Dotnet

## Goal

Add a Windows/macOS `expo-constants-dotnet` package whose values have explicit
sources. The package exposes a small typed constants surface through the normal
dotnet module registry. It does not claim to implement the full upstream
`expo-constants` interface.

## Scope

### In scope

- A private workspace package, managed module, TypeScript facade, and
  package-owned tests.
- A host-supplied, typed native version model on `DotnetRuntimeContext`.
- A versioned runtime-context creation ABI that carries the host's native
  version values to managed code before module registration.
- Windows/macOS host adapters and the generated app-level host. Mobile
  adapters continue to create contexts with no native version metadata.
- Deterministic tests of metadata decoding, provenance, generated properties,
  null values, and runtime-scoped session identity.

### Out of scope

- `expoConfig`, arbitrary manifest JSON, EAS Updates, Expo Go, and remote
  configuration.
- `globalThis.expo.modules`, `NativeModulesProxy`, Metro aliases, or importing
  the upstream `expo-constants` package specifier.
- Android/iOS support for this authored package.
- Device identifiers, installation identity, push tokens, and persistence.

## Accepted design

### Package and JavaScript surface

`expo-constants-dotnet` registers a native module named `ExponentConstants`
under `globalThis._expoDotnet.modules`. Its managed assembly and namespace are
`ExpoConstantsDotnet`. The facade calls
`requireDotnetModule<ExpoConstantsNativeModule>('ExponentConstants')` and
exports one read-only constants object as its default export, plus its types.

The exact JavaScript properties are:

| Property | Type | Meaning and source |
| --- | --- | --- |
| `platform` | `'windows' \| 'macos'` | Current operating system, determined by portable .NET OS APIs. |
| `executionEnvironment` | `'bare'` | This package only runs in an app that owns its native host; it is not Expo Go. |
| `sessionId` | `string` | A new canonical GUID for each `DotnetRuntimeContext` that instantiates the module. Stable for that context; not persisted. |
| `nativeAppVersion` | `string \| null` | macOS main bundle `CFBundleShortVersionString`; `null` on Windows unless a distinct native app version source is added through a separately approved change. |
| `nativeBuildVersion` | `string \| null` | macOS main bundle `CFBundleVersion`; Windows package identity's four-part version; `null` when unavailable. |
| `expoVersion` | `null` | The Expo Go app version. This host is not Expo Go. |

The package deliberately retains the two native version properties even though
upstream `expo-constants@57.0.2` removed them. It does not reinterpret a package
version, assembly version, Expo SDK version, or app config as a missing native
app version. `expoConfig` is absent, not a `null` compatibility placeholder.
There are no methods or other compatibility properties.

The C# module exposes the same six values as getter-only `[JS]` properties.
Generated property descriptors have no setter, so strict-mode JavaScript
assignment fails. A module access on Android, iOS, Linux, or an unknown OS
throws `PlatformNotSupportedException` through the generated binding's
catchable JavaScript error path. The TypeScript package does not promise
support on those hosts.

### Native metadata boundary

Native app versions are **host identity** under the existing `ABI Carries Only
Host Knowledge` rule in `docs/specs/runtime-and-abi.md`. The macOS bundle and
Windows package identity belong to the embedding app. Portable .NET cannot
identify either reliably from the generated `ExpoDotnetHost` assembly, process
working directory, or environment variables. By contrast, the current OS,
session GUID, and fixed execution-environment policy need no new ABI fields.

The shared `expo_dotnet_host.h` declares an `expo_dotnet_app_metadata` struct
with `size`, `version`, and two independent UTF-8 pointer/length pairs for
native app version and native build version. A null pointer with zero length
means unavailable. A non-null pointer with zero length means a malformed empty
value and fails validation. Negative lengths, null pointers with nonzero
lengths, invalid UTF-8, embedded NUL, and whitespace-only supplied values fail
the structured startup result. Native adapters borrow the buffers only for the
create call; generated managed code copies them before returning. The shared
header pins the struct layout for 32-bit and 64-bit pointer widths, and the
managed test harness executes and checks the mirror layout and decoder.

A new `expo_dotnet_create_runtime_context_result_v3` entry point accepts the
existing app-directories pointer and the new metadata pointer. Both desktop
loaders and both mobile loaders resolve that exact v3 symbol, so stale loader
and generated-host pairs fail symbol resolution rather than calling through an
incompatible function pointer. The app-directories struct and its version stay
unchanged. Mobile adapters pass a null metadata pointer. The old v2 create
entry point is removed from the generated host and loaders because an app-level
host and adapter are built together; teardown keeps its existing symbol.

The generated host strictly decodes both structs before constructing
`DotnetRuntimeContext`. It creates a public immutable `HostAppMetadata` model
and passes it through a new three-argument context constructor. Existing one-
and two-argument constructors remain and use `HostAppMetadata.Unconfigured`.
`DotnetRuntimeContext.AppMetadata` exposes that model after its usual active
state check. The model validates supplied strings without reading the
filesystem. This is a context input, not a mutable global or a test-only
production configuration hook.

On macOS, the adapter reads only string values from the main bundle's
`CFBundleShortVersionString` and `CFBundleVersion`. Missing keys map to null.
Present keys with a non-string value fail startup as malformed metadata.
On Windows, the adapter reads `Package::Current().Id().Version()` and formats
its major, minor, build, and revision components as four decimal numbers for
`nativeBuildVersion`. `nativeAppVersion` is null because package identity does
not supply a separate display version. An unpackaged Windows process has no
package identity and supplies both fields as null. Other host API failures
surface as startup errors; they do not fall back to assembly or app-config
values. Neither adapter logs version values.

### Runtime lifetime and tests

The generated provider creates one module instance per runtime context. That
instance generates one GUID and projects a read-only snapshot from the
context's metadata. Repeated reads in one context return the same values. A
new context, including a React Native reload, gets a new `sessionId`. Context
teardown removes the module instance and its session value. No file or install
identifier is created.

Pure package tests exercise metadata projection, platform rejection, null
behavior, and stable version mapping with explicit inputs. Hermes-backed
package tests exercise native registration, lower-camel property names,
strict-mode read-only behavior, and two runtime contexts. The existing
`ExpoModuleTestHost` gains a metadata constructor input for tests; no test
provider is compiled into the production package. Autolinking tests execute
the v3 entry point through its unmanaged function pointer, including malformed
metadata and both valid and null metadata. TypeScript tests assert that the
facade asks for `ExponentConstants` through `requireDotnetModule` and never
touches the Expo global registry. The managed suite, package JS tests,
autolinking tests, typecheck, and repository formatter are required gates.

## Delta requirements

### ADDED: Typed constants module

The package SHALL expose exactly the six typed getter-only properties listed
above under `_expoDotnet.modules.ExponentConstants`. The native version fields
SHALL preserve their named host sources and SHALL be null when those sources
are unavailable. `expoConfig` SHALL not be exported.

#### Scenario: The module is read on macOS
- **GIVEN** a macOS host supplies main-bundle app and build versions
- **WHEN** JavaScript reads the generated constants properties
- **THEN** `platform` SHALL be `macos`, `executionEnvironment` SHALL be `bare`,
  and the two native version properties SHALL match their bundle keys
- **AND** `expoVersion` SHALL be null

#### Scenario: The module is read on Windows
- **GIVEN** a packaged Windows host supplies a four-part package version
- **WHEN** JavaScript reads the generated constants properties
- **THEN** `platform` SHALL be `windows` and `nativeBuildVersion` SHALL equal
  that four-part version
- **AND** `nativeAppVersion` and `expoVersion` SHALL be null

#### Scenario: Values are unavailable
- **GIVEN** the host supplies no native version metadata
- **WHEN** JavaScript reads the generated properties
- **THEN** both native version properties SHALL be null
- **AND** no other source SHALL silently replace them

#### Scenario: Properties are immutable
- **GIVEN** the module was generated and registered
- **WHEN** strict-mode JavaScript assigns a constants property
- **THEN** the assignment SHALL fail with `TypeError`
- **AND** the property value SHALL remain unchanged

#### Scenario: An unsupported host accesses the module
- **GIVEN** the module runs on Android, iOS, Linux, or an unknown OS
- **WHEN** JavaScript first accesses it
- **THEN** it SHALL receive a catchable error naming unsupported platform use

### ADDED: Runtime-scoped session identity

The module SHALL generate one canonical GUID per `DotnetRuntimeContext` and
SHALL NOT persist it. `sessionId` SHALL remain stable within that context and
change for a newly created context.

#### Scenario: React runtime is replaced
- **GIVEN** one runtime context exposes a session ID
- **WHEN** it is disposed and another runtime context registers the package
- **THEN** the second context SHALL expose a different session ID

### ADDED: Host-supplied native app metadata

The native host SHALL supply only the version strings it owns through the v3
create entry point. The generated host SHALL validate and copy them before
module registration. `DotnetRuntimeContext` SHALL expose immutable
`HostAppMetadata` without platform-specific API calls in the managed core.

#### Scenario: Metadata is malformed
- **GIVEN** a supplied metadata struct has an invalid size, version,
  pointer/length pair, UTF-8 sequence, empty or whitespace-only value, or NUL
- **WHEN** the generated host decodes it
- **THEN** runtime-context creation SHALL fail through the structured error
  result before any module registers

#### Scenario: A stale adapter loads the generated host
- **GIVEN** an adapter requests a create symbol with the wrong ABI signature
- **WHEN** it resolves the generated host entry point
- **THEN** symbol resolution SHALL fail before a function pointer is called

#### Scenario: An unpackaged Windows host starts
- **GIVEN** `Package::Current()` reports no package identity
- **WHEN** the host creates the managed context
- **THEN** both native version fields SHALL be unavailable
- **AND** no process or assembly version SHALL be substituted
