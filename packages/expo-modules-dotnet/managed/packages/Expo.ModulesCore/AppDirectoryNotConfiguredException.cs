namespace Expo.ModulesCore;

/// <summary>
/// Thrown when module code reads an app-scoped directory the platform host never
/// supplied.
///
/// The managed core has no acceptable fallback here. Every path a portable
/// runtime can resolve on its own is user-wide or process-wide, so two apps on
/// one machine would resolve the same location and clobber each other. Failing
/// loudly instead points at the host adapter that owes the path, matching
/// upstream Android, which throws when its <c>AppDirectoriesService</c> is not
/// registered.
/// </summary>
public sealed class AppDirectoryNotConfiguredException : InvalidOperationException
{
  /// <summary>
  /// Creates the exception for one unconfigured accessor. Only the core raises it,
  /// so module authors can catch the type without being able to fabricate it.
  /// </summary>
  /// <param name="directoryProperty">
  /// Name of the accessor whose directory the host left unconfigured.
  /// </param>
  internal AppDirectoryNotConfiguredException(string directoryProperty)
      : base($"The host did not configure {directoryProperty}.")
  {
    DirectoryProperty = directoryProperty;
  }

  /// <summary>
  /// Gets the name of the accessor whose directory the host left unconfigured.
  /// </summary>
  public string DirectoryProperty { get; }
}
