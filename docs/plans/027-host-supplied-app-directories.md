# Plan 027: Host-supplied app-scoped directories on `DotnetRuntimeContext`

> **Executor instructions**: Follow this plan step by step. Run every
> verification command and confirm the expected result before moving on. Touch
> only the files in the In-scope list. If a STOP condition occurs, stop and
> report — do not improvise. Follow the repo's living-spec workflow: delta spec
> first, then plan, then implementation. Update the status row in
> `docs/plans/README.md` when done unless a reviewer says they maintain it.
>
> **Drift check (run first)**:
> ```sh
> git diff --stat 4c10f90b..HEAD -- packages/expo-modules-dotnet/native/include packages/expo-modules-dotnet/macos packages/expo-modules-dotnet/windows packages/expo-modules-dotnet/ios packages/expo-modules-dotnet/android packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet-autolinking/src/codegen apps/desktop-app/.expo apps/mobile-app/.expo docs/specs
> ```
> If the runtime-context entry-point signature, `RuntimeContextResult`, the
> aggregator codegen template, or `DotnetRuntimeContext`'s constructor changed,
> compare the live code against the excerpts in "Current state" before
> proceeding. A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED-HIGH (changes a private native↔managed ABI signature with six
  call sites across five platform adapters; a mismatched struct layout corrupts
  memory instead of failing to compile)
- **Depends on**: none
- **Blocks**: `docs/plans/022-expo-asset-dotnet.md`. Also unblocks plan 024
  (local filesystem) with no further ABI work, because Decision 5 ships the
  persistent files directory too.
- **Category**: core capability
- **Planned at**: `4c10f90b`, 2026-07-25

## Why this matters

An authored module cannot currently find out where it may write files. Plan 022
worked around that by resolving an OS cache root inside the module
(`docs/plans/022-expo-asset-dotnet.md:646-651`: `%LOCALAPPDATA%` on Windows,
`<UserProfile>/Library/Caches` on macOS, an XDG path on Linux). Those are
**user-wide** paths, not app-scoped ones, so two apps sharing a machine would
share and clobber one asset cache. That is defect D1 against plan 022 and it must
not ship.

Upstream never lets a module resolve its own paths. Both official platforms take
the directory from the host:

- **Android** — `AppContext.cacheDirectory` delegates to
  `AppDirectoriesService.cacheDirectory`, which returns `context.cacheDir`
  (`expo/packages/expo-modules-core/android/src/main/java/expo/modules/kotlin/services/AppDirectoriesService.kt:16`).
  The service is `open` so scoped hosts such as Expo Go can override it. When the
  service is not registered the accessor throws
  `"AppDirectoriesService is not registered in the ServicesRegistry."`
- **iOS** — `AppContextConfig.cacheDirectory` is a host-injectable `URL?`
  (`expo/packages/expo-modules-core/ios/Core/AppContextConfig.swift:5,11`).
  Separately, the legacy `appContext.fileSystem.cachesDirectory` is what
  `expo-asset` actually reads
  (`expo/packages/expo-asset/ios/AssetModule.swift:29`), inside
  `guard let … else { promise.reject(...) }`.

So: the path comes from the host, and a missing path fails loudly rather than
silently falling back. This plan gives `Expo.ModulesCore` the same property. The
operator's framing — "Cache dir should be exposed by `DotnetRuntimeContext`,
following upstream `AppContext` purpose" — settles the owner; `DotnetRuntimeContext`
already documents itself as this repo's narrow equivalent of upstream's
`AppContext` (`DotnetRuntimeContext.cs:5-28`).

The operator's standing rule applies: "these modules need to be state of the art.
No workarounds because core is missing a feature. We'll fix core instead."

### Why this justifies new ABI surface

`### Requirement: ABI Carries Only Host Knowledge`
(`docs/specs/runtime-and-abi.md`) requires any plan adding ABI surface to name
the host-knowledge category and say why portable .NET cannot answer it. For this
value:

- **Category**: host identity, plus host-supplied policy.
- **Why .NET cannot answer it**: .NET's own path APIs are all user-wide.
  `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` and
  `Path.GetTempPath()` do not know which app is asking, so two apps on one
  machine resolve the same root and clobber each other's cache. Making any of
  them app-scoped needs an app identity — a bundle identifier on macOS, a package
  family name on Windows MSIX — and obtaining either from managed code means
  P/Invoking CoreFoundation or kernel32, or guessing from `__CFBundleIdentifier`
  and `Info.plist`. That is a platform-specific native dependency inside a
  package that must stay portable.
- **Why the host supplies the finished path, not just the identity**: both cost
  one UTF-8 string. Supplying the identity alone would force the portable core to
  hard-code each OS's cache-root convention (`~/Library/Caches` on macOS,
  `%LOCALAPPDATA%` on Windows), which is more managed code for less capability —
  and it would remove the host's ability to override the directory, which
  upstream deliberately preserves for scoped hosts.

Everything else plan 022 does stays in .NET: `HttpClient` for the download,
`System.Security.Cryptography` for MD5, and `File`/`Directory`/`Path` for the
cache write. Two strings of host knowledge cross the ABI; the rest is the
base class library. If a future plan's ratio inverts, that is the signal to stop and
rethink rather than add a field.

