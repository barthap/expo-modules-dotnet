using System.Runtime.Serialization;
using Expo.ModulesCore.Tests.ExternalTypes;

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

public enum DigestEncoding
{
  [EnumMember(Value = "hex")]
  Hex,
  [EnumMember(Value = "base64")]
  Base64,
}

public sealed record DigestOptions(DigestAlgorithm Algorithm);

// The default is rendered as a cast of the member's number, so it never goes through a string.
public sealed record DigestSettings(string Label, DigestAlgorithm Algorithm = DigestAlgorithm.SHA1);

[Flags]
public enum DigestFlags
{
  None = 0,
  Upper = 1,
  Spaced = 2,
}

// [EnumMember] without Value names no string, so a number enum keeps crossing as its number.
[JSEnum(EnumRepresentation.Number)]
public enum QuietLevel
{
  [EnumMember]
  Low = 1,
  High = 2,
}

[ExpoSharedObject]
public sealed partial class DigestHasher : SharedObject
{
  [JS]
  public DigestHasher(DigestAlgorithm algorithm = DigestAlgorithm.SHA256)
  {
    Algorithm = algorithm;
  }

  [JS]
  public DigestAlgorithm Algorithm { get; set; }

  [JS]
  public DigestAlgorithm Swap(DigestAlgorithm next)
  {
    var previous = Algorithm;
    Algorithm = next;
    return previous;
  }
}

// Two string enum cases, then a string case.
[Union]
public abstract record DigestInput
{
  public sealed record Algorithm(DigestAlgorithm Value) : DigestInput;
  public sealed record Output(DigestEncoding Value) : DigestInput;
  public sealed record Text(string Value) : DigestInput;
}

[Union]
public abstract record DigestChoice
{
  public sealed record Algorithm(DigestAlgorithm Value) : DigestChoice;
  public sealed record Output(DigestEncoding Value) : DigestChoice;
}

[ExpoModule("EnumScenarios", Classes = new[] { typeof(DigestHasher) })]
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

  [JS]
  public DigestAlgorithm? EchoNullable(DigestAlgorithm? algorithm) => algorithm;

  [JS]
  public DigestOptions EchoOptions(DigestOptions options) => options;

  [JS]
  public IReadOnlyList<DigestAlgorithm> EchoList(IReadOnlyList<DigestAlgorithm> algorithms) => algorithms;

  [JS]
  public Dictionary<string, DigestAlgorithm> EchoMap(Dictionary<string, DigestAlgorithm> algorithms) => algorithms;

  [JS]
  public DigestAlgorithm Preferred { get; set; } = DigestAlgorithm.SHA1;

  // A constant is a getter-only property.
  [JS]
  public DigestAlgorithm Strongest => DigestAlgorithm.SHA256;

  [JS]
  public Task<DigestAlgorithm> EchoLater(DigestAlgorithm algorithm) => Task.FromResult(algorithm);

  [JS]
  public DigestAlgorithm CallWith(JavaScriptCallback<(DigestAlgorithm, int), DigestAlgorithm> callback) =>
      callback.Invoke((DigestAlgorithm.SHA1, 2));

  [Event]
  public partial Func<DigestAlgorithm, Task> OnAlgorithm { get; }

  [Event]
  public partial Func<DigestOptions, Task> OnOptions { get; }

  [JS]
  public Task EmitDigest(DigestAlgorithm algorithm) =>
      Task.WhenAll(OnAlgorithm(algorithm), OnOptions(new DigestOptions(algorithm)));

  [JS]
  public DigestAlgorithm PickDefault(DigestAlgorithm algorithm = DigestAlgorithm.SHA1) => algorithm;

  [JS]
  public DigestSettings EchoSettings(DigestSettings settings) => settings;

  [JS]
  public DigestFlags EchoFlags(DigestFlags flags) => flags;

  [JS]
  public DigestFlags CombinedFlags() => DigestFlags.Upper | DigestFlags.Spaced;

  [JS]
  public NumberMode EchoNumberMode(NumberMode mode) => mode;

  [JS]
  public QuietLevel EchoQuiet(QuietLevel level) => level;

  [JS]
  public string DescribeInput(DigestInput input) => input switch
  {
    DigestInput.Algorithm(var algorithm) => $"algorithm:{algorithm}",
    DigestInput.Output(var output) => $"output:{output}",
    DigestInput.Text(var text) => $"text:{text}",
    _ => throw new ArgumentOutOfRangeException(nameof(input)),
  };

  [JS]
  public string DescribeChoice(DigestChoice choice) => choice switch
  {
    DigestChoice.Algorithm(var algorithm) => $"algorithm:{algorithm}",
    DigestChoice.Output(var output) => $"output:{output}",
    _ => throw new ArgumentOutOfRangeException(nameof(choice)),
  };

  [JS]
  public DigestInput MakeInput(string which) => which switch
  {
    "algorithm" => new DigestInput.Algorithm(DigestAlgorithm.SHA1),
    "output" => new DigestInput.Output(DigestEncoding.Hex),
    _ => new DigestInput.Text(which),
  };

  [JS]
  public ExternalAlgorithm EchoExternal(ExternalAlgorithm algorithm) => algorithm;
}
