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
  private readonly object handleGate = new();
  private ExpoJsiPromiseHandle handle;
  private int activeLeases;
  private bool disposeRequested;

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
    using var lease = AcquireHandle();
    unsafe
    {
      var result = context.Api->ConvertPromiseToValue(context.RuntimeHandle, lease.Handle);
      if (!result.IsOk)
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
  /// <remarks>
  /// If a call to <see cref="AsValue" />, <see cref="Resolve" />, or <see cref="Reject" /> is in
  /// flight, this method marks the handle for release and returns without waiting; the last
  /// in-flight call releases the native handle when it exits.
  /// </remarks>
  public void Dispose()
  {
    ExpoJsiPromiseHandle valueToRelease = 0;
    lock (handleGate)
    {
      if (disposeRequested)
      {
        return;
      }
      disposeRequested = true;
      if (activeLeases == 0)
      {
        valueToRelease = handle;
        handle = 0;
      }
    }
    if (valueToRelease != 0)
    {
      unsafe
      {
        context.Api->ReleasePromiseHandle(context.RuntimeHandle, valueToRelease);
      }
    }
  }

  private void Settle(JavaScriptValue value, bool reject)
  {
    using var lease = AcquireHandle();
    ArgumentNullException.ThrowIfNull(value);
    unsafe
    {
      var error = context.Api->SettlePromise(
          context.RuntimeHandle,
          lease.Handle,
          reject ? ExpoJsiPromiseSettlement.Reject : ExpoJsiPromiseSettlement.Resolve,
          value.Handle
      );
      context.ThrowIfError(error, reject
          ? "Failed to reject JavaScript promise."
          : "Failed to resolve JavaScript promise.");
    }
  }

  private HandleLease AcquireHandle()
  {
    lock (handleGate)
    {
      if (disposeRequested || handle == 0)
      {
        throw new ObjectDisposedException(nameof(JavaScriptPromise));
      }
      var lease = new HandleLease(this, handle);
      activeLeases++;
      return lease;
    }
  }

  private void ReleaseLease()
  {
    ExpoJsiPromiseHandle valueToRelease = 0;
    lock (handleGate)
    {
      activeLeases--;
      if (activeLeases == 0 && disposeRequested)
      {
        valueToRelease = handle;
        handle = 0;
      }
    }
    if (valueToRelease != 0)
    {
      unsafe
      {
        context.Api->ReleasePromiseHandle(context.RuntimeHandle, valueToRelease);
      }
    }
  }

  private sealed class HandleLease : IDisposable
  {
    private JavaScriptPromise? owner;

    public HandleLease(JavaScriptPromise owner, ExpoJsiPromiseHandle handle)
    {
      this.owner = owner;
      Handle = handle;
    }

    public ExpoJsiPromiseHandle Handle { get; }

    public void Dispose()
    {
      var released = Interlocked.Exchange(ref owner, null);
      released?.ReleaseLease();
    }
  }
}
