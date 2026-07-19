using Expo.JSI.Interop;
using Expo.JSI.Internal;

namespace Expo.JSI;

/// <summary>Owns a runtime-affine JavaScript ArrayBuffer handle.</summary>
/// <remarks>
/// The wrapper snapshots the logical byte length at retention time. Byte pointers are borrowed
/// only for the synchronous callback passed to a <c>WithBytes</c> method. Every access requires
/// the current logical byte length to equal the retained snapshot, so a growth or shrink observed
/// during <c>WithBytes</c> or <c>WithReadOnlyBytes</c> access fails. Each instance owns one lease.
/// <see cref="Dispose" />
/// atomically releases that lease and is idempotent, but does not synchronize disposal with active
/// use; callers that need independent concurrent ownership must call <see cref="Retain" />.
/// This type deliberately has neither a finalizer nor SafeHandle backing. Forgetting
/// <see cref="Dispose" /> leaks its raw native handle; runtime teardown can only release or abandon
/// the tracked JSI storage.
/// </remarks>
public sealed class JavaScriptArrayBuffer : IDisposable
{
  private readonly JsiContext context;
  private readonly int byteLength;
  private ExpoJsiArrayBufferHandle handle;

  internal JavaScriptArrayBuffer(
      JsiContext context,
      ExpoJsiArrayBufferHandle handle,
      int byteLength
  )
  {
    this.context = context;
    this.handle = handle;
    this.byteLength = byteLength;
  }

  /// <summary>Gets the captured logical byte length without entering JSI.</summary>
  public int ByteLength
  {
    get
    {
      ThrowIfDisposed();
      return byteLength;
    }
  }

  /// <summary>Creates another owned lease for the same retained JSI storage.</summary>
  public unsafe JavaScriptArrayBuffer Retain()
  {
    ThrowIfDisposed();
    var result = context.Api->CloneArrayBufferHandle(handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to retain JavaScript ArrayBuffer.");
    }
    return new JavaScriptArrayBuffer(context, result.ArrayBuffer, result.ByteLength);
  }

  public void WithBytes(JavaScriptBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public unsafe TResult WithBytes<TResult>(JavaScriptBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    JavaScriptHandleScope.CurrentFor(context);
    var result = context.Api->GetArrayBufferBytes(context.RuntimeHandle, handle);
    ValidateSpanShape(result);
    var span = ReadMutableSpan(result, "Failed to access JavaScript ArrayBuffer bytes.");
    return action(span);
  }

  public void WithReadOnlyBytes(JavaScriptReadOnlyBytesAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    WithReadOnlyBytes(bytes =>
    {
      action(bytes);
      return 0;
    });
  }

  public unsafe TResult WithReadOnlyBytes<TResult>(JavaScriptReadOnlyBytesFunc<TResult> action)
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    JavaScriptHandleScope.CurrentFor(context);
    var result = context.Api->GetArrayBufferBytes(context.RuntimeHandle, handle);
    ValidateSpanShape(result);
    var span = ReadOnlySpanFrom(result, "Failed to access JavaScript ArrayBuffer bytes.");
    return action(span);
  }

  public Task WithBytesAsync(
      JavaScriptBytesAction action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    return WithBytesAsync<object?>(bytes =>
    {
      action(bytes);
      return null;
    }, cancellationToken);
  }

  public Task<TResult> WithBytesAsync<TResult>(
      JavaScriptBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    JavaScriptArrayBuffer lease;
    unsafe
    {
      lease = Retain();
    }
    return ExecuteWithLeaseAsync(lease, action, cancellationToken);
  }

  public Task WithReadOnlyBytesAsync(
      JavaScriptReadOnlyBytesAction action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    return WithReadOnlyBytesAsync<object?>(bytes =>
    {
      action(bytes);
      return null;
    }, cancellationToken);
  }

  public Task<TResult> WithReadOnlyBytesAsync<TResult>(
      JavaScriptReadOnlyBytesFunc<TResult> action,
      CancellationToken cancellationToken = default
  )
  {
    ArgumentNullException.ThrowIfNull(action);
    ThrowIfDisposed();
    JavaScriptArrayBuffer lease;
    unsafe
    {
      lease = Retain();
    }
    return ExecuteWithReadOnlyLeaseAsync(lease, action, cancellationToken);
  }

