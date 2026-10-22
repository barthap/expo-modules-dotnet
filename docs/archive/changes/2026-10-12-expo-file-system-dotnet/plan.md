# Expo File System Dotnet Local Files Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `packages/expo-file-system-dotnet`: the upstream
`expo-file-system` local-file subset (native module `FileSystem`, classes
`FileSystemFile` / `FileSystemDirectory`, and a `File` / `Directory` / `Paths`
facade) for Windows and macOS.

**Architecture:** One portable `net10.0` authored module. A pure
`FileSystemLocation` value object parses and canonicalizes every location for
an explicit path flavor (Windows or POSIX), so its tests run on every OS. An
internal abstract `FileSystemPath : SharedObject` holds the location and the
shared operations. Two sealed `[ExpoSharedObject]` classes declare the
upstream `[JS]` members. The TypeScript facade is a port of upstream's
`File.ts`, `Directory.ts`, `Paths.ts`, and `pathUtilities`, limited to the
members this slice ships.

**Tech Stack:** C# / .NET 10, `Expo.ModulesCore` generator, xUnit v3,
`Expo.ModulesCore.Testing` (`ExpoModuleTestHost`, Hermes), TypeScript,
Vitest, pnpm workspace.

**Spec:** `docs/changes/2026-10-12-expo-file-system-dotnet/spec.md`. Read it
before any task. It is the authority on member names, types, messages, and
semantics. Upstream reference sources (read-only): the `expo/expo` repo,
`packages/expo-file-system/src/` and `ios/` at `expo-file-system@58.0.7`.

## Global Constraints

- Native module name `FileSystem`; JS class names `FileSystemFile`,
  `FileSystemDirectory`. Assembly and namespace `ExpoFileSystemDotnet`.
- Windows and macOS only. Every module member and both shared-object
  constructors call `FileSystemPlatform.EnsureSupported()`.
- No member outside the spec's "Native surface" tables. No stubs, no
  throwing placeholders, no renamed substitutes (`writeText`, `copyToFile`).
- No C ABI change. No change under `packages/expo-modules-dotnet/` unless
  Task 6's subclass test fails; then stop and report before changing core.
- No runtime reflection, `object?[]`, `JsonSerializer`, or dynamic invoke in
  the package.
- Error messages are copied verbatim from the spec.
- Every commit passes the privacy check in `AGENTS.md`: no absolute local
  paths, usernames, or machine names in staged content.
- Commit messages end with the `Claude-Session:` trailer from the session.

## Review Focus

1. A Windows drive path given to the facade as one string
   (`new File('C:\\x\\a.txt')`) reaches native as `c:\x\a.txt` after WHATWG
   `URL` parsing. Native must treat `^[A-Za-z]:[\\/]` as a drive path on
   Windows and canonicalize the drive letter to uppercase (Task 1 test).
2. A percent-encoded separator (`%2F`, and `%5C` on Windows) or `%00` inside a
   URI segment must be rejected, not decoded into a path change (Task 1 test).
3. `..` segments above the root must clamp at the root, never escape a UNC
   share or drive (Task 1 test).
4. Hidden files must count in `Directory.size`, `info().files`, and
   `listAsRecords()`. `EnumerationOptions` skips `Hidden | System` by default
   (Task 5 test).
5. `Directory.create()` without `intermediates` must fail when the parent is
   missing. `Directory.CreateDirectory` creates parents silently (Task 3 test).

Known facade limit, not fixed here because it matches upstream:
`Paths.join('C:\\x', 'a.txt')` loses the second segment, because WHATWG `URL`
treats `C:` as a scheme. Windows callers join from `Paths.document` or a
`file:` URI. The facade also needs a WHATWG `URL` global, as upstream does.

---

## File Structure

```text
packages/expo-file-system-dotnet/
  package.json                     workspace package, vitest + tsc scripts
  expo-module.config.json          dotnet autolinking metadata
  tsconfig.json, vitest.config.ts  copied from expo-asset-dotnet
  src/
    index.ts                       public exports
    ExpoFileSystem.ts              native module lookup + native class types
    File.ts, Directory.ts, Paths.ts  facade classes (upstream port, subset)
    pathUtilities/index.ts, path.ts, url.ts  upstream port, verbatim logic
    public-api.typecheck.ts        compile-time absence/presence checks
    __tests__/autolinking.test.ts, facade.test.ts, pathUtilities.test.ts
  dotnet/ExpoFileSystemDotnet/
    ExpoFileSystemDotnet.csproj, AssemblyInfo.cs
    FileSystemLocation.cs          parse/canonicalize (pure)
    FileSystemPlatform.cs          OS guard with test override
    FileSystemRoots.cs             bundle root derivation (pure + current)
    FileSystemException.cs         error type + message factory
    FileSystemRecords.cs           CreateOptions, InfoOptions, records
    FileSystemPath.cs              shared base: location, lock, operations
    FileSystemFile.cs              [ExpoSharedObject] file class
    FileSystemDirectory.cs         [ExpoSharedObject] directory class
    FileSystemModule.cs            [ExpoModule("FileSystem")]
  dotnet/ExpoFileSystemDotnet.Tests/
    ExpoFileSystemDotnet.Tests.csproj, AssemblyInfo.cs
    TemporaryDirectory.cs, PlatformScope.cs
    FileSystemLocationTests.cs, FileSystemRootsTests.cs
    FileSystemPathTests.cs, FileSystemFileTests.cs,
    FileSystemDirectoryTests.cs, FileSystemModuleTests.cs,
    FileSystemBindingTests.cs
```

---

### Task 1: Package scaffold and `FileSystemLocation`

**Files:**

- Create: `packages/expo-file-system-dotnet/package.json`,
  `expo-module.config.json`, `tsconfig.json`, `vitest.config.ts`,
  `src/__tests__/autolinking.test.ts`
- Create: `dotnet/ExpoFileSystemDotnet/ExpoFileSystemDotnet.csproj`,
  `AssemblyInfo.cs`, `FileSystemLocation.cs`, `FileSystemException.cs`
- Create: `dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`,
  `AssemblyInfo.cs`, `FileSystemLocationTests.cs`
- Modify: `pnpm-lock.yaml` (through `pnpm install`)

**Interfaces:**

- Produces:
  - `internal enum PathFlavor { Posix, Windows }`
  - `internal static PathFlavor PathFlavors.Current` (Windows when
    `OperatingSystem.IsWindows()`, else Posix)
  - `internal sealed class FileSystemLocation` with:
    `static FileSystemLocation Parse(string input, bool isDirectory, PathFlavor flavor)`,
    `string Uri`, `string NativePath`, `bool IsDirectory`,
    `bool HasDirectoryPath`, `string Name`, `FileSystemLocation Parent`,
    `FileSystemLocation Child(string name, bool isDirectory)`,
    `FileSystemLocation WithName(string name)`,
    `bool IsWithin(FileSystemLocation root)`,
    `static void ValidateName(string name, PathFlavor flavor)`
  - `internal sealed class FileSystemException : Exception` and
    `internal static class FileSystemErrors` (messages below)

- [ ] **Step 1: Scaffold the package files**

Copy `tsconfig.json` and `vitest.config.ts` from
`packages/expo-asset-dotnet/` unchanged. `package.json`:

```json
{
  "name": "expo-file-system-dotnet",
  "version": "0.1.0",
  "private": true,
  "description": "Local file system module for Expo .NET hosts",
  "main": "src/index.ts",
  "types": "src/index.ts",
  "scripts": {
    "test": "vitest run",
    "typecheck": "tsc -p tsconfig.json --noEmit"
  },
  "peerDependencies": {
    "expo-modules-dotnet": "*"
  },
  "devDependencies": {
    "expo-modules-dotnet": "workspace:*",
    "typescript": "catalog:react-native-86",
    "vitest": "^3.0.0"
  }
}
```

`expo-module.config.json`:

```json
{
  "platforms": ["dotnet"],
  "dotnet": {
    "projects": [
      {
        "path": "dotnet/ExpoFileSystemDotnet/ExpoFileSystemDotnet.csproj",
        "assemblyName": "ExpoFileSystemDotnet"
      }
    ]
  }
}
```

`src/__tests__/autolinking.test.ts`: copy
`packages/expo-asset-dotnet/src/__tests__/autolinking.test.ts` and replace
the project path and assembly name with the values above.

