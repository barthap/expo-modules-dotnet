# 039: Generate `apps/desktop-app` native projects with expo-desktop 1.0 prebuild

> **Deliver with `living-spec-workflow`**: brainstorm, delta spec, plan,
> implement, verify, merge into `docs/specs/`. Set this item's row in
> `docs/roadmap.md` to `IN PROGRESS` when you start and `DONE` when the
> slice closes, then move this brief to `docs/archive/plans/`.
>
> **Drift check (run first)**: `git diff --stat c7dccbe..HEAD -- apps/desktop-app packages/expo-modules-dotnet/app.plugin.js packages/expo-modules-dotnet/src/__tests__/windowsReactNativeDir.test.ts packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj packages/expo-modules-dotnet-autolinking docs/specs/runtime-and-abi.md`
> If an in-scope file changed, compare "Current state" with the live code.
> A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MED. Two native trees are regenerated, and Windows needs a
  Windows machine to verify.
- **Depends on**: none
- **Category**: migration
- **Planned at**: `c7dccbe`, 2026-10-22

## Why this matters

expo-desktop reached 1.0 (`expo-desktop@1.0.5`, 2026-10-12). Its
`prebuild` now generates `macos` and `windows` projects and runs config-plugin
mods for both platforms. `apps/desktop-app` still uses hand-kept native trees
and pre-1.0 packages (`expo-desktop ^0.1.37`), and its Windows plugin mod has
never run end to end, because no supported host executed `windows` mods. The
roadmap waited for exactly this (plan 018 P2 and the "Expo Desktop prebuild
integration" backlog item). After this lands, a developer can create the
desktop app's native projects with one prebuild command, and the .NET wiring
comes from our config plugin, not from hand edits.

## Current state

- `apps/desktop-app/package.json`: `expo-desktop ^0.1.37`,
  `expo-desktop-config-plugins ^1.1.34`, `expo-desktop-modules-core ^54.0.13`,
  `expo-desktop-prebuild-config ^1.0.20`, `expo-desktop-stubs ^54.0.13`,
  RN catalog `react-native-81` (RNW `0.81.29` in `pnpm-workspace.yaml`).
  Scripts run `react-native run-macos` / `run-windows` directly, plus
  `autolink:windows`, `build:managed`, and `build:managed:windows`.
- `apps/desktop-app/app.json`: plugins `["expo-modules-dotnet"]`; no
  `platforms`, `experiments.outOfTreePlatforms`, `macos`, or `windows` keys.
- `apps/desktop-app/macos` and `apps/desktop-app/windows`: hand-kept trees
  (50 tracked files). `macos/Podfile` calls `use_expo_modules!` and
  `use_expo_modules_dotnet!(project_root: ...)`.
  `macos/desktopapp-macOS/Info.plist:23` holds the `ExpoModulesDotnetLoader`
  key with value `$(EXPO_DOTNET_LOADER)`. The build setting
  `EXPO_DOTNET_LOADER = hostfxr` and a shell build phase that runs
  `expo-modules-dotnet-autolinking link --platform macos` and copies
  `macos/Managed` into the app resources are hand-written in
  `macos/desktopapp.xcodeproj/project.pbxproj` (around line 362).
- `apps/desktop-app/metro.config.js`: hand-written `resolveRequest` that maps
  `react-native` to `react-native-macos` / `react-native-windows`.
- `packages/expo-modules-dotnet/app.plugin.js`: an `ios` dangerous mod adds
  the Podfile hook, and a `windows` dangerous mod
  (`configureWindowsReactNativeDir`) writes
  `.expo/dotnet/windows/ExpoDotnetReactNativeDir.props` from Node resolution
  of `react-native` at the app root, then adds an `<Import>` to the app-root
  `Directory.Build.props`. It has no `macos` mod, and the Podfile helper
  (`addExpoModulesDotnetPodfileHook`) reads only `ios/Podfile`.
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj:165`:
  `<Error Condition="'$(ReactNativeDir)' == ''" ... />`. The library needs a
  host-supplied `ReactNativeDir`.
- `docs/specs/runtime-and-abi.md:367`, "Scenario: Windows config-plugin
  resolution is deferred to expo-desktop": the app SHALL NOT assume the
  `windows` mod has run, and the resolver "remains deferred until expo-desktop
  provides a supported Windows prebuild/mod execution path". This item ends
  that deferral.
- expo-desktop facts, from `shirakaba/expo-desktop@1e45de7`:
  - `packages/expo-desktop-config-plugins/src/plugins/mod-compiler.js` adds
    `withWindowsBaseMods` and orders `windows: { dangerous: -2, ... }`.
  - `src/plugins/windows/withReactNativeDirs.js` (wired through
    `with-expo-windows.js`) sets `ReactNativeDir` only in the **app**
    `.vcxproj` and `.wapproj`, with
    `GetDirectoryNameOfFileAbove(... 'node_modules\react-native\package.json')`.
  - `src/plugins/windows/withDirectoryBuildProps.js` exists, but nothing
    imports it at that commit. If it is wired later, it replaces the whole
    app-root `Directory.Build.props` with `writeFile`.
  - Template `MyApp.cpp` calls
    `expo::windows::AddAttributedModules(packageBuilder, true)` and
    `expo::windows::EnableEagerTurboModules(settings)`.
- The SDK 54 template, from `npm view expo-desktop-template-bare-minimum@54.81.5`:
  `expo ~54.0.35`, `expo-desktop ^1.0.5`, `react-native 0.81.6`,
  `react-native-macos 0.81.9`, `react-native-windows 0.81.37`,
  `expo-desktop-modules-core ~54.0.14`, `expo-desktop-stubs ~54.0.15`,
  dev `expo-desktop-metro-config ~54.81.3`.
- Our `apps/desktop-app/windows/DesktopApp/AutolinkedNativeModules.g.targets`
  adds `ExpoModulesDotnet.vcxproj` as a plain `ProjectReference`, with no
  `Properties` or `AdditionalProperties`. Properties set in the app project do
  not reach the library.

## Constraints

- Stay on SDK 54 / RN 0.81. RNW pins the workspace catalog to 0.81 (roadmap,
  "Considered and rejected"). An SDK 55 / RN 0.83 move is separate work.
- Keep `expo-desktop-modules-core` and `expo-desktop-stubs` installed. They
  create the `global.expo` that `registerRootComponent` needs on Windows.
  Replacing stubs is item 040.
- `packages/expo-modules-dotnet` stays usable without expo-desktop. Its
  `windows` mod runs under any host that executes `windows` mods. Without such
  a host, the app supplies `ReactNativeDir` itself, as the spec says today.
- Keep our app-scoped Node resolver. Do not adopt expo-desktop's
  `GetDirectoryNameOfFileAbove` walk for the library: it is the directory walk
  that commit `3c64fb4d` and the spec reject, and the app-project value does
  not reach the library. The `Condition="'$(ReactNativeDir)' == ''"` stays, so
  a value that a host supplies still wins.
- NativeAOT artifact staging goes through
  `packages/expo-modules-dotnet-autolinking` (`AGENTS.md`).
- No ABI change.

## Scope

**In scope**:

- `apps/desktop-app`: move to expo-desktop 1.0 and the SDK 54 template
  versions, add the app config keys that prebuild needs, generate `macos` and
  `windows` with `expo-desktop prebuild`, and run through `expo-desktop run`.
  Decide whether the native trees stay committed or become CNG output.
- `packages/expo-modules-dotnet/app.plugin.js`: a `macos` mod for the Podfile
  hook, the `ExpoModulesDotnetLoader` `Info.plist` entry, the
  `EXPO_DOTNET_LOADER` build setting, and the managed build phase that today
  lives by hand in `project.pbxproj`. Also check that the `windows` mod
  runs under expo-desktop prebuild. Add tests next to
  `src/__tests__/windowsReactNativeDir.test.ts`.
- `packages/expo-modules-dotnet-autolinking`: only where a manual script
  (`autolink:windows`, `build:managed*`) moves into prebuild or run.
- `docs/specs/runtime-and-abi.md`: replace the deferral scenario with the
  supported expo-desktop prebuild contract.
- `apps/desktop-app/README.md`.

**Out of scope**:

- Replacing expo-desktop stubs or writing into `globalThis.expo` (item 040).
- SDK 55 / RN 0.83 and RNW 0.83.
- `apps/mobile-app`.
- Changes to expo-desktop itself. Report gaps upstream instead.

## Open design questions

1. **Committed native trees or CNG?** Recommended: CNG for `macos` and
   `windows`, if prebuild output plus our plugin builds without hand edits.
   This is the claim the item exists to prove. Keep committed trees only for
   a gap that is written down.
2. **Mod order against `withReactNativeDirs` and a future
   `withDirectoryBuildProps`.** Recommended: check the order in
   `mod-compiler.js` and `expo-desktop-prebuild-config/src/withDefaultPlugins.js`
   and add a test. If upstream wires `withDirectoryBuildProps`, our `<Import>`
   must still be in the file after prebuild. If it would not be, ask upstream
   to merge into the file instead of replacing it.
3. **Metro config.** Recommended: use `expo-desktop-metro-config`
   (`makeMetroConfig`) in place of the hand-written `resolveRequest`, if it
   covers both platforms.
4. **What happens to plan 018.** Recommended: this item activates 018's P2
   resolver. Close 018 when this item closes. 018's P1 PDB-lock work did not
   reproduce and needs no further work.

## Done criteria

- [ ] `pnpm install --frozen-lockfile` exits 0
- [ ] `pnpm --filter expo-modules-dotnet test` passes, with new unit tests for
      the `macos` mod and for the `windows` mod output after expo-desktop's
      own Windows mods
- [ ] `pnpm --filter desktop-app typecheck` exits 0, after the open question 1
      decision (generated trees committed or gitignored)
- [ ] On macOS: `expo-desktop prebuild --platform macos --clean` (the docs
      also show it as `node --run prebuild`), then
      `npx expo-desktop run macos`. The Metro log shows
      `[ExampleModule] C# add(22, 20) returned 42` (from
      `apps/desktop-app/App.tsx:63`), with no hand edit after prebuild.