## Current state

### The runtime-context ABI has no options blob

`packages/expo-modules-dotnet/macos/ManagedLoader.h:14-30` (verbatim; the Windows
copy at `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h:14-30`
is character-identical):

```cpp
struct RuntimeContextError {
  const char *message;
  int32_t messageLength;
  void *releaseContext;
  void (*release)(void *);
};

struct RuntimeContextResult {
  int32_t ok;
  void *runtimeContext;
  RuntimeContextError error;
};

using CreateRuntimeContextFn = void (*)(const expo_jsi_api *,
                                        expo_jsi_runtime_handle,
                                        RuntimeContextResult *);
using TeardownRuntimeContextFn = void (*)(void *);
```

Two inbound arguments, both opaque: the JSI API table and the runtime handle.
Nothing else crosses in. `ManagedModuleConfig`
(`macos/ManagedLoader.h:37-44`) holds loader-side artifact paths but is
native-only and never reaches managed code.

**These declarations are duplicated, not shared.** `native/include/expo_jsi.h`
does not contain them. There are three copies today:

1. `packages/expo-modules-dotnet/macos/ManagedLoader.h:14-30`
2. `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h:14-30`
3. `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm:20-34` — an
   older, divergent local redeclaration that does not include either
   `ManagedLoader.h`

That duplication is why this plan's risk is MED-HIGH rather than MED. Everything
crosses through a function pointer, so a struct that drifts in one copy produces
memory corruption at runtime, not a compile error. See "Decision 1".

### The managed side of the entry point is generated, not hand-written

`new DotnetRuntimeContext(runtime)` in production comes from the autolinking
codegen template, not from a package source file:
`packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts:180`,
inside `CreateRuntimeContextCore` (template body at `generateAggregator.ts:121-284`).

The emitted managed entry point, `apps/desktop-app/.expo/dotnet/EntryPoints.g.cs:12-19`
(verbatim):

```csharp
[UnmanagedCallersOnly(EntryPoint = "expo_dotnet_create_runtime_context_result",
                      CallConvs = new[] { typeof(CallConvCdecl) })]
public static unsafe void CreateRuntimeContextResult(nint api, nint runtimeHandle, RuntimeContextResult* result)
```

and the core it wraps, `EntryPoints.g.cs:56-70` (relevant lines):

```csharp
var runtime = JavaScriptRuntime.FromNative(api, runtimeHandle);
var context = new DotnetRuntimeContext(runtime);
...
LinkedExpoModulesProvider.Register(context);
```

`Register(context)` runs **inside** context creation, so a module constructor can
already observe the context. Anything the context must expose has to be present
before `Register` is called. That rules out a post-creation setter (Decision 2).

Full production chain:
native installer → `resolveRuntimeContextEntryPoints` →
`createRuntimeContext(api, runtimeHandle, &result)` →
`EntryPoints.CreateRuntimeContextResult` → `CreateRuntimeContextCore` →
`new DotnetRuntimeContext(runtime)` → `LinkedExpoModulesProvider.Register(context)`.

### `DotnetRuntimeContext` today

`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`:

- One constructor, `DotnetRuntimeContext(JavaScriptRuntime runtimeArgument)`
  (`:49-56`), which creates `JavaScriptObjectFactory`, `ModuleEventEmitter`,
  `ModuleRegistry`, `SharedObjectRegistry`.
- Public members `Runtime` (`:68`), `Objects` (`:80`), `ModuleRegistry` (`:92`),
  `Events` (`:104`); internal `SharedObjects` (`:116`).
- Every accessor takes `lock (gate)` and calls `ThrowIfNotActiveLocked()`
  (`:263-269`), which throws `ObjectDisposedException` once disposed.
- `Dispose()` (`:173-242`) is a three-state lifecycle with cross-thread waiting
  and `AggregateException` collection.

### There is no filesystem code in the managed core at all

A recursive search of `Expo.ModulesCore`, `Expo.JSI`, `Expo.ModulesCore.Testing`,
and `Expo.ModulesCore.Generator` (excluding `obj/`, `bin/`) for `System.IO`,
`Environment.GetFolderPath`, `Path.GetTempPath`, `SpecialFolder`, `Directory.`,
`Path.Combine`, `File.Read`, and `File.Write` returns **zero matches**. This plan
introduces the first path concept into the core, so it sets the precedent. Keep it
narrow: the core stores and validates strings and never touches a disk
(Decision 4).

No csproj sets `IsAotCompatible`, `PublishTrimmed`, `IsTrimmable`, or
`EnableTrimAnalyzer`, and there is no repo-root `Directory.Build.props`. But AOT
is a real publish mode applied from outside —
`packages/expo-modules-dotnet-autolinking/src/build.ts:42,47` pass
`/p:PublishAot=true` and `/p:PublishAotUsingRuntimePack=true --self-contained true`
— so new APIs must be NativeAOT-safe even though no analyzer enforces it.
`System.Text.UTF8Encoding` and `System.IO.Path`'s pure string helpers are.

### The ABI's string convention

Normative in `docs/specs/runtime-and-abi.md:223-226`: "The ABI SHALL represent
strings as UTF-8 pointer plus byte length and SHALL provide a release callback for
owned native string buffers."

In practice there are two ownership shapes:

- **Borrowed for the duration of the call** — `expo_jsi.h:216-218`
  `expo_jsi_create_string_fn(runtime, const uint8_t *data, int32_t length)`.
  Caller pins, callee copies immediately. Managed side pins with `fixed`
  (`Expo.JSI/Interop/ExpoJsiApi.cs:464-471`).
- **Callee-allocated with an explicit release callback** — `expo_jsi.h:95-104`
  `expo_jsi_string_result`, released in a `finally`
  (`ExpoJsiApi.cs:573-591`).

UTF-8 is validated strictly, never silently repaired:
`ExpoJsiApi.cs:346-349` uses
`new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)`.

### The versioned-struct convention

`packages/expo-modules-dotnet/native/include/expo_jsi.h:440-442`:

```c
typedef struct expo_jsi_api {
  uint32_t size;
  uint32_t version;
```

Managed validates strict version equality and reports both sides
(`ExpoJsiApi.cs:362-365`, `"Expo JSI ABI version mismatch: native={0} managed={1}."`;
`ExpectedVersion = 23` at `ExpoJsiApi.cs:1010`). Specified as
`### Requirement: ABI Version And Size Validation`
(`docs/specs/runtime-and-abi.md:245`).

Strict equality is safe here because the loader and the generated host are built
together for one app. Reuse this exact shape.

### The test host has no options seam

`packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`:

- Private constructor (`:17-21`); one factory,
  `public static ExpoModuleTestHost Create(Action<DotnetRuntimeContext, JavaScriptObject> register)`
  (`:38-78`), which calls `new DotnetRuntimeContext(runtime)` at `:49`.
- No options or builder parameter of any kind. No public `Context` property —
  a test reaches the context only through the `register` callback's first
  argument.
- `Dispose()` (`:175-240`) disposes the context on the runtime thread and then
  the Hermes runtime. It performs **no filesystem cleanup**, because nothing in
  the core has ever created files.

### Other call sites that must change together

| Call site | File:line |
| --- | --- |
| macOS loader typedef + resolve | `packages/expo-modules-dotnet/macos/ManagedLoader.h:27-30`, `macos/ManagedLoader.mm:17-21,199` |
| Windows loader typedef + resolve | `windows/ExpoModulesDotnet/ManagedLoader.h:27-30`, `ManagedLoader.cpp:19-24` |
| macOS installer invocation | `macos/ExpoModulesDotnetInstaller.mm:115-116`, factory at `:172-182` |
| Windows installer invocation | `windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp:103`, `registerModules` at `:87-117`, `Initialize` at `:211-274` |
| iOS installer (divergent local structs) | `ios/ExpoModulesDotnetInstaller.mm:20-34`, call at `:165` |
| Android bindings installer | `android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp` |
| Codegen template | `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts:121-284` |
| Emitted artifacts (checked in) | `apps/desktop-app/.expo/dotnet/EntryPoints.g.cs`, `apps/mobile-app/.expo/dotnet/EntryPoints.g.cs` |
| Dev console app (its own entry points) | `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs:11-41,100` |
| Test host | `Expo.ModulesCore.Testing/ExpoModuleTestHost.cs:38-78` |

The generated host csproj template compiles exactly two files
(`generateAggregator.ts:81-82`), so any new managed type must live in
`Expo.ModulesCore`, not alongside the generated code.

## Decisions

These are settled by upstream behavior, by the repo's own conventions, or by the
constraint in `AGENTS.md`. Decisions 1 and 5 were put to the operator on
2026-07-25 and are recorded here as answered; the rest need no confirmation.

### Decision 1 — extract the runtime-context ABI into one shared header

**Approved by the operator, 2026-07-25.** Do this rather than adding a struct to
each of the three duplicated declarations. Create
`packages/expo-modules-dotnet/native/include/expo_dotnet_host.h` holding
`RuntimeContextError`, `RuntimeContextResult`, the new app-directories struct,
the function-pointer typedefs, and the version constant. Have
`macos/ManagedLoader.h`, `windows/ExpoModulesDotnet/ManagedLoader.h`, and
`ios/ExpoModulesDotnetInstaller.mm` include it instead of redeclaring.

Rationale: everything crosses through a function pointer, so drift between copies
is undetectable at compile time and corrupts memory at runtime. Adding a struct to
three hand-maintained copies converts a latent smell into an active hazard. This
is a prerequisite for the change, not an unrelated cleanup.

Scope limit: move the declarations verbatim and update includes. Do not rename
types, do not change the existing field order, and do not touch the loader's
platform-divergent `char_t`/`std::wstring` config plumbing
(`macos/ManagedHostFxr.h:8` uses `char`, `windows/ManagedHostFxr.h:8` uses
`wchar_t`) — that divergence is in loader-private code and is out of scope.

The alternative that was rejected: add the struct to all three copies plus a
compile-time `static_assert` on `sizeof` in each. That catches size drift but not
field-order drift, which is the corrupting kind.

### Decision 2 — the directory arrives at context creation, not through a setter

`LinkedExpoModulesProvider.Register(context)` runs inside
`CreateRuntimeContextCore` (`EntryPoints.g.cs:62`), so a module constructor can
observe the context before any post-creation setter would run. A setter creates a
window where the cache directory is silently absent. Pass it as an argument to
the create entry point.

