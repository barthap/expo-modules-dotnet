namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript error object value.
/// </summary>
/// <remarks>
/// Dispose this wrapper when the managed owner no longer needs to keep the error value alive.
/// Property accessors read from the underlying JavaScript object without exposing disposable
/// temporary values.
/// </remarks>
public sealed class JavaScriptErrorObject : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JavaScriptValueHolder holder;

  internal JavaScriptErrorObject(JavaScriptValue value)
  {
    holder = new JavaScriptValueHolder(value);
  }

  /// <summary>
  /// Gets the error object's <c>name</c> property coerced to a string.
  /// </summary>
  public string Name => GetStringProperty("name");

  /// <summary>
  /// Gets the error object's <c>message</c> property coerced to a string.
  /// </summary>
  public string Message => GetStringProperty("message");

  /// <summary>
  /// Gets the error object's <c>stack</c> property coerced to a string, or <see langword="null" />
  /// when the property is nullish.
  /// </summary>
  public string? Stack => GetNullableStringProperty("stack");

  /// <summary>
  /// Creates a new owned value wrapper for this error object.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently.
  /// </remarks>
  public JavaScriptValue AsValue() => holder.AsValue();

  /// <summary>
  /// Releases the owned error value handle stored by this wrapper.
  /// </summary>
  public void Dispose() => holder.Dispose();

  private string GetStringProperty(string name)
  {
    return GetNullableStringProperty(name) ?? string.Empty;
  }

  private string? GetNullableStringProperty(string name)
  {
    var property = holder.Ref.AsObject().GetProperty(name);
    return property.IsNullish
        ? null
        : property.CoerceToString();
  }
}
