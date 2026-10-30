# Expo Desktop Prebuild Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `apps/desktop-app` has no committed `macos/` or `windows/`; `expo-desktop prebuild` plus our config plugins create them, and `expo-desktop run <platform>` starts the app with the C# modules loaded.

**Architecture:** The `expo-modules-dotnet` plugin gets validated options and `macos`/`windows` mods. It registers mods with `withMod` / `withDangerousMod` from `expo/config-plugins`, so it has no dependency on expo-desktop; expo-desktop supplies the `macos` and `windows` mod providers at prebuild time. Windows MSBuild logic lives in package-owned `.targets` files that the mods import. Non-.NET fixes go into one app-local plugin.

**Tech Stack:** Node CommonJS config plugins, vitest, MSBuild, CocoaPods/Xcode, pnpm workspace, expo-desktop 1.0.5 (SDK 54 template `54.81.5`).

**Spec:** `docs/changes/2026-10-22-expo-desktop-prebuild/spec.md`

## Global Constraints

- Stay on the RN 0.81 line: `react-native-macos 0.81.9`, `react-native-windows 0.81.37`. A template need outside 0.81 is a STOP.
- `expo-desktop ^1.0.5`, `expo-desktop-modules-core ~54.0.14`, `expo-desktop-stubs ~54.0.15`, dev `expo-desktop-metro-config ~54.81.3`, dev `expo-desktop-template-bare-minimum 54.81.5`.
- Plugin options: only `macos.loader` and `windows.loader`, values `"hostfxr"` | `"nativeaot"`, default `"hostfxr"`. Unknown keys throw.
- `packages/expo-modules-dotnet` SHALL NOT depend on any `expo-desktop*` package at runtime. Dev dependencies for tests are allowed.
- macOS mods edit only the `<name>-macOS` target, never `<name>-iOS`.
- Windows mods find files by search (`windows/*.sln`, the `.vcxproj` the sln names as the app, the one `.wapproj`), never by the name `MyApp` or `DesktopApp`.
- Every mod is idempotent: a second prebuild without `--clean` leaves one copy of each edit.
- No machine-local absolute path in any committed or generated-and-committed file. Generated props under `.expo/` may hold resolved paths; they are gitignored.
- No ABI change. NativeAOT staging stays in `expo-modules-dotnet-autolinking`.
- Pushes to `development` for Windows checks need the user's approval each time. Upstream reports need approval before posting.

## Review Focus

1. **Plugin used without expo-desktop** (`apps/mobile-app`, `expo prebuild -p ios`): the `ios` Podfile edit still writes `platform: :ios`, and the `macos`/`windows` mods do nothing and do not throw. Test in Task 2.
2. **Template with `-iOS` target listed first:** `EXPO_DOTNET_LOADER` must land on the `-macOS` target only. Test in Task 3.
3. **Prebuild twice without `--clean`:** one Podfile call, one Info.plist key, one `<Import>` per project, no duplicate `.sln` projects. Tests in Tasks 3 and 4.
4. **`ExpoDotnetLoader` passed on the MSBuild command line:** the generated props must not override it. Test in Task 4.
5. **App display name with spaces (`"Desktop App"`)**: macOS project is `DesktopApp.xcodeproj`; the mod finds it without `projectName`. Covered by the Task 5 integration test.

---

### Task 1: Export the Windows solution preparation from autolinking

