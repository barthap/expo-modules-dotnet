# Expo Desktop Prebuild For `apps/desktop-app`

## Goal

Generate the `macos` and `windows` native projects of `apps/desktop-app` with
`expo-desktop prebuild` (expo-desktop 1.0), with no committed native folders
and no hand edits after prebuild. All .NET wiring comes from the
`expo-modules-dotnet` config plugin. This is roadmap item 039 and closes plan
018.

A developer runs prebuild and `expo-desktop run macos` or
`expo-desktop run windows`, and the app starts with the C# modules loaded.

## Scope

### In scope

- `apps/desktop-app`: expo-desktop 1.0 packages and the SDK 54 template, the
  app config keys that prebuild needs, gitignored `macos/` and `windows/`,
  `package.json` scripts, Metro config, and `README.md`.
- `packages/expo-modules-dotnet/app.plugin.js`: plugin options, a `macos` mod,
  and the `windows` mods that replace every hand edit in the current trees.
- `packages/expo-modules-dotnet/windows/`: package-owned MSBuild files that
  the `windows` mods import.
- `packages/expo-modules-dotnet-autolinking`: export the host generator, the
  managed solution projection, and the mixed-mode debugger setup so that the
  plugin can call them.
- `pnpm-workspace.yaml` and `patches/`: the RNW patches move to the RNW 0.81.37
  dependency versions.
- `docs/specs/runtime-and-abi.md` and `docs/specs/dotnet-autolinking.md`.
- Closing plan 018.

### Out of scope

- Writing into `globalThis.expo` or replacing expo-desktop stubs (roadmap 040).
- SDK 55 / RN 0.83 / RNW 0.83.
- `apps/mobile-app`, and plugin options for iOS and Android.
- Changes to expo-desktop. Gaps are reported upstream.
- ABI changes.

### Anti-goals

- A generated project that needs a manual step between prebuild and run,
  other than one `package.json` script that runs a needed step and the run
  command together.
- Committed files under `apps/desktop-app/macos` or `apps/desktop-app/windows`.

## Accepted design

### Native folders are CNG output

- `apps/desktop-app/.gitignore` ignores `macos/`, `windows/`, and the
  app-root MSBuild files that the plugins write (`Directory.Build.props`,
  `Directory.Build.targets`, `NuGet.config`).
- The current committed trees are deleted. Their .NET content moves into
  plugin mods (below). Their remaining content is template output.
- Template names come from expo-desktop. Windows always uses `MyApp`. macOS
  uses the sanitized app name, so `"Desktop App"` gives `DesktopApp.xcodeproj`
  and the target `DesktopApp-macOS`. The current names `desktopapp` (macOS)
  and `DesktopApp` (Windows) go away. Mods find files by search, not by a
  fixed name.
- The macOS template project has two app targets, `<name>-iOS` and
  `<name>-macOS`. The macOS mods edit only the `-macOS` target.

### Packages and template

- `apps/desktop-app` moves to the versions of
  `expo-desktop-template-bare-minimum@54.81.5`: `expo-desktop ^1.0.5`,
  `expo-desktop-modules-core ~54.0.14`, `expo-desktop-stubs ~54.0.15`,
  `react-native-macos 0.81.9`, `react-native-windows 0.81.37`, and dev
  `expo-desktop-metro-config ~54.81.3`. `expo-desktop-config-plugins` and
  `expo-desktop-prebuild-config` move to the versions that `expo-desktop
  1.0.5` depends on.
- The `react-native-81` catalog in `pnpm-workspace.yaml` takes the new
  `react-native-macos` and `react-native-windows` patch versions. `react-native`
  and `expo` stay on the versions that the catalog has now, unless the template
  versions are needed for the build to pass. If so, the plan records why.
- RNW 0.81.37 depends on `@react-native-windows/cli@0.81.10` and
  `find-dotnet-tools@0.0.0-canary.3`. The two pnpm patches move to those
  versions. A patch is dropped only if the new version already has its fix.
  The Windows 10 PowerShell deploy bug is still present in cli 0.81.10.
