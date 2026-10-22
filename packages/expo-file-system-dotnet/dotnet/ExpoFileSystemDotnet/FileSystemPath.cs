using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

internal abstract class FileSystemPath : SharedObject
{
  private readonly Lock gate = new();
  private FileSystemLocation location;

  protected FileSystemPath(FileSystemLocation location)
  {
    this.location = location;
  }

  internal FileSystemLocation Location
  {
    get
    {
      lock (gate)
      {
        return location;
      }
    }
  }

  protected abstract void EnsureCorrectType(string nativePath);

  protected static void EnsureWritable(FileSystemLocation target)
  {
    if (target.IsWithin(FileSystemRoots.BundleLocation))
    {
      throw FileSystemErrors.MissingPermission(target.Uri);
    }
  }

  protected void DeleteCore()
  {
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (!File.Exists(path) && !Directory.Exists(path))
    {
      throw FileSystemErrors.UnableToDelete("path does not exist");
    }
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
      else
      {
        File.Delete(path);
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw FileSystemErrors.UnableToDelete(error.Message, error);
    }
  }

  protected void RenameCore(string newName)
  {
    lock (gate)
    {
      if (location.IsRoot)
      {
        throw FileSystemErrors.InvalidName(newName);
      }
      var target = location.WithName(newName);
      EnsureWritable(location);
      EnsureWritable(target);
      EnsureCorrectType(location.NativePath);
      if (location.IsDirectory)
      {
        Directory.Move(location.NativePath, target.NativePath);
      }
      else
      {
        File.Move(location.NativePath, target.NativePath);
      }
      location = target;
    }
  }
}
