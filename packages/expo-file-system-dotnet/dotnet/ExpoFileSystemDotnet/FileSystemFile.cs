using System.Text;
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
      throw WrongTypeException.ForFile();
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
    if (File.Exists(path) && !options.Overwrite)
    {
      throw new FileAlreadyExistsException(target.Uri);
    }
    try
    {
      if (options.Intermediates)
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
      throw new UnableToCreateException(error.Message);
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
    options ??= new InfoOptions();
    var target = Location;
    EnsureCorrectType(target.NativePath);
    if (!File.Exists(target.NativePath))
    {
      return new FileSystemFileInfo(false, target.Uri, null, null, null, null);
    }
    return new FileSystemFileInfo(
        true,
        target.Uri,
        options.Md5 ? FileMetadata.HashHex(target.NativePath, "MD5") : null,
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

  [JS]
  public void WriteSync(WriteContent content, WriteOptions? options = null)
  {
    options ??= new WriteOptions();
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var append = options.Append;
    switch (content)
    {
      case WriteContent.Text(var text):
        WriteBytes(target, EncodeText(target, text, options.Encoding), append);
        break;
      case WriteContent.Buffer(var buffer):
        buffer.WithReadOnlyBytes(bytes => WriteBytes(target, bytes, append));
        break;
      case WriteContent.View(var view):
        view.WithReadOnlyBytes(bytes => WriteBytes(target, bytes, append));
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(content));
    }
  }

  [JS]
  public Task Write(WriteContent content, WriteOptions? options = null)
  {
    options ??= new WriteOptions();
    var target = Location;
    EnsureWritable(target);
    EnsureCorrectType(target.NativePath);
    var append = options.Append;
    var encoding = options.Encoding;
    // A JavaScript-backed buffer is readable only on the JavaScript thread, so binary content is
    // copied here, before the Promise returns. Later changes to the buffer do not reach the file.
    return content switch
    {
      WriteContent.Text(var text) => Task.Run(() => WriteBytes(target, EncodeText(target, text, encoding), append)),
      WriteContent.Buffer(var buffer) => WriteCopyAsync(target, buffer.ToArray(), append),
      WriteContent.View(var view) => WriteCopyAsync(target, view.ToArray(), append),
      _ => throw new ArgumentOutOfRangeException(nameof(content)),
    };
  }

  [JS]
  public Uint8Array BytesSync()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return ReadBytes(target);
  }

  [JS]
  public Task<Uint8Array> Bytes()
  {
    var target = Location;
    EnsureCorrectType(target.NativePath);
    return Task.Run(() => ReadBytes(target));
  }

  /// <summary>Decodes base64 text, ignoring every character outside the base64 alphabet.</summary>
  /// <returns>The bytes, or <see langword="null" /> when the remaining characters do not decode.</returns>
  internal static byte[]? DecodeBase64Leniently(string text)
  {
    var kept = new char[text.Length];
    var count = 0;
    foreach (var c in text)
    {
      if (char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=')
      {
        kept[count++] = c;
      }
    }
    var bytes = new byte[count / 4 * 3];
    return Convert.TryFromBase64Chars(kept.AsSpan(0, count), bytes, out var written)
        ? bytes.AsSpan(0, written).ToArray()
        : null;
  }

  /// <summary>Returns the length as an <see cref="int" />, the largest length a view can hold.</summary>
  internal static int CheckedLength(long length, string uri) =>
      length > int.MaxValue ? throw new FileTooLargeException(uri) : (int)length;

  private static byte[] EncodeText(FileSystemLocation target, string text, EncodingType encoding) =>
      encoding == EncodingType.Base64
          ? DecodeBase64Leniently(text) ?? throw new UnableToWriteBase64DataException(target.Uri)
          : Encoding.UTF8.GetBytes(text);

  private static Task WriteCopyAsync(FileSystemLocation target, byte[] bytes, bool append) =>
      Task.Run(() => WriteBytes(target, bytes, append));

  // OpenOrCreate and SetLength, not FileMode.Create: on Windows, Create fails on a hidden file.
  private static void WriteBytes(FileSystemLocation target, ReadOnlySpan<byte> bytes, bool append)
  {
    using var stream = new FileStream(
        target.NativePath,
        append ? FileMode.Append : FileMode.OpenOrCreate,
        FileAccess.Write,
        FileShare.Read
    );
    if (!append)
    {
      stream.SetLength(0);
    }
    stream.Write(bytes);
  }

  private static Uint8Array ReadBytes(FileSystemLocation target)
  {
    using var stream = new FileStream(target.NativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var view = Uint8Array.Allocate(CheckedLength(stream.Length, target.Uri));
    try
    {
      view.WithBytes(bytes => stream.ReadExactly(bytes));
      return view;
    }
    catch
    {
      view.Dispose();
      throw;
    }
  }

  protected override void EnsureCorrectType(string nativePath)
  {
    if (Directory.Exists(nativePath))
    {
      throw new InvalidTypeFileException();
    }
  }

  private static FileSystemLocation ParseSupported(string uri)
  {
    FileSystemPlatform.EnsureSupported();
    return FileSystemLocation.Parse(uri, isDirectory: false, PathFlavors.Current);
  }
}