**Files:**
- Create: `packages/expo-modules-dotnet-autolinking/src/windows/prepareWindowsSolution.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/commands/syncWindowsCommand.ts` (use the new function after RNW autolink)
- Modify: `packages/expo-modules-dotnet-autolinking/src/index.ts` (export it)
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/prepareWindowsSolution.test.ts`

**Interfaces:**
- Produces: `prepareWindowsSolutionAsync(options: { appRoot: string; solutionPath: string; check?: boolean }): Promise<{ changed: boolean }>`, exported from the package root (`require('expo-modules-dotnet-autolinking').prepareWindowsSolutionAsync`). Order: `discoverDotnetManifestAsync` → `generateAggregator` (skipped when `check`) → `synchronizeManagedSolution` → `configureMixedModeDebugger` on the `.wapproj` that `findPackageProjectPath` returns. It does not run RNW `autolink-windows`. In `check` mode it writes nothing and returns `changed`.

- [ ] **Step 1:** Write tests on a temp app with a minimal `.sln` (one app `.vcxproj`, one `.wapproj`) and a fake resolved module:
  - `generates ExpoDotnetHost.csproj before projecting it into the solution` — after the call, `<appRoot>/.expo/dotnet/ExpoDotnetHost.csproj` exists and the `.sln` contains `Expo .NET Managed` and `ExpoDotnetHost`.
  - `is idempotent` — a second call returns `changed: false` and the `.sln` bytes are equal.
  - `check mode writes nothing` — file mtimes are unchanged and `changed` is `true` on a fresh solution.
  - `sets mixed-mode debugging on the package project` — `.wapproj` contains `<DebuggerType>Mixed</DebuggerType>`.
- [ ] **Step 2:** Run `pnpm --filter expo-modules-dotnet-autolinking test -- prepareWindowsSolution`. Expected: FAIL, function not exported.
- [ ] **Step 3:** Implement and make `sync-windows` call it after `runReactNativeWindowsAutolink`. Keep the existing `syncWindowsCommand.test.ts` passing unchanged.
- [ ] **Step 4:** Run `pnpm --filter expo-modules-dotnet-autolinking test`. Expected: all PASS.
- [ ] **Step 5:** Commit `refactor(autolinking): export Windows solution preparation`.

### Task 2: Plugin options and module layout

**Files:**
- Create: `packages/expo-modules-dotnet/plugin/options.js`
- Create: `packages/expo-modules-dotnet/plugin/ios.js` (move `addExpoModulesDotnetPodfileHook` here, parameterized by platform)
- Modify: `packages/expo-modules-dotnet/app.plugin.js` (entry: validate options, compose platform mods; keep the existing static helper properties so `windowsReactNativeDir.test.ts` passes)
- Modify: `packages/expo-modules-dotnet/package.json` (devDependency `expo-desktop-template-bare-minimum@54.81.5`, the fixture source for Tasks 2–5)
- Test: `packages/expo-modules-dotnet/src/__tests__/pluginOptions.test.ts`, `packages/expo-modules-dotnet/src/__tests__/podfileHook.test.ts`

**Interfaces:**
- Produces: `resolvePluginOptions(props: unknown): { macos: { loader: 'hostfxr' | 'nativeaot' }, windows: { loader: 'hostfxr' | 'nativeaot' } }` in `plugin/options.js`.
- Produces: `addExpoModulesDotnetPodfileHook(contents: string, platform: 'ios' | 'macos'): string` in `plugin/ios.js`. Error messages name `ios/Podfile` or `macos/Podfile` to match `platform`.

- [ ] **Step 1:** Write tests:
  - `undefined` and `{}` → both loaders `hostfxr`.
  - `{ macos: { loader: 'nativeaot' } }` → macOS `nativeaot`, Windows `hostfxr`.
  - `{ loader: 'nativeaot' }` throws, message contains `loader` and `macos`, `windows`.
  - `{ windows: { loader: 'aot' } }` throws, message contains `windows.loader`, `hostfxr`, `nativeaot`.
  - `{ windows: { loader: 'hostfxr', extra: 1 } }` throws naming `windows.extra`.
  - Podfile hook: the `ios` template Podfile gets `platform: :ios`; the expo-desktop SDK 54 macOS Podfile (read from the template dev dependency) gets `platform: :macos`; a second call returns the same string.
  - Review Focus 1: calling the plugin on a config with only `ios` (no expo-desktop providers) does not throw and leaves `mods.ios` set.
- [ ] **Step 2:** Run `pnpm --filter expo-modules-dotnet test`. Expected: new tests FAIL.
- [ ] **Step 3:** Implement. The plugin is `withExpoModulesDotnet(config, props)`; it calls `resolvePluginOptions(props)` first.
- [ ] **Step 4:** Run `pnpm --filter expo-modules-dotnet test`. Expected: PASS, including `windowsReactNativeDir.test.ts`.
- [ ] **Step 5:** Commit `feat(expo-modules-dotnet): validate config plugin options`.

### Task 3: macOS mods

**Files:**
- Create: `packages/expo-modules-dotnet/plugin/macos.js`
- Modify: `packages/expo-modules-dotnet/app.plugin.js`
- Test: `packages/expo-modules-dotnet/src/__tests__/macosMods.test.ts`

**Interfaces:**
- Consumes: `resolvePluginOptions`, `addExpoModulesDotnetPodfileHook(contents, 'macos')`.
- Produces: `withExpoModulesDotnetMacos(config, { loader })` that registers, through `withMod` from `expo/config-plugins` with `platform: 'macos'`:
  - `podfile`: Podfile hook with `platform: :macos`.
  - `xcodeproj`: `EXPO_DOTNET_LOADER = <loader>` on every build configuration of the native target whose name ends in `-macOS`. Throw if there is not exactly one such target.
  - `infoPlist`: `ExpoModulesDotnetLoader = "$(EXPO_DOTNET_LOADER)"`.
  - `dangerous`: create `macos/Managed/.gitignore` with the current content (`*`, `!.gitignore`, `!.gitkeep`) if missing.
- Pure helpers exported for tests: `setLoaderBuildSetting(project, loader)` (an `xcode` project object), `setLoaderInfoPlist(plist)`.

- [ ] **Step 1:** Write tests against the SDK 54 template `project.pbxproj` (parsed with `xcode`, from the template dev dependency):
  - `setLoaderBuildSetting` sets `EXPO_DOTNET_LOADER` on both configurations of `HelloWorld-macOS` and on none of `HelloWorld-iOS` (Review Focus 2).
  - Calling it twice gives the same serialized project.
  - `setLoaderInfoPlist` adds the key once; a second call is a no-op.
  - A project with two `-macOS` targets throws with a message that names both.
- [ ] **Step 2:** Run tests. Expected: FAIL.
- [ ] **Step 3:** Implement. If the `xcode` package is not already resolvable from `expo/config-plugins`, add it as a dependency of `expo-modules-dotnet` at the version that `@expo/config-plugins` uses.
- [ ] **Step 4:** Run `pnpm --filter expo-modules-dotnet test`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(expo-modules-dotnet): add macOS prebuild mods`.

