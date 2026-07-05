namespace Expo.ModulesCore;

/// <summary>
/// Optional base class for authored Expo modules that want convenient access to their runtime
/// context.
/// </summary>
/// <remarks>
/// Inheriting from this class is not required. Modules that already need another base class can
/// declare a constructor that accepts <see cref="DotnetRuntimeContext" /> and store it themselves.
/// </remarks>
public abstract class Module
{
  /// <summary>
  /// Initializes a module with the runtime context that owns this module instance.
  /// </summary>
  /// <param name="runtimeContext">Runtime-scoped context supplied by generated registration.</param>
  protected Module(DotnetRuntimeContext runtimeContext)
  {
    RuntimeContext = runtimeContext ?? throw new ArgumentNullException(nameof(runtimeContext));
  }

  /// <summary>
  /// Gets the runtime-scoped context that owns this module instance.
  /// </summary>
  protected DotnetRuntimeContext RuntimeContext { get; }
}
