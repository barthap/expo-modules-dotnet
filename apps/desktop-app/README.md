# Desktop App

Expo Desktop proof app for the React Native macOS and React Native Windows
lanes. It loads `example-module` through `expo-modules-dotnet` and calls
`ExampleModule.add(20, 22)` from shared JavaScript.

## Common Commands

From the repo root:

```powershell
pnpm --filter desktop-app start
pnpm --filter desktop-app macos
pnpm --filter desktop-app windows
pnpm --filter desktop-app build:managed:windows
pnpm --filter desktop-app typecheck
```

`pnpm --filter desktop-app windows` is the preferred Windows entry point. It
wraps `react-native run-windows` with the MSBuild property needed by the VS
2026 toolchain.

## Windows Setup

Use Visual Studio 2026 with:

- MSVC x64/x86 platform toolset `v145`.
- Windows SDK `10.0.22621.0` or newer.
- Node 22 or newer.
- pnpm 11.
- PowerShell 7 available as `pwsh.exe` for RNW CLI tooling.

If you run React Native Windows directly, pass the toolset explicitly:

```powershell
pnpm --filter desktop-app exec react-native run-windows --msbuildprops PlatformToolset=v145
```

Bare `react-native run-windows` may fail with a `v143` toolset error because
some Expo Desktop dependency projects inside `node_modules` still target the
older VS 2022 toolset. Do not install `v143` just for this proof unless you
explicitly want to satisfy those package projects locally.

## Visual Studio

Opening `apps/desktop-app/windows/DesktopApp.sln` in VS 2026 may prompt to
retarget `ExpoModulesCore` and `ExpoDesktopStubs` from `v143` to `v145`. That
prompt comes from npm dependency projects under `node_modules`, not from this
app's checked-in Windows project.

For local VS builds, retarget those dependency projects to `v145` when
prompted, or build from the command line with:

```powershell
MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /p:PlatformToolset=v145 /m:1
```

## Managed Artifacts

The Windows app builds managed proof artifacts through
`apps/desktop-app/scripts/build-managed.ps1` and stages them into
`apps/desktop-app/windows/Managed`. The app project copies that directory next
to the built executable as `Managed/`.

HostFXR is the default loader. Override with `EXPO_DOTNET_LOADER` or
`EXPO_JSI_DOTNET_LOADER` when testing another loader.
