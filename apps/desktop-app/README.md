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
delegates to the standard React Native Windows CLI flow:

```powershell
react-native run-windows
```

## Windows Setup

Use Visual Studio 2026 with:

- MSVC x64/x86 platform toolset `v145`.
- Windows SDK `10.0.22621.0` or newer.
- Node 22 or newer. The Node 24 bundled with VS 2026 works.
- pnpm 11.
- Windows PowerShell for RNW Appx deployment.

To run React Native Windows directly:

```powershell
pnpm --filter desktop-app exec react-native run-windows
```

The workspace patches two RNW 0.81.6 CLI packages so local Windows deploys use
Windows PowerShell before the NuGet-restored PowerShell 7.6.1 fallback, and so
the elevated helper relaunches the current PowerShell host correctly. On
Windows 10, the NuGet PowerShell can fail to load the Windows `Appx` module
during `react-native run-windows` deploy.

## Visual Studio

Open `apps/desktop-app/windows/DesktopApp.sln`, select `Debug` / `x64`, set
`DesktopApp.Package` as the startup project, start Metro separately, then press
Run/Debug.

The app includes `Directory.Build.targets`, which lets dependency projects that
still declare `v143` build with `v145` when the VS 2026 toolset is installed.
If VS still shows a setup-assistant retarget prompt, it comes from npm
dependency project files under `node_modules`; the checked-in build does not
require installing `v143` for this proof.

To build from the command line with the same solution:

```powershell
MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /m:1
```

RNW autolink is enabled in both CLI and MSBuild/VS flows. Regenerate autolink
files manually from `apps/desktop-app` when native dependencies change:

```powershell
pnpm exec react-native autolink-windows --sln "windows\DesktopApp.sln" --proj "windows\DesktopApp\DesktopApp.vcxproj"
```

`AutolinkedNativeModules.g.*` files are RNW-generated and checked in, matching
the RNW app template. Do not hand-format them; the MSBuild autolink check
compares the generated output byte-for-byte.

## Managed Artifacts

The Windows app builds managed proof artifacts through
`apps/desktop-app/scripts/build-managed.ps1` and stages them into
`apps/desktop-app/windows/Managed`. The app project copies that directory next
to the built executable as `Managed/`.

HostFXR is the default loader. Override with `EXPO_DOTNET_LOADER` or
`EXPO_JSI_DOTNET_LOADER` when testing another loader.
