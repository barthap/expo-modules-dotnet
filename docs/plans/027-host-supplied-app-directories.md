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
> git diff --stat 512ab46e..HEAD -- \
>   packages/expo-modules-dotnet/native/include \
>   packages/expo-modules-dotnet/macos \
>   packages/expo-modules-dotnet/windows \
>   packages/expo-modules-dotnet/ios \
>   packages/expo-modules-dotnet/android \
>   packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore \
>   packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing \
>   packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests \
>   packages/expo-modules-dotnet-autolinking/src/codegen \
>   packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts \
>   packages/expo-modules-dotnet-autolinking/src/__tests__/fixtures/entry-points-abi-harness.cs \
>   docs/changes/2026-07-25-host-app-directories \
>   docs/archive/changes/2026-07-25-host-app-directories \
>   docs/specs \
>   docs/plans/README.md
> ```
> If the runtime-context entry-point signature, `RuntimeContextResult`, the
> aggregator codegen template, or `DotnetRuntimeContext`'s constructor changed,
> compare the live code against the excerpts in "Current state" before
> proceeding. A mismatch is a STOP condition.

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: MED-HIGH (changes a private native↔managed ABI signature used by
  four native adapters; a mismatched signature or struct layout corrupts memory
  instead of failing to compile)
- **Depends on**: none
- **Blocks**: `docs/plans/022-expo-asset-dotnet.md`. Also unblocks plan 024
  (local filesystem) with no further ABI work, because Decision 5 ships the
  persistent files directory too.
- **Category**: core capability
- **Planned at**: `512ab46e`, 2026-07-25

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
- **iOS** — `AppContextConfig` accepts host-injected directory URLs and falls
  back to `FileManager`'s platform directories when either argument is nil
  (`expo/packages/expo-modules-core/ios/Core/AppContextConfig.swift:3-14`).
  Separately, `expo-asset` reads the legacy
  `appContext.fileSystem.cachesDirectory` and rejects the operation if that
  service is absent
  (`expo/packages/expo-asset/ios/AssetModule.swift:27-34`).

The common upstream rule is that the platform host resolves the path; module
logic does not reconstruct it from process-wide environment heuristics. This
plan gives `Expo.ModulesCore` the same ownership boundary. Its decision to throw
when a host supplies no path is repo-specific, matching Android's missing-service
behavior and preventing a portable fallback to the user-wide paths that caused
this defect. The operator's framing — "Cache dir should be exposed by
`DotnetRuntimeContext`, following upstream `AppContext` purpose" — settles the
owner; `DotnetRuntimeContext` already documents itself as this repo's narrow
equivalent of upstream's `AppContext` (`DotnetRuntimeContext.cs:5-28`).

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

`packages/expo-modules-dotnet/macos/ManagedLoader.h:14-30` (relevant shape; the
Windows copy at
`packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h:14-30`
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
does not contain them. There are four native copies today:

1. `packages/expo-modules-dotnet/macos/ManagedLoader.h:14-30`
2. `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h:14-30`
3. `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm:20-35`
4. `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp:15-31`

The mobile copies are field-identical but live in anonymous namespaces and do
not include either `ManagedLoader.h`.

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

Normative in `docs/specs/runtime-and-abi.md:264-268`: "The ABI SHALL represent
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
(`docs/specs/runtime-and-abi.md:286`).

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

### Related construction and ABI sites

| Call site | File:line |
| --- | --- |
| macOS loader typedef + resolve | `packages/expo-modules-dotnet/macos/ManagedLoader.h:27-30`, `macos/ManagedLoader.mm:17-21,199` |
| Windows loader typedef + resolve | `windows/ExpoModulesDotnet/ManagedLoader.h:27-30`, `ManagedLoader.cpp:19-24` |
| macOS installer invocation | `macos/ExpoModulesDotnetInstaller.mm:115-116`, factory at `:172-182` |
| Windows installer invocation | `windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp:103`, `registerModules` at `:87-117`, `Initialize` at `:211-274` |
| iOS installer (local declarations) | `ios/ExpoModulesDotnetInstaller.mm:20-34`, call at `:165` |
| Android bindings installer | `android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp` |
| Codegen template | `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts:121-284` |
| Ignored generated verification outputs | `apps/desktop-app/.expo/dotnet/EntryPoints.g.cs`, `apps/mobile-app/.expo/dotnet/EntryPoints.g.cs` |
| Dev console app (its own entry points) | `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs:11-41,100` |
| Test host | `Expo.ModulesCore.Testing/ExpoModuleTestHost.cs:38-78` |

The four native invocations and generated export must change atomically. The
create symbol and HostFXR method name must also change: reusing the old names
would let a stale native adapter call a generated host through the wrong
function-pointer signature before any struct version check could run. The dev
console uses a separate export and keeps compiling through the preserved
one-argument `DotnetRuntimeContext` constructor.

The generated host csproj template compiles exactly two files
(`generateAggregator.ts:81-82`). The public `AppDirectories` model therefore
belongs in `Expo.ModulesCore`, while the private native mirror and pointer
decoding belong in the already-generated `EntryPoints.g.cs`, beside
`RuntimeContextResult` and `RuntimeContextError`.

## Decisions

These are settled by upstream behavior, by the repo's own conventions, or by the
constraint in `AGENTS.md`. Decisions 1 and 5 were put to the operator on
2026-07-25 and are recorded here as answered; the rest need no confirmation.

### Decision 1 — extract the runtime-context ABI into one shared header

**Approved by the operator, 2026-07-25.** Do this instead of adding a struct to
each of the four duplicated declarations. Create
`packages/expo-modules-dotnet/native/include/expo_dotnet_host.h` holding
`RuntimeContextError`, `RuntimeContextResult`, the new app-directories struct,
the function-pointer typedefs, and the version constant. Have
`macos/ManagedLoader.h`, `windows/ExpoModulesDotnet/ManagedLoader.h`, and
`ios/ExpoModulesDotnetInstaller.mm` include it instead of redeclaring. Android's
`ExpoModulesDotnetBindingsInstaller.cpp` is the fourth native copy and SHALL
include the same header too.

Rationale: everything crosses through a function pointer, so drift between copies
is undetectable at compile time and corrupts memory at runtime. Adding a struct to
four hand-maintained copies converts a latent smell into an active hazard. This
is a prerequisite for the change, not an unrelated cleanup.

Scope limit: move the declarations verbatim into
`namespace expo::modules::dotnet`, update all four includes, and qualify the
mobile uses that currently live in anonymous namespaces. Do not rename types,
do not change the existing field order, and do not touch the loader's
platform-divergent `char_t`/`std::wstring` config plumbing
(`macos/ManagedHostFxr.h:8` uses `char`, `windows/ManagedHostFxr.h:8` uses
`wchar_t`) — that divergence is in loader-private code and is out of scope.

The alternative that was rejected: add the struct to all four copies plus a
compile-time `static_assert` on `sizeof` in each. That catches size drift but not
field-order drift, which is the corrupting kind.

### Decision 2 — the directory arrives at context creation, not through a setter

`LinkedExpoModulesProvider.Register(context)` runs inside
`CreateRuntimeContextCore` (`EntryPoints.g.cs:62`), so a module constructor can
observe the context before any post-creation setter would run. A setter creates a
window where the cache directory is silently absent. Pass it as an argument to
the create entry point.

Rename the NativeAOT symbol to
`expo_dotnet_create_runtime_context_result_v2` and the HostFXR method to
`CreateRuntimeContextResultV2`; rename the native typedef and entry-point field
to `CreateRuntimeContextV2Fn` and `createRuntimeContextV2`. Do not leave an
alias under the old create name. An old adapter/new host or new adapter/old host
pair must fail symbol or method resolution before invocation. The version inside
`expo_dotnet_app_directories` protects the contents of an already signature-safe
call; it cannot protect a call made through the wrong function type.

### Decision 3 — an unconfigured directory throws; the core never falls back

`Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` and
`Path.GetTempPath()` are user-wide or process-wide, which is the exact defect
being fixed, so there is no acceptable fallback inside portable code. Android
throws when `AppDirectoriesService` is not registered. iOS resolves defaults in
its platform config, while its asset module rejects the promise if the legacy
filesystem service is absent. Neither asks portable module logic to derive the
path.

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
input. Make the record immutable after construction so a caller cannot change
the context's directory policy after module registration. The private ABI mirror
and decoding stay in the generated host entry point; the public record owns
platform-neutral string validation. This is the only abstraction this plan adds.

### Decision 7 — which adapters supply a real path

- **The packaged Windows example and the macOS example SHALL supply real
  app-scoped paths.** They are plan 022's target hosts.
- **An unpackaged Windows process with no app-model identity SHALL pass
  "unconfigured".** An executable-name fallback is not a stable app identity:
  two unrelated apps can have the same executable name. Adding such a fallback
  would reproduce the collision this plan exists to remove.
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
`winrt::Microsoft::ReactNative::ReactContext` at `:211-274`) uses
`winrt::Windows::Storage::ApplicationData::Current()`:

| Directory | Packaged path source |
| --- | --- |
| cache | `ApplicationData::Current().LocalCacheFolder().Path()` |
| persistent files | `ApplicationData::Current().LocalFolder().Path()` |

Include `<winrt/Windows.Storage.h>` explicitly. `ApplicationData::Current()`
throws when no package identity exists, so catch `winrt::hresult_error`, log that
the unpackaged host did not provide an app identity, and pass "unconfigured" for
both fields. Do not fall back to `%LOCALAPPDATA%` plus an executable stem.

**macOS** (`ExpoModulesDotnetInstaller.mm`, factory at `:172-182`), each resolved
with `-[NSFileManager URLsForDirectory:inDomains:]` in `NSUserDomainMask` and
then `[[NSBundle mainBundle] bundleIdentifier]` appended using
`URLByAppendingPathComponent:isDirectory:`:

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

Verify the actual resolved paths on both platforms during Step 5. Record only
sanitized path shapes in committed artifacts, for example
`<user-home>/Library/Caches/<bundle-id>` and
`<local-app-data>/<package-family>/LocalCache`; never commit a real user profile,
machine path, or package identity. If any path resolves to a bare user-wide
root, that is a STOP condition — it is the defect this plan exists to remove.
After the adapter has checked both values, emit one durable marker without raw
paths or identities:
`[ExpoModulesDotnet] App directories configured: cache=app-scoped, persistent=app-scoped.`
Runtime verification must require this marker and the existing C# result `42`.

## Proposed shape

Native, in the new shared header:

```c
#define EXPO_DOTNET_HOST_ABI_VERSION 1

