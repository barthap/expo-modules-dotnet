namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OnStartObservingAttribute : Attribute
{
  public OnStartObservingAttribute()
  {
  }

  public OnStartObservingAttribute(string eventName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    EventName = eventName;
  }

  public string? EventName { get; }
}
