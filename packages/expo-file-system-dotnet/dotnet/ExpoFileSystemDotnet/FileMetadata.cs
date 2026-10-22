using System.Security.Cryptography;

namespace ExpoFileSystemDotnet;

internal static class FileMetadata
{
  internal static long? ToUnixMilliseconds(DateTime utc) =>
      new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

  internal static long? ModificationTime(string nativePath, bool isDirectory) =>
      Read(
          nativePath,
          isDirectory,
          () => isDirectory ? Directory.GetLastWriteTimeUtc(nativePath) : File.GetLastWriteTimeUtc(nativePath)
      );

  internal static long? CreationTime(string nativePath, bool isDirectory) =>
      Read(
          nativePath,
          isDirectory,
          () => isDirectory ? Directory.GetCreationTimeUtc(nativePath) : File.GetCreationTimeUtc(nativePath)
      );

  internal static string HashHex(string nativePath, string algorithm)
  {
    var hash = SelectHash(algorithm);
    using var stream = new FileStream(nativePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return Convert.ToHexStringLower(hash.Sync(stream));
  }

  internal static async Task<string> HashHexAsync(
      string nativePath,
      string algorithm,
      CancellationToken token
  )
  {
    var hash = SelectHash(algorithm);
    await using var stream = new FileStream(
        nativePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        81920,
        useAsync: true
    );
    return Convert.ToHexStringLower(await hash.Async(stream, token));
  }

  private static long? Read(string nativePath, bool isDirectory, Func<DateTime> read)
  {
    try
    {
      var exists = isDirectory ? Directory.Exists(nativePath) : File.Exists(nativePath);
      return exists ? ToUnixMilliseconds(read()) : null;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      return null;
    }
  }

  private static (Func<Stream, byte[]> Sync, Func<Stream, CancellationToken, ValueTask<byte[]>> Async) SelectHash(
      string algorithm
  ) =>
      algorithm switch
      {
        "MD5" => (MD5.HashData, MD5.HashDataAsync),
        "SHA-1" => (SHA1.HashData, SHA1.HashDataAsync),
        "SHA-256" => (SHA256.HashData, SHA256.HashDataAsync),
        "SHA-384" => (SHA384.HashData, SHA384.HashDataAsync),
        "SHA-512" => (SHA512.HashData, SHA512.HashDataAsync),
        _ => throw FileSystemErrors.UnsupportedDigest(algorithm),
      };
}