typedef struct expo_dotnet_app_directories {
  uint32_t size;     // sizeof(expo_dotnet_app_directories)
  uint32_t version;  // EXPO_DOTNET_HOST_ABI_VERSION

  // All strings: UTF-8, not NUL-terminated. Borrowed — valid only for the
  // duration of the create call. A null pointer paired with zero length means
  // "not configured", and each directory is independent of the other. Any
  // other pointer/length mismatch is invalid.

  // Temporary files the operating system may remove at any time.
  const uint8_t *cache_directory;
  int32_t cache_directory_length;

  // App files that must survive OS cache eviction.
  const uint8_t *persistent_files_directory;
  int32_t persistent_files_directory_length;
} expo_dotnet_app_directories;

using CreateRuntimeContextV2Fn = void (*)(const expo_jsi_api *,
                                          expo_jsi_runtime_handle,
                                          const expo_dotnet_app_directories *,
                                          RuntimeContextResult *);
```

The generated NativeAOT export is
`expo_dotnet_create_runtime_context_result_v2`, backed by the HostFXR method
`CreateRuntimeContextResultV2`. Neither loader probes the old create name as a
fallback. Teardown keeps its current name because its signature does not change.

Include `<cstddef>` and `<type_traits>` and lock the native layout:

```cpp
static_assert(sizeof(void *) == 4 || sizeof(void *) == 8);
static_assert(std::is_standard_layout_v<expo_dotnet_app_directories>);
static_assert(offsetof(expo_dotnet_app_directories, size) == 0);
static_assert(offsetof(expo_dotnet_app_directories, version) == 4);
static_assert(offsetof(expo_dotnet_app_directories, cache_directory) == 8);
static_assert(offsetof(expo_dotnet_app_directories, cache_directory_length) ==
              8 + sizeof(void *));
