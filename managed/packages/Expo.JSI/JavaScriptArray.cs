namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript array handle.
/// </summary>
/// <remarks>
/// Indexed reads return owned <see cref="JavaScriptValue" /> wrappers that must be disposed by the
/// caller. Use <see cref="JavaScriptArrayRef" /> for scoped, non-disposable traversal.
/// </remarks>
public sealed class JavaScriptArray : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiArrayHandle handle;

  internal JavaScriptArray(JsiContext context, ExpoJsiArrayHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  private JavaScriptArrayInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptArrayInner(context, handle);
    }
  }

  /// <summary>
  /// Gets the current JavaScript array length.
  /// </summary>
  public uint Length => Inner.Length;

  /// <summary>
  /// Gets an array element as an owned JavaScript value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue GetValue(uint index) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetValue(index));

  /// <summary>
  /// Sets an array element to an existing JavaScript value.
  /// </summary>
  /// <remarks>
  /// This method borrows <paramref name="value" /> for the duration of the call. Ownership of
  /// <paramref name="value" /> stays with the caller.
  /// </remarks>
  public void SetValue(uint index, JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Inner.SetValue(index, value.Handle);
  }

  /// <summary>
  /// Converts this array handle to an owned JavaScript object handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptObject" /> must be disposed independently.
  /// </remarks>
  public JavaScriptObject AsObject() => new(context, Inner.AsObject());

  /// <summary>
  /// Converts this array handle to an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently.
  /// </remarks>
  public JavaScriptValue AsValue() =>
    JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());

  /// <summary>
  /// Releases the owned native array handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleaseArrayHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
