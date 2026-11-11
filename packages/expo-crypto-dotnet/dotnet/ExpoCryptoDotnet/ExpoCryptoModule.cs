using System.Security.Cryptography;
using System.Text;
using Expo.ModulesCore;

namespace ExpoCryptoDotnet;

// The upstream expo-crypto@58.0.7 ExpoCrypto native module. Digests, random bytes, and UUIDs use
// .NET built-ins only, so the module has no platform branch and no ABI surface.
[ExpoModule("ExpoCrypto")]
public sealed partial class ExpoCryptoModule : Module
{
  public ExpoCryptoModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [JS]
  public string DigestString(DigestAlgorithm algorithm, string data, DigestOptions options) =>
      ComputeDigestString(algorithm, data, options.Encoding);

  // Hex is lowercase. Base64 is standard and padded, with no line breaks.
  private static string ComputeDigestString(DigestAlgorithm algorithm, string data, CryptoEncoding encoding)
  {
    var hash = Hash(algorithm, Encoding.UTF8.GetBytes(data));
    return encoding switch
    {
      CryptoEncoding.Hex => Convert.ToHexStringLower(hash),
      CryptoEncoding.Base64 => Convert.ToBase64String(hash),
      _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };
  }

  private static byte[] Hash(DigestAlgorithm algorithm, ReadOnlySpan<byte> data) => algorithm switch
  {
    DigestAlgorithm.MD5 => MD5.HashData(data),
    DigestAlgorithm.SHA1 => SHA1.HashData(data),
    DigestAlgorithm.SHA256 => SHA256.HashData(data),
    DigestAlgorithm.SHA384 => SHA384.HashData(data),
    DigestAlgorithm.SHA512 => SHA512.HashData(data),
    _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
  };
}
