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

  [Theory]
  [InlineData("/Volumes/Data2/x", "/")]
  [InlineData("/Volumes/Data/x", "/Volumes/Data")]
  public void DriveRootMatchingRequiresASeparatorBoundary(string path, string expected)
  {
    Assert.Equal(
        expected,
        FileSystemModule.SelectDriveRoot(path, ["/", "/Volumes/Data"], StringComparison.Ordinal)
    );
  }

  [Fact]
  public void DriveRootMatchingIsCaseInsensitiveOnWindowsRoots()
  {
    Assert.Equal(
        @"C:\",
        FileSystemModule.SelectDriveRoot(@"C:\x", [@"C:\", @"D:\"], StringComparison.OrdinalIgnoreCase)
    );
  }
}
