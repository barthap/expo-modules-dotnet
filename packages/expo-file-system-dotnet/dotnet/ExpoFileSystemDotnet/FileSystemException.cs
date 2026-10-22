namespace ExpoFileSystemDotnet;

internal sealed class FileSystemException(string message, Exception? inner = null)
    : Exception(message, inner);

internal static class FileSystemErrors
{
  internal static FileSystemException InvalidLocation(string input) =>
      new($"Invalid file system location: '{input}'");
  internal static FileSystemException InvalidName(string name) =>
      new($"Invalid file name: '{name}'");
  internal static FileSystemException FolderAtFileLocation() =>
      new("A folder with the same name already exists in the file location");
  internal static FileSystemException FileAtDirectoryLocation() =>
      new("A file with the same name already exists in the directory location");
  internal static FileSystemException WrongTypeFile() =>
      new("tried to create a file with a directory path");
  internal static FileSystemException WrongTypeDirectory() =>
      new("tried to create a directory with a file path");
  internal static FileSystemException AlreadyExists(string uri) =>
      new($"File '{uri}' already exists");
  internal static FileSystemException UnableToCreate(string reason, Exception? inner = null) =>
      new($"Unable to create file or directory: {reason}", inner);
  internal static FileSystemException UnableToDelete(string reason, Exception? inner = null) =>
      new($"Unable to delete file or directory: {reason}", inner);
  internal static FileSystemException MissingPermission(string uri) =>
      new($"Missing permission for uri: {uri}");
  internal static FileSystemException UnsupportedDigest(string algorithm) =>
      new($"Unsupported digest algorithm: {algorithm}");
}