The two `.csproj` files and both `AssemblyInfo.cs` files are copies of the
`ExpoAssetDotnet` ones with `ExpoAssetDotnet` replaced by
`ExpoFileSystemDotnet`. The test `AssemblyInfo.cs` keeps
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`: Task 2
adds process-wide overrides.

Run: `pnpm install` (updates `pnpm-lock.yaml` with the new workspace package).
Expected: exit 0.

- [ ] **Step 2: Write the failing location tests**

`FileSystemLocationTests.cs`:

```csharp
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemLocationTests
{
  [Theory]
  [InlineData("/a/b.txt", "file:///a/b.txt", "/a/b.txt")]
  [InlineData("file:///a/b.txt", "file:///a/b.txt", "/a/b.txt")]
  [InlineData("FILE:///a/b.txt", "file:///a/b.txt", "/a/b.txt")]
  [InlineData("/a/./c/../b.txt", "file:///a/b.txt", "/a/b.txt")]
  [InlineData("/../../b.txt", "file:///b.txt", "/b.txt")]
  [InlineData("/a b/%.txt", "file:///a%20b/%25.txt", "/a b/%.txt")]
  [InlineData("file:///a%20b/%C5%BC.txt", "file:///a%20b/%C5%BC.txt", "/a b/\u017C.txt")]
  [InlineData("/a/b.txt/", "file:///a/b.txt", "/a/b.txt")]
  public void PosixFileCanonicalizes(string input, string uri, string nativePath)
  {
    var location = FileSystemLocation.Parse(input, isDirectory: false, PathFlavor.Posix);
    Assert.Equal(uri, location.Uri);
    Assert.Equal(nativePath, location.NativePath);
    Assert.False(location.HasDirectoryPath);
  }

  [Theory]
  [InlineData("/a/b", "file:///a/b/", "/a/b")]
  [InlineData("/", "file:///", "/")]
  [InlineData("file:///a/b/", "file:///a/b/", "/a/b")]
  public void PosixDirectoryEndsWithSlash(string input, string uri, string nativePath)
  {
    var location = FileSystemLocation.Parse(input, isDirectory: true, PathFlavor.Posix);
    Assert.Equal(uri, location.Uri);
    Assert.Equal(nativePath, location.NativePath);
    Assert.True(location.HasDirectoryPath);
  }

  [Theory]
  [InlineData(@"C:\a b\x.txt", "file:///C:/a%20b/x.txt", @"C:\a b\x.txt")]
  [InlineData(@"c:\a\x.txt", "file:///C:/a/x.txt", @"C:\a\x.txt")]
  [InlineData("C:/a/x.txt", "file:///C:/a/x.txt", @"C:\a\x.txt")]
  [InlineData("file:///c:/a/x.txt", "file:///C:/a/x.txt", @"C:\a\x.txt")]
  [InlineData(@"C:\a\..\..\x.txt", "file:///C:/x.txt", @"C:\x.txt")]
  [InlineData(@"\\server\share\x.txt", "file://server/share/x.txt", @"\\server\share\x.txt")]
  [InlineData("file://server/share/x.txt", "file://server/share/x.txt", @"\\server\share\x.txt")]
  [InlineData(@"\\server\share\..\..\x.txt", "file://server/share/x.txt", @"\\server\share\x.txt")]
  public void WindowsFileCanonicalizes(string input, string uri, string nativePath)
  {
    var location = FileSystemLocation.Parse(input, isDirectory: false, PathFlavor.Windows);
    Assert.Equal(uri, location.Uri);
    Assert.Equal(nativePath, location.NativePath);
  }

  [Fact]
  public void WindowsDriveRootDirectory()
  {
    var location = FileSystemLocation.Parse(@"C:\", isDirectory: true, PathFlavor.Windows);
    Assert.Equal("file:///C:/", location.Uri);
    Assert.Equal(@"C:\", location.NativePath);
  }

  [Theory]
  [InlineData("", PathFlavor.Posix)]
  [InlineData("a/b.txt", PathFlavor.Posix)]
  [InlineData("./b.txt", PathFlavor.Posix)]
  [InlineData("http://example.com/a", PathFlavor.Posix)]
  [InlineData("content://media/1", PathFlavor.Posix)]
  [InlineData("file:///a/%ZZ.txt", PathFlavor.Posix)]
  [InlineData("file:///a/%2F.txt", PathFlavor.Posix)]
  [InlineData("file:///a/%00.txt", PathFlavor.Posix)]
  [InlineData("file:///a/b.txt?x=1", PathFlavor.Posix)]
  [InlineData("file:///a/b.txt#x", PathFlavor.Posix)]
  [InlineData("file://host/a/b.txt", PathFlavor.Posix)]
  [InlineData("file:a/b.txt", PathFlavor.Posix)]
  [InlineData("/a/\0b.txt", PathFlavor.Posix)]
  [InlineData(@"a\b.txt", PathFlavor.Windows)]
  [InlineData(@"\a\b.txt", PathFlavor.Windows)]
  [InlineData(@"C:a.txt", PathFlavor.Windows)]
  [InlineData(@"\\?\C:\a.txt", PathFlavor.Windows)]
  [InlineData(@"\\.\PhysicalDrive0", PathFlavor.Windows)]
  [InlineData("//?/C:/a.txt", PathFlavor.Windows)]
  [InlineData(@"\\server", PathFlavor.Windows)]
  [InlineData("file:///a/b.txt", PathFlavor.Windows)]
  [InlineData("file:///C:/a/%5C.txt", PathFlavor.Windows)]
  public void InvalidInputIsRejected(string input, PathFlavor flavor)
  {
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.Parse(input, isDirectory: false, flavor)
    );
    Assert.StartsWith("Invalid file system location", error.Message);
  }

  [Fact]
  public void ChildParentAndName()
  {
    var root = FileSystemLocation.Parse("/a", isDirectory: true, PathFlavor.Posix);
    var child = root.Child("b c.txt", isDirectory: false);
    Assert.Equal("file:///a/b%20c.txt", child.Uri);
    Assert.Equal("b c.txt", child.Name);
    Assert.Equal("file:///a/", child.Parent.Uri);
    Assert.Equal("file:///a/d.txt", child.WithName("d.txt").Uri);
  }

  [Theory]
  [InlineData("/app/X.app", "/app/X.app/Contents/a.png", true)]
  [InlineData("/app/X.app", "/app/X.app", true)]
  [InlineData("/app/X.app", "/app/X.APP/a.png", true)]
  [InlineData("/app/X.app", "/app/X.apps/a.png", false)]
  [InlineData("/app/X.app", "/app", false)]
  public void IsWithinComparesWholeSegments(string root, string path, bool expected)
  {
    var rootLocation = FileSystemLocation.Parse(root, isDirectory: true, PathFlavor.Posix);
    var location = FileSystemLocation.Parse(path, isDirectory: false, PathFlavor.Posix);
    Assert.Equal(expected, location.IsWithin(rootLocation));
  }

  [Theory]
  [InlineData("")]
  [InlineData(".")]
  [InlineData("..")]
  [InlineData("a/b")]
  [InlineData("a\0b")]
  public void InvalidNameIsRejected(string name)
  {
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.ValidateName(name, PathFlavor.Posix)
    );
    Assert.Equal($"Invalid file name: '{name}'", error.Message);
  }

  [Fact]
  public void BackslashNameIsRejectedOnWindowsOnly()
  {
    FileSystemLocation.ValidateName(@"a\b", PathFlavor.Posix);
    Assert.Throws<FileSystemException>(
        () => FileSystemLocation.ValidateName(@"a\b", PathFlavor.Windows)
    );
  }

  [Fact]
  public void CurrentFlavorRoundTripsTemporaryDirectory()
  {
    var temp = Path.GetFullPath(Path.GetTempPath());
    var location = FileSystemLocation.Parse(temp, isDirectory: true, PathFlavors.Current);
    var reparsed = FileSystemLocation.Parse(location.Uri, isDirectory: true, PathFlavors.Current);
    Assert.Equal(
        Path.TrimEndingDirectorySeparator(temp),
        Path.TrimEndingDirectorySeparator(reparsed.NativePath)
    );
  }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: build FAIL, `FileSystemLocation` not defined.

- [ ] **Step 4: Implement `FileSystemException` and `FileSystemLocation`**

`FileSystemException.cs` holds every spec message in one place:

```csharp
namespace ExpoFileSystemDotnet;

internal sealed class FileSystemException(string message, Exception? inner = null)
    : Exception(message, inner);

internal static class FileSystemErrors
{
  internal static FileSystemException InvalidLocation(string input) =>
      new($"Invalid file system location: '{input}'");
  internal static FileSystemException InvalidName(string name) =>
      new($"Invalid file name: '{name}'");
  internal static FileSystemException FolderAtFileLocation() =>
      new("A folder with the same name already exists in the file location");
  internal static FileSystemException FileAtDirectoryLocation() =>
      new("A file with the same name already exists in the directory location");
  internal static FileSystemException WrongTypeFile() =>
      new("tried to create a file with a directory path");
  internal static FileSystemException WrongTypeDirectory() =>
      new("tried to create a directory with a file path");
  internal static FileSystemException AlreadyExists(string uri) =>
      new($"File '{uri}' already exists");
  internal static FileSystemException UnableToCreate(string reason, Exception? inner = null) =>
      new($"Unable to create file or directory: {reason}", inner);
  internal static FileSystemException UnableToDelete(string reason, Exception? inner = null) =>
      new($"Unable to delete file or directory: {reason}", inner);
  internal static FileSystemException MissingPermission(string uri) =>
      new($"Missing permission for uri: {uri}");
  internal static FileSystemException UnsupportedDigest(string algorithm) =>
      new($"Unsupported digest algorithm: {algorithm}");
}
```

`FileSystemLocation.cs` rules (implement exactly; the tests pin them):

- Reject (with `InvalidLocation(input)`) null, empty, or any input with `\0`.
- Windows flavor, checked in this order on the raw input:
  1. Starts with `\\?\`, `\\.\`, `//?/`, or `//./` → reject.
  2. Matches `^[A-Za-z]:[\\/]` → drive path. Drive is the letter in
     uppercase plus `:`. Segments come from the rest, split on `\` and `/`.
  3. Starts with `\\` or `//` → UNC. Split the rest on `\` and `/`; the first
     non-empty part is the host, and at least one more part (the share) is
     required, else reject. The share is the first segment and the clamp
     floor for `..`.
  4. Otherwise fall through to the URI check.
- URI check, both flavors: `^[A-Za-z][A-Za-z0-9+.-]*:` matches → the scheme
  must equal `file` ignoring case, else reject. The remainder must start with
  `//`, else reject. Reject if it contains `?` or `#`. The authority runs to
  the next `/`:
  - Posix: a non-empty authority → reject.
  - Windows: a non-empty authority → UNC host; the first path segment is the
    share (required). An empty authority requires the first segment to match
    `^[A-Za-z]:$` → drive; otherwise reject.
  - Each segment is percent-decoded as UTF-8. A `%` not followed by two hex
    digits, an invalid UTF-8 sequence, or a decoded `/`, `\0`, or (Windows)
    `\` → reject.
- Otherwise (no scheme): Posix requires a leading `/`, split on `/`. Windows:
  reject (the drive and UNC forms were handled above).
- Segment normalization: drop empty and `.` segments; `..` removes the last
  segment but never the UNC share and never below the root.
- `HasDirectoryPath = IsDirectory || segments are empty || (UNC and only the
share segment)`.
- `Uri`: `"file://" + (host ?? "") + "/" + (drive is null ? "" : drive + "/")
  - string.Join('/', encoded segments) + (IsDirectory && segments.Count > 0 ? "/" : "")`.
Segment encoding: keep ASCII letters, digits, `-._~!$&'()*+,;=:@`; encode
every other UTF-8 byte as `%XX` with uppercase hex.
- `NativePath`: Posix `"/" + string.Join('/', segments)`; drive
  `drive + "\" + string.Join('\', segments)`; UNC
  `@"\\" + host + "\" + string.Join('\', segments)`. No trailing separator
  except a root (`/`, `C:\`).
- `Name`: last segment, or `""` for a root.
- `Parent`: same root, last segment removed, `IsDirectory = true`.
- `Child(name, isDirectory)`: calls `ValidateName(name, Flavor)`, appends.
- `WithName(name)`: `Parent.Child(name, IsDirectory)`.
- `IsWithin(root)`: same flavor, host and drive equal ignoring case, and
  `root` segments are a prefix of these segments comparing each segment with
  `StringComparison.OrdinalIgnoreCase`.
- `ValidateName(name, flavor)`: reject (`InvalidName`) empty, `.`, `..`, any
  `/` or `\0`, and on Windows any `\`.

Keep the parser as private static methods inside `FileSystemLocation`. Do not
call `System.IO.Path` or `System.Uri` in it: they follow the current OS, and
these tests must pass on every OS.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

Run: `pnpm --filter expo-file-system-dotnet test`
Expected: PASS (autolinking test).

- [ ] **Step 6: Commit**

```bash
git add packages/expo-file-system-dotnet pnpm-lock.yaml
git commit -m "feat(file-system): add package scaffold and location parser"
```

---

### Task 2: Platform guard, roots, records, and the module

**Files:**

- Create: `dotnet/ExpoFileSystemDotnet/FileSystemPlatform.cs`,
  `FileSystemRoots.cs`, `FileSystemRecords.cs`, `FileSystemModule.cs`
- Create: `dotnet/ExpoFileSystemDotnet.Tests/PlatformScope.cs`,
  `TemporaryDirectory.cs`, `FileSystemRootsTests.cs`,
  `FileSystemModuleTests.cs`

**Interfaces:**

- Consumes: `FileSystemLocation`, `PathFlavor`, `PathFlavors.Current`,
  `FileSystemErrors`.
- Produces:
  - `internal static class FileSystemPlatform` with
    `static void EnsureSupported()` and
    `static IDisposable OverrideForTests(bool supported)`.
  - `internal static class FileSystemRoots` with
    `static string BundleDirectoryFromProcessPath(string processPath, PathFlavor flavor)`,
    `static string BundleDirectory { get; }` (current process, cached),
    `static FileSystemLocation BundleLocation { get; }`,
    `static IDisposable OverrideBundleForTests(string nativePath)`.
  - Records (in `FileSystemRecords.cs`):
    `public sealed record CreateOptions(bool? Intermediates, bool? Overwrite, bool? Idempotent);`
    `public sealed record InfoOptions(bool? Md5);`
    `public sealed record PathInfo(bool Exists, bool? IsDirectory);`
    `public sealed record FileSystemFileInfo(bool Exists, string Uri, string? Md5, long? Size, long? ModificationTime, long? CreationTime);`
    `public sealed record FileSystemDirectoryInfo(bool Exists, string Uri, IReadOnlyList<string>? Files, long? Size, long? ModificationTime, long? CreationTime);`
    `public sealed record DirectoryEntryRecord(bool IsDirectory, string Uri);`
  - `[ExpoModule("FileSystem", Classes = ...)] public sealed partial class FileSystemModule : Module`

Option fields are `bool?` because the generated record codec reads a missing
lower-camel field as `undefined`, and a non-nullable `BoolCodec` rejects
`undefined`. `null` means the upstream default `false`.

`Classes` must name `FileSystemFile` and `FileSystemDirectory`, which Task 3
creates. In this task, create both classes as minimal stubs so the module
compiles: each is `[ExpoSharedObject] internal sealed partial class X :
SharedObject` with only a `[JS]` constructor `(string uri)` that calls
`FileSystemPlatform.EnsureSupported()`. Task 3 replaces both files. Do not
add any other member to them here. Declare both stubs `internal sealed
partial`.

- [ ] **Step 1: Write the failing tests**

`PlatformScope.cs` (test helper):

```csharp
namespace ExpoFileSystemDotnet.Tests;

internal static class PlatformScope
{
  internal static IDisposable Supported() => FileSystemPlatform.OverrideForTests(true);
}
```

`TemporaryDirectory.cs`: copy the `ExpoAssetDotnet.Tests` helper, with
prefix `expo-file-system-dotnet-` and namespace `ExpoFileSystemDotnet.Tests`.

`FileSystemRootsTests.cs`:

```csharp
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemRootsTests
{
  [Theory]
  [InlineData("/Applications/My App.app/Contents/MacOS/MyApp", "/Applications/My App.app")]
  [InlineData("/opt/tools/myapp", "/opt/tools")]
  [InlineData("/x/Contents/MacOS/MyApp", "/x/Contents/MacOS")]
  public void PosixBundleDirectory(string processPath, string expected)
  {
    Assert.Equal(
        expected,
        FileSystemRoots.BundleDirectoryFromProcessPath(processPath, PathFlavor.Posix)
    );
  }

  [Fact]
  public void WindowsBundleDirectoryIsExecutableDirectory()
  {
    Assert.Equal(
        @"C:\Program Files\My App",
        FileSystemRoots.BundleDirectoryFromProcessPath(
            @"C:\Program Files\My App\MyApp.exe",
            PathFlavor.Windows
        )
    );
  }

  [Fact]
  public void PlatformGuardRejectsUnsupportedOs()
  {
    using var _ = FileSystemPlatform.OverrideForTests(false);
    var error = Assert.Throws<PlatformNotSupportedException>(FileSystemPlatform.EnsureSupported);
    Assert.Equal("ExpoFileSystemDotnet supports only Windows and macOS.", error.Message);
  }
}
```

The `/x/Contents/MacOS/MyApp` case pins that only a directory whose name ends
with `.app` counts as a bundle.

`FileSystemModuleTests.cs` (Hermes, through the generated provider):

```csharp
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemModuleTests
{
  private static string Eval(ExpoModuleTestHost host, string code) =>
      host.Runtime.Execute(_ =>
      {
        using var value = host.Evaluate(code, "file-system-module.js");
        return value.AsString();
      });

  [Fact]
  public void RootsAreFileUrisOfContextDirectories()
  {
    using var platform = PlatformScope.Supported();
    using var cache = new TemporaryDirectory();
    using var documents = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(
        new AppDirectories(cache.Path, documents.Path),
        ExpoModulesProvider_ExpoFileSystemDotnet.Register
    );
    var expectedCache = FileSystemLocation
        .Parse(cache.Path, isDirectory: true, PathFlavors.Current).Uri;
    var expectedDocuments = FileSystemLocation
        .Parse(documents.Path, isDirectory: true, PathFlavors.Current).Uri;

    Assert.Equal(expectedCache, Eval(host, "_expoDotnet.modules.FileSystem.cacheDirectory"));
    Assert.Equal(
        expectedDocuments,
        Eval(host, "_expoDotnet.modules.FileSystem.documentDirectory")
    );
    Assert.EndsWith("/", expectedCache);
  }

  [Fact]
  public void UnconfiguredCacheDirectoryThrowsCatchableError()
  {
    using var platform = PlatformScope.Supported();
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoFileSystemDotnet.Register
    );

    var message = Eval(
        host,
        "(() => { try { _expoDotnet.modules.FileSystem.cacheDirectory; return 'no error'; }" +
        " catch (e) { return String(e.message); } })()"
    );

    Assert.Contains("CacheDirectory", message);
  }

  [Fact]
  public void UnsupportedPlatformThrowsCatchableError()
  {
    using var platform = FileSystemPlatform.OverrideForTests(false);
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoFileSystemDotnet.Register
    );

    var message = Eval(
        host,
        "(() => { try { _expoDotnet.modules.FileSystem.bundleDirectory; return 'no error'; }" +
        " catch (e) { return String(e.message); } })()"
    );

    Assert.Contains("supports only Windows and macOS", message);
  }

  [Fact]
  public void InfoReportsKind()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    using var host = ExpoModuleTestHost.Create(
        ExpoModulesProvider_ExpoFileSystemDotnet.Register
    );
    var dir = FileSystemLocation.Parse(temp.Path, isDirectory: true, PathFlavors.Current).Uri;

    var result = Eval(
        host,
        $"(() => {{ const m = _expoDotnet.modules.FileSystem;" +
        $" const f = m.info('{dir}a.txt'); const d = m.info('{dir}');" +
        $" const n = m.info('{dir}missing');" +
        $" return [f.exists, f.isDirectory, d.isDirectory, n.exists, n.isDirectory].join(','); }})()"
    );

    Assert.Equal("true,false,true,false,", result);
  }

  [Fact]
  public void DiskSpaceIsPositiveForConfiguredDocuments()
  {
    using var platform = PlatformScope.Supported();
    using var documents = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(
        new AppDirectories(persistentFilesDirectory: documents.Path),
        ExpoModulesProvider_ExpoFileSystemDotnet.Register
    );

    var result = Eval(
        host,
        "(() => { const m = _expoDotnet.modules.FileSystem;" +
        " return [m.totalDiskSpace > 0, m.availableDiskSpace >= 0," +
        " m.availableDiskSpace <= m.totalDiskSpace].join(','); })()"
    );

    Assert.Equal("true,true,true", result);
  }
}
```

`[].join` prints `null` as an empty string, so the `missing` entry ends with
`,` (null `isDirectory`). If the generated provider class name differs,
use the name the generator emits for assembly `ExpoFileSystemDotnet` (the
asset package uses `ExpoModulesProvider_ExpoAssetDotnet`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: build FAIL (types not defined).

- [ ] **Step 3: Implement**

`FileSystemPlatform.cs`:

```csharp
namespace ExpoFileSystemDotnet;

