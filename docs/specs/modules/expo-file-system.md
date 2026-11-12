# expo-file-system-dotnet

## Purpose

Define the behavior of the `packages/expo-file-system-dotnet` authored module.

## Requirements

### Requirement: Expo File System Dotnet Exposes The Upstream Local-File Shape

`packages/expo-file-system-dotnet` SHALL register a native module named
`FileSystem` only under `_expoDotnet.modules`, with shared-object classes
`FileSystemFile` and `FileSystemDirectory`. Every exported native member SHALL
have the upstream `expo-file-system` name, sync or async form, and value
types. Option records (`CreateOptions`, `InfoOptions`, `WriteOptions`,
`RelocationOptions`) SHALL declare every field as a non-nullable type with
the upstream default as a C# record parameter default, so an omitted or
`undefined` field takes the default and an explicit JavaScript `null` field
fails before authored code runs. An options parameter SHALL stay nullable, so
an omitted, `undefined`, or `null` options argument takes every default. The
package SHALL NOT export a stub, a throwing placeholder, or a differently
shaped substitute for an upstream member it does not implement. It SHALL add
no C ABI surface.

The facade SHALL export each TypeScript type under its upstream
`expo-file-system` name: `FileCreateOptions` for `File.create`,
`DirectoryCreateOptions` for `Directory.create`, `FileDigestAlgorithm`,
`FileInfo`, `DirectoryInfo`, `InfoOptions`, `PathInfo`, `FileWriteOptions`,
`RelocationOptions`, and the runtime enum `EncodingType`. It SHALL NOT export
`CreateOptions` or any other name or alias that upstream does not export.

Precondition failures SHALL surface as subclasses of `FileSystemException`, a
`CodedException`. Each subclass with an upstream counterpart SHALL carry the
upstream class name, so JavaScript sees the upstream `code`, and its
`Exception.Message` SHALL be the upstream message. I/O failures inside an
operation SHALL reach JavaScript with the .NET message and no `code`.

#### Scenario: Facade subclasses the native classes

- **GIVEN** the facade defines `class File extends FileSystemFile`
- **WHEN** JavaScript evaluates `new File(Paths.document, 'a.txt')`
- **THEN** the object SHALL inherit from `File.prototype`
- **AND** `instanceof File` and `instanceof FileSystemFile` SHALL be `true`
- **AND** native members SHALL operate on the canonical location
- **AND** the scenario SHALL be proven in the ES5 form a derived class
  compiles to (`Reflect.construct` with `newTarget`), because the testhost
  Hermes does not compile ES6 `class`

#### Scenario: Unimplemented members are absent

- **GIVEN** a member needs a core feature that is not available yet
- **WHEN** the package's types and native module are inspected
- **THEN** the member SHALL NOT exist on either

#### Scenario: Unsupported operating system

- **GIVEN** the module runs on an OS other than Windows or macOS
- **WHEN** JavaScript reads a member
- **THEN** it SHALL throw a catchable error from
  `PlatformNotSupportedException`

#### Scenario: Option record defaults

- **GIVEN** `create`, `info`, `write`, and `copy` calls
- **WHEN** JavaScript omits the options, passes `{}`, or passes a field as
  `undefined`
- **THEN** each field SHALL take its upstream default (`false`, and `'utf8'`
  for `encoding`)
- **AND** an explicit `null` for a `bool` or `encoding` field SHALL fail
  before authored code runs

#### Scenario: Explicit null in an existing option record fails

- **GIVEN** a missing file location, an existing directory, and an existing
  file
- **WHEN** JavaScript calls `create({ overwrite: null })` on the file
  location, `create({ idempotent: null })` on the directory, and
  `info({ md5: null })` on the existing file
- **THEN** each call SHALL throw before authored code runs, and the
  filesystem SHALL be unchanged
- **AND** `create(null)` on the file location and `info(null)` on the
  existing file SHALL succeed with every default

#### Scenario: Facade exports the upstream type names

- **GIVEN** the package's public TypeScript entry point `src/index.ts`
- **WHEN** a caller imports types from it
- **THEN** `FileCreateOptions`, `DirectoryCreateOptions`,
  `FileDigestAlgorithm`, `FileWriteOptions`, and `RelocationOptions` SHALL
  resolve, and `FileCreateOptions` SHALL have no `idempotent` key
- **AND** an import of `CreateOptions` SHALL fail to compile

#### Scenario: Errors carry upstream codes

