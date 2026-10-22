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
  public void BundleDirectoriesAreReadOnly()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    using var bundle = FileSystemRoots.OverrideBundleForTests(temp.Path);
    var subPath = Path.Combine(temp.Path, "sub");
    var childPath = Path.Combine(subPath, "child.txt");
    Directory.CreateDirectory(subPath);
    File.WriteAllText(childPath, "x");

    void AssertDenied(string uri, Action action)
    {
      var error = Assert.Throws<FileSystemException>(action);
      Assert.Equal($"Missing permission for uri: {uri}", error.Message);
      Assert.True(File.Exists(childPath));
      Assert.Equal("x", File.ReadAllText(childPath));
    }

    var sub = NewDirectory(subPath);
    AssertDenied(sub.Uri, () => sub.Create(new CreateOptions(null, true, null)));
    AssertDenied(sub.Uri, () => sub.Delete());
    AssertDenied(sub.Uri, () => sub.Rename("other"));
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "other")));

    var root = NewDirectory(temp.Path);
    AssertDenied(root.Uri, () => root.Delete());

    var child = NewFile(childPath);
    AssertDenied(child.Uri, () => child.Rename("renamed.txt"));
    Assert.False(File.Exists(Path.Combine(subPath, "renamed.txt")));

    var created = NewFile(Path.Combine(subPath, "new.txt"));
    AssertDenied(created.Uri, () => created.Create());
    Assert.False(File.Exists(Path.Combine(subPath, "new.txt")));
  }

  [Fact]
  public void FileCreateWithOverwriteReplacesSymlinkWithoutWritingThrough()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var target = Path.Combine(temp.Path, "target.txt");
    var link = Path.Combine(temp.Path, "link.txt");
    File.WriteAllText(target, "keep");
    var linked = true;
    try
    {
      File.CreateSymbolicLink(link, target);
    }
    catch (Exception error) when (error is UnauthorizedAccessException or IOException)
    {
      // Windows can need a privilege for symlinks; still assert the regular-file overwrite.
      linked = false;
      File.WriteAllText(link, "old");
    }

    NewFile(link).Create(new CreateOptions(null, true, null));

    Assert.Equal("keep", File.ReadAllText(target));
    var info = new FileInfo(link);
    Assert.Null(info.LinkTarget);
    Assert.Equal(0, info.Length);
    Assert.True(linked || OperatingSystem.IsWindows(), "symlinks must work off Windows");
  }

  [Fact]
  public void RenameOfARootFailsBeforeAnyIo()
  {
    using var platform = PlatformScope.Supported();
    var root = Path.GetPathRoot(Path.GetTempPath())!;

    var error = Assert.Throws<FileSystemException>(() => NewDirectory(root).Rename("x"));
    Assert.Equal("Invalid file name: 'x'", error.Message);
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