internal static class FileSystemPlatform
{
  private static bool? supportedOverride;

  internal static void EnsureSupported()
  {
    var supported = supportedOverride ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    if (!supported)
    {
      throw new PlatformNotSupportedException(
          "ExpoFileSystemDotnet supports only Windows and macOS."
      );
    }
  }

  internal static IDisposable OverrideForTests(bool supported)
  {
    var previous = supportedOverride;
    supportedOverride = supported;
    return new Restore(() => supportedOverride = previous);
  }

  private sealed class Restore(Action restore) : IDisposable
  {
    public void Dispose() => restore();
  }
}
```

`FileSystemRoots.cs`:

- `BundleDirectoryFromProcessPath`: parse `processPath` with
  `FileSystemLocation.Parse(processPath, isDirectory: false, flavor)`, take
  `Parent`. For Posix, if the parent's last three segments are
  `<name>.app`, `Contents`, `MacOS` (the `.app` check is
  `EndsWith(".app", StringComparison.OrdinalIgnoreCase)`), go up two more.
  Return `NativePath`.
- `BundleDirectory`: `Lazy<string>` over `Environment.ProcessPath`; a null
  process path throws `FileSystemException("Unable to determine the bundle
directory")`. A test override (same `Restore` pattern as the platform
  guard) takes precedence.
