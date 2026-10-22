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
