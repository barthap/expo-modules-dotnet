using System.Diagnostics.CodeAnalysis;

namespace Expo.JSI;

internal sealed class JavaScriptValueHolder : IJavaScriptValueRepresentable, IDisposable
{
  private JavaScriptValue? value;

  public JavaScriptValueHolder(JavaScriptValue value)
  {
    this.value = value;
  }

  public JavaScriptValue AsValue()
  {
    return AsValue(this);
  }

  internal JavaScriptValue AsValue(object owner)
  {
    ThrowIfDisposed(owner);
    return value.AsValue();
  }

  public void Dispose()
  {
    value?.Dispose();
    value = null;
  }

  [MemberNotNull(nameof(value))]
  private void ThrowIfDisposed() => ThrowIfDisposed(this);

  [MemberNotNull(nameof(value))]
  private void ThrowIfDisposed(object owner) =>
    ObjectDisposedException.ThrowIf(value is null, owner);
}
