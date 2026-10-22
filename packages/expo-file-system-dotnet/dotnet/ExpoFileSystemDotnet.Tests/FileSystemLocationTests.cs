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

  [Theory]
  [InlineData(@"\\./PhysicalDrive0")]
  [InlineData(@"\\?/C:/a.txt")]
  [InlineData(@"/\?\C:\a.txt")]
  [InlineData("file://./PhysicalDrive0")]
  [InlineData("file://%3F/C:/a.txt")]
  [InlineData("file://%2E/pipe/x")]
  [InlineData(@"\\..\share\x")]
  [InlineData(@"\\.\share\x")]
  [InlineData("file://server/")]
  [InlineData("file://server")]
  [InlineData(@"\\bad host\share\x")]
  [InlineData("file:///C:/a/%4")]
  [InlineData("file:///C:/a/%C5.txt")]
  [InlineData(@"C:\a\X.app.\b")]
  [InlineData(@"C:\a\b ")]
  [InlineData("file:///C:/a/b%20")]
  [InlineData("file:///C:/a/b.")]
  [InlineData(@"C:\X::$INDEX_ALLOCATION\a.txt")]
  [InlineData(@"C:\a|b\c.txt")]
  [InlineData("file:///C:/a%3Ab/c.txt")]
  public void WindowsRejectsDeviceHostAndAliasInput(string input)
  {
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.Parse(input, isDirectory: false, PathFlavor.Windows)
    );
    Assert.StartsWith("Invalid file system location", error.Message);
  }

  [Theory]
  [InlineData("file:///a/%4")]
  [InlineData("file:///a/%C5.txt")]
  public void PosixRejectsTruncatedEscapeInvalidUtf8AndLoneSurrogate(string input)
  {
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.Parse(input, isDirectory: false, PathFlavor.Posix)
    );
    Assert.StartsWith("Invalid file system location", error.Message);
  }

  [Theory]
  [InlineData(PathFlavor.Posix, "/a/")]
  [InlineData(PathFlavor.Windows, @"C:\a\")]
  public void LoneSurrogateIsRejectedNotReplaced(PathFlavor flavor, string prefix)
  {
    // Built at run time: xunit data serialization would replace a literal lone surrogate.
    var input = prefix + (char)0xD800 + "b";
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.Parse(input, isDirectory: false, flavor)
    );
    Assert.StartsWith("Invalid file system location", error.Message);
  }

  [Fact]
  public void WindowsDotSegmentsAreNormalizedBeforeTrailingDotCheck()
  {
    var location = FileSystemLocation.Parse(@"C:\a\.\b\..\c.txt", isDirectory: false, PathFlavor.Windows);
    Assert.Equal("file:///C:/a/c.txt", location.Uri);
  }

  [Fact]
  public void IsWithinIsFalseAcrossHostsAndDrives()
  {
    var driveC = FileSystemLocation.Parse(@"C:\a", isDirectory: true, PathFlavor.Windows);
    var driveD = FileSystemLocation.Parse(@"D:\a\b.txt", isDirectory: false, PathFlavor.Windows);
    var driveLower = FileSystemLocation.Parse(@"c:\a\b.txt", isDirectory: false, PathFlavor.Windows);
    var uncA = FileSystemLocation.Parse(@"\\one\share", isDirectory: true, PathFlavor.Windows);
    var uncB = FileSystemLocation.Parse(@"\\two\share\x.txt", isDirectory: false, PathFlavor.Windows);
    Assert.False(driveD.IsWithin(driveC));
    Assert.True(driveLower.IsWithin(driveC));
    Assert.False(uncB.IsWithin(uncA));
    Assert.False(uncB.IsWithin(driveC));
  }

  [Fact]
  public void ParentOfRootIsRoot()
  {
    var posix = FileSystemLocation.Parse("/", isDirectory: true, PathFlavor.Posix);
    Assert.Equal("file:///", posix.Parent.Uri);
    var drive = FileSystemLocation.Parse(@"C:\", isDirectory: true, PathFlavor.Windows);
    Assert.Equal("file:///C:/", drive.Parent.Uri);
    var unc = FileSystemLocation.Parse(@"\\server\share", isDirectory: true, PathFlavor.Windows);
    Assert.Equal("file://server/share/", unc.Parent.Uri);
  }

  [Fact]
  public void ValidateNameOnWindows()
  {
    FileSystemLocation.ValidateName("a b.txt", PathFlavor.Windows);
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.ValidateName(@"a\b", PathFlavor.Windows)
    );
    Assert.Equal(@"Invalid file name: 'a\b'", error.Message);
  }

  [Theory]
  [InlineData("a.")]
  [InlineData("a ")]
  [InlineData("a:b")]
  [InlineData("a?b")]
  [InlineData("a\u0001")]
  public void WindowsValidateNameRejectsAliasingNames(string name)
  {
    var error = Assert.Throws<FileSystemException>(
        () => FileSystemLocation.ValidateName(name, PathFlavor.Windows)
    );
    Assert.StartsWith("Invalid file name", error.Message);
  }

  [Fact]
  public void PosixValidateNameAllowsColon()
  {
    FileSystemLocation.ValidateName("a:b", PathFlavor.Posix);
  }
}
