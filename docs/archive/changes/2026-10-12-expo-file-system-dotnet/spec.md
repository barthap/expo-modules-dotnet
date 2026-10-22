# Expo File System Dotnet: Local Files Core

## Goal

Add a Windows/macOS `expo-file-system-dotnet` package that implements the
local-file subset of the upstream `expo-file-system` API (the `File`,
`Directory`, and `Paths` API, checked against `expo-file-system@58.0.7`) that
the current core can carry with the exact upstream native shape.

This is step 1 of three. Step 2 adds the missing core features (roadmap
034–037). Step 3 (roadmap 038) uses them to add the remaining local-file
members. Every member this slice ships has the upstream native name, sync or
async form, and value types, so later steps only add members and upstream's
own JavaScript can later run on the native module unchanged.

## Scope

### In scope

- A private workspace package `packages/expo-file-system-dotnet` with one
  portable `net10.0` managed project `ExpoFileSystemDotnet`, a test project,
  and a TypeScript facade.
- Native module `FileSystem` with shared-object classes `FileSystemFile` and
  `FileSystemDirectory`.
- The members listed under "Native surface".
- A facade that mirrors upstream's JavaScript layering: `File` and `Directory`
  subclass the native classes, plus `Paths` and the path utilities.
- The first coverage of a JavaScript subclass of a generated shared-object
  class. If it fails, the core fix belongs to this slice.

### Out of scope

- Members whose upstream shape the core cannot carry yet:
  - `write` / `writeSync`: needs a `string | ArrayBuffer` union argument
    (roadmap 035).
  - `bytes` / `bytesSync`: needs a `Uint8Array` return (roadmap 036).
  - `copy` / `copySync` / `move` / `moveSync`: need a base-class shared-object
    argument (roadmap 034).
  - Error `code` values: need coded errors (roadmap 037).
- `File.type` (MIME type). .NET has no portable MIME table. It goes to the
  roadmap's unplanned backlog.
- Network downloads and uploads, file handles, streams, `Blob` methods,
  pickers, watchers, preview, `appleSharedContainers`, and security-scoped
  access.
- Android and iOS support.
- `globalThis.expo.modules` registration, package aliasing, and the legacy
  API.

No out-of-scope member is exported, stubbed, or replaced by a differently
shaped native member.

## Accepted design

### Package shape

- `packages/expo-file-system-dotnet` follows `expo-asset-dotnet`:
  `package.json`, `expo-module.config.json` (`platforms: ["dotnet"]`),
  `src/index.ts`, `dotnet/ExpoFileSystemDotnet/`, and
  `dotnet/ExpoFileSystemDotnet.Tests/`.
- `[ExpoModule("FileSystem", Classes = new[] { typeof(FileSystemFile),
typeof(FileSystemDirectory) })]`.
- An internal abstract `FileSystemPath : SharedObject` holds the location
  and the shared logic. It is not a JavaScript class and not a codec type.
  The two sealed partial `[ExpoSharedObject]` classes declare their own
  `[JS]` members.
- The module supports Windows and macOS only. On any other OS, every member
  throws `PlatformNotSupportedException`, as in `expo-constants-dotnet`. The
  platform check is injectable so operation tests also run on Linux CI.
- All I/O uses .NET APIs. The slice adds no C ABI surface.

### Roots

| Member              | Type     | Value                                                                            |
| ------------------- | -------- | -------------------------------------------------------------------------------- |
| `documentDirectory` | `string` | `file:` URI of `DotnetRuntimeContext.PersistentFilesDirectory`, ending with `/`. |
| `cacheDirectory`    | `string` | `file:` URI of `DotnetRuntimeContext.CacheDirectory`, ending with `/`.           |
| `bundleDirectory`   | `string` | Plain native path, as upstream. Derived from `Environment.ProcessPath`.          |

- `bundleDirectory` on macOS is the enclosing `X.app` directory when the
  executable sits in `X.app/Contents/MacOS/`; otherwise it is the executable's
  directory. On Windows it is the executable's directory. .NET computes this
  from the process path, so it is not host knowledge under
  `### Requirement: ABI Carries Only Host Knowledge`.
- An unconfigured host directory surfaces the context's
  `AppDirectoryNotConfiguredException` as a JavaScript error when that root
  is read. There is no fallback path.

### Path model

A pure internal `FileSystemLocation` value object parses and canonicalizes
locations. `[ExpoModule]` and shared-object classes do not parse strings.

