namespace Expo.JSI;

public sealed class JavaScriptErrorObject : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JavaScriptValueHolder holder;

  internal JavaScriptErrorObject(JavaScriptValue value)
  {
    holder = new JavaScriptValueHolder(value);
  }

  public string Name => GetStringProperty("name");

  public string Message => GetStringProperty("message");

  public string? Stack => GetNullableStringProperty("stack");

  public JavaScriptValue AsValue() => holder.AsValue();

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
