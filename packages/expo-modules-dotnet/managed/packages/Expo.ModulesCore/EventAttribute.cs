namespace Expo.ModulesCore;

/// <summary>
/// Declares an awaitable module event generated as a cached <c>Func&lt;Task&gt;</c> or
/// <c>Func&lt;T, Task&gt;</c> property.
/// </summary>
/// <remarks>
/// The default JavaScript name lowercases only the first character of the property name; an
/// explicit name is preserved. Generated registration initializes the property before
/// <see cref="OnCreateAttribute"/> hooks run. The returned task represents dispatch completion.
/// Direct <see cref="Expo.JSI.JavaScriptValue"/> payloads remain caller-owned until that task
/// completes, while direct <see cref="ArrayBuffer"/> payloads are retained by the dispatcher.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class EventAttribute : Attribute
{
  public EventAttribute()
  {
  }

  public EventAttribute(string name) => Name = name;

  public string? Name { get; }
}