### Decision 3 — an unconfigured directory throws; the core never falls back

`Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` and
`Path.GetTempPath()` are user-wide or process-wide, which is the exact defect
being fixed, so there is no acceptable fallback inside portable code. Both
upstream platforms fail loudly instead: Android throws when
`AppDirectoriesService` is not registered, and iOS's asset module rejects the
promise when `cachesDirectory` is nil.

So the accessor's type is non-nullable `string` and it throws a new, specific
exception when the host supplied nothing. Module code then reads exactly like
upstream Android's `appContext.cacheDirectory`.

### Decision 4 — the core validates strings and never touches the disk

`Expo.ModulesCore` stores the path, checks it is non-empty and fully qualified,
and stops there. It does not create the directory, probe it for writability, or
canonicalize it. Creating a subdirectory before writing belongs to the consuming
module, matching upstream iOS, where `FileSystemUtilities.generatePathInCache`
calls `ensureDirExists` at use time
(`expo/packages/expo-modules-core/ios/FileSystemUtilities/FileSystemUtilities.swift:29-38`).

This keeps the first path concept in the core to pure string handling, which
preserves the portability constraint in `AGENTS.md` and stays trivially
NativeAOT-safe.

### Decision 5 — ship both directories, matching upstream's pair

**Decided by the operator, 2026-07-25**, against the plan's original
cache-only proposal. Ship `cache_directory` and `persistent_files_directory`
together, mirroring Android's `cacheDirectory` + `persistentFilesDirectory` and
iOS's `cacheDirectory` + `documentDirectory`.

Consequences:

- Plan 024 (local filesystem) needs **no** ABI work. It reads
  `context.PersistentFilesDirectory` as it already exists.
- Every rule in this plan applies to both fields equally: both are validated,
  both throw `AppDirectoryNotConfiguredException` when unconfigured, and either
  may be unconfigured independently of the other.
- The two directories are independent. A host MAY supply one and not the other,
  and the tests SHALL cover that case — an adapter that only has a cache path
  must not be forced to fabricate a documents path.

Name the second one `PersistentFilesDirectory`, following Android. iOS's
`documentDirectory` means the same thing, but "Documents" on Windows and macOS is
the user's own Documents folder, which this is not.

### Decision 6 — a wrapper record, not bare string parameters

Introduce `AppDirectories`, mirroring iOS's `AppContextConfig`, rather than adding
two string parameters to `DotnetRuntimeContext`'s constructor. It keeps the
constructor signature stable if upstream's third directory concept (iOS app-group
shared directories) ever lands, and it gives the native-decode logic one obvious
home. This is the only abstraction this plan adds.

### Decision 7 — which adapters supply a real path

- **Windows and macOS SHALL supply a real app-scoped path.** They are plan 022's
  target platforms.
- **iOS, Android, and `hermes-console-app` SHALL pass "unconfigured".** No module
  on those hosts consumes app-scoped storage through this bridge yet; upstream's
  own `expo-asset` serves iOS and Android. Passing a guessed path there would be
  inventing a contract with no consumer to validate it, and `hermes-console-app`
  is a dev harness with no app identity at all.

This is not a partial feature: the capability is complete end to end — the ABI
carries the directory, the core exposes it, and "unconfigured" is a defined state
that fails loudly (Decision 3). Which hosts populate it is per-adapter policy,
and an adapter with no consumer is correctly left unconfigured rather than
fabricating a path.

### Decision 8 — how each platform resolves its app-scoped paths

**Windows** (`ExpoModulesDotnetInstaller.cpp`, which already holds a
`winrt::Microsoft::ReactNative::ReactContext` at `:211-274`), preferring
`winrt::Windows::Storage::ApplicationData::Current()`:

| Directory | Packaged | Unpackaged fallback |
| --- | --- | --- |
| cache | `ApplicationData::Current().LocalCacheFolder().Path()` | `%LOCALAPPDATA%\<executable-stem>\Cache` |
| persistent files | `ApplicationData::Current().LocalFolder().Path()` | `%LOCALAPPDATA%\<executable-stem>\Data` |

`ApplicationData::Current()` throws for unpackaged processes, so guard it once and
use the fallbacks for both. Every branch is app-scoped; none is the bare user-wide
root.

**macOS** (`ExpoModulesDotnetInstaller.mm`, factory at `:172-182`), each resolved
with `NSSearchPathForDirectoriesInDomains(..., NSUserDomainMask, YES).firstObject`
and then `[[NSBundle mainBundle] bundleIdentifier]` appended:

| Directory | Search path | Typical unsandboxed result |
| --- | --- | --- |
| cache | `NSCachesDirectory` | `~/Library/Caches/<bundle-id>` |
| persistent files | `NSApplicationSupportDirectory` | `~/Library/Application Support/<bundle-id>` |

Use Application Support, not `NSDocumentDirectory`. Upstream's iOS
`documentDirectory` is app-private because iOS is always sandboxed, and Android's
`filesDir` is app-private too; on macOS `NSDocumentDirectory` resolves to the
user's visible `~/Documents` unless sandboxed, which is not app-private storage.
Application Support is the correct analogue of both.

