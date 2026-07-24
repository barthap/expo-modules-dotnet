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
pnpm --filter desktop-app autolink:windows
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

Use `pnpm --filter desktop-app autolink:windows` for the complete checked-in
solution projection. It first runs that app-local RNW command, then adds
`ExpoDotnetHost`, `Expo.JSI`, `Expo.ModulesCore`, and linked C# modules under
the `Expo .NET Managed` solution folder. The normal build-phase `link` hooks
remain responsible for managed artifact staging and ABI alignment.

The package project is configured for mixed debugging: HostFXR `Debug|x64`
sessions can stop in both C++ and C# source. NativeAOT remains a native-only
debugging workflow. `sync-windows --check` verifies the managed projection;
it intentionally does not invoke RNW's unreliable `autolink-windows --check`.

`AutolinkedNativeModules.g.*` files are RNW-generated and checked in, matching
the RNW app template. Do not hand-format them; the MSBuild autolink check
compares the generated output byte-for-byte.

## Managed Artifacts

Both apps build managed artifacts through the `expo-modules-dotnet-autolinking`
CLI (`link --platform macos|windows`), which runs automatically as an Xcode
script phase (macOS) or MSBuild target (Windows) and stages them into
`apps/desktop-app/macos/Managed` / `apps/desktop-app/windows/Managed`. The
Windows app project copies that directory next to the built executable as
`Managed/`.

HostFXR is the default loader. Override with `EXPO_DOTNET_LOADER` or
`EXPO_JSI_DOTNET_LOADER` when testing another loader.

### Windows NativeAOT

The Windows app defaults to HostFXR. To run the NativeAOT loader, pass the
matching MSBuild property so the autolinking target stages the NativeAOT
payload and writes the packaged loader marker:

```powershell
pnpm --filter desktop-app exec react-native run-windows --release --msbuildprops ExpoDotnetLoader=nativeaot
```

For a build-only check without launching the app:

```powershell
MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Release /p:Platform=x64 /p:ExpoDotnetLoader=nativeaot /m:1
```

To stage only the managed NativeAOT artifacts:

```powershell
pnpm --filter desktop-app exec expo-modules-dotnet-autolinking link --platform windows --mode nativeaot --configuration Release --rid win-x64
```

`EXPO_DOTNET_LOADER` and `EXPO_JSI_DOTNET_LOADER` are still available as local
runtime overrides, but AppX activation does not have to inherit them for the
NativeAOT flow above.

### macOS NativeAOT

The macOS app defaults to HostFXR. To run it with NativeAOT, override the
`EXPO_DOTNET_LOADER` Xcode build setting with `nativeaot`. This setting controls
both the managed artifact staging phase and the loader value embedded in the
built app's `Info.plist`.

In Xcode, open `apps/desktop-app/macos/desktopapp.xcworkspace`, select the
`desktopapp-macOS` target, and set the User-Defined `EXPO_DOTNET_LOADER` build
setting to `nativeaot` for the `Debug` configuration. Then run the app from
Xcode with Metro running.

For a command-line Debug build, pass the same setting directly to
`xcodebuild`:

```sh
set -euo pipefail
xcodebuild \
  -workspace apps/desktop-app/macos/desktopapp.xcworkspace \
  -scheme desktopapp-macOS \
  -configuration Debug \
  -derivedDataPath apps/desktop-app/macos/build \
  EXPO_DOTNET_LOADER=nativeaot \
  2>&1 | xcsift -f toon
open apps/desktop-app/macos/build/Build/Products/Debug/desktopapp.app
```

NativeAOT uses `osx-arm64` on Apple Silicon and `osx-x64` on Intel Macs. A
successful build logs `Built dotnet host: mode nativeaot` and stages
`libExpoDotnetHost.dylib` under `apps/desktop-app/macos/Managed`.

### macOS Xcode Environment

When building from Xcode.app, script phases do not inherit the interactive
shell environment. If Xcode cannot find `node` or `dotnet`, configure the
gitignored local env file:

```sh
cd apps/desktop-app/macos
{
  printf 'export NODE_BINARY="%s"\n' "$(command -v node)"
  printf 'export DOTNET_BINARY="%s"\n' "$(command -v dotnet)"
} > .xcode.env.local
```

The Expo .NET script phase follows the same convention as React Native and
Expo: it reads `.xcode.env`, then `.xcode.env.local`, and uses `NODE_BINARY`
and `DOTNET_BINARY` for the autolinking CLI and .NET build steps.
