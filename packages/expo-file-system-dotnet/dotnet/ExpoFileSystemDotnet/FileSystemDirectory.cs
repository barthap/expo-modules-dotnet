using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

[ExpoSharedObject]
internal sealed partial class FileSystemDirectory : FileSystemPath
{
  private static readonly EnumerationOptions AllEntries = new()
  {
    AttributesToSkip = 0,
    IgnoreInaccessible = false,
    RecurseSubdirectories = false,
  };

  private static readonly EnumerationOptions AllFilesRecursive = new()
  {
    AttributesToSkip = 0,
    IgnoreInaccessible = false,
    RecurseSubdirectories = true,
  };

  [JS]
  public FileSystemDirectory(string uri)
      : this(ParseSupported(uri))
  {
  }

  internal FileSystemDirectory(FileSystemLocation location)
      : base(location)
  {
  }

  [JS]
  public string Uri => Location.Uri;

  [JS]
  public bool Exists => Directory.Exists(Location.NativePath);

  [JS]
  public void ValidatePath()
  {
    if (!Location.HasDirectoryPath)
    {
      throw WrongTypeException.ForDirectory();
    }
  }

  [JS]
  public void Create(CreateOptions? options = null)
  {
    options ??= new CreateOptions();
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var path = target.NativePath;
    if (Directory.Exists(path))
    {
      if (options.Idempotent)
      {
        return;
      }
      if (!options.Overwrite)
      {
        throw new FileAlreadyExistsException(target.Uri);
      }
    }
    try
    {
      if (Directory.Exists(path))
      {
        Directory.Delete(path, recursive: true);
      }
      if (!options.Intermediates && !Directory.Exists(target.Parent.NativePath))
      {
        throw new UnableToCreateException("parent directory does not exist");
      }
      Directory.CreateDirectory(path);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw new UnableToCreateException(error.Message);
    }
  }

  [JS]
  public long? Size
  {
    get
    {
      try
      {
        var path = Location.NativePath;
        if (!Directory.Exists(path))
        {
          return null;
        }
        return new DirectoryInfo(path)
            .EnumerateFiles("*", AllFilesRecursive)
            .Sum(file => file.Length);
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }
  }

  [JS]
  public FileSystemDirectoryInfo Info()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    if (!Directory.Exists(target.NativePath))
    {
      return new FileSystemDirectoryInfo(false, target.Uri, null, null, null, null);
    }
    var files = Directory
        .EnumerateFileSystemEntries(target.NativePath, "*", AllEntries)
        .Select(Path.GetFileName)
        .Select(name => name!)
        .ToList();
    return new FileSystemDirectoryInfo(
        true,
        target.Uri,
        files,
        Size,
        FileMetadata.ModificationTime(target.NativePath, isDirectory: true),
        FileMetadata.CreationTime(target.NativePath, isDirectory: true)
    );
  }

  [JS]
  public IReadOnlyList<DirectoryEntryRecord> ListAsRecords()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Directory
        .EnumerateFileSystemEntries(target.NativePath, "*", AllEntries)
        .Select(entry =>
        {
          var isDirectory = Directory.Exists(entry);
          var child = target.Child(Path.GetFileName(entry), isDirectory);
          return new DirectoryEntryRecord(isDirectory, child.Uri);
        })
        .ToList();
  }

  // `content` is ignored on purpose: upstream iOS ignores it and the upstream facade passes `mimeType` here.
  [JS]
  public FileSystemFile CreateFile(string name, string? content)
  {
    var file = new FileSystemFile(Location.Child(name, isDirectory: false));
    file.Create();
    return file;
  }

  [JS]
  public FileSystemDirectory CreateDirectory(string name)
  {
    var directory = new FileSystemDirectory(Location.Child(name, isDirectory: true));
    directory.Create();
    return directory;
  }

  [JS]
  public void Delete() => DeleteCore();

  [JS]
  public void Rename(string newName) => RenameCore(newName);

  [JS]
  public void CopySync(FileSystemPath destination, RelocationOptions? options = null) =>
      CopyCore(destination, options);

  [JS]
  public Task Copy(FileSystemPath destination, RelocationOptions? options = null) =>
      CopyCoreAsync(destination, options);

  [JS]
  public void MoveSync(FileSystemPath destination, RelocationOptions? options = null) =>
      MoveCore(destination, options);

  [JS]
  public Task Move(FileSystemPath destination, RelocationOptions? options = null) =>
      MoveCoreAsync(destination, options);

  protected override void EnsureCorrectType(string nativePath)
  {
    if (File.Exists(nativePath))
    {
      throw new InvalidTypeDirectoryException();
    }
  }

  private static FileSystemLocation ParseSupported(string uri)
  {
    FileSystemPlatform.EnsureSupported();
    return FileSystemLocation.Parse(uri, isDirectory: true, PathFlavors.Current);
  }
}
