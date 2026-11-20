using Expo.ModulesCore;

namespace ExpoConstantsDotnet;

[ExpoModule("ExponentConstants")]
public sealed partial class ExpoConstantsModule : Module
{
  private readonly HostAppMetadata metadata;
  private readonly string sessionId;
  private readonly object manifestLock = new();
  private bool manifestLoaded;
  private string? manifest;

  public ExpoConstantsModule(DotnetRuntimeContext context)
      : base(context)
  {
    metadata = context.AppMetadata;
    sessionId = Guid.NewGuid().ToString("D");
  }

  [JS]
  public string Platform => RequireSupportedPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

  [JS]
  public string ExecutionEnvironment => RequireSupportedHostValue("bare");

  [JS]
  public string SessionId => RequireSupportedHostValue(sessionId);

  [JS]
  public string? NativeAppVersion => RequireSupportedHostValue(metadata.NativeAppVersion);

  [JS]
  public string? NativeBuildVersion => RequireSupportedHostValue(metadata.NativeBuildVersion);

  [JS]
  public string? ExpoVersion => RequireSupportedHostValue<string?>(null);

  /// <summary>
  /// The app's public Expo config as a JSON string, read from app.config in the
  /// host's resources directory; null when the host supplies no directory or no file.
  /// Upstream expo-constants derives Constants.expoConfig from it.
  /// </summary>
  [JS]
  public string? Manifest
  {
    get
    {
      _ = RequireSupportedPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
      lock (manifestLock)
      {
        // Only a successful read is cached, so a fixed file is picked up on the next read.
        if (!manifestLoaded)
        {
          manifest = AppConfigFile.Read(metadata.ResourcesDirectory);
          manifestLoaded = true;
        }
        return manifest;
      }
    }
  }

  private static T RequireSupportedHostValue<T>(T value)
  {
    _ = RequireSupportedPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
    return value;
  }

  internal static string RequireSupportedPlatform(bool isWindows, bool isMacOS)
  {
    if (isWindows)
    {
      return "windows";
    }
    if (isMacOS)
    {
      return "macos";
    }
    throw new PlatformNotSupportedException("ExpoConstantsDotnet supports only Windows and macOS.");
  }
}