  /// <summary>Creates an owned JavaScript value for this buffer.</summary>
  /// <remarks>
  /// The supplied runtime must be this buffer's originating runtime and have active runtime access.
  /// </remarks>
  public unsafe JavaScriptValue AsValue(JavaScriptRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    ThrowIfDisposed();
    if (!context.IsSameRuntime(runtime.Context))
    {
      throw new InvalidOperationException("ArrayBuffer belongs to a different JavaScript runtime.");
    }
    JavaScriptHandleScope.CurrentFor(context);
    var result = context.Api->ConvertArrayBufferToValue(context.RuntimeHandle, handle);
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, "Failed to convert JavaScript ArrayBuffer.");
    }
    return JavaScriptValue.FromOwnedHandle(context, result.Value);
  }

  public unsafe void Dispose()
  {
    var value = Interlocked.Exchange(ref handle, IntPtr.Zero);
    if (value != 0)
    {
      context.Api->ReleaseArrayBuffer(value);
    }
  }

  private static async Task<TResult> ExecuteWithLeaseAsync<TResult>(
      JavaScriptArrayBuffer lease,
      JavaScriptBytesFunc<TResult> action,
      CancellationToken cancellationToken
  )
  {
    try
    {
      return await new JavaScriptRuntime(lease.context).ExecuteAsync(
          _ => InvokeWithBytes(lease, action),
          JavaScriptTaskPriority.Immediate,
          cancellationToken
      ).ConfigureAwait(false);
    }
    finally
    {
      lease.Dispose();
    }
  }

  private static async Task<TResult> ExecuteWithReadOnlyLeaseAsync<TResult>(
      JavaScriptArrayBuffer lease,
      JavaScriptReadOnlyBytesFunc<TResult> action,
      CancellationToken cancellationToken
  )
  {
    try
    {
      return await new JavaScriptRuntime(lease.context).ExecuteAsync(
          _ => InvokeWithReadOnlyBytes(lease, action),
          JavaScriptTaskPriority.Immediate,
          cancellationToken
      ).ConfigureAwait(false);
    }
    finally
    {
      lease.Dispose();
    }
  }

  private static unsafe Span<byte> ReadMutableSpan(ExpoJsiByteSpanResult result, string fallback)
  {
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, fallback);
    }
    if (result.Data is null && result.Length != 0)
    {
      throw new InvalidOperationException("Native JSI returned a null byte pointer for non-empty storage.");
    }
    return new Span<byte>(result.Data, result.Length);
  }

  private static unsafe ReadOnlySpan<byte> ReadOnlySpanFrom(ExpoJsiByteSpanResult result, string fallback)
  {
    if (!result.IsOk)
    {
      JsiContext.ThrowNativeError(result.Error, fallback);
    }
    if (result.Data is null && result.Length != 0)
    {
      throw new InvalidOperationException("Native JSI returned a null byte pointer for non-empty storage.");
    }
    return new ReadOnlySpan<byte>(result.Data, result.Length);
  }

  private void ValidateSpanShape(ExpoJsiByteSpanResult result)
  {
    if (result.IsOk && (result.Length < 0 || result.Length != byteLength))
    {
      throw new InvalidOperationException("Native JSI returned an invalid ArrayBuffer byte length.");
    }
  }

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(handle == 0, this);

  private static TResult InvokeWithBytes<TResult>(
      JavaScriptArrayBuffer buffer,
      JavaScriptBytesFunc<TResult> action
  )
  {
    unsafe
    {
      return buffer.WithBytes(action);
    }
  }

  private static TResult InvokeWithReadOnlyBytes<TResult>(
      JavaScriptArrayBuffer buffer,
      JavaScriptReadOnlyBytesFunc<TResult> action
  )
  {
    unsafe
    {
      return buffer.WithReadOnlyBytes(action);
    }
  }
}
