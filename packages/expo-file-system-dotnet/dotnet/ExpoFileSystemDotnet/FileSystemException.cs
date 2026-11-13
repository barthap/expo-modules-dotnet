using Expo.ModulesCore;

namespace ExpoFileSystemDotnet;

/// <summary>A precondition failure of the module. The subclass name gives the JavaScript code.</summary>
internal abstract class FileSystemException : CodedException
{
  private protected FileSystemException(string message)
      : base(message)
  {
  }

  private protected FileSystemException(string code, string message)
      : base(code, message)
  {
  }
}

internal sealed class InvalidLocationException(string input)
    : FileSystemException($"Invalid file system location: '{input}'");

internal sealed class InvalidNameException(string name)
    : FileSystemException($"Invalid file name: '{name}'");

internal sealed class InvalidTypeFileException()
    : FileSystemException("A folder with the same name already exists in the file location");

internal sealed class InvalidTypeDirectoryException()
    : FileSystemException("A file with the same name already exists in the directory location");

// Upstream iOS throws `Exception(name: "wrong type")`, and its code keeps the space.
internal sealed class WrongTypeException : FileSystemException
{
  private WrongTypeException(string message)
      : base("ERR_WRONG TYPE", message)
  {
  }

  internal static WrongTypeException ForFile() => new("tried to create a file with a directory path");

  internal static WrongTypeException ForDirectory() => new("tried to create a directory with a file path");
}

internal sealed class FileAlreadyExistsException(string uri)
    : FileSystemException($"File '{uri}' already exists");

internal sealed class UnableToCreateException(string reason)
    : FileSystemException($"Unable to create file or directory: {reason}");

internal sealed class UnableToDeleteException(string reason)
    : FileSystemException($"Unable to delete file or directory: {reason}");

internal sealed class MissingPermissionException(string uri)
    : FileSystemException($"Missing permission for uri: {uri}");

internal sealed class UnsupportedDigestAlgorithmException(string algorithm)
    : FileSystemException($"Unsupported digest algorithm: {algorithm}");

internal sealed class UnableToWriteBase64DataException(string uri)
    : FileSystemException($"Unable to write base64 data to a file: {uri}");

internal sealed class CopyOrMoveDirectoryToFileException()
    : FileSystemException("Unable to copy or move a directory to a file");

internal sealed class DestinationAlreadyExistsException()
    : FileSystemException("Destination already exists");

internal sealed class DestinationDoesNotExistException()
    : FileSystemException("The destination path does not exist");

internal sealed class UnableToCopyException(string reason)
    : FileSystemException($"Unable to copy file or directory: {reason}");

internal sealed class UnableToMoveException(string reason)
    : FileSystemException($"Unable to move file or directory: {reason}");

internal sealed class UnableToDetermineBundleDirectoryException()
    : FileSystemException("Unable to determine the bundle directory");

internal sealed class FileTooLargeException(string uri)
    : FileSystemException($"File is too large to read into memory: {uri}");
