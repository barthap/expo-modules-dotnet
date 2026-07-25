# Host-Supplied App Directories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `subagent-codex-driven-development`,
> `superpowers:subagent-driven-development`, or
> `superpowers:executing-plans` to implement this plan task by task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let `DotnetRuntimeContext` expose host-supplied app-scoped cache and
persistent-files directories, so no module resolves its own paths.

**Architecture:** The native host fills a versioned, size-checked
`expo_dotnet_app_directories` struct and passes it to a renamed v2 create entry
point. The generated aggregator host decodes the borrowed UTF-8 strings and
builds an immutable `AppDirectories` record from `Expo.ModulesCore`, which owns
validation. The runtime context stores that record and throws
`AppDirectoryNotConfiguredException` for a directory the host did not supply.

**Tech Stack:** C++17 native adapters, WinRT, Objective-C++, .NET 10, C#,
NativeAOT and HostFXR loading, Vitest for the codegen tests, and xUnit v3 with
Hermes for the managed tests.

## Global constraints

- Treat `spec.md` in this directory as the normative delta.
- The managed core stores and validates strings. It never resolves, creates,
  canonicalizes, or probes a directory.
- Validation uses pure string checks only. No `Path.GetFullPath`,
  `Directory.*`, `File.*`, `Environment.GetFolderPath`, or `Path.GetTempPath`
  in `Expo.ModulesCore`.
- Move `RuntimeContextError` and `RuntimeContextResult` verbatim. Do not rename
  types or change field order.
- Do not leave an alias under the old create symbol or the old HostFXR method
  name.
- Do not hand-edit or stage the ignored generated `.expo/dotnet` outputs.
- Committed artifacts carry sanitized path shapes only. Never a real user
  profile, machine path, or package identity.
- Keep every new managed API NativeAOT-safe. No runtime reflection or dynamic
  invocation.

## Out of scope

Named so a later implementer does not re-add them.

- **App-group shared directories.** Upstream iOS's third directory concept has
  no consumer here yet.
- **Any filesystem operation in the managed core.** The core handles strings.
- **Temp-directory creation or lifetime management in `ExpoModuleTestHost`.**
  This change only lets a test *supply* a directory. Temp-dir fixtures belong to
  the consuming test.
- **Scoped or multi-app hosts, app groups, and Expo Go-style directory
  overriding.**
- **Reshaping `RuntimeContextError` or `RuntimeContextResult`** beyond the
  verbatim move into the shared header.
- **`ExpoModulesDotnet.podspec` and the Android `CMakeLists.txt`.** Their
  existing native-header glob and `native/include` search path already pick up
  the new header.
- **`hermes-console-app`'s entry points.** It uses a separate export and the
  preserved one-argument context constructor, so it needs no source change.
- **Unifying the loaders' `char_t` divergence.** The macOS loader uses `char`
  and the Windows loader uses `wchar_t` for loader-private config plumbing. That
  stays as it is.
- **Amending plan 022's own cache-root text.** That happens when 022 is amended.
- **Exposing either directory to JavaScript.** Nothing here adds a `[JS]`
  member.

## Requirement coverage

| Delta requirement | Implementation step | Verification step | Documentation step |
| --- | --- | --- | --- |
| The Create ABI Carries A Versioned App-Directories Struct | Steps 1 and 3 | Steps 3 and 5 | Step 6 |
| The Signature Change Uses Versioned Entry-Point Names | Step 3 | Steps 3 and 5 | Step 6 |
| Directory Strings Are Borrowed Strict UTF-8 | Step 3 | Steps 3 and 5 | Step 6 |
| Unconfigured Directories Have An Exact ABI Encoding | Step 3 | Steps 3 and 5 | Step 6 |
| The Runtime Context Exposes Both App Directories | Step 2 | Steps 2 and 5 | Step 6 |
| The Managed Core Validates Paths And Never Touches The Filesystem | Step 2 | Steps 2 and 5 | Step 6 |
| Ownership Of Marshalling And The Public Model Is Split | Steps 2 and 3 | Steps 3 and 5 | Step 6 |
| Test Hosts Pass Directories Through Without Managing Them | Step 2 | Steps 2 and 5 | Step 6 |
| Platform Adapters Follow A Defined Directory Policy | Step 4 | Steps 4 and 5 | Step 6 |
| Generated-Host Verification Executes The ABI Boundary | Step 3 | Steps 3 and 5 | Step 6 |