- `expo-desktop-template-bare-minimum@54.81.5` is a dev dependency of the app.
  Prebuild resolves a template from the project before it downloads one, so
  this pins the SDK 54 template.

### App config

`app.json` holds every value that prebuild otherwise asks for, so prebuild
runs without prompts:

- `macos.bundleIdentifier`: `dev.expo.modules.dotnet.desktopapp` (the current
  value).
- `windows.namespace`, `windows.projectGuid`, `windows.packageGuid`: fixed
  values committed in `app.json`.
- Any display-name plugin props that prebuild writes.
- `plugins` lists `expo-modules-dotnet` and the app-local toolset plugin.

### `expo-modules-dotnet` plugin options

```json
["expo-modules-dotnet", {
  "macos": { "loader": "hostfxr" },
  "windows": { "loader": "hostfxr" }
}]
```

- Only `macos` and `windows` keys. There is no shared `loader` key, because
  iOS and Android support only `nativeaot` at runtime.
- `loader` is `"hostfxr"` or `"nativeaot"`. Each platform defaults to
  `"hostfxr"`, which is the CLI default for desktop platforms. So
  `"plugins": ["expo-modules-dotnet"]` keeps working.
- The plugin validates its options at prebuild time and throws an error that
  names the bad key and the allowed values. An unknown key is an error.
  Upstream `expo-build-properties` uses the same pattern.

### macOS mods

- **Podfile:** add the helper `require` and
  `use_expo_modules_dotnet!(platform: :macos, project_root: ...)` after
  `use_expo_modules!`, idempotently. The Ruby helper already supports
  `:macos`. Only the JS Podfile edit in `app.plugin.js` changes: today it reads
  only `ios/Podfile` and writes `platform: :ios`. The CocoaPods
  `script_phase` already makes the `[CP-User] Link Expo .NET Modules` build
  phase and copies `macos/Managed` into the bundle resources.
- **Build setting:** `EXPO_DOTNET_LOADER = <macos.loader>` on the app target,
  for every build configuration.
- **Info.plist:** `ExpoModulesDotnetLoader = $(EXPO_DOTNET_LOADER)`.
- **Managed folder:** the mod creates `macos/Managed` with its `.gitignore`.
  The builder checks whether the current folder reference in the app target's
  resources is still needed, given that the script phase already copies the
  folder. If it is not, it goes away.
- The mods use the `macos` platform mods of `expo-desktop-config-plugins`.
  If a mod needs `projectName`, which expo-desktop sets only for `ios`, the
  mod finds the project file itself.

### Windows mods

