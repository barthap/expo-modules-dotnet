using Expo.ModulesCore;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemExceptionTests
{
  // JavaScript reads `code` and `message`. These are the upstream values, so code that branches
  // on `error.code` works the same here as on iOS and Android.
  [Fact]
  public void EveryErrorCarriesItsCodeAndMessageAndNoCause()
  {
    CodedException[] errors =
    [
      new InvalidLocationException("x"),
      new InvalidNameException("a/b"),
      new InvalidTypeFileException(),
      new InvalidTypeDirectoryException(),
      WrongTypeException.ForFile(),
      WrongTypeException.ForDirectory(),
      new FileAlreadyExistsException("file:///a"),
      new UnableToCreateException("r"),
      new UnableToDeleteException("r"),
      new MissingPermissionException("file:///a"),
      new UnsupportedDigestAlgorithmException("SHA-3"),
      new UnableToWriteBase64DataException("file:///a"),
      new CopyOrMoveDirectoryToFileException(),
      new DestinationAlreadyExistsException(),
      new DestinationDoesNotExistException(),
      new UnableToCopyException("r"),
      new UnableToMoveException("r"),
      new UnableToDetermineBundleDirectoryException(),
      new FileTooLargeException("file:///a"),
    ];

    Assert.All(errors, error => Assert.IsAssignableFrom<FileSystemException>(error));
    Assert.All(errors, error => Assert.Null(error.InnerException));
    Assert.Equal(
        new[]
        {
          "ERR_INVALID_LOCATION|Invalid file system location: 'x'",
          "ERR_INVALID_NAME|Invalid file name: 'a/b'",
          "ERR_INVALID_TYPE_FILE|A folder with the same name already exists in the file location",
          "ERR_INVALID_TYPE_DIRECTORY|A file with the same name already exists in the directory location",
          "ERR_WRONG TYPE|tried to create a file with a directory path",
          "ERR_WRONG TYPE|tried to create a directory with a file path",
          "ERR_FILE_ALREADY_EXISTS|File 'file:///a' already exists",
          "ERR_UNABLE_TO_CREATE|Unable to create file or directory: r",
          "ERR_UNABLE_TO_DELETE|Unable to delete file or directory: r",
          "ERR_MISSING_PERMISSION|Missing permission for uri: file:///a",
          "ERR_UNSUPPORTED_DIGEST_ALGORITHM|Unsupported digest algorithm: SHA-3",
          "ERR_UNABLE_TO_WRITE_BASE64_DATA|Unable to write base64 data to a file: file:///a",
          "ERR_COPY_OR_MOVE_DIRECTORY_TO_FILE|Unable to copy or move a directory to a file",
          "ERR_DESTINATION_ALREADY_EXISTS|Destination already exists",
          "ERR_DESTINATION_DOES_NOT_EXIST|The destination path does not exist",
          "ERR_UNABLE_TO_COPY|Unable to copy file or directory: r",
          "ERR_UNABLE_TO_MOVE|Unable to move file or directory: r",
          "ERR_UNABLE_TO_DETERMINE_BUNDLE_DIRECTORY|Unable to determine the bundle directory",
          "ERR_FILE_TOO_LARGE|File is too large to read into memory: file:///a",
        },
        errors.Select(error => $"{error.Code}|{error.Message}")
    );
  }
}
