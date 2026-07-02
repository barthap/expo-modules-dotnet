# Windows Desktop Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a React Native Windows lane to `apps/desktop-app` that loads `example-module` through `expo-modules-dotnet` and proves `ExampleModule.add(20, 22)` returns `42`.

**Architecture:** Keep JS shared with the accepted macOS proof. Add RNW project files under `apps/desktop-app/windows`, add RNW package-provider glue under `packages/expo-modules-dotnet/windows`, and stage managed artifacts into an app-local Windows `Managed` directory. The Windows adapter obtains the active RNW JSI runtime, creates the existing `ReactNativeRuntimeConnector`, and calls the same managed registration ABI used by macOS.

**Tech Stack:** pnpm workspace, Expo Desktop, React Native 0.81, React Native Windows 0.81, C++/WinRT RNW native module, C++ JSI, HostFXR, .NET 10, MSBuild/Visual Studio 2026.

---

## File Structure

- Modify `apps/desktop-app/package.json`: add RNW dependency and Windows scripts.
- Modify `apps/desktop-app/metro.config.js`: map `react-native` to `react-native-windows` for the `windows` platform while preserving macOS mapping.
- Modify `apps/desktop-app/App.tsx`: make the label platform-neutral or platform-specific without changing the proof call.
- Create `apps/desktop-app/react-native.config.js`: point RNW CLI at the checked-in Windows solution when needed.
- Create `apps/desktop-app/scripts/build-managed.ps1`: Windows managed artifact staging for HostFXR and NativeAOT.
- Modify `apps/desktop-app/scripts/build-managed.sh`: keep macOS behavior intact and align script naming if necessary.
- Create `apps/desktop-app/windows/**`: checked-in RNW app project generated from RNW 0.81 C++ app template and then edited for this app.
- Create `apps/desktop-app/windows/Managed/.gitignore` and `.gitkeep`: app-owned managed staging directory.
- Modify `packages/expo-modules-dotnet/react-native.config.js`: add Windows platform metadata for RNW autolinking.
- Create `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/**`: RNW native module project, package provider, loader, and JSI installer.
- Reuse `packages/expo-modules-dotnet/native/include/expo_jsi.h` and `packages/expo-modules-dotnet/native/packages/jsi/**`: do not fork the ABI or reusable bridge.
- Modify `docs/specs/runtime-and-abi.md`, `docs/specs/runtime-scheduling.md`, and `docs/specs/modules-core-boundary.md` after implementation to merge accepted Windows behavior.
- Create or update `docs/archive/spike-results/windows-desktop-proof.md` or a similarly scoped durable proof note with commands and findings.

## Task 1: Prepare Workspace Dependencies

- [ ] **Step 1: Verify Node and pnpm from the active shell**

Run:

```powershell
node --version
pnpm --version
```

Expected: Node is modern enough for RNW 0.81 tooling and pnpm prints `11.7.0` or another pnpm 11 version. If the inherited shell still points at old Node, use the VS-bundled Node and pnpm executables explicitly for local commands without committing machine-specific paths.

- [ ] **Step 2: Add React Native Windows dependency**

Modify `apps/desktop-app/package.json` dependencies:

```json
"react-native-windows": "catalog:react-native-81"
```

Add scripts:

```json
"windows": "pnpm run:windows",
"run:windows": "react-native run-windows",
"build:managed:windows": "powershell -ExecutionPolicy Bypass -File ./scripts/build-managed.ps1"
```

Keep existing `macos`, `run:macos`, `build:managed`, and `typecheck` scripts.

- [ ] **Step 3: Install workspace dependencies**

Run:

```powershell
pnpm install --frozen-lockfile
```

Expected: lockfile already contains catalog-compatible RNW or updates are intentionally reviewed if the frozen install fails because `react-native-windows` was not previously materialized for `desktop-app`.

If the frozen install fails only because the lockfile does not yet include the
new dependency edge, run:

```powershell
pnpm install --lockfile-only
pnpm install --frozen-lockfile
```

Expected: the first command updates only workspace dependency metadata and the
second command proves the lockfile is reproducible.

- [ ] **Step 4: Commit dependency/script slice**

Run:

```powershell
git add apps/desktop-app/package.json pnpm-lock.yaml
git diff --cached --check
git commit -m "chore: add desktop windows dependency"
```

If `pnpm-lock.yaml` is unchanged, do not stage it.

## Task 2: Generate And Normalize The RNW App Project

