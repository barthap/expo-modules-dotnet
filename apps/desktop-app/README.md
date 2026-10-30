# Desktop App

Expo Desktop app for the React Native macOS and React Native Windows lanes. It
loads `example-module` and `expo-constants-dotnet` through
`expo-modules-dotnet` and calls `ExampleModule.add(20, 22)` from shared
JavaScript.

The `macos/` and `windows/` folders are not committed.
[expo-desktop](https://github.com/shirakaba/expo-desktop) prebuild creates them
from the SDK 54 template (`expo-desktop-template-bare-minimum`, pinned as a dev
dependency), and the config plugins in `app.json` add everything else. Do not
edit the generated folders by hand: change a plugin and run prebuild again.

## Common Commands

From `apps/desktop-app`:

```sh
pnpm prebuild --platform macos --clean     # or --platform windows
pnpm macos                                 # expo-desktop run macos
pnpm windows                               # expo-desktop run windows
pnpm typecheck
```

`expo-desktop run <platform>` runs prebuild first when the platform folder is
missing. It builds the app, starts Metro, and launches the app. A successful
start logs `[ExampleModule] C# add(22, 20) returned 42` in the Metro output.

## Config Plugins

- `expo-modules-dotnet` adds the .NET wiring to both platforms:
  the Podfile hook, the loader build setting and `Info.plist` key on macOS,
  and the MSBuild imports, generated props, and managed solution projects on
  Windows. Its options select the loader per platform:

  ```json
  ["expo-modules-dotnet", {
    "macos": { "loader": "hostfxr" },
    "windows": { "loader": "hostfxr" }
  }]
  ```

  Each loader is `hostfxr` (the default) or `nativeaot`. Run prebuild again
  after you change an option.
- `./plugins/withDesktopAppFixes` holds fixes that are not .NET work:
  - the macOS Podfile resolves `react-native-macos` through Node, because the
    template path does not exist in the pnpm layout;
  - the app-root `Directory.Build.targets` lets RNW dependency projects that
    declare the `v143` toolset build with `v145` on Visual Studio 2026;
  - the app-root `NuGet.config` adds the React Native NuGet feed, which
    prebuild does not write.

## macOS

With the `hostfxr` loader, the plugin turns off the App Sandbox that the
template enables. The sandbox denies reads of the machine-wide .NET install.
With `nativeaot`, the sandbox stays on.

NativeAOT uses `osx-arm64` on Apple Silicon and `osx-x64` on Intel Macs. A
successful build logs `Built dotnet host: mode nativeaot` and stages
`libExpoDotnetHost.dylib` under `macos/Managed`.

When you build from Xcode.app, script phases do not inherit the interactive
shell environment. If Xcode cannot find `node` or `dotnet`, write the
gitignored local env file after each `prebuild --clean`:

```sh
cd apps/desktop-app/macos
{
  printf 'export NODE_BINARY="%s"\n' "$(command -v node)"
  printf 'export DOTNET_BINARY="%s"\n' "$(command -v dotnet)"
} > .xcode.env.local
```

## Windows

Use Visual Studio 2026 with:

- MSVC x64/x86 platform toolset `v145`.
- Windows SDK `10.0.22621.0` or newer.
- Node 22 or newer. The Node 24 bundled with VS 2026 works.
- pnpm 11.
- Windows PowerShell for RNW Appx deployment.

The workspace patches two RNW CLI packages so local Windows deploys use
Windows PowerShell before the NuGet-restored PowerShell 7.6.1 fallback, and so
the elevated helper relaunches the current PowerShell host correctly. On
Windows 10, the NuGet PowerShell can fail to load the Windows `Appx` module
during deploy.

The generated solution is `windows/MyApp.sln`, with the app project
`windows/MyApp/MyApp.vcxproj` and the package project
`windows/MyApp.Package/MyApp.Package.wapproj`. expo-desktop always names the
Windows projects `MyApp`. The package project is configured for mixed
debugging: HostFXR `Debug|x64` sessions can stop in both C++ and C# source.

## Managed Artifacts

The `expo-modules-dotnet-autolinking` CLI (`link --platform macos|windows`)
builds and stages the managed artifacts. It runs as an Xcode script phase on
macOS and as an MSBuild target on Windows, and stages into `macos/Managed` or
`windows/Managed`. `EXPO_DOTNET_LOADER` and `EXPO_JSI_DOTNET_LOADER` stay
available as local runtime overrides.
