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

  /// <summary>Returns the longest root that contains <paramref name="path"/> at a separator boundary.</summary>
  internal static string? SelectDriveRoot(
      string path,
      IEnumerable<string> roots,
      StringComparison comparison
  ) =>
      roots
          .Where(root =>
              path.StartsWith(root, comparison)
              && (
                  root.Length == 0
                  || root[^1] is '/' or '\\'
                  || path.Length == root.Length
                  || path[root.Length] == Path.DirectorySeparatorChar
                  || path[root.Length] == Path.AltDirectorySeparatorChar
              )
          )
          .OrderByDescending(root => root.Length)
          .FirstOrDefault();

  private long? ReadDrive(Func<DriveInfo, long> read)
  {
    FileSystemPlatform.EnsureSupported();
    try
    {
      var path = Path.GetFullPath(context.PersistentFilesDirectory);
      var comparison = OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;
      var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
      var root = SelectDriveRoot(path, drives.Select(d => d.RootDirectory.FullName), comparison);
      var drive = drives.FirstOrDefault(d =>
          string.Equals(d.RootDirectory.FullName, root, comparison));
      return drive is null ? null : read(drive);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException
        or AppDirectoryNotConfiguredException)
    {
      return null;
    }
  }
}
