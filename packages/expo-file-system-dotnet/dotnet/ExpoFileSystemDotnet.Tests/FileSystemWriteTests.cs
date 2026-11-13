using System.Text;
using Expo.ModulesCore;
using Xunit;

namespace ExpoFileSystemDotnet.Tests;

public sealed class FileSystemWriteTests
{
  private static FileSystemFile NewFile(string path) =>
      new(FileSystemLocation.Parse(path, isDirectory: false, PathFlavors.Current));

  private static WriteContent Text(string value) => new WriteContent.Text(value);

  [Fact]
  public async Task TextIsUtf8WithoutByteOrderMark()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.txt");
    var file = NewFile(path);

    file.WriteSync(Text("zażółć"));
    Assert.Equal(Encoding.UTF8.GetBytes("zażółć"), File.ReadAllBytes(path));

    await file.Write(Text("abc"));
    Assert.Equal("abc"u8.ToArray(), File.ReadAllBytes(path));
  }

  [Fact]
  public void AppendAddsAndDefaultTruncates()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.txt");
    File.WriteAllText(path, "abc");
    var file = NewFile(path);

    file.WriteSync(Text("d"), new WriteOptions(Append: true));
    Assert.Equal("abcd", File.ReadAllText(path));

    file.WriteSync(Text("e"));
    Assert.Equal("e", File.ReadAllText(path));

    var created = Path.Combine(temp.Path, "new.txt");
    NewFile(created).WriteSync(Text("x"), new WriteOptions(Append: true));
    Assert.Equal("x", File.ReadAllText(created));
  }

  [Theory]
  [InlineData("AQID", "010203")]
  [InlineData("AQID\n", "010203")]
  [InlineData(" A Q\tI-D_", "010203")]
  [InlineData("AQ==", "01")]
  [InlineData("", "")]
  [InlineData("A", null)]
  [InlineData("AQI", null)]
  [InlineData("A=QI", null)]
  public void Base64IgnoresCharactersOutsideTheAlphabet(string input, string? expectedHex)
  {
    var bytes = FileSystemFile.DecodeBase64Leniently(input);

    Assert.Equal(expectedHex, bytes is null ? null : Convert.ToHexString(bytes));
  }

  [Fact]
  public async Task InvalidBase64FailsAndLeavesTheFile()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "a.bin");
    var file = NewFile(path);
    var base64 = new WriteOptions(Encoding: EncodingType.Base64);

    file.WriteSync(Text("AQID\n"), base64);
    Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));

    var error = Assert.Throws<UnableToWriteBase64DataException>(() => file.WriteSync(Text("A"), base64));
    Assert.Equal("ERR_UNABLE_TO_WRITE_BASE64_DATA", error.Code);
    Assert.Equal($"Unable to write base64 data to a file: {file.Uri}", error.Message);
    var asyncError = await Assert.ThrowsAsync<UnableToWriteBase64DataException>(
        () => file.Write(Text("A"), base64));
    Assert.Equal(error.Message, asyncError.Message);
    Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
  }

  [Fact]
  public void MissingParentFailsWithTheDotnetError()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var file = NewFile(Path.Combine(temp.Path, "missing", "a.txt"));

    var error = Assert.Throws<DirectoryNotFoundException>(() => file.WriteSync(Text("x")));

    Assert.IsNotAssignableFrom<CodedException>(error);
    Assert.False(Directory.Exists(Path.Combine(temp.Path, "missing")));
  }

  [Fact]
  public async Task PermissionThenTypeAreCheckedBeforeContent()
  {
    using var platform = PlatformScope.Supported();
    using var temp = new TemporaryDirectory();
    var folder = Path.Combine(temp.Path, "d");
    Directory.CreateDirectory(folder);
    var invalidBase64 = new WriteOptions(Encoding: EncodingType.Base64);

    var typeError = Assert.Throws<InvalidTypeFileException>(
        () => NewFile(folder).WriteSync(Text("A"), invalidBase64));
    Assert.Equal("ERR_INVALID_TYPE_FILE", typeError.Code);
    await Assert.ThrowsAsync<InvalidTypeFileException>(() => NewFile(folder).Write(Text("A"), invalidBase64));

    using var bundle = FileSystemRoots.OverrideBundleForTests(temp.Path);
    var bundled = NewFile(folder);
    var permissionError = Assert.Throws<MissingPermissionException>(
        () => bundled.WriteSync(Text("A"), invalidBase64));
    Assert.Equal($"Missing permission for uri: {bundled.Uri}", permissionError.Message);
    await Assert.ThrowsAsync<MissingPermissionException>(() => bundled.Write(Text("x")));
    Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
  }
}
