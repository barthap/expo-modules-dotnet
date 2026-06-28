namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript function handle.
/// </summary>
/// <remarks>
/// Dispose this wrapper when the function handle is no longer needed. Use <see cref="AsValue" />
/// to create an owned value wrapper for storing or passing the function as a JavaScript value.
/// </remarks>
public sealed class JavaScriptFunction : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiFunctionHandle handle;

  internal JavaScriptFunction(JsiContext context, ExpoJsiFunctionHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  /// <summary>
  /// Converts this function handle to an owned JavaScript value handle.
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
      var result = context.Api->ConvertFunctionToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript function to value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Releases the owned native function handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseFunctionHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