Under App Sandbox these search paths already resolve inside the app container;
appending the bundle identifier is Apple's documented convention for the
unsandboxed case and is harmless in a container. If `bundleIdentifier` is nil,
pass "unconfigured" for both rather than a bare user-wide root.

Verify the actual resolved paths on both platforms during Step 6 and record all
four in the delta spec. If any resolves to a bare user-wide root, that is a STOP
condition — it is the defect this plan exists to remove.

## Proposed shape

Native, in the new shared header:

```c
#define EXPO_DOTNET_HOST_ABI_VERSION 1

typedef struct expo_dotnet_app_directories {
  uint32_t size;     // sizeof(expo_dotnet_app_directories)
  uint32_t version;  // EXPO_DOTNET_HOST_ABI_VERSION

  // All strings: UTF-8, not NUL-terminated. Borrowed — valid only for the
  // duration of the create call. A null pointer or zero length means "not
  // configured", and each directory is independent of the other.

  // Temporary files the operating system may remove at any time.
  const uint8_t *cache_directory;
  int32_t cache_directory_length;

  // User documents and other files that must survive OS cache eviction.
  const uint8_t *persistent_files_directory;
  int32_t persistent_files_directory_length;
} expo_dotnet_app_directories;

using CreateRuntimeContextFn = void (*)(const expo_jsi_api *,
                                        expo_jsi_runtime_handle,
                                        const expo_dotnet_app_directories *,
                                        RuntimeContextResult *);
```

Borrowed-for-the-call ownership is the right convention here (matching
`expo_jsi_create_string_fn`) because the lifetime is bounded by the call: managed
code decodes to a `string` before returning, so no release callback is needed. The
whole struct pointer may be null, which also means "not configured" — that keeps
Decision 7's adapters honest without a sentinel.

Managed, in `Expo.ModulesCore`:

```csharp
public sealed record AppDirectories
{
  public static AppDirectories Unconfigured { get; } = new();

  /// <summary>Fully qualified app-scoped cache directory, or null if the host supplied none.</summary>
  public string? CacheDirectory { get; init; }

  /// <summary>Fully qualified app-scoped persistent files directory, or null if the host supplied none.</summary>
  public string? PersistentFilesDirectory { get; init; }
}
```

and on the context:

```csharp
public DotnetRuntimeContext(JavaScriptRuntime runtimeArgument)
    : this(runtimeArgument, AppDirectories.Unconfigured) { }

public DotnetRuntimeContext(JavaScriptRuntime runtimeArgument, AppDirectories directories) { ... }

/// <summary>
/// A directory for temporary files the operating system may remove at any time.
/// </summary>
/// <exception cref="AppDirectoryNotConfiguredException">The host supplied no cache directory.</exception>
public string CacheDirectory { get; }

/// <summary>
/// A directory for user documents and other files that must survive cache eviction.
/// </summary>
/// <exception cref="AppDirectoryNotConfiguredException">The host supplied no persistent files directory.</exception>
public string PersistentFilesDirectory { get; }
```

Keeping the one-argument constructor means all 650 existing tests and both
in-tree app artifacts keep compiling; it now reads as "no directories
configured", which is exactly true.

## Conventions

- Two-space indentation, file-scoped namespaces, `///` XML docs on public
  members — match `DotnetRuntimeContext.cs`.
- Every new public accessor on the context takes `lock (gate)` and calls
  `ThrowIfNotActiveLocked()` first, like `Runtime` (`:68-78`). A disposed context
  must throw `ObjectDisposedException` from `CacheDirectory` too.
- Decode UTF-8 with `new UTF8Encoding(false, throwOnInvalidBytes: true)`, matching
  `ExpoJsiApi.cs:346-349`. Do **not** add `InternalsVisibleTo("Expo.ModulesCore")`
  to `Expo.JSI.csproj` to reuse its instance — declare a private static one in the
  new file.
- Validate with `Path.IsPathFullyQualified`, which is a pure string check with no
  disk access. Do not call `Path.GetFullPath`, `Directory.Exists`, or anything
  else that touches the filesystem.
- ABI mismatch messages follow the existing format: include both values, as in
  `"Expo JSI ABI version mismatch: native={0} managed={1}."`
- C++ files follow the surrounding installer style; run `scripts/format.sh` and
  let it decide formatting.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full managed regression | `scripts/test-managed.sh` | exit 0; 650 pre-existing tests still pass, plus the new ones |
| ModulesCore runtime tests only | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | exit 0 |
| Autolinking codegen tests | `pnpm --filter expo-modules-dotnet-autolinking test` | exit 0; `generateAggregator.test.ts` covers the new parameter |
| Autolinking typecheck | `pnpm --filter expo-modules-dotnet-autolinking typecheck` | exit 0 |
| Regenerate app artifacts | `expo-modules-dotnet-autolinking generate` from each app dir — confirm the exact subcommand with `--help` first | `apps/*/.expo/dotnet/EntryPoints.g.cs` regenerated; diff matches the template change and nothing else |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | no output |
| No filesystem access in the core | `rg "Directory\.\|File\.\|GetFolderPath\|GetTempPath\|SpecialFolder\|GetFullPath" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore` | no matches |
| Struct declarations are not duplicated | `rg -n "RuntimeContextResult\s*\{\|expo_dotnet_app_directories\s*\{" packages/expo-modules-dotnet` | exactly one definition of each, both in `native/include/expo_dotnet_host.h` |

