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
      throw new MissingPermissionException(target.Uri);
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
      throw new UnableToDeleteException("path does not exist");
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
      throw new UnableToDeleteException(error.Message);
    }
  }

  protected void CopyCore(FileSystemPath destination, RelocationOptions? options)
  {
    var (source, target) = CheckRelocation(destination, move: false);
    FileSystemRelocation.Copy(source, target, options?.Overwrite ?? false);
  }

  protected Task CopyCoreAsync(FileSystemPath destination, RelocationOptions? options)
  {
    var (source, target) = CheckRelocation(destination, move: false);
    var overwrite = options?.Overwrite ?? false;
    return Task.Run(() => FileSystemRelocation.Copy(source, target, overwrite));
  }

  protected void MoveCore(FileSystemPath destination, RelocationOptions? options)
  {
    var (source, target) = CheckRelocation(destination, move: true);
    SetLocation(FileSystemRelocation.Move(source, target, options?.Overwrite ?? false));
  }

  protected Task MoveCoreAsync(FileSystemPath destination, RelocationOptions? options)
  {
    var (source, target) = CheckRelocation(destination, move: true);
    var overwrite = options?.Overwrite ?? false;
    return Task.Run(() => SetLocation(FileSystemRelocation.Move(source, target, overwrite)));
  }

  // Steps 1 and 2 of a copy or move: permission, then the type of both objects. They run on the
  // calling thread with the locations captured here.
  private (FileSystemLocation Source, FileSystemLocation Destination) CheckRelocation(
      FileSystemPath destination,
      bool move
  )
  {
    var source = Location;
    var target = destination.Location;
    if (move)
    {
      EnsureWritable(source);
    }
    EnsureWritable(target);
    EnsureCorrectType(source.NativePath);
    destination.EnsureCorrectType(target.NativePath);
    return (source, target);
  }

  private void SetLocation(FileSystemLocation value)
  {
    lock (gate)
    {
      location = value;
    }
  }

  protected void RenameCore(string newName)
  {
    lock (gate)
    {
      if (location.IsRoot)
      {
        throw new InvalidNameException(newName);
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