- Accepted input: a `file:` URI, or an absolute native path. This matches the
  upstream URL converter, which accepts both.
- Rejected before any `System.IO` call: relative paths, schemes other than
  `file`, malformed percent escapes, Windows device namespace paths
  (`\\?\`, `\\.\`), and a `file:` URI with an authority on macOS.
- Canonical `uri`: `.` and `..` segments are resolved. A directory URI always
  ends with `/`, and a file URI never does, as upstream's
  `standardizedFileURL` behavior.
- Windows: `C:\a b\x.txt` becomes `file:///C:/a%20b/x.txt`, and
  `\\server\share\x.txt` becomes `file://server/share/x.txt`. The UNC
  authority is never treated as a path segment.
- Percent encoding round-trips: decoding the canonical URI gives back the
  native path.

### Permissions

As upstream: the bundle tree is read-only. A mutation whose target is inside
`bundleDirectory` fails with `Missing permission for uri: <uri>` before any
side effect. Elsewhere the operating system decides. Symlinks get no special
handling.

### Shared behavior

- Type check before every operation: a directory at a `File` location fails
  with `A folder with the same name already exists in the file location`; a
  file at a `Directory` location fails with `A file with the same name already
exists in the directory location`.
- `validatePath()` throws `tried to create a file with a directory path` for a
  `FileSystemFile` whose URI ends with `/`, and `tried to create a directory
with a file path` for a `FileSystemDirectory` whose URI does not. With
  canonical input this succeeds; it exists for upstream facade parity.
- `delete()` is recursive for directories. A missing target fails with
  `Unable to delete file or directory: path does not exist`.
- `rename(newName)` keeps the parent directory and updates `uri` in place.
  A name that is empty, contains a separator, or is `.` or `..` is rejected
  before any side effect.
- `create(options?)` takes `CreateOptions {intermediates, overwrite,
idempotent}`, all `false` by default.
  - File: if an item exists and `overwrite` is `false`, it fails with
    `File '<uri>' already exists`. Otherwise it removes any existing item and
    creates an empty file. `intermediates` creates missing parent directories.
  - Directory: if it exists and `idempotent` is `true`, nothing happens. If it
    exists otherwise, it fails with `File '<uri>' already exists` unless
    `overwrite` is `true`, which replaces the directory with an empty one.
    `intermediates` creates missing parent directories.
- Other I/O failures surface as JavaScript errors whose messages do not need
  to match Foundation error text.

### Native surface

`FileSystemFile` (constructor `(uri: string)`):

| Member                             | Form         | Type                              |
| ---------------------------------- | ------------ | --------------------------------- |
| `uri`                              | property     | `string`                          |
| `exists`                           | property     | `boolean`; true only for a file   |
| `size`                             | property     | `number` (`long`); `0` on error   |
| `md5`                              | property     | `string \| null`; `null` on error |
| `modificationTime`, `lastModified` | property     | `number \| null` (ms since epoch) |
| `creationTime`                     | property     | `number \| null` (ms since epoch) |
| `validatePath()`                   | sync         | `void`                            |
| `create(options?)`                 | sync         | `void`                            |
| `delete()`                         | sync         | `void`                            |
| `rename(newName)`                  | sync         | `void`                            |
| `info(options?)`                   | sync         | `FileInfo`                        |
| `textSync()` / `text()`            | sync / async | `string`                          |
| `base64Sync()` / `base64()`        | sync / async | `string`                          |
| `digest(algorithm)`                | async        | `string`                          |

- `info({md5})` returns `FileInfo {exists, uri, md5?, size?,
modificationTime?, creationTime?}`. `md5` is present only when requested.
  A missing file returns `{exists: false, uri}`.
- `text` decodes UTF-8 and honors a byte-order mark. `base64` is standard
  base64 without line breaks.
- `digest` accepts `MD5`, `SHA-1`, `SHA-256`, `SHA-384`, and `SHA-512`,
  streams the file, and returns lowercase hex. Any other value fails with
  `Unsupported digest algorithm: <algorithm>`.

`FileSystemDirectory` (constructor `(uri: string)`):

| Member                      | Form     | Type                                             |
| --------------------------- | -------- | ------------------------------------------------ |
| `uri`                       | property | `string`                                         |
| `exists`                    | property | `boolean`; true only for a directory             |
| `size`                      | property | `number \| null`; recursive sum, `null` on error |
| `validatePath()`            | sync     | `void`                                           |
| `create(options?)`          | sync     | `void`                                           |
| `delete()`                  | sync     | `void`                                           |
| `rename(newName)`           | sync     | `void`                                           |
| `info()`                    | sync     | `DirectoryInfo`                                  |
| `listAsRecords()`           | sync     | `{isDirectory: boolean, uri: string}[]`          |
| `createFile(name, content)` | sync     | `FileSystemFile`                                 |
| `createDirectory(name)`     | sync     | `FileSystemDirectory`                            |

- `info()` returns `DirectoryInfo {exists, uri, files?, size?,
modificationTime?, creationTime?}`, where `files` holds child names.
- `listAsRecords()` fails for a missing directory. Order follows the OS.
- `createFile(name, content: string | null)` and `createDirectory(name)` use
  default create options, validate `name` as `rename` does, and return new
  native instances. `content` is ignored, as upstream.

Module members:

| Member                                                   | Form     | Type                              |
| -------------------------------------------------------- | -------- | --------------------------------- |
| `documentDirectory`, `cacheDirectory`, `bundleDirectory` | property | `string`                          |
| `totalDiskSpace`, `availableDiskSpace`                   | property | `number \| null`                  |
| `info(uri)`                                              | sync     | `PathInfo {exists, isDirectory?}` |

Disk space reads `DriveInfo` for the volume that holds the document directory
and returns `null` on error.

Records are generated positional records with lower-camel JavaScript fields.
Options are optional nullable record arguments.

### Concurrency

Async members run their I/O on the thread pool and never block the JavaScript
thread. A lock guards each object's location. An async operation captures the
location when it starts, so a concurrent `rename` does not redirect it.

### TypeScript facade

The facade mirrors upstream's JavaScript layering:

- `File extends FileSystemFile` and `Directory extends FileSystemDirectory`,
  with `(...uris: (string | File | Directory)[])` constructors that call
  `Paths.join` and then `validatePath()`.
- `File`: `name`, `extension`, `parentDirectory`.
- `Directory`: `name`, `parentDirectory`, `list()` (maps `listAsRecords` to
  `File` / `Directory` instances), and `createFile` / `createDirectory`
  wrapped into facade instances.
- `Paths`: `cache`, `document`, `bundle`, `info(...uris)`, `totalDiskSpace`,
  `availableDiskSpace`, and the path utilities ported from upstream
  `pathUtilities` (`join`, `relative`, `isAbsolute`, `normalize`, `dirname`,
  `basename`, `extname`, `parse`).

Members from later steps are absent from the types, not typed as optional.

## Delta requirements

### ADDED Requirement: Expo File System Dotnet Exposes The Upstream Local-File Shape

`packages/expo-file-system-dotnet` SHALL register a native module named
`FileSystem` only under `_expoDotnet.modules`, with shared-object classes
`FileSystemFile` and `FileSystemDirectory`. Every exported native member SHALL
have the upstream `expo-file-system` name, sync or async form, and value
types. The package SHALL NOT export a stub, a throwing placeholder, or a
differently shaped substitute for an upstream member it does not implement.
It SHALL add no C ABI surface.

#### Scenario: Facade subclasses the native classes

- **GIVEN** the facade defines `class File extends FileSystemFile`
- **WHEN** JavaScript evaluates `new File(Paths.document, 'a.txt')`
- **THEN** the object SHALL inherit from `File.prototype`
- **AND** `instanceof File` and `instanceof FileSystemFile` SHALL be `true`
- **AND** native members SHALL operate on the canonical location

#### Scenario: Unimplemented members are absent

- **GIVEN** a member needs a core feature that is not available yet
- **WHEN** the package's types and native module are inspected
- **THEN** the member SHALL NOT exist on either

#### Scenario: Unsupported operating system

- **GIVEN** the module runs on an OS other than Windows or macOS
- **WHEN** JavaScript reads a member
- **THEN** it SHALL throw a catchable error from
  `PlatformNotSupportedException`

### ADDED Requirement: Expo File System Dotnet Canonicalizes Locations

Every location SHALL be parsed by one tested value object before any
`System.IO` call. Inputs SHALL be a `file:` URI or an absolute native path.
Relative paths, other schemes, malformed percent escapes, Windows device
namespace paths, and a `file:` authority on macOS SHALL be rejected. The
canonical `uri` SHALL resolve `.` and `..`, SHALL end with `/` exactly for
directories, and SHALL round-trip Windows drive letters, UNC authorities, and
percent encoding.

#### Scenario: Windows path becomes a file URI

- **GIVEN** the Windows path `C:\a b\x.txt`
- **WHEN** a `FileSystemFile` is created from it
- **THEN** `uri` SHALL be `file:///C:/a%20b/x.txt`

#### Scenario: UNC authority is preserved

- **GIVEN** the Windows path `\\server\share\x.txt`
- **WHEN** it is canonicalized
- **THEN** `uri` SHALL be `file://server/share/x.txt`

#### Scenario: Invalid input is rejected before I/O

- **GIVEN** a relative path, an `http:` URI, a malformed escape, or a
  `\\?\` path
- **WHEN** a shared object is created from it
- **THEN** creation SHALL fail
- **AND** no filesystem call SHALL have run

### ADDED Requirement: Expo File System Dotnet Roots Come From The Runtime Context

`documentDirectory` and `cacheDirectory` SHALL be the `file:` URIs of
`DotnetRuntimeContext.PersistentFilesDirectory` and `CacheDirectory`, read
when accessed. An unconfigured directory SHALL surface as a JavaScript error
and SHALL NOT fall back to another path. `bundleDirectory` SHALL be a plain
path derived from `Environment.ProcessPath`: the enclosing `.app` directory
on macOS when the executable is in `Contents/MacOS/`, otherwise the
executable's directory.

#### Scenario: Unconfigured cache directory

- **GIVEN** the host supplied no cache directory
- **WHEN** JavaScript reads `cacheDirectory`
- **THEN** it SHALL throw a catchable error naming the unconfigured accessor

#### Scenario: Bundle tree is read-only

- **GIVEN** a location inside `bundleDirectory`
- **WHEN** `create`, `delete`, or `rename` runs on it
- **THEN** it SHALL fail with `Missing permission for uri: <uri>`
- **AND** the filesystem SHALL be unchanged

### ADDED Requirement: Expo File System Dotnet Local Operations Match Upstream Semantics

`create`, `delete`, `rename`, `info`, `text`, `base64`, `digest`,
`listAsRecords`, `createFile`, `createDirectory`, and the metadata properties
SHALL follow the semantics in this change's accepted design, which follow
upstream iOS. Validation SHALL run before side effects. Async members SHALL
NOT block the JavaScript thread and SHALL use the location captured when they
start.

#### Scenario: Create without overwrite on an existing file

- **GIVEN** a file exists at the location
- **WHEN** `create()` runs with default options
- **THEN** it SHALL fail with `File '<uri>' already exists`
- **AND** the file contents SHALL be unchanged

#### Scenario: Idempotent directory create

- **GIVEN** a directory exists at the location
- **WHEN** `create({idempotent: true})` runs
- **THEN** it SHALL succeed without changing the directory

#### Scenario: Type mismatch

- **GIVEN** a directory exists at a `FileSystemFile` location
- **WHEN** any file operation runs
- **THEN** it SHALL fail with `A folder with the same name already exists in
the file location`

#### Scenario: Rename updates the object

- **GIVEN** an existing file
- **WHEN** `rename('b.txt')` runs
- **THEN** the file SHALL be renamed in its parent directory
- **AND** `uri` SHALL end with `/b.txt`

#### Scenario: Unsupported digest algorithm

- **GIVEN** an existing file
- **WHEN** `digest('SHA-3')` runs
- **THEN** the promise SHALL reject with `Unsupported digest algorithm: SHA-3`

## Verification

- `scripts/test-managed.sh` passes on macOS with no skips. It discovers the
  new test project.
- `pnpm --filter expo-file-system-dotnet test` and `typecheck` pass.
- `scripts/format.sh --check --all` and `git diff --check` pass.
- The hot-path reflection `rg` check finds nothing in the new package.
- The Windows CI lane passes, including the Windows path cases. This needs a
  push, which needs the user's approval.

## Roadmap

- 024 becomes "local files core" and is `IN PROGRESS` in the commit that adds
  this spec.
- New rows: 034 base-class shared-object arguments, 035 union arguments, 036
  typed-array codecs, 037 coded errors, 038 file-system completion.
  034–036 follow 030. 038 depends on 034–036. 025 also depends on 036.
- `File.type` goes to the unplanned backlog.
