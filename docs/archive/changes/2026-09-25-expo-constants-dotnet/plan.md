# Expo Constants Dotnet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Windows/macOS `expo-constants-dotnet` package with six typed, read-only values whose native version strings come from the embedding app.

**Architecture:** The Windows and macOS adapters read their own app metadata and pass it through one size-extensible host-context struct in the four-argument v3 create call. The generated host validates and copies the available strings into `DotnetRuntimeContext`; a generated authored module projects them with OS and runtime-scoped values. The package facade resolves only `_expoDotnet.modules.ExponentConstants`.

**Tech Stack:** C++/Objective-C++ native adapters, C ABI, .NET 10, Expo.ModulesCore Roslyn generator, TypeScript, Vitest, xUnit v3, Hermes testhost.

**Spec:** `docs/changes/2026-09-25-expo-constants-dotnet/spec.md`

## Global Constraints

- The package supports Windows and macOS, with native name `ExponentConstants`, assembly `ExpoConstantsDotnet`, and exactly six getter-only JavaScript properties.
- `nativeAppVersion` and `nativeBuildVersion` are `string | null`; `expoVersion` is always `null`; `expoConfig` is absent.
- macOS reads `CFBundleShortVersionString` and `CFBundleVersion` from the main bundle. Windows reads the four-part `Package::Current().Id().Version()` as `nativeBuildVersion` and supplies no `nativeAppVersion`.
- `sessionId` is one canonical GUID per runtime context that instantiates the module, with no persistence.
- The ABI carries native app versions as host identity only. Platform detection, GUID generation, and the fixed `bare` value stay in .NET.
- All create-call strings are borrowed UTF-8 pointer/length pairs. The generated host copies them before returning and rejects malformed input through `RuntimeContextResult`.
- Keep the directory fields at their existing offsets in one `expo_dotnet_host_context` struct. Append optional version pairs, accept complete known prefixes and unknown trailing bytes, and reject sizes ending inside a known pair. Keep struct version 1 for compatible additions.
- Keep the create call at four arguments. Rename the create symbol and HostFXR method to v3; do not export v2 from the final generated host.
- Do not add Expo global registration, Metro aliases, general JSON codecs, mobile support, or machine-local paths to committed files.
- Before every commit, scan staged content for local absolute paths, usernames, machine names, and private hostnames.

## Review Focus

1. A host supplies an empty, whitespace, or NUL-bearing version string: startup reports malformed metadata, not a fabricated version (Tasks 1 and 2 tests).
2. Windows has no package identity: both native versions are null and app registration still succeeds (Task 3 Windows proof).
3. One metadata field is missing while the other is present: the present value survives independently (Tasks 1 and 2 tests).
4. A host sends a truncated known prefix, invalid UTF-8, NUL, or pointer/length mismatch: the unmanaged create call returns a releasable structured error before registration (Task 2 harness).
5. React Native replaces its runtime: the next context gets a different session ID while the old one stays stable until teardown (Task 4 Hermes tests).

---

## File map

- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/HostAppMetadata.cs`: immutable version pair and validation.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`: constructor input and active-state checked accessor.
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`: explicit metadata input for Hermes tests.
- `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`: extensible host-context layout and v3 function pointer.
- `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`: v3 export, size-aware host-context decoder, and context construction.
- `packages/expo-modules-dotnet-autolinking/src/__tests__/fixtures/entry-points-abi-harness.cs`: executable ABI and malformed-input checks.
- `packages/expo-modules-dotnet/{macos,windows,ios,android}/...`: v3 loader use; desktop adapters resolve metadata, mobile adapters pass null.
- `packages/expo-constants-dotnet/`: C# module/tests, TypeScript facade/tests, package metadata, and autolinking declaration.
- `apps/desktop-app/{App.tsx,package.json}` and `pnpm-lock.yaml`: consume the package in the real desktop example and show the resolved fields.
- `docs/{README.md,roadmap.md,plans/README.md,specs/*}`: close the accepted behavior and index the package.

### Task 1: Add immutable host metadata to the runtime context

**Files:**
- Create: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/HostAppMetadata.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`
- Modify: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs`
- Test: `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`

**Interfaces:**
- Produces: `HostAppMetadata(string? nativeAppVersion = null, string? nativeBuildVersion = null)`, `HostAppMetadata.Unconfigured`, `DotnetRuntimeContext(JavaScriptRuntime, AppDirectories, HostAppMetadata)`, `DotnetRuntimeContext.AppMetadata`, and `ExpoModuleTestHost.Create(AppDirectories, HostAppMetadata, Action<DotnetRuntimeContext, JavaScriptObject>)`.
- Existing one- and two-argument context constructors continue to select `HostAppMetadata.Unconfigured`.

- [ ] **Step 1: Write failing model and context tests.** Assert null defaults, independent fields, values kept verbatim, empty/whitespace/NUL rejection, constructor null rejection, and `ObjectDisposedException` before metadata access after teardown. Use the existing `DotnetRuntimeContextTests` fixture and a new `ExpoModuleTestHostTests` case that observes metadata during registration.

```csharp
var metadata = new HostAppMetadata("1.0", null);
Assert.Equal("1.0", metadata.NativeAppVersion);
Assert.Null(metadata.NativeBuildVersion);
Assert.Throws<ArgumentException>(() => new HostAppMetadata(" ", null));
Assert.Throws<ArgumentException>(() => new HostAppMetadata(null, "1\0x"));
```

- [ ] **Step 2: Run the focused managed tests and confirm the new types fail to compile.**

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
```

- [ ] **Step 3: Implement the model and overloads.** Validate only supplied strings; do not read platform APIs or files. Preserve the existing context lock and active-state check in the new accessor. Delegate old constructors and test-host overloads to the new three-input path.

```csharp
public HostAppMetadata AppMetadata
{
  get
  {
    lock (gate)
    {
      ThrowIfNotActiveLocked();
      return appMetadata;
    }
  }
}
```

- [ ] **Step 4: Run the focused suite green, then stage and commit this slice.**

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
git diff --check
git add packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests
git diff --cached --check
git commit -m 'feat(runtime): carry host app metadata per context'
```

Before the commit command, inspect the staged diff for local absolute paths, real usernames, machine names, and private hostnames; a clean whitespace check alone is not enough.

### Task 2: Move runtime creation to the v3 ABI

**Files:**
- Modify: `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`
- Modify: `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`
- Modify: `packages/expo-modules-dotnet-autolinking/src/__tests__/fixtures/entry-points-abi-harness.cs`
- Modify: `packages/expo-modules-dotnet/macos/ManagedLoader.h`
- Modify: `packages/expo-modules-dotnet/macos/ManagedLoader.mm`
- Modify: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.cpp`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
- Modify: `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`

**Interfaces:**
- Consumes: `HostAppMetadata` and the new context constructor from Task 1.
- Produces: `expo_dotnet_host_context` and `CreateRuntimeContextV3Fn(api, runtime, hostContext, result)`. The managed HostFXR method is `CreateRuntimeContextResultV3`; the NativeAOT export is `expo_dotnet_create_runtime_context_result_v3`.

- [ ] **Step 1: Update tests and the executable harness first.** Assert four create parameters, v3-only symbol and method resolution, host-context decode before context construction, and mirror offsets for the unchanged directory prefix: `0, 4, 8, 8 + pointerSize, 24/16, 32/20`. Assert the complete prefix sizes `40/24`, `56/32`, and `72/40`. Run the harness through a four-parameter unmanaged function pointer. Test null context; directory-only, one-version, full, and future-extended sizes; each field alone; non-ASCII UTF-8; undersized, partial-tail, and wrong-version structs; negative length; null/nonzero and non-null/zero pairs; invalid UTF-8; NUL; and whitespace.

```csharp
var create =
    (delegate* unmanaged[Cdecl]<nint, nint, nint, RuntimeContextResult*, void>)
        &CreateRuntimeContextResultV3;
RuntimeContextResult result = default;
create(0, 0, hostContextPointer, &result);
// DecodeHostContext runs before JavaScriptRuntime.FromNative, so malformed
// input reports the host-context failure. Release result.Error exactly once.
```

- [ ] **Step 2: Run the autolinking tests red.**

```sh
pnpm --filter expo-modules-dotnet-autolinking test
```

- [ ] **Step 3: Add the shared native struct and generated decoder.** Replace the old directories struct with `expo_dotnet_host_context`, preserving the original fields and offsets, then append the two version pairs. Give the one struct a size and version header, strict UTF-8 decoding, and null-pointer meaning both directories and metadata are unconfigured. Validate the 40/24-byte directory prefix before version and payload fields; read each appended pair only when its full 56/32- or 72/40-byte prefix is present. Reject partial known pairs; accept and ignore bytes past the full known prefix. Use the existing `RuntimeContextResult` error/release path. Keep all mirrors private and C# generated code NativeAOT-safe.

```csharp
var (directories, metadata) = DecodeHostContext(hostContext);
var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
var context = new DotnetRuntimeContext(runtime, directories, metadata);
```

- [ ] **Step 4: Switch all four adapters to v3 in the same task.** Update the desktop HostFXR method strings and NativeAOT symbol strings. The desktop adapters fill the existing directory prefix and leave both version pointers null until Task 3 fills them. Update iOS and Android to pass one null host-context pointer. Remove the v2 create typedef, generated export, and loader lookups from the final source state of this task.

```cpp
entryPoints.createRuntimeContextV3(
  expo::dotnet::reactNativeExpoJsiApi(), runtimeHandle, hostContextPointer,
  &result);
```

- [ ] **Step 5: Run autolinking tests and scans green, then commit.** Generate the host for both desktop and mobile app roots so stale HostFXR method names or NativeAOT exports are visible. Compile the shared header directly on the current C++ toolchain.

```sh
pnpm --filter expo-modules-dotnet-autolinking test
pnpm --filter expo-modules-dotnet-autolinking typecheck
pnpm --dir apps/desktop-app exec expo-modules-dotnet-autolinking generate
pnpm --dir apps/mobile-app exec expo-modules-dotnet-autolinking generate
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
c++ -std=c++20 -fsyntax-only -Ipackages/expo-modules-dotnet/native/include -x c++ packages/expo-modules-dotnet/native/include/expo_dotnet_host.h
rg -n 'expo_dotnet_create_runtime_context_result_v2|CreateRuntimeContextResultV2|CreateRuntimeContextV2Fn|createRuntimeContextV2' packages/expo-modules-dotnet packages/expo-modules-dotnet-autolinking/src/codegen
git diff --check
```

The old-name scan expects no source matches. Stage only Task 2 paths, run the staged privacy scan, and commit `feat(runtime): accept host app metadata through v3 create ABI`.

### Task 3: Resolve native versions in desktop hosts

**Files:**
- Modify: `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- Modify: `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`
- Test: `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts` and its ABI harness for field independence and malformed metadata.

**Interfaces:**
- Consumes: `expo_dotnet_host_context` and `createRuntimeContextV3` from Task 2.
- Produces: borrowed native app/build version strings scoped to the create call; no new public API.

- [ ] **Step 1: Add a host-boundary test for both versions and one missing version.** The generated-host harness passes `("1.0", "1")`, `(null, "1.0.0.0")`, and `(null, null)` into `DecodeHostContext` and checks exact results. Assert the metadata is already copied before the borrowed buffers leave scope.

```csharp
WithHostContext("1.0", "1", pointer =>
{
  var (_, decoded) = DecodeHostContext(pointer);
  CheckEqual("1.0", decoded.NativeAppVersion, "app version");
  CheckEqual("1", decoded.NativeBuildVersion, "build version");
});
```

- [ ] **Step 2: Resolve macOS main-bundle values.** Read `CFBundleShortVersionString` and `CFBundleVersion` as `NSString`; nil means unavailable, a present non-string value produces a clear registration error. Convert each present value to UTF-8 in the same `registerModules` frame that calls v3, keep buffers alive through that call, and do not log their contents.

```objc
NSDictionary *info = [[NSBundle mainBundle] infoDictionary];
id appVersion = info[@"CFBundleShortVersionString"];
id buildVersion = info[@"CFBundleVersion"];
// A present value that is not NSString fails before calling the entry point.
```

- [ ] **Step 3: Resolve Windows package identity.** Read `winrt::Windows::ApplicationModel::Package::Current().Id().Version()`, format `Major.Minor.Build.Revision` using decimal components, and pass it only as `nativeBuildVersion`. Catch the specific no-package-identity failure and pass both fields unavailable; surface other failures as registration errors. Do not use assembly/file version or app config.

```cpp
const auto version = winrt::Windows::ApplicationModel::Package::Current().Id().Version();
const std::string buildVersion = std::to_string(version.Major) + "." +
  std::to_string(version.Minor) + "." +
  std::to_string(version.Build) + "." + std::to_string(version.Revision);
```

- [ ] **Step 4: Verify locally available adapter work.** Run `pnpm --filter expo-modules-dotnet-autolinking test` for the ABI fixture and build the desktop app on macOS with `pnpm --dir apps/desktop-app run:macos`. Keep borrowed buffers alive through the create call and check that the Windows code uses the same shared struct, but leave Windows build and runtime proof for Task 6. Do not commit raw local bundle paths or package IDs.

- [ ] **Step 5: Stage only the desktop adapter and ABI-test changes, scan, and commit** `feat(runtime): supply desktop native app versions` after the local tests and macOS build pass. Record Windows verification as pending, not passing.

### Task 4: Implement the authored C# constants module

**Files:**
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet/ExpoConstantsDotnet.csproj`
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet/ExpoConstantsModule.cs`
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet/AssemblyInfo.cs`
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/ExpoConstantsDotnet.Tests.csproj`
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/ExpoConstantsModuleTests.cs`
- Create: `packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/AssemblyInfo.cs`

**Interfaces:**
- Consumes: `DotnetRuntimeContext.AppMetadata` from Task 1 and generated `[JS]` getter support.
- Produces: `[ExpoModule("ExponentConstants")] public sealed partial class ExpoConstantsModule`, with public getter-only `Platform`, `ExecutionEnvironment`, `SessionId`, `NativeAppVersion`, `NativeBuildVersion`, and `ExpoVersion` properties. Tests may call an internal `RequireSupportedPlatform(bool isWindows, bool isMacOS)` helper through `InternalsVisibleTo`.

- [ ] **Step 1: Create the two csproj files and write pure and Hermes-backed failing tests.** Use the asset package's generator and xUnit project references so the test runner can discover this project. Pure tests check Windows/macOS selection and unsupported platforms; Hermes tests check the exact lower-camel own-property names, null values, strict-mode assignment failure, and two contexts with different GUIDs. Set app/build versions through `ExpoModuleTestHost.Create(AppDirectories.Unconfigured, metadata, provider)` and assert they survive exactly.

```csharp
Assert.Equal("windows", ExpoConstantsModule.RequireSupportedPlatform(true, false));
Assert.Throws<PlatformNotSupportedException>(() =>
    ExpoConstantsModule.RequireSupportedPlatform(false, false));
Assert.Equal(
    "executionEnvironment,expoVersion,nativeAppVersion,nativeBuildVersion,platform,sessionId",
    names
);
```

- [ ] **Step 2: Run the package project red.**

```sh
scripts/test-managed.sh --project packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/ExpoConstantsDotnet.Tests.csproj
```

- [ ] **Step 3: Implement the module using direct typed getters.** Follow the asset package csproj/generator wiring. Detect OS with `OperatingSystem.IsWindows()` and `OperatingSystem.IsMacOS()`, generate `Guid.NewGuid().ToString("D")` once in the constructor, and capture `context.AppMetadata`. Return `"bare"` and `null` for the fixed fields. No provider interface, persistence, process introspection, or reflection is needed.

```csharp
[JS] public string Platform { get; }
[JS] public string ExecutionEnvironment => "bare";
[JS] public string SessionId { get; }
[JS] public string? NativeAppVersion => metadata.NativeAppVersion;
[JS] public string? NativeBuildVersion => metadata.NativeBuildVersion;
[JS] public string? ExpoVersion => null;
```

- [ ] **Step 4: Run focused tests green, check generated diagnostics and reflection scan, then commit.**

```sh
scripts/test-managed.sh --project packages/expo-constants-dotnet/dotnet/ExpoConstantsDotnet.Tests/ExpoConstantsDotnet.Tests.csproj
rg 'Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer' packages/expo-constants-dotnet/dotnet packages/expo-modules-dotnet/managed/packages
git diff --check
```

Stage only the new C# package files, scan staged content, and commit `feat(constants): expose typed runtime constants`.

### Task 5: Add the TypeScript facade and desktop consumer

**Files:**
- Create: `packages/expo-constants-dotnet/package.json`
- Create: `packages/expo-constants-dotnet/expo-module.config.json`
- Create: `packages/expo-constants-dotnet/tsconfig.json`
- Create: `packages/expo-constants-dotnet/vitest.config.ts`
- Create: `packages/expo-constants-dotnet/src/index.ts`
- Create: `packages/expo-constants-dotnet/src/__tests__/index.test.ts`
- Create: `packages/expo-constants-dotnet/src/__tests__/autolinking.test.ts`
- Modify: `apps/desktop-app/package.json`
- Modify: `apps/desktop-app/App.tsx`
- Modify: `pnpm-lock.yaml`

**Interfaces:**
- Consumes: generated native name `ExponentConstants` and its six lower-camel getters from Task 4.
- Produces: default-exported read-only constants facade and exported `ExpoConstants` type; the desktop example presents native values for real host verification.

- [ ] **Step 1: Create minimal package/test config and write failing Vitest cases.** Add `package.json` with the `test` and `typecheck` scripts and add `vitest.config.ts` so the package filter can run. Mock `requireDotnetModule` and assert one call with `ExponentConstants`, exact property forwarding, and no access to `globalThis.expo`. Check `expo-module.config.json` resolves to the managed csproj and its assembly name. Include a compile-time type check by running package `typecheck` after implementation.

```ts
const facade = await import('../index');
expect(mockRequireDotnetModule).toHaveBeenCalledWith('ExponentConstants');
expect(facade.default.nativeBuildVersion).toBe('1');
expect((globalThis as { expo?: unknown }).expo).toBeUndefined();
```

- [ ] **Step 2: Run package Vitest red.**

```sh
pnpm --filter expo-constants-dotnet test
```

- [ ] **Step 3: Add the private workspace package and facade.** Read the repo's React Native best practices and Expo API docs skills before editing `App.tsx` or the public TypeScript API. Follow `expo-asset-dotnet` package/test config, declare `dotnet.projects` for `ExpoConstantsDotnet.csproj`, and use `DotnetModule` plus `requireDotnetModule`. Document and export a TypeScript interface with readonly fields and exact literal types for `platform`, `executionEnvironment`, and `expoVersion`.

```ts
export interface ExpoConstants {
  readonly platform: 'windows' | 'macos';
  readonly executionEnvironment: 'bare';
  readonly sessionId: string;
  readonly nativeAppVersion: string | null;
  readonly nativeBuildVersion: string | null;
  readonly expoVersion: null;
}
```

- [ ] **Step 4: Consume it in the desktop example.** Add `"expo-constants-dotnet": "workspace:*"` to `apps/desktop-app/package.json` and one `Constants` display row in `App.tsx` that shows platform, session ID, and both native versions (show `null` explicitly). Keep the existing example-module showcase intact. Run a lockfile update, then a frozen install.

```sh
pnpm install --lockfile-only
pnpm install --frozen-lockfile
pnpm --filter expo-constants-dotnet test
pnpm --filter expo-constants-dotnet typecheck
pnpm --dir apps/desktop-app typecheck
```

- [ ] **Step 5: Stage package/desktop/lockfile files, scan, and commit** `feat(constants): add facade and desktop showcase`.

### Task 6: Verify the full slice and close the living specs

**Files:**
- Modify: `docs/specs/modules-core-boundary.md`
- Modify: `docs/specs/runtime-and-abi.md`
- Modify: `docs/specs/dotnet-autolinking.md`
- Modify: `docs/specs/README.md`
- Modify: `docs/README.md`
- Modify: `docs/roadmap.md`
- Modify: `docs/plans/README.md`
- Modify: `docs/plans/023-expo-constants-dotnet.md`
- Move: `docs/changes/2026-09-25-expo-constants-dotnet/{spec.md,plan.md}` to `docs/archive/changes/2026-09-25-expo-constants-dotnet/` after accepted behavior is merged.

**Interfaces:**
- Consumes: all prior task outputs.
- Produces: current-state specs and one completed roadmap entry; no code API.

- [ ] **Step 1: Run the required local gates on the completed tree.**

```sh
scripts/test-managed.sh
pnpm --filter expo-constants-dotnet test
pnpm --filter expo-constants-dotnet typecheck
pnpm --filter expo-modules-dotnet-autolinking test
pnpm --filter expo-modules-dotnet-autolinking typecheck
pnpm --dir apps/desktop-app typecheck
scripts/format.sh --check --all
git diff --check
```

If formatting changes are needed, run `scripts/format.sh` and repeat the check. No skipped suite counts as a pass.

- [ ] **Step 2: Prove the generated host and desktop adapters in both loader modes.** On macOS run `EXPO_DOTNET_LOADER=hostfxr pnpm --dir apps/desktop-app run:macos` and `EXPO_DOTNET_LOADER=nativeaot pnpm --dir apps/desktop-app run:macos`; the example row must show `macos`, `1.0`, and `1`. On Windows run `scripts/test-managed.ps1`, then run the desktop app once with `$env:EXPO_DOTNET_LOADER = 'hostfxr'` and once with `'nativeaot'`; the row must show `windows`, `null`, and `1.0.0.0`. Confirm both mobile adapters compile and still load the v3 host with a null host-context pointer. If the Windows machine is not available when local verification reaches this step, stop and ask the user for access. Do not claim Windows passes or close plan 023.

- [ ] **Step 3: Review actual tests and source against every spec scenario.** Check unsupported-platform handling, field independence, strict-mode read-only behavior, per-context IDs, structured malformed-input errors, and v3-only symbol resolution. If a platform cannot be run, report it as an open verification gate and do not mark plan 023 done.

- [ ] **Step 4: Merge the accepted requirements into the three relevant living specs and indexes.** Update `modules-core-boundary.md` for the context model and authored package, `runtime-and-abi.md` for the size-extensible v3 host-context ABI and adapter policy, and `dotnet-autolinking.md` for generated decoding. Record the verified status in `docs/plans/README.md`, `docs/plans/023-expo-constants-dotnet.md`, and `docs/roadmap.md`, update `docs/README.md` and `docs/specs/README.md`, then archive this change directory.

- [ ] **Step 5: Run docs/privacy checks, commit closure, and prepare local-main integration.**

```sh
git diff --check
rg 'self[-]contained planning package|planning[ ]artifacts,[ ]not[ ]implementation|expo[-]modules[-]windows[-]core|Phase[ ]1:[ ]clean[ ]separate[ ]research[ ]repo|create[ ]a[ ]clean[ ]local[ ]research[ ]repository' docs/README.md docs/specs docs/roadmap.md AGENTS.md .agents/skills
git status --short --branch
```

Scan staged files for local absolute paths, usernames, machine names, and private hostnames, then commit `docs(constants): close typed constants contract`. Follow the user-approved branch workflow: back up the completed feature branch, curate it into a few meaningful commits without changing its final tree, verify tree equality and local `main`, then fast-forward local `main`. Do not push or open a PR.
