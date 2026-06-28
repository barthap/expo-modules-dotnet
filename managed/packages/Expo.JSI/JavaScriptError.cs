using System.Diagnostics.CodeAnalysis;

namespace Expo.JSI;

public sealed class JavaScriptError : IJavaScriptValueRepresentable, IDisposable
{
  private JavaScriptValue? value;

  internal JavaScriptError(JavaScriptValue value)
  {
    this.value = value;
  }

  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    return value.AsValue();
  }

  public void Dispose()
  {
    value?.Dispose();
    value = null;
  }

  [MemberNotNull(nameof(value))]
  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(value is null, this);
}
