# Delta: Linux hosts for the headless Hermes console proof

Modifies `docs/specs/hermes-testhost.md`, requirement "Headless Hermes
Console Runners".

## Requirement (replaces the runner-pairing sentence)

The headless Hermes console proof SHALL have platform-paired runners. The
bash runner `scripts/run-hermes-console-app.sh` SHALL support macOS and
Linux hosts, selecting the host NativeAOT runtime identifier and published
library name per platform. The Windows HostFXR runner SHALL be
`scripts/run-hermes-console-app.ps1`.

#### Scenario: Linux console proof runs both loaders
- **GIVEN** a Linux host with a Linux Hermes prebuilt destroot
- **WHEN** a developer runs `scripts/run-hermes-console-app.sh` with
  `EXPO_JSI_DOTNET_LOADER` set to `hostfxr` or `nativeaot`
- **THEN** it SHALL build the managed console app for the Linux host RID
- **AND** the native host SHALL load `HermesConsoleApp.so` (NativeAOT) or
  the HostFXR runtime via nethost (HostFXR)
- **AND** the proof SHALL exercise the same registration behavior as the
  macOS console proof