static_assert(offsetof(expo_dotnet_app_directories, persistent_files_directory) ==
              (sizeof(void *) == 8 ? 24 : 16));
static_assert(offsetof(expo_dotnet_app_directories, persistent_files_directory_length) ==
              (sizeof(void *) == 8 ? 32 : 20));
static_assert(sizeof(expo_dotnet_app_directories) ==
              (sizeof(void *) == 8 ? 40 : 24));
```

Borrowed-for-the-call ownership is the right convention here (matching
`expo_jsi_create_string_fn`) because the lifetime is bounded by the call: managed
code decodes to a `string` before returning, so no release callback is needed.
The whole struct pointer may be null, which means both directories are
unconfigured. At field level, only `(nullptr, 0)` means unconfigured. A non-null
pointer with zero length is a supplied empty string and fails normal path
validation; a null pointer with a nonzero length is an invalid ABI pair. This
distinction prevents an empty host value from silently becoming "not
configured".

Managed, in `Expo.ModulesCore`:

```csharp
public sealed record AppDirectories
{
  public static AppDirectories Unconfigured { get; } = new(null, null);

  public AppDirectories(
      string? cacheDirectory = null,
      string? persistentFilesDirectory = null
  )
  {
    CacheDirectory = Validate(cacheDirectory, nameof(cacheDirectory));
    PersistentFilesDirectory = Validate(
        persistentFilesDirectory,
        nameof(persistentFilesDirectory)
    );
  }

  /// <summary>Fully qualified app-scoped cache directory, or null if unconfigured.</summary>
  public string? CacheDirectory { get; }

  /// <summary>Fully qualified app-scoped persistent directory, or null if unconfigured.</summary>
  public string? PersistentFilesDirectory { get; }
}
```

and on the context:

```csharp
private readonly AppDirectories appDirectories;

public DotnetRuntimeContext(JavaScriptRuntime runtimeArgument)
    : this(runtimeArgument, AppDirectories.Unconfigured) { }

public DotnetRuntimeContext(JavaScriptRuntime runtimeArgument, AppDirectories directories)
{
  runtime = runtimeArgument ?? throw new ArgumentNullException(nameof(runtimeArgument));
  appDirectories = directories ?? throw new ArgumentNullException(nameof(directories));
  // Preserve the existing object/registry initialization unchanged.
}

/// <summary>
/// A directory for temporary files the operating system may remove at any time.
/// </summary>
/// <exception cref="AppDirectoryNotConfiguredException">The host supplied no cache directory.</exception>
public string CacheDirectory
{
  get
  {
    lock (gate)
    {
      ThrowIfNotActiveLocked();
      return appDirectories.CacheDirectory
          ?? throw new AppDirectoryNotConfiguredException(nameof(CacheDirectory));
    }
  }
}

/// <summary>
/// A directory for app files that must survive cache eviction.
/// </summary>
/// <exception cref="AppDirectoryNotConfiguredException">The host supplied no persistent files directory.</exception>
public string PersistentFilesDirectory
{
  get
  {
    lock (gate)
    {
      ThrowIfNotActiveLocked();
      return appDirectories.PersistentFilesDirectory
          ?? throw new AppDirectoryNotConfiguredException(nameof(PersistentFilesDirectory));
    }
  }
}
```

`Validate` rejects empty, whitespace-only, NUL-containing, and
non-fully-qualified values without probing the filesystem. Both the direct
managed constructor and ABI-decoded strings flow through this helper:

```csharp
private static string? Validate(string? value, string parameterName)
{
  if (value is null)
  {
    return null;
  }
  ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
  if (value.IndexOf('\0') >= 0)
  {
    throw new ArgumentException("App directory paths cannot contain NUL.", parameterName);
  }
  if (!Path.IsPathFullyQualified(value))
  {
    throw new ArgumentException(
        "App directory paths must be fully qualified.",
        parameterName
    );
  }
  return value;
}
```

`AppDirectoryNotConfiguredException` has this shape:

```csharp
public sealed class AppDirectoryNotConfiguredException : InvalidOperationException
{
  internal AppDirectoryNotConfiguredException(string directoryProperty)
      : base($"The host did not configure {directoryProperty}.")
  {
    DirectoryProperty = directoryProperty;
  }

  public string DirectoryProperty { get; }
}
```

Module authors can catch the public type without being able to fabricate
framework exceptions.

The generated `EntryPoints.g.cs` owns a private sequential-layout native mirror
and strict UTF-8 decoding. Its public unmanaged
`CreateRuntimeContextResultV2` entry point receives the native struct pointer as
`nint`, so a private mirror does not leak into the generated host's public API.
It decodes before constructing `DotnetRuntimeContext`, then passes the immutable
`AppDirectories` instance into the two-argument constructor.

The generated decoder follows this structure; field names must match the native
header:

```csharp
private const uint ExpectedHostAbiVersion = 1;
private static readonly UTF8Encoding StrictUtf8 = new(false, true);

