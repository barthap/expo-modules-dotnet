using Expo.ModulesCore;

namespace ExpoConstantsDotnet;

[ExpoModule("ExponentConstants")]
public sealed partial class ExpoConstantsModule : Module
{
  private readonly HostAppMetadata metadata;

  public ExpoConstantsModule(DotnetRuntimeContext context)
      : base(context)
  {
    metadata = context.AppMetadata;
    Platform = RequireSupportedPlatform(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());
    SessionId = Guid.NewGuid().ToString("D");
  }

  [JS]
  public string Platform { get; }

  [JS]
  public string ExecutionEnvironment => "bare";

  [JS]
  public string SessionId { get; }

  [JS]
  public string? NativeAppVersion => metadata.NativeAppVersion;

  [JS]
  public string? NativeBuildVersion => metadata.NativeBuildVersion;

  [JS]
  public string? ExpoVersion => null;

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
