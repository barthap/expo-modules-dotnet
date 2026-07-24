# Plan 022: `expo-asset-dotnet` for Windows and macOS

> **Executor instructions**: Run the drift check first. Follow the living-spec
> workflow before code changes, including operator approval of the delta spec.
> Complete every verification command. Do not add Expo global registration,
> Metro aliases, or mobile support in this plan. Update the plan index when
> complete.
>
> **Drift check**: `git diff --stat 9247d75d..HEAD -- packages/expo-modules-dotnet packages/example-module docs/specs/ docs/module-authoring-guide.md`
> Compare the current code with the evidence below. Stop if a changed boundary
> invalidates an assumption.

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: MED (cache correctness, download failure behavior, packaged paths)
- **Depends on**: none
- **Category**: authored module
- **Planned at**: `9247d75d`, 2026-07-24

## Why this matters

`expo-asset-dotnet` is the first real authored package. It proves package
layout, Roslyn-generated bindings, autolinking, promise rejection, and a useful
Windows/macOS capability without claiming that `expo-asset` resolves through
Expo's global module registry. Its JavaScript side should reuse Expo's existing
asset source resolution and `Asset` class; the native module only owns the
download/cache operation.

## Current state

- `docs/module-authoring-guide.md` defines the package shape: a `src/` facade,
  one `net10.0` project under `dotnet/`, `[ExpoModule]`/`[JS]` members, and
  `expo-module.config.json` with a `dotnet.projects` entry.
- `packages/expo-modules-dotnet/src/index.ts` resolves normal authored modules
  from `globalThis._expoDotnet.modules` with `requireDotnetModule`. That is the
  only registration target in this plan.
- `packages/expo-modules-dotnet-autolinking/src/resolveDotnetModules.ts` and
  `src/codegen/generateAggregator.ts` aggregate all declared projects. They
  have no Windows/macOS project selector, so this package must be one portable
  assembly and reject unsupported runtime platforms itself.
- `apps/desktop-app/node_modules/expo-desktop-stubs/windows/ExpoDesktopStubs/ExpoAsset.h`
  provides a Windows behavioral reference: `downloadAsync(url, md5Hash, type)`
  returns `file:` URLs unchanged, uses `ExponentAsset-<id>.<type>` cache names,
  validates an MD5 cache hit when supplied, and rejects download failures.
- `Expo.ModulesCore` supports `Task<T>` promise bindings. The existing
  `ArrayBuffer` work is not needed for this package.

## Scope

**In scope**

- Create the private workspace package `packages/expo-asset-dotnet` with its
  TypeScript facade, autolinking metadata, `net10.0` authored project, and
  package-owned tests.
- Register native module `ExpoAsset` under `_expoDotnet.modules`; expose only
  `downloadAsync(url: string, md5Hash: string | null, type: string)`.
- Support Windows and macOS. A module call on any other runtime must fail with
  a clear `PlatformNotSupportedException`, surfaced as a rejected Promise.
- Handle HTTPS/HTTP downloads, already-local `file:` URLs, deterministic cache
  names, MD5 cache validation, atomic replacement, and error mapping.
- Add a small app integration fixture that proves autolinking finds the package
  and the JS facade invokes the managed module.

**Out of scope**

- Registering into `globalThis.expo.modules`, package-specifier aliasing, or
  claiming `expo-asset` compatibility.
- Android/iOS, asset metadata resolution, `Asset` class reimplementation,
  image decoding, cache management APIs, and arbitrary URI schemes.
- Sharing a filesystem public API with plan 024. The two packages may share a
  later internal helper only after an explicit follow-up, not by introducing a
  premature common package here.

## Git workflow

- Branch: `advisor/022-expo-asset-dotnet` from the current integration branch.
- Use focused conventional commits. Do not push or open a PR without approval.
- Before every commit, scan staged content for machine-local paths and names.

## Steps

### Step 1: Approve the module contract

Create `docs/changes/2026-<mm-dd>-expo-asset-dotnet/spec.md` and its matching
implementation plan through `.agents/skills/living-spec-workflow/SKILL.md`.
The approved contract must define:

1. Package name `expo-asset-dotnet`, native name `ExpoAsset`, and normal
   `_expoDotnet.modules` lookup only.
2. The exact `downloadAsync` input validation: nonempty URL and type, MD5 as
   either `null` or 32 hexadecimal characters, and a catchable rejection for
   malformed values.
