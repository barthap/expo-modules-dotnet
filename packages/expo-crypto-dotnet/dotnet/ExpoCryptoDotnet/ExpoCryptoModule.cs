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

  // Generated glue decodes the arguments on the JavaScript thread. The digest then runs on the .NET
  // thread pool, like Swift @JS(.concurrent) and Kotlin Dispatchers.Default. Only a string and two
  // enums cross into the lambda.
  [JS]
  public Task<string> DigestStringAsync(DigestAlgorithm algorithm, string data, DigestOptions options)
  {
    var encoding = options.Encoding;
    return Task.Run(() => ComputeDigestString(algorithm, data, encoding));
  }

  // Fills exactly the bytes of the view, in place, with no size limit. Returns nothing, like
  // Android; the facade returns the caller's object.
  [JS]
  public void GetRandomValues(Uint8Array typedArray) =>
      typedArray.WithBytes(bytes => RandomNumberGenerator.Fill(bytes));

  // Hashes exactly the bytes of data into a managed array, then copies min(hash length, output
  // length) bytes to the start of output, like Android. The data borrow ends before the output
  // borrow starts, so output and data may share bytes.
  [JS]
  public void Digest(DigestAlgorithm algorithm, Uint8Array output, Uint8Array data)
  {
    var hash = data.WithReadOnlyBytes(bytes => Hash(algorithm, bytes));
    output.WithBytes(bytes =>
    {
      var count = Math.Min(hash.Length, bytes.Length);
      hash.AsSpan(0, count).CopyTo(bytes);
    });
  }

  // A version 4 UUID with the RFC 4122 variant, 36 lowercase characters. .NET 10 Guid.NewGuid uses
  // the OS CSPRNG. The UUID is an identifier, not key material.
  [JS]
  public string RandomUUID() => Guid.NewGuid().ToString("D");

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