- **App-root files:**
  - `Directory.Build.props` imports
    `.expo/dotnet/windows/ExpoDotnetReactNativeDir.props`, as it does now.
  - The generated props file also sets
    `ExpoDotnetLoader` (from `windows.loader`) and `ExpoDotnetManagedDir`
    (the app's `windows/Managed/` folder), each only when the property is
    empty, so a value from the command line still wins.
- **App `.vcxproj`:** one `<Import>` of a package-owned targets file in
  `packages/expo-modules-dotnet/windows/`. That file imports
  `ExpoDotnetAutolink.targets` and holds the copy of `Managed` into
  `$(OutDir)Managed\`. No other XML goes into the `.vcxproj`.
- **Package `.wapproj`:** one `<Import>` of a package-owned targets file that
  adds the `Managed` payload. The payload path uses the app project name from
  MSBuild properties, not a fixed `DesktopApp` or `MyApp`.
- **`.sln` and debugger:** a `windows` mod generates the
  `ExpoDotnetHost.csproj` host, then runs the managed solution projection and
  the mixed-mode debugger setup, in the same order as `sync-windows` today. The plugin calls code exported from
  `expo-modules-dotnet-autolinking`, so the logic stays in one place.
- **Run-time check:** `expo-desktop run windows` runs `autolink-windows`
  before it builds. If that step removes our `.sln` entries, the app's `windows`
  script runs `sync-windows` and then `expo-desktop run windows`. The plan
  records which case is true.
- **Mod order:** expo-desktop's default `windows` mods run before ours. None
  of them writes `Directory.Build.props` at expo-desktop `1e45de7`. A test
  runs our mods after theirs and checks that our `<Import>` is in the file.

### App-local plugin for non-.NET fixes

The current trees hold fixes that are not .NET work. They go into one plugin in
`apps/desktop-app/plugins/`, not into `expo-modules-dotnet`:

- The v143 to v145 platform toolset override (today
  `apps/desktop-app/Directory.Build.targets`).
- `NuGet.config` with the `react-native` Azure feed and nuget.org. Prebuild
  does not write it.
- Any other current hand edit that the first build after prebuild still needs.
  Candidates: the Podfile `react-native-macos` path and `REACT_NATIVE_PATH`
  setup for the pnpm layout, autolinking through `node` in place of `npx`,
  `RCT_NEW_ARCH_ENABLED` / `RCTNewArchEnabled`, `HermesPlatform` and
  `CodegenCommand` in `ExperimentalFeatures.props`, and
  `MinimumVisualStudioVersion`. RN macOS 0.81 turns on the New Architecture
  and Hermes by default, so some may not be needed.

The builder first builds the plain prebuild output, then adds only the fixes
that a failure proves necessary. Each fix has a comment with the failure it
prevents. Each fix that is an expo-desktop gap is listed in the plan for an
upstream report; the report itself needs approval before it is posted.

### Metro

- The app tries `expo-desktop-metro-config` in place of the hand-written
  `resolveRequest`. If it does not resolve `react-native-macos` and
  `react-native-windows` in the pnpm workspace, the hand-written config stays,
  and the plan records the reason.

### Scripts

- `macos` and `windows` scripts run `expo-desktop prebuild` (when the folder
  is missing) and `expo-desktop run <platform>`.
- `autolink:windows` and `build:managed*` are removed if prebuild and the build
  hooks cover them. A script that still has a reason to exist stays with that
  reason in `README.md`.

## Delta requirements

### MODIFIED Requirement (`runtime-and-abi.md`): Loader Choice Preserves ABI Shape

REMOVED Scenario: Windows config-plugin resolution is deferred to expo-desktop.

ADDED Scenario: Windows `ReactNativeDir` comes from expo-desktop prebuild

- **GIVEN** a Windows app lists `expo-modules-dotnet` as a config plugin
- **WHEN** `expo-desktop prebuild --platform windows` runs
- **THEN** the plugin's `windows` mod SHALL write
  `.expo/dotnet/windows/ExpoDotnetReactNativeDir.props` from Node resolution
  of `react-native` at the app root
- **AND** the app-root `Directory.Build.props` SHALL import that file
- **AND** the adapter project SHALL keep
  `Condition="'$(ReactNativeDir)' == ''"`, so a value that a host supplies wins
- **AND** the adapter project SHALL NOT derive `ReactNativeDir` from its own
  package directory or from a directory walk

ADDED Scenario: Host without `windows` mods

- **GIVEN** a Windows app that is not generated by expo-desktop prebuild
- **WHEN** the adapter project builds
- **THEN** the app SHALL supply `ReactNativeDir` itself
- **AND** standard Expo prebuild and the RNW CLI SHALL NOT be documented as
  hosts that run the plugin's `windows` mods

### ADDED Requirement (`dotnet-autolinking.md`): Desktop App Wiring Uses The Config Plugin

`packages/expo-modules-dotnet/app.plugin.js` SHALL own every .NET edit to the
native projects that `expo-desktop prebuild` generates for `macos` and
`windows`. A CNG desktop app SHALL build and run after prebuild with no hand
edit.

#### Scenario: macOS prebuild

- **GIVEN** a CNG app that lists `expo-modules-dotnet` in `expo.plugins`
- **WHEN** `expo-desktop prebuild --platform macos` runs and CocoaPods
  installs
- **THEN** the Podfile SHALL call
  `use_expo_modules_dotnet!(platform: :macos, ...)`
- **AND** the app target SHALL have `EXPO_DOTNET_LOADER` set to the configured
  loader, the `ExpoModulesDotnetLoader` Info.plist key, and the `Managed`
  folder as a bundle resource
- **AND** the generated files SHALL contain no machine-local absolute paths

#### Scenario: Windows prebuild

- **GIVEN** the same app
- **WHEN** `expo-desktop prebuild --platform windows` runs
- **THEN** the app `.vcxproj` and the package `.wapproj` SHALL each import one
  package-owned targets file
- **AND** the `.sln` SHALL contain the generated host, the managed core
  packages, and the resolved module projects under `Expo .NET Managed`
- **AND** the generated props SHALL set `ExpoDotnetLoader` and
  `ExpoDotnetManagedDir` only when they are empty

#### Scenario: Prebuild runs twice

- **GIVEN** a prebuild already ran
- **WHEN** prebuild runs again without `--clean`
- **THEN** each mod SHALL leave exactly one copy of each edit

### ADDED Requirement (`dotnet-autolinking.md`): Config Plugin Options Are Validated

The plugin SHALL accept `macos.loader` and `windows.loader`, each
`"hostfxr"` or `"nativeaot"`, each defaulting to `"hostfxr"`. It SHALL throw at
prebuild time for an unknown key or value, and the error SHALL name the key and
the allowed values.

#### Scenario: No options

- **GIVEN** `"plugins": ["expo-modules-dotnet"]`
- **WHEN** prebuild runs for `macos` or `windows`
- **THEN** the loader SHALL be `hostfxr`

#### Scenario: Shared loader key

- **GIVEN** `["expo-modules-dotnet", { "loader": "nativeaot" }]`
- **WHEN** prebuild runs
- **THEN** the plugin SHALL throw, and the error SHALL name `loader` as an
  unknown key

### MODIFIED Requirement (`dotnet-autolinking.md`): Windows Solution Projection

The managed solution projection SHALL run from the plugin's `windows` mod
during expo-desktop prebuild. `sync-windows` SHALL stay for apps with
committed native projects. Both SHALL use the same exported code.

## Verification

- `pnpm install --frozen-lockfile` exits 0.
- `pnpm --filter expo-modules-dotnet test`: tests for option validation, each
  `macos` mod, each `windows` mod against the SDK 54 template files, mod order
  after expo-desktop's default mods, and running prebuild twice.
- `pnpm --filter expo-modules-dotnet-autolinking test` passes.
- `pnpm --filter desktop-app typecheck` exits 0.
- macOS: `expo-desktop prebuild --platform macos --clean`, then
  `expo-desktop run macos`. The Metro log shows
  `[ExampleModule] C# add(22, 20) returned 42`, with no hand edit, and
  `git status` shows no tracked change under `apps/desktop-app`.
- Windows, through the Windows Remote Control session: the same with
  `--platform windows`. The roadmap notes get the date, commit, and result.
- `scripts/test-managed.sh` passes with no skips.
- `scripts/format.sh --check --all` passes.

## Stop conditions

- The SDK 54 template needs RN or RNW outside the 0.81 line.
- The RNW patches cannot move to the RNW 0.81.37 dependency versions, and the
  Windows 10 PowerShell deploy bug they fix is still present.
- The generated Windows app needs `ReactNativeDir` forwarded through
  `ProjectReference`.
- A generated project needs an edit that no config plugin can own.
- expo-desktop prebuild does not run `macos` or `windows` mods from a user
  plugin in practice, though the source at `1e45de7` shows that it does.