[StructLayout(LayoutKind.Sequential)]
private unsafe struct NativeAppDirectories
{
  public uint Size;
  public uint Version;
  public byte* CacheDirectory;
  public int CacheDirectoryLength;
  public byte* PersistentFilesDirectory;
  public int PersistentFilesDirectoryLength;
}

private static unsafe AppDirectories DecodeAppDirectories(nint pointer)
{
  if (pointer == 0)
  {
    return AppDirectories.Unconfigured;
  }

  var native = (NativeAppDirectories*)pointer;
  var expectedSize = (uint)sizeof(NativeAppDirectories);
  if (native->Size < expectedSize)
  {
    throw new InvalidOperationException(
        $"Expo .NET host app-directories struct is too small. Expected at least {expectedSize}, got {native->Size}."
    );
  }
  if (native->Version != ExpectedHostAbiVersion)
  {
    throw new InvalidOperationException(
        $"Expo .NET host ABI version mismatch: native={native->Version} managed={ExpectedHostAbiVersion}."
    );
  }

  return new AppDirectories(
      DecodeDirectory(native->CacheDirectory, native->CacheDirectoryLength, "cache_directory"),
      DecodeDirectory(
          native->PersistentFilesDirectory,
          native->PersistentFilesDirectoryLength,
          "persistent_files_directory"
      )
  );
}

private static unsafe string? DecodeDirectory(byte* data, int length, string fieldName)
{
  if (length < 0)
  {
    throw new InvalidOperationException($"{fieldName} has a negative byte length.");
  }
  if (data == null)
  {
    if (length == 0)
    {
      return null;
    }
    throw new InvalidOperationException($"{fieldName} has a byte length but no data.");
  }
  return StrictUtf8.GetString(new ReadOnlySpan<byte>(data, length));
}
```

The codegen test also compiles and runs the generated host. Keep a checked-in
fixture at
`src/__tests__/fixtures/entry-points-abi-harness.cs`; the Vitest case generates
against the real repo `Expo.JSI` and `Expo.ModulesCore` projects in a temporary
directory, adds the fixture as a compile item, and invokes `dotnet run` on the
temporary host project. Emit `EntryPoints` as a `partial` class so the fixture,
compiled into the same assembly, can call the private decoder without widening
production visibility. The fixture SHALL:

1. call `CreateRuntimeContextResultV2` through its
   `delegate* unmanaged[Cdecl]` function pointer for each invalid input and
   verify the structured error result and release callback;
2. call the private decoder for valid inputs and verify both strings reach
   `AppDirectories` independently;
3. assert managed `sizeof` and field offsets for the process pointer width; and
4. cover null struct, undersized size, wrong version, negative length,
   null/nonzero, non-null/zero, invalid UTF-8, and valid UTF-8.

The Android build compiles the shared native layout assertions for its configured
32-bit and 64-bit ABIs (`armeabi-v7a`, `arm64-v8a`, `x86`, and `x86_64`).
Together, the executable managed harness and native target builds check the
actual layouts instead of only matching generated source text.

Keeping the one-argument constructor means existing tests and callers keep
compiling; it now reads as "no directories configured", which is exactly true.

## Conventions

- Two-space indentation, file-scoped namespaces, `///` XML docs on public
  members — match `DotnetRuntimeContext.cs`.
- Every new public accessor on the context takes `lock (gate)` and calls
  `ThrowIfNotActiveLocked()` first, like `Runtime` (`:68-78`). A disposed context
  must throw `ObjectDisposedException` from `CacheDirectory` too.
- Decode UTF-8 in the generated `EntryPoints.g.cs` with
  `new UTF8Encoding(false, throwOnInvalidBytes: true)`, matching
  `ExpoJsiApi.cs:346-349`. Do **not** add an `InternalsVisibleTo` edge to reuse
  the `Expo.JSI` instance; emit one private static decoder beside the generated
  native mirror.
- Validate with `Path.IsPathFullyQualified`, which is a pure string check with no
  disk access. Do not call `Path.GetFullPath`, `Directory.Exists`, or anything
  else that touches the filesystem.
- Keep the native mirror sequential and in this exact field order: `Size`,
  `Version`, cache pointer/length, persistent pointer/length. The shared C++
  header SHALL contain `static_assert` checks for standard layout, field offsets,
  and total size on 32-bit and 64-bit pointer targets. The generated decoder
  SHALL check `Size` before `Version` and before reading either pointer field.
- ABI mismatch messages follow the existing format: include both values, as in
  `"Expo JSI ABI version mismatch: native={0} managed={1}."`
- C++ files follow the surrounding installer style; run `scripts/format.sh` and
  let it decide formatting.

## Commands you will need

