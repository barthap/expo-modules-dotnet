namespace ExpoFileSystemDotnet.Tests;

internal static class PlatformScope
{
  internal static IDisposable Supported() => FileSystemPlatform.OverrideForTests(true);
}
