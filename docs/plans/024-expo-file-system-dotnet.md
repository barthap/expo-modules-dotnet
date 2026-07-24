# Plan 024: `expo-file-system-dotnet` local files core for Windows and macOS

> **Executor instructions**: This is an authored third-party package, not an
> upstream compatibility claim. Run the drift check and complete the
> living-spec workflow before implementation. Do not add unimplemented public
> stubs.
>
> **Drift check**: `git diff --stat 9247d75d..HEAD -- packages/expo-modules-dotnet/managed/packages/Expo.ModulesCore packages/expo-modules-dotnet docs/specs/`

## Status

- **Priority**: P1
- **Effort**: L
- **Risk**: HIGH (path security, URI canonicalization, destructive operations)
- **Depends on**: none; execute after plans 022 and 023 to reuse established package testing conventions
- **Category**: authored module
- **Planned at**: `9247d75d`, 2026-07-24

## Why this matters

This package is a useful local filesystem module for Windows and macOS while
remaining honest about its scope. It proves records, async/sync bindings,
`ArrayBuffer` byte transfer, errors, and platform behavior. It intentionally
does not claim that the upstream `expo-file-system` package or all of its
objects are available.

## Current state

- `Expo.ModulesCore` supports generated records, sync methods, `Task<T>`, and
  `ArrayBuffer`. `ArrayBuffer.WithReadOnlyBytes[Async]` and `WithBytes[Async]`
  provide scoped byte access; byte arrays and memory copy at generated codec
  boundaries.
- `docs/specs/ownership-and-scoped-refs.md` requires explicit ownership and
  only scoped byte access. This module must not retain a JS-backed buffer after
  the method call without taking an explicit copy/lease.
- The current autolinker has no platform-specific project selection. The
  package therefore uses one portable assembly with explicit runtime guards.
- No current public filesystem package or host path service exists. The module
  must define its own package-private roots and path/URI translation layer.

## Scope

**In scope**

- Create `packages/expo-file-system-dotnet` with native module name
  `ExpoFileSystemDotnet`, normal `_expoDotnet.modules` lookup, a `Paths` export,
  and `File`/`Directory` JavaScript classes backed by typed native operations.
- Windows and macOS only. Android/iOS are unsupported and must not be
  advertised as supported by package metadata or facade types.
- Local absolute native paths and `file:` URIs, canonicalized to the object's
  `.uri` file URI.
- `Paths.cache`, `Paths.document`, and read-only `Paths.bundle`.
- Create, read, write, copy, move, delete, directory enumeration/child creation,
  and metadata, with sync and async forms where the approved contract exposes
  both.

**Out of scope**

- HTTP/network tasks, file handles, streams, Blob, pickers, watchers/events,
  uploads, previews, Android content URIs, and Android/iOS support.
- Upstream package aliasing, Expo global registration, compatibility stubs, or
  methods that only throw `NotImplementedException`.
- Relative paths, any non-`file:` URI scheme, Windows device namespace paths
  beginning `\\?\` or `\\.\`, and paths that resolve outside an approved
  root when an operation takes a root-relative child name.

## Required path contract

The delta spec must preserve these decisions:

- Inputs may be absolute platform-native paths or `file:` URIs. Relative paths
  are invalid.
- Objects expose a canonical file URI. On Windows,
  `C:\\work\\report.txt` becomes `file:///C:/work/report.txt` and
  `\\\\server\\share\\report.txt` becomes `file://server/share/report.txt`.
- Percent decoding/encoding, drive letters, separators, URI authority, and UNC
  paths must round-trip without treating a network authority as a local path
  segment. Use a tested parser/value object, not string replacement.
- Reject `file:` URIs with malformed escapes, remote authority on macOS unless
  explicitly supported by the spec, and Windows device namespace paths before
  calling `System.IO`.
- The bundle root is read-only. Any mutation targeted at it fails clearly and
  before a destructive operation.

## Steps

### Step 1: Approve the filesystem contract

Create `docs/changes/2026-<mm-dd>-expo-file-system-dotnet/spec.md` and matching
`plan.md` under the living-spec workflow. Obtain approval after defining:

1. Exact TypeScript types, constructors, method names, sync/async pairs, return
   records, overwrite/recursive defaults, and error classes.
