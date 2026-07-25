namespace Expo.ModulesCore;

/// <summary>
/// App-scoped directories a platform host supplies to a runtime context.
///
/// This type is the C# bridge's equivalent of the host-injected directories that
/// Expo's native <c>AppContext</c> exposes upstream as <c>cacheDirectory</c> and
/// <c>persistentFilesDirectory</c>. Only the platform host can resolve these
/// paths: every managed path API is user-wide or process-wide, so a portable
/// module that resolved its own directory would share one location with every
/// other app on the machine.
///
/// Each directory is independent. A host that knows one path and not the other
/// supplies just the one it knows, and leaves the other unconfigured rather than
/// fabricating a value.
///
/// The record validates strings and nothing more. It does not create the
/// directory, probe it for writability, or canonicalize it, because the managed
/// core never touches a disk. Creating a subdirectory before writing belongs to
/// the consuming module.
/// </summary>
public sealed record AppDirectories
{
  /// <summary>
  /// Gets a shared instance carrying no directories, for hosts that supply none.
  /// </summary>
  public static AppDirectories Unconfigured { get; } = new(null, null);

  /// <summary>
  /// Creates an immutable set of app-scoped directories.
  /// </summary>
  /// <param name="cacheDirectory">
  /// A fully qualified directory for temporary files, or <c>null</c> when the host
  /// supplies none.
  /// </param>
  /// <param name="persistentFilesDirectory">
  /// A fully qualified directory for files that must survive cache eviction, or
  /// <c>null</c> when the host supplies none.
  /// </param>
  /// <exception cref="ArgumentException">
  /// A supplied path is empty, whitespace-only, contains a NUL character, or is
  /// not fully qualified.
  /// </exception>
  public AppDirectories(
      string? cacheDirectory = null,
      string? persistentFilesDirectory = null
  )
  {
    CacheDirectory = Validate(cacheDirectory, nameof(cacheDirectory));
    PersistentFilesDirectory = Validate(
        persistentFilesDirectory,
        nameof(persistentFilesDirectory)
    );
  }

  /// <summary>Fully qualified app-scoped cache directory, or null if unconfigured.</summary>
  public string? CacheDirectory { get; }

  /// <summary>Fully qualified app-scoped persistent directory, or null if unconfigured.</summary>
  public string? PersistentFilesDirectory { get; }

  private static string? Validate(string? value, string parameterName)
  {
    if (value is null)
    {
      return null;
    }

    ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
    if (value.IndexOf('\0') >= 0)
    {
      throw new ArgumentException("App directory paths cannot contain NUL.", parameterName);
    }

    // A pure string check, so it stays off the disk. Resolving a relative value
    // instead would anchor it to the process working directory, which is not the
    // app scope the host means.
    if (!Path.IsPathFullyQualified(value))
    {
      throw new ArgumentException(
          "App directory paths must be fully qualified.",
          parameterName
      );
    }

    return value;
  }
}
