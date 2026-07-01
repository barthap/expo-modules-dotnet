namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class JSAttribute : Attribute
{
  public JSAttribute()
  {
  }

  public JSAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
  }

  public string? Name { get; }
}