2. The platform root resolver: Windows app-local cache/documents and macOS
   sandbox cache/application-support paths, plus how bundle root comes from the
   host app location. It must not use the process working directory.
3. Input/canonical URI rules above, symlink behavior, race handling, and whether
   `move` is allowed across volumes with copy/delete fallback.
4. File create options, directory create options, bytes/text encoding behavior,
   missing target behavior, and metadata timestamps/sizes.
5. Root-safety policy for child names and deletion. If sandbox containment is a
   required security invariant, specify how canonical/symlink checks enforce it.
6. Binary API ownership: synchronous operations may borrow bytes only inside the
   `ArrayBuffer` callback; asynchronous reads produce a new native-backed
   buffer, and asynchronous writes copy/retain bytes before scheduling I/O.

Commit approved artifacts before source changes.

### Step 2: Create the package and path model

Create:

- `packages/expo-file-system-dotnet/package.json`
- `packages/expo-file-system-dotnet/expo-module.config.json`
- `packages/expo-file-system-dotnet/src/index.ts`
- `packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet/`
- `packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/`

Use one portable `net10.0` module project. Implement a package-private
`FileSystemLocation` value object that holds a validated native full path and
canonical URI. Keep URI parsing, platform validation, and root resolution out
of `[ExpoModule]` classes so they have pure deterministic tests.

Represent options/results with generated positional records and enums, not
untyped dictionaries. The TS facade owns ergonomic `File`, `Directory`, and
`Paths` objects; the native module receives validated canonical URI strings and
typed options, never JavaScript object layouts.

### Step 3: Implement local operations

Implement only approved operations:

1. `File`: create, text/textSync, bytes/bytesSync, write/writeSync,
   copy/copySync, move/moveSync, delete/deleteSync, and metadata.
2. `Directory`: create/createSync, list/listSync, child file/directory creation,
   copy/copySync, move/moveSync, delete/deleteSync, and metadata.
3. `Paths`: cache, document, bundle, plus approved URI/path join helpers.

Use `System.IO` APIs behind operation services. Validate all options before
side effects, use same-directory temp files plus replacement for overwrite
writes, and ensure move/copy semantics preserve approved overwrite behavior.
Do not retain JS-owned buffers across async work: read/copy bytes before I/O,
then construct a new `ArrayBuffer.CopyFrom` result for async binary reads.

### Step 4: Test the hard cases

Add pure tests for Windows drive paths, UNC paths, macOS POSIX paths, `file:`
URI encoding, malformed URIs, relative paths, forbidden schemes, and device
namespaces. Run Windows cases on Windows CI and macOS cases on the local macOS
host where their platform semantics are meaningful.

Add operation tests for every sync/async pair, missing resources, overwrite
rules, recursive/nonrecursive delete, bundle mutation denial, cross-directory
copy/move, and text/binary round trips. Add Hermes binding tests that prove
binary read output is a new `ArrayBuffer`, writing a subrange respects the
caller-selected bytes, and rejected native errors become catchable JS errors.

### Step 5: Merge docs and verify

Merge approved requirements into the relevant living specs, archive the change
folder, and set plan 024 to done only after both Windows and macOS validation
is recorded.

## Commands

| Purpose | Command | Expected result |
| --- | --- | --- |
| Package JS tests | `pnpm --filter expo-file-system-dotnet test` | exit 0 |
| Managed package tests | `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj` | exit 0 |
| Full managed regression | `scripts/test-managed.sh` | exit 0 |
| Formatting | `scripts/format.sh --check --all` | exit 0 |
| Windows validation | app/package test run on Windows | required recorded pass |

## Done criteria

- The declared local `Paths`, `File`, and `Directory` contract works on Windows
  and macOS with canonical URI behavior.
- Every destructive operation has deterministic semantics and coverage.
- No unsupported API is exported as a throwing placeholder.
- Android/iOS, global Expo compatibility, and network concerns remain absent.
- Verification and living-spec merge are complete.

## STOP conditions

- URI/path correctness requires an unreviewed parser or an untested platform
  behavior.
- A proposed operation cannot enforce the approved root/symlink safety policy.
- The surface grows into network, stream, picker, event, or file-handle work.
- A platform needs a distinct project selection mechanism from autolinking.