- `BundleLocation`: `FileSystemLocation.Parse(BundleDirectory, true, PathFlavors.Current)`.

`FileSystemModule.cs`:

```csharp
using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

[ExpoModule("FileSystem", Classes = new[] { typeof(FileSystemFile), typeof(FileSystemDirectory) })]
public sealed partial class FileSystemModule : Module
{
  private readonly DotnetRuntimeContext context;

  public FileSystemModule(DotnetRuntimeContext context)
      : base(context)
  {
    this.context = context;
  }

  [JS]
  public string DocumentDirectory => DirectoryUri(() => context.PersistentFilesDirectory);

  [JS]
  public string CacheDirectory => DirectoryUri(() => context.CacheDirectory);

  [JS]
  public string BundleDirectory
  {
    get
    {
      FileSystemPlatform.EnsureSupported();
      return FileSystemRoots.BundleDirectory;
    }
  }

  [JS]
  public long? TotalDiskSpace => ReadDrive(drive => drive.TotalSize);

  [JS]
  public long? AvailableDiskSpace => ReadDrive(drive => drive.AvailableFreeSpace);

  [JS]
  public PathInfo Info(string uri)
  {
    FileSystemPlatform.EnsureSupported();
    var path = FileSystemLocation.Parse(uri, isDirectory: false, PathFlavors.Current).NativePath;
    if (Directory.Exists(path))
    {
      return new PathInfo(true, true);
    }
    return File.Exists(path) ? new PathInfo(true, false) : new PathInfo(false, null);
  }

  private static string DirectoryUri(Func<string> read)
  {
    FileSystemPlatform.EnsureSupported();
    return FileSystemLocation.Parse(read(), isDirectory: true, PathFlavors.Current).Uri;
  }

  private long? ReadDrive(Func<DriveInfo, long> read)
  {
    FileSystemPlatform.EnsureSupported();
    try
    {
      var path = Path.GetFullPath(context.PersistentFilesDirectory);
      var comparison = OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;
      var drive = DriveInfo.GetDrives()
          .Where(d => d.IsReady && path.StartsWith(d.RootDirectory.FullName, comparison))
          .OrderByDescending(d => d.RootDirectory.FullName.Length)
          .FirstOrDefault();
      return drive is null ? null : read(drive);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException
        or AppDirectoryNotConfiguredException)
    {
      return null;
    }
  }
}
```

`ObjectDisposedException` and `PlatformNotSupportedException` are not caught.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "feat(file-system): add FileSystem module roots and info"
```

---

### Task 3: Shared path base and lifecycle operations

**Files:**

- Create: `dotnet/ExpoFileSystemDotnet/FileSystemPath.cs`
- Replace: `dotnet/ExpoFileSystemDotnet/FileSystemFile.cs`,
  `FileSystemDirectory.cs` (stubs from Task 2)
- Create: `dotnet/ExpoFileSystemDotnet.Tests/FileSystemPathTests.cs`

**Interfaces:**

- Consumes: `FileSystemLocation`, `FileSystemErrors`, `FileSystemRoots`,
  `FileSystemPlatform`, `CreateOptions`.
- Produces (used by Tasks 4–6):
  - `internal abstract class FileSystemPath : SharedObject` with
    `protected FileSystemPath(FileSystemLocation location)`,
    `internal FileSystemLocation Location { get; }` (read under lock),
    `protected abstract void EnsureCorrectType(string nativePath)`,
    `protected static void EnsureWritable(FileSystemLocation target)`,
    `protected void DeleteCore()`, `protected void RenameCore(string newName)`.
  - `FileSystemFile` and `FileSystemDirectory` are `internal sealed partial`
    classes. The spec allows internal shared-object classes, and a public
    class cannot derive from the internal base.
  - `FileSystemFile` members: `[JS] FileSystemFile(string uri)`,
    `internal FileSystemFile(FileSystemLocation location)`, `[JS] string Uri`,
    `[JS] bool Exists`, `[JS] void ValidatePath()`,
    `[JS] void Create(CreateOptions? options = null)`, `[JS] void Delete()`,
    `[JS] void Rename(string newName)`.
  - `FileSystemDirectory`: the same set with directory semantics.

`FileSystemPath` must not carry `[ExpoSharedObject]` or `[JS]` members. All
`[JS]` members live on the sealed classes and call `protected`/`internal`
helpers on the base.

- [ ] **Step 1: Write the failing tests**

`FileSystemPathTests.cs` tests the C# classes directly (no Hermes):

```csharp
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemPathTests
{
  private static FileSystemFile NewFile(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current));

  private static FileSystemDirectory NewDirectory(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current));

  [Fact]
  public void FileCreateMakesEmptyFile()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = NewFile(Path.Combine(temp.Path, "a.txt"));

    file.Create();

    Assert.True(file.Exists);
    Assert.Equal(0, new FileInfo(Path.Combine(temp.Path, "a.txt")).Length);
  }

  [Fact]
  public void FileCreateWithoutOverwriteKeepsContents()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.txt");
    File.WriteAllText(path, "keep");
    var file = NewFile(path);

    var error = Assert.Throws<FileSystemException>(() => file.Create());

    Assert.Equal($"File '{file.Uri}' already exists", error.Message);
    Assert.Equal("keep", File.ReadAllText(path));
  }

  [Fact]
  public void FileCreateWithOverwriteTruncates()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.txt");
    File.WriteAllText(path, "old");

    NewFile(path).Create(new CreateOptions(null, true, null));

    Assert.Equal(string.Empty, File.ReadAllText(path));
  }

  [Fact]
  public void FileCreateNeedsIntermediatesForMissingParent()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "x", "y", "a.txt");

    var error = Assert.Throws<FileSystemException>(() => NewFile(path).Create());
    Assert.StartsWith("Unable to create file or directory: ", error.Message);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "x")));

    NewFile(path).Create(new CreateOptions(true, null, null));
    Assert.True(File.Exists(path));
  }

  [Fact]
  public void DirectoryCreateRules()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "d");
    var directory = NewDirectory(path);

    directory.Create();
    File.WriteAllText(Path.Combine(path, "child.txt"), "x");

    var error = Assert.Throws<FileSystemException>(() => directory.Create());
    Assert.Equal($"File '{directory.Uri}' already exists", error.Message);

    directory.Create(new CreateOptions(null, null, true));
    Assert.True(File.Exists(Path.Combine(path, "child.txt")));

    directory.Create(new CreateOptions(null, true, null));
    Assert.Empty(Directory.EnumerateFileSystemEntries(path));
  }

  [Fact]
  public void DirectoryCreateWithoutIntermediatesFailsForMissingParent()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "x", "d");

    var error = Assert.Throws<FileSystemException>(() => NewDirectory(path).Create());
    Assert.StartsWith("Unable to create file or directory: ", error.Message);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "x")));

    NewDirectory(path).Create(new CreateOptions(true, null, null));
    Assert.True(Directory.Exists(path));
  }

  [Fact]
  public void TypeMismatchFailsBeforeSideEffects()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var dirPath = Path.Combine(temp.Path, "d");
    Directory.CreateDirectory(dirPath);
    var filePath = Path.Combine(temp.Path, "f");
    File.WriteAllText(filePath, "x");

    var fileError = Assert.Throws<FileSystemException>(() => NewFile(dirPath).Delete());
    Assert.Equal("A folder with the same name already exists in the file location", fileError.Message);
    var dirError = Assert.Throws<FileSystemException>(() => NewDirectory(filePath).Delete());
    Assert.Equal("A file with the same name already exists in the directory location", dirError.Message);
    Assert.True(Directory.Exists(dirPath));
    Assert.True(File.Exists(filePath));
    Assert.False(NewFile(dirPath).Exists);
    Assert.False(NewDirectory(filePath).Exists);
  }

  [Fact]
  public void DeleteIsRecursiveAndRejectsMissing()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "d");
    Directory.CreateDirectory(Path.Combine(path, "nested"));
    File.WriteAllText(Path.Combine(path, "nested", "a.txt"), "x");

    NewDirectory(path).Delete();
    Assert.False(Directory.Exists(path));

    var error = Assert.Throws<FileSystemException>(() => NewDirectory(path).Delete());
    Assert.Equal("Unable to delete file or directory: path does not exist", error.Message);
  }

  [Fact]
  public void RenameUpdatesUriInPlace()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.txt");
    File.WriteAllText(path, "x");
    var file = NewFile(path);

    file.Rename("b.txt");

    Assert.EndsWith("/b.txt", file.Uri);
    Assert.True(File.Exists(Path.Combine(temp.Path, "b.txt")));
    Assert.False(File.Exists(path));
    Assert.Throws<FileSystemException>(() => file.Rename("../c.txt"));
  }

  [Fact]
  public void BundleTreeIsReadOnly()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var bundle = FileSystemRoots.OverrideBundleForTests(temp.Path);
    var path = Path.Combine(temp.Path, "a.txt");
    File.WriteAllText(path, "x");
    var file = NewFile(path);

    var error = Assert.Throws<FileSystemException>(() => file.Delete());
    Assert.Equal($"Missing permission for uri: {file.Uri}", error.Message);
    Assert.Throws<FileSystemException>(() => file.Rename("b.txt"));
    Assert.Throws<FileSystemException>(() => NewFile(Path.Combine(temp.Path, "n.txt")).Create());
    Assert.True(File.Exists(path));
    Assert.False(File.Exists(Path.Combine(temp.Path, "n.txt")));
  }

  [Fact]
  public void ValidatePathChecksTrailingSlash()
  {
    using var platform = PlatformScope.Supported();
    var root = Path.GetPathRoot(Path.GetTempPath())!;
    var error = Assert.Throws<FileSystemException>(() => NewFile(root).ValidatePath());
    Assert.Equal("tried to create a file with a directory path", error.Message);
    NewDirectory(root).ValidatePath();
  }
}
```

Default test bundle: every test except `BundleTreeIsReadOnly` runs with the
real process bundle (the test runner's directory), which never contains the
temporary directory.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: build FAIL (members missing).

- [ ] **Step 3: Implement `FileSystemPath`**

```csharp
using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