## Platform reach

Windows adapter builds and the packaged Windows run are not runnable from a
macOS executor, and the macOS runs are not runnable from a Windows executor.
This change therefore cannot be reported as fully verified from one machine. An
executor that finishes a clean implementation hands off as **awaiting platform
verification** and names every gate it could not run. Do not report the change
as done on the strength of one platform.

---

## Step 1: Extract the shared native header without changing behavior

**Files:**

- Create `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`.
- Modify `packages/expo-modules-dotnet/macos/ManagedLoader.h`.
- Modify `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h`.
- Modify `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj`.
- Modify `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`.
- Modify
  `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`.

This step adds no struct and changes no signature. It removes the hazard that
makes the next steps safe: four hand-maintained copies of declarations that
cross through a function pointer, where a field-order drift corrupts memory
instead of failing to compile.

### 1.1 Compare all four copies before deleting any

- [ ] Diff the declarations at `macos/ManagedLoader.h:14-30`,
  `windows/ExpoModulesDotnet/ManagedLoader.h:14-30`,
  `ios/ExpoModulesDotnetInstaller.mm:20-35`, and
  `android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp:15-31`.
- [ ] **STOP and report** if any field, field order, default initializer, or
  function-pointer parameter differs. That is a pre-existing memory-safety bug
  and it is reported, not silently fixed here.

### 1.2 Move the declarations verbatim

- [ ] Create the shared header holding `RuntimeContextError`,
  `RuntimeContextResult`, and both function-pointer typedefs in
  `namespace expo::modules::dotnet`.
- [ ] Copy the field order, types, and default initializers exactly. Rename
  nothing.
- [ ] Include the shared header from all four native files and delete all four
  local declarations.
- [ ] Qualify the mobile uses that currently sit in anonymous namespaces.
- [ ] Add the header to `ExpoModulesDotnet.vcxproj` beside `expo_jsi.h`.

### 1.3 Gate

- [ ] The declaration scan finds the moved types only in
  `native/include/expo_dotnet_host.h`:

```sh
rg -n -e "struct RuntimeContext" packages/expo-modules-dotnet/native/include packages/expo-modules-dotnet/macos packages/expo-modules-dotnet/windows packages/expo-modules-dotnet/ios packages/expo-modules-dotnet/android
```

- [ ] The Android adapter builds.
- [ ] The iOS adapter builds.
- [ ] The available desktop adapter builds.
- [ ] Registration behavior is unchanged: the example module still reports its
  result on the platforms the executor can run.

Commit this refactor on its own, before any struct or signature change.

---

## Step 2: Add the managed model, the context accessors, and the test-host seam

**Files:**

- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/AppDirectories.cs`.
- Create
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/AppDirectoryNotConfiguredException.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs`.
- Modify
  `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`.

### 2.1 Write the failing tests first

- [ ] A default-constructed directory model leaves both values unconfigured.
- [ ] Each fully qualified path built from the current OS is retained verbatim.
- [ ] Empty, whitespace-only, NUL-containing, and relative values are rejected
  for each constructor parameter, naming the offending parameter.
- [ ] Both context constructors throw `AppDirectoryNotConfiguredException` from
  both accessors when unconfigured.
- [ ] Either directory can be configured while the other stays unconfigured.
- [ ] The two-argument context constructor rejects `null` with
  `ArgumentNullException`.
- [ ] A disposed context throws `ObjectDisposedException` from both accessors,
  before configuration is consulted.
- [ ] The exception's directory-name member and its message distinguish the two
  accessors.
- [ ] A fully qualified path that does not exist on disk is accepted, proving
  validation performs no probe.
- [ ] The directory-aware test-host factory exposes supplied values inside the
  registration callback, and the existing factory stays source-compatible.
- [ ] Build test paths from the test OS, for example through
  `Path.GetPathRoot(Environment.CurrentDirectory)`. Do not hard-code one OS's
  path syntax.