3. Accepted URL classes: return canonical `file:` URLs as local assets; accept
   HTTP(S); reject all other schemes with an explanatory error.
4. Cache roots per platform. Use a package-private resolver with Windows app
   local data and the macOS sandbox user's `Library/Caches`; create the
   `ExponentAsset` subdirectory safely and fail loudly when no usable root is
   available. Do not base this on the working directory.
5. Cache identity, extension sanitization, and an atomic temp-file-to-final
   replacement rule so a failed or interrupted download never becomes a hit.
6. MD5 behavior: supplied hashes verify existing and downloaded bytes; absent
   hashes use a SHA/MD5-safe deterministic URL key without pretending to verify
   contents.
7. Cancellation, HTTP status handling, redirects, resource disposal, and
   normalized error messages.

Present the spec for approval. Commit the approved spec and change plan before
implementation.

### Step 2: Add package skeleton and binding tests

Create:

- `packages/expo-asset-dotnet/package.json`
- `packages/expo-asset-dotnet/expo-module.config.json`
- `packages/expo-asset-dotnet/src/index.ts`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/ExpoAssetDotnet.csproj`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet/ExpoAssetModule.cs`
- `packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/`

Model project references and `PublishAot` analyzer configuration on
`packages/example-module/dotnet/ExampleModule/ExampleModule.csproj`. Declare
one autolinked assembly name, `ExpoAssetDotnet`, and module name `ExpoAsset`.
The facade must call `requireDotnetModule<ExpoAssetNativeModule>('ExpoAsset')`;
it should expose a narrowly typed native download function for Expo-side code,
not a duplicate JavaScript asset resolver.

Create deterministic unit tests around a temp cache root and injectable
`HttpMessageHandler`. Tests must never call the public network.

### Step 3: Implement cache and download behavior

Implement package-private services for platform validation, URI handling,
cache-root selection, file hashing, and download persistence. Use `HttpClient`
through an injectable client/factory so tests control response status, content,
and cancellation. Keep `ExpoAssetModule` thin: validate, ask the service to
resolve/download, then return a canonical `file:` URI.

The implementation must:

1. Return a valid input `file:` URI without opening the network.
2. Build deterministic `ExponentAsset-<id>.<safe-type>` names without allowing
   separators or traversal through `type` or the cache id.
3. Return a cache hit only after required MD5 verification succeeds.
4. Download to a same-directory temporary file, verify supplied MD5, then
   atomically replace the destination.
5. Delete partial files on every failure path and dispose streams, responses,
   cancellation registrations, and hashing objects.
6. Preserve the operation's failure cause in the rejected JavaScript error
   without exposing local absolute paths unnecessarily.

### Step 4: Wire tests and examples

Add managed tests for file URL pass-through, URL-key cache hit, valid/invalid
MD5 hit, corrupt cache redownload, HTTP non-success, cancellation, extension
sanitization, and atomic-write cleanup. Add a JS facade test that verifies the
native name and nullable MD5 argument shape. Extend the desktop example only
with a small executable check if it can use a local test server; do not add a
UI-only demonstration.

### Step 5: Merge docs and verify

Merge the accepted delta into the appropriate authored-module/lifecycle living
spec sections, archive the change folder, and mark plan 022 done in the index.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package JS tests | `pnpm --filter expo-asset-dotnet test` | exit 0 |
| Managed package tests | `dotnet test packages/expo-asset-dotnet/dotnet/ExpoAssetDotnet.Tests/ExpoAssetDotnet.Tests.csproj` | exit 0 |
| Full managed regression | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Autolinking resolution | `pnpm --filter expo-modules-dotnet-autolinking test` | exit 0 |

## Done criteria

- The package autolinks as one portable assembly and registers `ExpoAsset`.
- Windows/macOS calls implement the approved cache/download contract.
- All other platforms reject clearly; no compatibility aliases or globals land.
- Cache correctness and failure cleanup have deterministic tests.
- Required verification passes, living specs are updated, and no local paths
  appear in committed artifacts.

## STOP conditions

- The package needs `globalThis.expo.modules`, Metro aliasing, or Expo core
  changes to work. Those belong to the deferred compatibility work.
- A cache root cannot be defined safely for sandboxed Windows or macOS hosts.
- The current runtime cannot settle the download task safely during teardown.
- A required upstream behavior needs platform-specific native APIs or a public
  filesystem contract outside this scope.
