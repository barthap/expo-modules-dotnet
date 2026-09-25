using Xunit;

namespace ExpoAssetDotnet.Tests;

public sealed class AssetRequestValidationTests
{
  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("images/icon.png")]
  public void Validate_RejectsEmptyOrRelativeUrl(string url)
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => AssetRequestValidation.Validate(url, null, "png")
    );

    Assert.Equal("Asset URL must be a non-empty absolute URI.", exception.Message);
  }

  [Fact]
  public void Validate_RejectsUnsupportedSchemeByName()
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => AssetRequestValidation.Validate("ftp://example.com/a.png", null, "png")
    );

    Assert.Equal("Unsupported asset URL scheme: 'ftp'.", exception.Message);
  }

  [Theory]
  [InlineData("")]
  [InlineData("../evil")]
  [InlineData("png/x")]
  [InlineData("p.g")]
  [InlineData("abcdefghijklmnopq")]
  public void Validate_RejectsTypeThatCannotFormASafeExtension(string type)
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => AssetRequestValidation.Validate("https://example.com/a", null, type)
    );

    Assert.Equal("Asset type must match ^[A-Za-z0-9]{1,16}$.", exception.Message);
  }

  [Theory]
  [InlineData("0123456789abcdef0123456789abcde")]
  [InlineData("0123456789abcdef0123456789abcdef0")]
  [InlineData("0123456789abcdef0123456789abcdeg")]
  public void Validate_RejectsMalformedMd5Hash(string md5Hash)
  {
    var exception = Assert.Throws<InvalidOperationException>(
        () => AssetRequestValidation.Validate("https://example.com/a", md5Hash, "png")
    );

    Assert.Equal(
        "Asset md5Hash must be null or 32 hexadecimal characters.",
        exception.Message
    );
  }

  [Fact]
  public void Validate_NormalizesAcceptedHashAndPreservesOriginalUrl()
  {
    const string url = "FILE:///tmp/My%20Asset.png";

    var request = AssetRequestValidation.Validate(
        url,
        "ABCDEF0123456789ABCDEF0123456789",
        "PNG"
    );

    Assert.Equal(url, request.OriginalUrl);
    Assert.Equal("file", request.Uri.Scheme);
    Assert.Equal("abcdef0123456789abcdef0123456789", request.Md5Hash);
    Assert.Equal("PNG", request.Type);
  }
}
