using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemFileTests
{
  private static (TemporaryDirectory Temp, string Path, FileSystemFile File) Fixture(byte[] contents)
  {
    var temp = new TemporaryDirectory();
    var path = System.IO.Path.Combine(temp.Path, "a.txt");
    System.IO.File.WriteAllBytes(path, contents);
    var file = new FileSystemFile(
        FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current)
    );
    return (temp, path, file);
  }

  [Fact]
  public async Task TextDecodesUtf8AndHonorsBom()
  {
    using var platform = PlatformScope.Supported();
    var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("zażółć")).ToArray();
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      Assert.Equal("zażółć", file.TextSync());
      Assert.Equal("zażółć", await file.Text());
    }
  }

  [Fact]
  public async Task Base64HasNoLineBreaks()
  {
    using var platform = PlatformScope.Supported();
    var bytes = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      var expected = Convert.ToBase64String(bytes);
      Assert.Equal(expected, file.Base64Sync());
      Assert.Equal(expected, await file.Base64());
      Assert.DoesNotContain('\n', expected);
    }
  }

  [Theory]
  [InlineData("MD5")]
  [InlineData("SHA-1")]
  [InlineData("SHA-256")]
  [InlineData("SHA-384")]
  [InlineData("SHA-512")]
  public async Task DigestReturnsLowercaseHex(string algorithm)
  {
    using var platform = PlatformScope.Supported();
    var bytes = Encoding.UTF8.GetBytes("hello");
    var (temp, _, file) = Fixture(bytes);
    using (temp)
    {
      byte[] expected = algorithm switch
      {
        "MD5" => MD5.HashData(bytes),
        "SHA-1" => SHA1.HashData(bytes),
        "SHA-256" => SHA256.HashData(bytes),
        "SHA-384" => SHA384.HashData(bytes),
        _ => SHA512.HashData(bytes),
      };
      Assert.Equal(Convert.ToHexStringLower(expected), await file.Digest(algorithm));
    }
  }

  [Fact]
  public async Task UnsupportedDigestFails()
  {
    using var platform = PlatformScope.Supported();
    var (temp, _, file) = Fixture([1]);
    using (temp)
    {
      var error = await Assert.ThrowsAsync<FileSystemException>(() => file.Digest("SHA-3"));
      Assert.Equal("Unsupported digest algorithm: SHA-3", error.Message);
    }
  }

  [Fact]
  public void MetadataForExistingFile()
  {
    using var platform = PlatformScope.Supported();
    var (temp, path, file) = Fixture(Encoding.UTF8.GetBytes("hello"));
    using (temp)
    {
      var written = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
      System.IO.File.SetLastWriteTimeUtc(path, written);
      var expectedMs = new DateTimeOffset(written).ToUnixTimeMilliseconds();

      Assert.Equal(5, file.Size);
      Assert.Equal(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("hello"))), file.Md5);
      Assert.Equal(expectedMs, file.ModificationTime);
      Assert.Equal(expectedMs, file.LastModified);
      Assert.NotNull(file.CreationTime);

      var info = file.Info(new InfoOptions(true));
      Assert.True(info.Exists);
      Assert.Equal(file.Uri, info.Uri);
      Assert.Equal(file.Md5, info.Md5);
      Assert.Equal(5, info.Size);
      Assert.Equal(expectedMs, info.ModificationTime);
      Assert.Null(file.Info().Md5);
    }
  }

  [Fact]
  public void MetadataForMissingFile()
  {
    using var platform = PlatformScope.Supported();
    var (temp, path, file) = Fixture([1]);
    using (temp)
    {
      System.IO.File.Delete(path);
      Assert.Equal(0, file.Size);
      Assert.Null(file.Md5);
      Assert.Null(file.ModificationTime);
      Assert.Null(file.CreationTime);
      Assert.Equal(new FileSystemFileInfo(false, file.Uri, null, null, null, null), file.Info());
      Assert.ThrowsAny<Exception>(() => file.TextSync());
    }
  }
}
