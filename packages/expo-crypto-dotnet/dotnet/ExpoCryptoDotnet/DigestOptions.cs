using System.Runtime.Serialization;

namespace ExpoCryptoDotnet;

// Upstream DigestOptions.Encoding. Each member crosses as its upstream raw string.
public enum CryptoEncoding
{
  [EnumMember(Value = "hex")]
  Hex,
  [EnumMember(Value = "base64")]
  Base64,
}

// Upstream DigestOptions: a missing or undefined encoding means hex.
public sealed record DigestOptions(CryptoEncoding Encoding = CryptoEncoding.Hex);