| Purpose | Command | Expected on success |
| --- | --- | --- |
| Full managed regression | `scripts/test-managed.sh` | exit 0; all pre-existing and new tests pass |
| ModulesCore runtime tests only | `scripts/test-managed.sh --project packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Expo.ModulesCore.Tests.csproj` | exit 0 |
| Autolinking codegen and compiled ABI tests | `pnpm --filter expo-modules-dotnet-autolinking test` | exit 0; generated source assertions and the executable ABI harness pass |
| Autolinking typecheck | `pnpm --filter expo-modules-dotnet-autolinking typecheck` | exit 0 |
| Autolinking build | `pnpm --filter expo-modules-dotnet-autolinking build` | exit 0 |
| Generate desktop host | `pnpm --dir apps/desktop-app exec expo-modules-dotnet-autolinking generate` | exit 0; three ignored files written or skipped under `apps/desktop-app/.expo/dotnet` |
| Generate mobile host | `pnpm --dir apps/mobile-app exec expo-modules-dotnet-autolinking generate` | exit 0; three ignored files written or skipped under `apps/mobile-app/.expo/dotnet` |
| Compare generated entry points | `cmp apps/desktop-app/.expo/dotnet/EntryPoints.g.cs apps/mobile-app/.expo/dotnet/EntryPoints.g.cs` | exit 0, no output |
| Android adapter build | `apps/mobile-app/android/gradlew -p apps/mobile-app/android :app:assembleDebug --console=plain` | exit 0 |
| Android runtime | `pnpm --dir apps/mobile-app exec expo run:android` | app launches; Metro reports the C# example result `42` |
| iOS runtime | `pnpm --dir apps/mobile-app exec expo run:ios --device 19046C77-3797-4356-97D2-B372A3F01383` | app launches; Metro reports the C# example result `42` |
| Apple adapter builds | commands below | each exits 0 through `pipefail` |
| macOS Metro | `pnpm --filter desktop-app start -- --localhost` | Metro waits for the two runtime launches |
| macOS HostFXR runtime | `EXPO_DOTNET_LOADER=hostfxr apps/desktop-app/macos/build/Build/Products/Debug/desktopapp.app/Contents/MacOS/desktopapp` | app logs the app-scoped directory marker and Metro reports `42` |
| macOS NativeAOT runtime | `EXPO_DOTNET_LOADER=nativeaot apps/desktop-app/macos/build-nativeaot/Build/Products/Debug/desktopapp.app/Contents/MacOS/desktopapp` | app logs the app-scoped directory marker and Metro reports `42` |
| Windows HostFXR build | `MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Debug /p:Platform=x64 /m:1` | exit 0 |
| Windows NativeAOT build | `MSBuild.exe apps/desktop-app/windows/DesktopApp.sln /restore /p:Configuration=Release /p:Platform=x64 /p:ExpoDotnetLoader=nativeaot /m:1` | exit 0 |
| Windows HostFXR runtime | `pnpm --filter desktop-app windows` | packaged app launches and reports the C# example result `42` |
| Windows NativeAOT runtime | `pnpm --filter desktop-app exec react-native run-windows --release --msbuildprops ExpoDotnetLoader=nativeaot` | packaged app launches and reports the C# example result `42` |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Whitespace | `git diff --check` | no output |
| No filesystem access in the core | `rg -n -e "Directory\\." -e "File\\." -e "GetFolderPath" -e "GetTempPath" -e "SpecialFolder" -e "GetFullPath" packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore` | no matches |
| Native declarations are not duplicated | `rg -n -e "struct RuntimeContext" -e "using CreateRuntimeContextV2Fn" -e "struct expo_dotnet_app_directories" packages/expo-modules-dotnet/native/include packages/expo-modules-dotnet/macos packages/expo-modules-dotnet/windows packages/expo-modules-dotnet/ios packages/expo-modules-dotnet/android` | exactly four matches, all in `native/include/expo_dotnet_host.h` |
| Old create name removed | `rg -n "expo_dotnet_create_runtime_context_result\\b|CreateRuntimeContextResult\\b|CreateRuntimeContextFn\\b|createRuntimeContext\\b" packages/expo-modules-dotnet packages/expo-modules-dotnet-autolinking/src` | no matches outside historical test data that deliberately builds a stale pairing |

Use the repo's `xcodebuildmcp-cli` skill for Apple builds when available; the
CLI fallbacks are:

```sh
set -o pipefail
xcodebuild \
  -workspace apps/mobile-app/ios/mobileapp.xcworkspace \
  -scheme mobileapp \
  -configuration Debug \
  -destination 'platform=iOS Simulator,id=19046C77-3797-4356-97D2-B372A3F01383' \
  build 2>&1 | xcsift -f toon
```

```sh
set -o pipefail
xcodebuild \
  -workspace apps/desktop-app/macos/desktopapp.xcworkspace \
  -scheme desktopapp-macOS \
  -configuration Debug \
  -derivedDataPath apps/desktop-app/macos/build \
  build 2>&1 | xcsift -f toon
```

```sh
set -o pipefail
xcodebuild \
  -workspace apps/desktop-app/macos/desktopapp.xcworkspace \
  -scheme desktopapp-macOS \
  -configuration Debug \
  -derivedDataPath apps/desktop-app/macos/build-nativeaot \
  EXPO_DOTNET_LOADER=nativeaot \
  build 2>&1 | xcsift -f toon
```

Windows and Apple native builds are not runnable from one machine. A first
executor may hand off a clean implementation as **awaiting platform
verification**, but plan 027 stays TODO/BLOCKED until all four adapter builds
pass and the packaged Windows and macOS apps report sanitized app-scoped path
shapes. Do not mark it DONE based only on the local platform.

## Scope

**In scope**

Native ABI:
- NEW `packages/expo-modules-dotnet/native/include/expo_dotnet_host.h`
- `packages/expo-modules-dotnet/macos/ManagedLoader.h`
- `packages/expo-modules-dotnet/macos/ManagedLoader.mm`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.h`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ManagedLoader.cpp`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnet.vcxproj`
- `packages/expo-modules-dotnet/ios/ExpoModulesDotnetInstaller.mm`
- `packages/expo-modules-dotnet/android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`

Directory-providing platform adapters:
- `packages/expo-modules-dotnet/macos/ExpoModulesDotnetInstaller.mm`
- `packages/expo-modules-dotnet/windows/ExpoModulesDotnet/ExpoModulesDotnetInstaller.cpp`

Managed core:
- NEW `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/AppDirectories.cs`
- NEW `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/AppDirectoryNotConfiguredException.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore/DotnetRuntimeContext.cs`

Codegen:
- `packages/expo-modules-dotnet-autolinking/src/codegen/generateAggregator.ts`
- `packages/expo-modules-dotnet-autolinking/src/__tests__/generateAggregator.test.ts`
- NEW `packages/expo-modules-dotnet-autolinking/src/__tests__/fixtures/entry-points-abi-harness.cs`

Test host and tests:
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Testing/ExpoModuleTestHost.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs`
- `packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`