Run:

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
```

Expected before implementation: compilation fails only because the model, the
exception, the accessors, and the test-host overload do not exist.

### 2.2 Implement the model and the exception

- [ ] Add the immutable `AppDirectories` record with an unconfigured singleton
  and one optional parameter per directory.
- [ ] Add one private validation helper that returns `null` for `null` and
  otherwise rejects empty, whitespace-only, NUL-containing, and
  non-fully-qualified values. Use `Path.IsPathFullyQualified`.
- [ ] Add `AppDirectoryNotConfiguredException` deriving from
  `InvalidOperationException`, with an internal constructor and a public member
  naming the unconfigured accessor.
- [ ] Match `DotnetRuntimeContext.cs` conventions: two-space indentation,
  file-scoped namespace, `///` XML docs on public members.

### 2.3 Implement the context accessors and the test-host overload

- [ ] Add the two-argument `DotnetRuntimeContext` constructor and have the
  existing one-argument constructor delegate with the unconfigured value.
- [ ] Store the immutable record and leave the existing object and registry
  initialization untouched.
- [ ] Add both accessors. Each takes `lock (gate)`, calls
  `ThrowIfNotActiveLocked()`, then returns the value or throws the specific
  configuration exception.
- [ ] Add the test-host factory overload taking the directory model, and have
  the existing overload delegate with the unconfigured value. Do not change the
  existing signature.
- [ ] Do not add any directory creation or cleanup to the test host.

### 2.4 Gate

Run:

```sh
scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj
rg -n -e "Directory\\." -e "File\\." -e "GetFolderPath" -e "GetTempPath" -e "SpecialFolder" -e "GetFullPath" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore
```

- [ ] The focused ModulesCore tests pass.
- [ ] The filesystem scan reports no matches.
- [ ] Reverting the path retention, the independence of the two directories, or
  the disposal guard makes its own test fail.

Commit the managed slice.

---

## Step 3: Change the ABI and the generated export atomically

**Files:**

- Modify `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`.
- Modify `packages/expo-modules-dotnet/macos/ManagedLoader.h` and
  `packages/expo-modules-dotnet/macos/ManagedLoader.mm`.
- Modify `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h`
  and `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.cpp`.
- Modify `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`,
  `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`,
  `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`, and
  `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`.
- Modify `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`.
- Modify `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`.
- Create
  `packages/expo-modules-dotnet-autolinking/src/__tests__/fixtures/entry-points-abi-harness.cs`.

**This task is one commit.** Do not split the native function-pointer change
from the generated managed export. A three-argument export called through a
four-argument native typedef compiles while treating the directory pointer as
`RuntimeContextResult*`. Splitting the commit creates a tree that builds and
corrupts memory.

### 3.1 Add failing codegen assertions and the harness driver

- [ ] Assert the NativeAOT symbol `expo_dotnet_create_runtime_context_result_v2`
  and the HostFXR method `CreateRuntimeContextResultV2`, and assert the absence
  of both old names.
- [ ] Assert the four-parameter export signature with the directory pointer as
  a native integer before the result pointer.
- [ ] Assert a private sequential native mirror in the shared header's exact
  field order.
- [ ] Assert size-before-version validation, with both values in each mismatch
  message.
- [ ] Assert strict UTF-8 decoding.
- [ ] Assert the pointer/length rules: `(null, 0)` unconfigured,
  `(null, nonzero)` invalid, negative length invalid, and `(non-null, 0)`
  decoded so constructor validation rejects it.
- [ ] Assert construction of the runtime context with the decoded model.
- [ ] Add the checked-in harness fixture and the Vitest driver that generates
  against the real `Expo.JSI` and `Expo.ModulesCore` projects in a temporary
  directory, adds the fixture as a compile item, and runs the temporary host
  project.

Run:

```sh
pnpm --filter expo-modules-dotnet-autolinking test
```

Expected before implementation: the source assertions and the harness driver
both fail.

### 3.2 Extend the shared header

- [ ] Add the host ABI version constant and the
  `expo_dotnet_app_directories` struct: `size`, `version`, cache pointer and
  length, persistent pointer and length, in that order.
- [ ] Document the borrowed-for-the-call UTF-8 lifetime and the exact
  null/length rules in the header.
- [ ] Add `static_assert` checks for standard layout, every field offset, and
  the total size on 32-bit and 64-bit pointer targets.
- [ ] Rename the create typedef to `CreateRuntimeContextV2Fn`, insert the
  directory pointer before the result pointer, and rename the desktop
  entry-point field to `createRuntimeContextV2`.
- [ ] Leave the teardown typedef and name alone.

### 3.3 Update the resolvers and all four native call sites

