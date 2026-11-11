using System.Runtime.Serialization;

namespace Expo.ModulesCore.Tests.Modules;

// A plain enum: no attributes, so each member crosses as its C# name.
public enum PlainMode
{
  Slow,
  Fast,
}

// The upstream expo-crypto algorithm names. A member with [EnumMember(Value)] crosses as that
// string; MD5 has none, so it crosses as its C# name.
public enum DigestAlgorithm
{
  MD5,
  [EnumMember(Value = "SHA-1")]
  SHA1,
  [EnumMember(Value = "SHA-256")]
  SHA256,
}

[JSEnum(EnumRepresentation.Number)]
public enum NumberMode
{
  Slow,
  Fast,
}

public sealed record NumberModeHolder(NumberMode Mode);

[ExpoModule("EnumScenarios")]
public sealed partial class EnumScenariosModule : Module
{
  public EnumScenariosModule(DotnetRuntimeContext context)
      : base(context)
  {
  }

  [JS]
  public PlainMode EchoPlain(PlainMode mode) => mode;

  // 42 names no member of PlainMode.
  [JS]
  public PlainMode PlainOutOfRange() => (PlainMode)42;

  [JS]
  public string DescribeNumberModeAsString([JSEnum(EnumRepresentation.String)] NumberMode mode) => mode.ToString();

  [JS]
  public NumberModeHolder EchoNumberModeHolder(NumberModeHolder holder) => holder;

  [JS]
  [return: JSEnum(EnumRepresentation.Number)]
  public PlainMode? EchoPlainAsNullableNumber([JSEnum(EnumRepresentation.Number)] PlainMode? mode) => mode;

  [JS]
  public DigestAlgorithm EchoAlgorithm(DigestAlgorithm algorithm) => algorithm;

  // Tells JavaScript which C# member arrived.
  [JS]
  public string DescribeAlgorithm(DigestAlgorithm algorithm) => algorithm.ToString();

  [JS]
  public string EchoText(string text) => text;

  // 42 names no member of DigestAlgorithm.
  [JS]
  public DigestAlgorithm UndefinedAlgorithm() => (DigestAlgorithm)42;

  [JS]
  public Task<string> HashAsync(DigestAlgorithm algorithm) => Task.FromResult(algorithm.ToString());
}
