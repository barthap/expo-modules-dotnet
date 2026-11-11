using System.Runtime.Serialization;

namespace ExpoCryptoDotnet;

// The digest algorithms of upstream Android (DigestAlgorithm.kt). Each member crosses as its
// upstream raw string. MD2, MD4, and SHA-224 have no .NET implementation, so they are absent and
// fail to decode.
public enum DigestAlgorithm
{
  MD5,
  [EnumMember(Value = "SHA-1")]
  SHA1,
  [EnumMember(Value = "SHA-256")]
  SHA256,
  [EnumMember(Value = "SHA-384")]
  SHA384,
  [EnumMember(Value = "SHA-512")]
  SHA512,
}