- [ ] Update the symbol and method-name constants in `macos/ManagedLoader.mm`
  and `windows/ExpoModulesDotnet/ManagedLoader.cpp` to the v2 names.
- [ ] Update the literal symbol names used by the iOS and Android installers.
- [ ] Do not probe or export the old create name as a fallback.
- [ ] Update all four native invocations to pass a null struct pointer for now.
  Step 4 fills in Windows and macOS.

### 3.4 Update the generator

- [ ] Emit the private sequential native mirror beside the existing result
  types.
- [ ] Emit one private strict-UTF-8 decoder using
  `new UTF8Encoding(false, throwOnInvalidBytes: true)`. Do not add an
  `InternalsVisibleTo` edge to reuse the `Expo.JSI` instance.
- [ ] Emit one per-field decode helper covering negative length, null with a
  length, null with zero length, and non-null decoding.
- [ ] Check `size` before reading `version` or either pointer field.
- [ ] Decode and construct the public model before constructing the runtime
  context, so module registration observes the directories.
- [ ] Rename the export and the HostFXR-facing method to the v2 names.
- [ ] Emit the entry-point type as `partial` so the harness can reach the
  private decoder without widening production visibility.
- [ ] Regenerate both example app hosts. Do not hand-edit or stage them.

### 3.5 Gate

Run:

```sh
pnpm --filter expo-modules-dotnet-autolinking test
pnpm --filter expo-modules-dotnet-autolinking typecheck
pnpm --filter expo-modules-dotnet-autolinking build
pnpm --dir apps/desktop-app exec expo-modules-dotnet-autolinking generate
pnpm --dir apps/mobile-app exec expo-modules-dotnet-autolinking generate
cmp apps/desktop-app/.expo/dotnet/EntryPoints.g.cs apps/mobile-app/.expo/dotnet/EntryPoints.g.cs
scripts/test-managed.sh
```

- [ ] The codegen tests compile and run the ABI harness, and the harness
  releases every structured error buffer.
- [ ] Typecheck, build, and both host generations pass, and the two generated
  entry-point files are identical.
- [ ] The full managed suite passes.
- [ ] The Android adapter, the iOS adapter, and the available desktop adapter
  build.
- [ ] The declaration scan finds the struct and both typedefs only in the shared
  header.
- [ ] The old-name scan is empty:

```sh
rg -n "expo_dotnet_create_runtime_context_result\\b|CreateRuntimeContextResult\\b|CreateRuntimeContextFn\\b|createRuntimeContext\\b" packages/expo-modules-dotnet packages/expo-modules-dotnet-autolinking/src
```

- [ ] Symbol inspection of the built NativeAOT library (`nm -gU` on Apple,
  `dumpbin /exports` on Windows) shows the v2 symbol present and the old symbol
  absent. Together with the harness's assertion on the managed method name, that
  proves either stale pairing fails resolution before invocation.

Commit the header, both resolvers, all four native call sites, the generator,
the fixture, and the codegen test together.

---

## Step 4: Populate the packaged Windows and macOS hosts

**Files:**

- Modify `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`.
- Modify `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`.

Build the UTF-8 strings and the struct in the same stack frame as the create
call. The buffers must outlive the synchronous call.

### 4.1 Windows

- [ ] Include `<winrt/Windows.Storage.h>` explicitly.
- [ ] Call `ApplicationData::Current()` once. Use `LocalCacheFolder().Path()`
  for the cache directory and `LocalFolder().Path()` for persistent files.
- [ ] Convert both with `winrt::to_string`.
- [ ] On `winrt::hresult_error`, log that the unpackaged host provided no app
  identity and pass a null struct pointer.
- [ ] Do not add a `%LOCALAPPDATA%` fallback or an executable-name fallback.

### 4.2 macOS

- [ ] Take the first cache URL and the first Application Support URL from
  `-[NSFileManager URLsForDirectory:inDomains:]` in `NSUserDomainMask`.
- [ ] Append the non-empty main-bundle identifier with
  `URLByAppendingPathComponent:isDirectory:`.
- [ ] Use Application Support, not `NSDocumentDirectory`. On macOS
  `NSDocumentDirectory` resolves to the user's visible Documents folder unless
  sandboxed, which is not app-private storage.
- [ ] Convert both final paths to explicit UTF-8 strings and pass the populated
  struct.
- [ ] If a URL or the bundle identifier is absent, log the missing host identity
  and pass a null struct pointer.

