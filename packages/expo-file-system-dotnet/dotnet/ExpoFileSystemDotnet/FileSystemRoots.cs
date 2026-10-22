namespace ExpoFileSystemDotnet;

internal static class FileSystemRoots
{
  private static readonly Lazy<string> processBundleDirectory = new(() =>
      BundleDirectoryFromProcessPath(
          Environment.ProcessPath
              ?? throw new FileSystemException("Unable to determine the bundle directory"),
          PathFlavors.Current
      )
  );

  private static string? bundleOverride;

  internal static string BundleDirectory => bundleOverride ?? processBundleDirectory.Value;

  internal static FileSystemLocation BundleLocation =>
      FileSystemLocation.Parse(BundleDirectory, isDirectory: true, PathFlavors.Current);

  internal static string BundleDirectoryFromProcessPath(string processPath, PathFlavor flavor)
  {
    var directory = FileSystemLocation.Parse(processPath, isDirectory: false, flavor).Parent;
    if (flavor == PathFlavor.Posix && directory.Name == "MacOS")
    {
      var contents = directory.Parent;
      if (contents.Name == "Contents")
      {
        var app = contents.Parent;
        if (app.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
          directory = app;
        }
      }
    }
    return directory.NativePath;
  }

  internal static IDisposable OverrideBundleForTests(string nativePath)
  {
    var previous = bundleOverride;
    bundleOverride = nativePath;
    return new Restore(() => bundleOverride = previous);
  }

  private sealed class Restore(Action restore) : IDisposable
  {
    public void Dispose() => restore();
  }
}