internal abstract class FileSystemPath : SharedObject
{
  private readonly Lock gate = new();
  private FileSystemLocation location;

  protected FileSystemPath(FileSystemLocation location)
  {
    this.location = location;
  }

  internal FileSystemLocation Location
  {
    get
    {
      lock (gate)
      {
        return location;
      }
    }
  }

  protected abstract void EnsureCorrectType(string nativePath);

  protected static void EnsureWritable(FileSystemLocation target)
  {
    if (target.IsWithin(FileSystemRoots.BundleLocation))
    {
      throw FileSystemErrors.MissingPermission(target.Uri);
    }
  }

  protected void DeleteCore()
  {
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (!File.Exists(path) && !Directory.Exists(path))
    {
      throw FileSystemErrors.UnableToDelete("path does not exist");
    }
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
      else
      {
        File.Delete(path);
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw FileSystemErrors.UnableToDelete(error.Message, error);
    }
  }

  protected void RenameCore(string newName)
  {
    lock (gate)
    {
      var target = location.WithName(newName);
      EnsureWritable(location);
      EnsureWritable(target);
      EnsureCorrectType(location.NativePath);
      if (location.IsDirectory)
      {
        Directory.Move(location.NativePath, target.NativePath);
      }
      else
      {
        File.Move(location.NativePath, target.NativePath);
      }
      location = target;
    }
  }
}
```

`FileSystemPath` and both sealed classes are `internal`. The module class
and the records stay `public`, because the module's public `[JS]` members
return them.

`FileSystemFile.cs` (directory version mirrors it with directory rules):

```csharp
using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

[ExpoSharedObject]
internal sealed partial class FileSystemFile : FileSystemPath
{
  [JS]
  public FileSystemFile(string uri)
      : this(ParseSupported(uri))
  {
  }

  internal FileSystemFile(FileSystemLocation location)
      : base(location)
  {
  }

  [JS]
  public string Uri => Location.Uri;

  [JS]
  public bool Exists => File.Exists(Location.NativePath);

  [JS]
  public void ValidatePath()
  {
    if (Location.HasDirectoryPath)
    {
      throw FileSystemErrors.WrongTypeFile();
    }
  }

  [JS]
  public void Create(CreateOptions? options = null)
  {
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (File.Exists(path) && options?.Overwrite != true)
    {
      throw FileSystemErrors.AlreadyExists(target.Uri);
    }
    try
    {
      if (options?.Intermediates == true)
      {
        Directory.CreateDirectory(target.Parent.NativePath);
      }
      using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw FileSystemErrors.UnableToCreate(error.Message, error);
    }
  }

  [JS]
  public void Delete() => DeleteCore();

  [JS]
  public void Rename(string newName) => RenameCore(newName);

  protected override void EnsureCorrectType(string nativePath)
  {
    if (Directory.Exists(nativePath))
    {
      throw FileSystemErrors.FolderAtFileLocation();
    }
  }

  private static FileSystemLocation ParseSupported(string uri)
  {
    FileSystemPlatform.EnsureSupported();
    return FileSystemLocation.Parse(uri, isDirectory: false, PathFlavors.Current);
  }
}
```

`FileSystemDirectory.Create(options)`:

```csharp
  [JS]
  public void Create(CreateOptions? options = null)
  {
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (Directory.Exists(path))
    {
      if (options?.Idempotent == true)
      {
        return;
      }
      if (options?.Overwrite != true)
      {
        throw FileSystemErrors.AlreadyExists(target.Uri);
      }
    }
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
      if (options?.Intermediates != true && !Directory.Exists(target.Parent.NativePath))
      {
        throw FileSystemErrors.UnableToCreate("parent directory does not exist");
      }
      Directory.CreateDirectory(path);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw FileSystemErrors.UnableToCreate(error.Message, error);
    }
  }
```

`Exists` is `Directory.Exists`, `ValidatePath` throws `WrongTypeDirectory()`
when `!Location.HasDirectoryPath`, `EnsureCorrectType` throws
`FileAtDirectoryLocation()` when `File.Exists(nativePath)`, and the `[JS]`
constructor parses with `isDirectory: true`.

If the generator reports a diagnostic for the intermediate `FileSystemPath`
base or the internal constructor, stop and report the diagnostic text. Do not
restructure around it without approval.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "feat(file-system): add file and directory lifecycle operations"
```

---

### Task 4: File contents, metadata, and digest

**Files:**

- Modify: `dotnet/ExpoFileSystemDotnet/FileSystemFile.cs`
- Create: `dotnet/ExpoFileSystemDotnet/FileMetadata.cs`
- Create: `dotnet/ExpoFileSystemDotnet.Tests/FileSystemFileTests.cs`

**Interfaces:**

- Consumes: `FileSystemFile`, `Location`, `EnsureCorrectType`,
  `InfoOptions`, `FileSystemFileInfo`.