### 4.3 Check the resolved values, then emit the marker

- [ ] Before the create call, confirm both paths are distinct, fully qualified,
  and app-scoped.
- [ ] Emit one durable, path-free marker only after those checks pass.
- [ ] **STOP** if a path is a bare user-wide root, if packaged Windows is
  unconfigured, or if the design appears to need a filesystem call in the
  managed core.
- [ ] Remove every temporary raw-path diagnostic before committing.

### 4.4 Gate

- [ ] The macOS HostFXR run and the macOS NativeAOT run both log the marker and
  report the example module's result.
- [ ] The Windows HostFXR run and the Windows NativeAOT run both do the same for
  the packaged app.
- [ ] Committed artifacts record sanitized path shapes only, such as
  `<user-home>/Library/Caches/<bundle-id>` and
  `<local-app-data>/<package-family>/LocalCache`.
- [ ] If one platform is unavailable to the executor, hand off as awaiting
  platform verification and name the missing gates.

Commit source and sanitized docs only.

---

## Step 5: Run the full verification matrix

**Files:** none. This step only runs and records.

### 5.1 Run every gate

Run the managed, codegen, formatting, scan, and adapter-build commands. Use
`scripts/test-managed.ps1` for the Windows managed pass. Enable `pipefail`
before any Apple CLI pipeline. Prefer the repo's `xcodebuildmcp-cli` skill for
Apple builds when it is available.

```sh
scripts/test-managed.sh
pnpm --filter expo-modules-dotnet-autolinking test
pnpm --filter expo-modules-dotnet-autolinking typecheck
pnpm --filter expo-modules-dotnet-autolinking build
scripts/format.sh --check --all
git diff --check
```

- [ ] Every managed, codegen, formatting, whitespace, declaration, old-name, and
  filesystem-scan gate passes.
- [ ] All four adapters build.
- [ ] Android and iOS execute the generated v2 export and register the example
  module.
- [ ] HostFXR and NativeAOT both run on both desktop targets.
- [ ] No gate is skipped silently. A skipped gate keeps the change non-done and
  is named in the handoff.

### 5.2 Check the diff before staging

- [ ] Every tracked file in `git diff --name-only` is in this plan's file lists.
- [ ] The ignored `.expo/dotnet` outputs exist but are not staged.
- [ ] The staged diff contains no local absolute path, username, machine name,
  private hostname, or real package identity.

---

## Step 6: Merge the accepted delta and archive the change

**Files:**

- Modify `docs/specs/runtime-and-abi.md`.
- Modify `docs/specs/modules-core-boundary.md`.
- Modify `docs/specs/dotnet-autolinking.md`.
- Modify `docs/specs/hermes-testhost.md`.
- Modify `docs/plans/README.md`.
- Move this change package to
  `docs/archive/changes/2026-07-25-host-app-directories/`.

### 6.1 Confirm the implementation matches the approved delta

- [ ] Compare the verified implementation and the proposed living-spec edits
  against `spec.md`.
- [ ] **STOP** and get approval if the implementation materially diverged, or if
  the spec edits would add a requirement that was not approved. Mechanical
  wording and evidence updates that preserve the approved requirements do not
  need another approval.

### 6.2 Merge into the living specs

- [ ] `runtime-and-abi.md`: the create entry point's new parameter, the v2 name
  rule, the struct's size and version validation, and the borrowed UTF-8 field
  rules.
- [ ] `modules-core-boundary.md`: the host-supplied directories, the
  unconfigured and disposed behavior, the independence of the two directories,
  and the no-filesystem rule.
- [ ] `dotnet-autolinking.md`: the aggregator's marshalling responsibility and
  the compiled ABI harness.
- [ ] `hermes-testhost.md`: the binary-compatible test-host pass-through.
- [ ] `docs/plans/README.md`: mark plan 027 done only now, unblock plan 022, and
  replace 022's cache-root blocker with the requirement to read
  `context.CacheDirectory`.

### 6.3 Archive and close

- [ ] Move this change directory to `docs/archive/changes/`.
- [ ] Do not merge, archive, or mark plan 027 done while platform verification
  is outstanding.

Run:

```sh
scripts/format.sh --check --all
git diff --check
```

Expected: both are clean, and the staged diff carries no local absolute path,
username, machine name, private hostname, or real package identity.