Docs:
- NEW, transient `docs/changes/2026-07-25-host-app-directories/spec.md`
- NEW, transient `docs/changes/2026-07-25-host-app-directories/plan.md`
- archive destinations
  `docs/archive/changes/2026-07-25-host-app-directories/spec.md` and
  `docs/archive/changes/2026-07-25-host-app-directories/plan.md`
- `docs/specs/runtime-and-abi.md`, `docs/specs/modules-core-boundary.md`,
  `docs/specs/dotnet-autolinking.md`, `docs/specs/hermes-testhost.md`
- `docs/plans/README.md`

Ignored verification outputs, regenerate but do not stage or hand-edit:
- `apps/desktop-app/.expo/dotnet/EntryPoints.g.cs`
- `apps/mobile-app/.expo/dotnet/EntryPoints.g.cs`

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
- `ExpoModulesDotnet.podspec` and Android `CMakeLists.txt`: their existing
  native-header glob and `native/include` search path already pick up the new
  header.
- `apps/hermes-console-app/managed/HermesConsoleApp/EntryPoints.cs`: it uses a
  separate entry point and the preserved one-argument context constructor.
- Unifying the loaders' `char_t` divergence.
- Amending plan 022's own cache-root text. That happens when 022 is amended, and
  it must delete `docs/plans/022-expo-asset-dotnet.md:646-651` (including the
  Linux/XDG branch, defect D2) in favor of `context.CacheDirectory`.
- Exposing either directory to JavaScript. Nothing in this plan adds a `[JS]`
  member.

## Git workflow

Use a normal branch named `codex/027-host-app-directories` from the latest
integration branch that contains commit `512ab46e` or its merged equivalent.
Do not branch from an unrelated dirty feature branch, and do not use a worktree.
Commit only after each task's verification passes. Suggested messages:

1. `docs(specs): specify host-supplied app directories`
2. `docs(plans): plan host-supplied app directories`
3. `refactor(native): share runtime context host ABI`
4. `feat(modules-core): expose host-supplied app directories`
5. `feat(autolinking): pass app directories through the host ABI`
6. `feat(windows, macos): supply app-scoped directories`
7. `docs(specs): merge host app directory requirements`

Do not create a PR. Do not push without being asked.

## Steps

### Step 1: Approve and commit the delta spec, then the change plan

Write `docs/changes/2026-07-25-host-app-directories/spec.md` in the repo's
requirement/scenario style (GIVEN/WHEN/THEN, `SHALL`), covering:

- The ABI SHALL carry a versioned, size-checked app-directories struct into
  managed context creation.
- The signature-changing create entry point SHALL use the versioned NativeAOT
  symbol `expo_dotnet_create_runtime_context_result_v2` and HostFXR method
  `CreateRuntimeContextResultV2`; stale old/new pairings SHALL fail resolution
  without invocation.
- Directory strings SHALL be strict UTF-8 borrowed for the create call.
- A null struct pointer SHALL mean both directories are unconfigured.
- At field level, `(null, 0)` SHALL mean unconfigured; `(null, nonzero)` SHALL be
  rejected; `(non-null, 0)` SHALL decode as an empty supplied string and fail
  path validation.
- `DotnetRuntimeContext` SHALL expose independent cache and persistent-files
  directories, throw `AppDirectoryNotConfiguredException` for an unconfigured
  value, and throw `ObjectDisposedException` after disposal.
- The managed core SHALL NOT resolve, create, canonicalize, or probe
  directories. It SHALL reject empty, whitespace-only, NUL-containing, and
  non-fully-qualified supplied paths.
- The packaged Windows and macOS examples SHALL supply app-scoped paths.
  Unpackaged Windows without app-model identity, iOS, Android, and the dev
  console app SHALL be unconfigured, with Decision 7's rationale.
- The generated host SHALL own pointer and UTF-8 decoding;
  `Expo.ModulesCore` SHALL own the immutable public model and platform-neutral
  validation.
- Generated-host verification SHALL execute the unmanaged failure boundary and
  valid decoder inputs from a compiled harness, not only inspect emitted text.

State which host-knowledge categories apply and why portable .NET cannot answer
them, as required by `### Requirement: ABI Carries Only Host Knowledge`.

Run `git diff --check` and the committed-path privacy scan. Present the delta
spec to the operator and STOP until it is approved. Commit the approved spec
alone. Then write
`docs/changes/2026-07-25-host-app-directories/plan.md`, preserving the atomic
ABI task and gates below. Present that plan for approval, STOP again, and commit
it alone after approval. This backlog plan does not replace either approval.

**Verify**: both docs commits contain only their intended artifact; neither
contains a real absolute path, username, machine name, private hostname, or
package identity.

### Step 2: Extract the shared native header without changing behavior

Create `native/include/expo_dotnet_host.h` with `RuntimeContextError`,
`RuntimeContextResult`, and both function-pointer typedefs, copied without
field-order or type changes from `macos/ManagedLoader.h:14-30`. Put them in
`namespace expo::modules::dotnet`. Include the header from
`macos/ManagedLoader.h`, `windows/ExpoModulesDotnet/ManagedLoader.h`,
`ios/ExpoModulesDotnetInstaller.mm`, and
`android/src/main/cpp/ExpoModulesDotnetBindingsInstaller.cpp`; delete all four
old declarations and qualify mobile uses. Add the header to
`ExpoModulesDotnet.vcxproj` beside `expo_jsi.h`.

Compare all four copies before deleting them. If any field or function-pointer
parameter differs, stop and report a pre-existing ABI drift bug.

**Verify**: the declaration scan finds only the shared declarations. Android,
iOS, and the available desktop platform compile with unchanged registration
behavior. Commit this refactor before adding the new struct or signature.

### Step 3: Add the immutable managed model through tests

First add failing tests to
`Expo.ModulesCore.Tests/Modules/DotnetRuntimeContextTests.cs` for:

