namespace Expo.ModulesCore;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class OnStopObservingAttribute : Attribute
{
  public OnStopObservingAttribute()
  {
  }

  public OnStopObservingAttribute(string eventName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
    EventName = eventName;
  }

  public string? EventName { get; }
}
