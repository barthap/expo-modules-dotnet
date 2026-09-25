using Xunit;

namespace ExpoAssetDotnet.Tests;

public sealed class AssetCachePathsTests
{
  [Fact]
  public void Create_DerivesCacheIdFromUrlAndCreatesExponentAssetDirectory()
  {
    var root = CreateTemporaryRoot();
    try
    {
      var request = AssetRequestValidation.Validate(
          "https://example.com/a.png",
          null,
          "png"
      );

      var paths = AssetCachePaths.Create(root, request);

      Assert.Equal("ExponentAsset-8c11d92f07c76baf29e49afd2301deb2.png", paths.FileName);
      Assert.Equal(Path.Combine(root, "ExponentAsset"), paths.CacheDirectory);
      Assert.Equal(Path.Combine(paths.CacheDirectory, paths.FileName), paths.FinalPath);
      Assert.True(Directory.Exists(paths.CacheDirectory));
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public void Create_UsesNormalizedSuppliedHash()
  {
    var root = CreateTemporaryRoot();
    try
    {
      var request = AssetRequestValidation.Validate(
          "https://example.com/a.png",
          "ABCDEF0123456789ABCDEF0123456789",
          "png"
      );

      var paths = AssetCachePaths.Create(root, request);

      Assert.Equal("ExponentAsset-abcdef0123456789abcdef0123456789.png", paths.FileName);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  [InlineData("relative/cache")]
  public void Create_RejectsInvalidHostCacheRootWithoutCreatingIt(string root)
  {
    var request = AssetRequestValidation.Validate("https://example.com/a.png", null, "png");

    Assert.Throws<ArgumentException>(() => AssetCachePaths.Create(root, request));
  }

  [Fact]
  public void Create_RejectsPathThatEscapesAfterAValidationRegression()
  {
    var root = CreateTemporaryRoot();
    try
    {
      var request = new AssetRequest(
          "https://example.com/a.png",
          new Uri("https://example.com/a.png"),
          "abcdef0123456789abcdef0123456789",
          "x/../../../escape"
      );

      var exception = Assert.Throws<InvalidOperationException>(
          () => AssetCachePaths.Create(root, request)
      );

      Assert.Equal("Asset cache path escaped the ExponentAsset directory.", exception.Message);
    }
    finally
    {
      Directory.Delete(root, recursive: true);
    }
  }

  private static string CreateTemporaryRoot()
  {
    var root = Path.Combine(Path.GetTempPath(), $"expo-asset-dotnet-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
  }
}
