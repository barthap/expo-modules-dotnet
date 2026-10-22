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
    Directory.Delete(Path, recursive: true);
  }
}