1. `new AppDirectories()` leaves both values unconfigured.
2. Each current-OS fully qualified path is retained verbatim.
3. Empty, whitespace-only, NUL-containing, and relative values are rejected for
   each constructor parameter.
4. Both context constructors throw `AppDirectoryNotConfiguredException` from
   both accessors when unconfigured.
5. Either directory can be configured while the other remains unconfigured.
6. The two-argument context constructor rejects `null` directories with
   `ArgumentNullException`.
7. A disposed context throws `ObjectDisposedException` before checking
   configuration.
8. `DirectoryProperty` and the exception message distinguish the two accessors.

Add the test-host case to
`Expo.ModulesCore.Tests/Testing/ExpoModuleTestHostTests.cs`, beside the existing
factory and lifecycle tests: `ExpoModuleTestHost.Create(directories, register)`
must expose supplied values inside `register`, while `Create(register)` remains
source- and binary-compatible.

Build test paths from the test OS, for example
`Path.Combine(Path.GetPathRoot(Environment.CurrentDirectory)!,
"expo-dotnet-tests", "cache")`; do not hard-code one OS's path syntax.
Run the focused test command and confirm failure because the API is absent.

Implement the immutable record and exception from "Proposed shape", the
two-argument context constructor and lifecycle-guarded accessors, and this exact
test-host overload:

```csharp
public static ExpoModuleTestHost Create(
    AppDirectories directories,
    Action<DotnetRuntimeContext, JavaScriptObject> register
)
```

The existing overload delegates with `AppDirectories.Unconfigured`. The context
stores the immutable record; each accessor locks `gate`, calls
`ThrowIfNotActiveLocked()`, then returns or throws the specific configuration
exception.

**Verify**: focused ModulesCore tests pass, the no-filesystem scan has no
matches, and reverting the path, independence, or disposal guard makes its test
fail. Commit the managed slice.

### Step 4: Change the ABI and generated export atomically

This task is one commit. Do not split the native function-pointer change from
the generated managed export: a three-argument export called through a
four-argument native typedef can compile while treating the directory pointer
as `RuntimeContextResult*`.

First extend `generateAggregator.test.ts` with failing source assertions for:

- the NativeAOT symbol `expo_dotnet_create_runtime_context_result_v2`, HostFXR
  method `CreateRuntimeContextResultV2`, and absence of their old names;
- `CreateRuntimeContextResultV2(nint api, nint runtimeHandle, nint
  appDirectories, RuntimeContextResult* result)`;
- a private sequential native mirror in exact shared-header field order;
- size-before-version validation with both values in mismatch messages;
- strict UTF-8;
- `(null, 0)` unconfigured, null/nonzero invalid, negative invalid, and
  non-null/zero decoded for constructor validation;
- `new DotnetRuntimeContext(runtime, directories)`.

Add the checked-in compiled harness from "Proposed shape" and make its Vitest
driver fail before implementation. Source-string assertions remain useful for
the exact emitted contract, but they do not replace the executable harness.
Then, in one working slice:

1. Add `EXPO_DOTNET_HOST_ABI_VERSION = 1` and
   `expo_dotnet_app_directories` to the shared header.
2. Document borrowed UTF-8 lifetime and the exact null/length rules.
3. Add `static_assert` checks for standard layout, every field offset, and total
   size (`40` on 64-bit pointers, `24` on 32-bit pointers).
4. Rename the typedef to `CreateRuntimeContextV2Fn`, add the directory pointer
   before `RuntimeContextResult*`, and rename the desktop entry-point field to
   `createRuntimeContextV2`.
5. Rename the generated NativeAOT export and HostFXR method to the v2 names.
   Update the literal symbol names in iOS and Android and both symbol/method
   constants in `macos/ManagedLoader.mm` and
   `windows/ExpoModulesDotnet/ManagedLoader.cpp`. Do not probe or export the old
   create name as a fallback.
6. Update all four native invocations to pass `nullptr`.
7. Update the generator with the private mirror and one per-field decode helper.
   Check `Size` before reading `Version` or either pointer. Decode before
   constructing the runtime context.
8. Emit `EntryPoints` as `partial`, run the executable harness cases, and
   release every structured error buffer in the fixture.
9. Regenerate both ignored app hosts. Do not hand-edit or stage them.

`hermes-console-app` keeps the preserved one-argument constructor; it does not
implement this exported host ABI and needs no source change.

**Verify**: autolinking tests compile and run the ABI harness; typecheck, build,
generated-host comparison, full managed tests, Android, iOS, and the available
desktop native build all pass. The declaration scan finds only shared
definitions and the old-name scan is empty. Inspect the built NativeAOT library
with the platform symbol tool (`nm -gU` on Apple, `dumpbin /exports` on Windows):
the v2 symbol is present and the old symbol is absent. The harness reflects the
generated managed type and makes the same assertion for the HostFXR method.
Those positive/negative checks prove that either stale pairing fails resolution
before invocation. Commit the header, resolver files, four native call sites,
generator, fixture, and generator test together.

### Step 5: Populate packaged Windows and macOS hosts

Create the UTF-8 `std::string` values and `expo_dotnet_app_directories` in the
same stack frame as `createRuntimeContextV2`; the strings must outlive the
synchronous call.

- Windows: include `<winrt/Windows.Storage.h>`, call
  `ApplicationData::Current()` once, use `LocalCacheFolder().Path()` and
  `LocalFolder().Path()`, and convert with `winrt::to_string`. On
  `winrt::hresult_error`, log that an unpackaged host lacks app identity and
  pass a null struct. Do not invent a `%LOCALAPPDATA%` fallback.
- macOS: obtain the first cache and Application Support URLs through
  `NSFileManager`, append the nonempty main-bundle identifier with
  `URLByAppendingPathComponent:isDirectory:`, convert both final paths to
  explicit UTF-8 strings, and pass the populated struct. If a URL or bundle
  identifier is absent, log the missing host identity and pass a null struct.

