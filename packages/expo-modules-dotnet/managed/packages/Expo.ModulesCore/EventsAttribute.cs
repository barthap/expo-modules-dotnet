namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventsAttribute : Attribute
{
  public EventsAttribute(params string[] names)
  {
    ArgumentNullException.ThrowIfNull(names);

    Names = Array.AsReadOnly(names.ToArray());
  }

  public IReadOnlyList<string> Names { get; }
}
