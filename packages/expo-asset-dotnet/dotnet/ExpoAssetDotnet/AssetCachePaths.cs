using System.Security.Cryptography;
using System.Text;

namespace ExpoAssetDotnet;

internal readonly record struct AssetCachePaths(
    string CacheDirectory,
    string FileName,
    string FinalPath
)
{
  private const string CacheSubdirectory = "ExponentAsset";

  internal static AssetCachePaths Create(string hostCacheRoot, AssetRequest request)
  {
    if (string.IsNullOrWhiteSpace(hostCacheRoot) ||
        !Path.IsPathFullyQualified(hostCacheRoot))
    {
      throw new ArgumentException(
          "Host cache root must be a fully qualified path.",
          nameof(hostCacheRoot)
      );
    }

    var cacheDirectory = Path.GetFullPath(Path.Combine(hostCacheRoot, CacheSubdirectory));
    Directory.CreateDirectory(cacheDirectory);

    var cacheId = request.Md5Hash ?? ComputeUrlCacheId(request.OriginalUrl);
    var fileName = $"ExponentAsset-{cacheId}.{request.Type}";
    var finalPath = Path.GetFullPath(Path.Combine(cacheDirectory, fileName));
    AssertContained(cacheDirectory, finalPath);

    return new AssetCachePaths(cacheDirectory, fileName, finalPath);
  }

  private static string ComputeUrlCacheId(string url)
  {
    // MD5 preserves the upstream cache identity. It is not used as a security primitive.
    var hash = MD5.HashData(Encoding.UTF8.GetBytes(url));
    return Convert.ToHexStringLower(hash);
  }

  private static void AssertContained(string cacheDirectory, string finalPath)
  {
    var relativePath = Path.GetRelativePath(cacheDirectory, finalPath);
    if (Path.IsPathRooted(relativePath) ||
        relativePath.Equals("..", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
    {
      throw new InvalidOperationException("Asset cache path escaped the ExponentAsset directory.");
    }
  }

}
