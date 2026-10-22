# 040: Let a Windows app opt in to `*-dotnet` modules in place of expo-desktop stubs

> **Deliver with `living-spec-workflow`**: brainstorm, delta spec, plan,
> implement, verify, merge into `docs/specs/`. Set this item's row in
> `docs/roadmap.md` to `IN PROGRESS` when you start and `DONE` when the
> slice closes, then move this brief to `docs/archive/plans/`.
>
> **Drift check (run first)**: `git diff --stat c7dccbe..HEAD -- packages/expo-modules-dotnet packages/expo-modules-dotnet-autolinking packages/expo-asset-dotnet packages/expo-constants-dotnet packages/expo-file-system-dotnet apps/desktop-app docs/specs/`
> If an in-scope file changed, compare "Current state" with the live code.
> A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH. It changes which module upstream Expo JS code reaches on
  Windows, before the app's first render.
- **Depends on**: 039. This item closes with asset and constants. 038 and
  025 add file system and crypto to the substitution set later.
- **Category**: direction
- **Planned at**: `c7dccbe`, 2026-10-22

## Why this matters

On Windows, expo-desktop gives an app almost no Expo modules. Its docs scope
page lists "All Expo SDK modules" as unsupported and says only "a
provisional, untested `expo-asset`" exists on Windows. Our `expo-asset-dotnet`,
`expo-constants-dotnet`, and `expo-file-system-dotnet` implement those native
modules, but upstream Expo JS code cannot reach them, because they register
only into `globalThis._expoDotnet.modules`. After this lands, a Windows app can
opt in, and upstream packages such as `expo-asset` and `expo-constants` then
call our .NET modules in place of the expo-desktop stubs. This is the
user-visible value the authored modules exist for.

## Current state

- expo-desktop on Windows, from `shirakaba/expo-desktop@1e45de7`:
  - `packages/expo-desktop-modules-core/windows/ExpoModulesCore/MainRuntimeInstaller.h`
    is a `REACT_EAGER_TURBO_MODULE` whose `REACT_INIT` runs before the bundle.
    It defines `global.expo` with `preparePropertyDescriptor(runtime, 1 << 1)`,
    which is enumerable only, so the property is not writable and not
    configurable. `global.expo.modules` is a plain `jsi::Object` with
    `ExpoAsset` (from `nativeModuleProxy`), `ExponentConstants` (an empty
    object), and a stub `NativeModulesProxy`.
  - `packages/expo-desktop-stubs/windows/ExpoDesktopStubs/ExpoAsset.h` is a
    C++/WinRT `downloadAsync(url, md5Hash, type)`.
  - The template `MyApp.cpp` calls
    `expo::windows::AddAttributedModules(packageBuilder, true)` and
    `expo::windows::EnableEagerTurboModules(settings)`.
- Upstream call sites (SDK 54 packages installed in this workspace):
  - `expo-asset@12.0.13` `build/ExpoAsset.js` is the only file that reaches
    the native `ExpoAsset`. It exports
    `downloadAsync(url, md5Hash, type)`. `packages/expo-asset-dotnet/src/index.ts`
    exports a function with the same name and signature.
  - `expo-constants@18.0.13` `build/ExponentConstants.js` is
    `export default requireOptionalNativeModule('ExponentConstants')`.
  - `expo-asset/build/PlatformUtils.js` and `expo-constants/build/Constants.js`
    also call `requireOptionalNativeModule('ExpoUpdates')` and
    `requireNativeModule('ExpoGo')`. Those are not ours.
- Our side:
  - `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.h:14`
    is a plain `REACT_MODULE`. It installs `_expoDotnet` when JS calls it,
    which is after expo-desktop's eager installer.
  - `packages/expo-modules-dotnet/src/index.ts` exposes
    `requireDotnetModule(name)` over `globalThis._expoDotnet.modules`.
  - `packages/expo-asset-dotnet/expo-module.config.json` has
    `"platforms": ["dotnet"]` and a `dotnet.projects` list. It has no
    substitution field.
  - `apps/desktop-app/metro.config.js` already has a per-platform
    `resolveRequest`, which maps `react-native` to `react-native-windows`.
    That is the exemplar for a Metro-level mechanism.
- Roadmap Direction, milestone 3, says registration into `globalThis.expo.modules`
  is opt-in per module, owned by autolinking policy, with `Expo.ModulesCore`
  kept generic.

## Constraints

- Windows only. On macOS, upstream `expo-modules-core` and the upstream Expo
  packages already work, so nothing in this item changes macOS, iOS, or
  Android resolution.
- Opt-in. Without the opt-in, a Windows app behaves exactly as today.
- `_expoDotnet` stays our own global. Authored modules never write to
  `globalThis.expo` themselves.
- Package names stay `expo-*-dotnet`. They exist next to the upstream
  packages; they do not replace them on npm.
