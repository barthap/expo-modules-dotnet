namespace Expo.ModulesCore.Tests.Modules;

// A plain enum: no attributes, so each member crosses as its C# name.
public enum PlainMode
{
  Slow,
  Fast,
}

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
}
