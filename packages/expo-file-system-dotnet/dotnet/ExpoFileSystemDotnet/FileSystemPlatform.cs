namespace ExpoFileSystemDotnet;

internal static class FileSystemPlatform
{
  private static bool? supportedOverride;

  internal static void EnsureSupported()
  {
    var supported = supportedOverride ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    if (!supported)
    {
      throw new PlatformNotSupportedException(
          "ExpoFileSystemDotnet supports only Windows and macOS."
      );
    }
  }

  internal static IDisposable OverrideForTests(bool supported)
  {
    var previous = supportedOverride;
    supportedOverride = supported;
    return new Restore(() => supportedOverride = previous);
  }

  private sealed class Restore(Action restore) : IDisposable
  {
    public void Dispose() => restore();
  }
}
