# Plan 023: `expo-constants-dotnet` for Windows and macOS

> **Executor instructions**: Run the drift check, then use the living-spec
> workflow before editing code. This plan deliberately does not add Expo global
> registration or upstream package aliasing.
>
> **Drift check**: `git diff --stat 9247d75d..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet docs/specs/`

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (host metadata provenance and heterogeneous config values)
- **Depends on**: none; execute after plan 022 for package-pattern validation
- **Category**: authored module
- **Planned at**: `9247d75d`, 2026-07-24

## Why this matters

Constants exercises generated `[JS]` properties, a central Expo module name,
and host-app metadata without involving device APIs. It must be honest about
what the .NET host can supply. The current generator supports typed properties
and records, but not a generic `object`/arbitrary JSON codec, so `expoConfig`
cannot be faked by serializing process state or by adding reflection-based
conversion.

## Current state

- `docs/module-authoring-guide.md` documents `[JS]` properties. Existing
  `GeneratedPropertiesModule` tests under
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/`
  prove generated accessors and read-only behavior.
- `docs/specs/modules-core-boundary.md` limits generated module codecs to
  supported scalar, record, collection, enum, `ArrayBuffer`, and explicit JSI
  wrapper shapes. `JavaScriptObject` is not a general generated codec.
- `ModuleRegistry.GetOrCreateDotnetModulesObject` owns the normal
  `_expoDotnet.modules` registration target. `GetOrCreateExpoModulesObject`
  exists as an explicit compatibility path but must not be used here.
- The autolinker creates an app-level host but supplies no typed app metadata
  service to authored modules. That missing input is an architectural decision,
  not something a module can infer safely.

## Scope

**In scope**

- Create `packages/expo-constants-dotnet`, registering native name
  `ExponentConstants` through the standard dotnet registry.
- Windows/macOS only, with a clear rejected call or package-level unsupported
  error on Android/iOS.
- A deliberate, documented constants model covering platform, execution
  environment, session identity, native version/build metadata, and an optional
  typed app configuration payload only when an approved source exists.
- Generated property coverage, TypeScript facade types, and deterministic
  metadata/provider tests.

**Out of scope**

- `globalThis.expo.modules.ExponentConstants`, `NativeModulesProxy`, Metro
  aliases, or a claim that importing `expo-constants` works.
- Reading an arbitrary Expo manifest through reflection, untrusted environment
  variables, current-working-directory files, or a JSON `object` codec.
- Android/iOS, EAS Updates, device identifiers, push tokens, and remote config.

## Steps

### Step 1: Approve metadata provenance and public shape

Create `docs/changes/2026-<mm-dd>-expo-constants-dotnet/spec.md` and matching
`plan.md`. The specification must settle all public values before code:

1. Package/native names and the Windows/macOS support matrix.
2. Exact TypeScript and C# fields, including their nullability. At minimum
   define `platform`, `executionEnvironment`, `sessionId`, `nativeAppVersion`,
   `nativeBuildVersion`, and `expoVersion` semantics rather than returning
   placeholder strings.
3. A single provenance chain for every field. Use build-generated app metadata
   when the host can provide it, assembly/package metadata where that is the
   defined source, and `null` when unavailable. Never silently substitute a
   different source.
4. Whether the first release includes `expoConfig`. If yes, define an explicit
   typed configuration schema and a generated/staged host input. If arbitrary
   Expo config is required, STOP and first plan a reusable safe JSON value
   boundary; do not add `Dictionary<string, object>` reflection conversion.
5. Whether identity is per runtime, installation, or app launch and its
   persistence/teardown behavior.
6. Error behavior for missing or malformed metadata, and a test-only metadata
   provider that cannot leak into production configuration.

Obtain approval and commit the change artifacts before implementation.

### Step 2: Create package and metadata boundary

Create a package matching the plan 022 layout:

- `packages/expo-constants-dotnet/package.json`
- `packages/expo-constants-dotnet/expo-module.config.json`
- `packages/expo-constants-dotnet/src/index.ts`
- `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet/`
- `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/`

Use one `net10.0` project and `ExponentConstants` as the native module name.
Put metadata reading behind a narrow package-private interface with explicit
production implementations for Windows/macOS and a deterministic test
implementation. Keep the module class responsible only for runtime platform
validation and typed export projection.

### Step 3: Implement constants without a generic JSON escape hatch

Use `[JS]` properties for immutable scalar/record values. For any optional
configuration object, use an approved positional record schema whose generated
lower-camel fields are tested. Do not return a stored `JavaScriptValue` unless
the approved spec gives precise runtime ownership and construction rules; a
typed record is preferred for this first package.

Session values must be isolated per `DotnetRuntimeContext` when their semantics
are runtime-scoped. Installation values must use an approved app-local storage
location and handle unavailable persistence explicitly. All public properties
must either have a supported getter or be omitted; do not expose methods that
throw `NotImplementedException` as a compatibility placeholder.

### Step 4: Test app metadata and generated property behavior

Add managed tests using the Hermes fixture for lower-camel property names,
read-only assignment failure, exact null behavior, two runtime sessions, and
unsupported platform failure. Add pure provider tests for missing metadata,
malformed metadata, version mapping, and persistence semantics. Add TypeScript
tests that ensure the facade requests `ExponentConstants` and does not touch
the Expo global registry.

### Step 5: Merge docs and verify

Merge accepted requirements into the appropriate living specs, archive the
change package, and update the index status.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package JS tests | `pnpm --filter expo-constants-dotnet test` | exit 0 |
| Managed package tests | `dotnet test packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/ExpoConstantsDotnet.Tests.csproj` | exit 0 |
| Full managed regression | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |

## Done criteria

- `ExponentConstants` has a typed, documented Windows/macOS constants surface.
- Every value has an approved source and deterministic test coverage.
- No generic JSON/reflection codec, global Expo registration, or fake
  compatibility export was added.
- Required verification and living-spec merge pass.

## STOP conditions

- The desired public shape requires arbitrary manifest JSON before a safe
  generic JSON boundary is designed and approved.
- The host cannot provide a provenance-preserving value for a required field.
- Mobile support, compatibility aliasing, or Expo core registry mutation becomes
  necessary to validate the package.
