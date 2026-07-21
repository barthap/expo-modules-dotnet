namespace Expo.ModulesCore;

/// <summary>
/// Marks a generated JavaScript method or instance accessor property on an Expo module, or the
/// exposed constructor of a shared-object class.
/// </summary>
/// <remarks>
/// Unnamed members use the generated lower-camel JavaScript name; an explicit name is used
/// verbatim. Readable properties become JavaScript getters and public or internal ordinary
/// setters become JavaScript setters. Use an attributed module declaration for authored APIs;
/// generated binding helpers are not an authoring surface.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
    Inherited = false)]
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
