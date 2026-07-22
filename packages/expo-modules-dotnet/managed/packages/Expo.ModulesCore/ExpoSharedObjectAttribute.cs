namespace Expo.ModulesCore;

/// <summary>
/// Marks a top-level, non-generic, sealed, partial class derived from
/// <see cref="SharedObject"/> as an authored shared-object class.
/// </summary>
/// <remarks>
/// An unnamed declaration exposes the authored C# type name verbatim as the JavaScript class
/// name; an explicit name is used verbatim instead. The class becomes available to JavaScript
/// only when an owning module lists it in <see cref="ExpoModuleAttribute.Classes"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExpoSharedObjectAttribute : Attribute
{
  public ExpoSharedObjectAttribute()
  {
  }

  public ExpoSharedObjectAttribute(string name)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(name);
    Name = name;
  }

  public string? Name { get; }
}
