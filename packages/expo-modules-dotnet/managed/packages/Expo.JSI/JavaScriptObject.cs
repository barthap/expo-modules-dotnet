using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript object value handle.
/// </summary>
/// <remarks>
/// Methods on this type return owned wrappers. Dispose every returned value/object wrapper when it
/// is no longer needed. Use scoped refs when temporary traversal should avoid disposable
/// intermediates.
/// </remarks>
public sealed class JavaScriptObject : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiValueHandle handle;

  internal JavaScriptObject(JsiContext context, ExpoJsiValueHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  private JavaScriptObjectInner Inner
  {
    get
    {
      ThrowIfDisposed();
      return new JavaScriptObjectInner(context, handle);
    }
  }

  /// <summary>
  /// Sets a property to an existing JavaScript value.
  /// </summary>
  /// <remarks>
  /// This method borrows <paramref name="value" /> for the duration of the call. Ownership of
  /// <paramref name="value" /> stays with the caller.
  /// </remarks>
  public void SetProperty(string name, JavaScriptValue value)
  {
    ArgumentNullException.ThrowIfNull(value);
    Inner.SetProperty(name, value.Handle);
  }

  /// <summary>
  /// Gets a property as an owned JavaScript value.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed by the caller.
  /// </remarks>
  public JavaScriptValue GetProperty(string name) =>
    JavaScriptValue.FromOwnedHandle(context, Inner.GetProperty(name));

  /// <summary>
  /// Gets this object's own property names as managed strings.
  /// </summary>
  public IReadOnlyList<string> GetOwnPropertyNames() => Inner.GetOwnPropertyNames();

  /// <summary>
  /// Clones this object as an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently. Disposing it does
  /// not dispose this object wrapper.
  /// </remarks>
  public JavaScriptValue AsValue() =>
    JavaScriptValue.FromOwnedHandle(context, Inner.AsValue());

  /// <summary>
  /// Releases the owned native object value handle.
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
