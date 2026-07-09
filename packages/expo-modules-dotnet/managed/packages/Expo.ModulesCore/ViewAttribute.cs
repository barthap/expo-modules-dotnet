namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViewAttribute : Attribute
{
  public ViewAttribute(string componentName, Type viewType)
  {
    ComponentName = componentName;
    ViewType = viewType;
  }

  public string ComponentName { get; }

  public Type ViewType { get; }
}
