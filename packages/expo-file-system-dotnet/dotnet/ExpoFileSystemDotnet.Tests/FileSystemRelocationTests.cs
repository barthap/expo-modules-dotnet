using Expo.ModulesCore;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemRelocationTests
{
  private static FileSystemFile NewFile(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current));

  private static FileSystemDirectory NewDirectory(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: true, PathFlavors.Current));

  // Every entry under root as "relative path=contents" (directories as "relative path/"), so a
  // test can assert that a failed call changed nothing.
  private static string[] Tree(string root) =>
      Directory
          .EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
          .Select(path =>
          {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            return Directory.Exists(path) ? relative + "/" : relative + "=" + File.ReadAllText(path);
          })
          .Order(StringComparer.Ordinal)
          .ToArray();

  private static string Write(string path, string contents)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, contents);
    return path;
  }

  [Fact]
  public void FileIntoAnExistingDirectory()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var source = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    var directory = NewDirectory(Path.Combine(temp.Path, "d"));
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));

    source.CopySync(directory);
    Assert.Equal(new[] { "a.txt=A", "d/", "d/a.txt=A" }, Tree(temp.Path));

    source.MoveSync(directory, new RelocationOptions(Overwrite: true));
    Assert.Equal(new[] { "d/", "d/a.txt=A" }, Tree(temp.Path));
    Assert.Equal(directory.Uri + "a.txt", source.Uri);
  }

  [Fact]
  public void FileOntoAFile()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var source = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    var target = NewFile(Path.Combine(temp.Path, "b.txt"));

    source.CopySync(target);
    Assert.Equal(new[] { "a.txt=A", "b.txt=A" }, Tree(temp.Path));

    var moved = NewFile(Path.Combine(temp.Path, "c.txt"));
    source.MoveSync(moved);
    Assert.Equal(new[] { "b.txt=A", "c.txt=A" }, Tree(temp.Path));
    Assert.Equal(moved.Uri, source.Uri);
  }

  [Fact]
  public void DirectoryIntoAnExistingAndAMissingDirectory()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "nested", "n.txt"), "N");
    Write(Path.Combine(temp.Path, "s", ".hidden"), "H");
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    var source = NewDirectory(Path.Combine(temp.Path, "s"));

    source.CopySync(NewDirectory(Path.Combine(temp.Path, "d")));
    source.CopySync(NewDirectory(Path.Combine(temp.Path, "n")));

    Assert.Equal(Tree(Path.Combine(temp.Path, "s")), Tree(Path.Combine(temp.Path, "d", "s")));
    Assert.Equal(Tree(Path.Combine(temp.Path, "s")), Tree(Path.Combine(temp.Path, "n")));
    Assert.Equal(new[] { ".hidden=H", "nested/", "nested/n.txt=N" }, Tree(Path.Combine(temp.Path, "n")));

    var target = NewDirectory(Path.Combine(temp.Path, "m"));
    source.MoveSync(target);
    Assert.Equal(target.Uri, source.Uri);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "s")));
    Assert.Equal(new[] { ".hidden=H", "nested/", "nested/n.txt=N" }, Tree(Path.Combine(temp.Path, "m")));
  }

  [Fact]
  public void PreconditionsFailInOrderWithoutSideEffects()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    Write(Path.Combine(temp.Path, "s", "x.txt"), "X");
    var directory = NewDirectory(Path.Combine(temp.Path, "s"));
    var existing = NewFile(Write(Path.Combine(temp.Path, "b.txt"), "B"));
    var before = Tree(temp.Path);

    void Fails<T>(string code, Action action)
        where T : FileSystemException
    {
      var error = Assert.Throws<T>(action);
      Assert.Equal(code, error.Code);
      Assert.Equal(before, Tree(temp.Path));
    }

    Fails<DestinationDoesNotExistException>(
        "ERR_DESTINATION_DOES_NOT_EXIST",
        () => file.CopySync(NewDirectory(Path.Combine(temp.Path, "missing"))));
    Fails<DestinationDoesNotExistException>(
        "ERR_DESTINATION_DOES_NOT_EXIST",
        () => file.CopySync(NewFile(Path.Combine(temp.Path, "missing", "c.txt"))));
    Fails<DestinationDoesNotExistException>(
        "ERR_DESTINATION_DOES_NOT_EXIST",
        () => directory.CopySync(NewDirectory(Path.Combine(temp.Path, "missing", "deeper"))));
    Fails<CopyOrMoveDirectoryToFileException>(
        "ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE",
        () => directory.CopySync(NewFile(Path.Combine(temp.Path, "c.txt"))));
    Fails<DestinationAlreadyExistsException>(
        "ERR_DESTINATION_ALREADY_EXISTS",
        () => file.CopySync(existing));
    Fails<DestinationAlreadyExistsException>(
        "ERR_DESTINATION_ALREADY_EXISTS",
        () => file.MoveSync(existing));
    Fails<UnableToCopyException>(
        "ERR_UNABLE_TO_COPY",
        () => directory.CopySync(NewDirectory(Path.Combine(temp.Path, "s"))));
    Fails<UnableToMoveException>(
        "ERR_UNABLE_TO_MOVE",
        () => directory.MoveSync(NewDirectory(Path.Combine(temp.Path, "s", "inner"))));
    Fails<UnableToCopyException>(
        "ERR_UNABLE_TO_COPY",
        () => file.CopySync(file, new RelocationOptions(Overwrite: true)));
    Fails<UnableToMoveException>(
        "ERR_UNABLE_TO_MOVE",
        () => file.MoveSync(NewFile(Path.Combine(temp.Path, "a.txt")), new RelocationOptions(Overwrite: true)));

    var nested = Assert.Throws<UnableToCopyException>(
        () => directory.CopySync(NewDirectory(Path.Combine(temp.Path, "s"))));
    Assert.Equal("Unable to copy file or directory: destination is inside the source", nested.Message);
    var same = Assert.Throws<UnableToCopyException>(() => file.CopySync(file, new RelocationOptions(Overwrite: true)));
    Assert.Equal("Unable to copy file or directory: source and destination are the same", same.Message);

    var missing = NewFile(Path.Combine(temp.Path, "gone.txt"));
    var missingError = Assert.Throws<FileNotFoundException>(
        () => missing.CopySync(existing, new RelocationOptions(Overwrite: true)));
    Assert.IsNotAssignableFrom<CodedException>(missingError);
    Assert.Equal(before, Tree(temp.Path));
    Assert.Throws<DirectoryNotFoundException>(
        () => NewDirectory(Path.Combine(temp.Path, "nope")).CopySync(NewDirectory(Path.Combine(temp.Path, "s"))));
    Assert.Equal(before, Tree(temp.Path));
  }

  [Fact]
  public void OverwriteReplacesAnEntryOfEitherType()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    Write(Path.Combine(temp.Path, "d", "a.txt", "deep.txt"), "D");
    Write(Path.Combine(temp.Path, "s", "x.txt"), "X");
    Write(Path.Combine(temp.Path, "t", "s"), "file in the way");

    file.CopySync(NewDirectory(Path.Combine(temp.Path, "d")), new RelocationOptions(Overwrite: true));
    NewDirectory(Path.Combine(temp.Path, "s")).CopySync(
        NewDirectory(Path.Combine(temp.Path, "t")),
        new RelocationOptions(Overwrite: true));

    Assert.Equal(
        new[] { "a.txt=A", "d/", "d/a.txt=A", "s/", "s/x.txt=X", "t/", "t/s/", "t/s/x.txt=X" },
        Tree(temp.Path));
  }

  [Fact]
  public void TypeAndPermissionAreCheckedFirst()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Directory.CreateDirectory(Path.Combine(temp.Path, "folder"));
    var notAFile = NewFile(Path.Combine(temp.Path, "folder"));
    var file = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    var notADirectory = NewDirectory(Path.Combine(temp.Path, "a.txt"));

    Assert.Throws<InvalidTypeFileException>(() => notAFile.CopySync(NewDirectory(temp.Path)));
    Assert.Throws<InvalidTypeDirectoryException>(() => file.CopySync(notADirectory));

    using var bundle = FileSystemRoots.OverrideBundleForTests(Path.Combine(temp.Path, "folder"));
    var bundled = NewDirectory(Path.Combine(temp.Path, "folder"));
    var copyError = Assert.Throws<MissingPermissionException>(() => notAFile.CopySync(bundled));
    Assert.Equal($"Missing permission for uri: {bundled.Uri}", copyError.Message);
    var moveError = Assert.Throws<MissingPermissionException>(() => notAFile.MoveSync(NewDirectory(temp.Path)));
    Assert.Equal($"Missing permission for uri: {notAFile.Uri}", moveError.Message);
  }

  // The bundle root is a child of the destination, so the resolved target is the bundle itself.
  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public void OverwriteNeverReplacesTheBundleThroughItsParent(bool isDirectory, bool move)
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "app", "bundle.txt"), "BUNDLE");
    var sourcePath = Path.Combine(temp.Path, "out", "app");
    if (isDirectory)
    {
      Write(Path.Combine(sourcePath, "x.txt"), "X");
    }
    else
    {
      Write(sourcePath, "X");
    }
    using var bundle = FileSystemRoots.OverrideBundleForTests(Path.Combine(temp.Path, "app"));
    FileSystemPath source = isDirectory ? NewDirectory(sourcePath) : NewFile(sourcePath);
    var destination = NewDirectory(temp.Path);
    var before = Tree(temp.Path);

    var options = new RelocationOptions(Overwrite: true);
    var error = Assert.Throws<MissingPermissionException>(
        () =>
        {
          if (isDirectory)
          {
            var directory = (FileSystemDirectory)source;
            if (move) directory.MoveSync(destination, options); else directory.CopySync(destination, options);
          }
          else
          {
            var file = (FileSystemFile)source;
            if (move) file.MoveSync(destination, options); else file.CopySync(destination, options);
          }
        });

    var targetUri = isDirectory ? NewDirectory(Path.Combine(temp.Path, "app")).Uri : NewFile(Path.Combine(temp.Path, "app")).Uri;
    Assert.Equal($"Missing permission for uri: {targetUri}", error.Message);
    Assert.Equal(before, Tree(temp.Path));
  }

  [Fact]
  public void BundleTreeIsACopySource()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var bundled = NewFile(Write(Path.Combine(temp.Path, "app", "a.txt"), "A"));
    Directory.CreateDirectory(Path.Combine(temp.Path, "out"));
    using var bundle = FileSystemRoots.OverrideBundleForTests(Path.Combine(temp.Path, "app"));

    bundled.CopySync(NewDirectory(Path.Combine(temp.Path, "out")));

    Assert.Equal(new[] { "app/", "app/a.txt=A", "out/", "out/a.txt=A" }, Tree(temp.Path));
  }

  [Fact]
  public void CaseOnlyMoveRenamesInPlaceOnACaseInsensitiveVolume()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Write(Path.Combine(temp.Path, "a.txt"), "A");
    var caseInsensitive = File.Exists(Path.Combine(temp.Path, "A.TXT"));
    var file = NewFile(path);
    var renamed = NewFile(Path.Combine(temp.Path, "A.txt"));

    if (!caseInsensitive)
    {
      // On a case-sensitive volume, A.txt is another item, so the move is an ordinary move.
      file.MoveSync(renamed);
      Assert.Equal(new[] { "A.txt=A" }, Tree(temp.Path));
      return;
    }

    file.MoveSync(renamed);

    Assert.Equal(new[] { "A.txt=A" }, Tree(temp.Path));
    Assert.Equal(renamed.Uri, file.Uri);
  }

  [Fact]
  public void SameItemComparesStoredNames()
  {
    using var temp = new TemporaryDirectory();
    var stored = Write(Path.Combine(temp.Path, "Dir", "a.txt"), "A");
    var other = Path.Combine(temp.Path, "dir", "A.txt");
    var caseInsensitive = File.Exists(other);
    if (!caseInsensitive)
    {
      // IsSameItem compares existing entries. On a case-sensitive volume this is a second item.
      Write(other, "B");
    }

    Assert.True(FileSystemRelocation.IsSameItem(stored, stored));
    Assert.False(FileSystemRelocation.IsSameItem(stored, Path.Combine(temp.Path, "Dir", "b.txt")));
    Assert.Equal(caseInsensitive, FileSystemRelocation.IsSameItem(stored, other));
  }

  [Fact]
  public void SymbolicLinksAreCopiedAsLinks()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var outside = Write(Path.Combine(temp.Path, "outside.txt"), "O");
    Directory.CreateDirectory(Path.Combine(temp.Path, "outsideDir"));
    Directory.CreateDirectory(Path.Combine(temp.Path, "s"));
    // On Windows, creating a link needs Developer Mode or an elevated process.
    File.CreateSymbolicLink(Path.Combine(temp.Path, "s", "file-link"), outside);
    Directory.CreateSymbolicLink(Path.Combine(temp.Path, "s", "dir-link"), Path.Combine(temp.Path, "outsideDir"));

    NewDirectory(Path.Combine(temp.Path, "s")).CopySync(NewDirectory(Path.Combine(temp.Path, "c")));

    Assert.Equal(outside, new FileInfo(Path.Combine(temp.Path, "c", "file-link")).LinkTarget);
    Assert.Equal(
        Path.Combine(temp.Path, "outsideDir"),
        new DirectoryInfo(Path.Combine(temp.Path, "c", "dir-link")).LinkTarget);
    Assert.Equal("O", File.ReadAllText(outside));
  }

  [Fact]
  public void FailedDirectoryCopyRemovesWhatItCreated()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "a.txt"), "A");
    var locked = Write(Path.Combine(temp.Path, "s", "z", "locked.txt"), "L");
    var before = Tree(Path.Combine(temp.Path, "s"));
    var source = NewDirectory(Path.Combine(temp.Path, "s"));

    Exception error;
    using (FileLocks.BlockRead(locked))
    {
      error = Assert.ThrowsAny<Exception>(() => source.CopySync(NewDirectory(Path.Combine(temp.Path, "c"))));
    }

    Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().FullName);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "c")));
    Assert.Equal(before, Tree(Path.Combine(temp.Path, "s")));
  }

  // A target that another writer creates after the precondition checks is not ours to delete.
  [Fact]
  public void FailedFallbackCopyKeepsADirectoryAnotherWriterCreated()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "x.txt"), "SOURCE");
    var foreign = Path.Combine(temp.Path, "m", "x.txt");
    var source = FileSystemLocation.Parse(Path.Combine(temp.Path, "s"), isDirectory: true, PathFlavors.Current);
    var destination = FileSystemLocation.Parse(Path.Combine(temp.Path, "m"), isDirectory: true, PathFlavors.Current);

    var error = Assert.Throws<DestinationAlreadyExistsException>(
        () => FileSystemRelocation.Move(
            source,
            destination,
            overwrite: false,
            (_, target) =>
            {
              Write(foreign, "FOREIGN");
              return false;
            }));

    Assert.Equal("ERR_DESTINATION_ALREADY_EXISTS", error.Code);
    Assert.Equal(new[] { "x.txt=FOREIGN" }, Tree(Path.Combine(temp.Path, "m")));
    Assert.Equal(new[] { "x.txt=SOURCE" }, Tree(Path.Combine(temp.Path, "s")));
  }

  [Fact]
  public void FileCopyKeepsATargetThatAppearedAfterTheChecks()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "a.txt"), "SOURCE");
    var foreign = Write(Path.Combine(temp.Path, "b.txt"), "FOREIGN");
    var source = FileSystemLocation.Parse(Path.Combine(temp.Path, "a.txt"), isDirectory: false, PathFlavors.Current);
    var target = FileSystemLocation.Parse(foreign, isDirectory: false, PathFlavors.Current);

    Assert.Throws<DestinationAlreadyExistsException>(() => FileSystemRelocation.CopyEntry(source, target));

    Assert.Equal(new[] { "a.txt=SOURCE", "b.txt=FOREIGN" }, Tree(temp.Path));
  }

  [Fact]
  public void DirectoryMoveToAnotherVolumeCopiesThenDeletes()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "nested", "n.txt"), "N");
    var source = FileSystemLocation.Parse(Path.Combine(temp.Path, "s"), isDirectory: true, PathFlavors.Current);
    var destination = FileSystemLocation.Parse(Path.Combine(temp.Path, "m"), isDirectory: true, PathFlavors.Current);

    var moved = FileSystemRelocation.Move(source, destination, overwrite: false, (_, _) => false);

    Assert.Equal(destination.Uri, moved.Uri);
    Assert.Equal(new[] { "m/", "m/nested/", "m/nested/n.txt=N" }, Tree(temp.Path));
  }

  [Fact]
  public void FailedFallbackCopyKeepsTheSource()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "a.txt"), "A");
    var locked = Write(Path.Combine(temp.Path, "s", "z", "locked.txt"), "L");
    var before = Tree(Path.Combine(temp.Path, "s"));
    var source = FileSystemLocation.Parse(Path.Combine(temp.Path, "s"), isDirectory: true, PathFlavors.Current);
    var destination = FileSystemLocation.Parse(Path.Combine(temp.Path, "m"), isDirectory: true, PathFlavors.Current);

    Exception error;
    using (FileLocks.BlockRead(locked))
    {
      error = Assert.ThrowsAny<Exception>(
          () => FileSystemRelocation.Move(source, destination, overwrite: false, (_, _) => false));
    }

    Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().FullName);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "m")));
    Assert.Equal(before, Tree(Path.Combine(temp.Path, "s")));
  }

  // The default move step falls back to copy and delete only for another volume. Any other
  // failure of Directory.Move propagates, and nothing is copied.
  [Fact]
  public void SameVolumeMoveFailurePropagates()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = Write(Path.Combine(temp.Path, "s", "a.txt"), "A");
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    var source = NewDirectory(Path.Combine(temp.Path, "s"));
    var uri = source.Uri;

    Exception error;
    using (FileLocks.BlockDelete(file))
    {
      error = Assert.ThrowsAny<Exception>(() => source.MoveSync(NewDirectory(Path.Combine(temp.Path, "d"))));
    }

    Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().FullName);
    Assert.IsNotAssignableFrom<CodedException>(error);
    Assert.Equal(uri, source.Uri);
    Assert.Equal(new[] { "d/", "s/", "s/a.txt=A" }, Tree(temp.Path));
  }

  [Fact]
  public void SourceDeleteFailureAfterAFullCopyKeepsTheTarget()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    Write(Path.Combine(temp.Path, "s", "a.txt"), "A");
    var stuck = Write(Path.Combine(temp.Path, "s", "z", "stuck.txt"), "S");
    var source = FileSystemLocation.Parse(Path.Combine(temp.Path, "s"), isDirectory: true, PathFlavors.Current);
    var destination = FileSystemLocation.Parse(Path.Combine(temp.Path, "m"), isDirectory: true, PathFlavors.Current);

    UnableToMoveException error;
    using (FileLocks.BlockDelete(stuck))
    {
      error = Assert.Throws<UnableToMoveException>(
          () => FileSystemRelocation.Move(source, destination, overwrite: false, (_, _) => false));
    }

    Assert.Equal("Unable to move file or directory: Failed to delete source after move", error.Message);
    Assert.Equal(new[] { "a.txt=A", "z/", "z/stuck.txt=S" }, Tree(Path.Combine(temp.Path, "m")));
    Assert.True(File.Exists(stuck));
  }

  [Fact]
  public async Task AsyncFormsUseTheLocationsCapturedAtTheCall()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = NewFile(Write(Path.Combine(temp.Path, "a.txt"), "A"));
    Directory.CreateDirectory(Path.Combine(temp.Path, "d"));
    var directory = NewDirectory(Path.Combine(temp.Path, "d"));

    await file.Copy(directory);
    await file.Move(directory, new RelocationOptions(Overwrite: true));

    Assert.Equal(directory.Uri + "a.txt", file.Uri);
    Assert.Equal(new[] { "d/", "d/a.txt=A" }, Tree(temp.Path));
    var error = await Assert.ThrowsAsync<DestinationDoesNotExistException>(
        () => file.Move(NewDirectory(Path.Combine(temp.Path, "missing"))));
    Assert.Equal(directory.Uri + "a.txt", file.Uri);
    Assert.Equal("ERR_DESTINATION_DOES_NOT_EXIST", error.Code);
  }
}
