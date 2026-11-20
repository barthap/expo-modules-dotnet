namespace Expo.ModulesCore;

/// <summary>
/// Host identity values supplied by the embedding host for one runtime context:
/// native app versions and the read-only resources directory.
/// Missing values remain null; the managed core does not infer them from assemblies or files.
/// </summary>
public sealed record HostAppMetadata
{
  /// <summary>Gets a shared instance with no host-supplied values.</summary>
  public static HostAppMetadata Unconfigured { get; } = new();

  /// <summary>Creates an immutable set of host-supplied values.</summary>
  /// <param name="nativeAppVersion">The app's native display version, if supplied.</param>
  /// <param name="nativeBuildVersion">The app's native build version, if supplied.</param>
  /// <param name="resourcesDirectory">
  /// The absolute read-only directory that holds the staged managed artifacts and their
  /// resource files, if supplied.
  /// </param>
  public HostAppMetadata(
      string? nativeAppVersion = null,
      string? nativeBuildVersion = null,
      string? resourcesDirectory = null
  )
  {
    NativeAppVersion = Validate(nativeAppVersion, nameof(nativeAppVersion));
    NativeBuildVersion = Validate(nativeBuildVersion, nameof(nativeBuildVersion));
    ResourcesDirectory = ValidateDirectory(resourcesDirectory, nameof(resourcesDirectory));
  }

  /// <summary>Gets the native app version, or null when the host has no source for it.</summary>
  public string? NativeAppVersion { get; }

  /// <summary>Gets the native build version, or null when the host has no source for it.</summary>
  public string? NativeBuildVersion { get; }

  /// <summary>
  /// Gets the host's read-only directory of staged managed artifacts and their resource
  /// files, or null when the host does not supply it. Module code must not write to it.
  /// </summary>
  /// <remarks>
  /// Unlike the app-scoped directories, an unconfigured value is null rather than an
  /// exception: it is optional host information, not storage.
  /// </remarks>
  // @ref docs/specs/modules-core-boundary.md "Runtime Context Exposes Host-Supplied App Directories"
  public string? ResourcesDirectory { get; }

  private static string? Validate(string? value, string parameterName)
  {
    if (value is null)
    {
      return null;
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.IndexOf('\0') >= 0)
    {
      throw new ArgumentException("Host-supplied values cannot contain NUL.", parameterName);
    }
    return value;
  }

  private static string? ValidateDirectory(string? value, string parameterName)
  {
    if (Validate(value, parameterName) is not { } directory)
    {
      return null;
    }
    if (!Path.IsPathFullyQualified(directory))
    {
      throw new ArgumentException("The resources directory must be a fully qualified path.", parameterName);
    }
    return directory;
  }
}