- Autolinking owns the policy. `Expo.ModulesCore` and the ABI do not change.
  If a mechanism needs ABI surface, that is a STOP condition.
- Substitute only modules whose upstream native shape we implement in full.
  `expo-file-system` substitution waits for 038.

## Scope

**In scope**:

- The opt-in setting and where it lives.
- The substitution mechanism for `ExpoAsset` and `ExponentConstants`. A
  mechanism that later file-system and crypto entries can reuse without
  changes.
- `packages/expo-modules-dotnet-autolinking` and
  `packages/expo-modules-dotnet` changes that the mechanism needs.
- `apps/desktop-app` opt-in and verification on Windows.
- A living spec for the substitution contract.

**Out of scope**:

- macOS, iOS, and Android substitution.
- Replacing `MainRuntimeInstaller`, `NativeModulesProxy`, or the
  `EventEmitter` / `SharedObject` base classes that expo-desktop installs.
- `expo-file-system` and `expo-crypto` substitution. They join after 038 and
  025 ship the full upstream native shape.
- Changes to expo-desktop itself. Report gaps upstream instead.

## Open design questions

1. **Mechanism.**
   - **A, Metro file alias (recommended).** When `platform === 'windows'` and
     the app opts in, resolve the upstream native-accessor file to our
     package: `expo-asset/build/ExpoAsset.js` to `expo-asset-dotnet`, and
     `expo-constants/build/ExponentConstants.js` to an entry point in
     `expo-constants-dotnet` that default-exports the .NET
     `ExponentConstants` native module. This never touches `globalThis.expo`, needs no
     eager native install, and the evidence above shows the asset signatures
     already match. Risk: upstream can rename a `build/` file in a patch
     release. Deliver mode must pin the alias set per upstream version and
     fail the build when a target file is missing.
   - **B, eager registry entry.** Make our installer eager with expo-desktop's
     `REACT_EAGER_TURBO_MODULE` shim, install after their
     `MainRuntimeInstaller`, and overwrite entries in the plain
     `global.expo.modules` object. This covers every upstream call site, but
     it adds a native ordering dependency on expo-desktop's eager-module
     order and writes into a global we do not own.
   - Recommended: A. B stays the fallback if a needed module has call sites
     that a file alias cannot cover.
2. **Where the opt-in lives.** Recommended: the app's config (an
   `expo-modules-dotnet` config-plugin option or an `app.json` field) lists
   which upstream modules to substitute. The module's `expo-module.config.json`
   declares which upstream module and files it can stand in for. The app
   chooses, and the module describes.
3. **Startup order.** The `expo-desktop-stubs` README says the stubs exist so
   the app survives `import { registerRootComponent } from "expo"`, so upstream
   modules are probably reached before app code runs (not verified in this
   repo). Deliver mode must check that `_expoDotnet` is installed before the
   first substituted call. With A, the substituted file can install on first
   use through `requireDotnetModule`. Confirm this with a cold start on
   Windows.
4. **Spec home.** Recommended: a new section in the living spec that owns
   autolinking policy. Name the file in the delta spec.

## Done criteria

- [ ] `scripts/test-managed.sh` passes with no skips
- [ ] `pnpm --filter expo-modules-dotnet-autolinking test` and
      `pnpm --filter expo-modules-dotnet test` pass, with tests for the opt-in
      policy, the alias or registry set, and the "opt-in off" case
- [ ] `pnpm --filter desktop-app typecheck` exits 0
- [ ] On a Windows machine, with the opt-in on: `expo-asset` downloads an
      asset through `ExpoAssetDotnet`, and `Constants.expoConfig` from
      `expo-constants` is not null and comes from `ExpoConstantsDotnet`. With
      the opt-in off, the expo-desktop stubs run (`Constants.expoConfig` is
      null). Add the date, commit, and result to the roadmap notes, as the 024
      note does.
- [ ] A Metro resolver unit test shows that for `platform: 'macos'` and
      `'ios'` the upstream accessor files resolve unchanged with the opt-in on
- [ ] `scripts/format.sh --check --all` passes
- [ ] The substitution contract is in the living spec chosen in open
      question 4
- [ ] Roadmap row is `DONE`, and this brief is in `docs/archive/plans/`

## STOP conditions

- 039 is not `DONE`.
- The mechanism needs a write to `globalThis.expo` on macOS, or a new ABI
  surface.
- An upstream call site needs a native method that the `*-dotnet` module does
  not implement, and no partial-module policy covers it.
- The expo-desktop stubs cannot stay in place when the opt-in is off.

## Maintenance notes

- An SDK upgrade must re-check the alias targets (mechanism A) or the
  eager-module order (mechanism B).
- `expo-crypto-dotnet` (025) and file-system write and copy (038) join the
  substitution set when they ship.
- The four-platform demo app (Unplanned backlog) depends on this item.