- Produces:
  - `internal static class FileMetadata` with
    `static long? ToUnixMilliseconds(DateTime utc)`,
    `static long? ModificationTime(string nativePath, bool isDirectory)`,
    `static long? CreationTime(string nativePath, bool isDirectory)`,
    `static string HashHex(string nativePath, string algorithm)`,
    `static Task<string> HashHexAsync(string nativePath, string algorithm, CancellationToken token)`.
  - `FileSystemFile`: `[JS] long Size`, `[JS] string? Md5`,
    `[JS] long? ModificationTime`, `[JS] long? LastModified`,
    `[JS] long? CreationTime`, `[JS] FileSystemFileInfo Info(InfoOptions? options = null)`,
    `[JS] string TextSync()`, `[JS] Task<string> Text()`,
    `[JS] string Base64Sync()`, `[JS] Task<string> Base64()`,
    `[JS] Task<string> Digest(string algorithm)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemFileTests
{
  private static (TemporaryDirectory Temp, string Path, FileSystemFile File) Fixture(byte[] contents)
  {
    var temp = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temp.Path, "a.txt");
    System.IO.File.WriteAllBytes(path, contents);
    var file = new FileSystemFile(
        FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current)
    );
    return (temp, path, file);
  }

  [Fact]
  public async Task TextDecodesUtf8AndHonorsBom()
  {
    using var platform = PlatformScope.Supported();
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("zażółć")).ToArray();
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      Assert.Equal("zażółć", file.TextSync());
      Assert.Equal("zażółć", await file.Text());
    }
  }

  [Fact]
  public async Task Base64HasNoLineBreaks()
  {
    using var platform = PlatformScope.Supported();
    var bytes = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      var expected = Convert.ToBase64String(bytes);
      Assert.Equal(expected, file.Base64Sync());
      Assert.Equal(expected, await file.Base64());
      Assert.DoesNotContain('\n', expected);
    }
  }

  [Theory]
  [InlineData("MD5")]
  [InlineData("SHA-1")]
  [InlineData("SHA-256")]
  [InlineData("SHA-384")]
  [InlineData("SHA-512")]
  public async Task DigestReturnsLowercaseHex(string algorithm)
  {
    using var platform = PlatformScope.Supported();
    var bytes = Encoding.UTF8.GetBytes("hello");
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      byte[] expected = algorithm switch
      {
        "MD5" => MD5.HashData(bytes),
        "SHA-1" => SHA1.HashData(bytes),
        "SHA-256" => SHA256.HashData(bytes),
        "SHA-384" => SHA384.HashData(bytes),
        _ => SHA512.HashData(bytes),
      };
      Assert.Equal(Convert.ToHexStringLower(expected), await file.Digest(algorithm));
    }
  }

  [Fact]
  public async Task UnsupportedDigestFails()
  {
    using var platform = PlatformScope.Supported();
    var (temp, _, file) = Fixture([1]);
    using (temp)
    {
      var error = await Assert.ThrowsAsync<FileSystemException>(() => file.Digest("SHA-3"));
      Assert.Equal("Unsupported digest algorithm: SHA-3", error.Message);
    }
  }

  [Fact]
  public void MetadataForExistingFile()
  {
    using var platform = PlatformScope.Supported();
    var (temp, path, file) = Fixture(Encoding.UTF8.GetBytes("hello"));
    using (temp)
    {
      var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
      System.IO.File.SetLastWriteTimeUtc(path, written);
      var expectedMs = new DateTimeOffset(written).ToUnixTimeMilliseconds();

      Assert.Equal(5, file.Size);
      Assert.Equal(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("hello"))), file.Md5);
      Assert.Equal(expectedMs, file.ModificationTime);
      Assert.Equal(expectedMs, file.LastModified);
      Assert.NotNull(file.CreationTime);

      var info = file.Info(new InfoOptions(true));
      Assert.True(info.Exists);
      Assert.Equal(file.Uri, info.Uri);
      Assert.Equal(file.Md5, info.Md5);
      Assert.Equal(5, info.Size);
      Assert.Equal(expectedMs, info.ModificationTime);
      Assert.Null(file.Info().Md5);
    }
  }

  [Fact]
  public void MetadataForMissingFile()
  {
    using var platform = PlatformScope.Supported();
    var (temp, path, file) = Fixture([1]);
    using (temp)
    {
      System.IO.File.Delete(path);
      Assert.Equal(0, file.Size);
      Assert.Null(file.Md5);
      Assert.Null(file.ModificationTime);
      Assert.Null(file.CreationTime);
      Assert.Equal(new FileSystemFileInfo(false, file.Uri, null, null, null, null), file.Info());
      Assert.ThrowsAny<Exception>(() => file.TextSync());
    }
  }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: build FAIL (members missing).

- [ ] **Step 3: Implement**

`FileMetadata.cs`:

- `ModificationTime` / `CreationTime`: return `null` unless the item exists
  with the expected kind; else `File.GetLastWriteTimeUtc` /
  `File.GetCreationTimeUtc` (or the `Directory.*` versions) converted with
  `new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()`.
  Catch `IOException` and `UnauthorizedAccessException` → `null`.
- `HashHex` / `HashHexAsync`: map `MD5` → `MD5.HashData(stream)` /
  `MD5.HashDataAsync(stream, token)`, `SHA-1` → `SHA1`, `SHA-256` →
  `SHA256`, `SHA-384` → `SHA384`, `SHA-512` → `SHA512`; anything else →
  `FileSystemErrors.UnsupportedDigest(algorithm)` before opening the file.
  Open with `new FileStream(path, FileMode.Open, FileAccess.Read,
FileShare.Read, 81920, useAsync: true)` for the async version. Return
  `Convert.ToHexStringLower(hash)`.

`FileSystemFile` additions:

```csharp
  [JS]
  public long Size
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        return File.Exists(path) ? new FileInfo(path).Length : 0;
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return 0;
      }
    }
  }

  [JS]
  public string? Md5
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        return File.Exists(path) ? FileMetadata.HashHex(path, "MD5") : null;
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }
  }

  [JS]
  public long? ModificationTime => FileMetadata.ModificationTime(Location.NativePath, isDirectory: false);

  [JS]
  public long? LastModified => ModificationTime;

  [JS]
  public long? CreationTime => FileMetadata.CreationTime(Location.NativePath, isDirectory: false);

  [JS]
  public FileSystemFileInfo Info(InfoOptions? options = null)
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    if (!File.Exists(target.NativePath))
    {
      return new FileSystemFileInfo(false, target.Uri, null, null, null, null);
    }
    return new FileSystemFileInfo(
        true,
        target.Uri,
        options?.Md5 == true ? FileMetadata.HashHex(target.NativePath, "MD5") : null,
        new FileInfo(target.NativePath).Length,
        FileMetadata.ModificationTime(target.NativePath, isDirectory: false),
        FileMetadata.CreationTime(target.NativePath, isDirectory: false)
    );
  }

  [JS]
  public string TextSync()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return File.ReadAllText(target.NativePath);
  }

  [JS]
  public Task<string> Text()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(() => File.ReadAllTextAsync(target.NativePath));
  }

  [JS]
  public string Base64Sync()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Convert.ToBase64String(File.ReadAllBytes(target.NativePath));
  }

  [JS]
  public Task<string> Base64()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(async () =>
        Convert.ToBase64String(await File.ReadAllBytesAsync(target.NativePath)));
  }

  [JS]
  public Task<string> Digest(string algorithm)
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(() => FileMetadata.HashHexAsync(target.NativePath, algorithm, CancellationToken.None));
  }
```

`File.ReadAllText` detects a UTF-8 BOM and defaults to UTF-8 without one.
Each async member reads `Location` once, before `Task.Run`. That is the
"location captured at start" rule. A test of it would race the thread pool,
so reviewers check it in the code instead.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "feat(file-system): add file reads, metadata, and digest"
```

---

### Task 5: Directory metadata, listing, and child creation

**Files:**

- Modify: `dotnet/ExpoFileSystemDotnet/FileSystemDirectory.cs`
- Create: `dotnet/ExpoFileSystemDotnet.Tests/FileSystemDirectoryTests.cs`

**Interfaces:**

- Consumes: `FileSystemDirectory`, `FileSystemFile(FileSystemLocation)`,
  `FileMetadata`, `FileSystemDirectoryInfo`, `DirectoryEntryRecord`.
- Produces: `[JS] long? Size`, `[JS] FileSystemDirectoryInfo Info()`,
  `[JS] IReadOnlyList<DirectoryEntryRecord> ListAsRecords()`,
  `[JS] FileSystemFile CreateFile(string name, string? content)`,
  `[JS] FileSystemDirectory CreateDirectory(string name)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemDirectoryTests
{
  private static FileSystemDirectory NewDirectory(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current));

  private static void MakeHidden(string path)
  {
    if (OperatingSystem.IsWindows())
    {
      File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }
  }

  [Fact]
  public void SizeInfoAndListIncludeHiddenEntries()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllBytes(Path.Combine(temp.Path, "a.bin"), new byte[3]);
    var hidden = Path.Combine(temp.Path, ".hidden");
    File.WriteAllBytes(hidden, new byte[4]);
    MakeHidden(hidden);
    Directory.CreateDirectory(Path.Combine(temp.Path, "sub"));
    File.WriteAllBytes(Path.Combine(temp.Path, "sub", "b.bin"), new byte[5]);
    var directory = NewDirectory(temp.Path);

    Assert.Equal(12, directory.Size);

    var info = directory.Info();
    Assert.True(info.Exists);
    Assert.Equal(directory.Uri, info.Uri);
    Assert.Equal(new[] { ".hidden", "a.bin", "sub" }, info.Files!.Order(StringComparer.Ordinal));
    Assert.Equal(12, info.Size);

    var records = directory.ListAsRecords().OrderBy(r => r.Uri, StringComparer.Ordinal).ToList();
    Assert.Equal(3, records.Count);
    Assert.Equal(directory.Uri + ".hidden", records[0].Uri);
    Assert.False(records[0].IsDirectory);
    Assert.Equal(directory.Uri + "sub/", records[2].Uri);
    Assert.True(records[2].IsDirectory);
  }

  [Fact]
  public void MissingDirectory()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var directory = NewDirectory(Path.Combine(temp.Path, "missing"));

    Assert.False(directory.Exists);
    Assert.Null(directory.Size);
    Assert.Equal(new FileSystemDirectoryInfo(false, directory.Uri, null, null, null, null), directory.Info());
    Assert.ThrowsAny<Exception>(() => directory.ListAsRecords());
  }

  [Fact]
  public void CreateFileAndDirectoryReturnNewInstances()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var directory = NewDirectory(temp.Path);

    var file = directory.CreateFile("a b.txt", "ignored");
    var child = directory.CreateDirectory("sub");

    Assert.Equal(directory.Uri + "a%20b.txt", file.Uri);
    Assert.Equal(0, new FileInfo(Path.Combine(temp.Path, "a b.txt")).Length);
    Assert.Equal(directory.Uri + "sub/", child.Uri);
    Assert.True(Directory.Exists(Path.Combine(temp.Path, "sub")));
    Assert.Throws<FileSystemException>(() => directory.CreateFile("a b.txt", null));
    Assert.Throws<FileSystemException>(() => directory.CreateDirectory("../escape"));
  }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: build FAIL.

- [ ] **Step 3: Implement**

```csharp
  private static readonly EnumerationOptions AllEntries = new()
  {
    AttributesToSkip = 0,
    IgnoreInaccessible = false,
    RecurseSubdirectories = false,
  };

  private static readonly EnumerationOptions AllFilesRecursive = new()
  {
    AttributesToSkip = 0,
    IgnoreInaccessible = false,
    RecurseSubdirectories = true,
  };

  [JS]
  public long? Size
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        if (!Directory.Exists(path))
        {
          return null;
        }
        return new DirectoryInfo(path)
            .EnumerateFiles("*", AllFilesRecursive)
            .Sum(file => file.Length);
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }
  }

  [JS]
  public FileSystemDirectoryInfo Info()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    if (!Directory.Exists(target.NativePath))
    {
      return new FileSystemDirectoryInfo(false, target.Uri, null, null, null, null);
    }
    var files = Directory
        .EnumerateFileSystemEntries(target.NativePath, "*", AllEntries)
        .Select(Path.GetFileName)
        .Select(name => name!)
        .ToList();
    return new FileSystemDirectoryInfo(
        true,
        target.Uri,
        files,
        Size,
        FileMetadata.ModificationTime(target.NativePath, isDirectory: true),
        FileMetadata.CreationTime(target.NativePath, isDirectory: true)
    );
  }

  [JS]
  public IReadOnlyList<DirectoryEntryRecord> ListAsRecords()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Directory
        .EnumerateFileSystemEntries(target.NativePath, "*", AllEntries)
        .Select(entry =>
        {
          var isDirectory = Directory.Exists(entry);
          var child = target.Child(Path.GetFileName(entry), isDirectory);
          return new DirectoryEntryRecord(isDirectory, child.Uri);
        })
        .ToList();
  }

  [JS]
  public FileSystemFile CreateFile(string name, string? content)
  {
    var file = new FileSystemFile(Location.Child(name, isDirectory: false));
    file.Create();
    return file;
  }

  [JS]
  public FileSystemDirectory CreateDirectory(string name)
  {
    var directory = new FileSystemDirectory(Location.Child(name, isDirectory: true));
    directory.Create();
    return directory;
  }