- **GIVEN** an existing file and a `File` object at its location
- **WHEN** `create()` runs, synchronously, and
  `copy(directoryThatDoesNotExist)` runs through the async form
- **THEN** the thrown error SHALL have `code` `ERR_FILE_ALREADY_EXISTS` and
  message `File '<uri>' already exists`
- **AND** the rejection SHALL have `code` `ERR_DESTINATION_DOES_NOT_EXIST`
  and message `The destination path does not exist`

### Requirement: Expo File System Dotnet Canonicalizes Locations

Every location SHALL be parsed by one tested value object before any
`System.IO` call. Inputs SHALL be a `file:` URI or an absolute native path.
Relative paths, other schemes, malformed percent escapes, Windows device
namespace paths, and a `file:` authority on macOS SHALL be rejected with
`Invalid file system location: '<input>'`. An invalid entry name SHALL be
rejected with `Invalid file name: '<name>'`. The canonical `uri` SHALL resolve
`.` and `..`, SHALL end with `/` exactly for directories, and SHALL round-trip
Windows drive letters, UNC authorities, and percent encoding. The drive letter
SHALL be canonicalized to uppercase.

Location parsing SHALL also reject, before any I/O:

- On the Windows flavor only, any device prefix with a separator (`\` or `/`)
  at positions 0, 1, and 3 and `.` or `?` at position 2.
- A UNC host, in native form or as a percent-decoded URI authority, that does
  not match `[A-Za-z0-9._-]+` or that consists only of dots.
- On the Windows flavor, a segment that ends with `.` or a space. Win32 strips
  them, which would alias another name past the bundle-tree check. `.` and `..`
  are exempt because they are normalized first.
- On the Windows flavor, a segment that contains `<`, `>`, `:`, `"`, `|`, `?`,
  `*`, or a character from U+0001 to U+001F. NTFS stream syntax such as
  `X::$INDEX_ALLOCATION` names directory `X` and would alias it past the
  bundle-tree check. The drive designator (`C:`) is not a segment and SHALL
  still parse.
- On the Windows flavor, an entry name passed to name validation that ends with
  `.` or a space, or that contains any character from the previous rule. A name
  with `:` SHALL remain valid on the Posix flavor.
- Input that is not valid UTF-16, such as a lone surrogate.

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
- **THEN** creation SHALL fail with `Invalid file system location: '<input>'`
- **AND** no filesystem call SHALL have run

### Requirement: Expo File System Dotnet Roots Come From The Runtime Context

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
- **WHEN** `create`, `delete`, `rename`, `write`, or `writeSync` runs on it,
  `move` runs with it as source or destination, or `copy` runs with it as
  destination
- **THEN** it SHALL fail with `code` `ERR_MISSING_PERMISSION` and message
  `Missing permission for uri: <uri>`
- **AND** the filesystem SHALL be unchanged

#### Scenario: Bundle tree is a copy source

- **GIVEN** a file inside `bundleDirectory` and a writable directory
- **WHEN** `copySync(directory)` and `bytesSync()` run on the bundle file
- **THEN** both SHALL succeed and the bundle file SHALL be unchanged

### Requirement: Expo File System Dotnet Local Operations Match Upstream Semantics

`create`, `delete`, `rename`, `info`, `text`, `base64`, `bytes`, `write`,
`digest`, `copy`, `move`, `listAsRecords`, `createFile`, `createDirectory`,
and the metadata properties SHALL follow upstream iOS semantics, except where
a requirement below names a different rule. Validation SHALL run before side
effects. Async members SHALL NOT block the JavaScript thread and SHALL use the
locations captured when they start. A completed `move` and `rename` SHALL
update the object's location.

#### Scenario: Create without overwrite on an existing file

- **GIVEN** a file exists at the location
- **WHEN** `create()` runs with default options
- **THEN** it SHALL fail with `code` `ERR_FILE_ALREADY_EXISTS` and message
  `File '<uri>' already exists`
- **AND** the file contents SHALL be unchanged

#### Scenario: Idempotent directory create

- **GIVEN** a directory exists at the location
- **WHEN** `create({idempotent: true})` runs
- **THEN** it SHALL succeed without changing the directory

#### Scenario: Type mismatch

- **GIVEN** a directory exists at a `FileSystemFile` location
- **WHEN** any file operation runs, `write`, `bytes`, `copy`, and `move`
  included
- **THEN** it SHALL fail with `code` `ERR_INVALID_TYPE_FILE` and message
  `A folder with the same name already exists in the file location`

#### Scenario: Rename updates the object

- **GIVEN** an existing file
- **WHEN** `rename('b.txt')` runs
- **THEN** the file SHALL be renamed in its parent directory
- **AND** `uri` SHALL end with `/b.txt`

#### Scenario: Unsupported digest algorithm

- **GIVEN** an existing file
- **WHEN** `digest('SHA-3')` runs
- **THEN** the promise SHALL reject with `Unsupported digest algorithm: SHA-3`

### Requirement: Expo File System Dotnet Errors Carry Upstream Codes

`FileSystemException` SHALL be an internal abstract `CodedException`. Every
precondition failure of the module, and every error the module builds as a
`FileSystemException`, SHALL be one of its sealed subclasses below, with this
`code` and this `Exception.Message`. A code SHALL come from the class name by
the `CodedException` rule, except where the table marks it explicit. Failures
of .NET I/O calls inside an operation SHALL NOT be wrapped and SHALL reach
JavaScript with no `code`. `PlatformNotSupportedException` from the
unsupported-OS check and the core `AppDirectoryNotConfiguredException` are not
module errors and SHALL stay uncoded. The one exception to the no-wrap rule is
the `catch` block of `FileSystemFile.Create`, `FileSystemDirectory.Create`,
and `FileSystemPath.DeleteCore`: there an `IOException` or
`UnauthorizedAccessException` SHALL become `UnableToCreateException` or
`UnableToDeleteException`, with that exception's `Message` as `<reason>`.
`UnableToCreateException` and `UnableToDeleteException` SHALL have no inner
exception, so the JavaScript message shows the reason once and has no
`→ Caused by:` line.

| Class | Code | Message |
| --- | --- | --- |
| `InvalidLocationException` | `ERR_INVALID_LOCATION` | `Invalid file system location: '<input>'` |
| `InvalidNameException` | `ERR_INVALID_NAME` | `Invalid file name: '<name>'` |
| `InvalidTypeFileException` | `ERR_INVALID_TYPE_FILE` | `A folder with the same name already exists in the file location` |
| `InvalidTypeDirectoryException` | `ERR_INVALID_TYPE_DIRECTORY` | `A file with the same name already exists in the directory location` |
| `WrongTypeException` | `ERR_WRONG TYPE` (explicit, the iOS code for `name: "wrong type"`) | `tried to create a file with a directory path`, or `tried to create a directory with a file path` |
| `FileAlreadyExistsException` | `ERR_FILE_ALREADY_EXISTS` | `File '<uri>' already exists` |
| `UnableToCreateException` | `ERR_UNABLE_TO_CREATE` | `Unable to create file or directory: <reason>` |
| `UnableToDeleteException` | `ERR_UNABLE_TO_DELETE` | `Unable to delete file or directory: <reason>` |
| `MissingPermissionException` | `ERR_MISSING_PERMISSION` | `Missing permission for uri: <uri>` |
| `UnsupportedDigestAlgorithmException` | `ERR_UNSUPPORTED_DIGEST_ALGORITHM` | `Unsupported digest algorithm: <algorithm>` |
| `UnableToWriteBase64DataException` | `ERR_UNABLE_TO_WRITE_BASE64_DATA` | `Unable to write base64 data to a file: <uri>` |
| `CopyOrMoveDirectoryToFileException` | `ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE` | `Unable to copy or move a directory to a file` |
| `DestinationAlreadyExistsException` | `ERR_DESTINATION_ALREADY_EXISTS` | `Destination already exists` |
| `DestinationDoesNotExistException` | `ERR_DESTINATION_DOES_NOT_EXIST` | `The destination path does not exist` |
| `UnableToCopyException` | `ERR_UNABLE_TO_COPY` | `Unable to copy file or directory: <reason>` |
| `UnableToMoveException` | `ERR_UNABLE_TO_MOVE` | `Unable to move file or directory: <reason>` |
| `UnableToDetermineBundleDirectoryException` | `ERR_UNABLE_TO_DETERMINE_BUNDLE_DIRECTORY` | `Unable to determine the bundle directory` |
| `FileTooLargeException` | `ERR_FILE_TOO_LARGE` | `File is too large to read into memory: <uri>` |

#### Scenario: Sync and async failures carry the same code

- **GIVEN** a directory at a `File` location
- **WHEN** JavaScript calls `textSync()` and awaits `text()`
- **THEN** the throw and the rejection SHALL both have `code`
  `ERR_INVALID_TYPE_FILE` and the same message

#### Scenario: Unable-to-create errors show the reason once

- **GIVEN** a `File` whose parent directory is missing
- **WHEN** JavaScript calls `create()` without `intermediates`
- **THEN** the error SHALL have `code` `ERR_UNABLE_TO_CREATE` and a message
  that starts with `Unable to create file or directory: ` followed by the
  .NET failure message, because `FileSystemFile.Create` wraps an I/O failure
- **AND** the message SHALL NOT contain `→ Caused by:`

#### Scenario: Directory create and delete failures have no cause

- **GIVEN** `FileSystemDirectory.Create` or `FileSystemPath.DeleteCore` meets
  an `IOException` or `UnauthorizedAccessException`
- **WHEN** the module throws `UnableToCreateException` or
  `UnableToDeleteException`
- **THEN** the exception SHALL have no inner exception, and its message SHALL
  end with the caught exception's `Message`
- **AND** its JavaScript message SHALL NOT contain `→ Caused by:`

#### Scenario: I/O failures stay uncoded

- **GIVEN** a `File` whose parent directory is missing
- **WHEN** `writeSync('x')` runs
- **THEN** the error SHALL have the .NET message and `'code' in error` SHALL
  be `false`

### Requirement: Expo File System Dotnet Writes Files

`FileSystemFile.write` (async) and `writeSync` SHALL take `WriteContent`, a
`[Union]` of `string`, `ArrayBuffer`, and `Uint8Array`, and an optional
`WriteOptions`. Text SHALL be written as UTF-8 without a byte order mark. With
`Encoding == base64`, characters outside the base64 alphabet SHALL be
ignored, and content that still does not decode SHALL fail with
`UnableToWriteBase64DataException` before the file is touched. `Encoding`
SHALL be ignored for binary content. `Append == false` SHALL create or
truncate the file. `Append == true` SHALL create a missing file and otherwise
append. A missing file SHALL be created. A missing parent SHALL fail with the
.NET error. The async form SHALL copy binary content on the JavaScript thread
before it returns, and SHALL do file I/O off the JavaScript thread. The facade
SHALL pass any typed array other than `Uint8Array` and `DataView` as a
`Uint8Array` over the same bytes.

#### Scenario: Each content kind writes its bytes

- **GIVEN** a `File` at a missing location in an existing directory
- **WHEN** JavaScript calls `writeSync('zażółć')`, then
  `writeSync(new Uint8Array([1, 2, 3]).buffer)`, then
  `writeSync(new Uint8Array(buf, 2, 3))` over an 8-byte `buf`
- **THEN** after each call the file SHALL hold exactly the UTF-8 bytes of the
  text with no BOM, then `01 02 03`, then bytes 2 to 4 of `buf`

#### Scenario: Facade passes other typed arrays as bytes

- **GIVEN** the facade `File` and an `Int16Array` view at byte offset 2 of a
  buffer
- **WHEN** JavaScript calls `file.writeSync(view)`
- **THEN** the native member SHALL receive a `Uint8Array` over the same
  buffer, offset, and byte length, and the file SHALL hold those bytes
- **AND** the same `Int16Array` passed to the native class without the facade
  SHALL fail with the union mismatch error

#### Scenario: Append and truncate

- **GIVEN** a file that holds `abc`
- **WHEN** `writeSync('d', { append: true })` runs, and then `writeSync('e')`
- **THEN** the file SHALL hold `abcd`, and then `e`

#### Scenario: Base64 content

- **GIVEN** a file location
- **WHEN** `writeSync('AQID\n', { encoding: 'base64' })` runs, and then
  `writeSync('A', { encoding: EncodingType.Base64 })`
- **THEN** the first call SHALL write `01 02 03`
- **AND** the second SHALL fail with `code`
  `ERR_UNABLE_TO_WRITE_BASE64_DATA` and leave `01 02 03` in place

#### Scenario: Invalid content is rejected before the call

- **GIVEN** a `File`
- **WHEN** JavaScript passes `5`, `{}`, `null`, `undefined`, or a `DataView`
  as content, or `{ encoding: 'hex' }` as options
- **THEN** `writeSync` SHALL throw and `write` SHALL reject before authored
  code runs
- **AND** the file SHALL be unchanged

#### Scenario: Async write captures the content

- **GIVEN** a `Uint8Array` that holds `01 02`
- **WHEN** JavaScript calls `const p = file.write(view)`, sets `view[0] = 9`,
  and awaits `p`
- **THEN** the file SHALL hold `01 02`
- **AND** the view's buffer lease SHALL release exactly once

### Requirement: Expo File System Dotnet Reads Bytes

`FileSystemFile.bytes` (async) and `bytesSync` SHALL return a `Uint8Array`
with `byteOffset` 0 over a new buffer whose length equals the file length,
read through one open stream. An empty file SHALL return a zero-length view.
A file longer than `int.MaxValue` bytes SHALL fail before allocation with
`FileTooLargeException`. The async form SHALL read off the JavaScript thread.
Reading SHALL be allowed inside the bundle tree.

#### Scenario: Bytes round-trip

- **GIVEN** a file that holds bytes 0 to 255
- **WHEN** JavaScript calls `bytesSync()` and `await bytes()`
- **THEN** each result SHALL be a `Uint8Array` with `byteOffset` 0, `length`
  256, `buffer.byteLength` 256, and the file bytes

#### Scenario: Empty and missing files

- **GIVEN** an empty file and a missing file
- **WHEN** `bytesSync()` runs on each
- **THEN** the empty file SHALL give a zero-length `Uint8Array`
- **AND** the missing file SHALL throw with no `code`

### Requirement: Expo File System Dotnet Copies And Moves

`copy`, `copySync`, `move`, and `moveSync` on `FileSystemFile` and
`FileSystemDirectory` SHALL take a `FileSystemPath` destination and an
optional `RelocationOptions`. The async forms SHALL capture both locations
when they start and SHALL do file I/O off the JavaScript thread. Each call
SHALL run these checks in this order, all before any side effect:

1. Permission. `copy` SHALL require the destination outside the bundle tree.
   `move` SHALL require the source and the destination outside it. A failure
   SHALL be `MissingPermissionException` with that object's `uri`. After step
   3 resolves the target, the target SHALL also be outside the bundle tree,
   with the same error and the target's `uri`.
2. Type. The source and the destination SHALL each pass their type check
   (`InvalidTypeFileException` or `InvalidTypeDirectoryException`).
3. Target resolution, by this table:

| Source | Destination | Target | Failure |
| --- | --- | --- | --- |
| File | `Directory` that exists | `<destination>/<source name>` | none |
| File | `Directory` that does not exist | none | `DestinationDoesNotExistException` |
| File | `File` | the destination | `DestinationDoesNotExistException` when its parent is missing |
| Directory | `Directory` that exists | `<destination>/<source name>/` | none |
| Directory | `Directory` that does not exist | the destination | `DestinationDoesNotExistException` when its parent is missing |
| Directory | `File` | none | `CopyOrMoveDirectoryToFileException` |

4. Source existence. A missing source SHALL fail with the .NET
   `FileNotFoundException` or `DirectoryNotFoundException`, uncoded.
5. Same item. The stored path of an existing entry is its path with each
   segment replaced by the name that the parent directory listing stores for
   it. The source and the target are the same item when the target exists
   and both stored paths are equal ordinally. Then `copy` SHALL fail with
   `UnableToCopyException` (`source and destination are the same`). `move`
   SHALL fail with `UnableToMoveException` (same reason) when the two
   canonical URIs are also equal ordinally. When the URIs differ, the request
   changes only letter case, and `move` SHALL rename the entry in place and
   SHALL delete nothing. When the entry's stored name already equals the target
   name, because only a parent differs in case, `move` SHALL change no file
   system entry and SHALL set the object's location.
6. Nesting. A directory target inside the source directory SHALL fail with
   `UnableToCopyException` or `UnableToMoveException`
   (`destination is inside the source`).
7. Existing target. Without `Overwrite`, an existing target SHALL fail with
   `DestinationAlreadyExistsException`. With `Overwrite`, the existing entry
   SHALL be deleted recursively, whatever its type, after every other check
   passed. Before the delete, the call SHALL resolve each of the source and
   the target as follows: every link in a parent directory is replaced by its
   final target, and every segment is replaced by its stored name. The last
   segment SHALL NOT be followed. When the two resolved paths are equal, the
   call SHALL fail with `UnableToCopyException` or `UnableToMoveException`
   (`source and destination are the same`). When the resolved target contains
   the resolved source, compared by whole path segments, the call SHALL fail
   with the same exceptions (`destination contains the source`). Both
   failures SHALL delete nothing.

A directory copy SHALL copy every file and subdirectory, hidden entries
included, and SHALL copy a symbolic link inside the tree as a link. A failed
copy SHALL delete the target entries it created, SHALL rethrow the original
error, and SHALL NOT change the source. A copy whose target exists when the
copy starts, because another writer created it after the checks, SHALL fail
with `DestinationAlreadyExistsException` and SHALL delete nothing. A directory move that
`Directory.Move` cannot do because the target is on another volume SHALL copy
the tree and then delete the source. If that copy fails, the partial target
SHALL be removed and the source SHALL stay. If the source delete fails after a
complete copy, the call SHALL fail with `UnableToMoveException`
(`Failed to delete source after move`) and the complete target SHALL stay. A
completed move SHALL set the object's location to the target. A failed move
SHALL leave the location unchanged.

#### Scenario: File into a directory

- **GIVEN** a file `a.txt` and an existing directory `d/`
- **WHEN** `a.copySync(d)` runs, and then `a.moveSync(d, { overwrite: true })`
- **THEN** `d/a.txt` SHALL hold the source bytes after the copy
- **AND** after the move the source SHALL be gone, `a.uri` SHALL end with
  `/d/a.txt`, and `a.name` SHALL be `a.txt`

#### Scenario: Directory into an existing and a missing directory

- **GIVEN** a directory `s/` with a nested file and a hidden file, an existing
  directory `d/`, and a missing `n/` whose parent exists
- **WHEN** `s.copySync(d)` runs, and then `s.copySync(n)`
- **THEN** the tree SHALL be at `d/s/` and at `n/`, entry for entry

#### Scenario: Preconditions fail without side effects

- **GIVEN** a source file, a source directory, and an existing target
- **WHEN** a file is copied to a missing directory, a directory is copied to
  a `File`, a file is copied to an existing target without `overwrite`, a
  directory is copied into its own subdirectory, a file is copied onto itself
  with `overwrite`, and a missing source is copied with `overwrite` onto an
  existing target
- **THEN** the calls SHALL fail with `ERR_DESTINATION_DOES_NOT_EXIST`,
  `ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE`, `ERR_DESTINATION_ALREADY_EXISTS`,
  `ERR_UNABLE_TO_COPY`, `ERR_UNABLE_TO_COPY`, and an uncoded error
- **AND** the filesystem SHALL be unchanged after each call

#### Scenario: Overwrite never deletes a source inside the target

- **GIVEN** a directory `a/c/c/` and a file `a/f/f`
- **WHEN** `copySync` and `moveSync` with `overwrite: true` run on `a/c/c/` and
  on `a/f/f`, each with the destination `a/`
- **THEN** each call SHALL fail with `ERR_UNABLE_TO_COPY` or
  `ERR_UNABLE_TO_MOVE` (`destination contains the source`)
- **AND** the source and its contents SHALL still exist

#### Scenario: Overwrite never deletes a source reached through a link

- **GIVEN** a symbolic link `links/up` to a directory `a/` that holds the
  directories `a/s/` and `a/c/c/`
- **WHEN** `copySync` and `moveSync` with `overwrite: true` run on `a/s/` with
  the destination `links/up`, and on `a/c/c/` with the destination `links/up`
- **THEN** the first calls SHALL fail with `source and destination are the
  same` and the second calls with `destination contains the source`
- **AND** the sources and their contents SHALL still exist

#### Scenario: Async forms

- **GIVEN** a file and a directory destination
- **WHEN** JavaScript awaits `copy(d)` and `move(d)`, and a failing `move`
- **THEN** each SHALL return a Promise that resolves `undefined` after the
  filesystem change
- **AND** the failing `move` SHALL reject with its code and leave `uri`
  unchanged

#### Scenario: Case-only rename through move

- **GIVEN** a file `a.txt` on Windows or on a case-insensitive macOS volume
- **WHEN** `moveSync(new File(dir, 'A.txt'))` runs
- **THEN** the file SHALL be renamed to `A.txt` with its contents unchanged

#### Scenario: Symbolic links in a copied tree

- **GIVEN** a directory that holds a symbolic link to a file outside it
- **WHEN** the directory is copied
- **THEN** the copy SHALL hold a symbolic link with the same target, not a
  copy of the file
