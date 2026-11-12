namespace ExpoFileSystemDotnet;

/// <summary>
/// Steps 3 to 7 of copy and move, then the file system work. The caller has checked permission and
/// type. A location with <see cref="FileSystemLocation.IsDirectory" /> set comes from a
/// <c>FileSystemDirectory</c>, any other from a <c>FileSystemFile</c>.
/// </summary>
internal static class FileSystemRelocation
{
  /// <summary>Moves a directory and returns <see langword="true" />, or returns <see langword="false" /> when the target is on another volume.</summary>
  internal delegate bool MoveDirectoryStep(string source, string target);

  private static readonly EnumerationOptions AllEntries = new()
  {
    AttributesToSkip = 0,
    IgnoreInaccessible = false,
    RecurseSubdirectories = false,
  };

  internal static void Copy(FileSystemLocation source, FileSystemLocation destination, bool overwrite)
  {
    var (target, _) = Prepare(source, destination, overwrite, move: false);
    CopyEntry(source, target);
  }

  /// <summary>Moves the source and returns the location it now has.</summary>
  internal static FileSystemLocation Move(
      FileSystemLocation source,
      FileSystemLocation destination,
      bool overwrite,
      MoveDirectoryStep? moveDirectory = null
  )
  {
    var (target, renameInPlace) = Prepare(source, destination, overwrite, move: true);
    if (renameInPlace)
    {
      // The same item in other letter case: rename it and delete nothing.
      MoveEntry(source, target);
      return target;
    }
    if (!source.IsDirectory)
    {
      // File.Move copies and deletes by itself when the target is on another volume.
      File.Move(source.NativePath, target.NativePath);
      return target;
    }
    if (!(moveDirectory ?? TryMoveDirectory)(source.NativePath, target.NativePath))
    {
      CopyEntry(source, target);
      try
      {
        Directory.Delete(source.NativePath, recursive: true);
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        throw new UnableToMoveException("Failed to delete source after move");
      }
    }
    return target;
  }

  /// <summary>Returns whether both paths are equal after each segment is replaced by the name its parent directory stores.</summary>
  /// <remarks>Both paths must exist.</remarks>
  internal static bool IsSameItem(string first, string second)
  {
    if (string.Equals(first, second, StringComparison.Ordinal))
    {
      return true;
    }
    // A stored name differs from a requested name only in letter case, so paths that differ in
    // more than case name different items.
    if (!string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }
    var root = Path.GetPathRoot(first) ?? "";
    var firstSegments = first[root.Length..].Split(Path.DirectorySeparatorChar);
    var secondSegments = second[root.Length..].Split(Path.DirectorySeparatorChar);
    var parent = root;
    for (var i = 0; i < firstSegments.Length; i++)
    {
      if (
          !string.Equals(firstSegments[i], secondSegments[i], StringComparison.Ordinal)
          && !string.Equals(
              StoredName(parent, firstSegments[i]),
              StoredName(parent, secondSegments[i]),
              StringComparison.Ordinal
          )
      )
      {
        return false;
      }
      parent = Path.Join(parent, firstSegments[i]);
    }
    return true;
  }

  // Returns the target. RenameInPlace is true for a move that only changes letter case.
  private static (FileSystemLocation Target, bool RenameInPlace) Prepare(
      FileSystemLocation source,
      FileSystemLocation destination,
      bool overwrite,
      bool move
  )
  {
    var target = ResolveTarget(source, destination);
    // The destination was checked before. A destination outside the bundle can still resolve to a
    // target that is the bundle itself.
    FileSystemPath.EnsureWritable(target);
    var sourcePath = source.NativePath;
    if (source.IsDirectory ? !Directory.Exists(sourcePath) : !File.Exists(sourcePath))
    {
      throw source.IsDirectory
          ? new DirectoryNotFoundException($"Could not find a part of the path '{sourcePath}'.")
          : new FileNotFoundException($"Could not find file '{sourcePath}'.", sourcePath);
    }
    var targetPath = target.NativePath;
    var targetExists = File.Exists(targetPath) || Directory.Exists(targetPath);
    if (targetExists && IsSameItem(sourcePath, targetPath))
    {
      if (!move)
      {
        throw new UnableToCopyException("source and destination are the same");
      }
      if (string.Equals(source.Uri, target.Uri, StringComparison.Ordinal))
      {
        throw new UnableToMoveException("source and destination are the same");
      }
      return (target, true);
    }
    if (source.IsDirectory && target.IsWithin(source))
    {
      throw move
          ? new UnableToMoveException("destination is inside the source")
          : new UnableToCopyException("destination is inside the source");
    }
    if (targetExists)
    {
      if (!overwrite)
      {
        throw new DestinationAlreadyExistsException();
      }
      DeleteEntry(targetPath);
    }
    return (target, false);
  }