```

`content` is ignored on purpose (upstream iOS ignores it, and the upstream
facade passes `mimeType` in that position). Add a one-line comment saying so.
`EnumerationOptions` with `AttributesToSkip = 0` is the Review Focus 4 fix.
`DirectoryInfo` in `Size` is `System.IO.DirectoryInfo`; this is why the
record is named `FileSystemDirectoryInfo`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "feat(file-system): add directory listing, metadata, and children"
```

---

### Task 6: Hermes binding tests

**Files:**

- Create: `dotnet/ExpoFileSystemDotnet.Tests/FileSystemBindingTests.cs`

**Interfaces:**

- Consumes: the generated provider `ExpoModulesProvider_ExpoFileSystemDotnet`
  and every `[JS]` member from Tasks 2–5.
- Produces: nothing new. This task proves the generated boundary.

- [ ] **Step 1: Write the tests**

```csharp
using Expo.ModulesCore;
using Expo.ModulesCore.Generated;
using Expo.ModulesCore.Testing;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemBindingTests
{
  private static string Eval(ExpoModuleTestHost host, string code) =>
      host.Runtime.Execute(_ =>
      {
        using var value = host.Evaluate(code, "file-system-binding.js");
        return value.AsString();
      });

  private static string DirUri(string path) =>
      FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current).Uri;

  [Fact]
  public void JavaScriptSubclassKeepsItsPrototype()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          class File extends m.FileSystemFile {
            constructor(uri) { super(uri); this.validatePath(); }
            get name() { return this.uri.split('/').pop(); }
          }
          const f = new File('{{DirUri(temp.Path)}}a.txt');
          return [
            Object.getPrototypeOf(f) === File.prototype,
            f instanceof File,
            f instanceof m.FileSystemFile,
            f.name,
            f.exists,
            f.textSync(),
          ].join(',');
        })()
        """
    );

    Assert.Equal("true,true,true,a.txt,true,x", result);
  }

  [Fact]
  public void LaterStepMembersAreAbsent()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const f = new m.FileSystemFile('{{DirUri(temp.Path)}}a.txt');
          const d = new m.FileSystemDirectory('{{DirUri(temp.Path)}}');
          const absent = ['write', 'writeSync', 'bytes', 'bytesSync', 'copy', 'copySync',
            'move', 'moveSync', 'open', 'type'];
          return absent.filter((name) => name in f || name in d).join(',');
        })()
        """
    );

    Assert.Equal(string.Empty, result);
  }

  [Fact]
  public async Task AsyncFailureRejectsWithCatchableError()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    File.WriteAllText(Path.Combine(temp.Path, "a.txt"), "x");
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var error = await Assert.ThrowsAsync<JavaScriptPromiseRejectedException>(
        () => host.EvaluatePromiseAsync(
            $"new _expoDotnet.modules.FileSystem.FileSystemFile('{DirUri(temp.Path)}a.txt').digest('SHA-3')",
            TestContext.Current.CancellationToken
        )
    );

    Assert.Contains("Unsupported digest algorithm: SHA-3", error.Message);
  }

  [Fact]
  public void SyncFailureAndInvalidLocationThrowCatchableErrors()
  {
    using var platform = PlatformScope.Supported();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        """
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const messages = [];
          try { new m.FileSystemFile('relative/a.txt'); } catch (e) { messages.push(e.message); }
          try { new m.FileSystemFile('http://example.com/a'); } catch (e) { messages.push(e.message); }
          return messages.map((message) => message.includes('Invalid file system location')).join(',');
        })()
        """
    );

    Assert.Equal("true,true", result);
  }

  [Fact]
  public void CreateFileReturnsNativeInstanceAndOptionsDecode()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var host = ExpoModuleTestHost.Create(ExpoModulesProvider_ExpoFileSystemDotnet.Register);

    var result = Eval(
        host,
        $$"""
        (() => {
          const m = _expoDotnet.modules.FileSystem;
          const d = new m.FileSystemDirectory('{{DirUri(temp.Path)}}nested/deep');
          d.create({ intermediates: true });
          d.create({ idempotent: true });
          const f = d.createFile('a.txt', null);
          const info = f.info({ md5: true });
          const records = d.listAsRecords();
          return [
            f instanceof m.FileSystemFile,
            f.uri.endsWith('/nested/deep/a.txt'),
            info.exists,
            info.size,
            typeof info.md5,
            records.length,
            records[0].isDirectory,
          ].join(',');
        })()
        """
    );

    Assert.Equal("true,true,true,0,string,1,false", result);
  }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test packages/expo-file-system-dotnet/dotnet/ExpoFileSystemDotnet.Tests/ExpoFileSystemDotnet.Tests.csproj`
Expected: PASS.

If `JavaScriptSubclassKeepsItsPrototype` fails, this is the core gap the spec
names. Stop, record the exact failure, and report it before changing any file
under `packages/expo-modules-dotnet/`. The core fix needs its own reviewed
change.

- [ ] **Step 3: Run the full managed suite and the hot-path check**

Run: `scripts/test-managed.sh`
Expected: exit 0, and the output lists `ExpoFileSystemDotnet.Tests`.

Run: `rg "Assembly.GetTypes|MethodInfo.Invoke|Delegate.DynamicInvoke|object\?\[\]|JsonSerializer" packages/expo-file-system-dotnet`
Expected: no matches (exit 1).

- [ ] **Step 4: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "test(file-system): cover the generated JavaScript boundary"
```

---

### Task 7: TypeScript facade

**Files:**

- Create: `src/ExpoFileSystem.ts`, `src/File.ts`, `src/Directory.ts`,
  `src/Paths.ts`, `src/index.ts`, `src/pathUtilities/index.ts`,
  `src/pathUtilities/path.ts`, `src/pathUtilities/url.ts`,
  `src/public-api.typecheck.ts`
- Create: `src/__tests__/facade.test.ts`, `src/__tests__/pathUtilities.test.ts`

**Interfaces:**

- Consumes: native members from Tasks 2–5 under their JS names.
- Produces: public exports `File`, `Directory`, `Paths`, and the types
  `CreateOptions`, `InfoOptions`, `FileInfo`, `DirectoryInfo`, `PathInfo`.

- [ ] **Step 1: Port `pathUtilities` and write its tests**

Copy upstream `src/pathUtilities/path.ts`, `url.ts`, and `index.ts` from
`expo-file-system@58.0.7`. Keep the Node.js MIT license header in `path.ts`
and `url.ts`. Add one line at the top of each file:
`// Ported from expo-file-system@58.0.7 (MIT).` Change only the import in
`index.ts` to `import type { Directory, File } from '../index';`.

`pathUtilities.test.ts`:

```ts
import { describe, expect, it } from "vitest";

import { PathUtilities } from "../pathUtilities";

describe("PathUtilities", () => {
  it("joins onto a file URI and encodes later segments", () => {
    expect(PathUtilities.join("file:///docs/", "a b", "c#.txt")).toBe(
      "file:///docs/a%20b/c%23.txt",
    );
  });

  it("joins plain POSIX paths", () => {
    expect(PathUtilities.join("/docs", "..", "x.txt")).toBe("/x.txt");
  });

  it("keeps a single Windows drive path for native canonicalization", () => {
    expect(PathUtilities.join("C:\\x\\a.txt").toLowerCase()).toBe(
      "c:\\x\\a.txt",
    );
  });

  it("exposes basename, extname, and dirname", () => {
    expect(PathUtilities.basename("file:///docs/a.txt")).toBe("a.txt");
    expect(PathUtilities.extname("file:///docs/a.txt")).toBe(".txt");
    expect(PathUtilities.dirname("file:///docs/a.txt")).toBe("file:///docs");
  });
});
```

If an expectation differs from what the ported upstream code returns, keep
the upstream behavior and change the expectation. Then note the difference
in the commit message.

- [ ] **Step 2: Write the facade tests**

`facade.test.ts` mocks the native module with plain classes:

```ts
import { beforeEach, describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => {
  class FileSystemFile {
    uri: string;
    constructor(uri: string) {
      this.uri = uri.replace(/\/+$/, "");
    }
    validatePath() {}
  }
  class FileSystemDirectory {
    uri: string;
    constructor(uri: string) {
      this.uri = uri.endsWith("/") ? uri : `${uri}/`;
    }
    validatePath() {}
    listAsRecords() {
      return [
        { isDirectory: false, uri: `${this.uri}a.txt` },
        { isDirectory: true, uri: `${this.uri}sub/` },
      ];
    }
    createFile(name: string) {
      return new FileSystemFile(`${this.uri}${name}`);
    }
    createDirectory(name: string) {
      return new FileSystemDirectory(`${this.uri}${name}`);
    }
  }
  return {
    module: {
      FileSystemFile,
      FileSystemDirectory,
      documentDirectory: "file:///docs/",
      cacheDirectory: "file:///cache/",
      bundleDirectory: "/app/X.app",
      totalDiskSpace: 100,
      availableDiskSpace: 40,
      info: vi.fn(() => ({ exists: true, isDirectory: false })),
    },
  };
});

vi.mock("expo-modules-dotnet", () => ({
  DotnetSharedObject: class {},
  requireDotnetModule: vi.fn(() => native.module),
}));

describe("facade", () => {
  beforeEach(() => {
    native.module.info.mockClear();
  });

  it("builds files from directories and segments", async () => {
    const { Directory, File, Paths } = await import("../index");
    const file = new File(Paths.document, "notes", "a b.txt");
    expect(file).toBeInstanceOf(File);
    expect(file).toBeInstanceOf(native.module.FileSystemFile);
    expect(file.uri).toBe("file:///docs/notes/a%20b.txt");
    expect(file.name).toBe("a b.txt");
    expect(file.extension).toBe(".txt");
    expect(file.parentDirectory).toBeInstanceOf(Directory);
  });

  it("maps listed records to facade instances", async () => {
    const { Directory, File } = await import("../index");
    const entries = new Directory("file:///docs/").list();
    expect(entries[0]).toBeInstanceOf(File);
    expect(entries[1]).toBeInstanceOf(Directory);
  });

  it("wraps created children", async () => {
    const { Directory, File } = await import("../index");
    const directory = new Directory("file:///docs/");
    expect(directory.createFile("a.txt", null)).toBeInstanceOf(File);
    expect(directory.createDirectory("sub")).toBeInstanceOf(Directory);
  });

  it("exposes Paths roots and joins info segments", async () => {
    const { Directory, Paths } = await import("../index");
    expect(Paths.cache).toBeInstanceOf(Directory);
    expect(Paths.bundle.uri).toBe("/app/X.app/");
    expect(Paths.totalDiskSpace).toBe(100);
    expect(Paths.availableDiskSpace).toBe(40);
    Paths.info("file:///docs", "a.txt");
    expect(native.module.info).toHaveBeenCalledWith("file:///docs/a.txt");
  });
});
```

`public-api.typecheck.ts`:

```ts
import type { Directory, File } from "./index";

type Assert<T extends true> = T;
type Absent<T, K extends PropertyKey> = K extends keyof T ? false : true;

type FileHasNoWrite = Assert<Absent<File, "write">>;
type FileHasNoBytes = Assert<Absent<File, "bytes">>;
type FileHasNoCopy = Assert<Absent<File, "copy">>;
type FileHasNoMove = Assert<Absent<File, "move">>;
type FileHasNoType = Assert<Absent<File, "type">>;
type DirectoryHasNoCopy = Assert<Absent<Directory, "copy">>;
type FileHasText = Assert<"text" extends keyof File ? true : false>;
type DirectoryHasList = Assert<"list" extends keyof Directory ? true : false>;

export type PublicApiChecks = [
  FileHasNoWrite,
  FileHasNoBytes,
  FileHasNoCopy,
  FileHasNoMove,
  FileHasNoType,
  DirectoryHasNoCopy,
  FileHasText,
  DirectoryHasList,
];
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `pnpm --filter expo-file-system-dotnet test`
Expected: FAIL (`../index` not found).

- [ ] **Step 4: Implement the facade**

`ExpoFileSystem.ts`:

```ts
import { DotnetSharedObject, requireDotnetModule } from "expo-modules-dotnet";

export type CreateOptions = {
  intermediates?: boolean;
  overwrite?: boolean;
  idempotent?: boolean;
};

export type InfoOptions = { md5?: boolean };

export type FileInfo = {
  exists: boolean;
  uri?: string;
  md5?: string | null;
  size?: number | null;
  modificationTime?: number | null;
  creationTime?: number | null;
};

export type DirectoryInfo = {
  exists: boolean;
  uri?: string;
  files?: string[] | null;
  size?: number | null;
  modificationTime?: number | null;
  creationTime?: number | null;
};

export type PathInfo = { exists: boolean; isDirectory: boolean | null };

export declare class NativeFile extends DotnetSharedObject {
  constructor(uri: string);
  readonly uri: string;
  readonly exists: boolean;
  readonly size: number;
  readonly md5: string | null;
  readonly modificationTime: number | null;
  readonly lastModified: number | null;
  readonly creationTime: number | null;
  validatePath(): void;
  create(options?: CreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(options?: InfoOptions): FileInfo;
  textSync(): string;
  text(): Promise<string>;
  base64Sync(): string;
  base64(): Promise<string>;
  digest(
    algorithm: "MD5" | "SHA-1" | "SHA-256" | "SHA-384" | "SHA-512",
  ): Promise<string>;
}

export declare class NativeDirectory extends DotnetSharedObject {
  constructor(uri: string);
  readonly uri: string;
  readonly exists: boolean;
  readonly size: number | null;
  validatePath(): void;
  create(options?: CreateOptions): void;
  delete(): void;
  rename(newName: string): void;
  info(): DirectoryInfo;
  listAsRecords(): { isDirectory: boolean; uri: string }[];
  createFile(name: string, content: string | null): NativeFile;
  createDirectory(name: string): NativeDirectory;
}

type FileSystemNativeModule = {
  FileSystemFile: typeof NativeFile;
  FileSystemDirectory: typeof NativeDirectory;
  readonly documentDirectory: string;
  readonly cacheDirectory: string;
  readonly bundleDirectory: string;
  readonly totalDiskSpace: number | null;
  readonly availableDiskSpace: number | null;
  info(uri: string): PathInfo;
};

export default requireDotnetModule<FileSystemNativeModule>("FileSystem");
```

`File.ts`, `Directory.ts`, `Paths.ts`: port the upstream classes, keeping
only these members, with upstream bodies:

- `File`: the `(...uris)` constructor, `parentDirectory`, `extension`,
  `name`.
- `Directory`: the `(...uris)` constructor, `parentDirectory`, `list()`,
  `name`, `createFile(name, mimeType)`, `createDirectory(name)`.
- `Paths extends PathUtilities`: `cache`, `bundle`, `document`,
  `totalDiskSpace`, `availableDiskSpace`, `info(...uris)`.

Upstream's `Directory.list()` is declared `override` because the upstream
native type declares `list`. Our native type has no `list`, so drop the
`override` keyword. `File`, `Directory`, and `Paths` import each other, as
upstream does; keep upstream's import structure so the cycle resolves the
same way. Keep each upstream JSDoc comment for the members you port.

`index.ts`:

```ts
export { Directory } from "./Directory";
export { File } from "./File";
export { Paths } from "./Paths";
export type {
  CreateOptions,
  DirectoryInfo,
  FileInfo,
  InfoOptions,
  PathInfo,
} from "./ExpoFileSystem";
```

- [ ] **Step 5: Run tests and typecheck**

Run: `pnpm --filter expo-file-system-dotnet test`
Expected: PASS.

Run: `pnpm --filter expo-file-system-dotnet typecheck`
Expected: exit 0.

- [ ] **Step 6: Commit**

```bash
git add packages/expo-file-system-dotnet
git commit -m "feat(file-system): add File, Directory, and Paths facade"
```

---

### Task 8: Living spec merge, docs, and roadmap

**Files:**

- Modify: `docs/specs/modules-core-boundary.md` (append after the
  `Expo Asset Dotnet` requirement blocks)
- Modify: `docs/module-authoring-guide.md` only if it lists the authored
  packages; add one line for `expo-file-system-dotnet` in the same style
- Move: `docs/changes/2026-10-12-expo-file-system-dotnet/` →
  `docs/archive/changes/2026-10-12-expo-file-system-dotnet/`
- Modify: `docs/roadmap.md` (024 notes)

- [ ] **Step 1: Merge the requirements**

Copy the four `ADDED Requirement` blocks from the change spec into
`docs/specs/modules-core-boundary.md`, after the last `Expo Asset Dotnet`
block. Drop the `ADDED` prefix from each heading. Where the implementation
named something the spec left open (`FileSystemException`, `Invalid file
system location: '<input>'`, `Invalid file name: '<name>'`, the `bool?`
option fields), add it to the matching requirement text. Add no other new
requirement.

- [ ] **Step 2: Archive the change and update the roadmap**

```bash
git mv docs/changes/2026-10-12-expo-file-system-dotnet docs/archive/changes/2026-10-12-expo-file-system-dotnet
```

In `docs/roadmap.md`, change the change link in the "024 → 034–036 → 038"
note to `archive/changes/2026-10-12-expo-file-system-dotnet/spec.md`. Leave
row 024 `IN PROGRESS` and the brief in `docs/plans/`: the Windows CI run in
Step 4 closes it.

- [ ] **Step 3: Verify**

Run: `scripts/test-managed.sh`
Expected: exit 0, no skipped tests.

Run: `pnpm --filter expo-file-system-dotnet test && pnpm --filter expo-file-system-dotnet typecheck`
Expected: exit 0.

Run: `scripts/format.sh --check --all`
Expected: "Formatting check passed." If it fails, run `scripts/format.sh`
and repeat.

Run: `git diff --check`
Expected: no output.

Privacy check on everything staged:
`git diff --cached | rg -n "/Users/|/home/|C:\\\\Users\\\\"`
Expected: no matches.

Commit:

```bash
git add -A
git commit -m "docs(file-system): merge local files core into living specs"
```

- [ ] **Step 4: Windows validation (needs the user)**

Ask the user to approve pushing the branch so the Windows lanes of
`native-tests.yml` run the package tests. Do not push without that approval.
After a green Windows run:

- Set row 024 to `DONE` in `docs/roadmap.md`.
- `git mv docs/plans/024-expo-file-system-dotnet.md docs/archive/plans/`
  and update the row link to `archive/plans/024-expo-file-system-dotnet.md`.
- Add to the 024 note: the run ID and date of the Windows pass.
- Commit `docs(roadmap): close 024 file-system local files core`.

If the Windows run fails, fix the cause in the owning task's files with a
test that reproduces it, and repeat this step.
