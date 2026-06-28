namespace Expo.JSI;

public sealed class JavaScriptPromiseValue : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JavaScriptValueHolder holder;

  internal JavaScriptPromiseValue(JavaScriptValue value)
  {
    holder = new JavaScriptValueHolder(value);
  }

  public JavaScriptValue AsValue() => holder.AsValue();

  public void Dispose() => holder.Dispose();
}