Native builds for Windows and macOS are not runnable from a single machine.
Whichever platform cannot be built locally: say so explicitly in the handoff
rather than reporting the step as verified. That is a reporting requirement, not
an excuse to skip the code.

## Scope

**In scope**

Native ABI:
- NEW `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`
- `packages/expo-modules-dotnet/macos/ManagedLoader.h`, `macos/ManagedLoader.mm`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h`,
  `windows/ExpoModulesDotnet/ManagedLoader.cpp`

Platform adapters:
- `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.h`,
  `ExpoModulesDotnetInstaller.cpp`
- `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`

Managed core:
- NEW `.../Expo.ModulesCore/AppDirectories.cs`
- NEW `.../Expo.ModulesCore/AppDirectoryNotConfiguredException.cs`
- `.../Expo.ModulesCore/DotnetRuntimeContext.cs`

Codegen and generated artifacts:
- `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`
- `apps/desktop-app/.expo/dotnet/EntryPoints.g.cs`,
  `apps/mobile-app/.expo/dotnet/EntryPoints.g.cs` (regenerated, not hand-edited)

Other call sites and tests:
- `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs`
- `.../Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`
- `.../Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs`

Docs:
- NEW `docs/changes/<yyyy-mm-dd>-host-app-directories/spec.md`, `plan.md`
- `docs/specs/runtime-and-abi.md`, `docs/specs/modules-core-boundary.md`,
  `docs/specs/dotnet-autolinking.md`, `docs/specs/hermes-testhost.md`
- `docs/plans/README.md`

**Out of scope**

- App-group shared directories, upstream iOS's third directory concept
  (`AppContextConfig.appGroupSharedDirectories`). No consumer exists yet.
- Any filesystem operation in the managed core (Decision 4).
- Creating, cleaning, or lifetime-managing temp directories in
  `ExpoModuleTestHost`. This plan only lets a test *supply* a directory. Temp-dir
  fixtures belong to the consuming test, and the test-core design is owned by the
  Codex session recorded in `docs/plans/README.md`; do not extend the test host
  beyond a pass-through.
- Scoped/multi-app hosts, app groups, or Expo Go-style directory overriding.
- Renaming or reshaping `RuntimeContextError` / `RuntimeContextResult` beyond
  moving them verbatim (Decision 1's scope limit).
- Unifying the loaders' `char_t` divergence.
- Amending plan 022's own cache-root text. That happens when 022 is amended, and
  it must delete `docs/plans/022-expo-asset-dotnet.md:646-651` (including the
  Linux/XDG branch, defect D2) in favor of `context.CacheDirectory`.
- Exposing either directory to JavaScript. Nothing in this plan adds a `[JS]`
  member.

## Git workflow

Branch from the current work branch. One commit per step, each self-contained and
passing its own verification. Suggested messages:

1. `docs(specs): specify host-supplied app directories`
2. `docs(plans): plan host-supplied app directories`
3. `refactor(native): extract runtime context ABI into shared header`
4. `feat(native): add app directories to runtime context ABI`
5. `feat(modules-core): expose host-supplied app directories on runtime context`
6. `feat(autolinking): pass app directories through the generated aggregator`
7. `feat(windows, macos): supply app-scoped directories to managed core`
8. `test(modules-core): cover app directory configuration`
9. `docs(specs): merge host app directories delta`

Do not create a PR. Do not push without being asked.

## Steps

### Step 1: Delta spec and change plan

Write `docs/changes/<yyyy-mm-dd>-host-app-directories/spec.md` in the repo's
requirement/scenario style (GIVEN/WHEN/THEN, `SHALL`), covering:

- The ABI SHALL carry an app-directories struct into managed context creation,
  versioned and size-checked like `expo_jsi_api`.
- Directory strings SHALL be UTF-8, borrowed for the duration of the call.
- A null struct pointer, a null field pointer, or a zero length SHALL mean "not
  configured".
- `DotnetRuntimeContext` SHALL expose a cache directory and a persistent files
  directory; reading either when unconfigured SHALL throw
  `AppDirectoryNotConfiguredException`; reading either after disposal SHALL throw
  `ObjectDisposedException`.
- The two directories SHALL be independent; a host MAY supply one and not the
  other.
- The managed core SHALL NOT resolve, create, or probe directories.
- A supplied path SHALL be rejected at creation time when it is empty,
  whitespace, or not fully qualified.
- Windows and macOS adapters SHALL supply app-scoped paths; iOS, Android, and
  the dev console app SHALL pass "not configured" (Decision 7, with its
  rationale).

Also state, per `### Requirement: ABI Carries Only Host Knowledge`
(`docs/specs/runtime-and-abi.md`), which host-knowledge category the values fall
into and why portable .NET cannot answer it. The plan's "Why this justifies new
ABI surface" section has the argument; the delta spec needs it in requirement
form.

Then write `docs/changes/<yyyy-mm-dd>-host-app-directories/plan.md` mapping
requirements to Steps 2-8.

**Verify**: `scripts/format.sh --check --all` exits 0; no absolute local paths,
usernames, or machine names in either file. Commit them separately (spec, then
plan).

