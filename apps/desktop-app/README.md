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
builds through React Native Windows, skips the RNW Appx deploy helper, starts
Metro when needed, and launches the unpackaged `windows/x64/Debug/DesktopApp.exe`.
This avoids a RNW/PowerShell 7 issue where `Get-AppxPackage` can fail while
loading the Windows `Appx` module.

## Windows Setup

Use Visual Studio 2026 with:

- MSVC x64/x86 platform toolset `v145`.
- Windows SDK `10.0.22621.0` or newer.
- Node 22 or newer.
- pnpm 11.
- PowerShell 7 available as `pwsh.exe` for RNW CLI tooling.

If you run React Native Windows directly, pass the toolset explicitly. To build
without deploying:

```powershell
pnpm --filter desktop-app exec react-native run-windows --no-deploy --no-launch --msbuildprops PlatformToolset=v145
```

Bare `react-native run-windows` may fail with a `v143` toolset error because
some Expo Desktop dependency projects inside `node_modules` still target the
older VS 2022 toolset. Do not install `v143` just for this proof unless you
explicitly want to satisfy those package projects locally.

Running bare `react-native run-windows` may also fail during RNW's Appx deploy
phase if its PowerShell helper cannot load `Get-AppxPackage`. Use the repo
script above for the current proof app.

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

The app project disables RNW's MSBuild-time autolink check because RNW 0.81's
generated C++ output is not byte-for-byte stable with this repo's formatting
rules. Regenerate autolink files manually from `apps/desktop-app` when native
dependencies change:

```powershell
pnpm exec react-native autolink-windows --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

The solution also disables MSBuild-time `codegen-windows` for this proof app.
The generated native sources are checked in; rerun the RNW codegen/autolink
steps manually when native package inputs change.

## Managed Artifacts

The Windows app builds managed proof artifacts through
`apps/desktop-app/scripts/build-managed.ps1` and stages them into
`apps/desktop-app/windows/Managed`. The app project copies that directory next
to the built executable as `Managed/`.

HostFXR is the default loader. Override with `EXPO_DOTNET_LOADER` or
`EXPO_JSI_DOTNET_LOADER` when testing another loader.
