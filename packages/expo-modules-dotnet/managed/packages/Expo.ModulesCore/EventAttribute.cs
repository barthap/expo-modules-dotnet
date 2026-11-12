namespace Expo.ModulesCore;

/// <summary>
/// Declares an awaitable module event generated as a cached <c>Func&lt;Task&gt;</c> or
/// <c>Func&lt;T, Task&gt;</c> property.
/// </summary>
/// <remarks>
/// A bare <c>[Event]</c> drops a leading <c>On</c> or <c>on</c> that an uppercase letter follows,
/// then lowercases only the first character, so <c>OnProgress</c> emits <c>progress</c> and
/// <c>OnURLChange</c> emits <c>uRLChange</c>. Any other name only lowercases its first character,
/// so <c>Online</c> emits <c>online</c>. An explicit name is used verbatim, so
/// <c>[Event("onProgress")]</c> keeps an <c>onX</c> wire name. Generated registration initializes
/// the property before
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