### Step 2: Extract the shared native header

Create `native/include/expo_dotnet_host.h` with `RuntimeContextError`,
`RuntimeContextResult`, both function-pointer typedefs, and
`EXPO_DOTNET_HOST_ABI_VERSION`, copied verbatim from
`macos/ManagedLoader.h:14-30`. Include it from `macos/ManagedLoader.h`,
`windows/ExpoModulesDotnet/ManagedLoader.h`, and
`ios/ExpoModulesDotnetInstaller.mm`, deleting the local redeclarations. Confirm
the iOS copy really was field-identical before deleting it; if it diverged in
field order or type, that is a live bug — report it and stop.

No behavior change in this step.

**Verify**: `rg -n "struct RuntimeContextResult" packages/expo-modules-dotnet`
returns exactly one definition. Build whichever native platform is available.

### Step 3: Add the struct and the parameter to the ABI

Add `expo_dotnet_app_directories` and the third parameter to
`CreateRuntimeContextFn` in the shared header, with comments stating UTF-8,
borrowed-for-the-call, and the null-means-unconfigured rule. Update the loaders'
resolve sites (`macos/ManagedLoader.mm:17-21`,
`windows/ManagedLoader.cpp:19-24`) if they name the signature. Update every
native call site to pass `nullptr` for now so the tree keeps building.

**Verify**: native build succeeds on the available platform; `git grep -c
createRuntimeContext` still finds every call site and each compiles.

### Step 4: Managed core

Add `AppDirectories`, `AppDirectoryNotConfiguredException`, and the internal
native decode. Add the two-argument constructor and both the `CacheDirectory` and
`PersistentFilesDirectory` accessors. The decode, applied per directory field:

1. Null struct pointer → `AppDirectories.Unconfigured`.
2. `version` mismatch → throw, message naming both values.
3. `size` smaller than the managed mirror → throw.
4. Null field pointer or zero length → leave that property null, and keep
   decoding the other field. One unconfigured directory must not discard a
   configured one.
5. Negative length → throw.
6. Decode strict UTF-8; invalid bytes throw.
7. Reject empty, whitespace-only, or not-fully-qualified paths.

Write the per-field logic once and apply it to both fields rather than
duplicating it; the fields differ only in name.

Constructor validation must reject a bad path the same way whether it came from
the ABI or from a direct managed caller — put the check in the `AppDirectories`
constructor or an explicit validation method used by both paths, not only in the
native decode.

**Verify**: `scripts/test-managed.sh` exits 0 with all 650 pre-existing tests
still passing. The filesystem-access check from the Commands table returns no
matches.

### Step 5: Codegen and generated artifacts

Update the template at `generateAggregator.ts:121-284`: the
`[UnmanagedCallersOnly]` signature gains the pointer parameter, and
`CreateRuntimeContextCore` calls the core's decode and passes the result to the
new constructor. Keep the generated code thin — decoding lives in
`Expo.ModulesCore` (the generated csproj compiles only two files,
`generateAggregator.ts:81-82`).

Extend `generateAggregator.test.ts` to assert the emitted signature and that the
context is constructed with the decoded directories. Regenerate both apps'
artifacts with the CLI, never by hand.

Update `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs` to keep
compiling; it passes unconfigured (Decision 7).

**Verify**: autolinking tests and typecheck exit 0. The regenerated `.g.cs` diffs
contain only the template change. `scripts/test-managed.sh` still exits 0.

### Step 6: Windows and macOS adapters supply the path

Implement Decision 8 in each installer, populate the struct in the frame that
makes the create call, and keep the string alive across it. Record the paths you
actually observed.

**STOP** if either resolves to a bare user-wide cache root.

**Verify**: build and run each platform's example app where possible; log or
otherwise confirm the resolved path is app-scoped. State plainly which platform
you could not build locally.

### Step 7: Tests

Add to `Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs`:

1. One-argument constructor → `CacheDirectory` throws
   `AppDirectoryNotConfiguredException`.
2. Two-argument constructor with `AppDirectories.Unconfigured` → same.
3. A fully qualified path → returned verbatim, no trailing-separator rewriting.
4. Empty path → rejected at construction.
5. Whitespace-only path → rejected at construction.
6. Relative path → rejected at construction.
7. Disposed context → `CacheDirectory` throws `ObjectDisposedException`, not
   `AppDirectoryNotConfiguredException`. This is the ordering guard: the disposal
   check must come first.
8. `AppDirectoryNotConfiguredException`'s message names which directory was
   missing, so a module author can tell the two apart.

Run cases 1 through 7 against `CacheDirectory` and against
`PersistentFilesDirectory`, parameterized rather than copy-pasted. Then the case
that only exists because there are two:

9. Cache directory supplied, persistent files directory not → `CacheDirectory`
   returns its path **and** `PersistentFilesDirectory` throws. And the reverse.
   This is Decision 5's independence rule; without it a decode that abandons the
   whole struct on the first null field would pass everything else.

Cases 4-6 encode Decision 3's intent: a host bug must surface at startup, not as
files silently written next to the process's working directory.

Add coverage for the native decode's version mismatch, undersized `size`, and
invalid UTF-8 if the decode is reachable from managed tests; if it is not, say so
rather than adding a test seam that only tests itself.