- [ ] **Step 1: Generate RNW project into `apps/desktop-app/windows`**

From `apps/desktop-app`, run the RNW init command compatible with RNW 0.81:

```powershell
pnpm exec react-native init-windows --overwrite --template cpp-app
```

Expected: `apps/desktop-app/windows` contains a C++ RNW app solution and project.

- [ ] **Step 2: If CLI generation does not accept the template flag, use package config**

Add or verify this package config in `apps/desktop-app/package.json`:

```json
"react-native-windows": {
  "init-windows": {
    "name": "desktopapp",
    "namespace": "desktopapp",
    "template": "cpp-app"
  }
}
```

Then rerun:

```powershell
pnpm exec react-native init-windows --overwrite
```

- [ ] **Step 3: Normalize app identity**

Inspect generated app project files and set:

```cpp
viewOptions.ComponentName(L"desktopapp");
appWindow.Title(L"Desktop App");
```

Expected: the component name matches `index.ts` registration.

- [ ] **Step 4: Preserve Metro config**

If RNW generation overwrites `apps/desktop-app/metro.config.js`, restore the Expo Desktop config and add Windows resolution:

```js
if (
  platform === 'windows' &&
  (moduleName === 'react-native' || moduleName.startsWith('react-native/'))
) {
  const newModuleName = moduleName.replace('react-native', 'react-native-windows');
  return context.resolveRequest(context, newModuleName, platform);
}
```

Keep the existing macOS `react-native-macos` branch.

- [ ] **Step 5: Add managed staging placeholders**

Create:

```text
apps/desktop-app/windows/Managed/.gitignore
apps/desktop-app/windows/Managed/.gitkeep
```

`.gitignore` content:

```gitignore
*
!.gitignore
!.gitkeep
```

- [ ] **Step 6: Commit RNW project slice**

Run:

```powershell
git add apps/desktop-app/windows apps/desktop-app/metro.config.js apps/desktop-app/package.json
git diff --cached --check
git commit -m "feat: add desktop windows project"
```

## Task 3: Add Windows Managed Artifact Staging

- [ ] **Step 1: Write Windows staging script**

Create `apps/desktop-app/scripts/build-managed.ps1` with these responsibilities:

- Resolve repo root from the script location.
- Read `$env:EXPO_DOTNET_LOADER`, then `$env:EXPO_JSI_DOTNET_LOADER`, defaulting to `hostfxr`.
- Use Debug by default for HostFXR and Release by default for NativeAOT unless `$env:CONFIGURATION` is set.
- Build the generator analyzer before NativeAOT publish.
- For HostFXR, run `dotnet build` for `packages/example-module/dotnet/ExampleModule/ExampleModule.csproj`, clean `apps/desktop-app/windows/Managed` except placeholders, copy `*.dll`, `*.deps.json`, and `*.runtimeconfig.json` from the `net10.0` output, and copy `nethost.dll` plus `nethost.lib` or document why only the runtime DLL is staged.
- For NativeAOT, run `dotnet publish -r win-x64 /p:PublishAot=true /p:NativeLib=Shared` and copy the produced `ExampleModule.dll` native library plus any required import/runtime files.

- [ ] **Step 2: Test HostFXR staging**

Run:

```powershell
pnpm --filter desktop-app build:managed:windows
```

Expected: `apps/desktop-app/windows/Managed` contains `ExampleModule.dll`, `ExampleModule.runtimeconfig.json`, `ExampleModule.deps.json`, managed bridge assemblies, and `nethost.dll`.

- [ ] **Step 3: Commit staging slice**

Run:

```powershell
git add apps/desktop-app/scripts/build-managed.ps1 apps/desktop-app/windows/Managed/.gitignore apps/desktop-app/windows/Managed/.gitkeep
git diff --cached --check
git commit -m "feat: stage desktop windows managed artifacts"
```

## Task 4: Add RNW Adapter Project For `expo-modules-dotnet`

- [ ] **Step 1: Add RNW config metadata**

Modify `packages/expo-modules-dotnet/react-native.config.js`:

```js
windows: {
  sourceDir: './windows',
  solutionFile: 'ExpoModulesDotnet.sln',
  projects: [
    {
      projectFile: 'ExpoModulesDotnet/ExpoModulesDotnet.vcxproj',
      directDependency: true,
    },
  ],
},
```

Keep existing Android and iOS entries.

- [ ] **Step 2: Create Windows native module project**

Create a C++/WinRT RNW native module project under:

