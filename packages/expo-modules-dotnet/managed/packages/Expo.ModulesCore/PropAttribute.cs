namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PropAttribute : Attribute
{
  public PropAttribute(string name)
  {
    Name = name;
  }

  public string Name { get; }
}
