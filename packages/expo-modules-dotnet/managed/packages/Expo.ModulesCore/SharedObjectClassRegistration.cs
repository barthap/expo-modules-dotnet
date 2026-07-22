using Expo.JSI;

namespace Expo.ModulesCore;

/// <summary>
/// Owned registration of one authored shared-object class for one registry: the exact sealed
/// managed type plus the shared class prototype used by every paired instance.
/// </summary>
/// <remarks>
/// The registration owns its prototype wrapper so registry entries keep retaining only lifetime
/// state, NativeState state, and the weak counterpart. Generated code owns each registration for
/// the lifetime of its class installation and disposes it with the runtime context.
/// </remarks>
internal sealed class SharedObjectClassRegistration : IDisposable
{
  private readonly JavaScriptObject prototype;
  private bool disposed;

  private SharedObjectClassRegistration(
      SharedObjectRegistry registry,
      Type sharedObjectType,
      JavaScriptObject prototype)
  {
    Registry = registry;
    SharedObjectType = sharedObjectType;
    this.prototype = prototype;
  }

  internal SharedObjectRegistry Registry { get; }

  internal Type SharedObjectType { get; }

  /// <summary>
  /// Borrowed class prototype for generated member installation. Owned by this registration;
  /// callers must not dispose it.
  /// </summary>
  internal JavaScriptObject Prototype
  {
    get
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      return prototype;
    }
  }

  internal static SharedObjectClassRegistration Create(
      SharedObjectRegistry registry,
      Type sharedObjectType)
  {
    ArgumentNullException.ThrowIfNull(registry);
    ArgumentNullException.ThrowIfNull(sharedObjectType);
    if (!sharedObjectType.IsSubclassOf(typeof(SharedObject)))
    {
      throw new ArgumentException(
          $"'{sharedObjectType}' is not a SharedObject subclass.",
          nameof(sharedObjectType)
      );
    }

    var prototype = SharedObjectPrototype.CreateClassPrototype(registry.Runtime, registry);
    return new SharedObjectClassRegistration(registry, sharedObjectType, prototype);
  }

  internal JavaScriptObject CreateInstanceObject()
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return Registry.Runtime.CreateObjectWithPrototype(prototype);
  }

  public void Dispose()
  {
    if (!disposed)
    {
      disposed = true;
      prototype.Dispose();
    }
  }
}
