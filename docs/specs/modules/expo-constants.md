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
`expoConfig`, other compatibility fields, or methods. It SHALL NOT install an
upstream package alias, use `NativeModulesProxy`, or register into the Expo
global registry. Access on Android, iOS, Linux, or an unknown OS SHALL fail
through a catchable `PlatformNotSupportedException`. It SHALL NOT read device
or installation identity or persist `sessionId`.

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
