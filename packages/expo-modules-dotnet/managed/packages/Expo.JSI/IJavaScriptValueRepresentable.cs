namespace Expo.JSI;

/// <summary>
/// Represents a managed wrapper that can produce an owned JavaScript value handle.
/// </summary>
public interface IJavaScriptValueRepresentable
{
  /// <summary>
  /// Creates an owned value wrapper for the same JavaScript value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> owns a native handle and must be disposed by
  /// the caller.
  /// </remarks>
  JavaScriptValue AsValue();
}
