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
