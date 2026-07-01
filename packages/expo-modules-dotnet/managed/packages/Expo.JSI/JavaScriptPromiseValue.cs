namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript promise value.
/// </summary>
/// <remarks>
/// This wrapper stores the promise as an owned <see cref="JavaScriptValue" />. Dispose it when the
/// managed owner no longer needs to keep that value handle alive.
/// </remarks>
public sealed class JavaScriptPromiseValue : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JavaScriptValueHolder holder;

  internal JavaScriptPromiseValue(JavaScriptValue value)
  {
    holder = new JavaScriptValueHolder(value);
  }

  /// <summary>
  /// Creates a new owned value wrapper for the promise.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently.
  /// </remarks>
  public JavaScriptValue AsValue() => holder.AsValue();

  /// <summary>
  /// Releases the owned promise value handle stored by this wrapper.
  /// </summary>
  public void Dispose() => holder.Dispose();
}
