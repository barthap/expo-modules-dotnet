using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>
/// Owns a JavaScript promise capability handle.
/// </summary>
/// <remarks>
/// Dispose this wrapper after resolving or rejecting the promise, or when abandoning the capability.
/// The JavaScript promise object itself may still live in JavaScript after this handle is released.
/// </remarks>
public sealed class JavaScriptPromise : IJavaScriptValueRepresentable, IDisposable
{
  private readonly JsiContext context;
  private ExpoJsiPromiseHandle handle;

  internal JavaScriptPromise(JsiContext context, ExpoJsiPromiseHandle handle)
  {
    this.context = context;
    this.handle = handle;
  }

  /// <summary>
  /// Converts this promise handle to an owned JavaScript value handle.
  /// </summary>
  /// <remarks>
  /// The returned <see cref="JavaScriptValue" /> must be disposed independently. Disposing it does
  /// not dispose this promise wrapper.
  /// </remarks>
  public JavaScriptValue AsValue()
  {
    ThrowIfDisposed();
    unsafe
    {
      var result = context.Api->ConvertPromiseToValue(context.RuntimeHandle, handle);
      if (result.Ok == 0 || result.Value == 0)
      {
        JsiContext.ThrowNativeError(
            result.Error,
            "Failed to convert JavaScript promise to value."
        );
      }
      return JavaScriptValue.FromOwnedHandle(context, result.Value);
    }
  }

  /// <summary>
  /// Resolves the JavaScript promise with an existing value.
  /// </summary>
  /// <remarks>
  /// This method borrows <paramref name="value" /> for the duration of the call. Ownership of
  /// <paramref name="value" /> stays with the caller.
  /// </remarks>
  public void Resolve(JavaScriptValue value)
  {
    Settle(value, reject: false);
  }

  /// <summary>
  /// Rejects the JavaScript promise with an existing error or reason value.
  /// </summary>
  /// <remarks>
  /// This method borrows <paramref name="error" /> for the duration of the call. Ownership of
  /// <paramref name="error" /> stays with the caller.
  /// </remarks>
  public void Reject(JavaScriptValue error)
  {
    Settle(error, reject: true);
  }

  /// <summary>
  /// Releases the owned native promise capability handle.
  /// </summary>
  public void Dispose()
  {
    if (handle != 0)
    {
      unsafe
      {
        context.Api->ReleasePromiseHandle(context.RuntimeHandle, handle);
      }
      handle = 0;
    }
  }

  private void Settle(JavaScriptValue value, bool reject)
  {
    ThrowIfDisposed();
    ArgumentNullException.ThrowIfNull(value);
    unsafe
    {
      var error = context.Api->SettlePromise(
          context.RuntimeHandle,
          handle,
          reject ? ExpoJsiPromiseSettlement.Reject : ExpoJsiPromiseSettlement.Resolve,
          value.Handle
      );
      context.ThrowIfError(error, reject
          ? "Failed to reject JavaScript promise."
          : "Failed to resolve JavaScript promise.");
    }
  }

  private void ThrowIfDisposed()
  {
    ObjectDisposedException.ThrowIf(handle == 0, this);
  }
}
