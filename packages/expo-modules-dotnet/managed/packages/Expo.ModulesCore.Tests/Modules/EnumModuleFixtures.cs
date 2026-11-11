namespace Expo.ModulesCore.Tests.Modules;

// A plain enum: no attributes, so each member crosses as its C# name.
public enum PlainMode
{
  Slow,
  Fast,
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
}