```text
packages/expo-modules-dotnet/windows/ExpoModulesDotnet
```

The project SHALL compile these sources:

```text
ReactPackageProvider.cpp
ReactPackageProvider.h
ReactPackageProvider.idl
ExpoModulesDotnetInstaller.cpp
ExpoModulesDotnetInstaller.h
ManagedLoader.cpp
ManagedLoader.h
ManagedHostFxr.h
pch.cpp
pch.h
targetver.h
```

It SHALL include the reusable bridge sources by project reference or file include:

```text
packages/expo-modules-dotnet/native/packages/jsi/src/ExpoJsiBridge.cpp
packages/expo-modules-dotnet/native/packages/jsi/src/ReactNativeRuntimeConnector.cpp
```

It SHALL include headers from:

```text
packages/expo-modules-dotnet/native/include
packages/expo-modules-dotnet/native/packages/jsi/include
```

- [ ] **Step 3: Implement package provider**

`ReactPackageProvider.cpp` SHALL call:

```cpp
AddAttributedModules(packageBuilder, true);
```

This exposes the attributed installer module to RNW autolinking.

- [ ] **Step 4: Implement installer module**

`ExpoModulesDotnetInstaller` SHALL be an RNW attributed module named
`ExpoModulesDotnetInstaller` with:

```cpp
REACT_INIT(Initialize)
void Initialize(React::ReactContext const &reactContext) noexcept;

REACT_SYNC_METHOD(installModules)
bool installModules() noexcept;
```

`Initialize` SHALL capture `ReactContext`, create or reset install state, and
schedule JSI registration work through `reactContext.CallInvoker()->invokeAsync`.

The scheduled lambda SHALL create a `ReactNativeRuntimeConnector`, create an
opaque runtime handle, resolve the selected managed registration function, call
it with `reactNativeExpoJsiApi()` and the runtime handle, and keep the connector
and runtime handle alive for later direct host-function calls.

`installModules` SHALL return true only after registration has succeeded. If
JS calls it before async initialization finishes, return false and log the
reason; do not fake module installation.

- [ ] **Step 5: Implement Windows loader**

`ManagedLoader.cpp` SHALL mirror the macOS loader with Windows APIs:

- Use `LoadLibraryW` and `GetProcAddress`.
- Resolve `get_hostfxr_path` from staged `nethost.dll`.
- Resolve `hostfxr_initialize_for_runtime_config`,
  `hostfxr_get_runtime_delegate`, and `hostfxr_close`.
- Use wide-character HostFXR signatures.
- Resolve `ExampleModule.EntryPoints, ExampleModule` method `RegisterModules`
  with `UNMANAGEDCALLERSONLY_METHOD`.
- Resolve NativeAOT export `example_module_register_modules` from the staged
  native library when `nativeaot` is selected.

- [ ] **Step 6: Commit adapter slice**

Run:

```powershell
git add packages/expo-modules-dotnet/react-native.config.js packages/expo-modules-dotnet/windows
git diff --cached --check
git commit -m "feat: add windows dotnet adapter"
```

## Task 5: Wire The App To Autolink And Package Managed Artifacts

- [ ] **Step 1: Run RNW autolink for desktop app**

From `apps/desktop-app`, run:

```powershell
pnpm exec react-native autolink-windows
```

Expected: generated app autolink files include `expo-modules-dotnet`,
`expo-desktop-modules-core`, and `expo-desktop-stubs` package providers.

- [ ] **Step 2: Add app project build target for managed artifacts**

Modify the Windows app project so a pre-build target runs:

```powershell
powershell -ExecutionPolicy Bypass -File "$(ProjectDir)..\scripts\build-managed.ps1"
```

The target SHALL pass `EXPO_DOTNET_LOADER` from an MSBuild property defaulting
to `hostfxr`. It SHALL avoid absolute local paths.

- [ ] **Step 3: Copy managed artifacts next to the app binary**

Modify the Windows app project so files under `windows/Managed` are copied to a
`Managed` directory beside the built executable or packaged output used by the
debug run.

- [ ] **Step 4: Commit app wiring slice**

Run:

```powershell
git add apps/desktop-app/windows packages/expo-modules-dotnet/windows
git diff --cached --check
git commit -m "feat: wire desktop windows dotnet proof"
```

## Task 6: Verify The Proof And Record Evidence

- [ ] **Step 1: Typecheck desktop app**

Run:

```powershell
pnpm --filter desktop-app typecheck
```