- [ ] On a Windows machine: the same with `--platform windows` and
      `npx expo-desktop run windows`, and the same log line. Add the date,
      commit, and result to the roadmap notes, as the 024 note does.
- [ ] Plan 018 is closed: row `DONE`, brief in `docs/archive/plans/`
- [ ] `scripts/test-managed.sh` passes with no skips
- [ ] `scripts/format.sh --check --all` passes
- [ ] The deferral scenario in `docs/specs/runtime-and-abi.md` is replaced
      with the expo-desktop prebuild contract
- [ ] Roadmap row is `DONE`, and this brief is in `docs/archive/plans/`

## STOP conditions

- `expo-desktop prebuild --platform windows` does not run `windows` mods from
  user plugins in `app.json`, only its own.
- The SDK 54 template needs RN or RNW outside the 0.81 catalog.
- The generated Windows app needs our library to read `ReactNativeDir` from
  the app project, which needs property forwarding through `ProjectReference`.
- A generated project needs a hand edit that no config plugin can own.

## Maintenance notes

- Item 040 depends on this. Mechanism B in 040 needs the eager TurboModule
  wiring that the expo-desktop template writes in `MyApp.cpp`.
- If upstream wires `withDirectoryBuildProps`, check that our `<Import>`
  survives a prebuild.
- An SDK 55 / RN 0.83 move goes to the "Unplanned backlog". It changes the
  RN 0.81 catalog decision.
