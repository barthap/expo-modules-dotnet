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
    ThrowIfDisposed();
    return value.AsValue();
  }

  public JavaScriptValueRef Ref
  {
    get
    {
      ThrowIfDisposed();
      return value.Ref;
    }
  }

  public void Dispose()
  {
    value?.Dispose();
    value = null;
  }

  [MemberNotNull(nameof(value))]
  [MemberNotNull(nameof(value))]
  private void ThrowIfDisposed() =>
    ObjectDisposedException.ThrowIf(value is null, this);
}