Expected: TypeScript succeeds without changing mobile app dependencies.

- [ ] **Step 2: Build or run Windows app**

Run:

```powershell
pnpm --filter desktop-app windows
```

Expected: RNW builds and starts the Windows app. The UI or logs show
`C# add result: 42` or `[ExampleModule] C# add(20, 22) returned 42`.

If packaging fails before bridge execution, record it as a Windows toolchain or
packaging blocker with the failing command and error. If RNW cannot provide a
direct runtime install path, record that as the P0 architecture finding.

- [ ] **Step 3: Run managed suite**

Run:

```powershell
scripts/test-managed.sh
```

Expected: existing Hermes-backed managed tests pass.

- [ ] **Step 4: Run formatting**

Run:

```powershell
scripts/format.sh --check --all
```

If it reports format drift, run:

```powershell
scripts/format.sh
scripts/format.sh --check --all
```

- [ ] **Step 5: Run diff hygiene**

Run:

```powershell
git diff --check
rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\\?\\[\\]|JsonSerializer" packages/example-module packages/expo-modules-dotnet/managed
```

Expected: no whitespace errors, and no new hot-path reflection/dynamic/JSON
usage for generated bindings.

- [ ] **Step 6: Record proof evidence**

Create `docs/archive/spike-results/windows-desktop-proof.md` with:

```markdown
# Windows Desktop Proof

## Hypothesis

React Native Windows 0.81 can host the existing `expo-modules-dotnet` JSI ABI
and register `example-module` so `ExampleModule.add(20, 22)` executes as a
direct JSI host function and returns `42`.

## Commands Run

- `pnpm install --frozen-lockfile`: record exit code and important output.
- `pnpm --filter desktop-app typecheck`: record exit code and important output.
- `pnpm --filter desktop-app windows`: record exit code, app launch status, and
  whether `42` appeared.
- `scripts/test-managed.sh`: record exit code and important output.
- `scripts/format.sh --check --all`: record exit code and important output.
- `git diff --check`: record exit code and important output.

## Expected Result

The Windows desktop app displays or logs `42` from the C# example module.

## Actual Result

Record the observed Windows app behavior. If the app did not reach JS module
execution, name the earliest failing layer: dependency install, RNW generation,
MSBuild, packaging/deploy, runtime install, HostFXR resolution, managed
registration, or JS call.

## Artifacts

- `apps/desktop-app/windows`
- `apps/desktop-app/windows/Managed`
- `packages/expo-modules-dotnet/windows`

## Ownership/Lifetime Findings

Record which native object owns the `ReactNativeRuntimeConnector`, when the
borrowed RNW runtime handle is invalidated, and whether reload teardown remains
unresolved.

## Scheduler Findings

Record whether RNW exposes sync scheduling through the call invoker and whether
`ExampleModule.add(20, 22)` required any sync scheduling support. The expected
proof result is that the generated sync function runs directly as a JSI host
function during the JavaScript call.

## Stop/Go Decision

State whether the RNW proof is sufficient evidence for the next lifecycle
contract slice, or whether a named RNW/toolchain blocker must be resolved first.
```

- [ ] **Step 7: Commit evidence and verified implementation**

Run:

```powershell
git add apps packages docs/archive/spike-results/windows-desktop-proof.md
git diff --cached --check
git commit -m "test: verify windows desktop proof"
```

## Task 7: Merge Delta Into Living Specs And Clean Transient Artifacts

- [ ] **Step 1: Update living specs**

Merge accepted implemented behavior into:

```text
docs/specs/runtime-and-abi.md
docs/specs/runtime-scheduling.md
docs/specs/modules-core-boundary.md
docs/roadmap.md
```

Keep requirements current-state only. If the implementation diverged from this
spec, describe the verified behavior rather than the original hope.

- [ ] **Step 2: Remove transient change artifacts**

After living specs and proof evidence are durable, remove:

```text
docs/changes/2026-07-02-windows-desktop-proof/spec.md
docs/changes/2026-07-02-windows-desktop-proof/plan.md
```

Remove the directory if empty.

- [ ] **Step 3: Final verification**

Run:

```powershell
pnpm --filter desktop-app typecheck
scripts/test-managed.sh
scripts/format.sh --check --all
git diff --check
```

Run or report the latest Windows app build/run result.

- [ ] **Step 4: Commit living-spec sync**

Run:

```powershell
git add docs
git diff --cached --check
git commit -m "docs: record windows desktop proof"
```
