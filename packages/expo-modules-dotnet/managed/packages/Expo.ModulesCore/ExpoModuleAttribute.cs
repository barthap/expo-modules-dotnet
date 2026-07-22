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

  /// <summary>
  /// The shared-object classes this module owns. Each listed type must be an
  /// <see cref="ExpoSharedObjectAttribute"/>-annotated <see cref="SharedObject"/> class.
  /// </summary>
  public Type[] Classes { get; set; } = Array.Empty<Type>();
}
