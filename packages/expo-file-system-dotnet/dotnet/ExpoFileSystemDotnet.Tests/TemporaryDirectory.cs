namespace ExpoFileSystemDotnet.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
  internal TemporaryDirectory()
  {
    Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"expo-file-system-dotnet-{Guid.NewGuid():N}"
    );
    Directory.CreateDirectory(Path);
  }

  internal string Path { get; }

  public void Dispose()
  {
    // On Windows a read-only file blocks a recursive delete, and some tests leave such files.
    foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
    {
      var attributes = File.GetAttributes(file);
      if ((attributes & FileAttributes.ReadOnly) != 0)
      {
        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
      }
    }
    Directory.Delete(Path, recursive: true);
  }
}
