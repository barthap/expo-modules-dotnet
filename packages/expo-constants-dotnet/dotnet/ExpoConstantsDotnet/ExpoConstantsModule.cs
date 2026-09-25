using Expo.ModulesCore;

namespace ExpoConstantsDotnet;

[ExpoModule("ExponentConstants")]
public sealed partial class ExpoConstantsModule : Module
{
  private readonly HostAppMetadata metadata;
  private readonly string sessionId;

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
