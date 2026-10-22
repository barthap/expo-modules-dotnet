using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

[ExpoSharedObject]
internal sealed partial class FileSystemFile : FileSystemPath
{
  [JS]
  public FileSystemFile(string uri)
      : this(ParseSupported(uri))
  {
  }

  internal FileSystemFile(FileSystemLocation location)
      : base(location)
  {
  }

  [JS]
  public string Uri => Location.Uri;

  [JS]
  public bool Exists => File.Exists(Location.NativePath);

  [JS]
  public void ValidatePath()
  {
    if (Location.HasDirectoryPath)
    {
      throw FileSystemErrors.WrongTypeFile();
    }
  }

  [JS]
  public void Create(CreateOptions? options = null)
  {
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (File.Exists(path) && options?.Overwrite != true)
    {
      throw FileSystemErrors.AlreadyExists(target.Uri);
    }
    try
    {
      if (options?.Intermediates == true)
      {
        Directory.CreateDirectory(target.Parent.NativePath);
      }
      if (File.Exists(path))
      {
        // Delete removes a symlink itself, so the create below never writes through it.
        File.Delete(path);
      }
      using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw FileSystemErrors.UnableToCreate(error.Message, error);
    }
  }

  [JS]
  public void Delete() => DeleteCore();

  [JS]
  public void Rename(string newName) => RenameCore(newName);

  [JS]
  public long Size
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        return File.Exists(path) ? new FileInfo(path).Length : 0;
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return 0;
      }
    }
  }

  [JS]
  public string? Md5
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        return File.Exists(path) ? FileMetadata.HashHex(path, "MD5") : null;
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }
  }

  [JS]
  public long? ModificationTime => FileMetadata.ModificationTime(Location.NativePath, isDirectory: false);

  [JS]
  public long? LastModified => ModificationTime;

  [JS]
  public long? CreationTime => FileMetadata.CreationTime(Location.NativePath, isDirectory: false);

  [JS]
  public FileSystemFileInfo Info(InfoOptions? options = null)
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    if (!File.Exists(target.NativePath))
    {
      return new FileSystemFileInfo(false, target.Uri, null, null, null, null);
    }
    return new FileSystemFileInfo(
        true,
        target.Uri,
        options?.Md5 == true ? FileMetadata.HashHex(target.NativePath, "MD5") : null,
        new FileInfo(target.NativePath).Length,
        FileMetadata.ModificationTime(target.NativePath, isDirectory: false),
        FileMetadata.CreationTime(target.NativePath, isDirectory: false)
    );
  }

  [JS]
  public string TextSync()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return File.ReadAllText(target.NativePath);
  }

  [JS]
  public Task<string> Text()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(() => File.ReadAllTextAsync(target.NativePath));
  }

  [JS]
  public string Base64Sync()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Convert.ToBase64String(File.ReadAllBytes(target.NativePath));
  }

  [JS]
  public Task<string> Base64()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(async () => Convert.ToBase64String(await File.ReadAllBytesAsync(target.NativePath)));
  }

  [JS]
  public Task<string> Digest(string algorithm)
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(() => FileMetadata.HashHexAsync(target.NativePath, algorithm, CancellationToken.None));
  }

  protected override void EnsureCorrectType(string nativePath)
  {
    if (Directory.Exists(nativePath))
    {
      throw FileSystemErrors.FolderAtFileLocation();
    }
  }

  private static FileSystemLocation ParseSupported(string uri)
  {
    FileSystemPlatform.EnsureSupported();
    return FileSystemLocation.Parse(uri, isDirectory: false, PathFlavors.Current);
  }
}
