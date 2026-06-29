using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript function value handle.
/// </summary>
/// <remarks>
/// Dispose this wrapper when the function handle is no longer needed. Use <see cref="AsValue" />
/// to create an owned value wrapper for storing or passing the function as a JavaScript value.
/// </remarks>
public sealed class JavaScriptFunction : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  internal JavaScriptFunction(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  /// <summary>
  /// Clones this function as an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently. Disposing it does
  /// not dispose this function wrapper.
  /// </remarks>
  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->RetainValueAs(
          context.RuntimeHandle,
          handle,
          ExpoJsiValueExpectation.Function
      );
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to retain JavaScript function value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Releases the owned native function value handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseValueHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
