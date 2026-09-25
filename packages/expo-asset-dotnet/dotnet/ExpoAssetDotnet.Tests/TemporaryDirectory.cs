namespace ExpoAssetDotnet.Tests;

internal sealed class TemporaryDirectory : IDisposable
{
  internal TemporaryDirectory()
  {
    Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        $"expo-asset-dotnet-{Guid.NewGuid():N}"
    );
    Directory.CreateDirectory(Path);
  }

  internal string Path { get; }

  public void Dispose()
  {
    Directory.Delete(Path, recursive: true);
  }
}
