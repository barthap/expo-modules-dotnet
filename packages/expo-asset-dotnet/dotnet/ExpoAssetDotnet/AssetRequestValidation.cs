namespace ExpoAssetDotnet;

internal readonly record struct AssetRequest(
    string OriginalUrl,
    Uri Uri,
    string? Md5Hash,
    string Type
);

internal static class AssetRequestValidation
{
  internal static AssetRequest Validate(string url, string? md5Hash, string type)
  {
    if (string.IsNullOrWhiteSpace(url) ||
        !Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
      throw new InvalidOperationException("Asset URL must be a non-empty absolute URI.");
    }

    if (!IsSupportedScheme(uri.Scheme))
    {
      throw new InvalidOperationException($"Unsupported asset URL scheme: '{uri.Scheme}'.");
    }

    if (!IsValidType(type))
    {
      throw new InvalidOperationException("Asset type must match ^[A-Za-z0-9]{1,16}$.");
    }

    if (md5Hash is not null && !IsValidMd5Hash(md5Hash))
    {
      throw new InvalidOperationException(
          "Asset md5Hash must be null or 32 hexadecimal characters."
      );
    }

    return new AssetRequest(
        url,
        uri,
        md5Hash?.ToLowerInvariant(),
        type
    );
  }

  private static bool IsSupportedScheme(string scheme) =>
      scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
      scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
      scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

  private static bool IsValidType(string type)
  {
    if (type.Length is < 1 or > 16)
    {
      return false;
    }

    foreach (var character in type)
    {
      if (!char.IsAsciiLetterOrDigit(character))
      {
        return false;
      }
    }

    return true;
  }

  private static bool IsValidMd5Hash(string md5Hash)
  {
    if (md5Hash.Length != 32)
    {
      return false;
    }

    foreach (var character in md5Hash)
    {
      if (!Uri.IsHexDigit(character))
      {
        return false;
      }
    }

    return true;
  }
}
