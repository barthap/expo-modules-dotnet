using System.Runtime.Versioning;

namespace ExpoFileSystemDotnet.Tests;

/// <summary>Makes a file fail to delete until disposed.</summary>
/// <remarks>
/// On Windows an open handle without delete sharing does it. On macOS and Linux file modes do it,
/// so these tests need a user that is not root.
/// </remarks>
internal static class FileLocks
{
  /// <summary>The file can be read but not deleted.</summary>
  internal static IDisposable BlockDelete(string filePath)
  {
    if (OperatingSystem.IsWindows())
    {
      return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    var parent = Path.GetDirectoryName(filePath)!;
    return new ModeScope(parent, UnixFileMode.UserRead | UnixFileMode.UserExecute);
  }

  [UnsupportedOSPlatform("windows")]
  private sealed class ModeScope : IDisposable
  {
    private readonly string path;
    private readonly UnixFileMode previous;

    internal ModeScope(string path, UnixFileMode mode)
    {
      this.path = path;
      previous = File.GetUnixFileMode(path);
      File.SetUnixFileMode(path, mode);
    }

    public void Dispose() => File.SetUnixFileMode(path, previous);
  }
}
