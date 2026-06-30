namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExpoModuleAttribute : Attribute
{
  public ExpoModuleAttribute()
  {
  }

  public ExpoModuleAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
  }

  public string? Name { get; }
}
