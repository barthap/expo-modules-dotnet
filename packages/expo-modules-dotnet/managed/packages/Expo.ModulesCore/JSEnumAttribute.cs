namespace Expo.ModulesCore;

[AttributeUsage(
    AttributeTargets.Enum | AttributeTargets.Parameter | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class JSEnumAttribute : Attribute
{
  public JSEnumAttribute(EnumRepresentation representation)
  {
    Representation = representation;
  }

  public EnumRepresentation Representation { get; }
}
