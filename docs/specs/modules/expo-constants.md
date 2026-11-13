# expo-constants-dotnet

## Purpose

Define the behavior of the `packages/expo-constants-dotnet` authored module.

## Requirements

### Requirement: Expo Constants Dotnet Exposes Typed Host Values

`packages/expo-constants-dotnet` SHALL register `ExponentConstants` only under
`_expoDotnet.modules` and export a read-only TypeScript facade through its own
package. It SHALL expose exactly six getter-only `[JS]` properties:

| Property | Type | Source |
| --- | --- | --- |
| `platform` | `windows` or `macos` | Portable .NET OS APIs. |
| `executionEnvironment` | `bare` | This app-owned host is not Expo Go. |
| `sessionId` | canonical GUID string | A new value per `DotnetRuntimeContext` that instantiates the module. |
| `nativeAppVersion` | nullable string | macOS main-bundle `CFBundleShortVersionString`; null on Windows or when unavailable. |
| `nativeBuildVersion` | nullable string | macOS main-bundle `CFBundleVersion` or Windows package identity's four-part version; null when unavailable. |
| `expoVersion` | null | Expo Go version does not apply. |

The two native version properties SHALL remain available even though upstream
`expo-constants@57.0.2` removed them. The package SHALL NOT export
`expoConfig`, other compatibility fields, or methods. It SHALL NOT install a
package alias itself, use `NativeModulesProxy`, or register into the Expo
global registry. Access on Android, iOS, Linux, or an unknown OS SHALL fail
through a catchable `PlatformNotSupportedException`. It SHALL NOT read device
or installation identity or persist `sessionId`.

The package SHALL declare a substitution of `expo-constants`
`build/ExponentConstants.js` by `src/index.ts`, for the range `~18.0.13`
(`### Requirement: Dotnet Modules Declare Upstream Substitutions` in
`dotnet-autolinking.md`). The default export of `src/index.ts` SHALL be the
registered `ExponentConstants` module object, so that upstream
`expo-constants` reads the six properties: upstream copies them with an object
spread, which reads own enumerable properties, and generated `[JS]` properties
are own enumerable accessors. The module does not provide `manifest`, so
upstream `Constants.expoConfig` is `null` when the substitution is on, and
upstream `Constants.platform` is the string `'windows'` instead of upstream's
per-platform manifest object.

#### Scenario: Native versions retain their named sources
- **GIVEN** a desktop host supplies its native versions through
  `DotnetRuntimeContext.AppMetadata`
- **WHEN** JavaScript reads `ExponentConstants`
- **THEN** the version properties SHALL match those values, with missing
  values null
- **AND** no assembly, package, or process version SHALL replace a missing
  value

#### Scenario: Constants are read-only and runtime-scoped
- **GIVEN** the module is registered in one runtime context
- **WHEN** JavaScript reads its properties repeatedly and attempts strict-mode
  assignment
- **THEN** the values and `sessionId` SHALL remain stable, and assignment
  SHALL fail with `TypeError`
- **AND** a new runtime context SHALL receive a different `sessionId`

#### Scenario: Unsupported host accesses constants
- **GIVEN** the module is registered on a host other than Windows or macOS
- **WHEN** JavaScript first accesses it
- **THEN** it SHALL receive a catchable unsupported-platform error

#### Scenario: Upstream expo-constants reads the module through the substitution
- **GIVEN** a Windows app that opts in to `expo-constants`
- **WHEN** upstream `expo-constants` evaluates
- **THEN** its `Constants` SHALL have the six values of this module
- **AND** `Constants.expoConfig` SHALL be `null`