  private static FileSystemLocation ResolveTarget(FileSystemLocation source, FileSystemLocation destination)
  {
    if (destination.IsDirectory)
    {
      if (Directory.Exists(destination.NativePath))
      {
        return destination.Child(source.Name, source.IsDirectory);
      }
      if (!source.IsDirectory || !Directory.Exists(destination.Parent.NativePath))
      {
        throw new DestinationDoesNotExistException();
      }
      return destination;
    }
    if (source.IsDirectory)
    {
      throw new CopyOrMoveDirectoryToFileException();
    }
    if (!Directory.Exists(destination.Parent.NativePath))
    {
      throw new DestinationDoesNotExistException();
    }
    return destination;
  }

  // Copies the source to a target that does not exist. On failure, removes what it created and
  // rethrows the original error.
  private static void CopyEntry(FileSystemLocation source, FileSystemLocation target)
  {
    try
    {
      if (source.IsDirectory)
      {
        CopyDirectory(source.NativePath, target.NativePath);
      }
      else
      {
        File.Copy(source.NativePath, target.NativePath, overwrite: false);
      }
    }
    catch
    {
      try
      {
        DeleteEntry(target.NativePath);
      }
      catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
      {
        // The original error is the one to report.
      }
      throw;
    }
  }

  private static void CopyDirectory(string source, string target)
  {
    Directory.CreateDirectory(target);
    foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", AllEntries))
    {
      var destination = Path.Join(target, entry.Name);
      if (entry.LinkTarget is { } linkTarget)
      {
        if (entry is DirectoryInfo)
        {
          Directory.CreateSymbolicLink(destination, linkTarget);
        }
        else
        {
          File.CreateSymbolicLink(destination, linkTarget);
        }
      }
      else if (entry is DirectoryInfo)
      {
        CopyDirectory(entry.FullName, destination);
      }
      else
      {
        File.Copy(entry.FullName, destination, overwrite: false);
      }
    }
  }

  private static void MoveEntry(FileSystemLocation source, FileSystemLocation target)
  {
    if (source.IsDirectory)
    {
      Directory.Move(source.NativePath, target.NativePath);
    }
    else
    {
      File.Move(source.NativePath, target.NativePath);
    }
  }

  // Deletes a file, a link, or a directory tree. A link is removed, never followed.
  private static void DeleteEntry(string path)
  {
    FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    if (!entry.Exists && entry.LinkTarget is null)
    {
      return;
    }
    if (entry is DirectoryInfo directory && entry.LinkTarget is null)
    {
      directory.Delete(recursive: true);
    }
    else
    {
      entry.Delete();
    }
  }

  private static bool TryMoveDirectory(string source, string target)
  {
    try
    {
      Directory.Move(source, target);
      return true;
    }
    catch (IOException) when (!OnSameVolume(source, target))
    {
      return false;
    }
  }

  private static bool OnSameVolume(string source, string target)
  {
    var comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    var roots = DriveInfo.GetDrives().Select(drive => drive.RootDirectory.FullName).ToList();
    var targetParent = Path.GetDirectoryName(target) ?? target;
    return string.Equals(
        FileSystemModule.SelectDriveRoot(source, roots, comparison),
        FileSystemModule.SelectDriveRoot(targetParent, roots, comparison),
        comparison
    );
  }

  private static string StoredName(string parent, string name)
  {
    string? caseMatch = null;
    foreach (var entry in Directory.EnumerateFileSystemEntries(parent, "*", AllEntries))
    {
      var stored = Path.GetFileName(entry);
      if (string.Equals(stored, name, StringComparison.Ordinal))
      {
        return stored;
      }
      if (caseMatch is null && string.Equals(stored, name, StringComparison.OrdinalIgnoreCase))
      {
        caseMatch = stored;
      }
    }
    return caseMatch ?? name;
  }
}
