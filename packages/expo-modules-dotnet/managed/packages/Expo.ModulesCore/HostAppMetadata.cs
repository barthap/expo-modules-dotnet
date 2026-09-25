namespace Expo.ModulesCore;

/// <summary>
/// Native app version strings supplied by the embedding host for one runtime context.
/// Missing values remain null; the managed core does not infer them from assemblies or files.
/// </summary>
public sealed record HostAppMetadata
{
  /// <summary>Gets a shared instance with no native version strings.</summary>
  public static HostAppMetadata Unconfigured { get; } = new();

  /// <summary>Creates an immutable pair of host-supplied native versions.</summary>
  /// <param name="nativeAppVersion">The app's native display version, if supplied.</param>
  /// <param name="nativeBuildVersion">The app's native build version, if supplied.</param>
  public HostAppMetadata(string? nativeAppVersion = null, string? nativeBuildVersion = null)
  {
    NativeAppVersion = Validate(nativeAppVersion, nameof(nativeAppVersion));
    NativeBuildVersion = Validate(nativeBuildVersion, nameof(nativeBuildVersion));
  }

  /// <summary>Gets the native app version, or null when the host has no source for it.</summary>
  public string? NativeAppVersion { get; }

  /// <summary>Gets the native build version, or null when the host has no source for it.</summary>
  public string? NativeBuildVersion { get; }

  private static string? Validate(string? value, string parameterName)
  {
    if (value is null)
    {
      return null;
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.IndexOf('\0') >= 0)
    {
      throw new ArgumentException("Native app versions cannot contain NUL.", parameterName);
    }
    return value;
  }
}