Add the `AppDirectories` parameter to `ExpoModuleTestHost.Create` as an optional
argument or an overload, keeping the existing single-argument call working, and
assert a supplied directory reaches the context inside `register`. Nothing more
(see Out of scope).

**Verify**: `scripts/test-managed.sh` exits 0; every new case fails when its
guard is reverted. Check at least cases 6, 7, and 9 that way — a validation test
that passes against unvalidated code is worthless.

### Step 8: Docs and merge

Merge the accepted delta into `docs/specs/`:

- `runtime-and-abi.md` — extend `### Requirement: Managed Runtime Lifecycle Entry
  Points` (`:390`) with the new parameter; add the struct to the version/size
  requirement at `:245`; confirm the UTF-8 contract at `:223` covers the new
  fields.
- `modules-core-boundary.md` — a requirement under `### Requirement:
  Runtime-Scoped Dotnet Runtime Contexts` (`:1216`) for the host-supplied
  directory, its unconfigured behavior, and the no-filesystem-access rule.
- `dotnet-autolinking.md` — the aggregator's new parameter and marshalling
  responsibility.
- `hermes-testhost.md` — the test host's new pass-through.
- `docs/plans/README.md` — mark 027 done; update 022's row to depend on 026 only,
  and move the cache-root defect note from "blocked on 027" to "amend 022 to use
  `context.CacheDirectory`".

Archive the delta directory per the living-spec workflow.

**Verify**: `scripts/format.sh --check --all` and `git diff --check` are clean.
No local absolute paths, usernames, or machine names anywhere in the staged diff.

## Done criteria

1. `expo_dotnet_app_directories` and the runtime-context typedefs are declared
   exactly once, in `native/include/expo_dotnet_host.h`.
2. `CreateRuntimeContextFn` carries the struct pointer, and all six native call
   sites compile against it.
3. `DotnetRuntimeContext.CacheDirectory` and `.PersistentFilesDirectory` each
   return the host-supplied path, throw `AppDirectoryNotConfiguredException` when
   unconfigured, and throw `ObjectDisposedException` after disposal. Either can
   be unconfigured while the other is set.
4. `rg "Directory\.\|File\.\|GetFolderPath\|GetTempPath\|SpecialFolder"
   packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore` returns no
   matches.
5. Empty, whitespace-only, and relative paths are rejected at construction, for
   both directories.
6. The codegen template emits the new signature; both apps' `EntryPoints.g.cs`
   are regenerated from it and differ only by that change.
7. Windows and macOS installers supply both app-scoped paths, and all four
   observed paths are recorded in the delta spec. Any platform not built locally
   is named as such.
8. `ExpoModuleTestHost.Create` can supply directories without breaking existing
   callers.
9. `scripts/test-managed.sh`, `pnpm --filter expo-modules-dotnet-autolinking test`,
   `pnpm --filter expo-modules-dotnet-autolinking typecheck`,
   `scripts/format.sh --check --all`, and `git diff --check` all pass.
10. `docs/specs/` carries the merged requirements and `docs/plans/README.md` is
    updated.

## STOP conditions

- The iOS local struct declarations differ from `ManagedLoader.h` in field order
  or type. That is a live memory-safety bug that predates this plan; report it
  before touching anything.
- Any of the four resolved platform paths is a bare user-wide root.
- Any pre-existing test in the 650-test suite fails.
- The change appears to require a filesystem call inside `Expo.ModulesCore`.
  Re-read Decision 4; if it still seems necessary, stop and report rather than
  adding one.
- Making the ABI change requires editing a generated `.g.cs` by hand.
- A test needs a real writable directory to pass. That is 022's problem, not
  this plan's; this plan's tests only need path strings.

## Maintenance notes

- The whole runtime-context ABI lived in duplicated per-platform headers before
  this plan, and `ios/ExpoModulesDotnetInstaller.mm` had already drifted into an
  older shape. Step 2 removes that hazard. If a future platform adapter
  reintroduces a local redeclaration instead of including the shared header, the
  same silent-corruption risk comes back.
- Strict version equality (matching `ExpoJsiApi.ExpectedVersion`) is safe only
  because the loader and generated host are built together per app. If loaders
  ever ship independently of generated hosts, this becomes a real compatibility
  problem and `size`-based tolerant parsing would be needed instead.
- Upstream also exposes app-group shared directories on iOS
  (`AppContextConfig.appGroupSharedDirectories`) and uses them to validate
  filesystem permissions (`FileSystemUtilities.swift:86`). Nothing here needs it
  yet; it is the natural third field.
- Plan 022 currently describes appending an `ExponentAsset` subdirectory
  (`docs/plans/022-expo-asset-dotnet.md:785`) while also saying the file is
  written directly into the cache root (`:298`). Upstream writes
  `ExponentAsset-<id>.<type>` directly into the cache directory
  (`expo/packages/expo-asset/android/src/main/java/expo/modules/asset/AssetModule.kt:93`,
  `ios/AssetModule.swift:35`). Resolve that when 022 is amended, not here.
- If a module ever needs to degrade gracefully instead of throwing, add a
  `TryGetCacheDirectory` rather than making `CacheDirectory` nullable — the
  throwing accessor is what makes module code read like upstream Android's.
