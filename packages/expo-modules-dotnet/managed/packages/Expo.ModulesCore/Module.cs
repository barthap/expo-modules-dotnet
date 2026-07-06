using Expo.ModulesCore.Codecs;

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

  /// <summary>
  /// Emits a declared module event without payload.
  /// </summary>
  protected Task SendEventAsync(string eventName, CancellationToken cancellationToken = default) =>
      RuntimeContext.Events.EmitAsync(this, eventName, cancellationToken);

  /// <summary>
  /// Emits a declared module event with one encoded payload value.
  /// </summary>
  protected Task SendEventAsync<TCodec, T>(
      string eventName,
      T payload,
      CancellationToken cancellationToken = default)
      where TCodec : struct, IJavaScriptCodec<T> =>
      RuntimeContext.Events.EmitAsync<TCodec, T>(this, eventName, payload, cancellationToken);
}