Run each desktop app with HostFXR and NativeAOT. Confirm through a temporary
assertion before the call that paths are distinct, fully qualified, and
app-scoped. Emit the durable, path-free marker from Decision 8 only after those
checks pass. Start Metro once, then run both exact macOS executable commands from
the command table and require the marker plus result `42` for each. Remove any
temporary raw-path diagnostics before commit. Record only sanitized shapes.

**STOP** if a path is a bare user-wide root, packaged Windows is unconfigured,
or the design appears to need a managed-core filesystem call.

**Verify**: both desktop builds and all four desktop
platform/loader combinations pass with the durable directory marker and result
`42`. If one platform is unavailable, hand off as awaiting platform
verification and leave plan 027 non-DONE. Commit only source and sanitized docs.

### Step 6: Run the full verification matrix

Run every command in "Commands you will need", using
`scripts/test-managed.ps1` for the Windows managed pass. Enable `pipefail`
before an Apple CLI fallback pipeline.

Inspect `git diff --name-only`; every tracked file must be in Scope. Ignored
`.expo` outputs may exist but must not be staged. Scan the staged diff for local
absolute paths, usernames, machine names, private hostnames, and real package
identities.

**Verify**: all managed, codegen, format, whitespace, declaration, and four
adapter-build gates pass. Android and iOS execute the generated export and
register the example module. Both desktop loader modes run on both target
platforms. Any skipped gate keeps the plan non-DONE and is named in the handoff.

### Step 7: Merge the accepted delta and archive the change

Compare the verified implementation and proposed living-spec edits with the
approved delta. If implementation materially diverged, or the edits would add a
requirement the operator did not approve, STOP and obtain operator approval
before merging. Mechanical wording and evidence updates that preserve the
approved requirements do not need a third approval.

Merge the accepted delta into:

- `runtime-and-abi.md` — managed lifecycle entry-point parameter,
  version/size validation, and borrowed UTF-8 field rules;
- `modules-core-boundary.md` — host-supplied directories, unconfigured and
  disposed behavior, independence, and the no-filesystem rule;
- `dotnet-autolinking.md` — aggregator marshalling responsibility;
- `hermes-testhost.md` — the binary-compatible test-host pass-through;
- `docs/plans/README.md` — mark 027 done only now, unblock 022, and replace its
  cache-root blocker with the requirement to use `context.CacheDirectory`.

After every Step 6 gate passes, move the transient change directory to
`docs/archive/changes/2026-07-25-host-app-directories`. If platform
verification is outstanding, do not merge or archive the delta as completed and
do not mark 027 DONE.

**Verify**: `scripts/format.sh --check --all` and `git diff --check` are clean.
The staged diff contains no local absolute paths, usernames, machine names,
private hostnames, or real package identities.

## Done criteria

1. `expo_dotnet_app_directories` and the runtime-context typedefs are declared
   exactly once, in `native/include/expo_dotnet_host.h`.
2. `CreateRuntimeContextV2Fn`, the v2 NativeAOT symbol, v2 HostFXR method, both
   resolver files, and the generated managed export change in one commit; all
   four native call sites compile against the same signature. The old create
   names are absent, so stale old/new pairings fail resolution.
3. `DotnetRuntimeContext.CacheDirectory` and `.PersistentFilesDirectory` each
   return the host-supplied path, throw `AppDirectoryNotConfiguredException` when
   unconfigured, and throw `ObjectDisposedException` after disposal. Either can
   be unconfigured while the other is set.
4. The no-filesystem-access scan in "Commands you will need" returns no matches.
5. Empty, whitespace-only, NUL-containing, and relative paths are rejected at
   construction for both directories.
6. The generated decoder enforces size, version, strict UTF-8, and exact
   pointer/length rules. The compiled ABI harness executes every valid and
   invalid case, releases structured errors, and verifies managed layout. Both
   ignored app entry points regenerate byte-identically.
7. The packaged Windows and macOS installers supply both app-scoped paths and
   emit the path-free configured marker after validation. Committed docs contain
   sanitized shapes only. Android and iOS compile while passing the defined
   unconfigured value.
8. `ExpoModuleTestHost.Create` has a directory-aware overload without replacing
   the existing public signature.
9. Managed, codegen, format, declaration, Android, iOS, macOS, and Windows build
   gates pass; Android and iOS register the example module, and HostFXR plus
   NativeAOT run on both desktop targets.
10. `docs/specs/` carries the merged requirements, the change package is
    archived, and `docs/plans/README.md` is updated only after every gate passes.

## STOP conditions

- Any of the four native declaration copies differs in field order, type, or
  function-pointer parameters before extraction. That is a pre-existing
  memory-safety bug; report it before touching anything.
- The native function-pointer signature and generated managed export cannot be
  changed and verified in one commit.
- The verified implementation materially diverges from the approved delta, or
  the living-spec merge would add a new requirement. Obtain operator approval
  before merging or archiving.
- Any of the four resolved platform paths is a bare user-wide root.
- Packaged Windows cannot obtain `ApplicationData::Current()`.
- Any pre-existing test fails.
- The change appears to require a filesystem call inside `Expo.ModulesCore`.
  Re-read Decision 4; if it still seems necessary, stop and report rather than
  adding one.
- Making the ABI change requires editing a generated `.g.cs` by hand.
- A test needs a real writable directory to pass. That is 022's problem, not
  this plan's; this plan's tests only need path strings.

## Maintenance notes

- The runtime-context ABI lived in four duplicated native declarations before
  this plan. Step 2 removes that hazard. If a future platform adapter
  reintroduces a local redeclaration instead of including the shared header, the
  same silent-corruption risk comes back.
- The generated C# mirror remains a second-language declaration by necessity.
  Review any future struct change as one atomic native-plus-generator change,
  bump the host ABI version, update size/offset checks, and regenerate both app
  hosts.
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