### Task 4: Windows mods and package-owned targets

**Files:**
- Create: `packages/expo-modules-dotnet/windows/ExpoDotnetApp.targets` (imports `ExpoDotnetAutolink.targets` from its own directory; holds the `CopyExpoDotnetManagedArtifacts` target that today is in `DesktopApp.vcxproj:147-151`)
- Create: `packages/expo-modules-dotnet/windows/ExpoDotnetPackage.targets` (holds `AddExpoDotnetManagedPackagePayload` from `DesktopApp.Package.wapproj:76-85`; the `TargetPath`/`PackagePath` folder is the `%(Filename)` of the `.wapproj`'s `ProjectReference` to the app `.vcxproj`, not a literal name)
- Create: `packages/expo-modules-dotnet/plugin/windows.js`
- Modify: `packages/expo-modules-dotnet/app.plugin.js` (Windows props writer gains the loader and managed-dir properties)
- Modify: `packages/expo-modules-dotnet/package.json` (`files`, if present, includes the new targets)
- Test: `packages/expo-modules-dotnet/src/__tests__/windowsMods.test.ts`, extend `windowsReactNativeDir.test.ts`

**Interfaces:**
- Consumes: `resolvePluginOptions`; `prepareWindowsSolutionAsync` resolved at mod time with `require.resolve('expo-modules-dotnet-autolinking', { paths: [projectRoot] })`. If it does not resolve, throw an error that says to add `expo-modules-dotnet-autolinking` to the app.
- Produces: `withExpoModulesDotnetWindows(config, { loader })`, one `windows` dangerous mod that:
  1. Writes `.expo/dotnet/windows/ExpoDotnetReactNativeDir.props` with `ReactNativeDir` (as now), plus `ExpoDotnetLoader` = loader and `ExpoDotnetManagedDir` = `$(MSBuildThisFileDirectory)..\..\..\windows\Managed\`, each with `Condition="'$(X)' == ''"`.
  2. Merges the `Directory.Build.props` import (existing function).
  3. Adds one `<Import>` of `ExpoDotnetApp.targets` before `</Project>` in the app `.vcxproj`, and one of `ExpoDotnetPackage.targets` in the `.wapproj`. The import path is resolved through Node from the app root and written relative to the project file. Marker comment for idempotency, as `Directory.Build.props` does.
  4. Creates `windows/Managed/.gitignore`.
  5. Calls `prepareWindowsSolutionAsync({ appRoot, solutionPath })`.
- Pure helpers exported for tests: `addProjectImport(contents, importPath, marker): string`, `writeExpoDotnetWindowsProps(appRoot, { reactNativeDir, loader })`.

- [ ] **Step 1:** Write tests:
  - props content has `ExpoDotnetLoader` with the empty-condition and the configured value (Review Focus 4).
  - `addProjectImport` on the SDK 54 template `MyApp.vcxproj` adds exactly one import; a second call returns equal content (Review Focus 3).
  - on a temp copy of the template `windows/` folder, the mod run end to end (with a stub `prepareWindowsSolutionAsync` injected) edits the `.vcxproj` the `.sln` names and the single `.wapproj`, with no path containing `MyApp` hard-coded in plugin source (assert by `grep` of `plugin/windows.js` in the test).
  - the existing `windowsReactNativeDir.test.ts` cases still pass with the new props content.
- [ ] **Step 2:** Run tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `pnpm --filter expo-modules-dotnet test`. Expected: PASS.
- [ ] **Step 5:** Commit `feat(expo-modules-dotnet): add Windows prebuild mods`.

### Task 5: Prebuild integration test against real expo-desktop

**Files:**
- Modify: `packages/expo-modules-dotnet/package.json` (devDependencies: `expo-desktop-prebuild-config` and `expo-desktop-config-plugins` at the versions `expo-desktop@1.0.5` uses)
- Test: `packages/expo-modules-dotnet/src/__tests__/expoDesktopPrebuild.test.ts`

**Interfaces:**
- Consumes: the real `getPrebuildConfigAsync` and `compileModsAsync` from expo-desktop packages, Tasks 2–4.

- [ ] **Step 1:** Write a test that copies the template (`macos/`, `windows/`, a minimal `app.json` with name `"Desktop App"` and plugin `expo-modules-dotnet`) to a temp dir, then runs the expo-desktop prebuild config and `compileModsAsync` for `macos` and `windows` with `prepareWindowsSolutionAsync` stubbed. Assert:
  - macOS: Podfile has `platform: :macos`; `DesktopApp.xcodeproj` (Review Focus 5) `-macOS` target has `EXPO_DOTNET_LOADER = hostfxr`; Info.plist has the key.
  - Windows: `Directory.Build.props` has our import after expo-desktop's mods ran (spec mod-order check); `.vcxproj` and `.wapproj` each have one import.
  - Running the whole thing a second time changes no file (Review Focus 3).
  If the expo-desktop API needs more setup than the template copy (for example a `node_modules` layout), document it in the test and keep the test runnable on macOS and Windows CI hosts.
- [ ] **Step 2:** Run `pnpm install` then `pnpm --filter expo-modules-dotnet test -- expoDesktopPrebuild`. Expected: PASS (Tasks 2–4 already exist). If it fails, the failure is a real integration bug: fix it in the owning task's file.
- [ ] **Step 3:** Commit `test(expo-modules-dotnet): run mods through expo-desktop prebuild`.

### Task 6: Move `apps/desktop-app` to CNG on macOS

**Files:**
- Modify: `apps/desktop-app/package.json`, `app.json`, `metro.config.js`, `README.md`
- Create: `apps/desktop-app/.gitignore`, `apps/desktop-app/plugins/withDesktopAppFixes.js`
- Modify: `pnpm-workspace.yaml` (catalog `react-native-81`: `react-native-macos 0.81.9`, `react-native-windows 0.81.37`; `patchedDependencies`), `patches/` (port both patches to `@react-native-windows/cli@0.81.10` and `@react-native-windows/find-dotnet-tools@0.0.0-canary.3`), `pnpm-lock.yaml`
- Delete: `apps/desktop-app/macos/`, `apps/desktop-app/windows/`, `apps/desktop-app/Directory.Build.props`, `Directory.Build.targets`, `NuGet.config`

**Interfaces:**
- `app.json`: `name "Desktop App"`, `macos.bundleIdentifier "dev.expo.modules.dotnet.desktopapp"`, `windows.namespace "DesktopApp"`, fixed `windows.projectGuid` and `windows.packageGuid` (generate once, commit), `plugins: ["expo-modules-dotnet", "./plugins/withDesktopAppFixes"]` plus any expo-desktop plugin entry that prebuild writes on first run.
- `withDesktopAppFixes`: a `windows` dangerous mod that writes the app-root `Directory.Build.targets` (v143→v145 toolset override, content as today) and `NuGet.config` (content as today), each with a comment naming the failure it prevents. Other fixes are added only in Steps 4 and Task 7, with the same rule.
- Scripts: `macos`: `expo-desktop run macos`; `windows`: `expo-desktop run windows` (or `sync-windows && expo-desktop run windows`, see Task 7); `prebuild`: `expo-desktop prebuild`; `typecheck` unchanged. `autolink:windows`, `build:managed*`, `run:*` are removed unless Step 4 shows a need.

- [ ] **Step 1:** Update deps, catalog, patches; run `pnpm install`. Expected: exit 0, no "unused patch" error. Then `pnpm install --frozen-lockfile`. Expected: exit 0.
- [ ] **Step 2:** Update `app.json`, `.gitignore`, scripts; delete the committed trees and app-root MSBuild files.
- [ ] **Step 3:** Metro: replace `metro.config.js` with `makeMetroConfig` from `expo-desktop-metro-config`. Keep the hand-written config only if Step 4 fails to resolve `react-native-macos`; record the error in `README.md`.
- [ ] **Step 4:** Run from `apps/desktop-app`: `bunx expo-desktop prebuild --platform macos --clean`, then `bunx expo-desktop run macos`. Expected: Metro log `[ExampleModule] C# add(22, 20) returned 42`. For each build failure, add the smallest fix to `withDesktopAppFixes` (macOS mods) and note it under "Upstream gaps" in the roadmap 039 note draft. Re-run from `--clean` after each fix.
- [ ] **Step 5:** `git status` shows no tracked change under `apps/desktop-app` after the run. `pnpm --filter desktop-app typecheck` exits 0.
- [ ] **Step 6:** Commit `feat(desktop-app): generate macOS project with expo-desktop prebuild`.

### Task 7: Windows end to end

**Files:**
- Modify (only if needed): `apps/desktop-app/plugins/withDesktopAppFixes.js`, `apps/desktop-app/package.json`, `packages/expo-modules-dotnet/plugin/windows.js`, `packages/expo-modules-dotnet/windows/*.targets`

- [ ] **Step 1:** Ask the user to approve a force-push of the branch tip to `development` (`--force-with-lease`). Then message the Windows Remote Control session (see `ListAgents`): pull `development`, `pnpm install --frozen-lockfile`, in `apps/desktop-app` run `bunx expo-desktop prebuild --platform windows --clean` then `bunx expo-desktop run windows`, report the Metro log line and full errors.
- [ ] **Step 2:** Check whether `expo-desktop run windows`'s `autolink-windows` keeps the `Expo .NET Managed` folder in the `.sln`. If it removes it, set the `windows` script to `expo-modules-dotnet-autolinking sync-windows --sln <found sln> --proj <found vcxproj> && expo-desktop run windows` with paths that match the template (`windows/MyApp.sln`, `windows/MyApp/MyApp.vcxproj`), and record why in `README.md`.
- [ ] **Step 3:** Fix failures as in Task 6 Step 4; repeat Step 1 for each fix (each push needs approval).
- [ ] **Step 4:** Expected: Metro log `[ExampleModule] C# add(22, 20) returned 42` on Windows, and on Windows `git status` shows no tracked change under `apps/desktop-app`.
- [ ] **Step 5:** Commit `fix(...)` per fix, with the failure in the message.

### Task 8: Specs, docs, roadmap, closure

**Files:**
- Modify: `docs/specs/runtime-and-abi.md` (replace "Windows config-plugin resolution is deferred to expo-desktop" with the two scenarios from the spec delta)
- Modify: `docs/specs/dotnet-autolinking.md` (add "Desktop App Wiring Uses The Config Plugin" and "Config Plugin Options Are Validated"; modify "Windows Solution Projection")
- Modify: `apps/desktop-app/README.md` (prebuild and run steps, options, the app-local fixes and why)
- Modify: `docs/roadmap.md` (039 and 018 `DONE`; links to `archive/plans/`; 039 note with date, commit, macOS and Windows results, upstream gaps list)
- Move: `docs/plans/039-expo-desktop-prebuild.md`, `docs/plans/018-windows-build-deploy-reliability.md` → `docs/archive/plans/`
- Move: `docs/changes/2026-10-22-expo-desktop-prebuild/` → `docs/archive/changes/`

- [ ] **Step 1:** Edit the specs and docs as listed.
- [ ] **Step 2:** Run `scripts/test-managed.sh` (expected: pass, no skips), `pnpm --filter expo-modules-dotnet test`, `pnpm --filter expo-modules-dotnet-autolinking test`, `pnpm --filter desktop-app typecheck`, `scripts/format.sh --check --all`, `git diff --check`, and the living-spec `rg` check. Expected: all exit 0.
- [ ] **Step 3:** Scan the staged diff for local paths (`/Users/`, user name, machine name). Expected: none.
- [ ] **Step 4:** Commit `docs(desktop-app): close expo-desktop prebuild`.
- [ ] **Step 5:** Show the user the upstream gaps list and ask whether to report any of them.
